using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class FourClassVerticalSliceTests
{
    private const string EncounterId = "enc_w_jaw_worm";
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, string[]> StarterDecks => new()
    {
        {
            "default",
            [
                "striker_basic_attack", "striker_basic_attack",
                "striker_basic_defense", "striker_basic_defense",
                "striker_basic_quickdraw", "striker_basic_quickdraw",
                "striker_basic_step", "striker_basic_step",
                "striker_basic_finishing_slash", "striker_basic_finishing_slash",
            ]
        },
        {
            "guardian_mireille",
            [
                "guardian_basic_attack", "guardian_basic_attack",
                "guardian_basic_defense", "guardian_basic_defense",
                "guardian_basic_foothold", "guardian_basic_foothold",
                "guardian_basic_defensive_form", "guardian_basic_defensive_form",
                "guardian_basic_cover_line", "guardian_basic_cover_line",
            ]
        },
        {
            "tactician_yuno",
            [
                "tactician_basic_attack", "tactician_basic_attack",
                "tactician_basic_defense", "tactician_basic_defense",
                "tactician_basic_disrupting_shot", "tactician_basic_disrupting_shot",
                "tactician_basic_counterplan", "tactician_basic_counterplan",
                "tactician_basic_recursive_plan", "tactician_basic_recursive_plan",
            ]
        },
        {
            "enhancer_lyra",
            [
                "enhancer_basic_attack", "enhancer_basic_attack",
                "enhancer_basic_defense", "enhancer_basic_defense",
                "enhancer_basic_preparation", "enhancer_basic_preparation",
                "enhancer_basic_self_attunement", "enhancer_basic_self_attunement",
                "enhancer_basic_afterglow_hymn", "enhancer_basic_afterglow_hymn",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(StarterDecks))]
    public void All_characters_start_with_their_embedded_ten_card_deck_and_resources(
        string characterId,
        string[] expectedDeck)
    {
        var state = StartBattle(characterId);
        var actualDeck = state.Hand
            .Concat(state.DrawPile)
            .Concat(state.DiscardPile)
            .Select(card => card.CardDefinitionId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(3, state.Energy);
        Assert.Equal(3, state.EnergyMax);
        Assert.Equal(5, state.DrawPerTurn);
        Assert.Equal(5, state.Hand.Length);
        Assert.Equal(10, actualDeck.Length);
        Assert.Equal(
            expectedDeck.OrderBy(id => id, StringComparer.Ordinal),
            actualDeck);

        var participant = Assert.Single(state.Participants);
        Assert.Equal(characterId, participant.CharacterId);
        Assert.Equal(3, participant.EnergyPerTurn);
        Assert.Equal(5, participant.DrawPerTurn);
        Assert.Equal(5, participant.Hand.Length);
        Assert.Equal(BattlePhase.PlayerInput, state.Phase);
        Assert.Equal(BattleOutcome.Pending, state.Outcome);
        if (characterId == "guardian_mireille")
            Assert.Equal(1, state.Allies[0].GetStatus(BattleStatusIds.Steadfast));
    }

    [Fact]
    public void Striker_tactical_and_ultimate_apply_their_embedded_status_packages()
    {
        var tacticalReady = StartBattle("default") with { ComboGauge = 3 };

        var (afterTactical, _) = BattleEngine.UseCharacterSkill(
            tacticalReady, "tactical", Catalog, Rng());

        Assert.Equal(0, afterTactical.ComboGauge);
        Assert.Equal(4, afterTactical.Energy);
        Assert.Equal(6, afterTactical.Hand.Length);
        Assert.Equal(1, afterTactical.Allies[0].GetStatus(BattleStatusIds.Omnistrike));

        var ultimateReady = StartBattle("default") with { ComboGauge = 6 };
        var (afterUltimate, _) = BattleEngine.UseCharacterSkill(
            ultimateReady, "ultimate", Catalog, Rng());

        Assert.Equal(1, afterUltimate.Allies[0].GetStatus(BattleStatusIds.Vigor));
        Assert.Equal(1, afterUltimate.Allies[0].GetStatus(BattleStatusIds.Afterimage));
    }

    [Fact]
    public void Guardian_tactical_builds_defense_and_ultimate_queues_block_scaled_attack()
    {
        var tacticalReady = StartBattle("guardian_mireille") with { ComboGauge = 3 };

        var (afterTactical, _) = BattleEngine.UseCharacterSkill(
            tacticalReady, "tactical", Catalog, Rng());

        Assert.Equal(8, afterTactical.Allies[0].Block.Sum);
        Assert.Equal(1, afterTactical.Allies[0].GetStatus(BattleStatusIds.Cover));

        var ultimateReady = StartBattle("guardian_mireille") with { ComboGauge = 6 };
        var (afterUltimate, _) = BattleEngine.UseCharacterSkill(
            ultimateReady, "ultimate", Catalog, Rng());

        Assert.Equal(5, afterUltimate.Allies[0].Block.Sum);
        Assert.Equal(5, afterUltimate.Allies[0].AttackSingle.Sum);
        Assert.Single(afterUltimate.Allies[0].AttackSingle.Hits);
    }

    [Fact]
    public void Tactician_tactical_carries_combo_and_ultimate_uses_turn_maximum_for_poison()
    {
        var tacticalReady = StartBattle("tactician_yuno") with { ComboGauge = 3 };

        var (afterTactical, _) = BattleEngine.UseCharacterSkill(
            tacticalReady, "tactical", Catalog, Rng());

        Assert.Equal(7, afterTactical.Hand.Length);
        Assert.Equal(1, afterTactical.Allies[0].GetStatus(BattleStatusIds.ComboCarryover));

        var ultimateReady = StartBattle("tactician_yuno") with
        {
            ComboGauge = 9,
            MaxComboThisTurn = 7,
        };
        var (afterUltimate, _) = BattleEngine.UseCharacterSkill(
            ultimateReady, "ultimate", Catalog, Rng());

        Assert.Equal(7, afterUltimate.Enemies[0].GetStatus(BattleStatusIds.Poison));
    }

    [Fact]
    public void Enhancer_tactical_heals_and_ultimate_applies_its_support_buffs()
    {
        var started = StartBattle("enhancer_lyra");
        var damagedHero = started.Allies[0] with
        {
            CurrentHp = started.Allies[0].CurrentHp - 10,
        };
        var tacticalReady = started with
        {
            Allies = started.Allies.SetItem(0, damagedHero),
            ComboGauge = 2,
            TargetAllyIndex = 0,
        };

        var (afterTactical, _) = BattleEngine.UseCharacterSkill(
            tacticalReady, "tactical", Catalog, Rng());

        Assert.Equal(damagedHero.CurrentHp + 6, afterTactical.Allies[0].CurrentHp);
        Assert.Equal(1, afterTactical.Allies[0].GetStatus(BattleStatusIds.Resonance));

        var ultimateReady = StartBattle("enhancer_lyra") with
        {
            ComboGauge = 8,
            TargetAllyIndex = 0,
        };
        var (afterUltimate, _) = BattleEngine.UseCharacterSkill(
            ultimateReady, "ultimate", Catalog, Rng());

        Assert.Equal(1, afterUltimate.Allies[0].GetStatus(BattleStatusIds.Vigor));
        Assert.Equal(4, afterUltimate.Allies[0].GetStatus(BattleStatusIds.Regeneration));
    }

    [Theory]
    [MemberData(nameof(StarterDecks))]
    public void All_starter_decks_advance_across_multiple_turns_without_pending_resolution(
        string characterId,
        string[] expectedDeck)
    {
        var state = StartBattle(characterId);
        var expectedIds = expectedDeck.ToHashSet(StringComparer.Ordinal);

        for (var expectedTurn = 2; expectedTurn <= 3; expectedTurn++)
        {
            (state, _) = BattleEngine.EndTurn(state, Rng(), Catalog);

            Assert.Equal(expectedTurn, state.Turn);
            Assert.Equal(BattlePhase.PlayerInput, state.Phase);
            Assert.Equal(BattleOutcome.Pending, state.Outcome);
            Assert.Null(state.PendingCardPlay);
            Assert.Null(state.PendingTurnResolution);
            Assert.All(state.Participants, participant =>
                Assert.Null(participant.PendingCardPlay));
            Assert.All(state.Hand, card =>
                Assert.Contains(card.CardDefinitionId, expectedIds));
        }
    }

    private static BattleState StartBattle(string characterId)
    {
        var run = RunState.NewSoloRun(
            Catalog,
            rngSeed: 123UL,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: FixedNow,
            characterId: characterId,
            runId: $"vertical-slice-{characterId}");

        var (state, _) = BattleEngine.Start(run, EncounterId, Rng(), Catalog);
        return state;
    }

    private static IRng Rng() => new MinimumRng();

    private sealed class MinimumRng : IRng
    {
        public int NextInt(int minInclusive, int maxExclusive) => minInclusive;

        public double NextDouble() => 0d;
    }
}
