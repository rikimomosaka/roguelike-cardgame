using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle;

public class BattlePlaceholderTests
{
    [Fact]
    public void Start_SetsActiveBattleWithEnemiesAndPendingOutcome()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var state = TestRunStates.FreshDefault(cat);
        var pool = new EnemyPool(1, EnemyTier.Weak);
        state = state with { EncounterQueueWeak = EncounterQueue.Initialize(pool, cat, new SystemRng(1)) };

        var next = BattlePlaceholder.Start(state, pool, cat, new SystemRng(1));

        Assert.NotNull(next.ActiveBattle);
        Assert.Equal(BattleOutcome.Pending, next.ActiveBattle!.Outcome);
        Assert.NotEmpty(next.ActiveBattle.Enemies);
        foreach (var e in next.ActiveBattle.Enemies)
        {
            var def = cat.Enemies[e.EnemyDefinitionId];
            Assert.InRange(e.CurrentHp, def.HpMin, def.HpMax);
            Assert.Equal(e.CurrentHp, e.MaxHp);
            Assert.Equal(def.InitialMoveId, e.CurrentMoveId);
        }
        Assert.NotEqual(state.EncounterQueueWeak.AsEnumerable(), next.EncounterQueueWeak.AsEnumerable());
    }

    [Fact]
    public void Win_SetsOutcomeToVictory()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var state = TestRunStates.FreshDefault(cat) with
        {
            ActiveBattle = new BattleState("enc_w_jaw_worm",
                ImmutableArray.Create(new EnemyInstance("jaw_worm", 42, 42, "chomp")),
                BattleOutcome.Pending)
        };

        var next = BattlePlaceholder.Win(state);
        Assert.Equal(BattleOutcome.Victory, next.ActiveBattle!.Outcome);
    }
}
