using System;
using System.Linq;
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
        var row9 = cfg.FixedRows.Single(r => r.Row == 9);
        Assert.Equal(TileKind.Treasure, row9.Kind);
        Assert.Equal(48, cfg.UnknownResolutionWeights.Weights[TileKind.Enemy]);
        Assert.False(cfg.UnknownResolutionWeights.Weights.ContainsKey(TileKind.Elite));
    }

    [Fact]
    public void LoadAct1_ConfigIsUsableByGenerator()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        // rowNodeCountMin: 3 により各行に最低 3 ノードが配置されるため、edge-candidates-empty が起きにくい。
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

    [Fact]
    public void Parse_InvalidInvariant_RowNodeCountMinAboveMax_Throws()
    {
        // act1 の JSON を読み込み、テキスト置換で rowNodeCountMin > rowNodeCountMax の不整合を混入。
        var asm = typeof(MapGenerationConfigLoader).Assembly;
        using var stream = asm.GetManifestResourceStream("RoguelikeCardGame.Core.Map.Config.map-act1.json")!;
        using var reader = new System.IO.StreamReader(stream);
        var json = reader.ReadToEnd()
            .Replace("\"rowNodeCountMin\": 3", "\"rowNodeCountMin\": 5")
            .Replace("\"rowNodeCountMax\": 4", "\"rowNodeCountMax\": 4");
        var ex = Assert.Throws<MapGenerationConfigException>(() => MapGenerationConfigLoader.Parse(json));
        Assert.Contains("RowNodeCountMax", ex.Message);
    }
}
