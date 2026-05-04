using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Phase 10.6.B T7: POST /api/v1/runs/current/reward/reroll-card-choices の統合テスト。
/// </summary>
public class RunsControllerRerollTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public RunsControllerRerollTests(TempDataFactory f) => _factory = f;

    // Shared setup: account, run, move to enemy, win battle → ActiveReward ready.
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

    // 1. No active reward → 409
    [Fact]
    public async Task RerollCardChoices_NoActiveReward_Returns409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, "reroll-no-reward");
        BattleTestHelpers.WithAccount(client, "reroll-no-reward");
        (await client.PostAsync("/api/v1/runs/new", null)).EnsureSuccessStatusCode();

        var res = await client.PostAsync("/api/v1/runs/current/reward/reroll-card-choices", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    // 2. No reroll capability (no lucky_die relic) → 409
    [Fact]
    public async Task RerollCardChoices_NoCapability_Returns409()
    {
        var client = await ClientWithActiveRewardAsync("reroll-no-capability");
        // ActiveReward is set (no relics → no capability)
        var res = await client.PostAsync("/api/v1/runs/current/reward/reroll-card-choices", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    // 3. Already used → 409
    [Fact]
    public async Task RerollCardChoices_AlreadyUsed_Returns409()
    {
        var client = await ClientWithActiveRewardAsync("reroll-already-used");
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = (await repo.TryLoadAsync("reroll-already-used", CancellationToken.None))!;
        Assert.NotNull(s.ActiveReward);

        // Inject lucky_die relic + mark RerollUsed = true
        var rewardUsed = s.ActiveReward! with { RerollUsed = true };
        var injected = s with
        {
            Relics = new[] { "lucky_die" },
            ActiveReward = rewardUsed,
        };
        await repo.SaveAsync("reroll-already-used", injected, CancellationToken.None);

        var res = await client.PostAsync("/api/v1/runs/current/reward/reroll-card-choices", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    // 4. Not found (no run at all) → handled by no-run-in-progress 409
    [Fact]
    public async Task RerollCardChoices_NoRun_Returns409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, "reroll-no-run");
        BattleTestHelpers.WithAccount(client, "reroll-no-run");
        // No run started

        var res = await client.PostAsync("/api/v1/runs/current/reward/reroll-card-choices", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    // 5. Success: with lucky_die relic + Pending card reward → 200 OK + CardChoices refreshed + RerollUsed = true
    [Fact]
    public async Task RerollCardChoices_WithCapability_Returns200AndUpdatesChoices()
    {
        var client = await ClientWithActiveRewardAsync("reroll-success");
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = (await repo.TryLoadAsync("reroll-success", CancellationToken.None))!;
        Assert.NotNull(s.ActiveReward);

        // Ensure card reward is Pending
        var pendingReward = s.ActiveReward!.CardStatus == CardRewardStatus.Pending
            ? s.ActiveReward
            : s.ActiveReward with { CardStatus = CardRewardStatus.Pending };
        var injected = s with
        {
            Relics = new[] { "lucky_die" },
            ActiveReward = pendingReward,
        };
        await repo.SaveAsync("reroll-success", injected, CancellationToken.None);

        var res = await client.PostAsync("/api/v1/runs/current/reward/reroll-card-choices", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var activeReward = doc.RootElement.GetProperty("run").GetProperty("activeReward");
        Assert.True(activeReward.GetProperty("rerollUsed").GetBoolean());
        Assert.Equal("Pending", activeReward.GetProperty("cardStatus").GetString());
        Assert.Equal(3, activeReward.GetProperty("cardChoices").GetArrayLength());
    }

    // 6. After reroll used, second call → 409
    [Fact]
    public async Task RerollCardChoices_SecondCall_Returns409()
    {
        var client = await ClientWithActiveRewardAsync("reroll-double");
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = (await repo.TryLoadAsync("reroll-double", CancellationToken.None))!;
        Assert.NotNull(s.ActiveReward);

        var pendingReward = s.ActiveReward!.CardStatus == CardRewardStatus.Pending
            ? s.ActiveReward
            : s.ActiveReward with { CardStatus = CardRewardStatus.Pending };
        var injected = s with
        {
            Relics = new[] { "lucky_die" },
            ActiveReward = pendingReward,
        };
        await repo.SaveAsync("reroll-double", injected, CancellationToken.None);

        // First call succeeds
        var first = await client.PostAsync("/api/v1/runs/current/reward/reroll-card-choices", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second call → 409 (already used)
        var second = await client.PostAsync("/api/v1/runs/current/reward/reroll-card-choices", null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
