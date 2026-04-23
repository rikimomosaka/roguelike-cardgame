using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class BestiaryControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public BestiaryControllerTests(TempDataFactory factory) => _factory = factory;

    private static HttpClient WithAccount(HttpClient client, string? id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        if (id is not null) client.DefaultRequestHeaders.Add("X-Account-Id", id);
        return client;
    }

    private static async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
            res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_WithoutHeader_Returns400()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/bestiary");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownAccount_Returns404()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        WithAccount(client, "does-not-exist-" + Guid.NewGuid().ToString("N"));
        var resp = await client.GetAsync("/api/v1/bestiary");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_KnownAccount_NoBestiaryFile_Returns200_EmptyDiscovered_WithAllKnown()
    {
        _factory.ResetData();
        const string acc = "bestiary-ctrl-01";
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, acc);
        WithAccount(client, acc);

        var dto = await client.GetFromJsonAsync<BestiaryDto>("/api/v1/bestiary");
        Assert.NotNull(dto);
        Assert.Empty(dto!.DiscoveredCardBaseIds);
        Assert.NotEmpty(dto.AllKnownCardBaseIds);
        for (int i = 1; i < dto.AllKnownCardBaseIds.Count; i++)
            Assert.True(string.CompareOrdinal(dto.AllKnownCardBaseIds[i - 1], dto.AllKnownCardBaseIds[i]) <= 0);
    }

    [Fact]
    public async Task Get_InvalidAccountId_Returns400()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        WithAccount(client, "has/slash");
        var resp = await client.GetAsync("/api/v1/bestiary");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_KnownAccount_WithSeededBestiary_ReturnsDiscoveredIdsSorted()
    {
        _factory.ResetData();
        const string acc = "bestiary-ctrl-seeded";
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, acc);
        WithAccount(client, acc);

        var repo = _factory.Services.GetRequiredService<IBestiaryRepository>();
        var seeded = new BestiaryState(
            BestiaryState.CurrentSchemaVersion,
            ImmutableHashSet.Create("strike", "defend", "bash"),
            ImmutableHashSet.Create("relic_b", "relic_a"),
            ImmutableHashSet<string>.Empty,
            ImmutableHashSet.Create("enemy_cultist"));
        await repo.SaveAsync(acc, seeded, CancellationToken.None);

        var dto = await client.GetFromJsonAsync<BestiaryDto>("/api/v1/bestiary");
        Assert.NotNull(dto);
        Assert.Equal(BestiaryState.CurrentSchemaVersion, dto!.SchemaVersion);
        Assert.True(dto.DiscoveredCardBaseIds.SequenceEqual(new[] { "bash", "defend", "strike" }));
        Assert.True(dto.DiscoveredRelicIds.SequenceEqual(new[] { "relic_a", "relic_b" }));
        Assert.True(dto.EncounteredEnemyIds.SequenceEqual(new[] { "enemy_cultist" }));
        Assert.Empty(dto.DiscoveredPotionIds);
    }
}
