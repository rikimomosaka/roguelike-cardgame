using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (BattleState, IReadOnlyList<BattleEvent>) EndTurn(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        // Legacy fixtures/snapshots retain the original one-command behavior.
        // Explicit participant battles use EndTurn as the primary participant's
        // reversible Ready toggle; a solo battle still resolves immediately.
        if (state.Participants.IsDefaultOrEmpty)
            return ResolveQueuedTurn(state, rng, catalog);

        return SetParticipantReady(
            state,
            state.Participants[0].ParticipantId,
            isReady: true,
            rng,
            catalog);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ResolveQueuedTurn(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var detailed = ResolveQueuedTurnDetailed(state, rng, catalog);
        var events = detailed.ResolutionEvents.ToBuilder();
        if (detailed.NextTurnCpuPlanning is not null)
        {
            foreach (var action in detailed.NextTurnCpuPlanning.Actions)
            {
                foreach (var battleEvent in action.Events)
                    events.Add(battleEvent with { Order = events.Count });
            }
        }
        return (detailed.State, events.ToImmutable());
    }

    private static QueuedTurnResolutionResult ResolveQueuedTurnDetailed(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException($"EndTurn requires Phase=PlayerInput, got {state.Phase}");
        RejectWhileCardChoiceIsPending(state, "EndTurn");

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.EndTurn, Order: 0),
        };
        int order = 1;

        // 1. PlayerAttacking
        var s = state with { Phase = BattlePhase.PlayerAttacking };
        var (afterPA, evsPA) = PlayerAttackingResolver.Resolve(s, rng, catalog);
        s = afterPA;
        AddWithOrder(events, evsPA, ref order);

        if (s.PendingTurnResolution is not null)
            return PausedQueuedTurn(s, events);

        return ContinueAfterPlayerAttacking(
            s, events, order, rng, catalog, planNextTurnCpu: true);
    }

    /// <summary>
    /// Continues queued turn resolution after a human resolves a choice raised
    /// by an effect-only execution. Next-turn CPU planning is deliberately
    /// left to the external-command coordinator so it is journaled once.
    /// </summary>
    internal static (BattleState State, IReadOnlyList<BattleEvent> Events)
        ResumeQueuedTurnAfterChoice(
            BattleState state, IRng rng, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(catalog);
        if (state.PendingCardPlay is not null
            || (!state.Participants.IsDefaultOrEmpty
                && state.Participants.Any(participant =>
                    participant.PendingCardPlay is not null)))
            throw new InvalidOperationException(
                "Queued turn cannot resume until the pending card choice is resolved.");
        var continuation = state.PendingTurnResolution
            ?? throw new InvalidOperationException(
                "Queued turn has no pending resolution continuation.");

        var events = new List<BattleEvent>();
        int order = 0;
        var s = state with { PendingTurnResolution = null };
        QueuedTurnResolutionResult result;

        switch (continuation.Stage)
        {
            case TurnResolutionContinuationStage.PlayerAttacking:
            {
                s = s with { Phase = BattlePhase.PlayerAttacking };
                var (afterAttacks, attackEvents) =
                    PlayerAttackingResolver.ResolveRemaining(
                        s,
                        continuation.RemainingAllyActorInstanceIds,
                        rng,
                        catalog);
                s = afterAttacks;
                AddWithOrder(events, attackEvents, ref order);
                if (s.PendingTurnResolution is not null)
                    return (s, events.ToImmutableArray());
                result = ContinueAfterPlayerAttacking(
                    s, events, order, rng, catalog,
                    planNextTurnCpu: false);
                break;
            }
            case TurnResolutionContinuationStage.EnemyAttacking:
            {
                s = s with { Phase = BattlePhase.EnemyAttacking };
                var (afterAttacks, attackEvents) =
                    EnemyAttackingResolver.ResolveRemaining(
                        s,
                        continuation.RemainingEnemyActorInstanceIds,
                        rng,
                        catalog,
                        continuation.FirstEnemyBlockAlreadyReset,
                        continuation.FirstEnemySingleTargetActorInstanceId);
                s = afterAttacks;
                AddWithOrder(events, attackEvents, ref order);
                if (s.PendingTurnResolution is not null)
                    return (s, events.ToImmutableArray());
                result = ContinueAfterEnemyAttacking(
                    s, events, order, rng, catalog,
                    planNextTurnCpu: false);
                break;
            }
            case TurnResolutionContinuationStage.TurnStartContingencies:
            {
                var (afterContingencies, contingencyEvents, paused) =
                    ContingencyResolutionEngine.ResolveTurnStart(
                        s, rng, catalog);
                s = afterContingencies;
                AddWithOrder(events, contingencyEvents, ref order);
                if (paused)
                {
                    s = s with { PendingTurnResolution = continuation };
                    return (s, events.ToImmutableArray());
                }
                var (afterStart, startEvents) =
                    TurnStartProcessor.ContinueAfterContingencies(
                        s, rng, catalog);
                s = BattleParticipantTurnLifecycle.CommitPrimaryRoot(afterStart);
                AddWithOrder(events, startEvents, ref order);
                result = CompleteAfterTurnStart(
                    s, events, order, rng, catalog,
                    planNextTurnCpu: false);
                break;
            }
            default:
                throw new InvalidOperationException(
                    $"Unsupported queued-turn continuation stage '{continuation.Stage}'.");
        }

        return (result.State, result.ResolutionEvents);
    }

    private static QueuedTurnResolutionResult ContinueAfterPlayerAttacking(
        BattleState s,
        List<BattleEvent> events,
        int order,
        IRng rng,
        DataCatalog catalog,
        bool planNextTurnCpu)
    {

        // 2. 死亡判定 + 自動切替
        s = TargetingAutoSwitch.Apply(s);
        var (afterPlayerOutcome, playerOutcomeEvents) =
            BattleOutcomeResolver.ApplyIfTerminal(s, order);
        if (playerOutcomeEvents.Count > 0)
        {
            s = afterPlayerOutcome;
            AddWithOrder(events, playerOutcomeEvents, ref order);
            return new QueuedTurnResolutionResult(
                s, events.ToImmutableArray(), null,
                StateBeforeNextTurnCpuPlanning: s);
        }

        // 2.5. 味方側 status countdown (= player turn の終わり)。
        //  weak/vulnerable/poison 等の Decrement 系 status を 1 減らす。
        //  ここで countdown するのは「ターンを終えた側」だけ — 敵が後続の
        //  EnemyAttacking で player に新規 debuff を付与しても、その debuff は
        //  player の次のターンで活きるようになる (旧仕様は TurnStart で全
        //  actor 一斉 countdown だったため即削除されてバグになっていた)。
        var (afterAllyCd, evsAllyCd) = SideStatusCountdown.ApplyForSide(s, ActorSide.Ally, order);
        s = afterAllyCd;
        order += evsAllyCd.Count;
        events.AddRange(evsAllyCd);

        // 3. EnemyAttacking
        s = s with { Phase = BattlePhase.EnemyAttacking };
        var (afterEA, evsEA) = EnemyAttackingResolver.Resolve(s, rng, catalog);
        s = afterEA;
        AddWithOrder(events, evsEA, ref order);

        if (s.PendingTurnResolution is not null)
            return PausedQueuedTurn(s, events);

        return ContinueAfterEnemyAttacking(
            s, events, order, rng, catalog, planNextTurnCpu);
    }

    private static QueuedTurnResolutionResult ContinueAfterEnemyAttacking(
        BattleState s,
        List<BattleEvent> events,
        int order,
        IRng rng,
        DataCatalog catalog,
        bool planNextTurnCpu)
    {

        // 4. 死亡判定 + 自動切替
        s = TargetingAutoSwitch.Apply(s);
        // Summons/tokens are not player lives. A dead hero is a defeat even
        // when an allied token remains on the field (unless a revive effect
        // has already restored the hero before this checkpoint).
        var (afterEnemyOutcome, enemyOutcomeEvents) =
            BattleOutcomeResolver.ApplyIfTerminal(s, order);
        if (enemyOutcomeEvents.Count > 0)
        {
            s = afterEnemyOutcome;
            AddWithOrder(events, enemyOutcomeEvents, ref order);
            return new QueuedTurnResolutionResult(
                s, events.ToImmutableArray(), null,
                StateBeforeNextTurnCpuPlanning: s);
        }

        // 4.5. 敵側 status countdown (= enemy turn の終わり)。
        //  player が PlayerAttacking で敵に付与した weak/vulnerable は、敵が
        //  EnemyAttacking で実際に弱体化された後に countdown される (vulnerable=2
        //  ならこの enemy turn で 1 回弱体化を受けて、countdown で 1 → 次の enemy
        //  turn でもう 1 回弱体化を受けて、countdown で 0、合計 2 ターン分)。
        var (afterEnemyCd, evsEnemyCd) = SideStatusCountdown.ApplyForSide(s, ActorSide.Enemy, order);
        s = afterEnemyCd;
        order += evsEnemyCd.Count;
        events.AddRange(evsEnemyCd);

        // 5. ターン終了処理
        var (afterEnd, evsEnd) = TurnEndProcessor.Process(s, rng, catalog);
        s = afterEnd;
        AddWithOrder(events, evsEnd, ref order);
        s = BattleParticipantTurnLifecycle.CommitPrimaryRoot(s);
        s = BattleParticipantTurnLifecycle.ApplyEndToSecondaryParticipants(
            s, catalog);

        // Actor-bound turn-end statuses are counted down only after every
        // participant has decided whether its combo is carried. This keeps a
        // final Combo Carryover stack available long enough for secondary
        // participant state to set its explicit pending marker.
        var (afterFormalStatuses, formalStatusEvents) =
            FormalTurnEndStatusProcessor.Process(s, order);
        s = afterFormalStatuses;
        AddWithOrder(events, formalStatusEvents, ref order);

        // OnTurnEnd relics and powers may deal self damage. Resolve this
        // boundary before any next-turn draw/status/trigger can occur. Revive
        // has already been applied by the damage effect when present.
        s = SummonCleanup.Apply(s, events, ref order);
        s = TargetingAutoSwitch.Apply(s);
        var (afterTurnEndOutcome, turnEndOutcomeEvents) =
            BattleOutcomeResolver.ApplyIfTerminal(s, order);
        if (turnEndOutcomeEvents.Count > 0)
        {
            s = afterTurnEndOutcome;
            AddWithOrder(events, turnEndOutcomeEvents, ref order);
            return new QueuedTurnResolutionResult(
                s, events.ToImmutableArray(), null,
                StateBeforeNextTurnCpuPlanning: s);
        }

        // 6. ターン開始処理
        var (afterStart, evsStart) = TurnStartProcessor.Process(s, rng, catalog);
        AddWithOrder(events, evsStart, ref order);
        s = BattleParticipantTurnLifecycle.CommitPrimaryRoot(afterStart);

        if (s.PendingTurnResolution is not null)
            return PausedQueuedTurn(s, events);

        return CompleteAfterTurnStart(
            s, events, order, rng, catalog, planNextTurnCpu);
    }

    private static QueuedTurnResolutionResult CompleteAfterTurnStart(
        BattleState s,
        List<BattleEvent> events,
        int order,
        IRng rng,
        DataCatalog catalog,
        bool planNextTurnCpu)
    {

        // TurnStart 中に毒死などで Outcome 確定した場合、Phase 上書きをスキップ
        if (s.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
        {
            var terminal = BattleParticipantTurnLifecycle.ApplyTerminalReset(s);
            return new QueuedTurnResolutionResult(
                terminal,
                events.ToImmutableArray(),
                null,
                StateBeforeNextTurnCpuPlanning: terminal);
        }

        s = s with { Phase = BattlePhase.PlayerInput };
        // カード制御の敵は味方CPU・プレイヤーより先に行動を予約し、予告を確定させる。
        // 敵の計画はターン開始処理の一部として扱い、味方CPU計画前の基準 state に含める。
        // 敵の計画は予測のみなので、ライブのイベント列には流さない（行動再生の記録には残る）。
        var (afterEnemyPlan, _) = EnemyCardPlanner.StartTurnAndPlan(
            s, rng, catalog);
        s = afterEnemyPlan;
        var stateBeforeNextTurnCpuPlanning = s;
        if (!planNextTurnCpu)
        {
            return new QueuedTurnResolutionResult(
                s, events.ToImmutableArray(), null,
                stateBeforeNextTurnCpuPlanning);
        }
        var cpuPlanning = BattleCpuPlanningCoordinator.PlanUntilReady(
            stateBeforeNextTurnCpuPlanning, rng, catalog);
        s = cpuPlanning.State;
        return new QueuedTurnResolutionResult(
            s, events.ToImmutableArray(), cpuPlanning,
            stateBeforeNextTurnCpuPlanning);
    }

    private static QueuedTurnResolutionResult PausedQueuedTurn(
        BattleState state,
        List<BattleEvent> events)
    {
        if (state.PendingTurnResolution is null)
        {
            throw new InvalidOperationException(
                "A resolving card choice must preserve its queued-turn continuation.");
        }
        return new QueuedTurnResolutionResult(
            state,
            events.ToImmutableArray(),
            null,
            StateBeforeNextTurnCpuPlanning: state);
    }

    private sealed record QueuedTurnResolutionResult(
        BattleState State,
        ImmutableArray<BattleEvent> ResolutionEvents,
        BattleCpuPlanningResult? NextTurnCpuPlanning,
        BattleState StateBeforeNextTurnCpuPlanning);

    private static (BattleState, IReadOnlyList<BattleEvent>) ResolveOutcome(
        BattleState s, RoguelikeCardGame.Core.Battle.State.BattleOutcome outcome,
        List<BattleEvent> events, ref int order)
    {
        // 勝敗確定時は通常の TurnEndProcessor を通らず早期 return するため、
        // ここでもターン限定のコンボ状態を同じ値へリセットする。
        s = s with
        {
            Phase = BattlePhase.Resolved,
            Outcome = outcome,
            ComboCount = 0,
            MaxComboThisTurn = 0,
            LastPlayedOrigCost = null,
            NextCardComboFreePass = false,
            WildUsedInCurrentCombo = false,
            UsedCharacterSkillIdsThisTurn = System.Collections.Immutable.ImmutableHashSet<string>.Empty,
            CharacterSkillUsesThisTurn = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty,
            CardUseLockedThisTurn = false,
            CardUsesThisTurn = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty,
            PotionUsesThisTurn = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty,
        };
        s = BattleCardCostModifierLifecycle.Remove(
            s, CardCostModifierDuration.UntilTurnEnd);
        s = s with
        {
            Allies = s.Allies.Select(actor => actor with
            {
                Statuses = actor.Statuses
                    .Remove(BattleCardCostCalculator.HandCostReductionStatusId)
                    .Remove(BattleCardCostCalculator.HandCostIncreaseStatusId),
                StatusSources = actor.StatusSources
                    .Remove(BattleCardCostCalculator.HandCostReductionStatusId)
                    .Remove(BattleCardCostCalculator.HandCostIncreaseStatusId),
            }).ToImmutableArray(),
        };
        events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order, Note: outcome.ToString()));
        order++;
        return (BattleParticipantTurnLifecycle.ApplyTerminalReset(s), events);
    }

    private static void AddWithOrder(List<BattleEvent> dest, IReadOnlyList<BattleEvent> src, ref int order)
    {
        foreach (var ev in src)
        {
            dest.Add(ev with { Order = order });
            order++;
        }
    }
}
