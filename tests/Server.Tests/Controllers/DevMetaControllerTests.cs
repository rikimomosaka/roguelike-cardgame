using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Phase 10.5.M — GET /api/dev/meta のテスト。
/// formatter / engine が知っている enum 値リストを Form の dropdown 用に返す。
/// </summary>
public class DevMetaControllerTests : IClassFixture<DevWebApplicationFactory>
{
    private readonly DevWebApplicationFactory _factory;

    public DevMetaControllerTests(DevWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Meta_returns_lists_in_dev()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/dev/meta");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        // 必須フィールド (effectActions, keywords, statuses, cardTypes, rarities, etc.)
        Assert.True(body.TryGetProperty("cardTypes", out var ctEl));
        Assert.Equal(JsonValueKind.Array, ctEl.ValueKind);
        Assert.True(body.TryGetProperty("rarities", out var rarEl));
        Assert.Equal(JsonValueKind.Array, rarEl.ValueKind);
        Assert.True(body.TryGetProperty("effectActions", out var actEl));
        Assert.Equal(JsonValueKind.Array, actEl.ValueKind);
        Assert.True(body.TryGetProperty("effectScopes", out _));
        Assert.True(body.TryGetProperty("effectSides", out _));
        Assert.True(body.TryGetProperty("piles", out _));
        Assert.True(body.TryGetProperty("selectModes", out _));
        Assert.True(body.TryGetProperty("triggers", out var trigsEl));
        Assert.Equal(JsonValueKind.Array, trigsEl.ValueKind);
        Assert.True(body.TryGetProperty("amountSources", out _));
        Assert.True(body.TryGetProperty("keywords", out var kwEl));
        Assert.True(body.TryGetProperty("statuses", out _));
        // Phase 10.5.L1.5: relicTriggers 廃止、triggers に統合 (18 値)。
        Assert.False(body.TryGetProperty("relicTriggers", out _));

        // 内容サニティチェック
        var json = body.GetRawText();
        Assert.Contains("attack", json);
        Assert.Contains("addCard", json);
        Assert.Contains("wild", json);
        Assert.Contains("Common", json);
        // unified triggers の代表的な値 (relic + power 統合)
        Assert.Contains("OnPickup", json);
        Assert.Contains("Passive", json);
        Assert.Contains("OnEnemyDeath", json);
        Assert.Contains("OnPlayCard", json);
        Assert.Contains("OnDamageReceived", json);
        Assert.Contains("OnCombo", json);
        Assert.Contains("OnCardDiscarded", json);
        Assert.Contains("OnCardExhausted", json);
        Assert.Contains("OnEnterShop", json);
        Assert.Contains("OnCardAddedToDeck", json);
    }
}

public class DevMetaControllerProdTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _factory;

    public DevMetaControllerProdTests(ProductionWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Meta_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/dev/meta");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
