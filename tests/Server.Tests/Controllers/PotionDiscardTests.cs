using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class PotionDiscardTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public PotionDiscardTests(TempDataFactory f) => _factory = f;

    private async Task<HttpClient> StartRunAsync(string accountId)
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        (await client.PostAsync("/api/v1/runs/new", content: null)).EnsureSuccessStatusCode();
        return client;
    }

    private async Task OverwritePotionsAsync(string accountId, ImmutableArray<string> potions)
    {
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var s = await repo.TryLoadAsync(accountId, CancellationToken.None);
        Assert.NotNull(s);
        await repo.SaveAsync(accountId, s! with { Potions = potions }, CancellationToken.None);
    }

    [Fact]
    public async Task PotionDiscard_EmptySlot_Returns400()
    {
        var client = await StartRunAsync("alice");
        await OverwritePotionsAsync("alice", ImmutableArray.Create("health_potion", "", ""));
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/potion/discard", new { slotIndex = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PotionDiscard_OutOfRange_Returns400()
    {
        var client = await StartRunAsync("bob");
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/potion/discard", new { slotIndex = 99 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PotionDiscard_OccupiedSlot_Returns204_AndSlotBecomesEmpty()
    {
        var client = await StartRunAsync("carol");
        await OverwritePotionsAsync("carol", ImmutableArray.Create("health_potion", "swift_potion", ""));
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/potion/discard", new { slotIndex = 0 });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var cur = await client.GetAsync("/api/v1/runs/current");
        var doc = JsonDocument.Parse(await cur.Content.ReadAsStringAsync());
        var potions = doc.RootElement.GetProperty("run").GetProperty("potions");
        Assert.Equal("", potions[0].GetString());
        Assert.Equal("swift_potion", potions[1].GetString());
    }

    [Fact]
    public async Task PotionDiscard_ThenReceivePotion_Succeeds()
    {
        var client = await StartRunAsync("dave");
        // Fill all 3 slots
        await OverwritePotionsAsync("dave", ImmutableArray.Create("health_potion", "swift_potion", "energy_potion"));
        // Discard one
        var disc = await client.PostAsJsonAsync("/api/v1/runs/current/potion/discard", new { slotIndex = 1 });
        Assert.Equal(HttpStatusCode.NoContent, disc.StatusCode);
        // Verify slot 1 is empty
        var after = await client.GetAsync("/api/v1/runs/current");
        var potions = JsonDocument.Parse(await after.Content.ReadAsStringAsync())
            .RootElement.GetProperty("run").GetProperty("potions");
        Assert.Equal("", potions[1].GetString());
    }

    [Fact]
    public async Task PotionUse_HealthPotion_HealsAndConsumesSlot()
    {
        var client = await StartRunAsync("erin");
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var state = await repo.TryLoadAsync("erin", CancellationToken.None);
        Assert.NotNull(state);
        await repo.SaveAsync("erin", state! with
        {
            CurrentHp = 40,
            Potions = ImmutableArray.Create("health_potion", "", ""),
        }, CancellationToken.None);

        var response = await client.PostAsJsonAsync(
            "/api/v1/runs/current/potion/use", new { slotIndex = 0 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await repo.TryLoadAsync("erin", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(55, updated!.CurrentHp);
        Assert.Equal("", updated.Potions[0]);
    }

    [Fact]
    public async Task PotionUse_BattleOnlyPotion_ReturnsConflictAndKeepsSlot()
    {
        var client = await StartRunAsync("frank");
        await OverwritePotionsAsync("frank", ImmutableArray.Create("fire_potion", "", ""));

        var response = await client.PostAsJsonAsync(
            "/api/v1/runs/current/potion/use", new { slotIndex = 0 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var updated = await repo.TryLoadAsync("frank", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("fire_potion", updated!.Potions[0]);
    }

    [Fact]
    public async Task PotionUse_CompatibilityAlias_UsesSameOutsideBattleFlow()
    {
        var client = await StartRunAsync("grace");
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var state = await repo.TryLoadAsync("grace", CancellationToken.None);
        Assert.NotNull(state);
        await repo.SaveAsync("grace", state! with
        {
            CurrentHp = 20,
            Potions = ImmutableArray.Create("health_potion", "", ""),
        }, CancellationToken.None);

        var response = await client.PostAsJsonAsync(
            "/api/v1/runs/current/use-potion", new { slotIndex = 0 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await repo.TryLoadAsync("grace", CancellationToken.None);
        Assert.Equal(35, updated!.CurrentHp);
        Assert.Equal("", updated.Potions[0]);
    }
}
