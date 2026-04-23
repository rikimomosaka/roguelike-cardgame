using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
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
}
