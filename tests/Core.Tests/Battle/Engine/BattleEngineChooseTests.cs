using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// Phase 10.5.M2-Choose Task 3: PlayCard で choose effect 検出時の pause emit 確認。
/// </summary>
public class BattleEngineChooseTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    /// <summary>"exhaustCard" + Select=choose + Amount=N + Pile=hand を持つ最小カード定義。</summary>
    private static CardDefinition ExhaustChooseCard(string id = "exhaust_choose", int amount = 1) =>
        new(id, id, null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("exhaustCard", EffectScope.Self, null, amount,
                               Pile: "hand", Select: "choose")
            },
            UpgradedEffects: null, Keywords: null);

    [Fact]
    public void PlayCard_ExhaustCardChoose_HandHasMoreThanAmount_PausesWithPending()
    {
        // Arrange: choose card + 3 other cards in hand (4 total, exhaust 1 → candidates 4 > 1, pause)
        var chooseDef = ExhaustChooseCard(amount: 1);
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "s1"),
            BattleFixtures.MakeBattleCard("defend", "d1"),
            BattleFixtures.MakeBattleCard("strike", "s2"),
            BattleFixtures.MakeBattleCard(chooseDef.Id, "ec1"));
        var state = BattleFixtures.MinimalState(hand: hand);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), chooseDef });

        // Act: play the choose card (handIndex 3)
        var (next, _) = BattleEngine.PlayCard(state, handIndex: 3, null, null, Rng(), catalog);

        // Assert
        Assert.NotNull(next.PendingCardPlay);
        var pending = next.PendingCardPlay!;
        Assert.Equal("ec1", pending.CardInstanceId);
        Assert.Equal(0, pending.EffectIndex);
        Assert.Equal("exhaustCard", pending.Choice.Action);
        Assert.Equal("hand", pending.Choice.Pile);
        Assert.Equal(1, pending.Choice.Count);
        // Hand 4 cards (including the choose card itself) all candidates
        Assert.Equal(4, pending.Choice.CandidateInstanceIds.Length);
        // Card not yet exhausted (still in hand) and not moved (pause emit before card-move logic)
        Assert.Contains(next.Hand, c => c.InstanceId == "ec1");
    }

    /// <summary>"recoverFromDiscard" + Select=choose を持つ最小カード定義。Pile=hand に戻す。</summary>
    private static CardDefinition RecoverChooseCard(string id = "recover_choose", int amount = 1) =>
        new(id, id, null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("recoverFromDiscard", EffectScope.Self, null, amount,
                               Pile: "hand", Select: "choose")
            },
            UpgradedEffects: null, Keywords: null);

    [Fact]
    public void PlayCard_RecoverFromDiscardChoose_DiscardSizeAtAmount_AutoSkipsToRandomFallback()
    {
        // candidates == Amount (or less) → no pending, fall through to random fallback.
        // discard pile に 1 枚、Amount=2 → 1 <= 2 → auto-skip random で 1 枚回収。
        var chooseDef = RecoverChooseCard(amount: 2);
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard(chooseDef.Id, "rc1"));
        var discard = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "s_old"));
        var state = BattleFixtures.MinimalState(hand: hand, discard: discard);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), chooseDef });

        // Random fallback の rng は使わない可能性があるが、念のため index=0 を返す Rng を用意。
        var rng = new FakeRng(new[] { 0 }, new double[0]);
        var (next, _) = BattleEngine.PlayCard(state, handIndex: 0, null, null, rng, catalog);

        Assert.Null(next.PendingCardPlay);
        // 1 枚回収後 discard は空 (auto-skip random 経路が通った証拠)。
        Assert.Empty(next.DiscardPile.Where(c => c.InstanceId == "s_old"));
    }

    [Fact]
    public void PlayCard_RegularCard_NoPending()
    {
        // Regression: Strike (no choose) → no pending
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "s1")));
        var catalog = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(state, handIndex: 0, null, null, Rng(), catalog);
        Assert.Null(next.PendingCardPlay);
    }

    [Fact]
    public void PlayCard_WhilePendingSet_Throws()
    {
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "s1"))) with
        {
            PendingCardPlay = new PendingCardPlay(
                "card_inst_x", 0, false,
                new PendingChoice("exhaustCard", "hand", 1, ImmutableArray<string>.Empty)),
        };
        var catalog = BattleFixtures.MinimalCatalog();
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.PlayCard(state, handIndex: 0, null, null, Rng(), catalog));
    }
}
