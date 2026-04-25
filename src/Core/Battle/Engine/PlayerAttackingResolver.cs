using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// PlayerAttacking フェーズ実行。各 ally の Single→Random→All の順で発射。
/// 10.2.A は ally = 主人公 1 体のみ。10.2.D で召喚を inside-out で含める。
/// 親 spec §4-4 参照。
/// </summary>
internal static class PlayerAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(BattleState state, IRng rng)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        var allies = state.Allies.OrderBy(a => a.SlotIndex).ToList();
        foreach (var ally in allies)
        {
            if (!ally.IsAlive) continue;

            // 1. Single
            if (ally.AttackSingle.Sum > 0 && state.TargetEnemyIndex is { } ti && ti < state.Enemies.Length)
            {
                var target = state.Enemies[ti];
                var (updated, evs, _) = DealDamageHelper.Apply(
                    ally, target, ally.AttackSingle.RawTotal, scopeNote: "single", orderBase: order);
                state = state with { Enemies = state.Enemies.SetItem(ti, updated) };
                events.AddRange(evs);
                order += evs.Count;
            }

            // 2. Random
            if (ally.AttackRandom.Sum > 0 && state.Enemies.Length > 0)
            {
                int idx = rng.NextInt(0, state.Enemies.Length); // 死亡敵含む（spec §4-4 仕様）
                var target = state.Enemies[idx];
                var (updated, evs, _) = DealDamageHelper.Apply(
                    ally, target, ally.AttackRandom.RawTotal, scopeNote: "random", orderBase: order);
                state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
                events.AddRange(evs);
                order += evs.Count;
            }

            // 3. All
            if (ally.AttackAll.Sum > 0)
            {
                for (int i = 0; i < state.Enemies.Length; i++)
                {
                    var target = state.Enemies[i];
                    var (updated, evs, _) = DealDamageHelper.Apply(
                        ally, target, ally.AttackAll.RawTotal, scopeNote: "all", orderBase: order);
                    state = state with { Enemies = state.Enemies.SetItem(i, updated) };
                    events.AddRange(evs);
                    order += evs.Count;
                }
            }
        }

        return (state, events);
    }
}
