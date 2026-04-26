using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 死亡した召喚 actor (Side==Ally && DefinitionId != "hero" && !IsAlive)
/// の AssociatedSummonHeldInstanceId を辿り、対応カードを SummonHeld → DiscardPile に移動する。
/// 親 spec §5-4 / Phase 10.2.D spec §4-4 参照。
///
/// 呼出箇所: PlayerAttackingResolver / EnemyAttackingResolver / TurnStartProcessor (poison tick 後 / Lifetime tick 後)。
/// memory feedback ルール「state.Allies/Enemies 書き戻しは InstanceId 検索」準拠。
/// </summary>
internal static class SummonCleanup
{
    public static BattleState Apply(
        BattleState state, List<BattleEvent> events, ref int order)
    {
        var s = state;
        var deadSummonPairs = s.Allies
            .Where(a => a.Side == ActorSide.Ally
                     && a.DefinitionId != "hero"
                     && !a.IsAlive
                     && a.AssociatedSummonHeldInstanceId is not null)
            .Select(a => (a.InstanceId, a.AssociatedSummonHeldInstanceId!))
            .ToList();

        foreach (var (allyId, cardInstId) in deadSummonPairs)
        {
            int idx = -1;
            for (int i = 0; i < s.SummonHeld.Length; i++)
            {
                if (s.SummonHeld[i].InstanceId == cardInstId) { idx = i; break; }
            }
            if (idx < 0) continue;

            var card = s.SummonHeld[idx];
            s = s with
            {
                SummonHeld = s.SummonHeld.RemoveAt(idx),
                DiscardPile = s.DiscardPile.Add(card),
            };

            // ally の AssociatedSummonHeldInstanceId を null 化（再処理防止）
            int allyIdx = -1;
            for (int i = 0; i < s.Allies.Length; i++)
            {
                if (s.Allies[i].InstanceId == allyId) { allyIdx = i; break; }
            }
            if (allyIdx >= 0)
            {
                var actor = s.Allies[allyIdx];
                s = s with { Allies = s.Allies.SetItem(
                    allyIdx, actor with { AssociatedSummonHeldInstanceId = null }) };
            }
            // event 発火なし（state diff から UI が認識）
        }
        return s;
    }
}
