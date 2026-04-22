using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>Abandon エンドポイントが履歴を保存することを確認する統合テスト。</summary>
public class AbandonHistoryTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public AbandonHistoryTests(TempDataFactory factory) => _factory = factory;

    private static HttpClient WithAccount(HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        client.DefaultRequestHeaders.Add("X-Account-Id", id);
        return client;
    }

    private async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
            res.EnsureSuccessStatusCode();
    }

    private async Task<RunSnapshotDto?> GetCurrentAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/runs/current");
        if (resp.StatusCode == HttpStatusCode.NoContent) return null;
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RunSnapshotDto>());
    }

    private async Task<RunResultDto[]> ListHistoryViaApiAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/v1/history");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RunResultDto[]>()) ?? [];
    }

    [Fact]
    public async Task Abandon_SavesHistory_AndDeletesCurrent()
    {
        _factory.ResetData();
        const string acc = "abandon-hist-01";
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, acc);
        WithAccount(client, acc);

        // ラン開始
        var newResp = await client.PostAsync("/api/v1/runs/new", content: null);
        newResp.EnsureSuccessStatusCode();

        // Abandon
        var abandResp = await client.PostAsJsonAsync("/api/v1/runs/current/abandon",
            new HeartbeatRequestDto(ElapsedSeconds: 10));
        Assert.Equal(HttpStatusCode.OK, abandResp.StatusCode);
        // Response body is a RunResultDto with outcome=Abandoned.
        var resultDoc = System.Text.Json.JsonDocument.Parse(await abandResp.Content.ReadAsStringAsync());
        Assert.Equal("Abandoned", resultDoc.RootElement.GetProperty("outcome").GetString());

        // current が消えているはず
        Assert.Null(await GetCurrentAsync(client));

        // 履歴 API に Abandoned レコードが存在するはず
        var history = await ListHistoryViaApiAsync(client);
        Assert.Contains(history, h => h.Outcome == "Abandoned");
    }

    [Fact]
    public async Task Abandon_HistoryRecord_HasCorrectPlaySeconds()
    {
        _factory.ResetData();
        const string acc = "abandon-hist-02";
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, acc);
        WithAccount(client, acc);

        await client.PostAsync("/api/v1/runs/new", content: null);
        await client.PostAsJsonAsync("/api/v1/runs/current/abandon",
            new HeartbeatRequestDto(ElapsedSeconds: 42));

        // リポジトリから直接確認
        var repo = _factory.Services.GetRequiredService<IHistoryRepository>();
        var records = await repo.ListAsync(acc, CancellationToken.None);
        Assert.Single(records);
        Assert.Equal(RunProgress.Abandoned, records[0].Outcome);
        Assert.Equal(42, records[0].PlaySeconds);
    }
}
