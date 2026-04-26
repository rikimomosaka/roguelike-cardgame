using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnEndProcessorComboResetTests
{
    private static BattleState Make(int combo, int? lastOrigCost, bool freePass) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: combo,
            LastPlayedOrigCost: lastOrigCost,
            NextCardComboFreePass: freePass,
            EncounterId: "enc_test");

    [Fact] public void Resets_combo_count_to_zero()
    {
        var s = Make(combo: 5, lastOrigCost: 3, freePass: true);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Equal(0, next.ComboCount);
    }

    [Fact] public void Resets_last_played_orig_cost_to_null()
    {
        var s = Make(combo: 3, lastOrigCost: 4, freePass: false);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Null(next.LastPlayedOrigCost);
    }

    [Fact] public void Resets_next_card_combo_free_pass_to_false()
    {
        var s = Make(combo: 2, lastOrigCost: 7, freePass: true);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.False(next.NextCardComboFreePass);
    }

    [Fact] public void All_combo_fields_reset_simultaneously()
    {
        var s = Make(combo: 4, lastOrigCost: 6, freePass: true);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Equal(0, next.ComboCount);
        Assert.Null(next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }
}
