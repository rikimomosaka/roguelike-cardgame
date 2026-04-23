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
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Merchant マス到達時に生成された在庫カードの base id が
/// RunState.SeenCardBaseIds へ記録され、最終的に RunResultDto.SeenCardBaseIds に
/// 反映されるかを検証する統合テスト。
/// </summary>
public class MerchantControllerBestiaryTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public MerchantControllerBestiaryTests(TempDataFactory f) => _factory = f;

    // ─── Map helpers (same logic as MerchantControllerTests) ───────────────

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
        var empty = ImmutableArray.CreateRange(Enumerable.Repeat("", s.PotionSlotCount));
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

    // ─── Test ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MerchantEnter_InventoryCards_AddedToSeenCards()
    {
        const string AccountId = "phase08-merchant-bestiary";
        var client = await WalkToMerchantAsync(AccountId);

        // 到着した商人の在庫カード id を取得。
        var invRes = await client.GetAsync("/api/v1/merchant/inventory");
        Assert.Equal(HttpStatusCode.OK, invRes.StatusCode);
        using var invDoc = JsonDocument.Parse(await invRes.Content.ReadAsStringAsync());
        var inventoryCardIds = new List<string>();
        foreach (var c in invDoc.RootElement.GetProperty("cards").EnumerateArray())
            inventoryCardIds.Add(c.GetProperty("id").GetString()!);
        Assert.Equal(5, inventoryCardIds.Count);

        // Abandon して RunResultDto を取得し、SeenCardBaseIds にすべての在庫カード id が含まれることを確認。
        var abandon = await client.PostAsJsonAsync(
            "/api/v1/runs/current/abandon", new HeartbeatRequestDto(ElapsedSeconds: 0));
        abandon.EnsureSuccessStatusCode();

        var result = await abandon.Content.ReadFromJsonAsync<RunResultDto>();
        Assert.NotNull(result);
        foreach (var id in inventoryCardIds)
            Assert.Contains(id, result!.SeenCardBaseIds);
    }
}
