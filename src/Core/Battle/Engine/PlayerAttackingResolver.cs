using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// PlayerAttacking フェーズを解決する。
/// 蓄積された攻撃は合計値ではなく1ヒットずつ、追加された順に処理する。
/// </summary>
internal static class PlayerAttackingResolver
{
    private sealed record PendingEnemyHitReaction(
        string AttackerInstanceId,
        string TargetInstanceId,
        HitContext Context,
        IReadOnlyList<BattleEvent> DamageEvents,
        int ReflectAmountAtImpact);

    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState state, IRng rng, DataCatalog catalog) =>
        ResolveActors(state, actorIds: null, rng, catalog);

    /// <summary>
    /// Continues a player-attacking phase from the authoritative actor-id
    /// snapshot captured when an Afterimage choice paused it. Actors summoned
    /// while that choice is open are intentionally absent from this phase.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) ResolveRemaining(
        BattleState state,
        IReadOnlyList<string> remainingActorInstanceIds,
        IRng rng,
        DataCatalog catalog)
    {
        System.ArgumentNullException.ThrowIfNull(remainingActorInstanceIds);
        return ResolveActors(
            state with { PendingTurnResolution = null },
            remainingActorInstanceIds,
            rng,
            catalog);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ResolveActors(
        BattleState state,
        IReadOnlyList<string>? actorIds,
        IRng rng,
        DataCatalog catalog)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        // 現行ソロは SlotIndex 順（主人公→召喚）。マルチでは BattleState に
        // ルーム確定順を追加し、その順へ置き換える（仕様書 §3）。
        var allyIds = state.Allies
            .OrderBy(a => a.SlotIndex)
            .Select(a => a.InstanceId)
            .ToList();
        if (actorIds is not null)
            allyIds = actorIds.ToList();

        for (int allyOrderIndex = 0;
             allyOrderIndex < allyIds.Count;
             allyOrderIndex++)
        {
            string allyId = allyIds[allyOrderIndex];
            if (ResolutionShouldStop(state)) break;
            var ally = FindAlly(state, allyId);
            if (ally is null || !ally.IsAlive) continue;

            int hitIndex = 0;
            bool isSummon = ally.DefinitionId != "hero" && ally.CurrentMoveId is not null;
            bool hasPlannedAttack = isSummon
                ? HasSummonAttack(ally, catalog)
                : ally.AttackSingle.AddCount + ally.AttackRandom.AddCount + ally.AttackAll.AddCount > 0;
            if (hasPlannedAttack && ally.GetStatus(BattleStatusIds.Pursuit) > 0)
            {
                state = ResolvePursuit(
                    state, ally, events, ref order, ref hitIndex, catalog, rng);
                if (ResolutionShouldStop(state)) break;
                ally = FindAlly(state, allyId) ?? ally;
                if (!ally.IsAlive) continue;
            }
            if (isSummon)
            {
                state = ResolveSummonMove(
                    state, ally, events, ref order, ref hitIndex, catalog, rng);
            }
            else if (ally.GetStatus("omnistrike") > 0)
            {
                state = ResolveOmnistrike(
                    state, ally, events, ref order, ref hitIndex, catalog, rng);
            }
            else if (ally.GetStatus(BattleStatusIds.Disruption) > 0)
            {
                state = ResolveDisrupted(
                    state, ally, events, ref order, ref hitIndex, catalog, rng);
            }
            else
            {
                state = ResolveSingle(
                    state, ally, events, ref order, ref hitIndex, catalog, rng);
                state = ResolveRandom(
                    state, ally, rng, events, ref order, ref hitIndex, catalog);
                state = ResolveAll(
                    state, ally, events, ref order, ref hitIndex, catalog, rng);
            }

            var afterimage = BattleEngine.ResolveAfterimageAtActorEnd(
                state,
                allyId,
                rng,
                catalog,
                order);
            state = afterimage.State;
            foreach (var battleEvent in afterimage.Events)
                events.Add(battleEvent with { Order = order++ });
            if (afterimage.Paused)
            {
                state = state with
                {
                    PendingTurnResolution = new PendingTurnResolution(
                        TurnResolutionContinuationStage.PlayerAttacking)
                    {
                        RemainingAllyActorInstanceIds = allyIds
                            .Skip(allyOrderIndex + 1)
                            .ToImmutableArray(),
                    },
                };
                return (state, events);
            }
        }

        state = SummonCleanup.Apply(state, events, ref order);
        return (state, events);
    }

    /// <summary>
    /// Resolves only attacks appended by an effect-only execution such as
    /// Afterimage. Existing queued hits remain observational history and are
    /// never fired twice. Pursuit is deliberately not evaluated here.
    /// </summary>
    public static (BattleState State, IReadOnlyList<BattleEvent> Events)
        ResolveAddedAttacks(
            BattleState state,
            string actorInstanceId,
            int beforeSingleCount,
            int beforeRandomCount,
            int beforeAllCount,
        IRng rng,
        DataCatalog catalog,
        int orderStart,
        int hitIndexStart = 0,
        bool deferEnemyDeathTriggers = false)
    {
        var actor = FindAlly(state, actorInstanceId)
            ?? throw new System.InvalidOperationException(
                $"Added-attack actor '{actorInstanceId}' does not exist.");
        var singleHits = AddedHits(
            actor.AttackSingle, beforeSingleCount, nameof(beforeSingleCount));
        var randomHits = AddedHits(
            actor.AttackRandom, beforeRandomCount, nameof(beforeRandomCount));
        var allHits = AddedHits(
            actor.AttackAll, beforeAllCount, nameof(beforeAllCount));

        var events = new List<BattleEvent>();
        int order = orderStart;
        int hitIndex = hitIndexStart;
        var s = state;
        if (!actor.IsAlive)
            return (s, events);

        if (actor.GetStatus(BattleStatusIds.Omnistrike) > 0)
        {
            foreach (var hit in singleHits.Concat(randomHits).Concat(allHits))
            {
                if (ResolutionShouldStop(s)) break;
                var attacker = FindAlly(s, actorInstanceId);
                if (attacker is null || !attacker.IsAlive) break;
                s = ResolveImmediateAllHit(
                    s, attacker, hit.Amount, "omnistrike",
                    events, ref order, ref hitIndex, catalog, rng,
                    hit.Source, hit.CauseChain, deferEnemyDeathTriggers);
            }
            return (s, events);
        }

        if (actor.GetStatus(BattleStatusIds.Disruption) > 0)
        {
            foreach (var hit in singleHits.Concat(randomHits).Concat(allHits))
            {
                if (ResolutionShouldStop(s)) break;
                var attacker = FindAlly(s, actorInstanceId);
                if (attacker is null || !attacker.IsAlive) break;
                s = ResolveImmediateRandomHit(
                    s, attacker, hit.Amount, "random", rng,
                    events, ref order, ref hitIndex, catalog,
                    hit.Source, hit.CauseChain, deferEnemyDeathTriggers);
            }
            return (s, events);
        }

        // Match the normal resolver's pool ordering. Each appended Single keeps
        // its committed target, with the same one-time chain-start fallback.
        var aliveAtChainStart = SnapshotEnemyAliveIds(s);
        string? fallbackTargetId = GetSelectedEnemyId(
            s, actorInstanceId, retargetIfUnavailable: true);
        foreach (var hit in singleHits)
        {
            if (ResolutionShouldStop(s)) break;
            string? targetId = hit.TargetInstanceId;
            if (targetId is null || !aliveAtChainStart.Contains(targetId))
                targetId = fallbackTargetId;
            if (targetId is null) continue;
            int targetIndex = FindEnemyIndex(s, targetId);
            if (targetIndex < 0 || !s.Enemies[targetIndex].IsAlive) continue;
            var attacker = FindAlly(s, actorInstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            s = ResolveImmediateSingleHit(
                s, attacker, targetId, hit.Amount, "single",
                events, ref order, ref hitIndex, catalog, rng,
                hit.Source, hit.CauseChain, deferEnemyDeathTriggers);
        }

        foreach (var hit in randomHits)
        {
            if (ResolutionShouldStop(s)) break;
            var attacker = FindAlly(s, actorInstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            s = ResolveImmediateRandomHit(
                s, attacker, hit.Amount, "random", rng,
                events, ref order, ref hitIndex, catalog,
                hit.Source, hit.CauseChain, deferEnemyDeathTriggers);
        }

        foreach (var hit in allHits)
        {
            if (ResolutionShouldStop(s)) break;
            var attacker = FindAlly(s, actorInstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            s = ResolveImmediateAllHit(
                s, attacker, hit.Amount, "all",
                events, ref order, ref hitIndex, catalog, rng,
                hit.Source, hit.CauseChain, deferEnemyDeathTriggers);
        }

        return (s, events);
    }

    private static IReadOnlyList<AttackHit> AddedHits(
        AttackPool pool, int beforeCount, string parameterName)
    {
        if (beforeCount < 0 || beforeCount > pool.AddCount)
            throw new System.ArgumentOutOfRangeException(
                parameterName,
                beforeCount,
                $"Snapshot count must be between 0 and {pool.AddCount}.");
        return pool.Hits.Skip(beforeCount).ToArray();
    }

    private static bool HasSummonAttack(CombatActor ally, DataCatalog catalog) =>
        catalog.TryGetUnit(ally.DefinitionId, out var unit)
        && unit.Moves.FirstOrDefault(move => move.Id == ally.CurrentMoveId)
            ?.Effects.Any(effect => effect.Action == "attack") == true;

    private static BattleState ResolvePursuit(
        BattleState state,
        CombatActor ally,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        int amount = ally.GetStatus(BattleStatusIds.Pursuit);
        if (amount <= 0) return state;

        var consumed = BattleStatusRules.Decrement(ally, BattleStatusIds.Pursuit);
        int allyIndex = FindAllyIndex(state, ally.InstanceId);
        if (allyIndex < 0) return state;
        state = state with { Allies = state.Allies.SetItem(allyIndex, consumed) };

        var effectiveScope = AttackScopeRules.Resolve(consumed, EffectScope.All);
        string scopeNote = effectiveScope == EffectScope.Random
            ? "random"
            : BattleStatusIds.Pursuit;
        var source = new CombatSource(
            CombatSourceKind.Status,
            OwnerSlot: consumed.OwnerPlayerSlot ?? consumed.SlotIndex,
            OwnerInstanceId: consumed.OwnerActorInstanceId ?? consumed.InstanceId,
            ActorInstanceId: consumed.InstanceId,
            DefinitionId: BattleStatusIds.Pursuit,
            Scope: effectiveScope.ToString().ToLowerInvariant());

        state = effectiveScope == EffectScope.Random
            ? ResolveImmediateRandomHit(
                state, consumed, amount, scopeNote, rng,
                events, ref order, ref hitIndex, catalog, source)
            : ResolveImmediateAllHit(
                state, consumed, amount, scopeNote,
                events, ref order, ref hitIndex, catalog, rng, source);

        if (amount == 1)
        {
            events.Add(new BattleEvent(
                BattleEventKind.RemoveStatus, Order: order++,
                TargetInstanceId: ally.InstanceId,
                Note: BattleStatusIds.Pursuit,
                Source: ally.GetStatusSource(BattleStatusIds.Pursuit)));
        }
        return state;
    }

    private static BattleState ResolveSummonMove(
        BattleState state,
        CombatActor ally,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        if (!catalog.TryGetUnit(ally.DefinitionId, out var unitDef)) return state;
        var move = unitDef.Moves.FirstOrDefault(m => m.Id == ally.CurrentMoveId);
        if (move is null) return state;

        string? singleTargetId = GetSelectedEnemyId(state);
        var current = ally;
        var owner = ally.OwnerActorInstanceId is { } persistedOwnerId
            ? state.Allies.FirstOrDefault(a => a.InstanceId == persistedOwnerId)
            : null;
        owner ??= state.Allies
            .Where(a => a.DefinitionId == "hero")
            .OrderBy(a => a.SlotIndex)
            .FirstOrDefault()
            ?? state.Allies.OrderBy(a => a.SlotIndex).FirstOrDefault();

        // 攪乱: ユニットの行動（技 1 回）も失敗しうる。失敗しても次の技へは進む。
        bool moveFailed = DisruptionRules.RollActionFailure(ally, rng);
        if (moveFailed)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ActionFailed, Order: order++,
                CasterInstanceId: ally.InstanceId,
                Amount: DisruptionRules.FailurePercent(
                    ally.GetStatus(BattleStatusIds.Disruption))));
        }

        for (int effectIndex = 0; !moveFailed && effectIndex < move.Effects.Count; effectIndex++)
        {
            if (ResolutionShouldStop(state)) break;
            var effect = move.Effects[effectIndex];
            // An earlier effect may kill an enemy and fire OnEnemyDeath, which can
            // mutate this summon. Always continue from that latest actor snapshot.
            var refreshed = FindAlly(state, current.InstanceId);
            if (refreshed is null || !refreshed.IsAlive) break;
            current = refreshed;

            var effectiveScope = effect.Action == "attack"
                ? AttackScopeRules.Resolve(current, effect.Scope)
                : effect.Scope;
            var source = new CombatSource(
                CombatSourceKind.SummonMove,
                OwnerSlot: ally.OwnerPlayerSlot ?? owner?.SlotIndex,
                OwnerInstanceId: ally.OwnerActorInstanceId ?? owner?.InstanceId,
                ActorInstanceId: current.InstanceId,
                DefinitionId: current.DefinitionId,
                InstanceId: current.AssociatedSummonHeldInstanceId,
                MoveId: move.Id,
                EffectIndex: effectIndex,
                Scope: effectiveScope.ToString().ToLowerInvariant());

            if (effect.Action == "attack")
            {
                state = effectiveScope switch
                {
                    EffectScope.Single => ResolveImmediateSingleHit(
                        state, current, singleTargetId, effect.Amount, "single",
                        events, ref order, ref hitIndex, catalog, rng, source),
                    EffectScope.Random => ResolveImmediateRandomHit(
                        state, current, effect.Amount, "random", rng,
                        events, ref order, ref hitIndex, catalog, source),
                    EffectScope.All => ResolveImmediateAllHit(
                        state, current, effect.Amount, "all",
                        events, ref order, ref hitIndex, catalog, rng, source),
                    _ => state,
                };
            }
            else if (effect.Action == "block")
            {
                var newAlly = current with { Block = current.Block.Add(effect.Amount) };
                int allyIndex = FindAllyIndex(state, current.InstanceId);
                if (allyIndex < 0) continue;

                state = state with { Allies = state.Allies.SetItem(allyIndex, newAlly) };
                events.Add(new BattleEvent(
                    BattleEventKind.GainBlock,
                    Order: order++,
                    CasterInstanceId: current.InstanceId,
                    TargetInstanceId: current.InstanceId,
                    Amount: effect.Amount,
                    Source: source));
                current = newAlly;
            }
            else
            {
                var (afterApply, applyEvents) = EffectApplier.Apply(
                    state, current, effect, rng, catalog, source);
                state = afterApply;
                foreach (var battleEvent in applyEvents)
                    events.Add(battleEvent with
                    {
                        Order = order++,
                        Source = battleEvent.Source ?? source,
                    });
            }
            if (ResolutionShouldStop(state)) break;
        }

        int index = FindAllyIndex(state, current.InstanceId);
        if (index >= 0)
        {
            var transitioned = state.Allies[index] with { CurrentMoveId = move.NextMoveId };
            state = state with { Allies = state.Allies.SetItem(index, transitioned) };
        }

        return state;
    }

    private static BattleState ResolveOmnistrike(
        BattleState state,
        CombatActor ally,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        // 連結順を維持したまま、各ヒットのScopeだけをAllへ変える。
        var combined = ally.AttackSingle + ally.AttackRandom + ally.AttackAll;
        foreach (var hit in combined.Hits)
        {
            if (ResolutionShouldStop(state)) break;
            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateAllHit(
                state, attacker, hit.Amount, "omnistrike",
                events, ref order, ref hitIndex, catalog, rng, hit.Source,
                hit.CauseChain);
            if (ResolutionShouldStop(state)) break;
        }

        return state;
    }

    private static BattleState ResolveDisrupted(
        BattleState state,
        CombatActor ally,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        // Each queued hit performs an independent random draw. Combining the
        // pools here also converts target-locked Single hits and All hits that
        // were committed before Disruption was applied.
        var combined = ally.AttackSingle + ally.AttackRandom + ally.AttackAll;
        foreach (var hit in combined.Hits)
        {
            if (ResolutionShouldStop(state)) break;
            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateRandomHit(
                state, attacker, hit.Amount, "random", rng,
                events, ref order, ref hitIndex, catalog, hit.Source,
                hit.CauseChain);
        }

        return state;
    }

    private static BattleState ResolveSingle(
        BattleState state,
        CombatActor ally,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        // Each hit keeps the selection captured when its card was committed.
        // Targets unavailable before this actor starts may retarget once;
        // deaths during this actor's chain make only that target's later hits fizzle.
        var aliveAtChainStart = SnapshotEnemyAliveIds(state);
        string? fallbackTargetId = GetSelectedEnemyId(
            state, ally.InstanceId, retargetIfUnavailable: true);

        foreach (var hit in ally.AttackSingle.Hits)
        {
            if (ResolutionShouldStop(state)) break;
            string? targetId = hit.TargetInstanceId;
            if (targetId is null || !aliveAtChainStart.Contains(targetId))
                targetId = fallbackTargetId;
            if (targetId is null) continue;

            int targetIndex = FindEnemyIndex(state, targetId);
            // A target that died after this actor chain began stays dead for
            // the rest of that locked chain. Continue because a later card
            // may have committed its hits against a different enemy.
            if (targetIndex < 0 || !state.Enemies[targetIndex].IsAlive) continue;

            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateSingleHit(
                state, attacker, targetId, hit.Amount, "single",
                events, ref order, ref hitIndex, catalog, rng, hit.Source,
                hit.CauseChain);
            if (ResolutionShouldStop(state)) break;
        }

        return state;
    }

    private static BattleState ResolveRandom(
        BattleState state,
        CombatActor ally,
        IRng rng,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog)
    {
        foreach (var hit in ally.AttackRandom.Hits)
        {
            if (ResolutionShouldStop(state)) break;
            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateRandomHit(
                state, attacker, hit.Amount, "random", rng,
                events, ref order, ref hitIndex, catalog, hit.Source,
                hit.CauseChain);
            if (ResolutionShouldStop(state)) break;
        }

        return state;
    }

    private static BattleState ResolveAll(
        BattleState state,
        CombatActor ally,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        foreach (var hit in ally.AttackAll.Hits)
        {
            if (ResolutionShouldStop(state)) break;
            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateAllHit(
                state, attacker, hit.Amount, "all",
                events, ref order, ref hitIndex, catalog, rng, hit.Source,
                hit.CauseChain);
            if (ResolutionShouldStop(state)) break;
        }

        return state;
    }

    private static BattleState ResolveImmediateSingleHit(
        BattleState state,
        CombatActor attacker,
        string? targetId,
        int amount,
        string scopeNote,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng,
        CombatSource? source = null,
        DamageCauseChain? causeChain = null,
        bool deferEnemyDeathTriggers = false)
    {
        if (targetId is null) return state;
        int targetIndex = FindEnemyIndex(state, targetId);
        if (targetIndex < 0 || !state.Enemies[targetIndex].IsAlive) return state;

        int currentHitIndex = hitIndex++;
        state = DealEnemyHit(
            state, attacker, targetId, amount, scopeNote, currentHitIndex,
            events, ref order, ref hitIndex, catalog, rng, source, causeChain,
            deferEnemyDeathTriggers);
        return state;
    }

    private static BattleState ResolveImmediateRandomHit(
        BattleState state,
        CombatActor attacker,
        int amount,
        string scopeNote,
        IRng rng,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        CombatSource? source = null,
        DamageCauseChain? causeChain = null,
        bool deferEnemyDeathTriggers = false)
    {
        var candidates = AliveEnemyIds(state);
        if (candidates.Count == 0) return state;

        int selected = rng.NextInt(0, candidates.Count);
        int currentHitIndex = hitIndex++;
        state = DealEnemyHit(
            state, attacker, candidates[selected], amount, scopeNote, currentHitIndex,
            events, ref order, ref hitIndex, catalog, rng, source, causeChain,
            deferEnemyDeathTriggers);
        return state;
    }

    private static BattleState ResolveImmediateAllHit(
        BattleState state,
        CombatActor attacker,
        int amount,
        string scopeNote,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng,
        CombatSource? source = null,
        DamageCauseChain? causeChain = null,
        bool deferEnemyDeathTriggers = false)
    {
        var targetIds = AliveEnemyIds(state);
        if (targetIds.Count == 0) return state;

        // 同じAllヒットの対象間へOnEnemyDeath等を割り込ませない。
        // 攻撃者と対象はこの論理ヒットの開始時点で固定する。全対象分の
        // root AttackFire/DealDamage を先に連続発行してから、対象slot順で
        // OnHit・吸血・反射を解決する。これにより子AttackFireが親Allを
        // 分断せず、クライアントは同じHitIndexを1回の全体攻撃として扱える。
        var beforeAlive = SnapshotEnemyAliveIds(state);
        int currentHitIndex = hitIndex++;
        var pendingReactions = new List<PendingEnemyHitReaction>();
        foreach (string targetId in targetIds)
        {
            int targetIndex = FindEnemyIndex(state, targetId);
            if (targetIndex < 0 || !state.Enemies[targetIndex].IsAlive) continue;

            state = ApplyEnemyDamage(
                state, attacker, targetId, amount, scopeNote, currentHitIndex,
                events, ref order, source, causeChain, out var pending);
            if (pending is not null)
                pendingReactions.Add(pending);
        }

        foreach (var pending in pendingReactions)
        {
            state = ApplyPostHitReactions(
                state,
                pending.AttackerInstanceId,
                pending.TargetInstanceId,
                pending.Context,
                pending.DamageEvents,
                pending.ReflectAmountAtImpact,
                events, ref order, ref hitIndex, catalog, rng);
            if (ResolutionShouldStop(state)) break;
        }

        return deferEnemyDeathTriggers
            ? state
            : FireOnEnemyDeathForNewlyDead(
                state, beforeAlive, events, ref order, catalog, rng);
    }

    private static BattleState DealEnemyHit(
        BattleState state,
        CombatActor attacker,
        string targetId,
        int amount,
        string scopeNote,
        int resolvedHitIndex,
        List<BattleEvent> events,
        ref int order,
        ref int nextHitIndex,
        DataCatalog catalog,
        IRng rng,
        CombatSource? source = null,
        DamageCauseChain? causeChain = null,
        bool deferEnemyDeathTriggers = false)
    {
        int targetIndex = FindEnemyIndex(state, targetId);
        if (targetIndex < 0 || !state.Enemies[targetIndex].IsAlive) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
        state = ApplyEnemyHit(
            state, attacker, targetId, amount, scopeNote, resolvedHitIndex,
            events, ref order, ref nextHitIndex, catalog, rng, source, causeChain);

        return deferEnemyDeathTriggers
            ? state
            : FireOnEnemyDeathForNewlyDead(
                state, beforeAlive, events, ref order, catalog, rng);
    }

    private static BattleState ApplyEnemyHit(
        BattleState state,
        CombatActor attacker,
        string targetId,
        int amount,
        string scopeNote,
        int hitIndex,
        List<BattleEvent> events,
        ref int order,
        ref int nextHitIndex,
        DataCatalog catalog,
        IRng rng,
        CombatSource? source = null,
        DamageCauseChain? causeChain = null)
    {
        state = ApplyEnemyDamage(
            state, attacker, targetId, amount, scopeNote, hitIndex,
            events, ref order, source, causeChain, out var pending);
        if (pending is null) return state;

        return ApplyPostHitReactions(
            state,
            pending.AttackerInstanceId,
            pending.TargetInstanceId,
            pending.Context,
            pending.DamageEvents,
            pending.ReflectAmountAtImpact,
            events, ref order, ref nextHitIndex, catalog, rng);
    }

    private static BattleState ApplyEnemyDamage(
        BattleState state,
        CombatActor attacker,
        string targetId,
        int amount,
        string scopeNote,
        int hitIndex,
        List<BattleEvent> events,
        ref int order,
        CombatSource? source,
        DamageCauseChain? causeChain,
        out PendingEnemyHitReaction? pending)
    {
        pending = null;
        int targetIndex = FindEnemyIndex(state, targetId);
        if (targetIndex < 0 || !state.Enemies[targetIndex].IsAlive) return state;

        var targetAtImpact = state.Enemies[targetIndex];
        var resolvedSource = ResolveSource(attacker, source, scopeNote);
        var context = CreateAttackContext(
            resolvedSource, causeChain ?? DamageCauseChain.Empty, hitIndex);
        var (updated, damageEvents, _) = DealDamageHelper.Apply(
            attacker,
            targetAtImpact,
            baseAmount: amount,
            scopeNote: scopeNote,
            orderBase: order,
            hitIndex: hitIndex,
            context: context);

        state = state with { Enemies = state.Enemies.SetItem(targetIndex, updated) };
        events.AddRange(damageEvents);
        order += damageEvents.Count;
        pending = new PendingEnemyHitReaction(
            attacker.InstanceId,
            targetId,
            context,
            damageEvents,
            targetAtImpact.GetStatus("reflect"));
        return state;
    }

    private static CombatSource ResolveSource(
        CombatActor attacker, CombatSource? source, string scopeNote)
    {
        if (source is not null && source.Kind != CombatSourceKind.Unattributed)
            return source.Scope is null ? source with { Scope = scopeNote } : source;
        return new CombatSource(
            CombatSourceKind.Unattributed,
            OwnerSlot: attacker.DefinitionId == "hero" ? attacker.SlotIndex : null,
            OwnerInstanceId: attacker.DefinitionId == "hero" ? attacker.InstanceId : null,
            ActorInstanceId: attacker.InstanceId,
            DefinitionId: attacker.DefinitionId, Scope: scopeNote);
    }

    private static HitContext CreateAttackContext(
        CombatSource source, DamageCauseChain parentChain, int hitIndex)
    {
        if (parentChain.Length == 0)
            return HitContext.Attack(source, hitIndex);

        var attackFrame = new DamageCauseFrame(
            parentChain.Length, DamageType.Attack, source, hitIndex, "attack");
        return new HitContext(
            DamageType.Attack, source, parentChain.Add(attackFrame), hitIndex);
    }

    private static BattleState ApplyPostHitReactions(
        BattleState state,
        string attackerId,
        string targetId,
        HitContext context,
        IReadOnlyList<BattleEvent> damageEvents,
        int reflectAmountAtImpact,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        int actualHpLoss = damageEvents
            .FirstOrDefault(e => e.Kind == BattleEventKind.DealDamage)?.ActualHpLoss ?? 0;
        var attacker = FindActor(state, attackerId);
        var target = FindActor(state, targetId);
        bool hitLanded = damageEvents.Any(
            e => e.Kind == BattleEventKind.AttackFire && e.HitLanded == true);

        // OnHit is established before lifesteal/reflect. It therefore still fires
        // when the attacker is subsequently killed by this hit's reflection.
        bool isOnHitDerived = context.CauseChain.Frames.Any(
            frame => frame.Cause == "OnHit");
        if (hitLanded && !isOnHitDerived)
        {
            var relicCaster = FindOnHitCaster(state, context.Source);
            var relicCounts = AttackPoolCounts.From(relicCaster);
            var (afterRelicOnHit, relicOnHitEvents) = RelicTriggerProcessor.FireOnHit(
                state, targetId, context, catalog, rng, orderStart: order);
            state = afterRelicOnHit;
            foreach (var ev in relicOnHitEvents) events.Add(ev with { Order = order++ });
            state = ResolveTriggeredAttacks(
                state, relicCaster?.InstanceId, relicCounts,
                events, ref order, ref hitIndex, catalog, rng);

            var powerCaster = FindOnHitCaster(state, context.Source);
            var powerCounts = AttackPoolCounts.From(powerCaster);
            var (afterOnHit, onHitEvents) = PowerTriggerProcessor.FireOnHit(
                state, targetId, context, catalog, rng, orderStart: order);
            state = afterOnHit;
            foreach (var ev in onHitEvents) events.Add(ev with { Order = order++ });
            state = ResolveTriggeredAttacks(
                state, powerCaster?.InstanceId, powerCounts,
                events, ref order, ref hitIndex, catalog, rng);
        }

        // OnHit effects may mutate either actor; reactions continue from latest state.
        attacker = FindActor(state, attackerId);
        target = FindActor(state, targetId);

        // An independently controlled participant owns its own combat
        // statuses even when it was summoned by another actor. Legacy
        // fixed-move summons keep their historical owner-attribution fallback.
        string? sourceActorId = context.Source.ActorInstanceId;
        bool sourceIsParticipant = sourceActorId is not null
            && !state.Participants.IsDefaultOrEmpty
            && BattleParticipantAccess.TryGetByActor(
                state, sourceActorId, out _);
        string ownerId = sourceIsParticipant
            ? sourceActorId!
            : context.Source.OwnerInstanceId ?? attackerId;
        var ownerAtReaction = FindActor(state, ownerId);
        int lifestealPercent = ownerAtReaction?.GetStatus("lifesteal") ?? 0;
        if (actualHpLoss > 0 && lifestealPercent > 0)
        {
            var owner = FindActor(state, ownerId);
            if (owner is not null && owner.IsAlive)
            {
                int requested = (actualHpLoss * lifestealPercent) / 100;
                int healed = System.Math.Min(System.Math.Max(0, requested), owner.MaxHp - owner.CurrentHp);
                if (healed > 0)
                {
                    state = ReplaceActor(state, ownerId, owner with { CurrentHp = owner.CurrentHp + healed });
                    events.Add(new BattleEvent(
                        BattleEventKind.Heal, Order: order++,
                        CasterInstanceId: attackerId, TargetInstanceId: ownerId,
                        Amount: healed, Note: "lifesteal",
                        DamageType: DamageType.Attack, Source: context.Source,
                        CauseChain: context.CauseChain, HitIndex: context.HitIndex));
                }
            }
        }

        // Reflect is fixed Direct damage: no Block/modifiers and no recursive reflect.
        int reflectAmount = reflectAmountAtImpact;
        attacker = FindActor(state, attackerId);
        if (hitLanded && reflectAmount > 0 && attacker is not null && attacker.IsAlive)
        {
            int hpBefore = attacker.CurrentHp;
            int actual = System.Math.Min(hpBefore, reflectAmount);
            int overkill = System.Math.Max(0, reflectAmount - hpBefore);
            var reflected = attacker with { CurrentHp = System.Math.Max(0, hpBefore - reflectAmount) };
            bool revived = BattleStatusRules.TryRevive(reflected, out var afterRevive);
            reflected = afterRevive;
            state = ReplaceActor(state, attackerId, reflected);
            var reflectSource = new CombatSource(
                CombatSourceKind.Status, OwnerSlot: target?.SlotIndex ?? 0,
                OwnerInstanceId: targetId, ActorInstanceId: targetId,
                DefinitionId: "reflect");
            var reflectContext = context.CreateChild(DamageType.Reflected, reflectSource, "reflect");
            events.Add(new BattleEvent(
                BattleEventKind.DealDamage, Order: order++,
                CasterInstanceId: targetId, TargetInstanceId: attackerId,
                Amount: reflectAmount, Note: "reflect", HitIndex: context.HitIndex,
                BlockedAmount: 0, DamageType: DamageType.Reflected, Source: reflectSource,
                CauseChain: reflectContext.CauseChain,
                ActualHpLoss: actual, Overkill: overkill, HitLanded: true));
            if (revived)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus, Order: order++,
                    TargetInstanceId: attackerId,
                    Note: BattleStatusIds.Revive,
                    Source: attacker.GetStatusSource(BattleStatusIds.Revive)));
                events.Add(new BattleEvent(
                    BattleEventKind.Heal, Order: order++,
                    TargetInstanceId: attackerId,
                    Amount: reflected.CurrentHp,
                    Note: BattleStatusIds.Revive));
            }
            else if (!reflected.IsAlive)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ActorDeath, Order: order++,
                    CasterInstanceId: targetId, TargetInstanceId: attackerId,
                    Note: "reflect", HitIndex: context.HitIndex,
                    DamageType: DamageType.Reflected, Source: reflectSource,
                    CauseChain: reflectContext.CauseChain,
                    ActualHpLoss: actual, Overkill: overkill, HitLanded: true));
            }
            else if (actual > 0)
            {
                var (afterPower, powerEvents) = PowerTriggerProcessor.FireOnDamageReceived(
                    state, reflected.InstanceId, catalog, rng,
                    orderStart: order);
                state = afterPower;
                foreach (var ev in powerEvents) events.Add(ev with { Order = order++ });
            }
        }

        return state;
    }

    private static CombatActor? FindOnHitCaster(
        BattleState state, CombatSource source)
    {
        string? actorInstanceId = source.ActorInstanceId;
        if (actorInstanceId is null
            || (!state.Participants.IsDefaultOrEmpty
                && !BattleParticipantAccess.TryGetByActor(
                    state, actorInstanceId, out _)))
            actorInstanceId = source.OwnerInstanceId;

        if (!state.Participants.IsDefaultOrEmpty)
        {
            if (actorInstanceId is null
                || !BattleParticipantAccess.TryGetByActor(
                    state, actorInstanceId, out _))
                return null;
            return state.Allies.FirstOrDefault(actor =>
                actor.IsAlive && actor.InstanceId == actorInstanceId);
        }

        var legacyHero = ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
        if (legacyHero is null) return null;
        if (actorInstanceId is null
            || actorInstanceId == legacyHero.InstanceId
            || source.OwnerInstanceId == legacyHero.InstanceId)
            return legacyHero;
        return null;
    }

    private static BattleState ResolveTriggeredAttacks(
        BattleState state,
        string? attackerId,
        AttackPoolCounts before,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        if (attackerId is null) return state;

        var attacker = FindAlly(state, attackerId);
        if (attacker is null) return state;
        before.ValidateAgainst(attacker);
        bool hasAddedAttacks = attacker.AttackSingle.AddCount > before.Single
            || attacker.AttackRandom.AddCount > before.Random
            || attacker.AttackAll.AddCount > before.All;
        if (!hasAddedAttacks) return state;

        var resolved = ResolveAddedAttacks(
            state,
            attackerId,
            before.Single,
            before.Random,
            before.All,
            rng,
            catalog,
            order,
            hitIndex,
            deferEnemyDeathTriggers: true);
        state = resolved.State;
        foreach (var battleEvent in resolved.Events)
        {
            events.Add(battleEvent);
            order = System.Math.Max(order, battleEvent.Order + 1);
            if (battleEvent.Kind == BattleEventKind.AttackFire
                && battleEvent.HitIndex is int resolvedHitIndex)
                hitIndex = System.Math.Max(hitIndex, resolvedHitIndex + 1);
        }

        var current = FindAlly(state, attackerId);
        if (current is not null)
        {
            before.ValidateAgainst(current);
            state = ReplaceActor(
                state,
                attackerId,
                current with
                {
                    AttackSingle = current.AttackSingle.TakeFirst(before.Single),
                    AttackRandom = current.AttackRandom.TakeFirst(before.Random),
                    AttackAll = current.AttackAll.TakeFirst(before.All),
                });
        }

        return state;
    }

    private readonly record struct AttackPoolCounts(int Single, int Random, int All)
    {
        public static AttackPoolCounts From(CombatActor? actor) => actor is null
            ? default
            : new AttackPoolCounts(
                actor.AttackSingle.AddCount,
                actor.AttackRandom.AddCount,
                actor.AttackAll.AddCount);

        public void ValidateAgainst(CombatActor actor)
        {
            if (actor.AttackSingle.AddCount < Single
                || actor.AttackRandom.AddCount < Random
                || actor.AttackAll.AddCount < All)
                throw new System.InvalidOperationException(
                    "An OnHit trigger replaced an existing attack pool.");
        }
    }

    private static CombatActor? FindActor(BattleState state, string instanceId)
    {
        foreach (var actor in state.Allies)
            if (actor.InstanceId == instanceId) return actor;
        foreach (var actor in state.Enemies)
            if (actor.InstanceId == instanceId) return actor;
        return null;
    }

    private static BattleState ReplaceActor(BattleState state, string instanceId, CombatActor replacement)
    {
        int allyIndex = FindAllyIndex(state, instanceId);
        if (allyIndex >= 0)
            return state with { Allies = state.Allies.SetItem(allyIndex, replacement) };
        int enemyIndex = FindEnemyIndex(state, instanceId);
        return enemyIndex >= 0
            ? state with { Enemies = state.Enemies.SetItem(enemyIndex, replacement) }
            : state;
    }

    private static List<string> AliveEnemyIds(BattleState state) =>
        state.Enemies
            .Where(enemy => enemy.IsAlive)
            .OrderBy(enemy => enemy.SlotIndex)
            .Select(enemy => enemy.InstanceId)
            .ToList();

    private static string? GetSelectedEnemyId(
        BattleState state,
        string? actorInstanceId = null,
        bool retargetIfUnavailable = false)
    {
        int? selectedIndex = state.TargetEnemyIndex;
        if (actorInstanceId is not null
            && BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant))
            selectedIndex = participant.TargetEnemyIndex;

        if (selectedIndex is { } index
            && index >= 0
            && index < state.Enemies.Length
            && state.Enemies[index].IsAlive)
            return state.Enemies[index].InstanceId;

        return retargetIfUnavailable
            ? state.Enemies
                .Where(enemy => enemy.IsAlive)
                .OrderBy(enemy => enemy.SlotIndex)
                .Select(enemy => enemy.InstanceId)
                .FirstOrDefault()
            : null;
    }

    private static HashSet<string> SnapshotEnemyAliveIds(BattleState state)
    {
        var result = new HashSet<string>();
        foreach (var enemy in state.Enemies)
            if (enemy.IsAlive) result.Add(enemy.InstanceId);
        return result;
    }

    private static BattleState FireOnEnemyDeathForNewlyDead(
        BattleState state,
        HashSet<string> beforeAlive,
        List<BattleEvent> events,
        ref int order,
        DataCatalog catalog,
        IRng rng)
    {
        var newlyDead = state.Enemies
            .Where(enemy => beforeAlive.Contains(enemy.InstanceId) && !enemy.IsAlive)
            .OrderBy(enemy => enemy.SlotIndex)
            .Select(enemy => enemy.InstanceId)
            .ToList();

        foreach (string deadId in newlyDead)
        {
            var (afterRelic, relicEvents) = RelicTriggerProcessor.FireOnEnemyDeath(
                state, deadId, catalog, rng, orderStart: order);
            state = afterRelic;
            foreach (var battleEvent in relicEvents)
                events.Add(battleEvent with { Order = order++ });

            var (afterPower, powerEvents) = PowerTriggerProcessor.FireOnEnemyDeath(
                state, deadId, catalog, rng, orderStart: order);
            state = afterPower;
            foreach (var battleEvent in powerEvents)
                events.Add(battleEvent with { Order = order++ });
        }

        return state;
    }

    private static bool ResolutionShouldStop(BattleState state) =>
        BattleOutcomeResolver.Evaluate(state)
            == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat;

    private static CombatActor? FindAlly(BattleState state, string instanceId)
    {
        foreach (var ally in state.Allies)
            if (ally.InstanceId == instanceId) return ally;
        return null;
    }

    private static int FindAllyIndex(BattleState state, string instanceId)
    {
        for (int i = 0; i < state.Allies.Length; i++)
            if (state.Allies[i].InstanceId == instanceId) return i;
        return -1;
    }

    private static int FindEnemyIndex(BattleState state, string instanceId)
    {
        for (int i = 0; i < state.Enemies.Length; i++)
            if (state.Enemies[i].InstanceId == instanceId) return i;
        return -1;
    }
}
