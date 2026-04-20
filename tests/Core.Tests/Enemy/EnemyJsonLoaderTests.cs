using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Enemy;

public class EnemyJsonLoaderTests
{
    [Fact]
    public void ParseJawWorm()
    {
        var def = EnemyJsonLoader.Parse(JsonFixtures.JawWormJson);
        Assert.Equal("jaw_worm", def.Id);
        Assert.Equal(40, def.HpMin);
        Assert.Equal(44, def.HpMax);
        Assert.Equal(new EnemyPool(1, EnemyTier.Weak), def.Pool);
        Assert.Equal(3, def.Moveset.Count);
    }

    [Fact]
    public void ParseGremlinNob_IsElite()
    {
        var def = EnemyJsonLoader.Parse(JsonFixtures.GremlinNobJson);
        Assert.Equal(EnemyTier.Elite, def.Pool.Tier);
    }

    [Fact]
    public void UnknownTier_Throws()
    {
        var ex = Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(JsonFixtures.EnemyBadTierJson));
        Assert.Contains("tier", ex.Message);
        Assert.Contains("bad_enemy", ex.Message);
    }

    [Fact]
    public void InvertedHp_Throws()
    {
        var ex = Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(JsonFixtures.EnemyInvertedHpJson));
        Assert.Contains("hp", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void OutOfRangeAct_Throws()
    {
        var ex = Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(JsonFixtures.EnemyOutOfRangeActJson));
        Assert.Contains("act", ex.Message.ToLowerInvariant());
    }
}
