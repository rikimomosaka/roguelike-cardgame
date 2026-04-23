using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// PostBattleWin が生成した報酬の CardChoices を BestiaryTracker.NoteCardsSeen で
/// RunState.SeenCardBaseIds に記録するかを検証する統合テスト。
/// </summary>
public class RunsControllerBestiaryTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public RunsControllerBestiaryTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task AfterBattleWin_CardChoicesAddedToSeenCards()
    {
        _factory.ResetData();
        const string acc = "runs-bestiary-01";
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, acc);
        BattleTestHelpers.WithAccount(client, acc);

        // Start run + move to enemy + win battle (ActiveReward produced).
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(client);
        var winResp = await client.PostAsJsonAsync(
            "/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });
        winResp.EnsureSuccessStatusCode();

        // Read current: extract CardChoices from ActiveReward (via raw JSON to avoid
        // TileKind enum converter issues that affect typed RunSnapshotDto parsing here).
        var curResp = await client.GetAsync("/api/v1/runs/current");
        curResp.EnsureSuccessStatusCode();
        using var curDoc = JsonDocument.Parse(await curResp.Content.ReadAsStringAsync());
        var activeReward = curDoc.RootElement.GetProperty("run").GetProperty("activeReward");
        Assert.Equal(JsonValueKind.Object, activeReward.ValueKind);
        var choices = new List<string>();
        foreach (var c in activeReward.GetProperty("cardChoices").EnumerateArray())
            choices.Add(c.GetString()!);
        Assert.NotEmpty(choices);

        // Abandon the run so the RunResultDto is produced from the history record,
        // which copies SeenCardBaseIds from the RunState.
        var abandon = await client.PostAsJsonAsync(
            "/api/v1/runs/current/abandon", new HeartbeatRequestDto(ElapsedSeconds: 0));
        abandon.EnsureSuccessStatusCode();

        var result = await abandon.Content.ReadFromJsonAsync<RunResultDto>();
        Assert.NotNull(result);
        foreach (var id in choices)
            Assert.Contains(id, result!.SeenCardBaseIds);
    }
}
