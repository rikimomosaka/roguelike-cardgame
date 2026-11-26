using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public sealed class DataCatalogIntegrityValidatorTests
{
    [Fact]
    public void Event_gainCard_references_are_validated()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var eventDefinition = new EventDefinition(
            "broken_card_event",
            "Broken",
            "Start",
            ImmutableArray.Create(new EventChoice(
                "Take",
                null,
                ImmutableArray.Create<EventEffect>(new EventEffect.GainCard("missing_card")))),
            ImmutableArray.Create(1));
        var candidate = catalog with
        {
            Events = Replace(catalog.Events, eventDefinition.Id, eventDefinition),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("missing_card", error.Message);
        Assert.Contains("gainCard", error.Message);
    }

    [Fact]
    public void Card_pool_rejects_unknown_eligible_character()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            PoolKinds = new[] { CardPoolKind.Reward },
            PoolClass = CardPoolClass.Striker,
            EligibleCharacterIds = new[] { "missing_character" },
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("missing_character", error.Message);
    }

    [Fact]
    public void Card_pool_rejects_eligible_character_from_another_class()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var character = catalog.Characters.Values.First();
        var card = catalog.Cards.Values.First() with
        {
            PoolKinds = new[] { CardPoolKind.Reward },
            PoolClass = CardPoolClass.Guardian,
            EligibleCharacterIds = new[] { character.Id },
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains(character.Id, error.Message);
        Assert.Contains("Guardian", error.Message);
    }

    [Fact]
    public void Card_pool_rejects_universal_character_restriction()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var character = catalog.Characters.Values.First();
        var card = catalog.Cards.Values.First() with
        {
            PoolKinds = new[] { CardPoolKind.Reward },
            PoolClass = CardPoolClass.Universal,
            EligibleCharacterIds = new[] { character.Id },
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("Universal", error.Message);
    }

    [Fact]
    public void Card_pool_requires_class_when_kind_is_configured()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            PoolKinds = new[] { CardPoolKind.Reward },
            PoolClass = null,
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("poolClass", error.Message);
    }

    [Fact]
    public void Embedded_catalog_is_cross_reference_valid()
        => DataCatalogIntegrityValidator.Validate(EmbeddedDataLoader.LoadCatalog());

    [Theory]
    [InlineData("futureSource", "not supported")]
    [InlineData("currentCost", "only")]
    public void Card_amount_source_must_be_supported_for_its_action(
        string amountSource, string expectedMessage)
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            Effects = new[]
            {
                new CardEffect(
                    "attack", EffectScope.Single, EffectSide.Enemy, 1,
                    AmountSource: amountSource),
            },
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains(expectedMessage, error.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("setPhase", "rage", null, 0, "phase")]
    [InlineData("augmentCards", "nonexistent", "hand", 1, "keyword")]
    [InlineData("augmentCards", "bipolar", "hand", 0, "amount")]
    [InlineData("augmentCards", "bipolar", "graveyard", 1, "pile")]
    public void Phase_and_augment_effects_are_validated(
        string action, string name, string? pile, int amount, string expectedMessage)
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            Effects = new[]
            {
                new CardEffect(action, EffectScope.Self, null, amount, Name: name, Pile: pile, Select: "all"),
            },
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains(expectedMessage, error.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Augment_card_type_filter_is_validated()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            Effects = new[]
            {
                new CardEffect("augmentCards", EffectScope.Self, null, 1, Name: "bipolar",
                    Pile: "hand", Select: "all", CardTypeFilter: "Spell"),
            },
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("cardTypeFilter", error.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Card_cost_modifier_accepts_current_cost_amount_source()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            Effects = new[]
            {
                new CardEffect(
                    "modifyCardCost", EffectScope.Self, null, 0,
                    Pile: "hand", Select: "all", AmountSource: "currentCost",
                    CostDuration: "untilPlayed"),
            },
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        DataCatalogIntegrityValidator.Validate(candidate);
    }

    [Fact]
    public void Unknown_card_reference_is_rejected()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            Effects = new[]
            {
                new CardEffect("addCard", EffectScope.Self, null, 1, CardRefId: "missing_card"),
            },
        };
        var candidate = catalog with
        {
            Cards = Replace(catalog.Cards, card.Id, card),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("missing_card", error.Message);
    }

    [Fact]
    public void Unknown_move_transition_is_rejected()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        // 埋め込み敵は全てカード制御へ移行済みのため、旧 Move 形式の敵を直接組み立てる。
        var enemy = catalog.Enemies.Values.First();
        var brokenMove = new RoguelikeCardGame.Core.Battle.Definitions.MoveDefinition(
            "a", RoguelikeCardGame.Core.Battle.Definitions.MoveKind.Attack,
            System.Array.Empty<RoguelikeCardGame.Core.Cards.CardEffect>(), "missing_move");
        var candidateEnemy = enemy with
        {
            InitialMoveId = "a",
            Moves = new[] { brokenMove },
            Deck = null,
            AiProfileId = null,
        };
        var candidate = catalog with
        {
            Enemies = Replace(catalog.Enemies, enemy.Id, candidateEnemy),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("missing_move", error.Message);
    }

    [Fact]
    public void Normal_and_upgraded_keywords_are_validated_independently()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            Keywords = new[] { "wild" },
            UpgradedKeywords = new[] { "missing_keyword" },
        };
        var candidate = catalog with
        {
            Cards = Replace(catalog.Cards, card.Id, card),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("upgraded", error.Message);
        Assert.Contains("missing_keyword", error.Message);
    }

    [Theory]
    [InlineData(-2, false)]
    [InlineData(2, true)]
    public void Opposite_status_verb_is_only_allowed_for_negative_adjustment(
        int amount, bool shouldReject)
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            Effects = new[]
            {
                new CardEffect("debuff", EffectScope.Self, null, amount, Name: "strength"),
            },
        };
        var candidate = catalog with
        {
            Cards = Replace(catalog.Cards, card.Id, card),
        };

        var error = Record.Exception(() => DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Equal(shouldReject, error is DataCatalogException);
    }

    [Fact]
    public void Virulent_poison_must_be_applied_one_stack_at_a_time()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var card = catalog.Cards.Values.First() with
        {
            Effects = new[]
            {
                new CardEffect("debuff", EffectScope.Self, null, 2,
                    Name: "virulentPoison"),
            },
        };
        var candidate = catalog with { Cards = Replace(catalog.Cards, card.Id, card) };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("one stack at a time", error.Message);
    }

    [Fact]
    public void Unit_deck_references_are_validated()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var unit = catalog.Units!.Values.First(unit => unit.IsCardControlled) with
        {
            Deck = new[] { new UnitDeckEntry("missing_cpu_card") },
        };
        var candidate = catalog with
        {
            Units = Replace(catalog.Units, unit.Id, unit),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("missing_cpu_card", error.Message);
    }

    [Fact]
    public void Unit_deck_duplicate_entries_are_rejected()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var unit = catalog.Units!.Values.First(unit => unit.IsCardControlled);
        var entry = unit.EffectiveDeck[0];
        var candidateUnit = unit with { Deck = new[] { entry, entry } };
        var candidate = catalog with
        {
            Units = Replace(catalog.Units, unit.Id, candidateUnit),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("duplicate card", error.Message);
    }

    [Fact]
    public void Unit_ai_profile_is_validated()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var unit = catalog.Units!.Values.First(unit => unit.IsCardControlled) with
        {
            AiProfileId = "missing_profile",
        };
        var candidate = catalog with
        {
            Units = Replace(catalog.Units, unit.Id, unit),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("missing_profile", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unit_item_loadout_references_are_validated(bool relic)
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var unit = catalog.Units!.Values.First(candidate => candidate.IsCardControlled) with
        {
            OwnedRelicIds = relic ? new[] { "missing_relic" } : null,
            Potions = relic ? null : new[] { "missing_potion" },
        };
        var candidate = catalog with
        {
            Units = Replace(catalog.Units, unit.Id, unit),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains(relic ? "missing_relic" : "missing_potion", error.Message);
    }

    [Fact]
    public void Unit_skill_ids_are_unique()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var skill = catalog.Characters.Values.First().EffectiveSkills.First();
        var unit = catalog.Units!.Values.First(candidate => candidate.IsCardControlled) with
        {
            Skills = new[] { skill, skill },
        };
        var candidate = catalog with
        {
            Units = Replace(catalog.Units, unit.Id, unit),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("duplicated", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unit_skill_cost_and_usage_limit_are_validated(bool invalidCost)
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var source = catalog.Characters.Values.First().EffectiveSkills.First();
        var skill = invalidCost
            ? source with { Cost = new ResourceCost(Energy: -1) }
            : source with { UsesPerTurn = -1 };
        var unit = catalog.Units!.Values.First(candidate => candidate.IsCardControlled) with
        {
            Skills = new[] { skill },
        };
        var candidate = catalog with
        {
            Units = Replace(catalog.Units, unit.Id, unit),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains(invalidCost ? "cost" : "usesPerTurn", error.Message);
    }

    [Fact]
    public void Move_controlled_unit_rejects_participant_loadout_that_would_be_unused()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var move = new MoveDefinition(
            "wait", MoveKind.Defend,
            new[] { new CardEffect("block", EffectScope.Self, null, 1) },
            "wait");
        var moveUnit = new UnitDefinition(
            "legacy_unit", "legacy", "legacy", 10,
            InitialMoveId: move.Id,
            Moves: new[] { move },
            Potions: new[] { catalog.Potions.Keys.First() });
        var candidate = catalog with
        {
            Units = Replace(catalog.Units!, moveUnit.Id, moveUnit),
        };

        var error = Assert.Throws<DataCatalogException>(() =>
            DataCatalogIntegrityValidator.Validate(candidate));

        Assert.Contains("card-controlled", error.Message);
    }

    private static IReadOnlyDictionary<string, T> Replace<T>(
        IReadOnlyDictionary<string, T> values, string id, T replacement)
    {
        var result = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        result[id] = replacement;
        return result;
    }
}
