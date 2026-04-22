using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
/// Merchant HTTP エンドポイント（inventory/buy/discard/leave）の統合テスト。
/// seed=58 マップ: [0]Start -> [2]Enemy -> [5]Enemy -> [8]Unknown->Enemy -> [13]Merchant
/// </summary>
public class MerchantControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public MerchantControllerTests(TempDataFactory f) => _factory = f;

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
    /// Walk to Merchant tile and return the client positioned there.
    /// </summary>
    private async Task<HttpClient> WalkToMerchantAsync(string accountId)
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);

        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        newRes.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        var map = ParseMap(doc);
        var path = FindShortestPath(map, "Merchant");
        Assert.NotNull(path);

        for (int i = 1; i < path!.Count - 1; i++)
            await TraverseIntermediateAsync(client, path[i], accountId);

        (await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = path[^1], elapsedSeconds = 0 })).EnsureSuccessStatusCode();

        return client;
    }

    // ─── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInventory_NoActiveMerchant_Returns409()
    {
        const string AccountId = "e5-no-merchant";
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, AccountId);
        BattleTestHelpers.WithAccount(client, AccountId);

        (await client.PostAsync("/api/v1/runs/new", content: null)).EnsureSuccessStatusCode();

        var res = await client.GetAsync("/api/v1/merchant/inventory");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task GetInventory_AfterMoveToMerchantTile_ReturnsInventory()
    {
        const string AccountId = "e5-get-inv";
        var client = await WalkToMerchantAsync(AccountId);

        var res = await client.GetAsync("/api/v1/merchant/inventory");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(5, root.GetProperty("cards").GetArrayLength());
        Assert.Equal(2, root.GetProperty("relics").GetArrayLength());
        Assert.Equal(3, root.GetProperty("potions").GetArrayLength());
    }

    [Fact]
    public async Task Buy_InsufficientGold_Returns400()
    {
        const string AccountId = "e5-buy-nogold";
        var client = await WalkToMerchantAsync(AccountId);

        // Set gold to 0 while preserving ActiveMerchant.
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = await repo.TryLoadAsync(AccountId, CancellationToken.None);
        Assert.NotNull(s);
        await repo.SaveAsync(AccountId, s! with { Gold = 0 }, CancellationToken.None);

        // Get the first card id from inventory.
        var invRes = await client.GetAsync("/api/v1/merchant/inventory");
        var invDoc = JsonDocument.Parse(await invRes.Content.ReadAsStringAsync());
        var firstCardId = invDoc.RootElement.GetProperty("cards")[0].GetProperty("id").GetString()!;

        var res = await client.PostAsJsonAsync("/api/v1/merchant/buy",
            new { kind = "card", id = firstCardId });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Buy_UnknownId_Returns404()
    {
        const string AccountId = "e5-buy-unknown";
        var client = await WalkToMerchantAsync(AccountId);

        var res = await client.PostAsJsonAsync("/api/v1/merchant/buy",
            new { kind = "card", id = "no_such_card_xyz" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Buy_Success_DeductsGoldAndReturnsSnapshot()
    {
        const string AccountId = "e5-buy-ok";
        var client = await WalkToMerchantAsync(AccountId);

        // Set gold to 500 so we can definitely afford anything.
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = await repo.TryLoadAsync(AccountId, CancellationToken.None);
        Assert.NotNull(s);
        await repo.SaveAsync(AccountId, s! with { Gold = 500 }, CancellationToken.None);

        // Get the first card from inventory.
        var invRes = await client.GetAsync("/api/v1/merchant/inventory");
        var invDoc = JsonDocument.Parse(await invRes.Content.ReadAsStringAsync());
        var firstCardEl = invDoc.RootElement.GetProperty("cards")[0];
        var firstCardId = firstCardEl.GetProperty("id").GetString()!;

        var res = await client.PostAsJsonAsync("/api/v1/merchant/buy",
            new { kind = "card", id = firstCardId });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var runEl = doc.RootElement.GetProperty("run");
        int gold = runEl.GetProperty("gold").GetInt32();
        Assert.True(gold < 500, $"Expected gold < 500 after buy, got {gold}");

        // Card should appear in deck.
        var deck = runEl.GetProperty("deck").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();
        Assert.Contains(firstCardId, deck);
    }

    [Fact]
    public async Task Discard_Success_ReducesDeckByOne()
    {
        const string AccountId = "e5-discard";
        var client = await WalkToMerchantAsync(AccountId);

        // Ensure we have gold for discard.
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = await repo.TryLoadAsync(AccountId, CancellationToken.None);
        Assert.NotNull(s);
        int deckLenBefore = s!.Deck.Length;
        await repo.SaveAsync(AccountId, s with { Gold = 500 }, CancellationToken.None);

        var res = await client.PostAsJsonAsync("/api/v1/merchant/discard", new { deckIndex = 0 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        int deckLenAfter = doc.RootElement.GetProperty("run").GetProperty("deck").GetArrayLength();
        Assert.Equal(deckLenBefore - 1, deckLenAfter);
    }

    [Fact]
    public async Task Leave_SetsLeftSoFarAndInventoryStillAccessible()
    {
        const string AccountId = "e5-leave";
        var client = await WalkToMerchantAsync(AccountId);

        var leaveRes = await client.PostAsync("/api/v1/merchant/leave", null);
        Assert.Equal(HttpStatusCode.OK, leaveRes.StatusCode);

        // After leave, ActiveMerchant is still set (LeftSoFar=true), so GET inventory returns 200.
        var invRes = await client.GetAsync("/api/v1/merchant/inventory");
        Assert.Equal(HttpStatusCode.OK, invRes.StatusCode);

        // LeftSoFar flag should be true in the response.
        var doc = JsonDocument.Parse(await invRes.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("leftSoFar").GetBoolean());

        // Verify via repo: ActiveMerchant is not null and LeftSoFar = true.
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var after = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        Assert.NotNull(after.ActiveMerchant);
        Assert.True(after.ActiveMerchant!.LeftSoFar);
    }
}
