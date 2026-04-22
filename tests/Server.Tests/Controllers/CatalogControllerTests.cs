using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class CatalogControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public CatalogControllerTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetCards_Returns200_WithDictionaryOfDefinitions()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/catalog/cards");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Object, body.ValueKind);

        // "defend" exists in the embedded catalog and is named 「防御」.
        Assert.True(body.TryGetProperty("defend", out var defend));
        Assert.Equal("defend", defend.GetProperty("id").GetString());
        Assert.Equal("防御", defend.GetProperty("name").GetString());
        Assert.Equal("Skill", defend.GetProperty("cardType").GetString());
    }
}
