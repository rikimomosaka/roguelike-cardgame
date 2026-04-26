using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineFinalizeTests
{
    private static RunState MakeRun(int hp = 70)
    {
        return new RunState(
            SchemaVersion: RunState.CurrentSchemaVersion,
            CurrentAct: 1, CurrentNodeId: 0,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
            CharacterId: "default", CurrentHp: hp, MaxHp: 70, Gold: 0,
            Deck: ImmutableArray.Create(new CardInstance("strike", false)),
            Potions: ImmutableArray<string>.Empty, PotionSlotCount: 0,
            ActiveBattle: null, ActiveReward: null,
            EncounterQueueWeak: ImmutableArray<string>.Empty,
            EncounterQueueStrong: ImmutableArray<string>.Empty,
            EncounterQueueElite: ImmutableArray<string>.Empty,
            EncounterQueueBoss: ImmutableArray<string>.Empty,
            RewardRngState: new RoguelikeCardGame.Core.Rewards.RewardRngState(0, 0),
            ActiveMerchant: null, ActiveEvent: null,
            ActiveRestPending: false, ActiveRestCompleted: false,
            Relics: System.Array.Empty<string>(),
            PlaySeconds: 0, RngSeed: 1,
            SavedAtUtc: System.DateTimeOffset.UtcNow,
            Progress: RunProgress.InProgress,
            RunId: "run1", ActiveActStartRelicChoice: null,
            SeenCardBaseIds: ImmutableArray<string>.Empty,
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<RoguelikeCardGame.Core.Run.JourneyEntry>.Empty);
    }

    private static BattleState MakeResolved(int finalHp, BattleOutcome outcome) => new(
        Turn: 3, Phase: BattlePhase.Resolved, Outcome: outcome,
        Allies: ImmutableArray.Create(BattleFixtures.Hero(hp: finalHp)),
        Enemies: ImmutableArray<CombatActor>.Empty,
        TargetAllyIndex: 0, TargetEnemyIndex: null,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    [Fact] public void Throws_when_battle_not_resolved()
    {
        var run = MakeRun();
        var bs = MakeResolved(50, BattleOutcome.Victory) with { Phase = BattlePhase.PlayerInput };
        Assert.Throws<System.InvalidOperationException>(() => BattleEngine.Finalize(bs, run));
    }

    [Fact] public void Victory_returns_run_with_updated_hp_and_progress_inprogress()
    {
        var run = MakeRun(hp: 70);
        var bs = MakeResolved(45, BattleOutcome.Victory);
        var (after, summary) = BattleEngine.Finalize(bs, run);
        Assert.Equal(45, after.CurrentHp);
        Assert.Equal(RunProgress.InProgress, after.Progress);
        Assert.Equal(45, summary.FinalHeroHp);
        Assert.Equal(BattleOutcome.Victory, summary.Outcome);
    }

    [Fact] public void Defeat_sets_progress_to_GameOver()
    {
        var run = MakeRun(hp: 70);
        var bs = MakeResolved(0, BattleOutcome.Defeat);
        var (after, summary) = BattleEngine.Finalize(bs, run);
        Assert.Equal(0, after.CurrentHp);
        Assert.Equal(RunProgress.GameOver, after.Progress);
        Assert.Equal(BattleOutcome.Defeat, summary.Outcome);
    }

    [Fact] public void Battle_deck_does_not_leak_into_run()
    {
        var run = MakeRun(hp: 70);
        // 戦闘内パイルに余分なカードを置いた resolved BattleState
        var bs = MakeResolved(50, BattleOutcome.Victory) with
        {
            DrawPile = ImmutableArray.Create(BattleFixtures.MakeBattleCard("garbage", "g1")),
        };
        var (after, _) = BattleEngine.Finalize(bs, run);
        Assert.Single(after.Deck); // 元の "strike" 1 枚だけ
        Assert.Equal("strike", after.Deck[0].Id);
    }

    [Fact] public void ActiveBattle_cleared_to_null()
    {
        // 注: 10.2.A 段階では RunState.ActiveBattle は BattlePlaceholderState 型なので、
        // Finalize は ActiveBattle を直接いじらない。Phase 10.5 の wire-up で対応。
        var run = MakeRun(hp: 70);
        var bs = MakeResolved(50, BattleOutcome.Victory);
        var (after, _) = BattleEngine.Finalize(bs, run);
        Assert.Null(after.ActiveBattle);
    }
}
