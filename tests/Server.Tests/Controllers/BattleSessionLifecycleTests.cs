using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class BattleSessionLifecycleTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleSessionLifecycleTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task Force_new_run_removes_previous_battle_session_after_commit()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "force-new-cleans-session");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();
            var sessions = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(sessions.TryGet(accountId, out _));

            using var response = await client.PostAsync("/api/v1/runs/new?force=true", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(sessions.TryGet(accountId, out _));
            using var battleResponse = await client.GetAsync("/api/v1/runs/current/battle");
            Assert.Equal(HttpStatusCode.NotFound, battleResponse.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Legacy_battle_win_rejects_active_engine_session_without_mutating_run()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "legacy-win-active-session");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();
            var sessions = _factory.Services.GetRequiredService<BattleSessionStore>();
            var saves = _factory.Services.GetRequiredService<ISaveRepository>();
            Assert.True(sessions.TryGet(accountId, out var beforeSession));
            var beforeRun = await saves.TryLoadAsync(accountId, CancellationToken.None);
            Assert.NotNull(beforeRun?.ActiveBattle);

            using var response = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.True(sessions.TryGet(accountId, out var afterSession));
            Assert.Same(beforeSession, afterSession);
            var afterRun = await saves.TryLoadAsync(accountId, CancellationToken.None);
            Assert.NotNull(afterRun?.ActiveBattle);
            Assert.Null(afterRun!.ActiveReward);
        }
        finally
        {
            client.Dispose();
        }
    }
}
