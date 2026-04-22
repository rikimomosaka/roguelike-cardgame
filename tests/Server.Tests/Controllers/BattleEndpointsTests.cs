using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class BattleEndpointsTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public BattleEndpointsTests(TempDataFactory f) => _factory = f;

    [Fact]
    public async Task PostBattleWin_NoActiveBattle_Returns409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, "alice");
        BattleTestHelpers.WithAccount(client, "alice");
        await client.PostAsync("/api/v1/runs/new", null);

        var res = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task PostBattleWin_WithActiveBattle_SetsActiveReward()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, "bob");
        BattleTestHelpers.WithAccount(client, "bob");
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(client);

        var res = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win", new { elapsedSeconds = 1 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        // Response body is the updated snapshot.
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("run").GetProperty("activeBattle").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, doc.RootElement.GetProperty("run").GetProperty("activeReward").ValueKind);

        // Subsequent GET reflects the same state.
        var cur = await client.GetAsync("/api/v1/runs/current");
        var curDoc = JsonDocument.Parse(await cur.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, curDoc.RootElement.GetProperty("run").GetProperty("activeBattle").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, curDoc.RootElement.GetProperty("run").GetProperty("activeReward").ValueKind);
    }
}
