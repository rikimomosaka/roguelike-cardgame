using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Endpoint-level regression coverage for run RNG streams. Elapsed play time is
/// presentation/progress data and must not change a result at identical run coordinates.
/// </summary>
public sealed class RunRngPlaySecondsDeterminismTests : IClassFixture<TempDataFactory>
{
    private const long ShortPlayTime = 1;
    private const long LongPlayTime = 99_999;

    private readonly TempDataFactory _factory;

    public RunRngPlaySecondsDeterminismTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task MoveToUnknown_SameCoordinates_IgnoresPlaySeconds_ForKindAndContent()
    {
        var twins = await CreateTwinRunAsync("rng-node");
        var runStart = _factory.Services.GetRequiredService<RunStartService>();
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var map = runStart.RehydrateMap(twins.State.RngSeed, twins.State.CurrentAct);

        var (predecessor, target, expectedKind) = FindUnknownWithRandomContent(
            twins.State, map, catalog);
        var positioned = twins.State with
        {
            CurrentNodeId = predecessor.Id,
            VisitedNodeIds = ImmutableArray.Create(predecessor.Id),
            UnknownResolutions = twins.State.UnknownResolutions.Remove(target.Id),
        };
        await SaveTwinStatesAsync(twins, positioned);

        var firstResponse = await twins.First.PostAsJsonAsync(
            "/api/v1/runs/current/move",
            new { nodeId = target.Id, elapsedSeconds = 0 });
        var secondResponse = await twins.Second.PostAsJsonAsync(
            "/api/v1/runs/current/move",
            new { nodeId = target.Id, elapsedSeconds = 0 });
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        var first = (await twins.Repository.TryLoadAsync(
            twins.FirstAccountId, CancellationToken.None))!;
        var second = (await twins.Repository.TryLoadAsync(
            twins.SecondAccountId, CancellationToken.None))!;

        Assert.Equal(expectedKind, first.UnknownResolutions[target.Id]);
        Assert.Equal(first.UnknownResolutions[target.Id], second.UnknownResolutions[target.Id]);
        var firstContent = ContentSignature(first, expectedKind);
        Assert.NotEqual("null", firstContent);
        Assert.Equal(firstContent, ContentSignature(second, expectedKind));
    }

    [Fact]
    public async Task EventChoice_SameCoordinates_IgnoresPlaySeconds()
    {
        var twins = await CreateTwinRunAsync("rng-event");
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        var definition = catalog.Events["shady_merchant"];
        var eventState = twins.State with
        {
            Gold = 500,
            ActiveEvent = new EventInstance(definition.Id, definition.Choices),
        };
        await SaveTwinStatesAsync(twins, eventState);

        var firstResponse = await twins.First.PostAsJsonAsync(
            "/api/v1/event/choose", new { choiceIndex = 0 });
        var secondResponse = await twins.Second.PostAsJsonAsync(
            "/api/v1/event/choose", new { choiceIndex = 0 });
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        var first = (await twins.Repository.TryLoadAsync(
            twins.FirstAccountId, CancellationToken.None))!;
        var second = (await twins.Repository.TryLoadAsync(
            twins.SecondAccountId, CancellationToken.None))!;

        var firstRelic = Assert.Single(first.Relics);
        Assert.Equal(firstRelic, Assert.Single(second.Relics));
        Assert.Equal(first.CurrentHp, second.CurrentHp);
        Assert.Equal(first.MaxHp, second.MaxHp);
        Assert.Equal(first.AcquiredRelicIds.ToArray(), second.AcquiredRelicIds.ToArray());
    }

    [Fact]
    public async Task BattleReward_SameCoordinates_IgnoresPlaySeconds()
    {
        var twins = await CreateTwinRunAtBattleAsync("rng-battle-reward");
        await SaveTwinStatesAsync(twins, twins.State);

        var firstResponse = await twins.First.PostAsJsonAsync(
            "/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });
        var secondResponse = await twins.Second.PostAsJsonAsync(
            "/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        var first = (await twins.Repository.TryLoadAsync(
            twins.FirstAccountId, CancellationToken.None))!;
        var second = (await twins.Repository.TryLoadAsync(
            twins.SecondAccountId, CancellationToken.None))!;

        Assert.NotNull(first.ActiveReward);
        Assert.Equal(RewardSignature(first.ActiveReward!), RewardSignature(second.ActiveReward!));
        Assert.Equal(first.RewardRngState, second.RewardRngState);
    }

    [Fact]
    public async Task RewardReroll_SameCoordinates_IgnoresPlaySeconds()
    {
        var twins = await CreateTwinRunAtBattleAsync("rng-reroll");

        var win = await twins.First.PostAsJsonAsync(
            "/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });
        win.EnsureSuccessStatusCode();
        var withReward = (await twins.Repository.TryLoadAsync(
            twins.FirstAccountId, CancellationToken.None))!;
        Assert.NotNull(withReward.ActiveReward);

        var rerollable = withReward with
        {
            Relics = new[] { "gamble_dice" },
            ActiveReward = withReward.ActiveReward! with
            {
                CardStatus = CardRewardStatus.Pending,
                RerollUsed = false,
            },
        };
        await SaveTwinStatesAsync(twins, rerollable);

        var firstResponse = await twins.First.PostAsync(
            "/api/v1/runs/current/reward/reroll-card-choices", null);
        var secondResponse = await twins.Second.PostAsync(
            "/api/v1/runs/current/reward/reroll-card-choices", null);
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();

        var first = (await twins.Repository.TryLoadAsync(
            twins.FirstAccountId, CancellationToken.None))!;
        var second = (await twins.Repository.TryLoadAsync(
            twins.SecondAccountId, CancellationToken.None))!;

        Assert.True(first.ActiveReward!.RerollUsed);
        Assert.Equal(RewardSignature(first.ActiveReward), RewardSignature(second.ActiveReward!));
        Assert.Equal(first.RewardRngState, second.RewardRngState);
    }

    private async Task<TwinRun> CreateTwinRunAtBattleAsync(string accountPrefix)
    {
        var twins = await CreateTwinRunAsync(accountPrefix);
        var map = _factory.Services.GetRequiredService<RunStartService>()
            .RehydrateMap(twins.State.RngSeed, twins.State.CurrentAct);
        var start = map.GetNode(twins.State.CurrentNodeId);
        var enemy = start.OutgoingNodeIds
            .Select(map.GetNode)
            .First(node => node.Kind == TileKind.Enemy);

        var move = await twins.First.PostAsJsonAsync(
            "/api/v1/runs/current/move", new { nodeId = enemy.Id, elapsedSeconds = 0 });
        move.EnsureSuccessStatusCode();
        var atBattle = (await twins.Repository.TryLoadAsync(
            twins.FirstAccountId, CancellationToken.None))!;
        Assert.NotNull(atBattle.ActiveBattle);
        return twins with { State = atBattle };
    }

    private async Task<TwinRun> CreateTwinRunAsync(string accountPrefix)
    {
        _factory.ResetData();
        string firstAccountId = accountPrefix + "-a";
        string secondAccountId = accountPrefix + "-b";
        var first = _factory.CreateClient();
        var second = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(first, firstAccountId);
        await BattleTestHelpers.EnsureAccountAsync(second, secondAccountId);
        BattleTestHelpers.WithAccount(first, firstAccountId);
        BattleTestHelpers.WithAccount(second, secondAccountId);

        var start = await first.PostAsync("/api/v1/runs/new", null);
        start.EnsureSuccessStatusCode();
        await BattleTestHelpers.MarkActStartResolvedForTestAsync(
            _factory.Services, firstAccountId);

        var repository = _factory.Services.GetRequiredService<ISaveRepository>();
        var state = (await repository.TryLoadAsync(
            firstAccountId, CancellationToken.None))!;
        return new TwinRun(
            first, second, firstAccountId, secondAccountId, repository, state);
    }

    private static async Task SaveTwinStatesAsync(TwinRun twins, RunState state)
    {
        await twins.Repository.SaveAsync(
            twins.FirstAccountId,
            state with { PlaySeconds = ShortPlayTime },
            CancellationToken.None);
        await twins.Repository.SaveAsync(
            twins.SecondAccountId,
            state with { PlaySeconds = LongPlayTime },
            CancellationToken.None);
    }

    private static (MapNode Predecessor, MapNode Target, TileKind ExpectedKind)
        FindUnknownWithRandomContent(RunState state, DungeonMap map, DataCatalog catalog)
    {
        var config = catalog.UnknownConfig
            ?? MapGenerationConfigLoader.LoadAct1().UnknownResolutionWeights;

        foreach (var target in map.Nodes.Where(node => node.Kind == TileKind.Unknown))
        {
            var predecessor = map.Nodes.FirstOrDefault(
                node => node.OutgoingNodeIds.Contains(target.Id));
            if (predecessor is null) continue;

            var atTarget = state with { CurrentNodeId = target.Id };
            var weights = PassiveModifiers.ApplyUnknownWeightDeltas(config, atTarget, catalog);
            if (weights.Values.Sum() <= 0) weights = config.Weights;
            var rng = new SystemRng(RunRngSeed.Derive(
                state.RngSeed,
                state.CurrentAct,
                target.Id,
                RunRngPurpose.UnknownKind));
            var kind = UnknownResolver.ResolveOne(weights, rng);
            if (kind != TileKind.Rest)
                return (predecessor, target, kind);
        }

        throw new InvalidOperationException(
            "The fixed test map has no Unknown node that resolves to randomized content.");
    }

    private static string ContentSignature(RunState state, TileKind kind) => kind switch
    {
        TileKind.Enemy or TileKind.Elite or TileKind.Boss =>
            JsonSerializer.Serialize(state.ActiveBattle),
        TileKind.Merchant => JsonSerializer.Serialize(state.ActiveMerchant),
        TileKind.Treasure => JsonSerializer.Serialize(state.ActiveReward),
        TileKind.Event => JsonSerializer.Serialize(state.ActiveEvent),
        _ => throw new InvalidOperationException(
            $"{kind} does not exercise randomized node content."),
    };

    private static string RewardSignature(RewardState reward) =>
        JsonSerializer.Serialize(reward);

    private sealed record TwinRun(
        HttpClient First,
        HttpClient Second,
        string FirstAccountId,
        string SecondAccountId,
        ISaveRepository Repository,
        RunState State);
}
