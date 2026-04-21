using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class RunsControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public RunsControllerTests(TempDataFactory factory) => _factory = factory;

    private static HttpClient WithAccount(HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        client.DefaultRequestHeaders.Add("X-Account-Id", id);
        return client;
    }

    private async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
            res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_AccountMissing_Returns404()
    {
        var client = WithAccount(_factory.CreateClient(), "ghost");
        var res = await client.GetAsync("/api/v1/runs/latest");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Get_AccountExistsNoRun_Returns204()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "alice");
        WithAccount(client, "alice");

        var res = await client.GetAsync("/api/v1/runs/latest");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Get_AccountWithSavedRun_Returns200WithState()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "bob");

        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = EmbeddedDataLoader.LoadCatalog();
            var save = scope.ServiceProvider.GetRequiredService<ISaveRepository>();
            var run = RunState.NewSoloRun(
                catalog,
                rngSeed: 777UL,
                startNodeId: 0,
                unknownResolutions: System.Collections.Immutable.ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
                nowUtc: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
            await save.SaveAsync("bob", run, CancellationToken.None);
        }

        WithAccount(client, "bob");
        var res = await client.GetAsync("/api/v1/runs/latest");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"rngSeed\":777", body);
    }

    [Fact]
    public async Task Get_NoHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/runs/latest");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
