using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class RewardEndpointsTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public RewardEndpointsTests(TempDataFactory f) => _factory = f;

    // Shared setup helper: creates account, starts run, moves to Enemy, wins battle => ActiveReward ready.
    private async Task<System.Net.Http.HttpClient> ClientWithActiveRewardAsync(string accountId)
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(client);
        var win = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });
        win.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<JsonDocument> GetSnapshotAsync(System.Net.Http.HttpClient client)
    {
        var r = await client.GetAsync("/api/v1/runs/current");
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RewardGold_Claims()
    {
        var client = await ClientWithActiveRewardAsync("alice");
        var before = await GetSnapshotAsync(client);
        int beforeGold = before.RootElement.GetProperty("run").GetProperty("gold").GetInt32();
        int rewardGold = before.RootElement.GetProperty("run").GetProperty("activeReward").GetProperty("gold").GetInt32();

        var res = await client.PostAsync("/api/v1/runs/current/reward/gold", null);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var after = await GetSnapshotAsync(client);
        Assert.Equal(beforeGold + rewardGold, after.RootElement.GetProperty("run").GetProperty("gold").GetInt32());
        Assert.True(after.RootElement.GetProperty("run").GetProperty("activeReward").GetProperty("goldClaimed").GetBoolean());
    }

    [Fact]
    public async Task RewardGold_Twice_Returns409()
    {
        var client = await ClientWithActiveRewardAsync("bob");
        (await client.PostAsync("/api/v1/runs/current/reward/gold", null)).EnsureSuccessStatusCode();
        var res = await client.PostAsync("/api/v1/runs/current/reward/gold", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task RewardPotion_WithEmptySlot_Claims_OrSkipsIfNoPotion()
    {
        var client = await ClientWithActiveRewardAsync("carol");
        var snap = await GetSnapshotAsync(client);
        var potionIdEl = snap.RootElement.GetProperty("run").GetProperty("activeReward").GetProperty("potionId");
        if (potionIdEl.ValueKind == JsonValueKind.Null)
        {
            // No potion drop — endpoint should 409 (no potion to claim).
            var res = await client.PostAsync("/api/v1/runs/current/reward/potion", null);
            Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
            return;
        }
        var ok = await client.PostAsync("/api/v1/runs/current/reward/potion", null);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        var after = await GetSnapshotAsync(client);
        Assert.True(after.RootElement.GetProperty("run").GetProperty("activeReward").GetProperty("potionClaimed").GetBoolean());
    }

    [Fact]
    public async Task RewardCard_Skip_MarksSkipped()
    {
        var client = await ClientWithActiveRewardAsync("dave");
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/reward/card", new { skip = true });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var after = await GetSnapshotAsync(client);
        Assert.Equal("Skipped", after.RootElement.GetProperty("run").GetProperty("activeReward").GetProperty("cardStatus").GetString());
    }

    [Fact]
    public async Task RewardCard_Pick_AddsToDeck_AndBlocksRepeat()
    {
        var client = await ClientWithActiveRewardAsync("eve");
        var snap = await GetSnapshotAsync(client);
        string choice = snap.RootElement.GetProperty("run").GetProperty("activeReward")
            .GetProperty("cardChoices")[0].GetString()!;
        var deckBefore = new System.Collections.Generic.List<string>();
        foreach (var c in snap.RootElement.GetProperty("run").GetProperty("deck").EnumerateArray()) deckBefore.Add(c.GetString()!);

        var pick = await client.PostAsJsonAsync("/api/v1/runs/current/reward/card", new { cardId = choice });
        Assert.Equal(HttpStatusCode.NoContent, pick.StatusCode);

        var after = await GetSnapshotAsync(client);
        var deckAfter = new System.Collections.Generic.List<string>();
        foreach (var c in after.RootElement.GetProperty("run").GetProperty("deck").EnumerateArray()) deckAfter.Add(c.GetString()!);
        Assert.Equal(deckBefore.Count + 1, deckAfter.Count);
        Assert.Contains(choice, deckAfter);

        // Repeat blocked (card already resolved).
        var second = await client.PostAsJsonAsync("/api/v1/runs/current/reward/card", new { cardId = choice });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task RewardCard_BothCardIdAndSkip_Returns400()
    {
        var client = await ClientWithActiveRewardAsync("frank");
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/reward/card",
            new { cardId = "reward_common_01", skip = true });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task RewardCard_NeitherCardIdNorSkip_Returns400()
    {
        var client = await ClientWithActiveRewardAsync("grace");
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/reward/card", new { });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task RewardCard_UnknownCardId_Returns400()
    {
        var client = await ClientWithActiveRewardAsync("henry");
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/reward/card",
            new { cardId = "reward_common_99_nonexistent" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task RewardProceed_IncompleteRewards_Returns409()
    {
        var client = await ClientWithActiveRewardAsync("ivan");
        // Nothing claimed yet -> Proceed should 409.
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed", new { elapsedSeconds = 0 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task RewardProceed_AllDone_ClearsReward_AndAllowsNextMove()
    {
        var client = await ClientWithActiveRewardAsync("jane");
        // Claim gold
        (await client.PostAsync("/api/v1/runs/current/reward/gold", null)).EnsureSuccessStatusCode();
        // Claim potion if present
        var snap = await GetSnapshotAsync(client);
        var potionEl = snap.RootElement.GetProperty("run").GetProperty("activeReward").GetProperty("potionId");
        if (potionEl.ValueKind != JsonValueKind.Null)
        {
            (await client.PostAsync("/api/v1/runs/current/reward/potion", null)).EnsureSuccessStatusCode();
        }
        // Skip card
        (await client.PostAsJsonAsync("/api/v1/runs/current/reward/card", new { skip = true })).EnsureSuccessStatusCode();

        var proceed = await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed", new { elapsedSeconds = 1 });
        Assert.Equal(HttpStatusCode.NoContent, proceed.StatusCode);

        var after = await GetSnapshotAsync(client);
        Assert.Equal(JsonValueKind.Null, after.RootElement.GetProperty("run").GetProperty("activeReward").ValueKind);
    }
}
