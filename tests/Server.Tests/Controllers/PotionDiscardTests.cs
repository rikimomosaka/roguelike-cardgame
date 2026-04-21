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
}
