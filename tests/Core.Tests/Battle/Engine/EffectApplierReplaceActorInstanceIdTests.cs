using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// EffectApplier.ReplaceActor が InstanceId 検索で動作することを保証する回帰テスト。
/// 10.2.A の `IndexOf(before)` 経路は、複数 effect 連続適用で古い snapshot 参照になり
/// IndexOf == -1 / SetItem 例外を引き起こす latent bug を持っていた。
/// </summary>
public class EffectApplierReplaceActorInstanceIdTests
{
    private static BattleState MakeState(CombatActor hero, CombatActor enemy) => new(
        Turn: 1, Phase: BattlePhase.PlayerInput,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: ImmutableArray.Create(enemy),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        EncounterId: "enc_test");

    [Fact] public void Apply_attack_then_block_on_same_caster_succeeds()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = MakeState(hero, goblin);
        var rng = new FakeRng(new int[0], new double[0]);

        // 1 つ目: attack（caster.AttackSingle に加算）
        var atkEff = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
        var (s1, _) = EffectApplier.Apply(s, hero, atkEff, rng);
        Assert.Equal(5, s1.Allies[0].AttackSingle.Sum);

        // 2 つ目: block（caster.Block に加算）。caster ref は古い hero のまま渡しても InstanceId で再 fetch されて正しく動作することを検証
        var blkEff = new CardEffect("block", EffectScope.Self, null, 3);
        var caster1 = s1.Allies[0];   // 最新の hero
        var (s2, _) = EffectApplier.Apply(s1, caster1, blkEff, rng);
        Assert.Equal(5, s2.Allies[0].AttackSingle.Sum);  // attack 結果が保持
        Assert.Equal(3, s2.Allies[0].Block.Sum);          // block も加算
    }

    [Fact] public void Apply_block_with_stale_caster_ref_does_not_throw()
    {
        // ReplaceActor が IndexOf ベースだと、stale caster で IndexOf=-1 → SetItem 例外
        // InstanceId 検索なら例外なく更新できる
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = MakeState(hero, goblin);
        var rng = new FakeRng(new int[0], new double[0]);

        // hero に AttackSingle を加算（実 state を変える）
        var atkEff = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
        var (s1, _) = EffectApplier.Apply(s, hero, atkEff, rng);

        // 古い hero ref（=Statuses 等が更新前）を caster として block effect を再度適用
        var blkEff = new CardEffect("block", EffectScope.Self, null, 4);
        // stale caster (= 元の hero) を渡す。InstanceId 検索なら最新 actor を見つけて更新できる
        var (s2, _) = EffectApplier.Apply(s1, hero, blkEff, rng);

        // attack の結果が消失していないことを確認
        Assert.Equal(5, s2.Allies[0].AttackSingle.Sum);
        Assert.Equal(4, s2.Allies[0].Block.Sum);
    }
}
