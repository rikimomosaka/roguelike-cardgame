using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// 「ターンを終えた側」だけ status countdown する仕様 (Slay the Spire 慣習) の
/// 統合テスト。ユーザ報告のバグ:
///   - 敵が EnemyAttacking で player に weak=1 を debuff
///   - (旧仕様) TurnStart で全 actor 一括 countdown → weak 1→0 即削除
///   - (旧仕様) 次の player turn では weak が無く、player は弱体化されないまま
///     攻撃してしまう
/// 新仕様 (BattleEngine.EndTurn 中に SideStatusCountdown を side ごとに呼ぶ) で
/// この問題が解消されることを e2e で検証する。
/// </summary>
public class BattleEngineEndTurnDebuffTimingTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[0]);

    private static EnemyDefinition EnemyWithMove(
        string id, string moveId, MoveKind kind, params CardEffect[] effects) =>
        new(id, id, $"img_{id}", 30, new EnemyPool(1, EnemyTier.Weak),
            moveId,
            new[] {
                new MoveDefinition(moveId, kind, effects, moveId)
            });

    private static CombatActor MakeEnemy(string id, int slot, int hp, string moveId) =>
        new($"{id}_inst_{slot}", id, ActorSide.Enemy, slot, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, moveId,
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);

    private static BattleState MakeState(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerInput,
        Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    [Fact]
    public void Enemy_debuff_persists_into_next_player_turn()
    {
        // 敵 "snarl" が weak (Decrement 系 debuff、duration 1) を player に付与。
        // 旧仕様だと TurnStart で即 0 → 削除されて次の player turn で活きなかった。
        // 新仕様 (player turn end 直後に Ally 側 countdown だけ走る) では、
        // weak は EnemyAttacking で付与 → Enemy 側 countdown ではタッチされない →
        // 次の TurnStart でも player の weak はそのまま残る。
        var hero = BattleFixtures.Hero(70);
        var enemy = MakeEnemy("snarler", 0, 20, "snarl");
        var def = EnemyWithMove("snarler", "snarl", MoveKind.Debuff,
            new CardEffect("debuff", EffectScope.All, EffectSide.Enemy, 1, Name: "weak"));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = MakeState(hero, enemy);

        var (next, _) = BattleEngine.EndTurn(s, Rng(), catalog);

        // EndTurn 終了 = 次の player turn の TurnStart まで完了した state。
        // 敵から weak=1 を受け取って、Allies countdown の前 (= player turn 終了
        // 直後) には weak は無い、Enemies countdown の後 (= enemy turn 終了
        // 直後) に weak=1 が新規付与、その後の TurnStart は countdown を行わない。
        // → 次の player turn 開始時点で weak=1 のまま。
        var heroAfter = next.Allies.First(a => a.InstanceId == hero.InstanceId);
        Assert.Equal(1, heroAfter.GetStatus("weak"));
    }

    [Fact]
    public void Player_applied_vulnerable_lasts_correct_number_of_enemy_turns()
    {
        // 直接プレイヤーが vulnerable=2 を持った状態を入れて、EndTurn 後にどう
        // countdown されるか検証。新仕様では PlayerAttacking 直後に Ally
        // countdown が走るため、player の vulnerable が 2 → 1 に減る。
        var hero = BattleFixtures.WithVulnerable(BattleFixtures.Hero(70), 2);
        var enemy = MakeEnemy("dummy", 0, 20, "wait");
        var def = EnemyWithMove("dummy", "wait", MoveKind.Defend,
            new CardEffect("block", EffectScope.Self, null, 0));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = MakeState(hero, enemy);

        var (next, _) = BattleEngine.EndTurn(s, Rng(), catalog);

        // hero の vulnerable は player turn 終了で 2 → 1。
        var heroAfter = next.Allies.First(a => a.InstanceId == hero.InstanceId);
        Assert.Equal(1, heroAfter.GetStatus("vulnerable"));
    }

    [Fact]
    public void Enemy_status_decrements_at_end_of_enemy_turn()
    {
        // 敵に最初から vulnerable=2 がある状態。
        // PlayerAttacking では player は何もしない。EnemyAttacking で敵が動いて
        // (vulnerable=2 状態で 1 ターン経過) → Enemy 側 countdown で 2 → 1。
        var hero = BattleFixtures.Hero(70);
        var enemy = BattleFixtures.WithVulnerable(MakeEnemy("dummy", 0, 20, "wait"), 2);
        var def = EnemyWithMove("dummy", "wait", MoveKind.Defend,
            new CardEffect("block", EffectScope.Self, null, 0));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = MakeState(hero, enemy);

        var (next, _) = BattleEngine.EndTurn(s, Rng(), catalog);

        var enemyAfter = next.Enemies.First(e => e.InstanceId == enemy.InstanceId);
        Assert.Equal(1, enemyAfter.GetStatus("vulnerable"));
    }

    [Fact]
    public void Both_sides_countdown_independently_in_one_round()
    {
        // 両側に Decrement 系 status をセット。1 round (EndTurn 1 回) で各々 -1。
        var hero = BattleFixtures.WithVulnerable(BattleFixtures.Hero(70), 3);
        var enemy = BattleFixtures.WithVulnerable(MakeEnemy("dummy", 0, 20, "wait"), 3);
        var def = EnemyWithMove("dummy", "wait", MoveKind.Defend,
            new CardEffect("block", EffectScope.Self, null, 0));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = MakeState(hero, enemy);

        var (next, _) = BattleEngine.EndTurn(s, Rng(), catalog);

        var heroAfter = next.Allies.First(a => a.InstanceId == hero.InstanceId);
        var enemyAfter = next.Enemies.First(e => e.InstanceId == enemy.InstanceId);
        Assert.Equal(2, heroAfter.GetStatus("vulnerable"));
        Assert.Equal(2, enemyAfter.GetStatus("vulnerable"));
    }
}
