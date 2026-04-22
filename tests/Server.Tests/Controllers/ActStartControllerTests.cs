using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// POST /api/v1/act-start/choose の統合テスト。
/// EventControllerTests と同様に ISaveRepository を通じて
/// ActiveActStartRelicChoice を直接注入するアプローチを採用。
/// </summary>
public class ActStartControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public ActStartControllerTests(TempDataFactory f) => _factory = f;

    private async Task<HttpClient> StartFreshRunAsync(string accountId)
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        (await client.PostAsync("/api/v1/runs/new", content: null)).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>
    /// ISaveRepository 経由で ActStartRelicChoice を注入する。
    /// DataCatalog から act1 pool の実際のレリック ID を使用。
    /// </summary>
    private async Task<ImmutableArray<string>> InjectRelicChoiceAsync(string accountId)
    {
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var s = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;

        // カタログから act1 pool の実際の ID を 3 つ選ぶ
        var pool = catalog.ActStartRelicPools![1];
        var picked = ImmutableArray.Create(pool[0], pool[1], pool[2]);
        var choice = new ActStartRelicChoice(picked);
        await repo.SaveAsync(accountId, s with { ActiveActStartRelicChoice = choice }, CancellationToken.None);
        return picked;
    }

    // ─── POST /api/v1/act-start/choose ──────────────────────────────────────

    [Fact]
    public async Task Choose_NoChoiceActive_Returns409()
    {
        const string AccountId = "as1-no-choice";
        var client = await StartFreshRunAsync(AccountId);

        // NewRun 直後は ActiveActStartRelicChoice が null → 409
        var resp = await client.PostAsJsonAsync("/api/v1/act-start/choose",
            new { relicId = "some_relic" });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task NewRun_DoesNotAutoGenerateRelicChoice()
    {
        // Regression: act-start relic 選択はスタートマスを踏んだ時のイベントとして
        // 発動するため、NewRun 直後には activeActStartRelicChoice は null のままでなければならない。
        const string AccountId = "as1-newrun-null";
        var client = await StartFreshRunAsync(AccountId);

        var resp = await client.GetAsync("/api/v1/runs/current");
        resp.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var choice = doc.RootElement.GetProperty("run").GetProperty("activeActStartRelicChoice");
        Assert.Equal(JsonValueKind.Null, choice.ValueKind);
    }

    [Fact]
    public async Task Enter_OnStartTile_GeneratesRelicChoice()
    {
        // Regression: POST /act-start/enter はスタートマス入場時に 3 択を生成する。
        const string AccountId = "as1-enter-generate";
        var client = await StartFreshRunAsync(AccountId);

        var resp = await client.PostAsync("/api/v1/act-start/enter", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var choice = doc.RootElement.GetProperty("run").GetProperty("activeActStartRelicChoice");
        Assert.NotEqual(JsonValueKind.Null, choice.ValueKind);
        var ids = choice.GetProperty("relicIds");
        Assert.Equal(3, ids.GetArrayLength());

        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var pool = catalog.ActStartRelicPools![1];
        foreach (var el in ids.EnumerateArray())
            Assert.Contains(el.GetString()!, pool);
    }

    [Fact]
    public async Task Enter_WhenAlreadyActive_Returns409()
    {
        const string AccountId = "as1-enter-already-active";
        var client = await StartFreshRunAsync(AccountId);

        (await client.PostAsync("/api/v1/act-start/enter", content: null))
            .EnsureSuccessStatusCode();
        var resp = await client.PostAsync("/api/v1/act-start/enter", content: null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Enter_WhenStartAlreadyVisited_Returns409()
    {
        const string AccountId = "as1-enter-visited";
        var client = await StartFreshRunAsync(AccountId);

        // Start を踏んで relic を選ぶところまで進めると VisitedNodeIds に Start が入る
        (await client.PostAsync("/api/v1/act-start/enter", content: null))
            .EnsureSuccessStatusCode();
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        var picked = s.ActiveActStartRelicChoice!.RelicIds[0];
        (await client.PostAsJsonAsync("/api/v1/act-start/choose", new { relicId = picked }))
            .EnsureSuccessStatusCode();

        var resp = await client.PostAsync("/api/v1/act-start/enter", content: null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Choose_InvalidRelicId_Returns422()
    {
        const string AccountId = "as1-invalid-relic";
        var client = await StartFreshRunAsync(AccountId);
        await InjectRelicChoiceAsync(AccountId);

        var resp = await client.PostAsJsonAsync("/api/v1/act-start/choose",
            new { relicId = "not_a_real_relic" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Choose_Valid_Returns200_AndClearsChoice()
    {
        const string AccountId = "as1-valid-choose";
        var client = await StartFreshRunAsync(AccountId);
        var picked = await InjectRelicChoiceAsync(AccountId);
        var chosenRelicId = picked[0];

        var resp = await client.PostAsJsonAsync("/api/v1/act-start/choose",
            new { relicId = chosenRelicId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var runEl = doc.RootElement.GetProperty("run");

        // ActiveActStartRelicChoice が null になっているか
        Assert.Equal(JsonValueKind.Null, runEl.GetProperty("activeActStartRelicChoice").ValueKind);

        // 選んだレリックが relics に追加されているか
        bool found = false;
        foreach (var r in runEl.GetProperty("relics").EnumerateArray())
            if (r.GetString() == chosenRelicId) { found = true; break; }
        Assert.True(found, $"Chosen relic '{chosenRelicId}' should be in relics list");

        // リポジトリでも状態が保存されているか確認
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var after = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        Assert.Null(after.ActiveActStartRelicChoice);
        Assert.Contains(chosenRelicId, after.Relics);
    }

    [Fact]
    public async Task Choose_AfterAdvanceAct_AddsStartTileToVisited()
    {
        // Setup: simulate state immediately after AdvanceAct into act 2:
        //   VisitedNodeIds empty, CurrentNodeId = 999 (new act's start tile),
        //   ActiveActStartRelicChoice populated with 3 act2 relic ids.
        const string AccountId = "as1-advance-act-visited";
        var client = await StartFreshRunAsync(AccountId);

        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var s = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;

        // Act 2 pool からレリック ID を 3 つ選ぶ
        var pool = catalog.ActStartRelicPools![2];
        var picked = ImmutableArray.Create(pool[0], pool[1], pool[2]);
        var choice = new ActStartRelicChoice(picked);

        // AdvanceAct 直後の状態を模倣: VisitedNodeIds は空、CurrentNodeId は 999
        const int StartNodeId = 999;
        var injected = s with
        {
            VisitedNodeIds = ImmutableArray<int>.Empty,
            CurrentNodeId = StartNodeId,
            ActiveActStartRelicChoice = choice
        };
        await repo.SaveAsync(AccountId, injected, CancellationToken.None);

        // Action: choose the first relic
        var chosenRelicId = picked[0];
        var resp = await client.PostAsJsonAsync("/api/v1/act-start/choose",
            new { relicId = chosenRelicId });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Expect: VisitedNodeIds contains CurrentNodeId (999), choice is null
        var after = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        Assert.Null(after.ActiveActStartRelicChoice);
        Assert.Contains(StartNodeId, after.VisitedNodeIds);
    }
}
