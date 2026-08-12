using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class EffectApplierModifyCardCostTests
{
    private static DataCatalog Catalog(params CardDefinition[] cards) =>
        BattleFixtures.MinimalCatalog(cards: cards.Length == 0 ? null : cards);

    private static BattleCardInstance Card(string instanceId, string definitionId = "strike") =>
        new(instanceId, definitionId, false, CostOverride: null);

    private static CardEffect Modify(
        int amount = -1,
        string? pile = "hand",
        string? select = "all",
        string? duration = "untilPlayed",
        int? cardCount = null,
        string? trigger = null) =>
        new(
            "modifyCardCost", EffectScope.Self, Side: null, Amount: amount,
            Pile: pile, Select: select, Trigger: trigger,
            CostDuration: duration, CardCount: cardCount);

    private static CombatSource CardSource(string instanceId = "source-card") =>
        new(
            CombatSourceKind.CardEffect,
            OwnerSlot: 0,
            OwnerInstanceId: "hero_inst",
            ActorInstanceId: "hero_inst",
            DefinitionId: "modifier-card",
            InstanceId: instanceId,
            EffectIndex: 0,
            Scope: "self");

    private static BattleState WithPile(
        string pile, ImmutableArray<BattleCardInstance> cards)
    {
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray<BattleCardInstance>.Empty,
            draw: ImmutableArray<BattleCardInstance>.Empty,
            discard: ImmutableArray<BattleCardInstance>.Empty) with
        {
            ExhaustPile = ImmutableArray<BattleCardInstance>.Empty,
        };
        return pile switch
        {
            "hand" => state with { Hand = cards },
            "draw" => state with { DrawPile = cards },
            "discard" => state with { DiscardPile = cards },
            "exhaust" => state with { ExhaustPile = cards },
            _ => throw new ArgumentOutOfRangeException(nameof(pile)),
        };
    }

    private static ImmutableArray<BattleCardInstance> Pile(BattleState state, string pile) =>
        pile switch
        {
            "hand" => state.Hand,
            "draw" => state.DrawPile,
            "discard" => state.DiscardPile,
            "exhaust" => state.ExhaustPile,
            _ => throw new ArgumentOutOfRangeException(nameof(pile)),
        };

    [Theory]
    [InlineData("hand")]
    [InlineData("draw")]
    [InlineData("discard")]
    [InlineData("exhaust")]
    public void All_appends_modifier_in_pile_order_without_moving_cards_and_preserves_source(
        string pile)
    {
        var cards = ImmutableArray.Create(Card("a"), Card("b"));
        var state = WithPile(pile, cards);
        var source = CardSource();
        var rng = new RecordingRng();

        var (next, events) = EffectApplier.Apply(
            state, state.Allies[0],
            Modify(amount: -2, pile: pile, duration: "untilTurnEnd"),
            rng, Catalog(), source);

        Assert.Equal(new[] { "a", "b" }, Pile(next, pile).Select(c => c.InstanceId));
        foreach (var card in Pile(next, pile))
        {
            var modifier = Assert.Single(card.EffectiveCostModifiers);
            Assert.Equal(-2, modifier.Amount);
            Assert.Equal(CardCostModifierDuration.UntilTurnEnd, modifier.Duration);
            Assert.Equal(source.InstanceId, modifier.SourceId);
        }
        Assert.Collection(events,
            e => AssertModifyEvent(e, order: 0, target: "a", -2,
                $"untilTurnEnd:{pile}", source),
            e => AssertModifyEvent(e, order: 1, target: "b", -2,
                $"untilTurnEnd:{pile}", source));
        Assert.Empty(rng.IntCalls);
    }

    [Fact]
    public void Modifier_is_appended_after_existing_card_modifiers()
    {
        var existing = new CardCostModifier(
            +2, CardCostModifierDuration.UntilPlayed, "earlier");
        var card = Card("target").AddCostModifier(existing);
        var state = WithPile("discard", ImmutableArray.Create(card));

        var (next, _) = EffectApplier.Apply(
            state, state.Allies[0], Modify(amount: -1, pile: "discard"),
            new RecordingRng(), Catalog(), CardSource());

        var modifiers = Assert.Single(next.DiscardPile).EffectiveCostModifiers;
        Assert.Equal(2, modifiers.Length);
        Assert.Same(existing, modifiers[0]);
        Assert.Equal(-1, modifiers[1].Amount);
    }

    [Fact]
    public void Hand_selection_excludes_the_currently_playing_card()
    {
        var state = WithPile(
            "hand", ImmutableArray.Create(Card("playing"), Card("other")));

        var (next, events) = EffectApplier.Apply(
            state, state.Allies[0], Modify(pile: "hand"),
            new RecordingRng(), Catalog(), CardSource("playing"),
            playingCardInstanceId: "playing");

        Assert.Empty(next.Hand[0].EffectiveCostModifiers);
        Assert.Single(next.Hand[1].EffectiveCostModifiers);
        Assert.Equal("other", Assert.Single(events).TargetInstanceId);
    }

    [Fact]
    public void Card_source_without_play_resolution_context_does_not_exclude_its_instance()
    {
        var state = WithPile(
            "hand", ImmutableArray.Create(Card("source-card"), Card("other")));

        var (next, events) = EffectApplier.Apply(
            state, state.Allies[0], Modify(pile: "hand"),
            new RecordingRng(), Catalog(), CardSource("source-card"));

        Assert.All(next.Hand, card => Assert.Single(card.EffectiveCostModifiers));
        Assert.Equal(
            new[] { "source-card", "other" },
            events.Select(e => e.TargetInstanceId));
    }

    [Fact]
    public void Random_defaults_card_count_to_one_and_consumes_one_rng_value()
    {
        var state = WithPile(
            "draw", ImmutableArray.Create(Card("a"), Card("b"), Card("c")));
        var rng = new RecordingRng(1);

        var (next, events) = EffectApplier.Apply(
            state, state.Allies[0],
            Modify(amount: +2, pile: "draw", select: "random", cardCount: null),
            rng, Catalog(), CardSource());

        Assert.Empty(next.DrawPile[0].EffectiveCostModifiers);
        Assert.Single(next.DrawPile[1].EffectiveCostModifiers);
        Assert.Empty(next.DrawPile[2].EffectiveCostModifiers);
        Assert.Equal("b", Assert.Single(events).TargetInstanceId);
        Assert.Collection(rng.IntCalls,
            call => Assert.Equal((0, 3), call));
    }

    [Fact]
    public void Random_selects_without_replacement_in_rng_order_and_keeps_pile_order()
    {
        var state = WithPile(
            "discard", ImmutableArray.Create(Card("a"), Card("b"), Card("c")));
        var rng = new RecordingRng(2, 0);

        var (next, events) = EffectApplier.Apply(
            state, state.Allies[0],
            Modify(pile: "discard", select: "random", cardCount: 2),
            rng, Catalog(), CardSource());

        Assert.Equal(new[] { "a", "b", "c" },
            next.DiscardPile.Select(c => c.InstanceId));
        Assert.Single(next.DiscardPile[0].EffectiveCostModifiers);
        Assert.Empty(next.DiscardPile[1].EffectiveCostModifiers);
        Assert.Single(next.DiscardPile[2].EffectiveCostModifiers);
        Assert.Equal(new[] { "c", "a" }, events.Select(e => e.TargetInstanceId));
        Assert.Collection(rng.IntCalls,
            call => Assert.Equal((0, 3), call),
            call => Assert.Equal((0, 2), call));
    }

    [Fact]
    public void Modify_choose_uses_card_count_for_pending_and_excludes_playing_card()
    {
        var state = WithPile(
            "hand", ImmutableArray.Create(
                Card("a"), Card("b"), Card("c"), Card("playing")));
        var effect = Modify(
            amount: -5, pile: "hand", select: "choose", cardCount: 2);

        Assert.True(EffectApplier.NeedsPlayerChoice(
            state, effect, Catalog(), playingCardInstanceId: "playing"));
        var pending = EffectApplier.BuildPendingChoice(
            state, effect, Catalog(), playingCardInstanceId: "playing");

        Assert.Equal(2, pending.Count);
        Assert.Equal(new[] { "a", "b", "c" }, pending.CandidateInstanceIds);
    }

    [Fact]
    public void Existing_choose_actions_continue_to_use_amount_as_selection_count()
    {
        var state = WithPile(
            "hand", ImmutableArray.Create(Card("a"), Card("b"), Card("c")));
        var effect = new CardEffect(
            "exhaustCard", EffectScope.Self, Side: null, Amount: 2,
            Pile: "hand", Select: "choose", CardCount: 1);

        Assert.True(EffectApplier.NeedsPlayerChoice(state, effect, Catalog()));
        Assert.Equal(2, EffectApplier.BuildPendingChoice(
            state, effect, Catalog()).Count);
    }

    [Fact]
    public void Choose_pauses_then_applies_selected_cards_in_submitted_order_without_rng()
    {
        var effect = Modify(
            amount: -2, pile: "hand", select: "choose",
            duration: "untilTurnEnd", cardCount: 2);
        var modifyCard = new CardDefinition(
            "modifier-card", "Modifier", null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] { effect }, UpgradedEffects: null, Keywords: null);
        var hand = ImmutableArray.Create(
            Card("a"), Card("b"), Card("c"), Card("playing", modifyCard.Id));
        var state = BattleFixtures.MinimalState(hand: hand);
        var catalog = Catalog(BattleFixtures.Strike(), modifyCard);
        var rng = new RecordingRng();

        var (paused, _) = BattleEngine.PlayCard(
            state, handIndex: 3, null, null, rng, catalog);

        Assert.NotNull(paused.PendingCardPlay);
        Assert.Equal(2, paused.PendingCardPlay!.Choice.Count);
        Assert.Equal(new[] { "a", "b", "c" },
            paused.PendingCardPlay.Choice.CandidateInstanceIds);

        var (final, events) = BattleEngine.ResolveCardChoice(
            paused, ImmutableArray.Create("c", "a"), rng, catalog);

        Assert.Null(final.PendingCardPlay);
        Assert.Equal(new[] { "a", "b", "c" }, final.Hand.Select(c => c.InstanceId));
        Assert.Single(final.Hand[0].EffectiveCostModifiers);
        Assert.Empty(final.Hand[1].EffectiveCostModifiers);
        Assert.Single(final.Hand[2].EffectiveCostModifiers);
        var modifyEvents = events
            .Where(e => e.Kind == BattleEventKind.ModifyCardCost)
            .ToArray();
        Assert.Equal(new[] { "c", "a" },
            modifyEvents.Select(e => e.TargetInstanceId));
        Assert.All(modifyEvents, e =>
        {
            Assert.Equal(-2, e.Amount);
            Assert.Equal("untilTurnEnd:hand", e.Note);
            Assert.NotNull(e.Source);
            Assert.Equal(CombatSourceKind.CardEffect, e.Source!.Kind);
            Assert.Equal(modifyCard.Id, e.Source.DefinitionId);
            Assert.Equal("playing", e.Source.InstanceId);
            Assert.Equal(0, e.Source.EffectIndex);
        });
        Assert.Equal("playing", final.Hand[0].EffectiveCostModifiers[0].SourceId);
        Assert.Equal("playing", final.Hand[2].EffectiveCostModifiers[0].SourceId);
        Assert.Empty(rng.IntCalls);
    }

    [Fact]
    public void Choose_auto_applies_all_candidates_when_no_decision_is_needed_without_rng()
    {
        var effect = Modify(
            pile: "hand", select: "choose", cardCount: 3);
        var modifyCard = new CardDefinition(
            "modifier-card", "Modifier", null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] { effect }, UpgradedEffects: null, Keywords: null);
        var hand = ImmutableArray.Create(
            Card("a"), Card("b"), Card("playing", modifyCard.Id));
        var state = BattleFixtures.MinimalState(hand: hand);
        var catalog = Catalog(BattleFixtures.Strike(), modifyCard);
        var rng = new RecordingRng();

        var (next, events) = BattleEngine.PlayCard(
            state, handIndex: 2, null, null, rng, catalog);

        Assert.Null(next.PendingCardPlay);
        Assert.Equal(new[] { "a", "b" }, next.Hand.Select(c => c.InstanceId));
        Assert.All(next.Hand, c => Assert.Single(c.EffectiveCostModifiers));
        Assert.Equal(new[] { "a", "b" }, events
            .Where(e => e.Kind == BattleEventKind.ModifyCardCost)
            .Select(e => e.TargetInstanceId));
        Assert.Empty(rng.IntCalls);
    }

    [Fact]
    public void Chosen_path_preserves_an_explicit_combat_source()
    {
        var state = WithPile(
            "discard", ImmutableArray.Create(Card("a"), Card("b")));
        var effect = Modify(
            amount: +1, pile: "discard", select: "choose", cardCount: 1);
        var source = CardSource();

        var (next, events) = EffectApplier.ApplyChoseEffect(
            state, state.Allies[0], effect, ImmutableArray.Create("b"),
            new RecordingRng(), Catalog(), source);

        Assert.Empty(next.DiscardPile[0].EffectiveCostModifiers);
        var modifier = Assert.Single(next.DiscardPile[1].EffectiveCostModifiers);
        Assert.Equal(source.InstanceId, modifier.SourceId);
        Assert.Same(source, Assert.Single(events).Source);
    }

    [Fact]
    public void Triggered_choose_is_rejected_instead_of_creating_pending_state()
    {
        var state = WithPile("hand", ImmutableArray.Create(Card("a")));
        var effect = Modify(
            pile: "hand", select: "choose", cardCount: 1,
            trigger: "OnTurnStart");
        var source = new CombatSource(
            CombatSourceKind.PowerEffect, 0, "hero_inst", "hero_inst",
            DefinitionId: "power", InstanceId: "power-instance", EffectIndex: 0);

        Assert.Throws<InvalidOperationException>(() => EffectApplier.Apply(
            state, state.Allies[0], effect,
            new RecordingRng(), Catalog(), source));
    }

    public static IEnumerable<object[]> InvalidEffects()
    {
        yield return new object[] { Modify(amount: 0) };
        yield return new object[] { Modify(duration: null) };
        yield return new object[] { Modify(duration: "forever") };
        yield return new object[] { Modify(pile: null) };
        yield return new object[] { Modify(pile: "power") };
        yield return new object[] { Modify(select: null) };
        yield return new object[] { Modify(select: "first") };
        yield return new object[] { Modify(select: "random", cardCount: 0) };
        yield return new object[] { Modify(select: "choose", cardCount: -1) };
        yield return new object[] { Modify() with { Scope = EffectScope.Random } };
    }

    [Theory]
    [MemberData(nameof(InvalidEffects))]
    public void Invalid_configuration_is_rejected(CardEffect effect)
    {
        var state = WithPile("hand", ImmutableArray.Create(Card("a")));

        Assert.Throws<InvalidOperationException>(() => EffectApplier.Apply(
            state, state.Allies[0], effect,
            new RecordingRng(), Catalog(), CardSource()));
    }

    private static void AssertModifyEvent(
        BattleEvent battleEvent, int order, string target, int amount,
        string note, CombatSource source)
    {
        Assert.Equal(BattleEventKind.ModifyCardCost, battleEvent.Kind);
        Assert.Equal(order, battleEvent.Order);
        Assert.Equal("hero_inst", battleEvent.CasterInstanceId);
        Assert.Equal(target, battleEvent.TargetInstanceId);
        Assert.Equal(amount, battleEvent.Amount);
        Assert.Equal(note, battleEvent.Note);
        Assert.Same(source, battleEvent.Source);
    }

    private sealed class RecordingRng : IRng
    {
        private readonly Queue<int> _values;

        public RecordingRng(params int[] values) => _values = new Queue<int>(values);

        public List<(int MinInclusive, int MaxExclusive)> IntCalls { get; } = new();

        public int NextInt(int minInclusive, int maxExclusive)
        {
            IntCalls.Add((minInclusive, maxExclusive));
            if (_values.Count == 0)
                throw new InvalidOperationException("Unexpected RNG call.");
            int value = _values.Dequeue();
            if (value < minInclusive || value >= maxExclusive)
                throw new InvalidOperationException(
                    $"RNG value {value} is outside [{minInclusive}, {maxExclusive}).");
            return value;
        }

        public double NextDouble() =>
            throw new InvalidOperationException("Unexpected RNG call.");
    }
}
