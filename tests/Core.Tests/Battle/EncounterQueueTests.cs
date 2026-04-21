using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle;

public class EncounterQueueTests
{
    private static DataCatalog Cat() => EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void Initialize_ContainsAllEncountersOfThatPool()
    {
        var cat = Cat();
        var pool = new EnemyPool(1, EnemyTier.Weak);
        var q = EncounterQueue.Initialize(pool, cat, new SystemRng(42));
        var expected = cat.Encounters.Values.Where(e => e.Pool == pool).Select(e => e.Id).OrderBy(s => s).ToArray();
        var actual = q.OrderBy(s => s).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Initialize_SameSeed_SameOrder()
    {
        var cat = Cat();
        var pool = new EnemyPool(1, EnemyTier.Strong);
        var a = EncounterQueue.Initialize(pool, cat, new SystemRng(7));
        var b = EncounterQueue.Initialize(pool, cat, new SystemRng(7));
        Assert.Equal(a.AsEnumerable(), b.AsEnumerable());
    }

    [Fact]
    public void Draw_RotatesHeadToTail()
    {
        var q = ImmutableArray.Create("a", "b", "c");
        var (id, next) = EncounterQueue.Draw(q);
        Assert.Equal("a", id);
        Assert.Equal(ImmutableArray.Create("b", "c", "a").AsEnumerable(), next.AsEnumerable());
    }
}
