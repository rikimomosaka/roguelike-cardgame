using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardOnCardPlayTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void OnCardPlay_fires_after_card_effects_before_card_movement()
    {
        var relic = BattleFixtures.Relic("oc", "OnPlayCard", true,
            new CardEffect("block", EffectScope.Self, null, 3));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() },
            relics: new[] { relic });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card),
            ownedRelicIds: ImmutableArray.Create("oc")) with { Energy = 1 };

        var (after, events) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // strike effect (attack 6 → AttackSingle), then OnCardPlay relic block 3
        Assert.Equal(6, after.Allies[0].AttackSingle.Sum);
        Assert.Equal(3, after.Allies[0].Block.RawTotal);
        // events: PlayCard → AttackPool 加算は event なし → relic GainBlock with relic:oc
        var relicEv = events.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && e.Note != null && e.Note.Contains("relic:oc"));
        Assert.NotNull(relicEv);
        // カード移動: Discard へ (strike は exhaustSelf/retainSelf/Power/Unit でない)
        Assert.Single(after.DiscardPile);
        Assert.Equal("c1", after.DiscardPile[0].InstanceId);
    }

    [Fact]
    public void OnCardPlay_with_no_relics_keeps_existing_behavior()
    {
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card)) with { Energy = 1 };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        Assert.Equal(6, after.Allies[0].AttackSingle.Sum);
        Assert.Single(after.DiscardPile);
    }

    [Fact]
    public void OnCardPlay_relic_summon_does_not_affect_card_self_summonSucceeded()
    {
        // strike (Attack カード, NonUnit) をプレイし、OnCardPlay レリックが summon 効果を持つ
        // → カード自身は Discard へ移動 (Unit でないので SummonHeld には行かない)
        var relic = BattleFixtures.Relic("summon_r", "OnPlayCard", true,
            new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion"));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() },
            relics: new[] { relic });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card),
            ownedRelicIds: ImmutableArray.Create("summon_r")) with { Energy = 1 };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // Allies に minion が追加される
        Assert.Equal(2, after.Allies.Length);
        // strike カード自身は Discard へ (summon 成功フラグはカード自身の effect ループ内でのみセット)
        Assert.Single(after.DiscardPile);
        Assert.Empty(after.SummonHeld);
    }
}
