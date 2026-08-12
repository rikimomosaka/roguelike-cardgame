using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class RunMoveGuardTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public RunMoveGuardTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task Move_UnresolvedEvent_Returns409AndPreservesEvent()
    {
        const string accountId = "move-guard-event";
        var (client, targetNodeId) = await StartRunAsync(accountId);
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var state = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        var pendingEvent = new EventInstance(
            "test_event", ImmutableArray<EventChoice>.Empty, ChosenIndex: null);
        await repo.SaveAsync(
            accountId,
            state with { ActiveEvent = pendingEvent },
            CancellationToken.None);

        var response = await MoveAsync(client, targetNodeId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        Assert.Equal(state.CurrentNodeId, after.CurrentNodeId);
        Assert.Equal(pendingEvent, after.ActiveEvent);
    }

    [Fact]
    public async Task Move_BeforeEnteringActStart_Returns409AndPreservesStartNode()
    {
        const string accountId = "move-guard-act-start-entry";
        var (client, targetNodeId) = await StartRunAsync(accountId, completeActStart: false);
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var before = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;

        var response = await MoveAsync(client, targetNodeId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        Assert.Equal(before.CurrentNodeId, after.CurrentNodeId);
        Assert.Empty(after.VisitedNodeIds);
    }

    [Fact]
    public async Task Move_UncompletedRest_Returns409AndPreservesRestState()
    {
        const string accountId = "move-guard-rest";
        var (client, targetNodeId) = await StartRunAsync(accountId);
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var state = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        await repo.SaveAsync(
            accountId,
            state with { ActiveRestPending = true, ActiveRestCompleted = false },
            CancellationToken.None);

        var response = await MoveAsync(client, targetNodeId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        Assert.Equal(state.CurrentNodeId, after.CurrentNodeId);
        Assert.True(after.ActiveRestPending);
        Assert.False(after.ActiveRestCompleted);
    }

    [Fact]
    public async Task Move_PendingActStartRelicChoice_Returns409AndPreservesChoice()
    {
        const string accountId = "move-guard-act-start";
        var (client, targetNodeId) = await StartRunAsync(accountId);
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var state = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        var pool = catalog.ActStartRelicPools![state.CurrentAct];
        var choice = new ActStartRelicChoice(
            ImmutableArray.Create(pool[0], pool[1], pool[2]));
        await repo.SaveAsync(
            accountId,
            state with { ActiveActStartRelicChoice = choice },
            CancellationToken.None);

        var response = await MoveAsync(client, targetNodeId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        Assert.Equal(state.CurrentNodeId, after.CurrentNodeId);
        Assert.True(choice.RelicIds.SequenceEqual(after.ActiveActStartRelicChoice!.RelicIds));
    }

    [Fact]
    public async Task Move_ResolvedEvent_AllowsLeavingNode()
    {
        const string accountId = "move-guard-resolved-event";
        var (client, targetNodeId) = await StartRunAsync(accountId);
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var state = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        var resolvedEvent = new EventInstance(
            "test_event", ImmutableArray<EventChoice>.Empty, ChosenIndex: 0);
        await repo.SaveAsync(
            accountId,
            state with { ActiveEvent = resolvedEvent },
            CancellationToken.None);

        var response = await MoveAsync(client, targetNodeId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        Assert.Equal(targetNodeId, after.CurrentNodeId);
    }

    [Fact]
    public async Task Move_CompletedRest_AllowsLeavingNode()
    {
        const string accountId = "move-guard-completed-rest";
        var (client, targetNodeId) = await StartRunAsync(accountId);
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var state = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        await repo.SaveAsync(
            accountId,
            state with { ActiveRestPending = true, ActiveRestCompleted = true },
            CancellationToken.None);

        var response = await MoveAsync(client, targetNodeId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        Assert.Equal(targetNodeId, after.CurrentNodeId);
    }

    [Fact]
    public async Task Move_ActiveMerchant_RemainsAllowed()
    {
        const string accountId = "move-guard-merchant";
        var (client, targetNodeId) = await StartRunAsync(accountId);
        var repo = _factory.Services.GetRequiredService<ISaveRepository>();
        var state = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        var inventory = new MerchantInventory(
            ImmutableArray<MerchantOffer>.Empty,
            ImmutableArray<MerchantOffer>.Empty,
            ImmutableArray<MerchantOffer>.Empty,
            DiscardSlotUsed: false,
            DiscardPrice: 75);
        await repo.SaveAsync(
            accountId,
            state with { ActiveMerchant = inventory },
            CancellationToken.None);

        var response = await MoveAsync(client, targetNodeId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = (await repo.TryLoadAsync(accountId, CancellationToken.None))!;
        Assert.Equal(targetNodeId, after.CurrentNodeId);
    }

    private async Task<(HttpClient Client, int TargetNodeId)> StartRunAsync(
        string accountId,
        bool completeActStart = true)
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);

        var response = await client.PostAsync("/api/v1/runs/new", content: null);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        int currentNodeId = document.RootElement
            .GetProperty("run")
            .GetProperty("currentNodeId")
            .GetInt32();
        if (completeActStart)
            await BattleTestHelpers.MarkActStartResolvedForTestAsync(
                _factory.Services, accountId);
        foreach (var node in document.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
        {
            if (node.GetProperty("id").GetInt32() != currentNodeId) continue;
            int targetNodeId = node.GetProperty("outgoingNodeIds")[0].GetInt32();
            return (client, targetNodeId);
        }

        throw new Xunit.Sdk.XunitException("Current map node was not returned in the snapshot");
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, int targetNodeId) =>
        client.PostAsJsonAsync(
            "/api/v1/runs/current/move",
            new { nodeId = targetNodeId, elapsedSeconds = 0 });
}
