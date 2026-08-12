using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class BattleControllerConcurrencyTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleControllerConcurrencyTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task Concurrent_play_card_and_tactical_skill_preserve_both_state_changes()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "battle-command-serialization");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();

            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            var card = new BattleCardInstance(
                InstanceId: "concurrent-defend",
                CardDefinitionId: "defend",
                IsUpgraded: false,
                CostOverride: null);
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    Energy = 10,
                    Hand = ImmutableArray.Create(card),
                    ComboCount = 0,
                    MaxComboThisTurn = 0,
                    ComboGauge = BattleEngine.TacticalSkillGaugeCost,
                    LastPlayedOrigCost = null,
                    NextCardComboFreePass = false,
                    PendingCardPlay = null,
                },
            });

            var heldLock = await store.AcquireCommandLockAsync(
                accountId, CancellationToken.None);
            var playTask = client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(HandIndex: 0, TargetEnemyIndex: null, TargetAllyIndex: 0));
            var tacticalTask = client.PostAsync(
                "/api/v1/runs/current/battle/use-tactical-skill", null);

            await Task.Delay(150);
            bool playWaitedForLock = !playTask.IsCompleted;
            bool tacticalWaitedForLock = !tacticalTask.IsCompleted;
            await heldLock.DisposeAsync();

            using var playResponse = await playTask;
            using var tacticalResponse = await tacticalTask;
            Assert.True(playWaitedForLock);
            Assert.True(tacticalWaitedForLock);
            Assert.Equal(HttpStatusCode.OK, playResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, tacticalResponse.StatusCode);

            Assert.True(store.TryGet(accountId, out var finalSession));
            Assert.Same(session.Rng, finalSession.Rng);
            Assert.Equal(9, finalSession.State.Energy);
            Assert.Equal(1, finalSession.State.ComboGauge);
            Assert.DoesNotContain(
                finalSession.State.Hand, item => item.InstanceId == card.InstanceId);
            Assert.Contains(
                finalSession.State.DiscardPile, item => item.InstanceId == card.InstanceId);
            var hero = Assert.Single(
                finalSession.State.Allies, actor => actor.DefinitionId == "hero");
            Assert.Equal(1, hero.GetStatus("strength"));
            Assert.Equal(1, hero.GetStatus("omnistrike"));
            Assert.True(hero.Block.Sum > 0);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Abandon_waits_for_battle_command_lock_and_cannot_resurrect_session()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "battle-command-abandon");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();

            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var heldLock = await store.AcquireCommandLockAsync(
                accountId, CancellationToken.None);
            var abandonTask = client.PostAsJsonAsync(
                "/api/v1/runs/current/abandon", new { elapsedSeconds = 0 });

            await Task.Delay(150);
            bool abandonWaitedForLock = !abandonTask.IsCompleted;
            await heldLock.DisposeAsync();

            using var response = await abandonTask;
            Assert.True(abandonWaitedForLock);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(store.TryGet(accountId, out _));
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Get_battle_does_not_wait_for_account_mutation_lock()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "battle-get-not-locked");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();

            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var heldLock = await store.AcquireCommandLockAsync(accountId, CancellationToken.None);
            var getTask = client.GetAsync("/api/v1/runs/current/battle");
            var completed = await Task.WhenAny(getTask, Task.Delay(TimeSpan.FromSeconds(1)));
            await heldLock.DisposeAsync();

            Assert.Same(getTask, completed);
            using var response = await getTask;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Mutation_for_different_account_does_not_wait_for_held_lock()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "middleware-parallel-bob");
        try
        {
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var heldLock = await store.AcquireCommandLockAsync(
                "middleware-parallel-alice", CancellationToken.None);
            var heartbeatTask = client.PostAsJsonAsync(
                "/api/v1/runs/current/heartbeat", new { elapsedSeconds = 1 });
            var completed = await Task.WhenAny(
                heartbeatTask, Task.Delay(TimeSpan.FromSeconds(1)));
            await heldLock.DisposeAsync();

            Assert.Same(heartbeatTask, completed);
            using var response = await heartbeatTask;
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Case_variant_account_header_uses_same_mutation_gate()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "middleware-case-account");
        try
        {
            client.DefaultRequestHeaders.Remove(BattleControllerFixtures.AccountHeader);
            client.DefaultRequestHeaders.Add(
                BattleControllerFixtures.AccountHeader, accountId.ToUpperInvariant());
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var heldLock = await store.AcquireCommandLockAsync(
                accountId.ToUpperInvariant(), CancellationToken.None);
            var heartbeatTask = client.PostAsJsonAsync(
                "/api/v1/runs/current/heartbeat", new { elapsedSeconds = 1 });

            await Task.Delay(150);
            bool waitedForCaseVariantGate = !heartbeatTask.IsCompleted;
            await heldLock.DisposeAsync();

            using var response = await heartbeatTask;
            Assert.True(waitedForCaseVariantGate);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Case_variant_header_is_canonicalized_for_file_repositories()
    {
        const string canonicalId = "MiddlewareCanonicalAlice";
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: canonicalId);
        try
        {
            var saves = _factory.Services.GetRequiredService<ISaveRepository>();
            var before = await saves.TryLoadAsync(accountId, CancellationToken.None);
            Assert.NotNull(before);

            client.DefaultRequestHeaders.Remove(BattleControllerFixtures.AccountHeader);
            client.DefaultRequestHeaders.Add(
                BattleControllerFixtures.AccountHeader, canonicalId.ToLowerInvariant());

            using var response = await client.PostAsJsonAsync(
                "/api/v1/runs/current/heartbeat", new { elapsedSeconds = 3 });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var after = await saves.TryLoadAsync(canonicalId, CancellationToken.None);
            Assert.NotNull(after);
            Assert.Equal(before!.PlaySeconds + 3, after!.PlaySeconds);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Unknown_account_headers_do_not_allocate_command_locks()
    {
        using var client = _factory.CreateClient();
        var store = _factory.Services.GetRequiredService<BattleSessionStore>();
        int before = store.CommandLockCount;

        for (int i = 0; i < 20; i++)
        {
            client.DefaultRequestHeaders.Remove(BattleControllerFixtures.AccountHeader);
            client.DefaultRequestHeaders.Add(
                BattleControllerFixtures.AccountHeader, $"unknown-lock-{i}");
            using var response = await client.PostAsJsonAsync(
                "/api/v1/runs/current/heartbeat", new { elapsedSeconds = 1 });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(before, store.CommandLockCount);
    }

    [Fact]
    public async Task Cancelled_mutation_wait_releases_gate_for_retry()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "middleware-cancelled-wait");
        try
        {
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var heldLock = await store.AcquireCommandLockAsync(accountId, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            var cancelledTask = client.PostAsJsonAsync(
                "/api/v1/runs/current/heartbeat",
                new { elapsedSeconds = 1 }, cancellation.Token);

            await Task.Delay(100);
            Assert.False(cancelledTask.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await cancelledTask);
            await heldLock.DisposeAsync();

            using var retry = await client.PostAsJsonAsync(
                "/api/v1/runs/current/heartbeat", new { elapsedSeconds = 1 });
            Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Concurrent_heartbeat_and_finalize_preserve_both_persistent_changes()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "heartbeat-finalize-serialization");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();
            BattleControllerFixtures.ForceSessionVictory(_factory.Services, accountId);

            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var saves = _factory.Services.GetRequiredService<ISaveRepository>();
            var before = await saves.TryLoadAsync(accountId, CancellationToken.None);
            Assert.NotNull(before);

            var heldLock = await store.AcquireCommandLockAsync(accountId, CancellationToken.None);
            var heartbeatTask = client.PostAsJsonAsync(
                "/api/v1/runs/current/heartbeat", new { elapsedSeconds = 7 });
            var finalizeTask = client.PostAsync(
                "/api/v1/runs/current/battle/finalize", null);

            await Task.Delay(150);
            bool heartbeatWaited = !heartbeatTask.IsCompleted;
            bool finalizeWaited = !finalizeTask.IsCompleted;
            await heldLock.DisposeAsync();

            using var heartbeatResponse = await heartbeatTask;
            using var finalizeResponse = await finalizeTask;
            Assert.True(heartbeatWaited);
            Assert.True(finalizeWaited);
            Assert.Equal(HttpStatusCode.NoContent, heartbeatResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);

            var after = await saves.TryLoadAsync(accountId, CancellationToken.None);
            Assert.NotNull(after);
            Assert.Equal(before!.PlaySeconds + 7, after!.PlaySeconds);
            Assert.Null(after.ActiveBattle);
            Assert.NotNull(after.ActiveReward);
            Assert.False(store.TryGet(accountId, out _));
        }
        finally
        {
            client.Dispose();
        }
    }
}
