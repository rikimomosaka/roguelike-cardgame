using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle;

public class BattlePlaceholderBestiaryTests
{
    [Fact]
    public void Start_TracksAllEnemyIds()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var state = TestRunStates.FreshDefault(cat);
        var pool = new EnemyPool(1, EnemyTier.Weak);
        state = state with { EncounterQueueWeak = EncounterQueue.Initialize(pool, cat, new SystemRng(42)) };

        var after = BattlePlaceholder.Start(state, pool, cat, new SystemRng(42));

        var enc = cat.Encounters[after.ActiveBattle!.EncounterId];
        Assert.NotEmpty(enc.EnemyIds);
        foreach (var eid in enc.EnemyIds)
            Assert.Contains(eid, after.EncounteredEnemyIds);
    }
}
