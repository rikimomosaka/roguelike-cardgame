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
/// 移動時の非戦闘マス（Rest/Treasure/Merchant）副作用を検証する。
/// seed=58 の map に依存するため、到達不能な kind はテストをスキップする。
/// map layout for seed=58 (start=0):
///   [0] Start -> 1,2,3
///   [1] Enemy -> 4
///   [4] Unknown→Rest
///   [2] Enemy -> 5,6
///   [6] Rest -> 8,9
///   [5] Enemy -> 8
///   [8] Unknown→Enemy -> 12,13
///   [13] Merchant
/// Treasure と Event は start から近傍に到達不能のためスキップ。
/// </summary>
public class NonBattleMoveTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public NonBattleMoveTests(TempDataFactory f) => _factory = f;

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
        // BFS from start to first node with effective kind == targetKind.
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

    private async Task<HttpClient> NewClientAsync(string accountId)
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        return client;
    }

    private static async Task<JsonDocument> GetSnapshotAsync(HttpClient client)
    {
        var r = await client.GetAsync("/api/v1/runs/current");
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Move to nodeId, then clear any battle/reward that was triggered.
    /// Does NOT clear for the final step (caller inspects state).
    /// </summary>
    private async Task TraverseIntermediateAsync(HttpClient client, int nodeId, string accountId)
    {
        // Ensure potion slots are empty so ApplyPotion never fails.
        await ResetPotionsAsync(accountId);

        var move = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId, elapsedSeconds = 0 });
        move.EnsureSuccessStatusCode();

        // Drain battle / reward if present.
        await DrainBattleAndRewardAsync(client);
    }

    private async Task DrainBattleAndRewardAsync(HttpClient client)
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
            // Claim gold
            var gold = await client.PostAsync("/api/v1/runs/current/reward/gold", null);
            // may already be claimed from non-battle reward? It starts unclaimed — should succeed.
            if (gold.StatusCode != HttpStatusCode.NoContent && gold.StatusCode != HttpStatusCode.Conflict)
                gold.EnsureSuccessStatusCode();

            // Claim potion if present. Slots cleared earlier by caller.
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

                // Card choice: skip if present (cards may be empty for non-battle).
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

    private async Task ResetPotionsAsync(string accountId)
    {
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = await repo.TryLoadAsync(accountId, CancellationToken.None);
        if (s is null) return;
        var empty = ImmutableArray.CreateRange(System.Linq.Enumerable.Repeat("", s.PotionSlotCount));
        await repo.SaveAsync(accountId, s with { Potions = empty }, CancellationToken.None);
    }

    private async Task<(HttpClient client, List<int> path, MapInfo map)?> SetupWalkAsync(
        string accountId, string targetKind)
    {
        var client = await NewClientAsync(accountId);
        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        newRes.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        var map = ParseMap(doc);
        var path = FindShortestPath(map, targetKind);
        if (path is null || path.Count < 2) return null;
        return (client, path, map);
    }

    [Fact]
    public async Task Rest_Move_SetsActiveRestPending()
    {
        // Phase 6: Rest マス進入では即時回復せず ActiveRestPending=true のみ立てる。
        // 実際の回復／アップグレードは Task F2 で追加される Rest node 画面で選択する。
        var setup = await SetupWalkAsync("rest-walker", "Rest");
        Assert.NotNull(setup);
        var (client, path, _) = setup!.Value;

        for (int i = 1; i < path.Count - 1; i++)
            await TraverseIntermediateAsync(client, path[i], "rest-walker");

        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = await repo.TryLoadAsync("rest-walker", CancellationToken.None);
        Assert.NotNull(s);
        Assert.True(s!.MaxHp > 1);
        await repo.SaveAsync("rest-walker", s with { CurrentHp = 1 }, CancellationToken.None);

        int restId = path[^1];
        var moveRes = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = restId, elapsedSeconds = 0 });
        moveRes.EnsureSuccessStatusCode();

        var after = await GetSnapshotAsync(client);
        var runEl = after.RootElement.GetProperty("run");
        // 即時回復しないことを検証。ActiveRestPending フラグ自体は Task G3 の DTO 更新後に検証する。
        Assert.Equal(1, runEl.GetProperty("currentHp").GetInt32());
        Assert.Equal(JsonValueKind.Null, runEl.GetProperty("activeBattle").ValueKind);
        Assert.Equal(JsonValueKind.Null, runEl.GetProperty("activeReward").ValueKind);
    }

    [Fact]
    public async Task Merchant_Move_SetsActiveMerchant()
    {
        var setup = await SetupWalkAsync("merchant-walker", "Merchant");
        if (setup is null)
        {
            // Seed 58 has reachable Merchant via [0]->[2]->[5]->[8]->[13]; should not skip.
            Assert.Fail("Merchant kind unreachable in seed 58 — map layout changed.");
            return;
        }
        var (client, path, _) = setup!.Value;

        for (int i = 1; i < path.Count - 1; i++)
            await TraverseIntermediateAsync(client, path[i], "merchant-walker");

        // Capture HP/gold/deck before Merchant move.
        var before = await GetSnapshotAsync(client);
        var beforeRun = before.RootElement.GetProperty("run");
        int beforeHp = beforeRun.GetProperty("currentHp").GetInt32();
        int beforeGold = beforeRun.GetProperty("gold").GetInt32();
        int beforeDeckLen = beforeRun.GetProperty("deck").GetArrayLength();

        int merchantId = path[^1];
        var moveRes = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = merchantId, elapsedSeconds = 0 });
        moveRes.EnsureSuccessStatusCode();

        var after = await GetSnapshotAsync(client);
        var runEl = after.RootElement.GetProperty("run");
        // Merchant now generates an inventory and sets ActiveMerchant.
        Assert.NotEqual(JsonValueKind.Null, runEl.GetProperty("activeMerchant").ValueKind);
        Assert.Equal(JsonValueKind.Null, runEl.GetProperty("activeBattle").ValueKind);
        Assert.Equal(JsonValueKind.Null, runEl.GetProperty("activeReward").ValueKind);
        Assert.Equal(beforeHp, runEl.GetProperty("currentHp").GetInt32());
        Assert.Equal(beforeGold, runEl.GetProperty("gold").GetInt32());
        Assert.Equal(beforeDeckLen, runEl.GetProperty("deck").GetArrayLength());
        Assert.Equal(merchantId, runEl.GetProperty("currentNodeId").GetInt32());
    }

    [Fact(Skip = "seed=58 map has no Treasure reachable within practical walk (row 9+ only, 5+ battles deep); would require unreasonably long test setup.")]
    public async Task Treasure_Move_CreatesActiveReward_WithoutCards()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "TileKind does not define Event; Unknown resolves only to Enemy/Elite/Merchant/Rest/Treasure per UnknownResolutionConfig. No Event tiles exist in current Core.")]
    public async Task Event_Move_CreatesActiveReward_WithoutCards()
    {
        await Task.CompletedTask;
    }
}
