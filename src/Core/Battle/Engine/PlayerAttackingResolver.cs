using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// PlayerAttacking フェーズ実行。
/// omnistrike バフ持ち ally → Single+Random+All を合算して全敵に発射。
/// それ以外 → Single → Random → All の順で個別発射。
/// 各発射後に新規死亡敵を slot 順に OnEnemyDeath 発火。
/// 親 spec §4-4 / Phase 10.2.B spec §6 / Phase 10.2.E spec §5-5 参照。
/// </summary>
internal static class PlayerAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        // ally を SlotIndex 順で iterate（10.2.A は hero 1 体のみ、10.2.D で召喚を含める）
        var allyIds = state.Allies.OrderBy(a => a.SlotIndex).Select(a => a.InstanceId).ToList();
        foreach (var aid in allyIds)
        {
            var ally = FindAlly(state, aid);
            if (ally is null || !ally.IsAlive) continue;

            bool omni = ally.GetStatus("omnistrike") > 0;
            if (omni)
            {
                state = ResolveOmnistrike(state, ally, events, ref order, catalog, rng);
            }
            else
            {
                state = ResolveSingle(state, ally, events, ref order, catalog, rng);
                state = ResolveRandom(state, ally, rng, events, ref order, catalog);
                state = ResolveAll(state, ally, events, ref order, catalog, rng);
            }
        }

        // 10.2.D: 死亡 summon のクリーンアップ
        state = SummonCleanup.Apply(state, events, ref order);

        return (state, events);
    }

    private static BattleState ResolveOmnistrike(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        var combined = ally.AttackSingle + ally.AttackRandom + ally.AttackAll;
        if (combined.Sum <= 0) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
        var enemyIds = state.Enemies.Select(e => e.InstanceId).ToList();
        foreach (var eid in enemyIds)
        {
            int idx = -1;
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == eid) { idx = i; break; }
            if (idx < 0) continue;
            var current = state.Enemies[idx];

            var (updated, evs, _) = DealDamageHelper.Apply(
                ally, current,
                baseSum: combined.Sum, addCount: combined.AddCount,
                scopeNote: "omnistrike", orderBase: order);
            state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
            events.AddRange(evs);
            order += evs.Count;
        }

        state = FireOnEnemyDeathForNewlyDead(state, beforeAlive, events, ref order, catalog, rng);
        return state;
    }

    private static BattleState ResolveSingle(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        if (ally.AttackSingle.Sum <= 0) return state;
        if (state.TargetEnemyIndex is not { } ti || ti < 0 || ti >= state.Enemies.Length) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
        var (updated, evs, _) = DealDamageHelper.Apply(
            ally, state.Enemies[ti],
            baseSum: ally.AttackSingle.Sum, addCount: ally.AttackSingle.AddCount,
            scopeNote: "single", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(ti, updated) };
        events.AddRange(evs);
        order += evs.Count;

        state = FireOnEnemyDeathForNewlyDead(state, beforeAlive, events, ref order, catalog, rng);
        return state;
    }

    private static BattleState ResolveRandom(
        BattleState state, CombatActor ally, IRng rng, List<BattleEvent> events, ref int order,
        DataCatalog catalog)
    {
        if (ally.AttackRandom.Sum <= 0 || state.Enemies.Length == 0) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
        int idx = rng.NextInt(0, state.Enemies.Length); // 死亡敵含む（spec §4-4）
        var (updated, evs, _) = DealDamageHelper.Apply(
            ally, state.Enemies[idx],
            baseSum: ally.AttackRandom.Sum, addCount: ally.AttackRandom.AddCount,
            scopeNote: "random", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
        events.AddRange(evs);
        order += evs.Count;

        state = FireOnEnemyDeathForNewlyDead(state, beforeAlive, events, ref order, catalog, rng);
        return state;
    }

    private static BattleState ResolveAll(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        if (ally.AttackAll.Sum <= 0) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
        var enemyIds = state.Enemies.Select(e => e.InstanceId).ToList();
        foreach (var eid in enemyIds)
        {
            int idx = -1;
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == eid) { idx = i; break; }
            if (idx < 0) continue;
            var current = state.Enemies[idx];

            var (updated, evs, _) = DealDamageHelper.Apply(
                ally, current,
                baseSum: ally.AttackAll.Sum, addCount: ally.AttackAll.AddCount,
                scopeNote: "all", orderBase: order);
            state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
            events.AddRange(evs);
            order += evs.Count;
        }

        state = FireOnEnemyDeathForNewlyDead(state, beforeAlive, events, ref order, catalog, rng);
        return state;
    }

    private static System.Collections.Generic.HashSet<string> SnapshotEnemyAliveIds(BattleState state)
    {
        var set = new System.Collections.Generic.HashSet<string>();
        foreach (var e in state.Enemies)
            if (e.IsAlive) set.Add(e.InstanceId);
        return set;
    }

    private static BattleState FireOnEnemyDeathForNewlyDead(
        BattleState state, System.Collections.Generic.HashSet<string> beforeAlive,
        List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        var newlyDead = state.Enemies
            .Where(e => beforeAlive.Contains(e.InstanceId) && !e.IsAlive)
            .OrderBy(e => e.SlotIndex)
            .Select(e => e.InstanceId)
            .ToList();

        foreach (var deadId in newlyDead)
        {
            var (afterRelic, evsRelic) = RelicTriggerProcessor.FireOnEnemyDeath(
                state, deadId, catalog, rng, orderStart: order);
            state = afterRelic;
            foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }
        }
        return state;
    }

    private static CombatActor? FindAlly(BattleState state, string instanceId)
    {
        foreach (var a in state.Allies) if (a.InstanceId == instanceId) return a;
        return null;
    }
}
