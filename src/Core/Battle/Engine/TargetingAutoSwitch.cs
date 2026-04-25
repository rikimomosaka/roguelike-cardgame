using System.Linq;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 死亡判定後に対象を自動切替するヘルパー。最小スロット生存者へ。
/// 親 spec §7-4 参照。
/// </summary>
internal static class TargetingAutoSwitch
{
    public static BattleState Apply(BattleState state)
    {
        int? newE = state.TargetEnemyIndex;
        if (newE is { } ti)
        {
            if (ti < 0 || ti >= state.Enemies.Length || !state.Enemies[ti].IsAlive)
            {
                newE = state.Enemies
                    .Where(e => e.IsAlive)
                    .OrderBy(e => e.SlotIndex)
                    .Select(e => (int?)e.SlotIndex)
                    .FirstOrDefault();
            }
        }

        int? newA = state.TargetAllyIndex;
        if (newA is { } ai)
        {
            if (ai < 0 || ai >= state.Allies.Length || !state.Allies[ai].IsAlive)
            {
                newA = state.Allies
                    .Where(a => a.IsAlive)
                    .OrderBy(a => a.SlotIndex)
                    .Select(a => (int?)a.SlotIndex)
                    .FirstOrDefault();
            }
        }

        return state with { TargetEnemyIndex = newE, TargetAllyIndex = newA };
    }
}
