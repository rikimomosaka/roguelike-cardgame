using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 「そのターンが終わった側」の actor の Decrement 系 status を 1 ずつ減らす処理。
///
/// 旧仕様: TurnStartProcessor で「全 actor の status を一斉に -1」していたため、
/// 敵が end-of-enemy-turn で player に weak=1 を付与 → 次の player turn 開始
/// 直後に countdown で 0 → 削除 → player は weak の効果を 1 回も受けないバグが
/// あった。
///
/// 新仕様 (Slay the Spire 慣習): countdown は「ターンを終えた側」のみ実行。
///  - PlayerAttacking (= player turn の終わり) の直後 → ALLIES 側を countdown
///  - EnemyAttacking  (= enemy  turn の終わり) の直後 → ENEMIES 側を countdown
///
/// これにより、敵が enemy turn 中に付与した debuff は次の player turn でちゃんと
/// アクティブになる (player turn 中に countdown は走らない、player の status の
/// countdown は player turn の最後にだけ起きるため)。
/// </summary>
internal static class SideStatusCountdown
{
    public static (BattleState, IReadOnlyList<BattleEvent>) ApplyForSide(
        BattleState state, ActorSide side, int orderStart)
    {
        var events = new List<BattleEvent>();
        int order = orderStart;

        // 対象 side の InstanceId スナップショット (SlotIndex 順で決定論的に処理)
        var actorIds = (side == ActorSide.Ally
                ? state.Allies.OrderBy(a => a.SlotIndex)
                : state.Enemies.OrderBy(a => a.SlotIndex))
            .Select(a => a.InstanceId)
            .ToList();

        var s = state;
        foreach (var aid in actorIds)
        {
            CombatActor? actor = FindActor(s, aid);
            if (actor is null) continue;

            // status キー一覧のスナップショットを取り、順次 -1
            foreach (var id in actor.Statuses.Keys.ToList())
            {
                var def = StatusDefinition.Get(id);
                if (def.TickDirection != StatusTickDirection.Decrement)
                    continue;

                // 同 actor 内で複数 status を更新するため再 fetch (InstanceId 検索)
                actor = FindActor(s, aid)!;
                int newAmount = actor.GetStatus(id) - 1;
                ImmutableDictionary<string, int> newStatuses;
                if (newAmount <= 0)
                {
                    newStatuses = actor.Statuses.Remove(id);
                    events.Add(new BattleEvent(
                        BattleEventKind.RemoveStatus, Order: order++,
                        TargetInstanceId: aid, Note: id));
                }
                else
                {
                    newStatuses = actor.Statuses.SetItem(id, newAmount);
                    // countdown では ApplyStatus event は発火しない (旧 spec §5-2 を踏襲)
                }
                s = ReplaceActor(s, aid, actor with { Statuses = newStatuses });
            }
        }

        return (s, events);
    }

    private static CombatActor? FindActor(BattleState state, string instanceId)
    {
        foreach (var a in state.Allies) if (a.InstanceId == instanceId) return a;
        foreach (var e in state.Enemies) if (e.InstanceId == instanceId) return e;
        return null;
    }

    private static BattleState ReplaceActor(BattleState state, string instanceId, CombatActor after)
    {
        if (after.Side == ActorSide.Ally)
        {
            for (int i = 0; i < state.Allies.Length; i++)
                if (state.Allies[i].InstanceId == instanceId)
                    return state with { Allies = state.Allies.SetItem(i, after) };
        }
        else
        {
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == instanceId)
                    return state with { Enemies = state.Enemies.SetItem(i, after) };
        }
        return state;
    }
}
