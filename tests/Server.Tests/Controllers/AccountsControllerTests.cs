using System;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class AccountsControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public AccountsControllerTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_NewId_Returns201WithBody()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = "new-user" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(body);
        Assert.Equal("new-user", body!.Id);
    }

    [Fact]
    public async Task Post_DuplicateId_Returns409()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = "dup" });
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = "dup" });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has/slash")]
    public async Task Post_InvalidId_Returns400(string bad)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = bad });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Get_Existing_Returns200()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = "alice" });

        var res = await client.GetAsync("/api/v1/accounts/alice");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.Equal("alice", body!.Id);
    }

    [Fact]
    public async Task Get_Missing_Returns404()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/accounts/nope");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Theory]
    [InlineData("has%2Fslash")] // URL-encoded '/'
    [InlineData("%20")]          // URL-encoded space
    public async Task Get_InvalidId_Returns400(string badEncoded)
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync($"/api/v1/accounts/{badEncoded}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private sealed record AccountResponse(string Id, DateTimeOffset CreatedAt);
}

/// <summary>テスト間で独立した data ディレクトリを持つ Program 用 factory。</summary>
public sealed class TempDataFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "rcg-integration-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("DataStorage:RootDirectory", _dataRoot),
            });
        });
    }

    public void ResetData()
    {
        if (Directory.Exists(_dataRoot)) Directory.Delete(_dataRoot, recursive: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ResetData();
        base.Dispose(disposing);
    }
}
