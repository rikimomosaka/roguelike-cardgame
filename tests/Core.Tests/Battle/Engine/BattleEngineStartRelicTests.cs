using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineStartRelicTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    private static RunState MakeRun(string[]? relicIds = null, ImmutableArray<string>? potions = null)
    {
        var p = potions ?? ImmutableArray.Create("", "", "");
        return new RunState(
            SchemaVersion: RunState.CurrentSchemaVersion,
            CurrentAct: 1, CurrentNodeId: 0,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: System.Collections.Immutable.ImmutableDictionary<int, TileKind>.Empty,
            CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
            Deck: ImmutableArray.Create(new CardInstance("strike", false)),
            Potions: p,
            PotionSlotCount: p.Length,
            ActiveBattle: null, ActiveReward: null,
            EncounterQueueWeak: ImmutableArray<string>.Empty,
            EncounterQueueStrong: ImmutableArray<string>.Empty,
            EncounterQueueElite: ImmutableArray<string>.Empty,
            EncounterQueueBoss: ImmutableArray<string>.Empty,
            RewardRngState: new RewardRngState(0, 0),
            ActiveMerchant: null, ActiveEvent: null,
            ActiveRestPending: false, ActiveRestCompleted: false,
            Relics: relicIds ?? Array.Empty<string>(),
            PlaySeconds: 0L, RngSeed: 0UL,
            SavedAtUtc: DateTimeOffset.UnixEpoch,
            Progress: RunProgress.InProgress,
            RunId: "test-run",
            ActiveActStartRelicChoice: null,
            SeenCardBaseIds: ImmutableArray<string>.Empty,
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<JourneyEntry>.Empty);
    }

    [Fact]
    public void Start_with_no_relics_emits_BattleStart_and_TurnStart_only()
    {
        var run = MakeRun();
        var catalog = BattleFixtures.MinimalCatalog();

        var (state, events) = BattleEngine.Start(run, "enc_test", MakeRng(), catalog);

        Assert.Contains(events, e => e.Kind == BattleEventKind.BattleStart);
        Assert.Contains(events, e => e.Kind == BattleEventKind.TurnStart);
        Assert.DoesNotContain(events, e => e.Note != null && e.Note.Contains("relic:"));
    }

    [Fact]
    public void Start_with_OnBattleStart_relic_fires_after_TurnStart()
    {
        var relic = BattleFixtures.Relic("bs", RelicTrigger.OnBattleStart, true,
            new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var run = MakeRun(new[] { "bs" });

        var (state, events) = BattleEngine.Start(run, "enc_test", MakeRng(), catalog);

        Assert.Equal(5, state.Allies[0].Block.RawTotal);
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:bs")).ToList();
        Assert.Single(relicEvs);
        // OnBattleStart events は TurnStart event より後
        var tsIdx = events.ToList().FindIndex(e => e.Kind == BattleEventKind.TurnStart);
        var rsIdx = events.ToList().FindIndex(e => e.Note != null && e.Note.Contains("relic:bs"));
        Assert.True(rsIdx > tsIdx);
    }

    [Fact]
    public void Start_snapshots_OwnedRelicIds_from_RunState()
    {
        var relic = BattleFixtures.Relic("bs", RelicTrigger.OnBattleStart);
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var run = MakeRun(new[] { "bs" });

        var (state, _) = BattleEngine.Start(run, "enc_test", MakeRng(), catalog);

        Assert.Single(state.OwnedRelicIds);
        Assert.Equal("bs", state.OwnedRelicIds[0]);
    }

    [Fact]
    public void Start_snapshots_Potions_from_RunState()
    {
        var run = MakeRun(potions: ImmutableArray.Create("p1", "", "p2"));
        var catalog = BattleFixtures.MinimalCatalog();

        var (state, _) = BattleEngine.Start(run, "enc_test", MakeRng(), catalog);

        Assert.Equal(3, state.Potions.Length);
        Assert.Equal("p1", state.Potions[0]);
        Assert.Equal("", state.Potions[1]);
        Assert.Equal("p2", state.Potions[2]);
    }

    [Fact]
    public void Start_with_Implemented_false_OnBattleStart_skips()
    {
        var relic = BattleFixtures.Relic("unimpl", RelicTrigger.OnBattleStart, implemented: false,
            new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var run = MakeRun(new[] { "unimpl" });

        var (state, _) = BattleEngine.Start(run, "enc_test", MakeRng(), catalog);

        Assert.Equal(0, state.Allies[0].Block.RawTotal);
    }
}
