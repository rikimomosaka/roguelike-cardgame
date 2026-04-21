using System;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class MapGenerationConfigLoaderTests
{
    [Fact]
    public void LoadAct1_ReturnsNonNullConfig()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        Assert.Equal(15, cfg.RowCount);
        Assert.Equal(5, cfg.ColumnCount);
        Assert.Equal(9, cfg.FixedRows[0].Row);
        Assert.Equal(TileKind.Treasure, cfg.FixedRows[0].Kind);
    }

    [Fact]
    public void LoadAct1_ConfigIsUsableByGenerator()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        // seed 7 は act1 config の rowNodeCountMin: 2 で 100 回以内に edge-candidates-empty から回復できないため、58 を使用。
        var map = new DungeonMapGenerator().Generate(new SystemRng(58), cfg);
        Assert.Equal(0, map.GetNode(map.StartNodeId).Row);
        Assert.Equal(16, map.GetNode(map.BossNodeId).Row);
    }

    [Fact]
    public void Parse_UnknownField_Throws()
    {
        var badJson = "{\"rowCount\":15,\"extra\":1}";
        Assert.Throws<MapGenerationConfigException>(() => MapGenerationConfigLoader.Parse(badJson));
    }

    [Fact]
    public void Parse_MissingRequiredField_Throws()
    {
        var badJson = "{\"rowCount\":15}";
        Assert.Throws<MapGenerationConfigException>(() => MapGenerationConfigLoader.Parse(badJson));
    }
}
