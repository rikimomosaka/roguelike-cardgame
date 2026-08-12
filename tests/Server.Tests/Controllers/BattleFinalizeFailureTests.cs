using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class BattleFinalizeFailureTests : IClassFixture<FaultingFinalizeFactory>
{
    private readonly FaultingFinalizeFactory _factory;

    public BattleFinalizeFailureTests(FaultingFinalizeFactory factory) => _factory = factory;

    [Theory]
    [InlineData(SaveFailureMode.IoFailure)]
    [InlineData(SaveFailureMode.Cancellation)]
    public async Task Finalize_persistence_failure_keeps_resolved_session_for_retry(
        SaveFailureMode failureMode)
    {
        _factory.ResetData();
        using var client = _factory.CreateClient();
        const string accountId = "finalize-retry";
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(
            client, _factory.Services, accountId);
        (await client.PostAsync("/api/v1/runs/current/battle/start", null))
            .EnsureSuccessStatusCode();
        BattleControllerFixtures.ForceSessionVictory(_factory.Services, accountId);

        var sessions = _factory.Services.GetRequiredService<BattleSessionStore>();
        var saves = _factory.Services.GetRequiredService<ISaveRepository>();
        _factory.FaultingSaves.FailNextSave(failureMode);

        HttpResponseMessage? failedResponse = null;
        try
        {
            failedResponse = await client.PostAsync(
                "/api/v1/runs/current/battle/finalize", null);
            Assert.False(failedResponse.IsSuccessStatusCode);
        }
        catch (OperationCanceledException) when (failureMode == SaveFailureMode.Cancellation)
        {
            // A host may surface an unhandled cancellation directly instead of a 500 response.
        }
        finally
        {
            failedResponse?.Dispose();
        }

        Assert.True(sessions.TryGet(accountId, out var retryable));
        Assert.Equal(Core.Battle.State.BattlePhase.Resolved, retryable.State.Phase);
        var unchangedRun = await saves.TryLoadAsync(accountId, CancellationToken.None);
        Assert.NotNull(unchangedRun?.ActiveBattle);
        Assert.Null(unchangedRun!.ActiveReward);

        using var retryResponse = await client.PostAsync(
            "/api/v1/runs/current/battle/finalize", null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.False(sessions.TryGet(accountId, out _));
        var committedRun = await saves.TryLoadAsync(accountId, CancellationToken.None);
        Assert.Null(committedRun?.ActiveBattle);
        Assert.NotNull(committedRun?.ActiveReward);
    }

    [Theory]
    [InlineData(SaveFailureMode.IoFailure)]
    [InlineData(SaveFailureMode.Cancellation)]
    public async Task Abandon_delete_failure_keeps_battle_session_for_retry(
        SaveFailureMode failureMode)
    {
        _factory.ResetData();
        using var client = _factory.CreateClient();
        const string accountId = "abandon-retry";
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(
            client, _factory.Services, accountId);
        (await client.PostAsync("/api/v1/runs/current/battle/start", null))
            .EnsureSuccessStatusCode();

        var sessions = _factory.Services.GetRequiredService<BattleSessionStore>();
        var saves = _factory.Services.GetRequiredService<ISaveRepository>();
        _factory.FaultingSaves.FailNextDelete(failureMode);

        HttpResponseMessage? failedResponse = null;
        try
        {
            failedResponse = await client.PostAsJsonAsync(
                "/api/v1/runs/current/abandon", new { elapsedSeconds = 0 });
            Assert.False(failedResponse.IsSuccessStatusCode);
        }
        catch (OperationCanceledException) when (failureMode == SaveFailureMode.Cancellation)
        {
        }
        finally
        {
            failedResponse?.Dispose();
        }

        Assert.True(sessions.TryGet(accountId, out _));
        Assert.NotNull(await saves.TryLoadAsync(accountId, CancellationToken.None));
        var historyRepository = _factory.Services
            .GetRequiredService<IHistoryRepository>();
        var historyAfterFailure = await historyRepository
            .ListAsync(accountId, CancellationToken.None);
        var canonicalHistory = Assert.Single(historyAfterFailure);

        using var retryResponse = await client.PostAsJsonAsync(
            "/api/v1/runs/current/abandon", new { elapsedSeconds = 0 });
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retryResult = await retryResponse.Content.ReadFromJsonAsync<RunResultDto>();
        Assert.NotNull(retryResult);
        Assert.False(sessions.TryGet(accountId, out _));
        Assert.Null(await saves.TryLoadAsync(accountId, CancellationToken.None));
        var history = await historyRepository.ListAsync(accountId, CancellationToken.None);
        var persistedHistory = Assert.Single(history);
        Assert.Equal(accountId, persistedHistory.AccountId);
        Assert.Equal(canonicalHistory.EndedAtUtc, persistedHistory.EndedAtUtc);
        Assert.Equal(persistedHistory.EndedAtUtc.ToString("O"), retryResult!.EndedAtUtc);
    }
}

public enum SaveFailureMode
{
    None,
    IoFailure,
    Cancellation,
}

public sealed class FaultingFinalizeFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "rcg-finalize-failure-" + Guid.NewGuid().ToString("N"));
    private bool _disposed;

    public FaultInjectingSaveRepository FaultingSaves =>
        Services.GetRequiredService<FaultInjectingSaveRepository>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("DataStorage:RootDirectory", _dataRoot),
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISaveRepository>();
            services.AddSingleton<FileSaveRepository>();
            services.AddSingleton<FaultInjectingSaveRepository>();
            services.AddSingleton<ISaveRepository>(sp =>
                sp.GetRequiredService<FaultInjectingSaveRepository>());

            services.RemoveAll<RunStartService>();
            services.AddSingleton(sp => new RunStartService(
                sp.GetRequiredService<IDungeonMapGenerator>(),
                sp.GetRequiredService<MapGenerationConfig>(),
                sp.GetRequiredService<ISaveRepository>(),
                seedSource: () => 58));
        });
    }

    public void ResetData()
    {
        if (Directory.Exists(_dataRoot)) Directory.Delete(_dataRoot, recursive: true);
        if (!_disposed)
        {
            Services.GetRequiredService<BattleSessionStore>().Clear();
            FaultingSaves.Reset();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ResetData();
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}

public sealed class FaultInjectingSaveRepository : ISaveRepository
{
    private readonly FileSaveRepository _inner;
    private int _nextFailure;

    public FaultInjectingSaveRepository(FileSaveRepository inner) => _inner = inner;

    public void FailNextSave(SaveFailureMode mode) =>
        Interlocked.Exchange(ref _nextFailure, (int)mode);

    public void Reset()
    {
        Interlocked.Exchange(ref _nextFailure, (int)SaveFailureMode.None);
        Interlocked.Exchange(ref _nextDeleteFailure, (int)SaveFailureMode.None);
    }

    public Task SaveAsync(string accountId, RunState state, CancellationToken ct)
    {
        var failure = (SaveFailureMode)Interlocked.Exchange(
            ref _nextFailure, (int)SaveFailureMode.None);
        return failure switch
        {
            SaveFailureMode.IoFailure => Task.FromException(
                new IOException("Injected save failure.")),
            SaveFailureMode.Cancellation => Task.FromCanceled(
                new CancellationToken(canceled: true)),
            _ => _inner.SaveAsync(accountId, state, ct),
        };
    }

    public Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct) =>
        _inner.TryLoadAsync(accountId, ct);

    private int _nextDeleteFailure;

    public void FailNextDelete(SaveFailureMode mode) =>
        Interlocked.Exchange(ref _nextDeleteFailure, (int)mode);

    public Task DeleteAsync(string accountId, CancellationToken ct)
    {
        var failure = (SaveFailureMode)Interlocked.Exchange(
            ref _nextDeleteFailure, (int)SaveFailureMode.None);
        return failure switch
        {
            SaveFailureMode.IoFailure => Task.FromException(
                new IOException("Injected delete failure.")),
            SaveFailureMode.Cancellation => Task.FromCanceled(
                new CancellationToken(canceled: true)),
            _ => _inner.DeleteAsync(accountId, ct),
        };
    }
}
