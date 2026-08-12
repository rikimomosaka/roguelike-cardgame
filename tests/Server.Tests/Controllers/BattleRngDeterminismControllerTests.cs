using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class BattleRngDeterminismControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleRngDeterminismControllerTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task BattleStart_SameRunCoordinates_IgnoresPlaySeconds()
    {
        _factory.ResetData();

        var first = await StartBattleAtPlayTimeAsync("rng-time-a", 1);
        var second = await StartBattleAtPlayTimeAsync("rng-time-b", 9999);

        Assert.Equal(first, second);
    }

    private async Task<IReadOnlyList<string>> StartBattleAtPlayTimeAsync(
        string accountId,
        long playSeconds)
    {
        HttpClient client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(
            client, _factory.Services, accountId);

        var saves = _factory.Services.GetRequiredService<ISaveRepository>();
        var run = (await saves.TryLoadAsync(accountId, CancellationToken.None))!;
        await saves.SaveAsync(
            accountId,
            run with { PlaySeconds = playSeconds },
            CancellationToken.None);

        var response = await client.PostAsync(
            "/api/v1/runs/current/battle/start", content: null);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var state = json.RootElement.GetProperty("state");

        return state.GetProperty("hand").EnumerateArray()
            .Concat(state.GetProperty("drawPile").EnumerateArray())
            .Select(card => card.GetProperty("cardDefinitionId").GetString()!)
            .ToArray();
    }
}
