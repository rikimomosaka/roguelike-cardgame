using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Map;
using Xunit;

namespace RoguelikeCardGame.Server.Tests;

public class MapGeneratorDiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public MapGeneratorDiTests(WebApplicationFactory<Program> factory) { _factory = factory; }

    [Fact]
    public void IDungeonMapGenerator_Resolves()
    {
        using var scope = _factory.Services.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IDungeonMapGenerator>();
        Assert.NotNull(gen);
    }

    [Fact]
    public void MapGenerationConfig_Resolves()
    {
        using var scope = _factory.Services.CreateScope();
        var cfg = scope.ServiceProvider.GetRequiredService<MapGenerationConfig>();
        Assert.Equal(15, cfg.RowCount);
    }
}
