using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class BattleControllerComboSkillTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleControllerComboSkillTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_projects_combo_gauge_and_combo_reduction_only_for_current_hand()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "combo-skill-dto");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();

            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            Assert.NotEmpty(session.State.Hand);
            Assert.NotEmpty(session.State.DrawPile);

            var modifier = new CardCostModifier(
                Amount: -1,
                CardCostModifierDuration.UntilTurnEnd,
                SourceId: "controller-test");
            var handCard = (session.State.Hand[0] with { CostOverride = 2 })
                .AddCostModifier(modifier);
            var drawCard = (session.State.DrawPile[0] with { CostOverride = 2 })
                .AddCostModifier(modifier);
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    ComboGauge = 7,
                    Hand = session.State.Hand.SetItem(0, handCard),
                    DrawPile = session.State.DrawPile.SetItem(0, drawCard),
                    NextCardComboFreePass = true,
                },
            });

            var response = await client.GetAsync("/api/v1/runs/current/battle");
            response.EnsureSuccessStatusCode();
            var jsonText = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(jsonText);
            var dto = JsonSerializer.Deserialize<BattleStateDto>(
                jsonText, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Equal(7, json.RootElement.GetProperty("comboGauge").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("ComboGauge", out _));
            Assert.NotNull(dto);
            var projectedHand = Assert.Single(
                dto!.Hand, card => card.InstanceId == handCard.InstanceId);
            var projectedDraw = Assert.Single(
                dto.DrawPile, card => card.InstanceId == drawCard.InstanceId);
            Assert.Equal(0, projectedHand.CurrentCost);
            Assert.Equal(1, projectedHand.CostOverride); // legacy client applies combo -1
            Assert.Equal(1, projectedDraw.CurrentCost);
            Assert.Equal(1, projectedDraw.CostOverride);
            Assert.NotNull(projectedHand.OriginalCost);
            Assert.NotNull(projectedDraw.OriginalCost);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Use_tactical_skill_updates_session_preserves_rng_and_returns_action_response()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "combo-skill-tactical");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();

            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            var originalRng = session.Rng;
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    ComboGauge = BattleEngine.TacticalSkillGaugeCost,
                },
            });

            var response = await client.PostAsync(
                "/api/v1/runs/current/battle/use-tactical-skill", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);
            Assert.Equal(0, body!.State.ComboGauge);
            Assert.Contains(BattleEngine.TacticalSkillId,
                body.State.UsedCharacterSkillIdsThisTurn);
            Assert.Contains(body.Steps, step =>
                step.Event.Kind == "UseCharacterSkill" &&
                step.Event.Amount == BattleEngine.TacticalSkillGaugeCost &&
                step.Event.Note == "tactical");
            Assert.Contains(body.Steps, step =>
                step.Event.Kind == "ComboGaugeChanged" &&
                step.Event.Amount == -BattleEngine.TacticalSkillGaugeCost);

            Assert.True(store.TryGet(accountId, out var updatedSession));
            Assert.Same(originalRng, updatedSession.Rng);
            Assert.Equal(body.State.ComboGauge, updatedSession.State.ComboGauge);

            var restored = await client.GetFromJsonAsync<BattleStateDto>(
                "/api/v1/runs/current/battle");
            Assert.Equal(body.State.ComboGauge, restored!.ComboGauge);
            Assert.Contains(BattleEngine.TacticalSkillId,
                restored.UsedCharacterSkillIdsThisTurn);

            store.Set(accountId, updatedSession with
            {
                State = updatedSession.State with
                {
                    ComboGauge = BattleEngine.TacticalSkillGaugeCost,
                },
            });
            var secondUse = await client.PostAsync(
                "/api/v1/runs/current/battle/use-tactical-skill", null);
            Assert.Equal(HttpStatusCode.BadRequest, secondUse.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Use_ultimate_skill_projects_current_hand_cost_modifiers_and_persists_state()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "combo-skill-ultimate");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();

            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            Assert.NotEmpty(session.State.Hand);
            var originalRng = session.Rng;
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    ComboGauge = BattleEngine.UltimateSkillGaugeCost,
                },
            });

            var response = await client.PostAsync(
                "/api/v1/runs/current/battle/use-ultimate-skill", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);
            Assert.Equal(0, body!.State.ComboGauge);
            Assert.NotEmpty(body.State.Hand);
            Assert.All(body.State.Hand, card =>
            {
                Assert.Equal(0, card.CurrentCost);
                Assert.Equal(0, card.CostOverride);
                Assert.NotNull(card.OriginalCost);
            });
            Assert.Contains(body.Steps, step =>
                step.Event.Kind == "UseCharacterSkill" &&
                step.Event.Amount == BattleEngine.UltimateSkillGaugeCost &&
                step.Event.Note == "ultimate");

            Assert.True(store.TryGet(accountId, out var updatedSession));
            Assert.Same(originalRng, updatedSession.Rng);
            Assert.All(updatedSession.State.Hand, card => Assert.Contains(
                card.EffectiveCostModifiers,
                modifier => modifier.Duration == CardCostModifierDuration.UntilTurnEnd
                    && modifier.SourceId == BattleEngine.UltimateSkillId
                    && modifier.Amount < 0));

            var restored = await client.GetFromJsonAsync<BattleStateDto>(
                "/api/v1/runs/current/battle");
            Assert.All(restored!.Hand, card => Assert.Equal(0, card.CurrentCost));
        }
        finally
        {
            client.Dispose();
        }
    }

    [Theory]
    [InlineData("use-tactical-skill")]
    [InlineData("use-ultimate-skill")]
    public async Task Skill_when_no_session_returns_409(string action)
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: $"combo-skill-no-session-{action}");
        try
        {
            var response = await client.PostAsync(
                $"/api/v1/runs/current/battle/{action}", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Theory]
    [InlineData("use-tactical-skill")]
    [InlineData("use-ultimate-skill")]
    public async Task Skill_with_insufficient_gauge_returns_400_and_does_not_replace_session(
        string action)
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: $"combo-skill-insufficient-{action}");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            store.Set(accountId, session with
            {
                State = session.State with { ComboGauge = 0 },
            });

            var response = await client.PostAsync(
                $"/api/v1/runs/current/battle/{action}", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(store.TryGet(accountId, out var unchanged));
            Assert.Same(session.Rng, unchanged.Rng);
            Assert.Equal(0, unchanged.State.ComboGauge);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Theory]
    [InlineData("use-tactical-skill")]
    [InlineData("use-ultimate-skill")]
    public async Task Skill_outside_player_input_returns_400_from_core_guard(string action)
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: $"combo-skill-phase-{action}");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    Phase = BattlePhase.EnemyAttacking,
                    ComboGauge = BattleEngine.MaxComboGauge,
                },
            });

            var response = await client.PostAsync(
                $"/api/v1/runs/current/battle/{action}", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Theory]
    [InlineData("use-tactical-skill")]
    [InlineData("use-ultimate-skill")]
    public async Task Skill_while_card_choice_is_pending_returns_400_from_core_guard(string action)
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: $"combo-skill-pending-{action}");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            var pending = new PendingCardPlay(
                CardInstanceId: "pending-card",
                EffectIndex: 0,
                SummonSucceededBefore: false,
                Choice: new PendingChoice(
                    Action: "exhaustCard",
                    Pile: "hand",
                    Count: 1,
                    CandidateInstanceIds: ImmutableArray.Create("candidate")));
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    PendingCardPlay = pending,
                    ComboGauge = BattleEngine.MaxComboGauge,
                },
            });

            var response = await client.PostAsync(
                $"/api/v1/runs/current/battle/{action}", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }
}
