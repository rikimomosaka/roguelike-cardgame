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

public class TurnEndProcessorOnTurnEndTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void OnTurnEnd_relic_fires_after_AttackPool_reset_before_combo_reset()
    {
        // attack relic: AttackPool reset 後に hero pool に加算 → 次 turn まで保持
        var relic = BattleFixtures.Relic("te", "OnTurnEnd", true,
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 4));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        // 既存 hero に attack pool が積まれた状態を作る
        var heroWithAttack = BattleFixtures.Hero() with {
            AttackAll = AttackPool.Empty.Add(99),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(heroWithAttack),
            ownedRelicIds: ImmutableArray.Create("te"));

        var (after, _) = TurnEndProcessor.Process(state, MakeRng(), catalog);

        // AttackPool reset 後に relic が +4 → final 4 (元の 99 はリセット済み)
        Assert.Equal(4, after.Allies[0].AttackAll.Sum);
    }

    [Fact]
    public void OnTurnEnd_with_no_relics_keeps_existing_behavior()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState();

        var (after, events) = TurnEndProcessor.Process(state, MakeRng(), catalog);

        Assert.Equal(0, after.ComboCount);
    }

    [Fact]
    public void OnTurnEnd_combo_resets_after_relic_fires()
    {
        var relic = BattleFixtures.Relic("noop", "OnTurnEnd", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("noop")) with {
            ComboCount = 5,
            LastPlayedOrigCost = 2,
            NextCardComboFreePass = true,
        };

        var (after, _) = TurnEndProcessor.Process(state, MakeRng(), catalog);

        // コンボ系 reset
        Assert.Equal(0, after.ComboCount);
        Assert.Null(after.LastPlayedOrigCost);
        Assert.False(after.NextCardComboFreePass);
        // relic effect は Block 1 として hero に乗ったまま
        Assert.Equal(1, after.Allies[0].Block.RawTotal);
    }
}
