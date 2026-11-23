using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task GetCurrent_NoSave_Returns204()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "alice");
        WithAccount(client, "alice");
        var res = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task PostNew_CreatesRunReturnsSnapshot()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "bob");
        WithAccount(client, "bob");
        var res = await client.PostAsync("/api/v1/runs/new", content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("run", out var run));
        Assert.True(doc.RootElement.TryGetProperty("map", out _));
        Assert.Equal("default", run.GetProperty("characterId").GetString());
        Assert.Equal("player:0", run.GetProperty("hostMemberId").GetString());
        var member = Assert.Single(run.GetProperty("partyMembers").EnumerateArray());
        Assert.Equal("default", member.GetProperty("characterId").GetString());
    }

    [Fact]
    public async Task PostNew_EmptyObject_UsesDefaultCharacter()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "pick-empty-object");
        WithAccount(client, "pick-empty-object");

        var res = await client.PostAsJsonAsync("/api/v1/runs/new", new { });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("default", doc.RootElement.GetProperty("run").GetProperty("characterId").GetString());
    }

    [Fact]
    public async Task PostNew_ExplicitSingleCharacter_UsesSelection()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "pick-single");
        WithAccount(client, "pick-single");

        var res = await client.PostAsJsonAsync("/api/v1/runs/new", new
        {
            characterIds = new[] { "default" },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var run = doc.RootElement.GetProperty("run");
        Assert.Equal("default", run.GetProperty("characterId").GetString());
        Assert.Equal("default", Assert.Single(run.GetProperty("partyMembers").EnumerateArray())
            .GetProperty("characterId").GetString());
    }

    [Fact]
    public async Task PostNew_InvalidCharacterSelections_Return400WithoutCreatingRun()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "pick-invalid");
        WithAccount(client, "pick-invalid");
        object[] invalidBodies =
        {
            new { characterIds = Array.Empty<string>() },
            new { characterIds = new[] { " " } },
            new { characterIds = new[] { "missing-character" } },
            new { characterIds = new[] { "default", "default" } },
            new { characterIds = new[] { "a", "b", "c", "d", "e" } },
        };

        foreach (var body in invalidBodies)
        {
            var res = await client.PostAsJsonAsync("/api/v1/runs/new", body);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        var current = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
    }

    [Fact]
    public async Task PostNew_TwoKnownCharacters_Returns409UntilBattleSupportsPartyPlay()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "pick-multiple");
        WithAccount(client, "pick-multiple");
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var characters = Assert.IsType<Dictionary<string, CharacterDefinition>>(catalog.Characters);
        var alternate = characters["default"] with { Id = "alternate" };
        characters.Add(alternate.Id, alternate);
        try
        {
            var res = await client.PostAsJsonAsync("/api/v1/runs/new", new
            {
                characterIds = new[] { "default", "alternate" },
            });

            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            var current = await client.GetAsync("/api/v1/runs/current");
            Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
        }
        finally
        {
            characters.Remove(alternate.Id);
        }
    }

    [Fact]
    public async Task PostNew_InvalidForcedSelection_PreservesExistingRun()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "pick-force-invalid");
        WithAccount(client, "pick-force-invalid");
        var created = await client.PostAsync("/api/v1/runs/new", content: null);
        created.EnsureSuccessStatusCode();
        var saves = _factory.Services.GetRequiredService<ISaveRepository>();
        var before = await saves.TryLoadAsync("pick-force-invalid", CancellationToken.None);
        Assert.NotNull(before);

        var invalid = await client.PostAsJsonAsync("/api/v1/runs/new?force=true", new
        {
            characterIds = new[] { "missing-character" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var after = await saves.TryLoadAsync("pick-force-invalid", CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal(before!.RunId, after!.RunId);
    }

    [Fact]
    public async Task PostNew_ExistingInProgress_Returns409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "carol");
        WithAccount(client, "carol");
        await client.PostAsync("/api/v1/runs/new", content: null);
        var res = await client.PostAsync("/api/v1/runs/new", content: null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task PostNew_ForceTrue_Overwrites()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "dan");
        WithAccount(client, "dan");
        await client.PostAsync("/api/v1/runs/new", content: null);
        var res = await client.PostAsync("/api/v1/runs/new?force=true", content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task PostMove_AdjacentNode_Returns204AndAdvances()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "eve");
        WithAccount(client, "eve");
        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        await BattleTestHelpers.MarkActStartResolvedForTestAsync(
            _factory.Services, "eve");
        var doc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        int startId = doc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32();
        int targetId = -1;
        foreach (var n in doc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
        {
            if (n.GetProperty("id").GetInt32() == startId)
            {
                targetId = n.GetProperty("outgoingNodeIds")[0].GetInt32();
                break;
            }
        }
        Assert.True(targetId >= 0);
        var moveRes = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = targetId, elapsedSeconds = 5 });
        Assert.Equal(HttpStatusCode.NoContent, moveRes.StatusCode);

        var curRes = await client.GetAsync("/api/v1/runs/current");
        var curDoc = JsonDocument.Parse(await curRes.Content.ReadAsStringAsync());
        Assert.Equal(targetId, curDoc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32());
        Assert.True(curDoc.RootElement.GetProperty("run").GetProperty("playSeconds").GetInt64() >= 5);
    }

    [Fact]
    public async Task PostMove_NonAdjacent_Returns400()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "fay");
        WithAccount(client, "fay");
        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        await BattleTestHelpers.MarkActStartResolvedForTestAsync(
            _factory.Services, "fay");
        var doc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        int startId = doc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32();
        int bad = -1;
        foreach (var n in doc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
        {
            int id = n.GetProperty("id").GetInt32();
            if (id == startId) continue;
            bool isAdj = false;
            foreach (var adj in doc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
            {
                if (adj.GetProperty("id").GetInt32() == startId)
                {
                    foreach (var out_ in adj.GetProperty("outgoingNodeIds").EnumerateArray())
                        if (out_.GetInt32() == id) isAdj = true;
                }
            }
            if (!isAdj) { bad = id; break; }
        }
        Assert.True(bad >= 0);
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = bad, elapsedSeconds = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PostAbandon_TransitionsAndHidesFromCurrent()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "gus");
        WithAccount(client, "gus");
        await client.PostAsync("/api/v1/runs/new", content: null);
        var abandon = await client.PostAsJsonAsync("/api/v1/runs/current/abandon", new { elapsedSeconds = 3 });
        Assert.Equal(HttpStatusCode.OK, abandon.StatusCode);
        var cur = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.NoContent, cur.StatusCode);
    }

    [Fact]
    public async Task PostAbandon_ReturnsNotFound_WhenAccountMissing()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        WithAccount(client, "ghost");
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/abandon", new { elapsedSeconds = 0 });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task PostHeartbeat_ReturnsNotFound_WhenAccountMissing()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        WithAccount(client, "ghost");
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/heartbeat", new { elapsedSeconds = 0 });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task PostHeartbeat_AddsPlaySeconds()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "hal");
        WithAccount(client, "hal");
        await client.PostAsync("/api/v1/runs/new", content: null);
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/heartbeat", new { elapsedSeconds = 7 });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var cur = await client.GetAsync("/api/v1/runs/current");
        var doc = JsonDocument.Parse(await cur.Content.ReadAsStringAsync());
        Assert.Equal(7, doc.RootElement.GetProperty("run").GetProperty("playSeconds").GetInt64());
    }

    [Fact]
    public async Task FullFlow_NewMoveHeartbeatCurrent_AccumulatesPlaySeconds()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "ivy");
        WithAccount(client, "ivy");

        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        await BattleTestHelpers.MarkActStartResolvedForTestAsync(
            _factory.Services, "ivy");
        var newDoc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        int startId = newDoc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32();
        int firstTarget = -1;
        foreach (var n in newDoc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
            if (n.GetProperty("id").GetInt32() == startId)
                firstTarget = n.GetProperty("outgoingNodeIds")[0].GetInt32();

        await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = firstTarget, elapsedSeconds = 4 });
        await client.PostAsJsonAsync("/api/v1/runs/current/heartbeat", new { elapsedSeconds = 3 });

        var cur = await client.GetAsync("/api/v1/runs/current");
        var doc = JsonDocument.Parse(await cur.Content.ReadAsStringAsync());
        Assert.Equal(firstTarget, doc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32());
        Assert.Equal(7, doc.RootElement.GetProperty("run").GetProperty("playSeconds").GetInt64());
    }

    [Fact]
    public async Task NoHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
