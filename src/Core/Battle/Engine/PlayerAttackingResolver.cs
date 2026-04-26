using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// PlayerAttacking フェーズ実行。
/// omnistrike バフ持ち ally → Single+Random+All を合算して全敵に発射。
/// それ以外 → Single → Random → All の順で個別発射。
/// 親 spec §4-4 / Phase 10.2.B spec §6 参照。
/// </summary>
internal static class PlayerAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(BattleState state, IRng rng)
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
                state = ResolveOmnistrike(state, ally, events, ref order);
            }
            else
            {
                state = ResolveSingle(state, ally, events, ref order);
                state = ResolveRandom(state, ally, rng, events, ref order);
                state = ResolveAll(state, ally, events, ref order);
            }
        }

        // 10.2.D: 死亡 summon のクリーンアップ
        state = SummonCleanup.Apply(state, events, ref order);

        return (state, events);
    }

    private static BattleState ResolveOmnistrike(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order)
    {
        var combined = ally.AttackSingle + ally.AttackRandom + ally.AttackAll;
        if (combined.Sum <= 0) return state;

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
        return state;
    }

    private static BattleState ResolveSingle(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order)
    {
        if (ally.AttackSingle.Sum <= 0) return state;
        if (state.TargetEnemyIndex is not { } ti || ti < 0 || ti >= state.Enemies.Length) return state;

        var (updated, evs, _) = DealDamageHelper.Apply(
            ally, state.Enemies[ti],
            baseSum: ally.AttackSingle.Sum, addCount: ally.AttackSingle.AddCount,
            scopeNote: "single", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(ti, updated) };
        events.AddRange(evs);
        order += evs.Count;
        return state;
    }

    private static BattleState ResolveRandom(
        BattleState state, CombatActor ally, IRng rng, List<BattleEvent> events, ref int order)
    {
        if (ally.AttackRandom.Sum <= 0 || state.Enemies.Length == 0) return state;

        int idx = rng.NextInt(0, state.Enemies.Length); // 死亡敵含む（spec §4-4）
        var (updated, evs, _) = DealDamageHelper.Apply(
            ally, state.Enemies[idx],
            baseSum: ally.AttackRandom.Sum, addCount: ally.AttackRandom.AddCount,
            scopeNote: "random", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
        events.AddRange(evs);
        order += evs.Count;
        return state;
    }

    private static BattleState ResolveAll(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order)
    {
        if (ally.AttackAll.Sum <= 0) return state;

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
        return state;
    }

    private static CombatActor? FindAlly(BattleState state, string instanceId)
    {
        foreach (var a in state.Allies) if (a.InstanceId == instanceId) return a;
        return null;
    }
}
