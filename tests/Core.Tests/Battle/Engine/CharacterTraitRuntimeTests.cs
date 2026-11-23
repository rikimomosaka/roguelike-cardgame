using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class CharacterTraitRuntimeTests
{
    [Fact]
    public void Combo_trait_fires_on_threshold_crossing_once_per_turn()
    {
        var setup = CreateSetup();

        var (afterFirst, firstEvents) = CharacterTraitRuntime.TryTrigger(
            setup.State,
            setup.Hero.InstanceId,
            CharacterTraitTrigger.OnComboReached,
            setup.Catalog,
            new FakeRng([], []),
            previousValue: 3,
            currentValue: 4,
            orderStart: 7);

        Assert.Equal(1, afterFirst.Allies[0].GetStatus("afterimage"));
        Assert.Equal(1, afterFirst.CharacterTraitUsesThisTurn["combo_flash"]);
        var triggered = Assert.Single(
            firstEvents,
            battleEvent => battleEvent.Kind ==
                BattleEventKind.CharacterTraitTriggered);
        Assert.Equal(7, triggered.Order);
        Assert.Equal(CombatSourceKind.CharacterTrait, triggered.Source?.Kind);
        Assert.Equal("combo_flash", triggered.Source?.DefinitionId);
        var applied = Assert.Single(
            firstEvents,
            battleEvent => battleEvent.Kind == BattleEventKind.ApplyStatus);
        Assert.Equal(CombatSourceKind.CharacterTrait, applied.Source?.Kind);

        var (afterSecond, secondEvents) = CharacterTraitRuntime.TryTrigger(
            afterFirst,
            setup.Hero.InstanceId,
            CharacterTraitTrigger.OnComboReached,
            setup.Catalog,
            new FakeRng([], []),
            previousValue: 3,
            currentValue: 5);

        Assert.Same(afterFirst, afterSecond);
        Assert.Empty(secondEvents);
        Assert.Equal(1, afterSecond.Allies[0].GetStatus("afterimage"));
    }

    [Theory]
    [InlineData(4, 5)]
    [InlineData(1, 3)]
    [InlineData(4, 4)]
    public void Combo_trait_does_not_fire_without_crossing_from_below(
        int previousValue,
        int currentValue)
    {
        var setup = CreateSetup();

        var (next, events) = CharacterTraitRuntime.TryTrigger(
            setup.State,
            setup.Hero.InstanceId,
            CharacterTraitTrigger.OnComboReached,
            setup.Catalog,
            new FakeRng([], []),
            previousValue,
            currentValue);

        Assert.Same(setup.State, next);
        Assert.Empty(events);
    }

    [Fact]
    public void ResetTurnUsage_clears_current_projected_root()
    {
        var setup = CreateSetup();
        var (used, _) = CharacterTraitRuntime.TryTrigger(
            setup.State,
            setup.Hero.InstanceId,
            CharacterTraitTrigger.OnComboReached,
            setup.Catalog,
            new FakeRng([], []),
            previousValue: 0,
            currentValue: 4);

        var reset = CharacterTraitRuntime.ResetTurnUsage(used);

        Assert.Empty(reset.CharacterTraitUsesThisTurn);
    }

    private static (BattleState State, CombatActor Hero, DataCatalog Catalog)
        CreateSetup()
    {
        var hero = BattleFixtures.Hero();
        var state = new BattleState(
            Turn: 1,
            Phase: BattlePhase.PlayerInput,
            Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0,
            TargetEnemyIndex: 0,
            Energy: 3,
            EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 4,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test")
        {
            CharacterId = "trait_character",
            MaxComboThisTurn = 4,
        };

        var trait = new CharacterTraitDefinition(
            "combo_flash",
            "Combo Flash",
            CharacterTraitTrigger.OnComboReached,
            new[]
            {
                new CardEffect(
                    "buff", EffectScope.Self, null, 1,
                    Name: "afterimage"),
            },
            Threshold: 4,
            UsesPerTurn: 1);
        var character = new CharacterDefinition(
            "trait_character",
            "Trait Character",
            70,
            0,
            3,
            new[] { "strike" },
            Trait: trait);
        var catalog = BattleFixtures.MinimalCatalog() with
        {
            Characters = new Dictionary<string, CharacterDefinition>
            {
                [character.Id] = character,
            },
        };

        return (state, hero, catalog);
    }
}
