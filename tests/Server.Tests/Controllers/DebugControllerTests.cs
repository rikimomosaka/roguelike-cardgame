using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>dev-only POST /api/v1/debug/damage の統合テスト。</summary>
public class DebugControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _fx;
    public DebugControllerTests(TempDataFactory fx) => _fx = fx;

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static void WithAccount(HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        client.DefaultRequestHeaders.Add("X-Account-Id", id);
    }

    private static async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var r = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (r.StatusCode != HttpStatusCode.Created && r.StatusCode != HttpStatusCode.Conflict)
            r.EnsureSuccessStatusCode();
    }

    private static async Task StartRunAsync(HttpClient client)
        => (await client.PostAsync("/api/v1/runs/new", null)).EnsureSuccessStatusCode();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<RunSnapshotDto?> GetCurrentAsync(HttpClient client)
    {
        var r = await client.GetAsync("/api/v1/runs/current");
        if (r.StatusCode == HttpStatusCode.NoContent) return null;
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<RunSnapshotDto>(JsonOpts);
    }

    // ─── tests ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Damage_ReducesHp()
    {
        _fx.ResetData();
        var client = _fx.CreateClient();
        await EnsureAccountAsync(client, "debug-a");
        WithAccount(client, "debug-a");
        await StartRunAsync(client);

        var before = await GetCurrentAsync(client);
        var resp = await client.PostAsJsonAsync("/api/v1/debug/damage",
            new DebugDamageRequestDto(Amount: 10));
        resp.EnsureSuccessStatusCode();
        var after = await GetCurrentAsync(client);

        Assert.Equal(before!.Run.CurrentHp - 10, after!.Run.CurrentHp);
    }

    [Fact]
    public async Task Damage_HpReachesZero_ReturnsRunResultAndDeletesCurrent()
    {
        _fx.ResetData();
        var client = _fx.CreateClient();
        await EnsureAccountAsync(client, "debug-b");
        WithAccount(client, "debug-b");
        await StartRunAsync(client);

        var resp = await client.PostAsJsonAsync("/api/v1/debug/damage",
            new DebugDamageRequestDto(Amount: 9999));
        resp.EnsureSuccessStatusCode();

        var result = await resp.Content.ReadFromJsonAsync<RunResultDto>(JsonOpts);
        Assert.Equal("GameOver", result!.Outcome);
        Assert.Null(await GetCurrentAsync(client));
    }
}
