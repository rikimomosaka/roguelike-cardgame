using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// イベント HTTP エンドポイント（GET /event/current, POST /event/choose）の統合テスト。
/// 注意: seed=58 マップで Event タイルに辿り着くことは難しいため、
/// ISaveRepository を通じて ActiveEvent を直接注入するアプローチを採用。
/// </summary>
public class EventControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public EventControllerTests(TempDataFactory f) => _factory = f;

    /// <summary>
    /// 新規ランを開始し、AccountId ヘッダを設定済みの HttpClient を返す。
    /// ActiveEvent は注入されていない（通常の Start ノード状態）。
    /// </summary>
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
    /// リポジトリ経由で RunState に ActiveEvent を注入する。
    /// blessing_fountain（選択肢: 水を飲む(HP+15) / コインを投げ入れる(最大HP+5) / 立ち去る）を使用。
    /// choiceIndex=2 は無条件・副作用なし（立ち去る）。
    /// choiceIndex=0 は無条件・HP+15。
    /// </summary>
    private async Task InjectBlessingFountainEventAsync(string accountId)
    {
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var s = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        var def = catalog.Events["blessing_fountain"];
        var inst = new EventInstance(def.Id, def.Choices);
        await repo.SaveAsync(accountId, s with { ActiveEvent = inst }, CancellationToken.None);
    }

    /// <summary>
    /// shady_merchant イベントを注入する。
    /// choiceIndex=0 は minGold=50 条件あり（ゴールド不足でテスト可能）。
    /// choiceIndex=2 は無条件（断る）。
    /// </summary>
    private async Task InjectShadyMerchantEventAsync(string accountId)
    {
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var s = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        var def = catalog.Events["shady_merchant"];
        var inst = new EventInstance(def.Id, def.Choices);
        await repo.SaveAsync(accountId, s with { ActiveEvent = inst }, CancellationToken.None);
    }

    // ─── GET /event/current ──────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrent_NoActiveEvent_Returns409()
    {
        const string AccountId = "ev1-no-event";
        var client = await StartFreshRunAsync(AccountId);

        var res = await client.GetAsync("/api/v1/event/current");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task GetCurrent_WithActiveEvent_ReturnsEventDto()
    {
        const string AccountId = "ev1-with-event";
        var client = await StartFreshRunAsync(AccountId);
        await InjectBlessingFountainEventAsync(AccountId);

        var res = await client.GetAsync("/api/v1/event/current");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("blessing_fountain", root.GetProperty("eventId").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("description").GetString()));

        // blessing_fountain には 3 つの選択肢がある（水を飲む / コインを投げ入れる / 立ち去る）
        var choices = root.GetProperty("choices");
        Assert.Equal(3, choices.GetArrayLength());

        // 全選択肢にラベルが存在することを確認
        foreach (var c in choices.EnumerateArray())
            Assert.False(string.IsNullOrEmpty(c.GetProperty("label").GetString()));
    }

    // ─── POST /event/choose ──────────────────────────────────────────────────

    [Fact]
    public async Task PostChoose_NoActiveEvent_Returns409()
    {
        const string AccountId = "ev1-choose-no-event";
        var client = await StartFreshRunAsync(AccountId);

        var res = await client.PostAsJsonAsync("/api/v1/event/choose", new { choiceIndex = 0 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task PostChoose_ChoiceIndexOutOfRange_Returns400()
    {
        const string AccountId = "ev1-choose-oob";
        var client = await StartFreshRunAsync(AccountId);
        await InjectBlessingFountainEventAsync(AccountId);

        var res = await client.PostAsJsonAsync("/api/v1/event/choose", new { choiceIndex = 999 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PostChoose_ConditionNotMet_Returns409()
    {
        const string AccountId = "ev1-choose-cond-fail";
        var client = await StartFreshRunAsync(AccountId);
        await InjectShadyMerchantEventAsync(AccountId);

        // ゴールドを 0 に設定して minGold=50 条件を満たさないようにする
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        await repo.SaveAsync(AccountId, s with { Gold = 0 }, CancellationToken.None);

        // choiceIndex=0 は 50 ゴールド必要。条件を満たさないため 409
        var res = await client.PostAsJsonAsync("/api/v1/event/choose", new { choiceIndex = 0 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task PostChoose_ValidChoice_AppliesAndSetsChosenIndex()
    {
        const string AccountId = "ev1-choose-ok";
        var client = await StartFreshRunAsync(AccountId);

        // HP を既知の値に設定（MaxHp=80, CurrentHp=50）してからイベント注入
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s0 = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        await repo.SaveAsync(AccountId, s0 with { CurrentHp = 50, MaxHp = 80 }, CancellationToken.None);

        await InjectBlessingFountainEventAsync(AccountId);

        // choiceIndex=0: 水を飲む（HP +15）→ CurrentHp は 65 になるはず
        var res = await client.PostAsJsonAsync("/api/v1/event/choose", new { choiceIndex = 0 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var runEl = doc.RootElement.GetProperty("run");

        // HP+15 が適用されているか
        Assert.Equal(65, runEl.GetProperty("currentHp").GetInt32());

        // activeEvent は null ではなく、chosenIndex がセットされているか
        Assert.NotEqual(JsonValueKind.Null, runEl.GetProperty("activeEvent").ValueKind);
        Assert.Equal(0, runEl.GetProperty("activeEvent").GetProperty("chosenIndex").GetInt32());

        // リポジトリでも ActiveEvent が ChosenIndex=0 になっているか確認
        var after = (await repo.TryLoadAsync(AccountId, CancellationToken.None))!;
        Assert.NotNull(after.ActiveEvent);
        Assert.Equal(0, after.ActiveEvent!.ChosenIndex);
    }

    [Fact]
    public async Task PostChoose_LeaveChoice_Returns200AndSetsChosenIndex()
    {
        const string AccountId = "ev1-choose-leave";
        var client = await StartFreshRunAsync(AccountId);
        await InjectBlessingFountainEventAsync(AccountId);

        // choiceIndex=2: 立ち去る（副作用なし）
        var res = await client.PostAsJsonAsync("/api/v1/event/choose", new { choiceIndex = 2 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var runEl = doc.RootElement.GetProperty("run");

        // activeEvent は null ではなく chosenIndex=2 がセットされているか
        Assert.NotEqual(JsonValueKind.Null, runEl.GetProperty("activeEvent").ValueKind);
        Assert.Equal(2, runEl.GetProperty("activeEvent").GetProperty("chosenIndex").GetInt32());

        // GET /event/current は引き続き 200 を返す（ActiveEvent はまだ存在する）
        var getRes = await client.GetAsync("/api/v1/event/current");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
    }
}
