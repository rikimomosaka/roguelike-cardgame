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
        Assert.True(doc.RootElement.TryGetProperty("run", out _));
        Assert.True(doc.RootElement.TryGetProperty("map", out _));
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
        Assert.Equal(HttpStatusCode.NoContent, abandon.StatusCode);
        var cur = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.NoContent, cur.StatusCode);
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
    public async Task NoHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
