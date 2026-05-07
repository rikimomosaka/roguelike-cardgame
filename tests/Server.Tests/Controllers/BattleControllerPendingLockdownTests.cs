using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Phase 10.5.M2-Choose Task 5: PendingCardPlay 中は他 player-action endpoint が
/// 全て 409 で reject されることを確認する。
/// 直接 BattleSessionStore に PendingCardPlay を inject して guard を発火させる。
/// </summary>
public class BattleControllerPendingLockdownTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleControllerPendingLockdownTests(TempDataFactory factory) => _factory = factory;

    /// <summary>
    /// session.State.PendingCardPlay を直接書く。card / candidate は dummy で良い (lockdown は
    /// pending != null だけで判定する)。
    /// </summary>
    private static void InjectPendingState(System.IServiceProvider services, string accountId)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        if (!store.TryGet(accountId, out var session))
            throw new System.InvalidOperationException(
                $"InjectPendingState: session not found for {accountId}");
        var pending = new PendingCardPlay(
            CardInstanceId: "fake_card_inst",
            EffectIndex: 0,
            SummonSucceededBefore: false,
            Choice: new PendingChoice(
                Action: "exhaustCard",
                Pile: "hand",
                Count: 1,
                CandidateInstanceIds: ImmutableArray.Create("c1", "c2")));
        var newState = session.State with { PendingCardPlay = pending };
        store.Set(accountId, session with { State = newState });
    }

    /// <summary>
    /// PendingCardPlay を null に戻す。Test 11 用。
    /// </summary>
    private static void ClearPendingState(System.IServiceProvider services, string accountId)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        if (!store.TryGet(accountId, out var session))
            throw new System.InvalidOperationException(
                $"ClearPendingState: session not found for {accountId}");
        var newState = session.State with { PendingCardPlay = null };
        store.Set(accountId, session with { State = newState });
    }

    // 6. /play-card は 409
    [Fact]
    public async Task PlayCard_when_pending_returns_409()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            InjectPendingState(_factory.Services, accountId);

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

    // 7. /end-turn は 409
    [Fact]
    public async Task EndTurn_when_pending_returns_409()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            InjectPendingState(_factory.Services, accountId);

            var resp = await client.PostAsync("/api/v1/runs/current/battle/end-turn", null);
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // 8. /use-potion は 409
    [Fact]
    public async Task UsePotion_when_pending_returns_409()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithPotionAsync(
            _factory, potionId: "fire_potion");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            InjectPendingState(_factory.Services, accountId);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-potion",
                new UsePotionRequestDto(0, 0, null));
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // 9. /set-target は 409
    [Fact]
    public async Task SetTarget_when_pending_returns_409()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            InjectPendingState(_factory.Services, accountId);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/set-target",
                new SetTargetRequestDto("Enemy", 0));
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // 10. /finalize は 409 (pending は本来 Resolved まで生き残らないが、defensive guard を確認)
    [Fact]
    public async Task Finalize_when_pending_returns_409()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            InjectPendingState(_factory.Services, accountId);

            var resp = await client.PostAsync("/api/v1/runs/current/battle/finalize", null);
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // 11. pending を「手動」クリア後は /play-card が 409 ではなくなる (200 や他 4xx になる)。
    //     注: resolve endpoint 経由の unlock 検証は BattleControllerResolveChoiceTests
    //     (resolve+follow-up action の integration) でカバーされており、ここでは guard そのものが
    //     PendingCardPlay==null で解除されることだけを最小コストで確認する。
    [Fact]
    public async Task PlayCard_after_pending_manually_cleared_no_longer_returns_409()
    {
        // 注: 完全な resolve flow を回す代わりに、plan が許容する「直接 store で pending クリア」
        // approach を採用 (BattleControllerResolveChoiceTests T5 で実 resolve 経路は別途検証済)。
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            InjectPendingState(_factory.Services, accountId);

            // 一度 409 を確認
            var blocked = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(0, 0, 0));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

            // pending を直接クリア
            ClearPendingState(_factory.Services, accountId);

            // 再度 play-card → もう 409 ではない (200 or 400 どちらでも OK、guard 解除確認)
            var resp = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(0, 0, 0));
            Assert.NotEqual(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }
}
