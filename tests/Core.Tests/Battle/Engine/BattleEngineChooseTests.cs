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
        // Final review I-1 fix: プレイ中のカード自身 (ec1) は candidate から除外されるため、
        //   hand 4 枚中 candidate は他 3 枚 (s1 / d1 / s2) のみ。
        Assert.Equal(3, pending.Choice.CandidateInstanceIds.Length);
        Assert.DoesNotContain("ec1", pending.Choice.CandidateInstanceIds);
        // Card not yet exhausted (still in hand) and not moved (pause emit before card-move logic)
        Assert.Contains(next.Hand, c => c.InstanceId == "ec1");
    }

    /// <summary>"recoverFromDiscard" + Select=choose を持つ最小カード定義。Pile (移動先) は省略時 hand。</summary>
    private static CardDefinition RecoverChooseCard(string id = "recover_choose", int amount = 1, string destPile = "hand") =>
        new(id, id, null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("recoverFromDiscard", EffectScope.Self, null, amount,
                               Pile: destPile, Select: "choose")
            },
            UpgradedEffects: null, Keywords: null);

    /// <summary>"discard" + Select=choose を持つ最小カード定義。</summary>
    private static CardDefinition DiscardChooseCard(string id = "discard_choose", int amount = 1) =>
        new(id, id, null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("discard", EffectScope.Self, null, amount,
                               Pile: "hand", Select: "choose")
            },
            UpgradedEffects: null, Keywords: null);

    /// <summary>"upgrade" + Select=choose を持つ最小カード定義。</summary>
    private static CardDefinition UpgradeChooseCard(string id = "upgrade_choose", int amount = 1) =>
        new(id, id, null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("upgrade", EffectScope.Self, null, amount,
                               Pile: "hand", Select: "choose")
            },
            UpgradedEffects: null, Keywords: null);

    /// <summary>2 つの choose effect を持つカード (sequential pause 検証用)。</summary>
    private static CardDefinition DoubleChooseCard(string id = "double_choose") =>
        new(id, id, null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("exhaustCard", EffectScope.Self, null, 1,
                               Pile: "hand", Select: "choose"),
                new CardEffect("exhaustCard", EffectScope.Self, null, 1,
                               Pile: "discard", Select: "choose"),
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

    // ===== Phase 10.5.M2-Choose T4: ResolveCardChoice tests =====

    [Fact]
    public void ResolveCardChoice_NoPending_Throws()
    {
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "s1")));
        var catalog = BattleFixtures.MinimalCatalog();
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.ResolveCardChoice(state, ImmutableArray.Create("any"), Rng(), catalog));
    }

    [Fact]
    public void ResolveCardChoice_WrongCount_Throws()
    {
        var chooseDef = ExhaustChooseCard(amount: 1);
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "s1"),
            BattleFixtures.MakeBattleCard("defend", "d1"),
            BattleFixtures.MakeBattleCard(chooseDef.Id, "ec1"));
        var state = BattleFixtures.MinimalState(hand: hand);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), chooseDef });
        var (paused, _) = BattleEngine.PlayCard(state, handIndex: 2, null, null, Rng(), catalog);
        Assert.NotNull(paused.PendingCardPlay);

        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.ResolveCardChoice(paused,
                ImmutableArray.Create("s1", "d1"), Rng(), catalog));
    }

    [Fact]
    public void ResolveCardChoice_NotInCandidates_Throws()
    {
        var chooseDef = ExhaustChooseCard(amount: 1);
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "s1"),
            BattleFixtures.MakeBattleCard("defend", "d1"),
            BattleFixtures.MakeBattleCard(chooseDef.Id, "ec1"));
        var state = BattleFixtures.MinimalState(hand: hand);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), chooseDef });
        var (paused, _) = BattleEngine.PlayCard(state, handIndex: 2, null, null, Rng(), catalog);

        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.ResolveCardChoice(paused, ImmutableArray.Create("nonexistent_inst"), Rng(), catalog));
    }

    [Fact]
    public void ResolveCardChoice_Success_AppliesAndCompletes()
    {
        var chooseDef = ExhaustChooseCard(amount: 1);
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "s1"),
            BattleFixtures.MakeBattleCard("defend", "d1"),
            BattleFixtures.MakeBattleCard(chooseDef.Id, "ec1"));
        var state = BattleFixtures.MinimalState(hand: hand);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), chooseDef });
        var (paused, _) = BattleEngine.PlayCard(state, handIndex: 2, null, null, Rng(), catalog);

        var (final, events) = BattleEngine.ResolveCardChoice(paused,
            ImmutableArray.Create("s1"), Rng(), catalog);

        Assert.Null(final.PendingCardPlay);
        // s1 (strike) was exhausted
        Assert.Contains(final.ExhaustPile, c => c.InstanceId == "s1");
        Assert.DoesNotContain(final.Hand, c => c.InstanceId == "s1");
        // ec1 (the choose card itself, Skill type, no exhaustSelf) → discard
        Assert.Contains(final.DiscardPile, c => c.InstanceId == "ec1");
        // d1 still in hand
        Assert.Contains(final.Hand, c => c.InstanceId == "d1");
        // events include exhaust (relic events not asserted: fixture has no relics)
        Assert.Contains(events, e => e.Kind == BattleEventKind.Exhaust);
    }

    [Fact]
    public void ResolveCardChoice_DiscardChosen_MovesSelectedToDiscard()
    {
        var chooseDef = DiscardChooseCard(amount: 1);
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "s1"),
            BattleFixtures.MakeBattleCard("defend", "d1"),
            BattleFixtures.MakeBattleCard(chooseDef.Id, "dc1"));
        var state = BattleFixtures.MinimalState(hand: hand);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), chooseDef });
        var (paused, _) = BattleEngine.PlayCard(state, handIndex: 2, null, null, Rng(), catalog);
        Assert.NotNull(paused.PendingCardPlay);
        Assert.Equal("discard", paused.PendingCardPlay!.Choice.Action);

        var (final, events) = BattleEngine.ResolveCardChoice(paused,
            ImmutableArray.Create("d1"), Rng(), catalog);

        Assert.Null(final.PendingCardPlay);
        // d1 (defend) discarded
        Assert.Contains(final.DiscardPile, c => c.InstanceId == "d1");
        Assert.DoesNotContain(final.Hand, c => c.InstanceId == "d1");
        // s1 still in hand
        Assert.Contains(final.Hand, c => c.InstanceId == "s1");
        // dc1 (choose card) → discard
        Assert.Contains(final.DiscardPile, c => c.InstanceId == "dc1");
        // Discard event emitted
        Assert.Contains(events, e => e.Kind == BattleEventKind.Discard);
    }

    [Fact]
    public void ResolveCardChoice_UpgradeChosen_UpgradesSelectedCard()
    {
        // Strike has UpgradedCost or UpgradedEffects? Cost 1, UpgradedCost null, UpgradedEffects null
        // → IsUpgradable false. So Strike is NOT a candidate.
        // Define a custom upgradable card for the test. We need >= 2 upgradable instances in hand
        // (excluding the choose card) so that candidates > Amount (=1) and pause fires.
        var upgradable = new CardDefinition(
            "upcard", "upcard", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 9) },
            Keywords: null);
        var chooseDef = UpgradeChooseCard(amount: 1);
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard(upgradable.Id, "u1"),
            BattleFixtures.MakeBattleCard(upgradable.Id, "u2"),
            BattleFixtures.MakeBattleCard(chooseDef.Id, "uc1"));
        var state = BattleFixtures.MinimalState(hand: hand);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { upgradable, chooseDef });
        var (paused, _) = BattleEngine.PlayCard(state, handIndex: 2, null, null, Rng(), catalog);
        Assert.NotNull(paused.PendingCardPlay);
        Assert.Equal("upgrade", paused.PendingCardPlay!.Choice.Action);
        // u1 / u2 should be candidates (upgradable, not yet upgraded). uc1 (choose card) is NOT
        // a candidate after C1 fix (no UpgradedCost / UpgradedEffects).
        Assert.Contains("u1", paused.PendingCardPlay.Choice.CandidateInstanceIds);
        Assert.Contains("u2", paused.PendingCardPlay.Choice.CandidateInstanceIds);
        Assert.DoesNotContain("uc1", paused.PendingCardPlay.Choice.CandidateInstanceIds);

        var (final, events) = BattleEngine.ResolveCardChoice(paused,
            ImmutableArray.Create("u1"), Rng(), catalog);

        Assert.Null(final.PendingCardPlay);
        // u1 still in hand but now IsUpgraded
        var u1Final = final.Hand.FirstOrDefault(c => c.InstanceId == "u1");
        Assert.NotNull(u1Final);
        Assert.True(u1Final!.IsUpgraded);
        // u2 still in hand, not upgraded
        var u2Final = final.Hand.FirstOrDefault(c => c.InstanceId == "u2");
        Assert.NotNull(u2Final);
        Assert.False(u2Final!.IsUpgraded);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Upgrade);
    }

    [Fact]
    public void ResolveCardChoice_RecoverFromDiscardChosen_MovesSelectedToHand()
    {
        var chooseDef = RecoverChooseCard(amount: 1, destPile: "hand");
        // Hand has the choose card. Discard pile has 2 candidates.
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard(chooseDef.Id, "rc1"));
        var discardPile = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "s_disc1"),
            BattleFixtures.MakeBattleCard("defend", "d_disc1"));
        var state = BattleFixtures.MinimalState(hand: hand, discard: discardPile);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), chooseDef });
        var (paused, _) = BattleEngine.PlayCard(state, handIndex: 0, null, null, Rng(), catalog);
        Assert.NotNull(paused.PendingCardPlay);
        Assert.Equal("recoverFromDiscard", paused.PendingCardPlay!.Choice.Action);
        Assert.Equal("discard", paused.PendingCardPlay.Choice.Pile); // source for UI

        var (final, _) = BattleEngine.ResolveCardChoice(paused,
            ImmutableArray.Create("s_disc1"), Rng(), catalog);

        Assert.Null(final.PendingCardPlay);
        // s_disc1 moved from DiscardPile → Hand
        Assert.Contains(final.Hand, c => c.InstanceId == "s_disc1");
        Assert.DoesNotContain(final.DiscardPile, c => c.InstanceId == "s_disc1");
        // d_disc1 still in discard
        Assert.Contains(final.DiscardPile, c => c.InstanceId == "d_disc1");
        // Choose card itself → discard (post-resolve)
        Assert.Contains(final.DiscardPile, c => c.InstanceId == "rc1");
    }

    [Fact]
    public void ResolveCardChoice_SequentialPause_PausesAgainAfterFirstChoice()
    {
        // DoubleChooseCard has 2 choose effects. After resolving the first, ApplyEffectsFrom
        // should hit the second and pause again (PendingCardPlay set with EffectIndex=1).
        var chooseDef = DoubleChooseCard();
        // First choose: exhaustCard from hand; needs hand candidates > 1.
        // Second choose: exhaustCard from discard; needs discard candidates > 1.
        // We need >= 2 cards in hand AND >= 2 cards in discard so both pauses fire.
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "s1"),
            BattleFixtures.MakeBattleCard("defend", "d1"),
            BattleFixtures.MakeBattleCard(chooseDef.Id, "dc1"));
        var discardPile = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "ds1"),
            BattleFixtures.MakeBattleCard("defend", "dd1"));
        var state = BattleFixtures.MinimalState(hand: hand, discard: discardPile);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), chooseDef });

        // Play card → first pause
        var (paused1, _) = BattleEngine.PlayCard(state, handIndex: 2, null, null, Rng(), catalog);
        Assert.NotNull(paused1.PendingCardPlay);
        Assert.Equal(0, paused1.PendingCardPlay!.EffectIndex);
        Assert.Equal("hand", paused1.PendingCardPlay.Choice.Pile);

        // Resolve first choice (exhaust s1 from hand) → second pause should emit
        var (paused2, _) = BattleEngine.ResolveCardChoice(paused1,
            ImmutableArray.Create("s1"), Rng(), catalog);
        Assert.NotNull(paused2.PendingCardPlay);
        Assert.Equal(1, paused2.PendingCardPlay!.EffectIndex);
        Assert.Equal("discard", paused2.PendingCardPlay.Choice.Pile);

        // Resolve second choice (exhaust ds1 from discard) → fully complete
        var (final, _) = BattleEngine.ResolveCardChoice(paused2,
            ImmutableArray.Create("ds1"), Rng(), catalog);
        Assert.Null(final.PendingCardPlay);
        // s1 exhausted, ds1 exhausted, dc1 (choose card) → discard
        Assert.Contains(final.ExhaustPile, c => c.InstanceId == "s1");
        Assert.Contains(final.ExhaustPile, c => c.InstanceId == "ds1");
        Assert.Contains(final.DiscardPile, c => c.InstanceId == "dc1");
    }
}
