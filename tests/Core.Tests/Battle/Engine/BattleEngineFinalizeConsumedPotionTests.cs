using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineFinalizeConsumedPotionTests
{
    private static RunState MakeRun(ImmutableArray<string> potions)
    {
        return new RunState(
            SchemaVersion: RunState.CurrentSchemaVersion,
            CurrentAct: 1, CurrentNodeId: 0,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: System.Collections.Immutable.ImmutableDictionary<int, TileKind>.Empty,
            CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
            Deck: ImmutableArray.Create(new CardInstance("strike", false)),
            Potions: potions,
            PotionSlotCount: potions.Length,
            ActiveBattle: null, ActiveReward: null,
            EncounterQueueWeak: ImmutableArray<string>.Empty,
            EncounterQueueStrong: ImmutableArray<string>.Empty,
            EncounterQueueElite: ImmutableArray<string>.Empty,
            EncounterQueueBoss: ImmutableArray<string>.Empty,
            RewardRngState: new RewardRngState(0, 0),
            ActiveMerchant: null, ActiveEvent: null,
            ActiveRestPending: false, ActiveRestCompleted: false,
            Relics: Array.Empty<string>(),
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
    public void Finalize_with_no_consumption_returns_empty_ConsumedPotionIds()
    {
        var before = MakeRun(ImmutableArray.Create("p1", "p2", ""));
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p1", "p2", "")) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, summary) = BattleEngine.Finalize(state, before);

        Assert.Empty(summary.ConsumedPotionIds);
        Assert.Equal<IEnumerable<string>>(before.Potions, nextRun.Potions);
    }

    [Fact]
    public void Finalize_one_consumed_returns_potion_id_in_ConsumedPotionIds()
    {
        var before = MakeRun(ImmutableArray.Create("p1", "p2", ""));
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "p2", "")) with {  // p1 消費
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, summary) = BattleEngine.Finalize(state, before);

        Assert.Single(summary.ConsumedPotionIds);
        Assert.Equal("p1", summary.ConsumedPotionIds[0]);
        Assert.Equal("", nextRun.Potions[0]);
        Assert.Equal("p2", nextRun.Potions[1]);
    }

    [Fact]
    public void Finalize_same_id_in_two_slots_consumed_returns_two_entries()
    {
        var before = MakeRun(ImmutableArray.Create("p1", "p1", ""));
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "", "")) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, summary) = BattleEngine.Finalize(state, before);

        Assert.Equal(2, summary.ConsumedPotionIds.Length);
        Assert.Equal("p1", summary.ConsumedPotionIds[0]);
        Assert.Equal("p1", summary.ConsumedPotionIds[1]);
    }

    [Fact]
    public void Finalize_state_Potions_is_copied_to_RunState_Potions_wholesale()
    {
        var before = MakeRun(ImmutableArray.Create("p1", "p2", "p3"));
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p1", "", "p3")) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, _) = BattleEngine.Finalize(state, before);

        Assert.Equal<IEnumerable<string>>(state.Potions, nextRun.Potions);
    }

    [Fact]
    public void Finalize_Defeat_sets_Progress_to_GameOver()
    {
        var before = MakeRun(ImmutableArray.Create("", "", ""));
        var state = BattleFixtures.MinimalState() with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat,
        };

        var (nextRun, _) = BattleEngine.Finalize(state, before);

        Assert.Equal(RoguelikeCardGame.Core.Run.RunProgress.GameOver, nextRun.Progress);
    }

    [Fact]
    public void Finalize_Victory_keeps_Progress()
    {
        var before = MakeRun(ImmutableArray.Create("", "", ""));
        var state = BattleFixtures.MinimalState() with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, _) = BattleEngine.Finalize(state, before);

        Assert.Equal(before.Progress, nextRun.Progress);
    }

    [Fact]
    public void Finalize_uses_hero_DefinitionId_search_for_finalHp()
    {
        var before = MakeRun(ImmutableArray.Create("", "", ""));
        var injuredHero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 25 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(injuredHero)) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, summary) = BattleEngine.Finalize(state, before);

        Assert.Equal(25, summary.FinalHeroHp);
        Assert.Equal(25, nextRun.CurrentHp);
    }

    [Fact]
    public void Finalize_clamps_negative_hero_HP_to_zero()
    {
        var before = MakeRun(ImmutableArray.Create("", "", ""));
        var deadHero = BattleFixtures.Hero(hp: 70) with { CurrentHp = -5 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(deadHero)) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat,
        };

        var (_, summary) = BattleEngine.Finalize(state, before);

        Assert.Equal(0, summary.FinalHeroHp);
    }
}
