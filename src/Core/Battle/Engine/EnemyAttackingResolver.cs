using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
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
        var events = new List<BattleEvent>();
        int order = 0;

        var enemies = state.Enemies.OrderBy(e => e.SlotIndex).ToList();
        foreach (var enemy in enemies)
        {
            if (!enemy.IsAlive) continue;
            if (!catalog.TryGetEnemy(enemy.DefinitionId, out var def)) continue;
            var move = def.Moves.FirstOrDefault(m => m.Id == enemy.CurrentMoveId);
            if (move is null) continue;

            var currentEnemyState = state.Enemies.First(e => e.InstanceId == enemy.InstanceId);

            // Enemy definitions do not have a target-selection policy yet. Choose the
            // lowest living ally slot once at action start and keep it for every Single
            // hit in this move. If that actor dies, later Single hits intentionally miss.
            var singleTargetId = state.Allies
                .Where(a => a.IsAlive)
                .OrderBy(a => a.SlotIndex)
                .Select(a => a.InstanceId)
                .FirstOrDefault();

            // Why: 敵 block は前ターンの move で積んだものを次の player turn の
            // PlayerAttacking で消費する。自分の次の move を実行する直前に
            // ここでリセットすることで、move を 2 回連続実行しても block が
            // 蓄積し続けない仕様。
            {
                var resetBlock = currentEnemyState with { Block = BlockPool.Empty };
                int idxR = -1;
                for (int i = 0; i < state.Enemies.Length; i++)
                    if (state.Enemies[i].InstanceId == currentEnemyState.InstanceId) { idxR = i; break; }
                if (idxR >= 0)
                {
                    state = state with { Enemies = state.Enemies.SetItem(idxR, resetBlock) };
                    currentEnemyState = resetBlock;
                }
            }

            int hitIndex = 0;
            for (int effectIndex = 0; effectIndex < move.Effects.Count; effectIndex++)
            {
                var eff = move.Effects[effectIndex];
                var refreshedAtEffectStart = state.Enemies.FirstOrDefault(
                    e => e.InstanceId == currentEnemyState.InstanceId);
                if (refreshedAtEffectStart is null || !refreshedAtEffectStart.IsAlive) break;
                currentEnemyState = refreshedAtEffectStart;

                var source = new CombatSource(
                    CombatSourceKind.EnemyMove,
                    OwnerSlot: currentEnemyState.SlotIndex,
                    OwnerInstanceId: currentEnemyState.InstanceId,
                    ActorInstanceId: currentEnemyState.InstanceId,
                    DefinitionId: currentEnemyState.DefinitionId,
                    MoveId: move.Id,
                    EffectIndex: effectIndex,
                    Scope: eff.Scope.ToString().ToLowerInvariant());

                if (eff.Action == "attack")
                {
                    // Scopeごとに対象を決定し、InstanceIdベースで最新stateから再取得する。
                    // Randomはattack effectごとに再抽選し、Allだけが生存味方全員に着弾する。
                    // (スナップショット参照の IndexOf はフェーズ 10.2.B 以降のステータス tick で破綻する)
                    var targetIds = ResolveAttackTargetIds(
                        state, eff.Scope, singleTargetId, rng);
                    bool hitResolved = false;
                    bool livingHeroTookDamage = false;

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

                        var (updated, evs, _) = DealDamageHelper.Apply(
                            currentEnemyState, currentAlly,
                            baseAmount: eff.Amount,
                            scopeNote: ScopeNote(eff.Scope), orderBase: order,
                            hitIndex: hitIndex,
                            context: HitContext.Attack(source, hitIndex));
                        state = state with { Allies = state.Allies.SetItem(idx, updated) };
                        events.AddRange(evs);
                        order += evs.Count;
                        state = ApplyEnemyAttackReactions(
                            state, currentEnemyState.InstanceId, allyId, evs,
                            events, ref order);
                        hitResolved = true;

                        // 同じ All ヒットの全対象イベントを連続させるため、
                        // OnDamageReceived は対象ループ後に発火する。
                        bool dealt = evs.Any(e =>
                            e.Kind == BattleEventKind.DealDamage && e.ActualHpLoss > 0);
                        if (dealt && updated.DefinitionId == "hero" && updated.IsAlive)
                            livingHeroTookDamage = true;
                    }

                    if (hitResolved)
                    {
                        hitIndex++;

                        // 10.5.E: HP damage を受けて生存した hero の power を、
                        // 1論理ヒットの全対象着弾後に発火する。完全Blockでは発火しない。
                        if (livingHeroTookDamage)
                        {
                            var (afterPower, evsPower) = PowerTriggerProcessor.FireOnDamageReceived(
                                state, catalog, rng, orderStart: order);
                            state = afterPower;
                            foreach (var ev in evsPower)
                                events.Add(ev with { Order = order++ });

                        }

                        var refreshedEnemy = state.Enemies.FirstOrDefault(
                            e => e.InstanceId == currentEnemyState.InstanceId);
                        if (refreshedEnemy is null || !refreshedEnemy.IsAlive)
                        {
                            var (afterDeath, deathEvents) = RelicTriggerProcessor.FireOnEnemyDeath(
                                state, currentEnemyState.InstanceId, catalog, rng, orderStart: order);
                            state = afterDeath;
                            foreach (var deathEvent in deathEvents)
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

            // NextMoveId へ遷移（InstanceId ベースで検索して IndexOf を回避）
            int enemyIdx = -1;
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == currentEnemyState.InstanceId) { enemyIdx = i; break; }
            if (enemyIdx >= 0)
            {
                var transitioned = state.Enemies[enemyIdx] with { CurrentMoveId = move.NextMoveId };
                state = state with { Enemies = state.Enemies.SetItem(enemyIdx, transitioned) };
            }
        }

        // 10.2.D: 死亡 summon のクリーンアップ
        state = SummonCleanup.Apply(state, events, ref order);

        return (state, events);
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
            if (!updated.IsAlive)
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
