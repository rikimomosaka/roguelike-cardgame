using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class BattleControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleControllerTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task Start_when_no_active_run_returns_409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        const string accountId = "no-run-account";
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);

        var resp = await client.PostAsync("/api/v1/runs/current/battle/start", null);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Start_when_no_active_battle_returns_409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        const string accountId = "no-battle-account";
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        var newRes = await client.PostAsync("/api/v1/runs/new", null);
        newRes.EnsureSuccessStatusCode();

        // ラン開始直後はスタートマス（Enemy ではない）にいるため ActiveBattle は null。
        var resp = await client.PostAsync("/api/v1/runs/current/battle/start", null);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Start_creates_session_and_returns_BattleStart_TurnStart_events()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            var resp = await client.PostAsync("/api/v1/runs/current/battle/start", null);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);
            Assert.Contains(body!.Steps, s => s.Event.Kind == "BattleStart");
            Assert.Contains(body!.Steps, s => s.Event.Kind == "TurnStart");
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Start_is_idempotent_returns_same_session()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            var first = await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var second = await client.PostAsync("/api/v1/runs/current/battle/start", null);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var firstBody = await first.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            var secondBody = await second.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            // 2 回目は events 空 (冪等性、既存セッションをそのまま返却)
            Assert.Empty(secondBody!.Steps);
            Assert.Equal(firstBody!.State.Turn, secondBody.State.Turn);
            Assert.Equal(firstBody.State.EncounterId, secondBody.State.EncounterId);
        }
        finally
        {
            client.Dispose();
        }
    }
}
