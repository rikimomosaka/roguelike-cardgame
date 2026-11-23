using System.Net;
using System.Net.Http.Json;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class BattleControllerParticipantTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleControllerParticipantTests(TempDataFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Start_projects_primary_participant_and_action_revision()
    {
        var (client, _) = await BattleControllerFixtures
            .SetupRunWithActiveBattleAsync(_factory, "participant-start");
        using (client)
        {
            var response = await client.PostAsync(
                "/api/v1/runs/current/battle/start", null);
            response.EnsureSuccessStatusCode();
            var body = await response.Content
                .ReadFromJsonAsync<BattleActionResponseDto>();

            var participant = Assert.Single(body!.State.Participants!);
            Assert.Equal("player:0", participant.ParticipantId);
            Assert.Equal("Human", participant.ControllerKind);
            Assert.Equal(body.State.Energy, participant.Energy);
            Assert.Equal(body.State.Hand.Count, participant.Hand.Count);
            Assert.Equal(0, body.State.ActionRevision);
        }
    }

    [Fact]
    public async Task SetReady_checks_revision_and_is_visible_in_replay()
    {
        var (client, _) = await BattleControllerFixtures
            .SetupRunWithActiveBattleAsync(_factory, "participant-ready");
        using (client)
        {
            var start = await client.PostAsync(
                "/api/v1/runs/current/battle/start", null);
            var started = await start.Content
                .ReadFromJsonAsync<BattleActionResponseDto>();
            var participantId = Assert.Single(started!.State.Participants!)
                .ParticipantId;

            var stale = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/set-ready",
                new SetBattleParticipantReadyRequestDto(
                    participantId, false, ExpectedRevision: 99));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var accepted = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/set-ready",
                new SetBattleParticipantReadyRequestDto(
                    participantId, false, started.State.ActionRevision));
            accepted.EnsureSuccessStatusCode();
            var after = await accepted.Content
                .ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.True(after!.State.ActionRevision > started.State.ActionRevision);
            Assert.False(Assert.Single(after.State.Participants!).IsReady);

            var replayResponse = await client.GetAsync(
                $"/api/v1/runs/current/battle/replay?participantId={participantId}&afterSequence=0");
            replayResponse.EnsureSuccessStatusCode();
            var replay = await replayResponse.Content
                .ReadFromJsonAsync<BattleReplayDto>();
            var entry = Assert.Single(replay!.Entries);
            Assert.Equal("ready", entry.Kind);
            Assert.Equal(participantId, entry.ParticipantId);
            Assert.False(Assert.Single(entry.FeedChanges).IsReady);
        }
    }

    [Fact]
    public async Task Enemy_card_plays_planned_at_turn_start_are_replayable()
    {
        var (client, _) = await BattleControllerFixtures
            .SetupRunWithActiveBattleAsync(_factory, "enemy-card-replay");
        using (client)
        {
            var start = await client.PostAsync(
                "/api/v1/runs/current/battle/start", null);
            start.EnsureSuccessStatusCode();
            var started = await start.Content
                .ReadFromJsonAsync<BattleActionResponseDto>();
            // 全敵カード制御: 敵ごとにカード資源の概要が公開される（手札の中身は非公開）。
            var enemy = started!.State.Enemies[0];
            var enemyParticipant = Assert.Single(
                started.State.EnemyParticipants!,
                participant => participant.ActorInstanceId == enemy.InstanceId);

            var replayResponse = await client.GetAsync(
                $"/api/v1/runs/current/battle/replay?participantId={Uri.EscapeDataString(enemyParticipant.ParticipantId)}&afterSequence=0");
            replayResponse.EnsureSuccessStatusCode();
            var replay = await replayResponse.Content
                .ReadFromJsonAsync<BattleReplayDto>();

            Assert.NotEmpty(replay!.Entries);
            Assert.All(replay.Entries, entry =>
            {
                Assert.Equal("playCard", entry.Kind);
                Assert.Equal(enemy.InstanceId, entry.ActorInstanceId);
                Assert.NotNull(entry.Card);
                Assert.StartsWith("enemy_", entry.Card!.DefinitionId);
            });
            // 予約した攻撃・ブロックが「予定」として記録される。
            Assert.Contains(replay.Entries, entry => entry.Intents.Count > 0);
        }
    }

    [Fact]
    public async Task Replay_rejects_negative_sequence()
    {
        var (client, _) = await BattleControllerFixtures
            .SetupRunWithActiveBattleAsync(_factory, "participant-replay-invalid");
        using (client)
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var response = await client.GetAsync(
                "/api/v1/runs/current/battle/replay?afterSequence=-1");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Summoning_card_controlled_ally_plans_immediately_and_journals_actions()
    {
        const string accountId = "participant-summon";
        var (client, _) = await BattleControllerFixtures
            .SetupRunWithActiveBattleAsync(_factory, accountId);
        using (client)
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            var primary = BattleParticipantAccess.GetPrimary(session.State) with
            {
                Energy = 10,
                Hand = ImmutableArray.Create(new BattleCardInstance(
                    "forced-wisp-summon",
                    "starter_summon_3",
                    IsUpgraded: false,
                    CostOverride: null)),
            };
            store.Set(accountId, session with
            {
                State = BattleParticipantAccess.Replace(session.State, primary),
            });

            var response = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(0, 0, 0));
            response.EnsureSuccessStatusCode();
            var body = await response.Content
                .ReadFromJsonAsync<BattleActionResponseDto>();

            var cpu = Assert.Single(
                body!.State.Participants!,
                participant => participant.ControllerKind == "Cpu");
            Assert.True(cpu.IsReady);
            Assert.Equal("wisp", cpu.CharacterId);

            var driveCpu = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/set-ready",
                new SetBattleParticipantReadyRequestDto(
                    cpu.ParticipantId, false, body.State.ActionRevision));
            Assert.Equal(HttpStatusCode.BadRequest, driveCpu.StatusCode);

            var replayResponse = await client.GetAsync(
                $"/api/v1/runs/current/battle/replay?participantId={cpu.ParticipantId}");
            replayResponse.EnsureSuccessStatusCode();
            var replay = await replayResponse.Content
                .ReadFromJsonAsync<BattleReplayDto>();
            Assert.Contains(replay!.Entries, entry =>
                entry.Kind == "playCard"
                && entry.ParticipantId == cpu.ParticipantId
                && entry.Card is not null);
            Assert.True(replay.IsReady);

            var global = await client.GetFromJsonAsync<BattleReplayDto>(
                "/api/v1/runs/current/battle/replay");
            var sourcePlay = Assert.Single(global!.Entries, entry =>
                entry.Kind == "playCard"
                && entry.ParticipantId == primary.ParticipantId);
            var firstCpuPlay = global.Entries.First(entry =>
                entry.Kind == "playCard"
                && entry.ParticipantId == cpu.ParticipantId);
            Assert.NotNull(sourcePlay.Card);
            Assert.True(sourcePlay.Sequence < firstCpuPlay.Sequence);

            var endTurn = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/end-turn",
                new BattleCommandRequestDto(
                    primary.ParticipantId,
                    primary.ActorInstanceId,
                    "participant-legacy-end-turn",
                    body.State.ActionRevision));
            endTurn.EnsureSuccessStatusCode();
            var nextTurn = await endTurn.Content
                .ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.True(nextTurn!.State.Turn > body.State.Turn);
            var nextCpu = Assert.Single(
                nextTurn.State.Participants!,
                participant => participant.ParticipantId == cpu.ParticipantId);
            Assert.True(nextCpu.IsReady);

            var updatedReplay = await client.GetFromJsonAsync<BattleReplayDto>(
                $"/api/v1/runs/current/battle/replay?participantId={cpu.ParticipantId}");
            Assert.Contains(updatedReplay!.Entries, entry =>
                entry.Turn == nextTurn.State.Turn
                && entry.Kind == "playCard"
                && entry.Card?.CardInstanceId.StartsWith(
                    $"cpu-card:{cpu.ActorInstanceId}:",
                    StringComparison.Ordinal) == true);
            var sharedResolution = Assert.Single(
                updatedReplay.Entries,
                entry => entry.IsResolution);
            Assert.Equal("resolution", sharedResolution.ViewKind);
            Assert.Null(sharedResolution.ParticipantId);
            Assert.NotEmpty(sharedResolution.Effects);
            var nextFeed = await client.GetFromJsonAsync<BattleReplayDto>(
                $"/api/v1/runs/current/battle/replay?turn={nextTurn.State.Turn}" +
                $"&participantId={cpu.ParticipantId}");
            Assert.True(nextFeed!.IsReady);

            var completeGlobal = await client.GetFromJsonAsync<BattleReplayDto>(
                "/api/v1/runs/current/battle/replay");
            for (int index = 1; index < completeGlobal!.Entries.Count; index++)
            {
                Assert.True(
                    completeGlobal.Entries[index].RevisionBefore
                    >= completeGlobal.Entries[index - 1].RevisionAfter,
                    $"Replay revision regressed at sequence "
                    + $"{completeGlobal.Entries[index].Sequence}.");
            }
        }
    }
}
