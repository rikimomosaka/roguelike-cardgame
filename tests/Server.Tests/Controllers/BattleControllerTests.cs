using System.Linq;
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

    [Fact]
    public async Task Get_when_session_exists_returns_BattleStateDto()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            var resp = await client.GetAsync("/api/v1/runs/current/battle");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<BattleStateDto>();
            Assert.NotNull(body);
            Assert.Equal("PlayerInput", body!.Phase);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Get_when_no_session_returns_404()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            // start を呼ばずに直接 GET
            var resp = await client.GetAsync("/api/v1/runs/current/battle");

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task PlayCard_with_valid_index_advances_state_and_returns_events()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(0, 0, 0));

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);
            Assert.Contains(body!.Steps, s => s.Event.Kind == "PlayCard");
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task PlayCard_with_invalid_handIndex_returns_400()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(99, 0, 0));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task PlayCard_when_no_session_returns_409()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            // start を呼ばずに play-card
            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(0, 0, 0));

            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task EndTurn_resolves_phase_transitions_and_returns_events()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            var resp = await client.PostAsync("/api/v1/runs/current/battle/end-turn", null);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);
            Assert.Contains(body!.Steps, s => s.Event.Kind == "EndTurn");
            // ターン進行: 最終 state は PlayerInput または Resolved
            Assert.True(body.State.Phase == "PlayerInput" || body.State.Phase == "Resolved");
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task EndTurn_when_no_session_returns_409()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            var resp = await client.PostAsync("/api/v1/runs/current/battle/end-turn", null);
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task UsePotion_with_valid_slot_consumes_potion()
    {
        // SetupRunWithPotionAsync で RunState.Potions[0]="fire_potion" を仕込み、
        // /battle/start で BattleState.Potions に伝搬させた上で /use-potion を叩く。
        // fire_potion は single enemy attack なので targetEnemyIndex=0 を指定。
        var (client, _) = await BattleControllerFixtures.SetupRunWithPotionAsync(_factory, potionId: "fire_potion");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-potion",
                new UsePotionRequestDto(0, 0, null));

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);
            Assert.Contains(body!.Steps, s => s.Event.Kind == "UsePotion");
            // BattleEngine.UsePotion は使用後 slot を "" に置換 (Phase 10.2.E spec §4)。
            Assert.Equal("", body.State.Potions[0]);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task UsePotion_with_empty_slot_returns_400()
    {
        // SetupRunWithActiveBattleAsync は Potions を仕込まないため、
        // 全 slot は空文字。slot 0 を使おうとすると BattleEngine が
        // InvalidOperationException を投げ、controller は 400 に変換する。
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-potion",
                new UsePotionRequestDto(0, null, null));

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task PlayCard_with_negative_targetEnemyIndex_does_not_500()
    {
        // Issue 2 review: BattleEngine.PlayCard は負の target index を validate せず、
        // EffectApplier.ResolveTargets が "ei < state.Enemies.Length" の上限のみを
        // チェックする (line 169-174)。ei = -1 を渡すと state.Allies[-1] /
        // state.Enemies[-1] で IndexOutOfRangeException が発生し得る。
        // 現スターター (strike + defend のみ) では実トリガーは難しいが、
        // Tasks 7-10 で追加予定の buff/debuff カードや SetTarget endpoint で
        // 同経路が有効化されるため、controller の catch を broaden した。
        // この test は「負の target index で 500 が漏れない」ことを保証する
        // (200 でグレースフル無視、または 400 ならどちらも OK、500 のみ NG)。
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(0, -1, -1));

            Assert.NotEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }
}
