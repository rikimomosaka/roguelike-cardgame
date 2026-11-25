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
        var player = Assert.Single(dto.PlayerCharacters, character =>
            character.Id == "default" && character.Kind == "player"
            && character.IsDiscovered && !string.IsNullOrWhiteSpace(character.Name));
        Assert.Equal(80, player.MaxHp);
        Assert.Equal("Striker", player.CharacterClass);
        Assert.Contains("連閃", player.TraitDescription);
        // 能力値（体力以外）と、キーワード補足用の書式付き特性文が図鑑にも渡る。
        Assert.Equal(3, player.EnergyPerTurn);
        Assert.Equal(5, player.DrawPerTurn);
        Assert.Equal(6, player.Attack);
        Assert.Equal(5, player.Defense);
        Assert.Equal("連閃", player.TraitName);
        Assert.Contains("[S:afterimage]", player.TraitEffects);
        Assert.Equal(10, player.Deck!.Count);
        Assert.Contains("striker_basic_attack", player.Deck);
        Assert.Contains(player.Actions, action => action.Category == "戦技");
        Assert.Contains(player.Actions, action => action.Category == "奥義");
        Assert.False(string.IsNullOrWhiteSpace(player.Description));

        var ally = Assert.Single(dto.AllyCharacters, character =>
            character.Id == "wisp" && character.Name == "ウィスプ"
            && character.Kind == "ally");
        Assert.False(ally.IsDiscovered);
        Assert.Null(ally.CharacterClass);
        Assert.Equal(30, ally.MaxHp);
        Assert.Equal(3, ally.Attack);
        Assert.Equal(3, ally.Defense);
        Assert.True(ally.EnergyPerTurn > 0);
        Assert.True(ally.DrawPerTurn > 0);
        Assert.Null(ally.LifetimeTurns);
        Assert.Equal(4, ally.Actions.Count);
        Assert.Contains(ally.Actions, action =>
            action.Category == "行動カード"
            && action.Name == "鬼火 ×2"
            && action.Description.Contains("[N:1]"));
        Assert.Contains(ally.SummonCards, card =>
            card.Id == "starter_summon_3" && card.Name == "ウィスプ");
        Assert.DoesNotContain("wisp_will_o_wisp", dto.AllKnownCardBaseIds);
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

    [Fact]
    public async Task Get_DiscoveredSummonCard_RevealsReferencedAllyCharacter()
    {
        _factory.ResetData();
        const string acc = "bestiary-ctrl-summon";
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, acc);
        WithAccount(client, acc);

        var repo = _factory.Services.GetRequiredService<IBestiaryRepository>();
        var seeded = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("starter_summon_3"),
        };
        await repo.SaveAsync(acc, seeded, CancellationToken.None);

        var dto = await client.GetFromJsonAsync<BestiaryDto>("/api/v1/bestiary");

        var wisp = Assert.Single(dto!.AllyCharacters, character => character.Id == "wisp");
        Assert.True(wisp.IsDiscovered);
    }
}
