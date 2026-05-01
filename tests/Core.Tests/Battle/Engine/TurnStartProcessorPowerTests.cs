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
/// 10.5.E: TurnStartProcessor.Process が Step 8 (relic OnTurnStart) 直後に
/// PowerTriggerProcessor.Fire("OnTurnStart") を呼ぶ統合テスト。
/// </summary>
public class TurnStartProcessorPowerTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void OnTurnStart_power_fires_after_relic_and_before_TurnStart_event()
    {
        // OnTurnStart で block 4 を積む power
        var powerDef = new CardDefinition(
            Id: "p_block", Name: "p_block", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, 4, Trigger: "OnTurnStart"),
            },
            UpgradedEffects: null, Keywords: null);

        var instance = new BattleCardInstance("p1", "p_block", false, null);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });
        var state = BattleFixtures.MinimalState() with
        {
            PowerCards = ImmutableArray.Create(instance),
        };

        var (after, events) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        // hero に block 4 が積まれている
        Assert.Equal(4, after.Allies[0].Block.RawTotal);
        // power 由来の GainBlock event がある
        var powerEv = events.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && e.Note != null && e.Note.Contains("power:p_block"));
        Assert.NotNull(powerEv);
        // TurnStart event より前
        var tsIdx = events.ToList().FindIndex(e => e.Kind == BattleEventKind.TurnStart);
        var pIdx = events.ToList().FindIndex(e =>
            e.Note != null && e.Note.Contains("power:p_block"));
        Assert.True(pIdx < tsIdx);
    }

    [Fact]
    public void OnTurnStart_power_does_not_fire_for_other_triggers()
    {
        var powerDef = new CardDefinition(
            Id: "p_play", Name: "p_play", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, 4, Trigger: "OnPlayCard"),
            },
            UpgradedEffects: null, Keywords: null);

        var instance = new BattleCardInstance("p1", "p_play", false, null);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });
        var state = BattleFixtures.MinimalState() with
        {
            PowerCards = ImmutableArray.Create(instance),
        };

        var (after, _) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        // OnPlayCard trigger は TurnStart では発火せず
        Assert.Equal(0, after.Allies[0].Block.RawTotal);
    }

    [Fact]
    public void TurnStart_with_no_power_cards_keeps_existing_behavior()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState();

        var (after, events) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        // 既存挙動: TurnStart event が発生、PowerCards 関連の event なし
        Assert.Contains(events, e => e.Kind == BattleEventKind.TurnStart);
        Assert.DoesNotContain(events, e => e.Note != null && e.Note.Contains("power:"));
    }
}
