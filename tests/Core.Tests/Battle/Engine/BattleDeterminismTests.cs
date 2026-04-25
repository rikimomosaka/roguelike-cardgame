using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleDeterminismTests
{
    private static RunState MakeRun() => new(
        SchemaVersion: RunState.CurrentSchemaVersion,
        CurrentAct: 1, CurrentNodeId: 0,
        VisitedNodeIds: ImmutableArray<int>.Empty,
        UnknownResolutions: ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
        CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
        Deck: Enumerable.Range(0, 10).Select(i => new CardInstance("strike", false)).ToImmutableArray(),
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

    private static (BattleState, System.Collections.Generic.List<BattleEvent>) RunBattle(int seed)
    {
        var rng = new SequentialRng((ulong)seed);
        var run = MakeRun();
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", rng, cat);
        var allEvents = new System.Collections.Generic.List<BattleEvent>();

        // 1 ターン目：先頭の strike を打って EndTurn
        var (s2, evs1) = BattleEngine.PlayCard(s, 0, 0, 0, rng, cat);
        allEvents.AddRange(evs1);
        var (s3, evs2) = BattleEngine.EndTurn(s2, rng, cat);
        allEvents.AddRange(evs2);
        return (s3, allEvents);
    }

    /// <summary>ImmutableArray はレコード等価では参照比較になるため JSON シリアライズで深い構造比較を行う。</summary>
    private static string StateJson(BattleState s) =>
        JsonSerializer.Serialize(new
        {
            s.Turn, Phase = s.Phase.ToString(), Outcome = s.Outcome.ToString(),
            s.TargetAllyIndex, s.TargetEnemyIndex, s.Energy, s.EnergyMax, s.EncounterId,
            Allies = s.Allies.Select(a => new {
                a.InstanceId, a.DefinitionId, Side = a.Side.ToString(),
                a.SlotIndex, a.CurrentHp, a.MaxHp,
                Block = a.Block.Sum, AttackSingle = a.AttackSingle.Sum,
                AttackRandom = a.AttackRandom.Sum, AttackAll = a.AttackAll.Sum,
                a.CurrentMoveId }),
            Enemies = s.Enemies.Select(e => new {
                e.InstanceId, e.DefinitionId, Side = e.Side.ToString(),
                e.SlotIndex, e.CurrentHp, e.MaxHp,
                Block = e.Block.Sum, AttackSingle = e.AttackSingle.Sum,
                AttackRandom = e.AttackRandom.Sum, AttackAll = e.AttackAll.Sum,
                e.CurrentMoveId }),
            DrawPile = s.DrawPile.Select(c => new { c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride }),
            Hand = s.Hand.Select(c => new { c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride }),
            DiscardPile = s.DiscardPile.Select(c => new { c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride }),
            ExhaustPile = s.ExhaustPile.Select(c => new { c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride }),
        });

    [Fact] public void Same_seed_same_inputs_yields_identical_state()
    {
        var (a, _) = RunBattle(seed: 42);
        var (b, _) = RunBattle(seed: 42);
        Assert.Equal(StateJson(a), StateJson(b));
    }

    [Fact] public void Same_seed_same_inputs_yields_identical_events()
    {
        var (_, ea) = RunBattle(seed: 42);
        var (_, eb) = RunBattle(seed: 42);
        Assert.Equal(ea, eb);
    }
}
