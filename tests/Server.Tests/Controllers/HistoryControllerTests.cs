using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>GET /api/v1/history/last-result の統合テスト。</summary>
public class HistoryControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public HistoryControllerTests(TempDataFactory factory) => _factory = factory;

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

    // ── リスト取得 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NoHistory_Returns200EmptyArray()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "hist-list-empty");
        WithAccount(client, "hist-list-empty");

        var resp = await client.GetAsync("/api/v1/history");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<RunResultDto[]>();
        Assert.NotNull(list);
        Assert.Empty(list);
    }

    [Fact]
    public async Task List_NoHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/history");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_UnknownAccount_Returns404()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        WithAccount(client, "hist-list-ghost");
        var resp = await client.GetAsync("/api/v1/history");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── last-result ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LastResult_NoHistory_Returns204()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "hist-last-empty");
        WithAccount(client, "hist-last-empty");

        var resp = await client.GetAsync("/api/v1/history/last-result");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task LastResult_NoHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/history/last-result");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task LastResult_UnknownAccount_Returns404()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        WithAccount(client, "hist-last-ghost");
        var resp = await client.GetAsync("/api/v1/history/last-result");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
