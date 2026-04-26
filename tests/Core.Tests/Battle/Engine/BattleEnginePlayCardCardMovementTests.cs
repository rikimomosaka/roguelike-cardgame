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
/// Phase 10.2.D Task 12: PlayCard の 5 段優先順位カード移動ロジックのテスト。
/// 優先順位: exhaustSelf → Power → Unit+summonSucceeded → retainSelf → Discard
/// </summary>
public class BattleEnginePlayCardCardMovementTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> hand,
        int energy = 3)
        => new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");

    [Fact] public void Default_routing_to_discard()
    {
        // 通常 Attack カード → DiscardPile
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);

        Assert.Empty(next.Hand);
        Assert.Single(next.DiscardPile);
        Assert.Equal("c1", next.DiscardPile[0].InstanceId);
        Assert.Empty(next.ExhaustPile);
        Assert.Empty(next.SummonHeld);
        Assert.Empty(next.PowerCards);
    }

    [Fact] public void ExhaustSelf_routes_to_exhaust_pile()
    {
        // exhaustSelf 効果を持つカード → ExhaustPile
        var exhaustCard = new CardDefinition(
            "exhaust_skill", "Exhaust Skill", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("block", EffectScope.Self, null, 5),
                new CardEffect("exhaustSelf", EffectScope.Self, null, 0),
            },
            UpgradedEffects: null, Keywords: null);

        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("exhaust_skill", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { exhaustCard });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);

        Assert.Empty(next.Hand);
        Assert.Empty(next.DiscardPile);
        Assert.Single(next.ExhaustPile);
        Assert.Equal("c1", next.ExhaustPile[0].InstanceId);
    }

    [Fact] public void Power_card_routes_to_power_cards()
    {
        // CardType.Power → PowerCards (effects は実行される)
        var powerCard = new CardDefinition(
            "power1", "Power Card", null, CardRarity.Common, CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("buff", EffectScope.Self, null, 2, Name: "strength"),
            },
            UpgradedEffects: null, Keywords: null);

        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("power1", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { powerCard });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);

        Assert.Empty(next.Hand);
        Assert.Empty(next.DiscardPile);
        Assert.Empty(next.ExhaustPile);
        Assert.Single(next.PowerCards);
        Assert.Equal("c1", next.PowerCards[0].InstanceId);

        // 効果が反映されていること
        Assert.Equal(2, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void Unit_with_summon_success_routes_to_summon_held()
    {
        // CardType.Unit + summon 成功 → SummonHeld + AssociatedSummonHeldInstanceId 設定
        var unitCard = new CardDefinition(
            "summon_card", "Summon Card", null, CardRarity.Common, CardType.Unit,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion"),
            },
            UpgradedEffects: null, Keywords: null);

        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("summon_card", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(
            cards: new[] { unitCard },
            units: new[] { BattleFixtures.MinionDef() });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);

        Assert.Empty(next.Hand);
        Assert.Empty(next.DiscardPile);
        Assert.Empty(next.ExhaustPile);
        Assert.Empty(next.PowerCards);
        Assert.Single(next.SummonHeld);
        Assert.Equal("c1", next.SummonHeld[0].InstanceId);

        // 召喚 actor の AssociatedSummonHeldInstanceId が card.InstanceId にバインドされること
        Assert.Equal(2, next.Allies.Length);
        var summonActor = next.Allies[1];
        Assert.Equal("minion", summonActor.DefinitionId);
        Assert.Equal("c1", summonActor.AssociatedSummonHeldInstanceId);
    }

    [Fact] public void Unit_with_summon_failure_routes_to_discard()
    {
        // CardType.Unit + slot 満杯 (= summon 失敗) → DiscardPile
        var unitCard = new CardDefinition(
            "summon_card", "Summon Card", null, CardRarity.Common, CardType.Unit,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion"),
            },
            UpgradedEffects: null, Keywords: null);

        // slot 1〜3 全て埋まった状態
        var allies = ImmutableArray.Create(
            BattleFixtures.Hero(),
            BattleFixtures.SummonActor("s1", "minion", 1),
            BattleFixtures.SummonActor("s2", "minion", 2),
            BattleFixtures.SummonActor("s3", "minion", 3));

        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("summon_card", "c1"));
        var s = MakeState(hand) with { Allies = allies };

        var cat = BattleFixtures.MinimalCatalog(
            cards: new[] { unitCard },
            units: new[] { BattleFixtures.MinionDef() });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);

        Assert.Empty(next.Hand);
        Assert.Empty(next.SummonHeld);
        Assert.Single(next.DiscardPile);
        Assert.Equal("c1", next.DiscardPile[0].InstanceId);
        Assert.Equal(4, next.Allies.Length);   // 不変
    }

    [Fact] public void RetainSelf_routes_to_hand()
    {
        // retainSelf 効果 → Hand (元の index に保持)
        var retainCard = new CardDefinition(
            "retain_skill", "Retain Skill", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("block", EffectScope.Self, null, 5),
                new CardEffect("retainSelf", EffectScope.Self, null, 0),
            },
            UpgradedEffects: null, Keywords: null);

        // hand の中盤 (index 1) に retain card を配置
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c0"),
            BattleFixtures.MakeBattleCard("retain_skill", "c1"),
            BattleFixtures.MakeBattleCard("strike", "c2"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), retainCard });
        var (next, _) = BattleEngine.PlayCard(s, 1, 0, 0, Rng(), cat);

        Assert.Equal(3, next.Hand.Length);
        Assert.Equal("c1", next.Hand[1].InstanceId);   // 元 index 1 に維持
        Assert.Empty(next.DiscardPile);
        Assert.Empty(next.ExhaustPile);
    }

    [Fact] public void ExhaustSelf_overrides_retainSelf()
    {
        // 両方の効果 → ExhaustPile が優先 (priority order)
        var bothCard = new CardDefinition(
            "both_skill", "Both Skill", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("retainSelf", EffectScope.Self, null, 0),
                new CardEffect("exhaustSelf", EffectScope.Self, null, 0),
            },
            UpgradedEffects: null, Keywords: null);

        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("both_skill", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { bothCard });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);

        Assert.Empty(next.Hand);
        Assert.Single(next.ExhaustPile);
        Assert.Equal("c1", next.ExhaustPile[0].InstanceId);
        Assert.Empty(next.DiscardPile);
    }

    [Fact] public void Power_overrides_retainSelf_when_both_present()
    {
        // CardType.Power + retainSelf 効果 → PowerCards が優先
        var powerCard = new CardDefinition(
            "power_retain", "Power Retain", null, CardRarity.Common, CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength"),
                new CardEffect("retainSelf", EffectScope.Self, null, 0),
            },
            UpgradedEffects: null, Keywords: null);

        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("power_retain", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { powerCard });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);

        Assert.Empty(next.Hand);
        Assert.Single(next.PowerCards);
        Assert.Equal("c1", next.PowerCards[0].InstanceId);
        Assert.Empty(next.DiscardPile);
        Assert.Empty(next.ExhaustPile);
    }
}
