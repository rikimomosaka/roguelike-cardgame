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

/// <summary>
/// 静のターン終了（2026-11-28 改訂）。静の位相そのものはターン終了時に何もしない。
/// 「静でターンを終えた時」の特性（転相拳士シオン）を持つキャラだけ、手札のアタックに双極を付ける。
/// 双極付きのカードは、静の持ち主なら誰でも捨てずに残る（双極の効果）。
/// </summary>
public sealed class PhaseTurnEndTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    private static BattleCardInstance Card(string defId, string instId) => new(instId, defId, false, null);

    private static BattleState WithHero(ActorPhase phase, params BattleCardInstance[] hand) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = phase }),
            hand: ImmutableArray.Create(hand));

    /// <summary>主人公（default）に「静でターンを終えた時、手札の全アタックに双極」の特性を持たせたカタログ。</summary>
    private static DataCatalog CatalogWithCalmTrait()
    {
        var trait = new CharacterTraitDefinition(
            "breath_shift", "呼吸転換", CharacterTraitTrigger.OnCalmTurnEnd,
            new[]
            {
                new CardEffect("augmentCards", EffectScope.Self, null, 1,
                    Name: "bipolar", Pile: "hand", Select: "all", CardTypeFilter: "Attack"),
            });
        var character = new CharacterDefinition(
            "default", "Hero", 70, 99, 3, new[] { "strike" }, Trait: trait);
        return BattleFixtures.MinimalCatalog() with
        {
            Characters = new Dictionary<string, CharacterDefinition> { ["default"] = character },
        };
    }

    [Fact]
    public void CalmTurnEnd_WithoutTrait_DoesNotGrantBipolar()
    {
        var (after, events) = TurnEndProcessor.Process(
            WithHero(ActorPhase.Calm, Card("strike", "s1"), Card("defend", "d1")),
            Rng(), BattleFixtures.MinimalCatalog());

        Assert.Empty(after.Hand);
        Assert.All(after.DiscardPile, card => Assert.Equal(0, card.KeywordCount("bipolar")));
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.CardAugmented);
    }

    [Fact]
    public void CalmTurnEnd_WithTrait_GrantsBipolarToHandAttacks_AndRetainsThem()
    {
        var (after, events) = TurnEndProcessor.Process(
            WithHero(ActorPhase.Calm, Card("strike", "s1"), Card("defend", "d1")),
            Rng(), CatalogWithCalmTrait());

        var kept = Assert.Single(after.Hand);
        Assert.Equal("s1", kept.InstanceId);
        Assert.Equal(1, kept.KeywordCount("bipolar"));
        Assert.Equal("d1", Assert.Single(after.DiscardPile).InstanceId);
        Assert.Contains(events, e => e.Kind == BattleEventKind.CharacterTraitTriggered && e.Note == "breath_shift");
        Assert.Contains(events, e => e.Kind == BattleEventKind.CardAugmented && e.TargetInstanceId == "s1");
    }

    [Fact]
    public void CalmTwoTurns_WithTrait_StaysAtOneBipolar_WithoutAnotherAugmentEvent()
    {
        var catalog = CatalogWithCalmTrait();
        var (once, _) = TurnEndProcessor.Process(WithHero(ActorPhase.Calm, Card("strike", "s1")), Rng(), catalog);
        var (twice, events) = TurnEndProcessor.Process(once, Rng(), catalog);

        Assert.Equal(1, Assert.Single(twice.Hand).KeywordCount("bipolar"));
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.CardAugmented);
    }

    [Fact]
    public void CalmTurnEnd_RetainsCardsThatAlreadyHaveBipolar_EvenWithoutTrait()
    {
        var (after, _) = TurnEndProcessor.Process(
            WithHero(ActorPhase.Calm, Card("strike", "s1").AddKeyword("bipolar", 1), Card("strike", "s2")),
            Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal("s1", Assert.Single(after.Hand).InstanceId);
        Assert.Equal("s2", Assert.Single(after.DiscardPile).InstanceId);
    }

    [Fact]
    public void NoneTurnEnd_DiscardsBipolarCards_ButKeepsTheirBipolar()
    {
        var (after, _) = TurnEndProcessor.Process(
            WithHero(ActorPhase.None, Card("strike", "s1").AddKeyword("bipolar", 1)),
            Rng(), CatalogWithCalmTrait());

        Assert.Empty(after.Hand);
        Assert.Equal(1, Assert.Single(after.DiscardPile).KeywordCount("bipolar"));
    }

    [Fact]
    public void MotionTurnEnd_DoesNotGrantOrRetain()
    {
        var (after, _) = TurnEndProcessor.Process(
            WithHero(ActorPhase.Motion, Card("strike", "s1")),
            Rng(), CatalogWithCalmTrait());

        Assert.Empty(after.Hand);
        Assert.Equal(0, Assert.Single(after.DiscardPile).KeywordCount("bipolar"));
    }

    [Fact]
    public void Phase_PersistsAcrossTurns()
    {
        var state = WithHero(ActorPhase.Calm, Card("strike", "s1"));

        var (after, _) = BattleEngine.EndTurn(state, Rng(), CatalogWithCalmTrait());

        Assert.Equal(2, after.Turn);
        Assert.Equal(ActorPhase.Calm, after.Allies[0].Phase);
        Assert.Equal(1, after.Hand.Single(c => c.InstanceId == "s1").KeywordCount("bipolar"));
    }

    [Fact]
    public void ProjectedParticipantTurnEnd_WithTrait_GrantsAndRetains()
    {
        var actor = BattleFixtures.Hero() with { Phase = ActorPhase.Calm };
        var projected = WithHero(ActorPhase.Calm, Card("strike", "s1"), Card("defend", "d1"));

        var after = BattleParticipantTurnLifecycle.ApplyTurnEndToProjectedRoot(
            projected, actor, CatalogWithCalmTrait());

        Assert.Equal(1, Assert.Single(after.Hand).KeywordCount("bipolar"));
        Assert.Equal("d1", Assert.Single(after.DiscardPile).InstanceId);
    }

    [Fact]
    public void RetainsAtTurnEnd_PredictsCalmAttacks_OnlyForOwnersWithTheTrait()
    {
        var strike = BattleFixtures.Strike();
        var defend = BattleFixtures.Defend();

        Assert.True(BattleCardRetention.RetainsAtTurnEnd(Card("strike", "s1"), strike, ActorPhase.Calm, ownerAugmentsAttacksInCalm: true));
        Assert.False(BattleCardRetention.RetainsAtTurnEnd(Card("strike", "s1"), strike, ActorPhase.Calm));
        Assert.False(BattleCardRetention.RetainsAtTurnEnd(Card("defend", "d1"), defend, ActorPhase.Calm, ownerAugmentsAttacksInCalm: true));
        Assert.False(BattleCardRetention.RetainsAtTurnEnd(Card("strike", "s2"), strike, ActorPhase.Motion, ownerAugmentsAttacksInCalm: true));
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
