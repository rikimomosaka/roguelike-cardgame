using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
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
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        // 現行ソロは SlotIndex 順（主人公→召喚）。マルチでは BattleState に
        // ルーム確定順を追加し、その順へ置き換える（仕様書 §3）。
        var allyIds = state.Allies
            .OrderBy(a => a.SlotIndex)
            .Select(a => a.InstanceId)
            .ToList();

        foreach (string allyId in allyIds)
        {
            var ally = FindAlly(state, allyId);
            if (ally is null || !ally.IsAlive) continue;

            int hitIndex = 0;
            bool isSummon = ally.DefinitionId != "hero" && ally.CurrentMoveId is not null;
            if (isSummon)
            {
                state = ResolveSummonMove(
                    state, ally, events, ref order, ref hitIndex, catalog, rng);
                continue;
            }

            if (ally.GetStatus("omnistrike") > 0)
            {
                state = ResolveOmnistrike(
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
        }

        state = SummonCleanup.Apply(state, events, ref order);
        return (state, events);
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

        for (int effectIndex = 0; effectIndex < move.Effects.Count; effectIndex++)
        {
            var effect = move.Effects[effectIndex];
            // An earlier effect may kill an enemy and fire OnEnemyDeath, which can
            // mutate this summon. Always continue from that latest actor snapshot.
            var refreshed = FindAlly(state, current.InstanceId);
            if (refreshed is null || !refreshed.IsAlive) break;
            current = refreshed;

            var source = new CombatSource(
                CombatSourceKind.SummonMove,
                OwnerSlot: ally.OwnerPlayerSlot ?? owner?.SlotIndex,
                OwnerInstanceId: ally.OwnerActorInstanceId ?? owner?.InstanceId,
                ActorInstanceId: current.InstanceId,
                DefinitionId: current.DefinitionId,
                InstanceId: current.AssociatedSummonHeldInstanceId,
                MoveId: move.Id,
                EffectIndex: effectIndex,
                Scope: effect.Scope.ToString().ToLowerInvariant());

            if (effect.Action == "attack")
            {
                state = effect.Scope switch
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
            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateAllHit(
                state, attacker, hit.Amount, "omnistrike",
                events, ref order, ref hitIndex, catalog, rng, hit.Source,
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
        // 攻撃開始時の最終選択対象へ全Singleヒットを固定する。
        // 途中で死亡しても再照準せず、残りは不発になる。
        string? targetId = GetSelectedEnemyId(state);
        if (targetId is null) return state;

        foreach (var hit in ally.AttackSingle.Hits)
        {
            int targetIndex = FindEnemyIndex(state, targetId);
            if (targetIndex < 0 || !state.Enemies[targetIndex].IsAlive) break;

            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateSingleHit(
                state, attacker, targetId, hit.Amount, "single",
                events, ref order, ref hitIndex, catalog, rng, hit.Source,
                hit.CauseChain);
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
            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateRandomHit(
                state, attacker, hit.Amount, "random", rng,
                events, ref order, ref hitIndex, catalog, hit.Source,
                hit.CauseChain);
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
            var attacker = FindAlly(state, ally.InstanceId);
            if (attacker is null || !attacker.IsAlive) break;
            state = ResolveImmediateAllHit(
                state, attacker, hit.Amount, "all",
                events, ref order, ref hitIndex, catalog, rng, hit.Source,
                hit.CauseChain);
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
        DamageCauseChain? causeChain = null)
    {
        if (targetId is null) return state;
        int targetIndex = FindEnemyIndex(state, targetId);
        if (targetIndex < 0 || !state.Enemies[targetIndex].IsAlive) return state;

        int currentHitIndex = hitIndex++;
        state = DealEnemyHit(
            state, attacker, targetId, amount, scopeNote, currentHitIndex,
            events, ref order, ref hitIndex, catalog, rng, source, causeChain);
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
        DamageCauseChain? causeChain = null)
    {
        var candidates = AliveEnemyIds(state);
        if (candidates.Count == 0) return state;

        int selected = rng.NextInt(0, candidates.Count);
        int currentHitIndex = hitIndex++;
        state = DealEnemyHit(
            state, attacker, candidates[selected], amount, scopeNote, currentHitIndex,
            events, ref order, ref hitIndex, catalog, rng, source, causeChain);
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
        DamageCauseChain? causeChain = null)
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
        }

        return FireOnEnemyDeathForNewlyDead(
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
        DamageCauseChain? causeChain = null)
    {
        int targetIndex = FindEnemyIndex(state, targetId);
        if (targetIndex < 0 || !state.Enemies[targetIndex].IsAlive) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
        state = ApplyEnemyHit(
            state, attacker, targetId, amount, scopeNote, resolvedHitIndex,
            events, ref order, ref nextHitIndex, catalog, rng, source, causeChain);

        return FireOnEnemyDeathForNewlyDead(
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
            var relicCaster = FindOnHitCaster(state);
            int relicSingleCount = relicCaster?.AttackSingle.AddCount ?? 0;
            var (afterRelicOnHit, relicOnHitEvents) = RelicTriggerProcessor.FireOnHit(
                state, targetId, context, catalog, rng, orderStart: order);
            state = afterRelicOnHit;
            foreach (var ev in relicOnHitEvents) events.Add(ev with { Order = order++ });
            var relicHits = TakeAppendedSingleHits(
                ref state, relicCaster?.InstanceId, relicSingleCount);
            state = ResolveTriggeredSingleHits(
                state, relicCaster?.InstanceId, targetId, relicHits,
                events, ref order, ref hitIndex, catalog, rng);

            var powerCaster = FindOnHitCaster(state);
            int powerSingleCount = powerCaster?.AttackSingle.AddCount ?? 0;
            var (afterOnHit, onHitEvents) = PowerTriggerProcessor.FireOnHit(
                state, targetId, context, catalog, rng, orderStart: order);
            state = afterOnHit;
            foreach (var ev in onHitEvents) events.Add(ev with { Order = order++ });
            var powerHits = TakeAppendedSingleHits(
                ref state, powerCaster?.InstanceId, powerSingleCount);
            state = ResolveTriggeredSingleHits(
                state, powerCaster?.InstanceId, targetId, powerHits,
                events, ref order, ref hitIndex, catalog, rng);
        }

        // OnHit effects may mutate either actor; reactions continue from latest state.
        attacker = FindActor(state, attackerId);
        target = FindActor(state, targetId);

        // Lifesteal heals the owning actor from actual HP removed, never overkill.
        string ownerId = context.Source.OwnerInstanceId ?? attackerId;
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
            if (!reflected.IsAlive)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ActorDeath, Order: order++,
                    CasterInstanceId: targetId, TargetInstanceId: attackerId,
                    Note: "reflect", HitIndex: context.HitIndex,
                    DamageType: DamageType.Reflected, Source: reflectSource,
                    CauseChain: reflectContext.CauseChain,
                    ActualHpLoss: actual, Overkill: overkill, HitLanded: true));
            }
            else if (reflected.DefinitionId == "hero" && actual > 0)
            {
                var (afterPower, powerEvents) = PowerTriggerProcessor.FireOnDamageReceived(
                    state, catalog, rng, orderStart: order);
                state = afterPower;
                foreach (var ev in powerEvents) events.Add(ev with { Order = order++ });
            }
        }

        return state;
    }

    private static CombatActor? FindOnHitCaster(BattleState state) =>
        state.Allies.FirstOrDefault(
            actor => actor.DefinitionId == "hero" && actor.IsAlive);

    private static IReadOnlyList<AttackHit> TakeAppendedSingleHits(
        ref BattleState state, string? casterId, int originalCount)
    {
        if (casterId is null) return System.Array.Empty<AttackHit>();

        var caster = FindActor(state, casterId);
        if (caster is null) return System.Array.Empty<AttackHit>();
        if (caster.AttackSingle.AddCount < originalCount)
            throw new System.InvalidOperationException(
                "An OnHit trigger replaced the existing single-attack pool.");

        var appended = caster.AttackSingle.Hits
            .Skip(originalCount)
            .ToArray();
        if (appended.Length == 0) return appended;

        state = ReplaceActor(
            state, casterId,
            caster with { AttackSingle = caster.AttackSingle.TakeFirst(originalCount) });
        return appended;
    }

    private static BattleState ResolveTriggeredSingleHits(
        BattleState state,
        string? attackerId,
        string targetId,
        IReadOnlyList<AttackHit> hits,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex,
        DataCatalog catalog,
        IRng rng)
    {
        if (attackerId is null || hits.Count == 0) return state;

        foreach (var hit in hits)
        {
            var attacker = FindActor(state, attackerId);
            int targetIndex = FindEnemyIndex(state, targetId);
            if (attacker is null || !attacker.IsAlive ||
                targetIndex < 0 || !state.Enemies[targetIndex].IsAlive)
                break;

            int currentHitIndex = hitIndex++;
            // The enclosing root hit owns the OnEnemyDeath scan. Keeping the
            // nested hit inside that scan prevents an OnHit kill from firing
            // OnEnemyDeath twice while preserving normal child-hit reactions.
            state = ApplyEnemyHit(
                state, attacker, targetId, hit.Amount, "single", currentHitIndex,
                events, ref order, ref hitIndex, catalog, rng,
                hit.Source, hit.CauseChain);
        }

        return state;
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

    private static string? GetSelectedEnemyId(BattleState state)
    {
        if (state.TargetEnemyIndex is not { } index ||
            index < 0 || index >= state.Enemies.Length)
            return null;

        return state.Enemies[index].InstanceId;
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
        }

        return state;
    }

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
