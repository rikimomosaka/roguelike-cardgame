using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class EffectApplierPhaseTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    private static CardEffect SetPhase(string name, EffectScope scope = EffectScope.Self, EffectSide? side = null) =>
        new("setPhase", scope, side, 0, Name: name);

    private static CardEffect Augment(string select, int amount = 1, int? cardCount = null, string? filter = "Attack", string pile = "hand") =>
        new("augmentCards", EffectScope.Self, null, amount, Name: "bipolar",
            Pile: pile, Select: select, CardCount: cardCount, CardTypeFilter: filter);

    private static BattleCardInstance Card(string defId, string instId) => new(instId, defId, false, null);

    private static (BattleState State, System.Collections.Generic.IReadOnlyList<BattleEvent> Events) Apply(
        BattleState state, CardEffect effect, string? playing = null) =>
        EffectApplier.Apply(state, state.Allies[0], effect, Rng(), BattleFixtures.MinimalCatalog(),
            playingCardInstanceId: playing);

    [Fact]
    public void SetPhase_Self_Calm()
    {
        var (next, events) = Apply(BattleFixtures.MinimalState(), SetPhase("calm"));

        Assert.Equal(ActorPhase.Calm, next.Allies[0].Phase);
        var changed = Assert.Single(events);
        Assert.Equal(BattleEventKind.PhaseChanged, changed.Kind);
        Assert.Equal("none->calm:effect", changed.Note);
    }

    [Fact]
    public void SetPhase_Overwrites()
    {
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = ActorPhase.Motion }));

        var (next, _) = Apply(state, SetPhase("calm"));

        Assert.Equal(ActorPhase.Calm, next.Allies[0].Phase);
    }

    [Fact]
    public void SetPhase_SingleEnemy_None()
    {
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = ActorPhase.Calm }),
            enemies: ImmutableArray.Create(BattleFixtures.Goblin() with { Phase = ActorPhase.Motion }));

        var (next, _) = Apply(state, SetPhase("none", EffectScope.Single, EffectSide.Enemy));

        Assert.Equal(ActorPhase.None, next.Enemies[0].Phase);
        Assert.Equal(ActorPhase.Calm, next.Allies[0].Phase);
    }

    [Fact]
    public void InvertPhase_Calm_ToMotion()
    {
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = ActorPhase.Calm }));

        var (next, events) = Apply(state, new CardEffect("invertPhase", EffectScope.Self, null, 0));

        Assert.Equal(ActorPhase.Motion, next.Allies[0].Phase);
        Assert.Equal("calm->motion:effect", Assert.Single(events).Note);
    }

    [Fact]
    public void InvertPhase_None_NoEvent()
    {
        var (next, events) = Apply(BattleFixtures.MinimalState(), new CardEffect("invertPhase", EffectScope.Self, null, 0));

        Assert.Equal(ActorPhase.None, next.Allies[0].Phase);
        Assert.Empty(events);
    }

    [Fact]
    public void AugmentCards_AllAttacksInHand()
    {
        var state = BattleFixtures.MinimalState(hand: ImmutableArray.Create(
            Card("strike", "s1"), Card("defend", "d1"), Card("strike", "s2")));

        var (next, events) = Apply(state, Augment("all"));

        Assert.Equal(new[] { 1, 0, 1 }, next.Hand.Select(c => c.KeywordCount("bipolar")));
        Assert.Equal(2, events.Count(e => e.Kind == BattleEventKind.CardAugmented));
        var first = events.First(e => e.Kind == BattleEventKind.CardAugmented);
        Assert.Equal("s1", first.TargetInstanceId);
        Assert.Equal(1, first.Amount);
        Assert.Equal("bipolar:1", first.Note);
    }

    [Fact]
    public void AugmentCards_ExcludesThePlayingCardFromHandCandidates()
    {
        var state = BattleFixtures.MinimalState(hand: ImmutableArray.Create(
            Card("strike", "playing"), Card("strike", "s2")));

        var (next, _) = Apply(state, Augment("all"), playing: "playing");

        Assert.Equal(new[] { 0, 1 }, next.Hand.Select(c => c.KeywordCount("bipolar")));
    }

    [Fact]
    public void AugmentCards_Self_TargetsPlayedCard()
    {
        var state = BattleFixtures.MinimalState(hand: ImmutableArray.Create(
            Card("strike", "playing"), Card("strike", "s2")));

        var (next, _) = Apply(state, Augment("all", amount: 2, filter: null, pile: "self"), playing: "playing");

        Assert.Equal(new[] { 2, 0 }, next.Hand.Select(c => c.KeywordCount("bipolar")));
    }

    [Fact]
    public void AugmentCards_Choose_PausesWithPendingChoice_ThenAppliesToChosen()
    {
        var honing = new CardDefinition("honing", "Honing", null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] { Augment("choose", cardCount: 1) },
            UpgradedEffects: null, Keywords: null);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { honing, BattleFixtures.Strike(), BattleFixtures.Defend() });
        var state = BattleFixtures.MinimalState(hand: ImmutableArray.Create(
            Card("honing", "h1"), Card("strike", "s1"), Card("defend", "d1"), Card("strike", "s2")));

        var (paused, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.NotNull(paused.PendingCardPlay);
        Assert.Equal(new[] { "s1", "s2" }, paused.PendingCardPlay!.Choice.CandidateInstanceIds.AsEnumerable());

        var (resolved, events) = BattleEngine.ResolveCardChoice(paused, ImmutableArray.Create("s2"), Rng(), catalog);

        Assert.Null(resolved.PendingCardPlay);
        Assert.Equal(1, resolved.Hand.Single(c => c.InstanceId == "s2").KeywordCount("bipolar"));
        Assert.Equal(0, resolved.Hand.Single(c => c.InstanceId == "s1").KeywordCount("bipolar"));
        Assert.Contains(events, e => e.Kind == BattleEventKind.CardAugmented && e.TargetInstanceId == "s2");
    }
}
