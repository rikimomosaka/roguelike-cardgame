using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>静のターン終了: 手札のアタックに双極を付け、双極付きは捨てずに残す。</summary>
public sealed class PhaseTurnEndTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    private static BattleCardInstance Card(string defId, string instId) => new(instId, defId, false, null);

    private static BattleState WithHero(ActorPhase phase, params BattleCardInstance[] hand) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = phase }),
            hand: ImmutableArray.Create(hand));

    [Fact]
    public void CalmTurnEnd_GrantsBipolarToHandAttacks_AndRetainsThem()
    {
        var (after, events) = TurnEndProcessor.Process(
            WithHero(ActorPhase.Calm, Card("strike", "s1"), Card("defend", "d1")),
            Rng(), BattleFixtures.MinimalCatalog());

        var kept = Assert.Single(after.Hand);
        Assert.Equal("s1", kept.InstanceId);
        Assert.Equal(1, kept.KeywordCount("bipolar"));
        Assert.Equal("d1", Assert.Single(after.DiscardPile).InstanceId);
        Assert.Contains(events, e => e.Kind == BattleEventKind.CardAugmented && e.TargetInstanceId == "s1");
    }

    [Fact]
    public void CalmTwoTurns_StacksToBipolar2()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var (once, _) = TurnEndProcessor.Process(WithHero(ActorPhase.Calm, Card("strike", "s1")), Rng(), catalog);
        var (twice, _) = TurnEndProcessor.Process(once, Rng(), catalog);

        Assert.Equal(2, Assert.Single(twice.Hand).KeywordCount("bipolar"));
    }

    [Fact]
    public void NoneTurnEnd_DiscardsBipolarCards_ButKeepsTheirBipolar()
    {
        var (after, _) = TurnEndProcessor.Process(
            WithHero(ActorPhase.None, Card("strike", "s1").AddKeyword("bipolar", 1)),
            Rng(), BattleFixtures.MinimalCatalog());

        Assert.Empty(after.Hand);
        Assert.Equal(1, Assert.Single(after.DiscardPile).KeywordCount("bipolar"));
    }

    [Fact]
    public void MotionTurnEnd_DoesNotGrantOrRetain()
    {
        var (after, _) = TurnEndProcessor.Process(
            WithHero(ActorPhase.Motion, Card("strike", "s1")),
            Rng(), BattleFixtures.MinimalCatalog());

        Assert.Empty(after.Hand);
        Assert.Equal(0, Assert.Single(after.DiscardPile).KeywordCount("bipolar"));
    }

    [Fact]
    public void Phase_PersistsAcrossTurns()
    {
        var state = WithHero(ActorPhase.Calm, Card("strike", "s1"));

        var (after, _) = BattleEngine.EndTurn(state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(2, after.Turn);
        Assert.Equal(ActorPhase.Calm, after.Allies[0].Phase);
        Assert.Equal(1, after.Hand.Single(c => c.InstanceId == "s1").KeywordCount("bipolar"));
    }

    [Fact]
    public void ProjectedParticipantTurnEnd_GrantsAndRetains()
    {
        var actor = BattleFixtures.Hero() with { Phase = ActorPhase.Calm };
        var projected = WithHero(ActorPhase.Calm, Card("strike", "s1"), Card("defend", "d1"));

        var after = BattleParticipantTurnLifecycle.ApplyTurnEndToProjectedRoot(
            projected, actor, BattleFixtures.MinimalCatalog());

        Assert.Equal(1, Assert.Single(after.Hand).KeywordCount("bipolar"));
        Assert.Equal("d1", Assert.Single(after.DiscardPile).InstanceId);
    }

    [Fact]
    public void ShouldRetain_RequiresCalmOwnerAndBipolar()
    {
        var strike = BattleFixtures.Strike();
        var bipolar = Card("strike", "s1").AddKeyword("bipolar", 1);

        Assert.True(BattleCardRetention.ShouldRetain(bipolar, strike, ActorPhase.Calm));
        Assert.False(BattleCardRetention.ShouldRetain(bipolar, strike, ActorPhase.Motion));
        Assert.False(BattleCardRetention.ShouldRetain(Card("strike", "s2"), strike, ActorPhase.Calm));
    }
}
