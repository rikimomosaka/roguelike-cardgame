using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class AudioSettingsControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public AudioSettingsControllerTests(TempDataFactory factory) => _factory = factory;

    private sealed record AudioDto(int SchemaVersion, int Master, int Bgm, int Se, int Ambient);

    private async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
            res.EnsureSuccessStatusCode();
    }

    private static HttpClient WithAccount(HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        client.DefaultRequestHeaders.Add("X-Account-Id", id);
        return client;
    }

    [Fact]
    public async Task Get_MissingHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/audio-settings");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Get_AccountMissing_Returns404()
    {
        var client = WithAccount(_factory.CreateClient(), "ghost");
        var res = await client.GetAsync("/api/v1/audio-settings");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Get_NewAccount_ReturnsDefault()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "alice");
        WithAccount(client, "alice");

        var res = await client.GetAsync("/api/v1/audio-settings");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AudioDto>();
        Assert.NotNull(dto);
        Assert.Equal(80, dto!.Master);
        Assert.Equal(70, dto.Bgm);
        Assert.Equal(80, dto.Se);
        Assert.Equal(60, dto.Ambient);
    }

    [Fact]
    public async Task Put_ValidValues_Returns204_AndPersists()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "bob");
        WithAccount(client, "bob");

        var put = await client.PutAsJsonAsync("/api/v1/audio-settings",
            new AudioDto(1, Master: 10, Bgm: 20, Se: 30, Ambient: 40));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var got = await client.GetFromJsonAsync<AudioDto>("/api/v1/audio-settings");
        Assert.Equal(10, got!.Master);
    }

    [Fact]
    public async Task Put_OutOfRange_Returns400()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "carol");
        WithAccount(client, "carol");

        var res = await client.PutAsJsonAsync("/api/v1/audio-settings",
            new AudioDto(1, Master: 200, Bgm: 0, Se: 0, Ambient: 0));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Put_AccountMissing_Returns404()
    {
        var client = WithAccount(_factory.CreateClient(), "ghost");
        var res = await client.PutAsJsonAsync("/api/v1/audio-settings",
            new AudioDto(1, 50, 50, 50, 50));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
