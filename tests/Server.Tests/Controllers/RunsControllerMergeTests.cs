using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Abandon 時に RunHistoryRecord を履歴に追記した直後、IBestiaryRepository.MergeAsync が
/// 呼び出され、アカウント単位 Bestiary に discovered card が反映されることを確認する統合テスト。
/// </summary>
public class RunsControllerMergeTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public RunsControllerMergeTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task Abandon_MergesBestiary()
    {
        _factory.ResetData();
        const string acc = "phase08-bestiary-merge-01";
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, acc);
        BattleTestHelpers.WithAccount(client, acc);

        var newResp = await client.PostAsync("/api/v1/runs/new", content: null);
        newResp.EnsureSuccessStatusCode();

        var abandResp = await client.PostAsJsonAsync("/api/v1/runs/current/abandon",
            new HeartbeatRequestDto(ElapsedSeconds: 0));
        abandResp.EnsureSuccessStatusCode();

        var repo = _factory.Services.GetRequiredService<IBestiaryRepository>();
        var loaded = await repo.LoadAsync(acc, CancellationToken.None);
        Assert.NotEmpty(loaded.DiscoveredCardBaseIds);
    }
}
