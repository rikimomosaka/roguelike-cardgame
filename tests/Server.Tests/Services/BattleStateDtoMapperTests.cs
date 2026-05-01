using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Controllers;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

/// <summary>
/// Phase 10.5.C: hero (caster) の statuses が CardActorContext として
/// formatter に渡され、各 pile のカードの AdjustedDescription / AdjustedUpgradedDescription
/// が populate されることを integration レベルで検証する。
/// </summary>
public class BattleStateDtoMapperTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleStateDtoMapperTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task BattleStart_no_buff_emits_unmodified_marker_in_hand()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-no-buff");
        try
        {
            var res = await client.PostAsync("/api/v1/runs/current/battle/start", null);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);

            // hero に buff 無し → strike (attack 6 single) は無修飾 [N:6] のまま。
            // 5 枚の strike + 5 枚の defend を含むデッキを想定。
            var allCards = body!.State.Hand
                .Concat(body.State.DrawPile)
                .Concat(body.State.DiscardPile)
                .ToList();
            Assert.NotEmpty(allCards);
            // 全カードに adjustedDescription が populate されている。
            Assert.All(allCards, c =>
            {
                Assert.NotNull(c.AdjustedDescription);
                Assert.DoesNotContain("|up]", c.AdjustedDescription);
                Assert.DoesNotContain("|down]", c.AdjustedDescription);
            });

            // strike (attack 6) の adjustedDescription は無修飾の [N:6]。
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "strike");
            if (strike is not null)
            {
                Assert.Equal("敵 1 体に [N:6] ダメージ。", strike.AdjustedDescription);
            }
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Hero_with_strength_emits_up_marker_in_hand_descriptions()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-strength");
        try
        {
            // session を作るために start を呼ぶ
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            // hero に strength=2 を直接付与し、再度 GET で adjustedDescription を確認
            ApplyHeroStatus(_factory.Services, accountId, "strength", 2);

            var res = await client.GetAsync("/api/v1/runs/current/battle");
            res.EnsureSuccessStatusCode();
            var state = await res.Content.ReadFromJsonAsync<BattleStateDto>();
            Assert.NotNull(state);

            var allCards = state!.Hand
                .Concat(state.DrawPile)
                .Concat(state.DiscardPile)
                .ToList();
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "strike");
            Assert.NotNull(strike);
            // 6 + 2 = 8、base 6 と異なるので up 修飾。
            Assert.Equal("敵 1 体に [N:8|up] ダメージ。", strike!.AdjustedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Hero_with_weak_emits_down_marker_in_hand_descriptions()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-weak");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            ApplyHeroStatus(_factory.Services, accountId, "weak", 1);

            var res = await client.GetAsync("/api/v1/runs/current/battle");
            res.EnsureSuccessStatusCode();
            var state = await res.Content.ReadFromJsonAsync<BattleStateDto>();
            Assert.NotNull(state);

            var allCards = state!.Hand
                .Concat(state.DrawPile)
                .Concat(state.DiscardPile)
                .ToList();
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "strike");
            Assert.NotNull(strike);
            // 6 * 0.75 = 4.5 → floor 4、base 6 と異なるので down 修飾。
            Assert.Equal("敵 1 体に [N:4|down] ダメージ。", strike!.AdjustedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Hero_with_dexterity_emits_up_on_block_cards()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-dex");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            ApplyHeroStatus(_factory.Services, accountId, "dexterity", 3);

            var res = await client.GetAsync("/api/v1/runs/current/battle");
            res.EnsureSuccessStatusCode();
            var state = await res.Content.ReadFromJsonAsync<BattleStateDto>();
            Assert.NotNull(state);

            var allCards = state!.Hand
                .Concat(state.DrawPile)
                .Concat(state.DiscardPile)
                .ToList();
            var defend = allCards.FirstOrDefault(c => c.CardDefinitionId == "defend");
            Assert.NotNull(defend);
            // defend は block scope=Single side=Ally なので "味方 1 体にブロック ..." 文言。
            // dexterity=3 で 5 + 3 = 8、base 5 と異なるので up 修飾。
            Assert.Contains("|up]", defend!.AdjustedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// session 内の hero に status を直接付与するヘルパー。
    /// engine 経路を通さずに statuses を更新するため、formatter context のみを試験できる。
    /// </summary>
    private static void ApplyHeroStatus(
        System.IServiceProvider services, string accountId, string statusId, int amount)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        if (!store.TryGet(accountId, out var session))
            throw new System.InvalidOperationException(
                $"ApplyHeroStatus: session not found for accountId={accountId}");
        var newAllies = session.State.Allies.Select(a =>
        {
            if (a.DefinitionId != "hero") return a;
            var newStatuses = a.Statuses.SetItem(statusId, amount);
            return a with { Statuses = newStatuses };
        }).ToImmutableArray();
        var newState = session.State with { Allies = newAllies };
        store.Set(accountId, session with { State = newState });
    }

    [Fact]
    public async Task BattleStart_populates_adjustedUpgradedDescription_for_upgradable_cards()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-upgraded");
        try
        {
            var res = await client.PostAsync("/api/v1/runs/current/battle/start", null);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);

            var allCards = body!.State.Hand
                .Concat(body.State.DrawPile)
                .Concat(body.State.DiscardPile)
                .ToList();

            // strike は IsUpgradable なので AdjustedUpgradedDescription も非 null。
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "strike");
            Assert.NotNull(strike);
            Assert.NotNull(strike!.AdjustedUpgradedDescription);
            Assert.Equal("敵 1 体に [N:9] ダメージ。", strike.AdjustedUpgradedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }
}
