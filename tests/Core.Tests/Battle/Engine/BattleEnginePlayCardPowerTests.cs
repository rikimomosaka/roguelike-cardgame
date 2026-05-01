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
/// 10.5.E: BattleEngine.PlayCard 末尾 (destination pile 振り分け後) で
/// OnPlayCard / OnCombo power が発火する統合テスト。
/// </summary>
public class BattleEnginePlayCardPowerTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void OnPlayCard_power_fires_when_card_is_played()
    {
        var powerDef = new CardDefinition(
            Id: "p_play", Name: "p_play", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, 3, Trigger: "OnPlayCard"),
            },
            UpgradedEffects: null, Keywords: null);

        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), powerDef });

        var strike = BattleFixtures.MakeBattleCard("strike", "c1");
        var powerInst = new BattleCardInstance("p_inst", "p_play", false, null);
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(strike)) with
            {
                Energy = 1,
                PowerCards = ImmutableArray.Create(powerInst),
            };

        var (after, events) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // strike effect (attack 6 → AttackSingle)、OnPlayCard power が block 3
        Assert.Equal(6, after.Allies[0].AttackSingle.Sum);
        Assert.Equal(3, after.Allies[0].Block.RawTotal);
        // events: power 由来の GainBlock
        var powerEv = events.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && e.Note != null && e.Note.Contains("power:p_play"));
        Assert.NotNull(powerEv);
    }

    [Fact]
    public void OnPlayCard_power_does_not_fire_for_OnTurnStart_trigger()
    {
        // Trigger=OnTurnStart の power は PlayCard では発火しない
        var powerDef = new CardDefinition(
            Id: "p_ts", Name: "p_ts", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, 3, Trigger: "OnTurnStart"),
            },
            UpgradedEffects: null, Keywords: null);

        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), powerDef });

        var strike = BattleFixtures.MakeBattleCard("strike", "c1");
        var powerInst = new BattleCardInstance("p_inst", "p_ts", false, null);
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(strike)) with
            {
                Energy = 1,
                PowerCards = ImmutableArray.Create(powerInst),
            };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        Assert.Equal(0, after.Allies[0].Block.RawTotal);
    }

    [Fact]
    public void OnCombo_power_fires_when_combo_threshold_met()
    {
        // ComboMin=2 の OnCombo power: combo=2 達成時に block 5
        // strike (cost 1) を 2 回続けると combo=2 になる (LastPlayedOrigCost=1 → next cost matches?)
        // ここでは直接 ComboCount=1 を仕込んで cost 2 のカードを play すれば combo=2 になる。
        var powerDef = new CardDefinition(
            Id: "p_combo", Name: "p_combo", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, 5, ComboMin: 2, Trigger: "OnCombo"),
            },
            UpgradedEffects: null, Keywords: null);

        // cost 2 の strike-like カードを定義
        var cost2Strike = new CardDefinition(
            Id: "strike2", Name: "strike2", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Attack,
            Cost: 2, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4) },
            UpgradedEffects: null, Keywords: null);

        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), cost2Strike, powerDef });

        var card2 = BattleFixtures.MakeBattleCard("strike2", "c2");
        var powerInst = new BattleCardInstance("p_inst", "p_combo", false, null);
        // 直前に cost=1 のカードを play した状態を仕込む (LastPlayedOrigCost=1, ComboCount=1)
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card2)) with
            {
                Energy = 2,
                LastPlayedOrigCost = 1,
                ComboCount = 1,
                PowerCards = ImmutableArray.Create(powerInst),
            };

        var (after, events) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // combo=2 達成
        Assert.Equal(2, after.ComboCount);
        // OnCombo power の block 5 が発火
        Assert.Equal(5, after.Allies[0].Block.RawTotal);
        var comboEv = events.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && e.Note != null && e.Note.Contains("power:p_combo"));
        Assert.NotNull(comboEv);
    }

    [Fact]
    public void OnCombo_power_does_not_fire_when_below_threshold()
    {
        // ComboMin=3 の OnCombo power: combo=1 では発火しない
        var powerDef = new CardDefinition(
            Id: "p_combo3", Name: "p_combo3", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, 5, ComboMin: 3, Trigger: "OnCombo"),
            },
            UpgradedEffects: null, Keywords: null);

        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), powerDef });

        var strike = BattleFixtures.MakeBattleCard("strike", "c1");
        var powerInst = new BattleCardInstance("p_inst", "p_combo3", false, null);
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(strike)) with
            {
                Energy = 1,
                PowerCards = ImmutableArray.Create(powerInst),
                // 初回 play なので combo=1 になる
            };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        Assert.Equal(1, after.ComboCount);
        Assert.Equal(0, after.Allies[0].Block.RawTotal);
    }

    [Fact]
    public void Power_card_self_OnPlayCard_fires_when_power_played()
    {
        // power カード自身が OnPlayCard を持つ場合: PowerCards に追加された "後" で fire するので
        // 自身も発火対象 (self-trigger 許容仕様)
        var powerDef = new CardDefinition(
            Id: "p_self", Name: "p_self", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, 2, Trigger: "OnPlayCard"),
            },
            UpgradedEffects: null, Keywords: null);

        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var powerInst = new BattleCardInstance("p_inst", "p_self", false, null);
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(powerInst)) with
            {
                Energy = 1,
            };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // power カードは PowerCards へ
        Assert.Single(after.PowerCards);
        // 自身の OnPlayCard が発火し block 2
        Assert.Equal(2, after.Allies[0].Block.RawTotal);
    }
}
