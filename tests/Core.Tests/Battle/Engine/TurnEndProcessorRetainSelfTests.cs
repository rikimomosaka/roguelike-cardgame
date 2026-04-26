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
/// Phase 10.2.D: TurnEndProcessor の retainSelf-aware 手札整理テスト。
/// retainSelf 効果を持つカードのみ Hand に残し、それ以外は DiscardPile へ移す。
/// 親 spec §4-6 step 5 / Phase 10.2.D spec §6 参照。
/// </summary>
public class TurnEndProcessorRetainSelfTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[10], System.Array.Empty<double>());

    private static CardDefinition StrikeDef() =>
        new("strike", "Strike", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: null, Keywords: null);

    private static CardDefinition RetainCard() =>
        new("hold", "Hold", null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] { new CardEffect("retainSelf", EffectScope.Self, null, 0) },
            UpgradedEffects: null, Keywords: null);

    private static BattleState MakeState(ImmutableArray<BattleCardInstance> hand) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");

    [Fact]
    public void Retains_cards_with_retainSelf_effect()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null),
            new BattleCardInstance("c2", "hold", false, null));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { StrikeDef(), RetainCard() });

        var (next, _) = TurnEndProcessor.Process(s, MakeRng(), cat);

        Assert.Single(next.Hand);
        Assert.Equal("c2", next.Hand[0].InstanceId);   // hold のみ残る
        Assert.Single(next.DiscardPile);
        Assert.Equal("c1", next.DiscardPile[0].InstanceId);   // strike は捨てる
    }

    [Fact]
    public void Discards_all_when_no_retain()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null),
            new BattleCardInstance("c2", "strike", false, null));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { StrikeDef() });

        var (next, _) = TurnEndProcessor.Process(s, MakeRng(), cat);

        Assert.Empty(next.Hand);
        Assert.Equal(2, next.DiscardPile.Length);
    }

    [Fact]
    public void Combo_fields_still_reset()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "hold", false, null));
        var s = MakeState(hand) with
        {
            ComboCount = 3,
            LastPlayedOrigCost = 5,
            NextCardComboFreePass = true,
        };
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { RetainCard() });

        var (next, _) = TurnEndProcessor.Process(s, MakeRng(), cat);

        Assert.Equal(0, next.ComboCount);
        Assert.Null(next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }
}
