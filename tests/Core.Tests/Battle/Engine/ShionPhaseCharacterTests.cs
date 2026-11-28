using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// 静のダメージ軽減と、転相拳士シオン（striker_shion）の特性・戦技・奥義（2026-11-28）。
/// 組込データのカタログを使う。
/// </summary>
public sealed class ShionPhaseCharacterTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    private static BattleCardInstance Card(string defId, string instId) => new(instId, defId, false, null);

    private static BattleState ShionState(ActorPhase phase, int gauge = 0, params BattleCardInstance[] hand) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = phase }),
            hand: ImmutableArray.Create(hand),
            energy: 3) with
        {
            CharacterId = "striker_shion",
            ComboGauge = gauge,
        };

    // ---- 静: アタックで受けるダメージ 0.75 倍 ----

    [Fact]
    public void Calm_target_takes_three_quarters_attack_damage_after_block()
    {
        var target = BattleFixtures.Goblin(hp: 30) with { Phase = ActorPhase.Calm };

        var (updated, events, _) = DealDamageHelper.Apply(BattleFixtures.Hero(), target, 10, "single", 0);

        // 10 × 0.75 = 7（切り捨て）。
        Assert.Equal(30 - 7, updated.CurrentHp);
        Assert.Equal(7, events.Single(e => e.Kind == BattleEventKind.DealDamage).Amount);
    }

    [Fact]
    public void Calm_and_vulnerable_multiply_in_order()
    {
        var target = BattleFixtures.Goblin(hp: 30) with
        {
            Phase = ActorPhase.Calm,
            Statuses = ImmutableDictionary<string, int>.Empty.Add("vulnerable", 1),
        };

        var (updated, _, _) = DealDamageHelper.Apply(BattleFixtures.Hero(), target, 10, "single", 0);

        // 10 × 1.5 = 15 → × 0.75 = 11（各段で切り捨て）。
        Assert.Equal(30 - 11, updated.CurrentHp);
    }

    [Fact]
    public void Calm_does_not_reduce_non_attack_damage()
    {
        var target = BattleFixtures.Goblin(hp: 30) with { Phase = ActorPhase.Calm };
        var context = new HitContext(
            DamageType.Poison, CombatSource.Unattributed,
            new DamageCauseChain(ImmutableArray<DamageCauseFrame>.Empty));

        var (updated, _, _) = DealDamageHelper.ApplyAfterBlock(
            BattleFixtures.Hero(), target, 10, 10, 0, "poison", 0, null, context);

        Assert.Equal(30 - 10, updated.CurrentHp);
    }

    // ---- 特性: 動になった時コンボゲージ+1（動から動への上書きも） ----

    [Fact]
    public void Entering_motion_gains_one_combo_gauge_even_when_already_in_motion()
    {
        var setMotion = new RoguelikeCardGame.Core.Cards.CardEffect(
            "setPhase", RoguelikeCardGame.Core.Cards.EffectScope.Self, null, 0, Name: "motion");

        var fromNone = ShionState(ActorPhase.None);
        var (afterFirst, firstEvents) = EffectApplier.Apply(
            fromNone, fromNone.Allies[0], setMotion, Rng(), Catalog);
        Assert.Equal(1, afterFirst.ComboGauge);
        Assert.Contains(firstEvents, e => e.Kind == BattleEventKind.CharacterTraitTriggered);

        var (afterSecond, _) = EffectApplier.Apply(
            afterFirst, afterFirst.Allies[0], setMotion, Rng(), Catalog);
        Assert.Equal(ActorPhase.Motion, afterSecond.Allies[0].Phase);
        Assert.Equal(2, afterSecond.ComboGauge);
    }

    [Fact]
    public void Entering_calm_does_not_gain_combo_gauge()
    {
        var setCalm = new RoguelikeCardGame.Core.Cards.CardEffect(
            "setPhase", RoguelikeCardGame.Core.Cards.EffectScope.Self, null, 0, Name: "calm");
        var state = ShionState(ActorPhase.None);

        var (after, _) = EffectApplier.Apply(state, state.Allies[0], setCalm, Rng(), Catalog);

        Assert.Equal(0, after.ComboGauge);
    }

    [Fact]
    public void Other_characters_do_not_gain_combo_gauge_when_entering_motion()
    {
        var setMotion = new RoguelikeCardGame.Core.Cards.CardEffect(
            "setPhase", RoguelikeCardGame.Core.Cards.EffectScope.Self, null, 0, Name: "motion");
        var sera = ShionState(ActorPhase.None) with { CharacterId = "default" };

        var (after, _) = EffectApplier.Apply(sera, sera.Allies[0], setMotion, Rng(), Catalog);

        Assert.Equal(0, after.ComboGauge);
    }

    // ---- 戦技「転身」: ゲージ 2、位相を反転、位相なしでは使えない ----

    [Fact]
    public void Tactical_inverts_calm_to_motion_and_refunds_one_gauge_through_the_trait()
    {
        var (after, _) = BattleEngine.UseCharacterSkill(ShionState(ActorPhase.Calm, gauge: 2), "tactical", Catalog, Rng());

        Assert.Equal(ActorPhase.Motion, after.Allies[0].Phase);
        Assert.Equal(1, after.ComboGauge);
    }

    [Fact]
    public void Tactical_cannot_be_used_without_a_phase()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UseCharacterSkill(ShionState(ActorPhase.None, gauge: 2), "tactical", Catalog, Rng()));
    }

    // ---- 奥義「両極拳」: ゲージ 7、使用時の位相ごとに別の効果 ----

    [Fact]
    public void Ultimate_without_phase_blocks_and_enters_calm()
    {
        var (after, _) = BattleEngine.UseCharacterSkill(ShionState(ActorPhase.None, gauge: 7), "ultimate", Catalog, Rng());

        Assert.Equal(ActorPhase.Calm, after.Allies[0].Phase);
        Assert.Equal(8, after.Allies[0].Block.Sum);
        Assert.Equal(0, after.ComboGauge);
    }

    [Fact]
    public void Ultimate_in_calm_blocks_and_grants_bipolar_to_every_hand_card()
    {
        var state = ShionState(ActorPhase.Calm, 7,
            Card("striker_basic_attack", "a1"), Card("striker_basic_defense", "d1"));

        var (after, _) = BattleEngine.UseCharacterSkill(state, "ultimate", Catalog, Rng());

        Assert.Equal(12, after.Allies[0].Block.Sum);
        Assert.All(after.Hand, card => Assert.Equal(1, card.KeywordCount("bipolar")));
        // 静のまま（奥義はアタックではない）。
        Assert.Equal(ActorPhase.Calm, after.Allies[0].Phase);
    }

    [Fact]
    public void Ultimate_in_motion_gains_energy_and_draws()
    {
        var draw = Enumerable.Range(0, 5).Select(i => Card("striker_basic_attack", $"p{i}")).ToImmutableArray();
        var state = ShionState(ActorPhase.Motion, 7) with { DrawPile = draw };

        var (after, _) = BattleEngine.UseCharacterSkill(state, "ultimate", Catalog, Rng());

        Assert.Equal(3 + 2, after.Energy);
        Assert.Equal(3, after.Hand.Length);
        Assert.Equal(0, after.Allies[0].Block.Sum);
    }

    // ---- データ ----

    [Fact]
    public void Shion_definition_matches_the_design()
    {
        var shion = Catalog.Characters["striker_shion"];

        Assert.Equal("転相拳士シオン", shion.Name);
        Assert.Equal(CharacterClass.Striker, shion.CharacterClass);
        Assert.Equal(76, shion.MaxHp);
        Assert.Equal(6, shion.Attack);
        Assert.Equal(5, shion.Defense);
        var tactical = shion.EffectiveSkills.Single(s => s.Id == "tactical");
        Assert.Equal(2, tactical.Cost.ComboGauge);
        Assert.True(tactical.RequiresPhase);
        Assert.Equal(1, tactical.EffectiveUsesPerTurn);
        Assert.Equal(7, shion.EffectiveSkills.Single(s => s.Id == "ultimate").Cost.ComboGauge);
        Assert.Equal(2, shion.Deck.Count(id => id == "striker_basic_calm_form"));
        Assert.Equal(2, shion.Deck.Count(id => id == "striker_basic_motion_form"));
        Assert.Equal("静の型", Catalog.Cards["striker_basic_calm_form"].Name);
        Assert.Equal("動の型", Catalog.Cards["striker_basic_motion_form"].Name);
        Assert.Equal("静水の構え", Catalog.Cards["striker_phase_calm_form"].Name);
        Assert.Equal("奔流の構え", Catalog.Cards["striker_phase_motion_form"].Name);
    }
}
