using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// EnemyAttacking フェーズ実行。各生存敵の MoveDefinition.Effects を per-effect 即時発射し、
/// NextMoveId へ遷移する。親 spec §5-2-1 参照（敵 attack は per-effect 即時発射）。
/// </summary>
internal static class EnemyAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var enemyIds = state.Enemies
            .OrderBy(enemy => enemy.SlotIndex)
            .Select(enemy => enemy.InstanceId)
            .ToArray();
        return ResolveRemaining(state, enemyIds, rng, catalog);
    }

    /// <summary>
    /// Resolves an explicit snapshot of enemy actors.  It is used after a
    /// Contingency choice so actors that already completed their action are
    /// never executed twice and actors created later do not join this phase.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) ResolveRemaining(
        BattleState state,
        IReadOnlyList<string> enemyActorInstanceIds,
        IRng rng,
        DataCatalog catalog,
        bool firstEnemyBlockAlreadyReset = false,
        string? firstEnemySingleTargetActorInstanceId = null)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        for (int enemyCursor = 0;
             enemyCursor < enemyActorInstanceIds.Count;
             enemyCursor++)
        {
            if (ResolutionShouldStop(state)) break;
            string enemyId = enemyActorInstanceIds[enemyCursor];
            var enemy = state.Enemies.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.InstanceId,
                    enemyId,
                    System.StringComparison.Ordinal));
            if (enemy is null) continue;
            if (!enemy.IsAlive) continue;
            if (!catalog.TryGetEnemy(enemy.DefinitionId, out var def)) continue;
            // カード制御の敵は計画時に予約した戦場効果を実行する。後から使ったバフも
            // 攻撃より先に反映されるよう「非攻撃効果（使用順）→ 攻撃（使用順）」に並べる。
            // 旧 Move 敵は従来どおり Move の記載順。
            MoveDefinition? move = null;
            IReadOnlyList<CardEffect> actionEffects;
            IReadOnlyList<CombatSource>? plannedSources = null;
            if (def.IsCardControlled)
            {
                var ordered = enemy.PlannedEffects
                    .Where(planned => planned.Effect.Action != "attack")
                    .Concat(enemy.PlannedEffects
                        .Where(planned => planned.Effect.Action == "attack"))
                    .ToArray();
                actionEffects = ordered.Select(planned => planned.Effect).ToArray();
                plannedSources = ordered.Select(planned => planned.Source).ToArray();
            }
            else
            {
                move = def.Moves.FirstOrDefault(m => m.Id == enemy.CurrentMoveId);
                if (move is null) continue;
                actionEffects = move.Effects;
            }

            var currentEnemyState = state.Enemies.First(e => e.InstanceId == enemy.InstanceId);

            // Enemy definitions do not have a target-selection policy yet. Choose the
            // lowest living ally slot once at action start and keep it for every Single
            // hit in this move. If that actor dies, later Single hits intentionally miss.
            var singleTargetId = enemyCursor == 0 && firstEnemyBlockAlreadyReset
                ? firstEnemySingleTargetActorInstanceId
                : state.Allies
                    .Where(a => a.IsAlive)
                    .OrderBy(a => a.SlotIndex)
                    .Select(a => a.InstanceId)
                    .FirstOrDefault();

            // Why: 敵 block は前ターンの move で積んだものを次の player turn の
            // PlayerAttacking で消費する。自分の次の move を実行する直前に
            // ここでリセットすることで、move を 2 回連続実行しても block が
            // 蓄積し続けない仕様。
            if (!(enemyCursor == 0 && firstEnemyBlockAlreadyReset))
            {
                int carryLimit = System.Math.Max(
                    0, currentEnemyState.GetStatus(BattleStatusIds.Fortify));
                int carried = System.Math.Min(
                    currentEnemyState.Block.Display(
                        currentEnemyState.GetStatus(BattleStatusIds.Dexterity)),
                    carryLimit);
                var resetBlock = currentEnemyState with
                {
                    Block = new BlockPool(carried, 0),
                };
                int idxR = -1;
                for (int i = 0; i < state.Enemies.Length; i++)
                    if (state.Enemies[i].InstanceId == currentEnemyState.InstanceId) { idxR = i; break; }
                if (idxR >= 0)
                {
                    state = state with { Enemies = state.Enemies.SetItem(idxR, resetBlock) };
                    currentEnemyState = resetBlock;
                }
            }

            // Every armed BeforeEnemyAttack Contingency fires once after the
            // enemy's expiring block is reset and before the first hit of this
            // Attack action (including Pursuit). A human choose effect yields
            // before any hit; the reset cursor is persisted for exact resume.
            if (actionEffects.Any(effect => effect.Action == "attack"))
            {
                var contingency =
                    ContingencyResolutionEngine.ResolveBeforeEnemyAttack(
                        state, rng, catalog, order);
                state = contingency.State;
                foreach (var battleEvent in contingency.Events)
                    events.Add(battleEvent with { Order = order++ });
                if (contingency.PausedForChoice)
                {
                    state = state with
                    {
                        PendingTurnResolution = new PendingTurnResolution(
                            TurnResolutionContinuationStage.EnemyAttacking)
                        {
                            RemainingEnemyActorInstanceIds =
                                enemyActorInstanceIds
                                    .Skip(enemyCursor)
                                    .ToImmutableArray(),
                            FirstEnemyBlockAlreadyReset = true,
                            FirstEnemySingleTargetActorInstanceId =
                                singleTargetId,
                        },
                    };
                    return (state, events);
                }

                currentEnemyState = state.Enemies.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.InstanceId,
                        enemy.InstanceId,
                        System.StringComparison.Ordinal));
                if (currentEnemyState is null || !currentEnemyState.IsAlive)
                    continue;
            }

            int hitIndex = 0;
            if (currentEnemyState.GetStatus(BattleStatusIds.Pursuit) > 0
                && actionEffects.Any(effect => effect.Action == "attack"))
            {
                state = ResolveEnemyPursuit(
                    state, currentEnemyState, rng, events,
                    ref order, ref hitIndex);
                if (ResolutionShouldStop(state)) break;
                currentEnemyState = state.Enemies.FirstOrDefault(
                    candidate => candidate.InstanceId == enemy.InstanceId)
                    ?? currentEnemyState;
                if (!currentEnemyState.IsAlive)
                    continue;
            }
            string? announcedCardInstanceId = null;
            for (int effectIndex = 0; effectIndex < actionEffects.Count; effectIndex++)
            {
                if (ResolutionShouldStop(state)) break;
                var eff = actionEffects[effectIndex];
                var refreshedAtEffectStart = state.Enemies.FirstOrDefault(
                    e => e.InstanceId == currentEnemyState.InstanceId);
                if (refreshedAtEffectStart is null || !refreshedAtEffectStart.IsAlive) break;
                currentEnemyState = refreshedAtEffectStart;

                // 使ったカードが切り替わる時にカード名の演出用イベントを出す。
                if (plannedSources is not null)
                {
                    var cardSource = plannedSources[effectIndex];
                    string cardKey = cardSource.InstanceId ?? cardSource.DefinitionId ?? "";
                    if (!string.Equals(cardKey, announcedCardInstanceId, System.StringComparison.Ordinal))
                    {
                        announcedCardInstanceId = cardKey;
                        events.Add(new BattleEvent(
                            BattleEventKind.EnemyCardAction, Order: order++,
                            CasterInstanceId: currentEnemyState.InstanceId,
                            CardId: cardSource.DefinitionId,
                            Source: cardSource));
                    }
                }
                // 能力値参照（攻撃力・守備力）は実行時点の有効値で確定する。
                eff = EffectApplier.ResolveActorStatAtExecution(eff, state, currentEnemyState);

                var effectiveScope = eff.Action == "attack"
                    ? AttackScopeRules.Resolve(currentEnemyState, eff.Scope)
                    : eff.Scope;
                var source = plannedSources is not null
                    ? plannedSources[effectIndex] with
                    {
                        Scope = effectiveScope.ToString().ToLowerInvariant(),
                    }
                    : new CombatSource(
                        CombatSourceKind.EnemyMove,
                        OwnerSlot: currentEnemyState.SlotIndex,
                        OwnerInstanceId: currentEnemyState.InstanceId,
                        ActorInstanceId: currentEnemyState.InstanceId,
                        DefinitionId: currentEnemyState.DefinitionId,
                        MoveId: move!.Id,
                        EffectIndex: effectIndex,
                        Scope: effectiveScope.ToString().ToLowerInvariant());

                if (eff.Action == "attack")
                {
                    // Enemy attacks resolve immediately instead of entering an
                    // AttackPool, but presentation still needs the acquisition
                    // beat. Emit exactly once before any AttackFire event.
                    events.Add(new BattleEvent(
                        BattleEventKind.GainAttack, Order: order++,
                        CasterInstanceId: currentEnemyState.InstanceId,
                        TargetInstanceId: currentEnemyState.InstanceId,
                        Amount: eff.Amount,
                        Note: ScopeNote(effectiveScope),
                        Source: source));

                    // Scopeごとに対象を決定し、InstanceIdベースで最新stateから再取得する。
                    // Randomはattack effectごとに再抽選し、Allだけが生存味方全員に着弾する。
                    // (スナップショット参照の IndexOf はフェーズ 10.2.B 以降のステータス tick で破綻する)
                    var targetIds = ResolveAttackTargetIds(
                        state, effectiveScope, singleTargetId, rng);
                    bool hitResolved = false;
                    var damagedParticipantActorIds = new List<string>();
                    var defensiveStanceRewardedActors =
                        new HashSet<string>(System.StringComparer.Ordinal);

                    foreach (var allyId in targetIds)
                    {
                        // 最新の state からこの ally を再フェッチ
                        int idx = -1;
                        for (int i = 0; i < state.Allies.Length; i++)
                        {
                            if (state.Allies[i].InstanceId == allyId) { idx = i; break; }
                        }
                        if (idx < 0) continue; // ally が削除された（10.2.A では起きないが防御的）
                        var currentAlly = state.Allies[idx];
                        if (!currentAlly.IsAlive) continue; // 前の effect で既に死亡

                        var resolution = GuardianCombatRules.ApplyEnemyAttack(
                            state, currentEnemyState, allyId,
                            baseAmount: eff.Amount,
                            scopeNote: ScopeNote(effectiveScope), orderBase: order,
                            hitIndex: hitIndex,
                            context: HitContext.Attack(source, hitIndex));
                        state = resolution.State;
                        var updated = resolution.UpdatedTarget;
                        var evs = resolution.Events;
                        events.AddRange(evs);
                        order += evs.Count;
                        state = GuardianCombatRules.AwardDefensiveStance(
                            state,
                            resolution.FullyBlockingActorInstanceId,
                            defensiveStanceRewardedActors,
                            hitIndex,
                            events,
                            ref order);
                        state = ApplyEnemyAttackReactions(
                            state, currentEnemyState.InstanceId, allyId, evs,
                            events, ref order);
                        hitResolved = true;
                        if (ResolutionShouldStop(state)) break;

                        // 同じ All ヒットの全対象イベントを連続させるため、
                        // OnDamageReceived は対象ループ後に発火する。
                        bool dealt = evs.Any(e =>
                            e.Kind == BattleEventKind.DealDamage && e.ActualHpLoss > 0);
                        if (dealt && updated.IsAlive)
                            damagedParticipantActorIds.Add(updated.InstanceId);
                    }

                    if (hitResolved)
                    {
                        hitIndex++;

                        // Fire after every target in this logical hit has landed.
                        // Each damaged participant owns an independent power zone.
                        foreach (string damagedActorId in damagedParticipantActorIds)
                        {
                            var (afterPower, evsPower) = PowerTriggerProcessor.FireOnDamageReceived(
                                state, damagedActorId, catalog, rng,
                                orderStart: order);
                            state = afterPower;
                            foreach (var ev in evsPower)
                                events.Add(ev with { Order = order++ });
                        }

                        if (ResolutionShouldStop(state)) break;

                        var refreshedEnemy = state.Enemies.FirstOrDefault(
                            e => e.InstanceId == currentEnemyState.InstanceId);
                        if (refreshedEnemy is null || !refreshedEnemy.IsAlive)
                        {
                            var (afterDeath, deathEvents) = RelicTriggerProcessor.FireOnEnemyDeath(
                                state, currentEnemyState.InstanceId, catalog, rng, orderStart: order);
                            state = afterDeath;
                            foreach (var deathEvent in deathEvents)
                                events.Add(deathEvent with { Order = order++ });

                            var (afterPowerDeath, powerDeathEvents) =
                                PowerTriggerProcessor.FireOnEnemyDeath(
                                    state, currentEnemyState.InstanceId,
                                    catalog, rng, orderStart: order);
                            state = afterPowerDeath;
                            foreach (var deathEvent in powerDeathEvents)
                                events.Add(deathEvent with { Order = order++ });
                            break;
                        }
                        currentEnemyState = refreshedEnemy;
                    }
                }
                else if (eff.Action == "block")
                {
                    // 敵 move の block effect は scope=self を前提（10.2.A の制約）
                    var newEnemy = currentEnemyState with { Block = currentEnemyState.Block.Add(eff.Amount) };
                    int idx = -1;
                    for (int i = 0; i < state.Enemies.Length; i++)
                        if (state.Enemies[i].InstanceId == currentEnemyState.InstanceId) { idx = i; break; }
                    if (idx < 0) continue;
                    state = state with { Enemies = state.Enemies.SetItem(idx, newEnemy) };
                    events.Add(new BattleEvent(
                        BattleEventKind.GainBlock, Order: order,
                        CasterInstanceId: currentEnemyState.InstanceId,
                        TargetInstanceId: currentEnemyState.InstanceId,
                        Amount: eff.Amount,
                        Source: source));
                    order += 1;
                    currentEnemyState = newEnemy;
                }
                else
                {
                    // buff / debuff / heal / draw / discard / etc. は EffectApplier に委譲。
                    // EffectApplier.ResolveTargets は caster 視点で side を解釈するので、
                    // 敵 move の "side: enemy" は state.Allies に正しく着弾する。
                    var (afterApply, applyEvents) = EffectApplier.Apply(
                        state, currentEnemyState, eff, rng, catalog, source);
                    state = afterApply;
                    foreach (var ev in applyEvents)
                        events.Add(ev with
                        {
                            Order = order++,
                            Source = ev.Source ?? source,
                        });
                    var refreshed = state.Enemies.FirstOrDefault(
                        e => e.InstanceId == currentEnemyState.InstanceId);
                    if (refreshed is null) break;
                    currentEnemyState = refreshed;
                }
            }

            if (ResolutionShouldStop(state)) break;

            // NextMoveId へ遷移（InstanceId ベースで検索して IndexOf を回避）
            int enemyIdx = -1;
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == currentEnemyState.InstanceId) { enemyIdx = i; break; }
            if (enemyIdx >= 0)
            {
                // カード制御の敵は予約を消化済みとして空にする（次ターン開始時に再計画）。
                var transitioned = move is not null
                    ? state.Enemies[enemyIdx] with { CurrentMoveId = move.NextMoveId }
                    : state.Enemies[enemyIdx] with
                    {
                        PlannedEffects = ImmutableArray<PlannedCardEffect>.Empty,
                    };
                state = state with { Enemies = state.Enemies.SetItem(enemyIdx, transitioned) };
            }
        }

        // 10.2.D: 死亡 summon のクリーンアップ
        state = SummonCleanup.Apply(state, events, ref order);

        return (state, events);
    }

    private static BattleState ResolveEnemyPursuit(
        BattleState state,
        CombatActor attacker,
        IRng rng,
        List<BattleEvent> events,
        ref int order,
        ref int hitIndex)
    {
        int amount = attacker.GetStatus(BattleStatusIds.Pursuit);
        if (amount <= 0) return state;

        var consumed = BattleStatusRules.Decrement(attacker, BattleStatusIds.Pursuit);
        state = ReplaceActor(state, attacker.InstanceId, consumed);
        var effectiveScope = AttackScopeRules.Resolve(consumed, EffectScope.All);
        var source = new CombatSource(
            CombatSourceKind.Status,
            OwnerSlot: consumed.SlotIndex,
            OwnerInstanceId: consumed.InstanceId,
            ActorInstanceId: consumed.InstanceId,
            DefinitionId: BattleStatusIds.Pursuit,
            Scope: effectiveScope.ToString().ToLowerInvariant());

        events.Add(new BattleEvent(
            BattleEventKind.GainAttack, Order: order++,
            CasterInstanceId: consumed.InstanceId,
            TargetInstanceId: consumed.InstanceId,
            Amount: amount,
            Note: ScopeNote(effectiveScope),
            Source: source));

        var targetIds = ResolveAttackTargetIds(
            state, effectiveScope, singleTargetId: null, rng);
        var defensiveStanceRewardedActors =
            new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string targetId in targetIds)
        {
            if (ResolutionShouldStop(state)) break;
            var currentAttacker = FindActor(state, attacker.InstanceId);
            var target = FindActor(state, targetId);
            if (currentAttacker is null || !currentAttacker.IsAlive
                || target is null || !target.IsAlive)
                continue;

            var resolution = GuardianCombatRules.ApplyEnemyAttack(
                state, currentAttacker, targetId, amount,
                ScopeNote(effectiveScope), order, hitIndex,
                HitContext.Attack(source, hitIndex));
            state = resolution.State;
            var hitEvents = resolution.Events;
            events.AddRange(hitEvents);
            order += hitEvents.Count;
            state = GuardianCombatRules.AwardDefensiveStance(
                state,
                resolution.FullyBlockingActorInstanceId,
                defensiveStanceRewardedActors,
                hitIndex,
                events,
                ref order);
            state = ApplyEnemyAttackReactions(
                state, attacker.InstanceId, targetId, hitEvents,
                events, ref order);
            if (ResolutionShouldStop(state)) break;
        }
        hitIndex++;

        if (amount == 1)
        {
            events.Add(new BattleEvent(
                BattleEventKind.RemoveStatus, Order: order++,
                TargetInstanceId: attacker.InstanceId,
                Note: BattleStatusIds.Pursuit,
                Source: attacker.GetStatusSource(BattleStatusIds.Pursuit)));
        }
        return state;
    }

    private static IReadOnlyList<string> ResolveAttackTargetIds(
        BattleState state, EffectScope scope, string? singleTargetId, IRng rng)
    {
        if (scope == EffectScope.Single)
        {
            return singleTargetId is null
                ? System.Array.Empty<string>()
                : new[] { singleTargetId };
        }

        var livingIds = state.Allies
            .Where(a => a.IsAlive)
            .OrderBy(a => a.SlotIndex)
            .Select(a => a.InstanceId)
            .ToList();

        if (scope == EffectScope.Random)
        {
            if (livingIds.Count == 0) return System.Array.Empty<string>();
            return new[] { livingIds[rng.NextInt(0, livingIds.Count)] };
        }

        return scope == EffectScope.All
            ? livingIds
            : System.Array.Empty<string>();
    }

    private static string ScopeNote(EffectScope scope) => scope switch
    {
        EffectScope.Single => "single",
        EffectScope.Random => "random",
        EffectScope.All => "all",
        _ => "attack",
    };

    private static BattleState ApplyEnemyAttackReactions(
        BattleState state,
        string attackerId,
        string targetId,
        IReadOnlyList<BattleEvent> damageEvents,
        List<BattleEvent> events,
        ref int order)
    {
        var damageEvent = damageEvents.FirstOrDefault(e => e.Kind == BattleEventKind.DealDamage);
        if (damageEvent is null) return state;
        int actualHpLoss = damageEvent.ActualHpLoss ?? 0;
        var attacker = FindActor(state, attackerId);
        var target = FindActor(state, targetId);
        bool hitLanded = damageEvents.Any(
            e => e.Kind == BattleEventKind.AttackFire && e.HitLanded == true);

        int lifesteal = attacker?.GetStatus("lifesteal") ?? 0;
        if (actualHpLoss > 0 && lifesteal > 0 && attacker is not null && attacker.IsAlive)
        {
            int requested = actualHpLoss * lifesteal / 100;
            int healed = System.Math.Min(System.Math.Max(0, requested), attacker.MaxHp - attacker.CurrentHp);
            if (healed > 0)
            {
                state = ReplaceActor(state, attackerId, attacker with { CurrentHp = attacker.CurrentHp + healed });
                events.Add(new BattleEvent(
                    BattleEventKind.Heal, Order: order++,
                    CasterInstanceId: attackerId, TargetInstanceId: attackerId,
                    Amount: healed, Note: "lifesteal", HitIndex: damageEvent.HitIndex,
                    DamageType: DamageType.Attack, Source: damageEvent.Source,
                    CauseChain: damageEvent.CauseChain));
            }
        }

        int reflect = target?.GetStatus("reflect") ?? 0;
        attacker = FindActor(state, attackerId);
        if (hitLanded && reflect > 0 && attacker is not null && attacker.IsAlive)
        {
            int hpBefore = attacker.CurrentHp;
            int actual = System.Math.Min(hpBefore, reflect);
            int overkill = System.Math.Max(0, reflect - hpBefore);
            var updated = attacker with { CurrentHp = System.Math.Max(0, hpBefore - reflect) };
            bool revived = BattleStatusRules.TryRevive(updated, out var afterRevive);
            updated = afterRevive;
            state = ReplaceActor(state, attackerId, updated);
            var reflectSource = new CombatSource(
                CombatSourceKind.Status, target?.SlotIndex ?? 0,
                targetId, targetId, DefinitionId: "reflect");
            var parentChain = damageEvent.CauseChain
                ?? DamageCauseChain.Empty;
            var reflectChain = parentChain.Add(new DamageCauseFrame(
                parentChain.Length, DamageType.Reflected, reflectSource,
                damageEvent.HitIndex, "reflect"));
            events.Add(new BattleEvent(
                BattleEventKind.DealDamage, Order: order++,
                CasterInstanceId: targetId, TargetInstanceId: attackerId,
                Amount: reflect, Note: "reflect", BlockedAmount: 0,
                HitIndex: damageEvent.HitIndex, DamageType: DamageType.Reflected,
                Source: reflectSource, CauseChain: reflectChain,
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
                    Amount: updated.CurrentHp,
                    Note: BattleStatusIds.Revive));
            }
            else if (!updated.IsAlive)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ActorDeath, Order: order++,
                    CasterInstanceId: targetId, TargetInstanceId: attackerId,
                    Note: "reflect", HitIndex: damageEvent.HitIndex,
                    DamageType: DamageType.Reflected, Source: reflectSource,
                    CauseChain: reflectChain, ActualHpLoss: actual,
                    Overkill: overkill, HitLanded: true));
            }
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

    private static bool ResolutionShouldStop(BattleState state) =>
        BattleOutcomeResolver.Evaluate(state)
            == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat;

    private static BattleState ReplaceActor(BattleState state, string instanceId, CombatActor actor)
    {
        for (int i = 0; i < state.Allies.Length; i++)
            if (state.Allies[i].InstanceId == instanceId)
                return state with { Allies = state.Allies.SetItem(i, actor) };
        for (int i = 0; i < state.Enemies.Length; i++)
            if (state.Enemies[i].InstanceId == instanceId)
                return state with { Enemies = state.Enemies.SetItem(i, actor) };
        return state;
    }
}
