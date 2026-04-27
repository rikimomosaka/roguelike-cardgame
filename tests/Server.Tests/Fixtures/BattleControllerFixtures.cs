using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Controllers;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Fixtures;

/// <summary>
/// BattleController の test 用 helper。
/// 既存 <see cref="TempDataFactory"/> + <see cref="BattleTestHelpers"/> を流用し、
/// 「アカウント作成 → ラン開始 → 敵タイル進入で <c>RunState.ActiveBattle</c> が non-null」
/// の状態まで進めた HttpClient を返す。
/// </summary>
internal static class BattleControllerFixtures
{
    public const string AccountHeader = "X-Account-Id";

    /// <summary>
    /// IClassFixture から渡された <see cref="TempDataFactory"/> を使い、
    /// accountId / ラン / 敵タイル進入をセットアップして HttpClient を返す。
    /// fixture の所有者 (xUnit) が disposal を担うため、呼び出し側で factory を Dispose しないこと。
    /// </summary>
    public static async Task<(HttpClient client, string accountId)>
        SetupRunWithActiveBattleAsync(TempDataFactory factory, string accountId = "test-account")
    {
        factory.ResetData();
        var client = factory.CreateClient();

        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(client);

        return (client, accountId);
    }

    /// <summary>
    /// <see cref="SetupRunWithActiveBattleAsync"/> と同様に敵タイル進入直前まで進めた上で、
    /// <see cref="ISaveRepository"/> から RunState を読み出して <c>Potions[0]</c> に
    /// 指定 id を差し込んでから保存する。<c>/battle/start</c> 呼出時に
    /// <c>BattleEngine.Start</c> が <c>run.Potions</c> を <c>BattleState.Potions</c> に
    /// コピーするため、battle 内で UsePotion 可能な状態になる。
    /// </summary>
    public static async Task<(HttpClient client, string accountId)>
        SetupRunWithPotionAsync(TempDataFactory factory, string potionId, string accountId = "test-account")
    {
        var (client, _) = await SetupRunWithActiveBattleAsync(factory, accountId);

        using var scope = factory.Services.CreateScope();
        var saves = scope.ServiceProvider.GetRequiredService<ISaveRepository>();
        var run = await saves.TryLoadAsync(accountId, CancellationToken.None);
        if (run is null)
            throw new InvalidOperationException(
                $"SetupRunWithPotionAsync: run not found for accountId={accountId}");

        var newPotions = run.Potions.SetItem(0, potionId);
        var updated = run with { Potions = newPotions };
        await saves.SaveAsync(accountId, updated, CancellationToken.None);

        return (client, accountId);
    }

    public static void AssertNoSession(System.IServiceProvider services, string accountId)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        Assert.False(store.TryGet(accountId, out _));
    }

    /// <summary>
    /// 戦闘進行を直接書き換えて Resolved+Victory にする。Task 10 (Finalize) test 用。
    /// /battle/start で session が作られた後に呼び、敵 HP=0 + Phase=Resolved + Outcome=Victory に
    /// セットする。BattleEngine.Finalize は Phase=Resolved だけを validate するため、
    /// Allies の HP は維持して Victory 経路を通す。
    /// </summary>
    public static void ForceSessionVictory(System.IServiceProvider services, string accountId)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        if (!store.TryGet(accountId, out var session))
            throw new InvalidOperationException(
                $"ForceSessionVictory: session not found for accountId={accountId}");
        var killedEnemies = session.State.Enemies
            .Select(e => e with { CurrentHp = 0 })
            .ToImmutableArray();
        var newState = session.State with
        {
            Phase = BattlePhase.Resolved,
            Outcome = BattleOutcome.Victory,
            Enemies = killedEnemies,
        };
        store.Set(accountId, session with { State = newState });
    }

    /// <summary>
    /// 戦闘進行を直接書き換えて Resolved+Defeat にする。Task 10 (Finalize) test 用。
    /// hero HP=0 + Phase=Resolved + Outcome=Defeat にセット。
    /// </summary>
    public static void ForceSessionDefeat(System.IServiceProvider services, string accountId)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        if (!store.TryGet(accountId, out var session))
            throw new InvalidOperationException(
                $"ForceSessionDefeat: session not found for accountId={accountId}");
        var deadAllies = session.State.Allies
            .Select(a => a with { CurrentHp = 0 })
            .ToImmutableArray();
        var newState = session.State with
        {
            Phase = BattlePhase.Resolved,
            Outcome = BattleOutcome.Defeat,
            Allies = deadAllies,
        };
        store.Set(accountId, session with { State = newState });
    }
}
