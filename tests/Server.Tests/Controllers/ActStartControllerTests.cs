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
}
