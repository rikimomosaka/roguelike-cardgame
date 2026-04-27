using System;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Services;
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

        // テストでは固定シードを使い、マップ生成が必ず成功するようにする。
        // seed 58 は act1 config で確実に成功することが確認済み（MapGenerationConfigLoaderTests 参照）。
        builder.ConfigureServices(services =>
        {
            // 既存の RunStartService を上書きして固定シード源を渡す。
            services.AddSingleton(sp =>
            {
                var gen = sp.GetRequiredService<RoguelikeCardGame.Core.Map.IDungeonMapGenerator>();
                var cfg = sp.GetRequiredService<RoguelikeCardGame.Core.Map.MapGenerationConfig>();
                var saves = sp.GetRequiredService<RoguelikeCardGame.Server.Abstractions.ISaveRepository>();
                return new RunStartService(gen, cfg, saves, seedSource: () => 58);
            });
        });
    }

    public void ResetData()
    {
        if (Directory.Exists(_dataRoot)) Directory.Delete(_dataRoot, recursive: true);
        // BattleSessionStore は singleton で test 間共有のため、disk reset と一緒に in-memory も clear する。
        // Dispose 経路では Services が破棄済みのため、host が生きているときだけ実行する。
        if (!_disposed)
        {
            var store = Services.GetRequiredService<BattleSessionStore>();
            store.Clear();
        }
    }

    private bool _disposed;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ResetData();
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
