using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Locks down the participant-aware battle command envelope. These tests
/// deliberately submit requests twice and with stale client-side identities:
/// validation must happen before Core or the shared battle RNG is touched.
/// </summary>
public sealed class BattleControllerCommandContractTests : IClassFixture<TempDataFactory>
{
    private const string PlayCardPath = "/api/v1/runs/current/battle/play-card";
    private const string ReadyPath = "/api/v1/runs/current/battle/set-ready";

    private readonly TempDataFactory _factory;

    public BattleControllerCommandContractTests(TempDataFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task PlayCard_with_stale_revision_returns_409_without_mutating_state_rng_or_journal()
    {
        var (client, accountId) = await StartBattleAsync("command-stale-revision");
        using (client)
        {
            var rng = new CountingRng();
            var original = SetPrimaryHand(accountId, rng,
                Card("stale-defend", "defend"));
            var participant = BattleParticipantAccess.GetPrimary(original.State);

            using var response = await client.PostAsJsonAsync(
                PlayCardPath,
                PlayRequest(
                    participant,
                    cardInstanceId: "stale-defend",
                    commandId: "stale-play",
                    expectedRevision: original.State.ActionRevision + 1));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.True(Store.TryGet(accountId, out var unchanged));
            Assert.Same(original.State, unchanged.State);
            Assert.Same(original.Rng, unchanged.Rng);
            Assert.Same(original.ActionJournal, unchanged.ActionJournal);
            Assert.Equal(0, rng.TotalCalls);
        }
    }

    [Fact]
    public async Task PlayCard_rejects_stale_actor_and_card_instance_identities()
    {
        var (client, accountId) = await StartBattleAsync("command-stale-identities");
        using (client)
        {
            var rng = new CountingRng();
            var original = SetPrimaryHand(accountId, rng,
                Card("identity-defend", "defend"));
            var participant = BattleParticipantAccess.GetPrimary(original.State);

            using var wrongActor = await client.PostAsJsonAsync(
                PlayCardPath,
                PlayRequest(
                    participant,
                    cardInstanceId: "identity-defend",
                    commandId: "wrong-actor",
                    expectedRevision: original.State.ActionRevision,
                    actorInstanceId: "stale-actor"));
            Assert.Equal(HttpStatusCode.Conflict, wrongActor.StatusCode);

            using var wrongCard = await client.PostAsJsonAsync(
                PlayCardPath,
                PlayRequest(
                    participant,
                    cardInstanceId: "stale-card",
                    commandId: "wrong-card",
                    expectedRevision: original.State.ActionRevision));
            Assert.Equal(HttpStatusCode.Conflict, wrongCard.StatusCode);

            Assert.True(Store.TryGet(accountId, out var unchanged));
            Assert.Same(original.State, unchanged.State);
            Assert.Same(original.ActionJournal, unchanged.ActionJournal);
            Assert.Equal(0, rng.TotalCalls);
        }
    }

    [Fact]
    public async Task PlayCard_retry_with_same_command_and_payload_executes_once()
    {
        var (client, accountId) = await StartBattleAsync("command-play-idempotent");
        using (client)
        {
            var rng = new CountingRng();
            var original = SetPrimaryHand(accountId, rng,
                Card("retry-defend", "defend"));
            var participant = BattleParticipantAccess.GetPrimary(original.State);
            var request = PlayRequest(
                participant,
                cardInstanceId: "retry-defend",
                commandId: "retry-play-once",
                expectedRevision: original.State.ActionRevision);

            using var firstResponse = await client.PostAsJsonAsync(PlayCardPath, request);
            firstResponse.EnsureSuccessStatusCode();
            var firstBody = await firstResponse.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(firstBody);
            Assert.True(Store.TryGet(accountId, out var afterFirst));
            int rngCallsAfterFirst = rng.TotalCalls;

            using var retryResponse = await client.PostAsJsonAsync(PlayCardPath, request);
            retryResponse.EnsureSuccessStatusCode();
            var retryBody = await retryResponse.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(retryBody);
            Assert.Empty(retryBody!.Steps);

            Assert.True(Store.TryGet(accountId, out var afterRetry));
            Assert.Same(afterFirst.State, afterRetry.State);
            Assert.Same(afterFirst.ActionJournal, afterRetry.ActionJournal);
            Assert.Equal(rngCallsAfterFirst, rng.TotalCalls);
            Assert.DoesNotContain(
                BattleParticipantAccess.GetPrimary(afterRetry.State).Hand,
                card => card.InstanceId == "retry-defend");
            Assert.Equal(
                firstBody!.State.ActionRevision,
                retryBody.State.ActionRevision);
            Assert.Single(afterRetry.ActionJournal.Entries, entry =>
                entry.CommandId == "retry-play-once"
                && entry.Kind == "playCard");
        }
    }

    [Fact]
    public async Task PlayCard_command_id_cannot_be_reused_for_a_different_card()
    {
        var (client, accountId) = await StartBattleAsync("command-play-conflict");
        using (client)
        {
            var original = SetPrimaryHand(accountId, null,
                Card("first-defend", "defend"),
                Card("second-defend", "defend"));
            var participant = BattleParticipantAccess.GetPrimary(original.State);

            using var first = await client.PostAsJsonAsync(
                PlayCardPath,
                PlayRequest(
                    participant,
                    cardInstanceId: "first-defend",
                    commandId: "reused-play-id",
                    expectedRevision: original.State.ActionRevision));
            first.EnsureSuccessStatusCode();

            Assert.True(Store.TryGet(accountId, out var afterFirst));
            var currentParticipant = BattleParticipantAccess.GetPrimary(afterFirst.State);
            int secondIndex = Enumerable.Range(0, currentParticipant.Hand.Length)
                .Single(index =>
                    currentParticipant.Hand[index].InstanceId == "second-defend");
            using var conflict = await client.PostAsJsonAsync(
                PlayCardPath,
                new PlayCardRequestDto(
                    HandIndex: secondIndex,
                    TargetEnemyIndex: null,
                    TargetAllyIndex: 0,
                    ParticipantId: currentParticipant.ParticipantId,
                    ActorInstanceId: currentParticipant.ActorInstanceId,
                    CardInstanceId: "second-defend",
                    CommandId: "reused-play-id",
                    ExpectedRevision: afterFirst.State.ActionRevision));

            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.True(Store.TryGet(accountId, out var afterConflict));
            Assert.Same(afterFirst.State, afterConflict.State);
            Assert.Single(afterConflict.ActionJournal.Entries, entry =>
                entry.CommandId == "reused-play-id");
        }
    }

    [Fact]
    public async Task Skill_command_id_cannot_be_reused_for_another_skill()
    {
        var (client, accountId) = await StartBattleAsync("command-skill-conflict");
        using (client)
        {
            var original = SetPrimaryResource(
                accountId,
                participant => participant with
                {
                    ComboGauge = BattleEngine.MaxComboGauge,
                });
            var participant = BattleParticipantAccess.GetPrimary(original.State);
            const string commandId = "reused-skill-id";

            using var first = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-tactical-skill",
                Command(participant, commandId, original.State.ActionRevision));
            first.EnsureSuccessStatusCode();
            Assert.True(Store.TryGet(accountId, out var afterFirst));

            using var conflict = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-ultimate-skill",
                Command(participant, commandId, afterFirst.State.ActionRevision));

            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.True(Store.TryGet(accountId, out var afterConflict));
            Assert.Same(afterFirst.State, afterConflict.State);
            Assert.Single(afterConflict.ActionJournal.Entries, entry =>
                entry.CommandId == commandId);
        }
    }

    [Fact]
    public async Task Ready_command_id_cannot_be_reused_for_a_different_value()
    {
        var (client, accountId) = await StartBattleAsync("command-ready-conflict");
        using (client)
        {
            Assert.True(Store.TryGet(accountId, out var original));
            var participant = BattleParticipantAccess.GetPrimary(original.State);
            const string commandId = "reused-ready-id";

            using var first = await client.PostAsJsonAsync(
                ReadyPath,
                ReadyRequest(
                    participant,
                    isReady: false,
                    commandId,
                    original.State.ActionRevision));
            first.EnsureSuccessStatusCode();
            Assert.True(Store.TryGet(accountId, out var afterFirst));

            using var conflict = await client.PostAsJsonAsync(
                ReadyPath,
                ReadyRequest(
                    participant,
                    isReady: true,
                    commandId,
                    afterFirst.State.ActionRevision));

            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.True(Store.TryGet(accountId, out var afterConflict));
            Assert.Same(afterFirst.State, afterConflict.State);
            Assert.Single(afterConflict.ActionJournal.Entries, entry =>
                entry.CommandId == commandId);
        }
    }

    [Fact]
    public async Task Ready_retry_after_all_ready_resolution_does_not_resolve_the_turn_twice()
    {
        var (client, accountId) = await StartBattleAsync("command-ready-idempotent");
        using (client)
        {
            var rng = new CountingRng();
            Assert.True(Store.TryGet(accountId, out var started));
            var original = started with { Rng = rng };
            Store.Set(accountId, original);
            var participant = BattleParticipantAccess.GetPrimary(original.State);
            const string commandId = "ready-resolve-once";
            var request = ReadyRequest(
                participant,
                isReady: true,
                commandId,
                original.State.ActionRevision);

            using var firstResponse = await client.PostAsJsonAsync(ReadyPath, request);
            firstResponse.EnsureSuccessStatusCode();
            Assert.True(Store.TryGet(accountId, out var afterFirst));
            int turnAfterFirst = afterFirst.State.Turn;
            int rngCallsAfterFirst = rng.TotalCalls;

            using var retryResponse = await client.PostAsJsonAsync(ReadyPath, request);
            retryResponse.EnsureSuccessStatusCode();

            Assert.True(Store.TryGet(accountId, out var afterRetry));
            Assert.Same(afterFirst.State, afterRetry.State);
            Assert.Same(afterFirst.ActionJournal, afterRetry.ActionJournal);
            Assert.Equal(turnAfterFirst, afterRetry.State.Turn);
            Assert.Equal(rngCallsAfterFirst, rng.TotalCalls);
            Assert.Single(afterRetry.ActionJournal.Entries, entry =>
                entry.CommandId == commandId && entry.Kind == "ready");
            Assert.Single(afterRetry.ActionJournal.Entries, entry =>
                entry.CommandId == $"{commandId}:resolution"
                && entry.Kind == "resolveTurn");
            var readyEntry = Assert.Single(afterRetry.ActionJournal.Entries, entry =>
                entry.CommandId == commandId && entry.Kind == "ready");
            var resolutionEntry = Assert.Single(afterRetry.ActionJournal.Entries, entry =>
                entry.CommandId == $"{commandId}:resolution");
            Assert.NotNull(readyEntry.StateBefore);
            Assert.NotNull(readyEntry.StateAfter);
            Assert.NotNull(resolutionEntry.StateBefore);
            Assert.NotNull(resolutionEntry.StateAfter);
            Assert.Equal(1, readyEntry.StateAfter!.Turn);
            Assert.Equal("PlayerInput", readyEntry.StateAfter.Phase);
            Assert.Equal(
                JsonSerializer.Serialize(readyEntry.StateAfter),
                JsonSerializer.Serialize(resolutionEntry.StateBefore));
            Assert.Equal(2, resolutionEntry.StateAfter!.Turn);
        }
    }

    [Theory]
    [InlineData("use-tactical-skill", "tactical")]
    [InlineData("use-ultimate-skill", "ultimate")]
    public async Task Fixed_skill_endpoint_accepts_participant_command_metadata(
        string endpoint,
        string expectedSkillId)
    {
        var (client, accountId) = await StartBattleAsync(
            $"command-fixed-skill-{expectedSkillId}");
        using (client)
        {
            var original = SetPrimaryResource(
                accountId,
                participant => participant with
                {
                    ComboGauge = BattleEngine.MaxComboGauge,
                });
            var participant = BattleParticipantAccess.GetPrimary(original.State);
            string commandId = $"metadata-{expectedSkillId}";

            using var response = await client.PostAsJsonAsync(
                $"/api/v1/runs/current/battle/{endpoint}",
                Command(participant, commandId, original.State.ActionRevision));

            response.EnsureSuccessStatusCode();
            Assert.True(Store.TryGet(accountId, out var updated));
            var entry = Assert.Single(updated.ActionJournal.Entries, entry =>
                entry.CommandId == commandId);
            Assert.Equal("useCharacterSkill", entry.Kind);
            Assert.Equal(participant.ParticipantId, entry.ParticipantId);
            Assert.Equal(participant.ActorInstanceId, entry.ActorInstanceId);
            Assert.Equal(expectedSkillId, entry.Ability?.DefinitionId);
        }
    }

    [Fact]
    public async Task Potion_and_pending_choice_reject_stale_resource_identities()
    {
        var (client, accountId) = await StartBattleAsync("command-resource-identities");
        using (client)
        {
            var candidate = Card("choice-candidate", "defend");
            var pending = new PendingCardPlay(
                CardInstanceId: "actual-pending-card",
                EffectIndex: 0,
                SummonedActorInstanceIdsBefore: ImmutableArray<string>.Empty,
                Choice: new PendingChoice(
                    Action: "exhaustCard",
                    Pile: "hand",
                    Count: 1,
                    CandidateInstanceIds: ImmutableArray.Create(candidate.InstanceId)));
            var original = SetPrimaryResource(
                accountId,
                participant => participant with
                {
                    Hand = ImmutableArray.Create(candidate),
                    Potions = ImmutableArray.Create("health_potion"),
                    PendingCardPlay = pending,
                });
            var participant = BattleParticipantAccess.GetPrimary(original.State);

            using var potionConflict = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-potion",
                new UsePotionRequestDto(
                    PotionIndex: 0,
                    TargetEnemyIndex: null,
                    TargetAllyIndex: 0,
                    ParticipantId: participant.ParticipantId,
                    ActorInstanceId: participant.ActorInstanceId,
                    PotionId: "stale-potion",
                    CommandId: "stale-potion-command",
                    ExpectedRevision: original.State.ActionRevision));
            Assert.Equal(HttpStatusCode.Conflict, potionConflict.StatusCode);

            using var choiceConflict = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/resolve-card-choice",
                new ResolveCardChoiceRequestDto(
                    SelectedInstanceIds: [candidate.InstanceId],
                    ParticipantId: participant.ParticipantId,
                    ActorInstanceId: participant.ActorInstanceId,
                    PendingCardInstanceId: "stale-pending-card",
                    CommandId: "stale-choice-command",
                    ExpectedRevision: original.State.ActionRevision));
            Assert.Equal(HttpStatusCode.Conflict, choiceConflict.StatusCode);

            Assert.True(Store.TryGet(accountId, out var unchanged));
            Assert.Same(original.State, unchanged.State);
            Assert.Same(original.ActionJournal, unchanged.ActionJournal);
        }
    }

    [Fact]
    public async Task PlayCard_command_fingerprint_includes_targets_and_expected_revision()
    {
        var (client, accountId) = await StartBattleAsync("command-play-full-payload");
        using (client)
        {
            var rng = new CountingRng();
            var original = SetPrimaryHand(accountId, rng,
                Card("full-payload-defend", "defend"));
            var participant = BattleParticipantAccess.GetPrimary(original.State);
            const string commandId = "play-full-payload";
            var request = PlayRequest(
                participant,
                cardInstanceId: "full-payload-defend",
                commandId,
                expectedRevision: original.State.ActionRevision);

            using var accepted = await client.PostAsJsonAsync(PlayCardPath, request);
            accepted.EnsureSuccessStatusCode();
            Assert.True(Store.TryGet(accountId, out var afterAccepted));
            int rngCalls = rng.TotalCalls;

            using var targetConflict = await client.PostAsJsonAsync(
                PlayCardPath,
                request with { TargetAllyIndex = 1 });
            Assert.Equal(HttpStatusCode.Conflict, targetConflict.StatusCode);
            AssertUnchanged(accountId, afterAccepted, rng, rngCalls);

            using var revisionConflict = await client.PostAsJsonAsync(
                PlayCardPath,
                request with { ExpectedRevision = afterAccepted.State.ActionRevision });
            Assert.Equal(HttpStatusCode.Conflict, revisionConflict.StatusCode);
            AssertUnchanged(accountId, afterAccepted, rng, rngCalls);
        }
    }

    [Fact]
    public async Task Potion_command_fingerprint_includes_slot_identity_and_targets()
    {
        var (client, accountId) = await StartBattleAsync("command-potion-full-payload");
        using (client)
        {
            var rng = new CountingRng();
            var original = SetPrimaryResource(
                accountId,
                participant => participant with
                {
                    Potions = ImmutableArray.Create("health_potion", "strength_potion"),
                    IsReady = false,
                },
                rng);
            var participant = BattleParticipantAccess.GetPrimary(original.State);
            const string commandId = "potion-full-payload";
            var request = new UsePotionRequestDto(
                PotionIndex: 0,
                TargetEnemyIndex: null,
                TargetAllyIndex: 0,
                ParticipantId: participant.ParticipantId,
                ActorInstanceId: participant.ActorInstanceId,
                PotionId: "health_potion",
                CommandId: commandId,
                ExpectedRevision: original.State.ActionRevision);

            using var accepted = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-potion", request);
            accepted.EnsureSuccessStatusCode();
            Assert.True(Store.TryGet(accountId, out var afterAccepted));
            int rngCalls = rng.TotalCalls;

            using var slotConflict = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-potion",
                request with { PotionIndex = 1, PotionId = "strength_potion" });
            Assert.Equal(HttpStatusCode.Conflict, slotConflict.StatusCode);
            AssertUnchanged(accountId, afterAccepted, rng, rngCalls);

            using var targetConflict = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/use-potion",
                request with { TargetAllyIndex = null, TargetEnemyIndex = 0 });
            Assert.Equal(HttpStatusCode.Conflict, targetConflict.StatusCode);
            AssertUnchanged(accountId, afterAccepted, rng, rngCalls);
        }
    }

    [Fact]
    public async Task Choice_command_fingerprint_includes_normalized_selected_ids()
    {
        var (client, accountId) = await StartBattleAsync("command-choice-full-payload");
        using (client)
        {
            const string definitionId = "idempotency_choose_card";
            InjectChooseCard(definitionId);
            var rng = new CountingRng();
            var choose = Card("choose-card", definitionId);
            var first = Card("choose-first", "defend");
            var second = Card("choose-second", "defend");
            var pending = new PendingCardPlay(
                CardInstanceId: choose.InstanceId,
                EffectIndex: 0,
                SummonedActorInstanceIdsBefore: ImmutableArray<string>.Empty,
                Choice: new PendingChoice(
                    Action: "exhaustCard",
                    Pile: "hand",
                    Count: 1,
                    CandidateInstanceIds: ImmutableArray.Create(
                        first.InstanceId, second.InstanceId)));
            var original = SetPrimaryResource(
                accountId,
                participant => participant with
                {
                    Energy = 10,
                    Hand = ImmutableArray.Create(choose, first, second),
                    PendingCardPlay = pending,
                    IsReady = false,
                },
                rng);
            var participant = BattleParticipantAccess.GetPrimary(original.State);
            const string commandId = "choice-full-payload";
            var request = new ResolveCardChoiceRequestDto(
                SelectedInstanceIds: [first.InstanceId],
                ParticipantId: participant.ParticipantId,
                ActorInstanceId: participant.ActorInstanceId,
                PendingCardInstanceId: choose.InstanceId,
                CommandId: commandId,
                ExpectedRevision: original.State.ActionRevision);

            using var accepted = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/resolve-card-choice", request);
            accepted.EnsureSuccessStatusCode();
            Assert.True(Store.TryGet(accountId, out var afterAccepted));
            int rngCalls = rng.TotalCalls;

            using var conflict = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/resolve-card-choice",
                request with { SelectedInstanceIds = [second.InstanceId] });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            AssertUnchanged(accountId, afterAccepted, rng, rngCalls);
        }
    }

    [Fact]
    public async Task User_command_id_can_equal_a_server_derived_correlation_without_collision()
    {
        var (client, accountId) = await StartBattleAsync("command-derived-id-isolation");
        using (client)
        {
            var rng = new CountingRng();
            Assert.True(Store.TryGet(accountId, out var started));
            Store.Set(accountId, started with { Rng = rng });
            var participant = BattleParticipantAccess.GetPrimary(started.State);

            using var first = await client.PostAsJsonAsync(
                ReadyPath,
                ReadyRequest(
                    participant,
                    isReady: false,
                    commandId: "collision:resolution",
                    expectedRevision: started.State.ActionRevision));
            first.EnsureSuccessStatusCode();
            Assert.True(Store.TryGet(accountId, out var afterFirst));
            var current = BattleParticipantAccess.GetPrimary(afterFirst.State);

            using var second = await client.PostAsJsonAsync(
                ReadyPath,
                ReadyRequest(
                    current,
                    isReady: true,
                    commandId: "collision",
                    expectedRevision: afterFirst.State.ActionRevision));
            second.EnsureSuccessStatusCode();

            Assert.True(Store.TryGet(accountId, out var completed));
            var sameCorrelation = completed.ActionJournal.Entries
                .Where(entry => entry.CommandId == "collision:resolution")
                .ToArray();
            Assert.Equal(2, sameCorrelation.Length);
            Assert.Equal(2, sameCorrelation.Select(entry => entry.EntryId).Distinct().Count());
            Assert.True(completed.CommandReceipts.ContainsKey("collision"));
            Assert.True(completed.CommandReceipts.ContainsKey("collision:resolution"));
        }
    }

    [Fact]
    public async Task Human_command_snapshot_contains_ready_reopen_before_first_cpu_timeline_item()
    {
        var (client, accountId) = await StartBattleAsync("command-reopen-snapshot-boundary");
        using (client)
        {
            const string grantCardId = "test_grant_all_allies_energy";
            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            var cards = Assert.IsAssignableFrom<IDictionary<string, CardDefinition>>(
                catalog.Cards);
            cards[grantCardId] = new CardDefinition(
                Id: grantCardId,
                Name: grantCardId,
                DisplayName: null,
                Rarity: CardRarity.Common,
                CardType: CardType.Skill,
                Cost: 0,
                UpgradedCost: null,
                Effects:
                [
                    new CardEffect(
                        "gainEnergy", EffectScope.All, EffectSide.Ally, 1),
                ],
                UpgradedEffects: null,
                Keywords: null);

            Assert.True(Store.TryGet(accountId, out var started));
            var primary = BattleParticipantAccess.GetPrimary(started.State) with
            {
                Energy = 0,
                Hand = [Card("grant-card-instance", grantCardId)],
                DrawPile = [],
                DiscardPile = [],
                ExhaustPile = [],
                IsReady = false,
            };
            var hero = started.State.Allies.Single(actor => string.Equals(
                actor.InstanceId, primary.ActorInstanceId, StringComparison.Ordinal));
            var secondaryActor = hero with
            {
                InstanceId = "secondary-human-actor",
                SlotIndex = 1,
                OwnerPlayerSlot = 1,
                OwnerActorInstanceId = null,
            };
            var cpuActor = hero with
            {
                InstanceId = "timeline-cpu-actor",
                SlotIndex = 2,
                OwnerPlayerSlot = 0,
                OwnerActorInstanceId = hero.InstanceId,
            };
            var secondary = primary with
            {
                ParticipantId = "secondary-human",
                ActorInstanceId = secondaryActor.InstanceId,
                ControllerKind = BattleParticipantControllerKind.Human,
                OwnerPlayerSlot = 1,
                Energy = 0,
                Hand = [Card("secondary-defend", "defend")],
                CardUsesThisTurn = ImmutableDictionary<string, int>.Empty,
                IsReady = true,
            };
            var cpu = secondary with
            {
                ParticipantId = "timeline-cpu",
                ActorInstanceId = cpuActor.InstanceId,
                ControllerKind = BattleParticipantControllerKind.Cpu,
                OwnerPlayerSlot = 0,
                Hand = [],
                IsReady = false,
            };
            var customState = started.State with
            {
                Allies = [hero, secondaryActor, cpuActor],
                Participants = [primary, secondary, cpu],
            };
            customState = BattleParticipantAccess.ProjectToRoot(
                customState, primary.ParticipantId);
            Store.Set(accountId, started with { State = customState });
            const string commandId = "human-reopen-boundary";

            using var response = await client.PostAsJsonAsync(
                PlayCardPath,
                PlayRequest(
                    primary,
                    cardInstanceId: "grant-card-instance",
                    commandId,
                    expectedRevision: customState.ActionRevision));
            response.EnsureSuccessStatusCode();

            Assert.True(Store.TryGet(accountId, out var completed));
            var command = Assert.Single(completed.ActionJournal.Entries.Where(entry =>
                entry.CommandId == commandId));
            var cpuReady = Assert.Single(completed.ActionJournal.Entries.Where(entry =>
                entry.CommandId.StartsWith(
                    $"{commandId}:cpu-ready:", StringComparison.Ordinal)));
            Assert.False(command.StateAfter!.Participants!.Single(participant =>
                participant.ParticipantId == secondary.ParticipantId).IsReady);
            Assert.Equal(
                JsonSerializer.Serialize(command.StateAfter),
                JsonSerializer.Serialize(cpuReady.StateBefore));
        }
    }

    private BattleSessionStore Store =>
        _factory.Services.GetRequiredService<BattleSessionStore>();

    private async Task<(HttpClient Client, string AccountId)> StartBattleAsync(
        string accountId)
    {
        var setup = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId);
        (await setup.client.PostAsync(
            "/api/v1/runs/current/battle/start", null)).EnsureSuccessStatusCode();
        return (setup.client, setup.accountId);
    }

    private BattleSession SetPrimaryHand(
        string accountId,
        IRng? rng,
        params BattleCardInstance[] cards) =>
        SetPrimaryResource(
            accountId,
            participant => participant with
            {
                Energy = 10,
                Hand = cards.ToImmutableArray(),
                PendingCardPlay = null,
                IsReady = false,
            },
            rng);

    private BattleSession SetPrimaryResource(
        string accountId,
        Func<BattleParticipantState, BattleParticipantState> update,
        IRng? rng = null)
    {
        Assert.True(Store.TryGet(accountId, out var session));
        var primary = update(BattleParticipantAccess.GetPrimary(session.State));
        var updated = session with
        {
            State = BattleParticipantAccess.Replace(session.State, primary),
            Rng = rng ?? session.Rng,
        };
        Store.Set(accountId, updated);
        return updated;
    }

    private static BattleCardInstance Card(string instanceId, string definitionId) =>
        new(
            InstanceId: instanceId,
            CardDefinitionId: definitionId,
            IsUpgraded: false,
            CostOverride: null);

    private void InjectChooseCard(string definitionId)
    {
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var cards = Assert.IsAssignableFrom<IDictionary<string, CardDefinition>>(catalog.Cards);
        cards[definitionId] = new CardDefinition(
            Id: definitionId,
            Name: definitionId,
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects:
            [
                new CardEffect(
                    "exhaustCard", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "choose"),
            ],
            UpgradedEffects: null,
            Keywords: null);
    }

    private void AssertUnchanged(
        string accountId,
        BattleSession expected,
        CountingRng rng,
        int expectedRngCalls)
    {
        Assert.True(Store.TryGet(accountId, out var actual));
        Assert.Same(expected.State, actual.State);
        Assert.Same(expected.ActionJournal, actual.ActionJournal);
        Assert.Same(expected.CommandReceipts, actual.CommandReceipts);
        Assert.Equal(expectedRngCalls, rng.TotalCalls);
    }

    private static PlayCardRequestDto PlayRequest(
        BattleParticipantState participant,
        string cardInstanceId,
        string commandId,
        long expectedRevision,
        string? actorInstanceId = null) =>
        new(
            HandIndex: 0,
            TargetEnemyIndex: null,
            TargetAllyIndex: 0,
            ParticipantId: participant.ParticipantId,
            ActorInstanceId: actorInstanceId ?? participant.ActorInstanceId,
            CardInstanceId: cardInstanceId,
            CommandId: commandId,
            ExpectedRevision: expectedRevision);

    private static BattleCommandRequestDto Command(
        BattleParticipantState participant,
        string commandId,
        long expectedRevision) =>
        new(
            ParticipantId: participant.ParticipantId,
            ActorInstanceId: participant.ActorInstanceId,
            CommandId: commandId,
            ExpectedRevision: expectedRevision);

    private static SetBattleParticipantReadyRequestDto ReadyRequest(
        BattleParticipantState participant,
        bool isReady,
        string commandId,
        long expectedRevision) =>
        new(
            ParticipantId: participant.ParticipantId,
            IsReady: isReady,
            ExpectedRevision: expectedRevision,
            ActorInstanceId: participant.ActorInstanceId,
            CommandId: commandId);

    private sealed class CountingRng : IRng
    {
        public int IntCalls { get; private set; }
        public int DoubleCalls { get; private set; }
        public int TotalCalls => IntCalls + DoubleCalls;

        public int NextInt(int minInclusive, int maxExclusive)
        {
            IntCalls++;
            return minInclusive;
        }

        public double NextDouble()
        {
            DoubleCalls++;
            return 0d;
        }
    }
}
