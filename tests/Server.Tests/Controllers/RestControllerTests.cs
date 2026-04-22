using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Rest HTTP エンドポイント（heal / upgrade）の統合テスト。
/// seed=58 マップ: [0]Start -> [2]Enemy -> [6]Rest
/// </summary>
public class RestControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public RestControllerTests(TempDataFactory f) => _factory = f;

    // ─── Map helpers (same logic as NonBattleMoveTests) ───────────────────

    private sealed record MapInfo(
        int StartId,
        IReadOnlyDictionary<int, string> EffectiveKind,
        IReadOnlyDictionary<int, IReadOnlyList<int>> Outgoing);

    private static MapInfo ParseMap(JsonDocument doc)
    {
        int startId = doc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32();
        var resolutions = doc.RootElement.GetProperty("run").GetProperty("unknownResolutions");
        var kind = new Dictionary<int, string>();
        var outgoing = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var n in doc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
        {
            int id = n.GetProperty("id").GetInt32();
            string k = n.GetProperty("kind").GetString()!;
            if (k == "Unknown" && resolutions.TryGetProperty(id.ToString(), out var r))
                k = r.GetString()!;
            kind[id] = k;
            var outs = new List<int>();
            foreach (var o in n.GetProperty("outgoingNodeIds").EnumerateArray()) outs.Add(o.GetInt32());
            outgoing[id] = outs;
        }
        return new MapInfo(startId, kind, outgoing);
    }

    private static List<int>? FindShortestPath(MapInfo map, string targetKind)
    {
        var queue = new Queue<List<int>>();
        queue.Enqueue(new List<int> { map.StartId });
        var visited = new HashSet<int> { map.StartId };
        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            int tail = path[^1];
            foreach (var next in map.Outgoing[tail])
            {
                if (visited.Contains(next)) continue;
                visited.Add(next);
                var newPath = new List<int>(path) { next };
                if (map.EffectiveKind[next] == targetKind) return newPath;
                queue.Enqueue(newPath);
            }
        }
        return null;
    }

    private static async Task<JsonDocument> GetSnapshotAsync(HttpClient client)
    {
        var r = await client.GetAsync("/api/v1/runs/current");
        r.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync());
    }

    private async Task ResetPotionsAsync(string accountId)
    {
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = await repo.TryLoadAsync(accountId, CancellationToken.None);
        if (s is null) return;
        var empty = ImmutableArray.CreateRange(System.Linq.Enumerable.Repeat("", s.PotionSlotCount));
        await repo.SaveAsync(accountId, s with { Potions = empty }, CancellationToken.None);
    }

    private async Task DrainBattleAndRewardAsync(HttpClient client, string accountId)
    {
        var snap = await GetSnapshotAsync(client);
        var runEl = snap.RootElement.GetProperty("run");

        if (runEl.GetProperty("activeBattle").ValueKind != JsonValueKind.Null)
        {
            (await client.PostAsJsonAsync("/api/v1/runs/current/battle/win",
                new { elapsedSeconds = 0 })).EnsureSuccessStatusCode();
            snap = await GetSnapshotAsync(client);
            runEl = snap.RootElement.GetProperty("run");
        }

        if (runEl.GetProperty("activeReward").ValueKind != JsonValueKind.Null)
        {
            var gold = await client.PostAsync("/api/v1/runs/current/reward/gold", null);
            if (gold.StatusCode != HttpStatusCode.NoContent && gold.StatusCode != HttpStatusCode.Conflict)
                gold.EnsureSuccessStatusCode();

            snap = await GetSnapshotAsync(client);
            var reward = snap.RootElement.GetProperty("run").GetProperty("activeReward");
            if (reward.ValueKind != JsonValueKind.Null)
            {
                var potIdEl = reward.GetProperty("potionId");
                if (potIdEl.ValueKind != JsonValueKind.Null)
                {
                    (await client.PostAsync("/api/v1/runs/current/reward/potion", null))
                        .EnsureSuccessStatusCode();
                }

                snap = await GetSnapshotAsync(client);
                reward = snap.RootElement.GetProperty("run").GetProperty("activeReward");
                if (reward.ValueKind != JsonValueKind.Null)
                {
                    string cardStatus = reward.GetProperty("cardStatus").GetString()!;
                    if (cardStatus == "Pending")
                    {
                        (await client.PostAsJsonAsync("/api/v1/runs/current/reward/card",
                            new { skip = true })).EnsureSuccessStatusCode();
                    }

                    (await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed",
                        new { elapsedSeconds = 0 })).EnsureSuccessStatusCode();
                }
            }
        }
    }

    private async Task TraverseIntermediateAsync(HttpClient client, int nodeId, string accountId)
    {
        await ResetPotionsAsync(accountId);
        var move = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId, elapsedSeconds = 0 });
        move.EnsureSuccessStatusCode();
        await DrainBattleAndRewardAsync(client, accountId);
    }

    /// <summary>
    /// Walk to Rest tile and return the client positioned there.
    /// </summary>
    private async Task<HttpClient> WalkToRestAsync(string accountId)
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);

        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        newRes.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        var map = ParseMap(doc);
        var path = FindShortestPath(map, "Rest");
        Assert.NotNull(path);

        for (int i = 1; i < path!.Count - 1; i++)
            await TraverseIntermediateAsync(client, path[i], accountId);

        (await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = path[^1], elapsedSeconds = 0 })).EnsureSuccessStatusCode();

        return client;
    }

    // ─── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostHeal_RestPending_Returns200AndHeals()
    {
        const string AccountId = "f2-heal-pending";
        var client = await WalkToRestAsync(AccountId);

        // Set known HP values: CurrentHp=30, MaxHp=80 -> heal = ceil(80*0.30) = 24 -> newHp = 54
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        await repo.SaveAsync(AccountId, s with { CurrentHp = 30, MaxHp = 80 }, CancellationToken.None);

        var res = await client.PostAsync("/api/v1/rest/heal", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var runEl = doc.RootElement.GetProperty("run");
        Assert.Equal(54, runEl.GetProperty("currentHp").GetInt32());

        // Verify ActiveRestPending is still true and ActiveRestCompleted is set.
        var after = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        Assert.True(after.ActiveRestPending);
        Assert.True(after.ActiveRestCompleted);
    }

    [Fact]
    public async Task PostHeal_NotPending_Returns409()
    {
        const string AccountId = "f2-heal-not-pending";
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, AccountId);
        BattleTestHelpers.WithAccount(client, AccountId);

        (await client.PostAsync("/api/v1/runs/new", content: null)).EnsureSuccessStatusCode();

        var res = await client.PostAsync("/api/v1/rest/heal", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task PostUpgrade_ValidIndex_Returns200AndUpgrades()
    {
        const string AccountId = "f2-upgrade-ok";
        var client = await WalkToRestAsync(AccountId);

        var res = await client.PostAsJsonAsync("/api/v1/rest/upgrade", new { deckIndex = 0 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // Verify via repo: Deck[0].Upgraded == true, ActiveRestPending still true, ActiveRestCompleted set.
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var after = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        Assert.True(after.Deck[0].Upgraded);
        Assert.True(after.ActiveRestPending);
        Assert.True(after.ActiveRestCompleted);
    }

    [Fact]
    public async Task PostUpgrade_OutOfRange_Returns400()
    {
        const string AccountId = "f2-upgrade-oob";
        var client = await WalkToRestAsync(AccountId);

        var res = await client.PostAsJsonAsync("/api/v1/rest/upgrade", new { deckIndex = 999 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PostUpgrade_AlreadyUpgraded_Returns409()
    {
        const string AccountId = "f2-upgrade-dup";
        var client = await WalkToRestAsync(AccountId);

        // Pre-upgrade deck[0] via repo.
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        await repo.SaveAsync(AccountId,
            s with { Deck = s.Deck.SetItem(0, s.Deck[0] with { Upgraded = true }) },
            CancellationToken.None);

        var res = await client.PostAsJsonAsync("/api/v1/rest/upgrade", new { deckIndex = 0 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task PostUpgrade_NotPending_Returns409()
    {
        const string AccountId = "f2-upgrade-not-pending";
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, AccountId);
        BattleTestHelpers.WithAccount(client, AccountId);

        (await client.PostAsync("/api/v1/runs/new", content: null)).EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync("/api/v1/rest/upgrade", new { deckIndex = 0 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }
}
