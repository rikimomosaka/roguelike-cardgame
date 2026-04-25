using System;
using System.Collections.Immutable;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class BossWinFlowTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _fx;
    public BossWinFlowTests(TempDataFactory fx) { _fx = fx; }

    /// <summary>
    /// アカウントを作成し、ボス戦闘中の RunState を repo に直接書き込んで
    /// X-Account-Id ヘッダ付き HttpClient を返す。
    /// </summary>
    private async Task<HttpClient> SetupRunInBossBattle(string accountId, int act)
    {
        _fx.ResetData();
        var client = _fx.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);

        // 1. 通常の run 開始で RunState を作成
        (await client.PostAsync("/api/v1/runs/new", content: null)).EnsureSuccessStatusCode();

        var repo = _fx.Services.GetRequiredService<ISaveRepository>();
        var catalog = _fx.Services.GetRequiredService<DataCatalog>();

        var s = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;

        // 2. 指定の act に書き換え、Boss encounter queue に enc_b_guardian を注入する
        var bossQueue = ImmutableArray.Create("enc_b_guardian");
        var injected = s with
        {
            CurrentAct = act,
            EncounterQueueBoss = bossQueue,
        };

        // 3. BattlePlaceholder.Start で Boss 戦闘を開始する
        var rng = new SystemRng(42);
        var withBattle = BattlePlaceholder.Start(injected, new EnemyPool(act, EnemyTier.Boss), catalog, rng);

        await repo.SaveAsync(accountId, withBattle, CancellationToken.None);
        return client;
    }

    // Returns true if current run exists, false if deleted (204).
    // Populates isBossReward from activeReward.isBossReward.
    private async Task<(bool exists, bool isBossReward)> GetCurrentBossRewardState(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/runs/current");
        if (resp.StatusCode == System.Net.HttpStatusCode.NoContent) return (false, false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var activeReward = doc.RootElement.GetProperty("run").GetProperty("activeReward");
        if (activeReward.ValueKind == JsonValueKind.Null) return (true, false);
        return (true, activeReward.GetProperty("isBossReward").GetBoolean());
    }

    private async Task<System.Collections.Generic.IReadOnlyList<RunHistoryRecord>> ListHistory(string accountId)
    {
        var repo = _fx.Services.GetRequiredService<IHistoryRepository>();
        return await repo.ListAsync(accountId, CancellationToken.None);
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Act1Boss_Win_SetsIsBossRewardTrue()
    {
        const string AccountId = "boss-act1";
        var client = await SetupRunInBossBattle(AccountId, act: 1);

        var resp = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win",
            new BattleWinRequestDto(ElapsedSeconds: 0));
        resp.EnsureSuccessStatusCode();

        var (exists, isBossReward) = await GetCurrentBossRewardState(client);
        Assert.True(exists);
        Assert.True(isBossReward);
    }

    [Fact]
    public async Task Act3Boss_Win_ReturnsRunResult_AndDeletesCurrent()
    {
        const string AccountId = "boss-act3";
        var client = await SetupRunInBossBattle(AccountId, act: RunConstants.MaxAct);

        var resp = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win",
            new BattleWinRequestDto(ElapsedSeconds: 0));
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var outcome = doc.RootElement.GetProperty("outcome").GetString();
        var runId = doc.RootElement.GetProperty("runId").GetString();
        Assert.Equal("Cleared", outcome);

        // current run は削除されているはず (204 が返る)
        var currentResp = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, currentResp.StatusCode);

        // 履歴に保存されているはず
        var history = await ListHistory(AccountId);
        Assert.Contains(history, h => h.RunId == runId && h.Outcome == RunProgress.Cleared);
    }
}
