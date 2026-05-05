using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// POST /api/v1/runs/current/reward/proceed で IsBossReward=true の場合に
/// ActTransition.AdvanceAct が呼ばれ、アクト遷移が完了することを検証する。
/// </summary>
public class RewardProceedActTransitionTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _fx;
    public RewardProceedActTransitionTests(TempDataFactory fx) { _fx = fx; }

    /// <summary>
    /// アカウントを作成し、act1 ボス報酬を持つ RunState を直接 repo に書き込んで
    /// X-Account-Id ヘッダ付き HttpClient を返す。
    /// </summary>
    private async Task<HttpClient> SetupRunWithBossRewardAsync(string accountId, int act)
    {
        _fx.ResetData();
        var client = _fx.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);

        // 新しいランを開始
        (await client.PostAsync("/api/v1/runs/new", content: null)).EnsureSuccessStatusCode();

        var repo = _fx.Services.GetRequiredService<ISaveRepository>();
        var catalog = _fx.Services.GetRequiredService<DataCatalog>();

        var s = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;

        // ボス戦エンカウンターキューに enc_b_guardian を注入してボス戦闘を開始
        var bossQueue = ImmutableArray.Create("enc_b_guardian");
        var injected = s with
        {
            CurrentAct = act,
            EncounterQueueBoss = bossQueue,
        };

        // ボスを倒した直後の状態を模倣: BossRewardFlow でボス報酬を生成し ActiveReward に設定
        var rewardRng = new SystemRng(unchecked((int)injected.RngSeed ^ (int)injected.PlaySeconds ^ 0x5EED));
        var reward = BossRewardFlow.GenerateBossReward(injected, catalog, rewardRng);

        var withReward = injected with
        {
            ActiveBattle = null,
            ActiveReward = reward,
        };

        await repo.SaveAsync(accountId, withReward, CancellationToken.None);
        return client;
    }

    /// <summary>GET /api/v1/runs/current のスナップショットを返す（204 の場合は null）。</summary>
    private static async Task<JsonDocument?> GetCurrentSnapshotAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/runs/current");
        if (resp.StatusCode == HttpStatusCode.NoContent) return null;
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    // ─── Tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Proceed_OnBossReward_AdvancesActAndHealsFull()
    {
        const string AccountId = "proc-boss-act1";
        var client = await SetupRunWithBossRewardAsync(AccountId, act: 1);

        var beforeDoc = await GetCurrentSnapshotAsync(client);
        Assert.NotNull(beforeDoc);
        var beforeRun = beforeDoc!.RootElement.GetProperty("run");
        Assert.Equal(1, beforeRun.GetProperty("currentAct").GetInt32());

        // ActiveReward.isBossReward が true であることを確認
        var activeReward = beforeRun.GetProperty("activeReward");
        Assert.NotEqual(JsonValueKind.Null, activeReward.ValueKind);
        Assert.True(activeReward.GetProperty("isBossReward").GetBoolean());

        // Proceed を呼ぶ
        var resp = await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed",
            new RewardProceedRequestDto(ElapsedSeconds: 0));
        resp.EnsureSuccessStatusCode();

        // アクト遷移後の状態を確認
        var afterDoc = await GetCurrentSnapshotAsync(client);
        Assert.NotNull(afterDoc);
        var afterRun = afterDoc!.RootElement.GetProperty("run");

        // CurrentAct が 2 に進んでいる
        Assert.Equal(2, afterRun.GetProperty("currentAct").GetInt32());

        // HP がフル回復している
        int maxHp = afterRun.GetProperty("maxHp").GetInt32();
        int currentHp = afterRun.GetProperty("currentHp").GetInt32();
        Assert.Equal(maxHp, currentHp);

        // ActiveReward が null になっている
        Assert.Equal(JsonValueKind.Null, afterRun.GetProperty("activeReward").ValueKind);

        // VisitedNodeIds が空になっている
        var visited = afterRun.GetProperty("visitedNodeIds");
        Assert.Equal(0, visited.GetArrayLength());
    }

    [Fact]
    public async Task Proceed_OnNormalReward_DoesNotAdvanceAct()
    {
        // 通常の enemy バトル勝利後の報酬で proceed しても act は変わらない
        _fx.ResetData();
        var client = _fx.CreateClient();
        const string AccountId = "proc-normal-reward";
        await BattleTestHelpers.EnsureAccountAsync(client, AccountId);
        BattleTestHelpers.WithAccount(client, AccountId);
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(client);
        (await client.PostAsJsonAsync("/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 }))
            .EnsureSuccessStatusCode();

        var beforeDoc = await GetCurrentSnapshotAsync(client);
        Assert.NotNull(beforeDoc);
        int actBefore = beforeDoc!.RootElement.GetProperty("run").GetProperty("currentAct").GetInt32();
        var activeReward = beforeDoc.RootElement.GetProperty("run").GetProperty("activeReward");
        Assert.NotEqual(JsonValueKind.Null, activeReward.ValueKind);
        Assert.False(activeReward.GetProperty("isBossReward").GetBoolean());

        (await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed",
            new RewardProceedRequestDto(ElapsedSeconds: 0))).EnsureSuccessStatusCode();

        var afterDoc = await GetCurrentSnapshotAsync(client);
        Assert.NotNull(afterDoc);
        int actAfter = afterDoc!.RootElement.GetProperty("run").GetProperty("currentAct").GetInt32();
        Assert.Equal(actBefore, actAfter);
        Assert.Equal(JsonValueKind.Null, afterDoc.RootElement.GetProperty("run").GetProperty("activeReward").ValueKind);
    }

    /// <summary>
    /// Regression (Bug ②): Proceed on boss reward must return the snapshot body so
    /// the client can swap to the newly-generated act map without an extra GET.
    /// </summary>
    [Fact]
    public async Task Proceed_OnBossReward_ResponseBodyContainsNewActSnapshot()
    {
        const string AccountId = "proc-boss-body";
        var client = await SetupRunWithBossRewardAsync(AccountId, act: 1);

        var resp = await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed",
            new RewardProceedRequestDto(ElapsedSeconds: 0));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var run = body.RootElement.GetProperty("run");
        Assert.Equal(2, run.GetProperty("currentAct").GetInt32());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("activeReward").ValueKind);
        // The returned map must be the newly-generated act 2 map, reachable via map.nodes.
        Assert.True(body.RootElement.GetProperty("map").GetProperty("nodes").GetArrayLength() > 0);
        var startId = body.RootElement.GetProperty("map").GetProperty("startNodeId").GetInt32();
        Assert.Equal(startId, run.GetProperty("currentNodeId").GetInt32());
    }

    /// <summary>
    /// Regression: Act 遷移直後は選択肢を自動生成せず null のまま。層開始レリックは
    /// プレイヤーがスタートマスをクリックした時（/act-start/enter）に初めて生成される。
    /// </summary>
    [Fact]
    public async Task Proceed_OnBossReward_DoesNotAutoGenerateAct2RelicChoice()
    {
        const string AccountId = "proc-boss-act2-nochoice";
        var client = await SetupRunWithBossRewardAsync(AccountId, act: 1);

        var resp = await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed",
            new RewardProceedRequestDto(ElapsedSeconds: 0));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var run = body.RootElement.GetProperty("run");
        Assert.Equal(JsonValueKind.Null, run.GetProperty("activeActStartRelicChoice").ValueKind);

        // Enter で act2 の選択肢が生成される
        var enterResp = await client.PostAsync("/api/v1/act-start/enter", content: null);
        Assert.Equal(HttpStatusCode.OK, enterResp.StatusCode);
        var enterDoc = JsonDocument.Parse(await enterResp.Content.ReadAsStringAsync());
        var choice = enterDoc.RootElement.GetProperty("run").GetProperty("activeActStartRelicChoice");
        Assert.NotEqual(JsonValueKind.Null, choice.ValueKind);
        Assert.Equal(3, choice.GetProperty("relicIds").GetArrayLength());
        var catalog = _fx.Services.GetRequiredService<DataCatalog>();
        var pool = catalog.ActStartRelicPools![2];
        foreach (var el in choice.GetProperty("relicIds").EnumerateArray())
            Assert.Contains(el.GetString()!, pool);
    }

    /// <summary>
    /// Phase 10.6.B T8: アクト遷移後の unknownResolutions は空 (lazy resolve に切替)。
    /// Unknown タイルへの入場時に NodeEffectResolver が PassiveModifiers を適用して解決する。
    /// 以前のテストは pre-resolve を前提としていたが、lazy resolve 方式では
    /// 遷移直後は Empty で正しい動作であることを確認する。
    /// </summary>
    [Fact]
    public async Task Proceed_OnBossReward_NewActHasEmptyUnknownResolutions_LazyResolveMode()
    {
        const string AccountId = "proc-boss-unknowns";
        var client = await SetupRunWithBossRewardAsync(AccountId, act: 1);

        var resp = await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed",
            new RewardProceedRequestDto(ElapsedSeconds: 0));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var run = body.RootElement.GetProperty("run");
        var resolutions = run.GetProperty("unknownResolutions");

        // Phase 10.6.B T8: lazy resolve により遷移直後は空 dict で正常
        Assert.Equal(JsonValueKind.Object, resolutions.ValueKind);
        int count = 0;
        foreach (var _ in resolutions.EnumerateObject()) count++;
        Assert.Equal(0, count);
    }
}
