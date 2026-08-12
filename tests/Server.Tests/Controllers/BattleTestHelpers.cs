using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Tests.Controllers;

internal static class BattleTestHelpers
{
    public static HttpClient WithAccount(HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        client.DefaultRequestHeaders.Add("X-Account-Id", id);
        return client;
    }

    public static async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
            res.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Test-only setup for scenarios that are unrelated to the act-start UI flow.
    /// Production clients must complete /act-start/enter and /act-start/choose.
    /// </summary>
    public static async Task MarkActStartResolvedForTestAsync(
        IServiceProvider services,
        string accountId)
    {
        var saves = services.GetRequiredService<ISaveRepository>();
        var state = await saves.TryLoadAsync(accountId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Run not found for {accountId}");
        if (state.VisitedNodeIds.IndexOf(state.CurrentNodeId) >= 0) return;

        await saves.SaveAsync(
            accountId,
            state with
            {
                VisitedNodeIds = state.VisitedNodeIds.Add(state.CurrentNodeId),
            },
            CancellationToken.None);
    }

    /// <summary>
    /// Starts a new run, fetches /current, finds a start-outgoing node whose effective kind is Enemy
    /// (checking unknownResolutions when tile is Unknown), moves there. Caller must have already
    /// set X-Account-Id header.
    /// </summary>
    public static async Task StartRunAndMoveToEnemyAsync(
        HttpClient client,
        IServiceProvider services,
        string accountId)
    {
        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        newRes.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        await MarkActStartResolvedForTestAsync(services, accountId);
        int startId = doc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32();

        // Build a map from node id -> kind, plus unknownResolutions overlay.
        var nodes = doc.RootElement.GetProperty("map").GetProperty("nodes");
        var resolutions = doc.RootElement.GetProperty("run").GetProperty("unknownResolutions");

        JsonElement startNode = default;
        foreach (var n in nodes.EnumerateArray())
            if (n.GetProperty("id").GetInt32() == startId) { startNode = n; break; }

        int targetId = -1;
        foreach (var outId in startNode.GetProperty("outgoingNodeIds").EnumerateArray())
        {
            int id = outId.GetInt32();
            string kind = "";
            foreach (var n in nodes.EnumerateArray())
                if (n.GetProperty("id").GetInt32() == id)
                { kind = n.GetProperty("kind").GetString()!; break; }
            if (kind == "Unknown" && resolutions.TryGetProperty(id.ToString(), out var resolved))
                kind = resolved.GetString()!;
            if (kind == "Enemy") { targetId = id; break; }
        }
        if (targetId < 0) throw new System.InvalidOperationException(
            "No Enemy-kind adjacent to start; seed 58 map assumed to have at least one.");

        var moveRes = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = targetId, elapsedSeconds = 1 });
        moveRes.EnsureSuccessStatusCode();
    }
}
