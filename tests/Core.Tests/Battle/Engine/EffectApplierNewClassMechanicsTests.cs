using System.Collections.Immutable;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class EffectApplierNewClassMechanicsTests
{
    private static IRng Rng() => new FakeRng(new int[32], new double[8]);

    [Fact]
    public void Max_combo_this_turn_amount_source_uses_the_historical_maximum()
    {
        var state = BattleFixtures.MinimalState() with
        {
            ComboCount = 2,
            MaxComboThisTurn = 7,
        };
        var caster = state.Allies[0];
        var effect = new CardEffect(
            "attack",
            EffectScope.Single,
            EffectSide.Enemy,
            Amount: 0,
            AmountSource: "maxComboThisTurn");

        var (next, _) = EffectApplier.Apply(
            state,
            caster,
            effect,
            Rng(),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(7, next.Allies[0].AttackSingle.Sum);
    }

    [Fact]
    public void Gain_combo_gauge_targets_participant_resources_and_clamps_to_maximum()
    {
        var state = BattleFixtures.MinimalState() with { ComboGauge = 9 };
        var caster = state.Allies[0];
        var effect = new CardEffect(
            "gainComboGauge", EffectScope.Self, null, Amount: 4);

        var (next, events) = EffectApplier.Apply(
            state,
            caster,
            effect,
            Rng(),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(BattleEngine.MaxComboGauge, next.ComboGauge);
        var changed = Assert.Single(events);
        Assert.Equal(1, changed.Amount);
        Assert.Equal("remaining=10", changed.Note);
    }

    [Fact]
    public void Resonance_triggers_when_single_ally_target_resolves_to_self()
    {
        var hero = BattleFixtures.Hero() with
        {
            Statuses = BattleFixtures.Hero().Statuses.SetItem(
                BattleStatusIds.Resonance, 1),
        };
        var ally = BattleFixtures.Hero(slotIndex: 1) with
        {
            InstanceId = "ally-2",
            DefinitionId = "ally-definition",
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, ally)) with
        {
            TargetAllyIndex = 0,
        };
        var effect = new CardEffect(
            "buff",
            EffectScope.Single,
            EffectSide.Ally,
            Amount: 1,
            Name: BattleStatusIds.Vigor);

        var (next, _) = EffectApplier.Apply(
            state,
            hero,
            effect,
            Rng(),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(1, next.Allies[0].GetStatus(BattleStatusIds.Vigor));
        Assert.Equal(1, next.Allies[1].GetStatus(BattleStatusIds.Vigor));
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Resonance));
    }

    [Fact]
    public void Consuming_resonance_immediately_triggers_the_owners_character_trait()
    {
        var hero = BattleFixtures.Hero() with
        {
            Statuses = BattleFixtures.Hero().Statuses.SetItem(
                BattleStatusIds.Resonance, 1),
        };
        var ally = BattleFixtures.Hero(slotIndex: 1) with
        {
            InstanceId = "ally-2",
            DefinitionId = "ally-definition",
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, ally)) with
        {
            CharacterId = "resonance-character",
            TargetAllyIndex = 0,
        };
        var trait = new CharacterTraitDefinition(
            "harmony",
            "Harmony",
            CharacterTraitTrigger.OnResonanceConsumed,
            [new CardEffect(
                "block", EffectScope.All, EffectSide.Ally, Amount: 3)]);
        var character = new CharacterDefinition(
            "resonance-character", "Resonance Character", 70, 0, 3, [],
            Trait: trait);
        var catalog = BattleFixtures.MinimalCatalog() with
        {
            Characters = new Dictionary<string, CharacterDefinition>
            {
                [character.Id] = character,
            },
        };
        var effect = new CardEffect(
            "buff",
            EffectScope.Single,
            EffectSide.Ally,
            Amount: 1,
            Name: BattleStatusIds.Vigor);

        var (next, events) = EffectApplier.Apply(
            state, hero, effect, Rng(), catalog);

        Assert.Equal(3, next.Allies[0].Block.Sum);
        Assert.Equal(3, next.Allies[1].Block.Sum);
        Assert.Contains(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.CharacterTraitTriggered
            && battleEvent.Note == trait.Id);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void Resonance_does_not_trigger_for_another_or_multiple_targets(
        int targetAllyIndex,
        bool targetAll)
    {
        var hero = BattleFixtures.Hero() with
        {
            Statuses = BattleFixtures.Hero().Statuses.SetItem(
                BattleStatusIds.Resonance, 1),
        };
        var ally = BattleFixtures.Hero(slotIndex: 1) with
        {
            InstanceId = "ally-2",
            DefinitionId = "ally-definition",
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, ally)) with
        {
            TargetAllyIndex = targetAllyIndex,
        };
        var effect = new CardEffect(
            "buff",
            targetAll ? EffectScope.All : EffectScope.Single,
            EffectSide.Ally,
            Amount: 1,
            Name: BattleStatusIds.Vigor);

        var (next, _) = EffectApplier.Apply(
            state,
            hero,
            effect,
            Rng(),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(1, next.Allies[0].GetStatus(BattleStatusIds.Resonance));
        Assert.Equal(1, next.Allies[1].GetStatus(BattleStatusIds.Vigor));
        Assert.Equal(targetAll ? 1 : 0,
            next.Allies[0].GetStatus(BattleStatusIds.Vigor));
    }
}
