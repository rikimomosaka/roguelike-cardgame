using System;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public sealed class BattleSessionStoreConcurrencyTests
{
    [Fact]
    public async Task Same_account_is_serialized_and_wait_can_be_cancelled()
    {
        var store = new BattleSessionStore();
        var first = await store.AcquireCommandLockAsync("alice", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        var waiting = store.AcquireCommandLockAsync("alice", cancellation.Token).AsTask();
        Assert.False(waiting.IsCompleted);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);

        await first.DisposeAsync();
        await using var reacquired = await store.AcquireCommandLockAsync(
            "alice", CancellationToken.None);
    }

    [Fact]
    public async Task Different_accounts_can_hold_command_locks_in_parallel()
    {
        var store = new BattleSessionStore();
        await using var alice = await store.AcquireCommandLockAsync(
            "alice", CancellationToken.None);

        var bobTask = store.AcquireCommandLockAsync(
            "bob", CancellationToken.None).AsTask();
        var completed = await Task.WhenAny(bobTask, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.Same(bobTask, completed);
        await using var bob = await bobTask;
    }

    [Fact]
    public async Task Account_keys_are_case_insensitive_for_both_gate_and_session()
    {
        var store = new BattleSessionStore();
        var upperCaseLease = await store.AcquireCommandLockAsync(
            "Alice", CancellationToken.None);

        var lowerCaseWaiter = store.AcquireCommandLockAsync(
            "alice", CancellationToken.None).AsTask();
        Assert.False(lowerCaseWaiter.IsCompleted);

        await upperCaseLease.DisposeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var lowerCaseLease = await lowerCaseWaiter.WaitAsync(timeout.Token);

        var session = new BattleSession(State: null!, Rng: null!);
        store.Set("Alice", session);
        Assert.True(store.TryGet("alice", out var found));
        Assert.Same(session, found);

        store.Remove("ALICE");
        Assert.False(store.TryGet("alice", out _));
    }

    [Fact]
    public async Task Lease_is_released_when_command_scope_throws()
    {
        var store = new BattleSessionStore();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var commandLock = await store.AcquireCommandLockAsync(
                "alice", CancellationToken.None);
            throw new InvalidOperationException("expected");
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await using var reacquired = await store.AcquireCommandLockAsync(
            "alice", timeout.Token);
    }
}
