using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン開始処理。10.2.B で 毒ダメージ tick / status countdown / 死亡判定で Outcome 確定 を実装。
/// 10.2.E で OnTurnStart レリック発火を追加。
/// 親 spec §4-2 / Phase 10.2.B spec §5 参照。
/// </summary>
internal static class TurnStartProcessor
{
    public const int DrawPerTurn = 5;

    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng, DataCatalog catalog)
    {
        var s = state with { Turn = state.Turn + 1 };
        var events = new List<BattleEvent>();
        int order = 0;

        // Step 2: 毒ダメージ tick（Allies → Enemies、SlotIndex 順、InstanceId 検索で更新）
        s = ApplyPoisonTick(s, events, ref order);

        // 10.2.D: 毒死で召喚も死んだ場合のクリーンアップ（Outcome 確定前に SummonHeld → Discard）
        s = SummonCleanup.Apply(s, events, ref order);

        // Step 3: tick 後の死亡判定 + 自動切替 + Outcome 確定
        s = TargetingAutoSwitch.Apply(s);
        if (!s.Enemies.Any(e => e.IsAlive))
        {
            s = s with
            {
                Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
                Phase = BattlePhase.Resolved,
            };
            events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order++, Note: "Victory"));
            return (s, events);
        }
        if (!s.Allies.Any(a => a.IsAlive))
        {
            s = s with
            {
                Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat,
                Phase = BattlePhase.Resolved,
            };
            events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order++, Note: "Defeat"));
            return (s, events);
        }

        // Step 4: status countdown（Allies → Enemies、SlotIndex 順）
        s = ApplyStatusCountdown(s, events, ref order);

        // Step 5: Lifetime tick（10.2.D）
        s = ApplyLifetimeTick(s, events, ref order);

        // 10.2.D: Lifetime 死亡で召喚カードを Discard へ
        s = SummonCleanup.Apply(s, events, ref order);

        // Step 6-7: Energy reset / Draw
        s = s with { Energy = s.EnergyMax };
        s = DrawHelper.Draw(s, DrawPerTurn, rng, out _);

        // Step 8: OnTurnStart レリック発動 (10.2.E)
        var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
            s, RelicTrigger.OnTurnStart, catalog, rng, orderStart: order);
        s = afterRelic;
        foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

        // Step 9: TurnStart event
        events.Add(new BattleEvent(BattleEventKind.TurnStart, Order: order++, Note: $"turn={s.Turn}"));
        return (s, events);
    }

    private static BattleState ApplyPoisonTick(BattleState state, List<BattleEvent> events, ref int order)
    {
        // Allies と Enemies の InstanceId スナップショットを採る
        var actorIds = state.Allies.OrderBy(a => a.SlotIndex).Select(a => a.InstanceId)
            .Concat(state.Enemies.OrderBy(e => e.SlotIndex).Select(e => e.InstanceId))
            .ToList();

        var s = state;
        foreach (var aid in actorIds)
        {
            CombatActor? actor = FindActor(s, aid);
            if (actor is null || !actor.IsAlive) continue;
            int poison = actor.GetStatus("poison");
            if (poison <= 0) continue;

            bool wasAlive = actor.IsAlive;
            var updated = actor with { CurrentHp = actor.CurrentHp - poison };
            s = ReplaceActor(s, aid, updated);

            events.Add(new BattleEvent(
                BattleEventKind.PoisonTick, Order: order++,
                TargetInstanceId: aid, Amount: poison, Note: "poison"));

            if (wasAlive && !updated.IsAlive)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ActorDeath, Order: order++,
                    TargetInstanceId: aid, Note: "poison"));
            }
        }
        return s;
    }

    private static BattleState ApplyStatusCountdown(BattleState state, List<BattleEvent> events, ref int order)
    {
        // Allies と Enemies の InstanceId スナップショットを採る（SlotIndex 順）
        var actorIds = state.Allies.OrderBy(a => a.SlotIndex).Select(a => a.InstanceId)
            .Concat(state.Enemies.OrderBy(e => e.SlotIndex).Select(e => e.InstanceId))
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

                // 同 actor 内で複数 status を更新するため再 fetch（InstanceId 検索）
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
                    // countdown では ApplyStatus event は発火しない（spec §5-2）
                }
                s = ReplaceActor(s, aid, actor with { Statuses = newStatuses });
            }
        }
        return s;
    }

    private static BattleState ApplyLifetimeTick(
        BattleState state, List<BattleEvent> events, ref int order)
    {
        // Lifetime あり ally の InstanceId スナップショット
        var allyIds = state.Allies
            .Where(a => a.Side == ActorSide.Ally
                     && a.RemainingLifetimeTurns is not null
                     && a.IsAlive)
            .OrderBy(a => a.SlotIndex)
            .Select(a => a.InstanceId)
            .ToList();

        var s = state;
        foreach (var aid in allyIds)
        {
            var actor = FindActor(s, aid);
            if (actor is null || !actor.IsAlive) continue;
            if (actor.RemainingLifetimeTurns is null) continue;

            int newRemaining = actor.RemainingLifetimeTurns.Value - 1;

            if (newRemaining <= 0)
            {
                // 死亡
                var diedActor = actor with
                {
                    RemainingLifetimeTurns = newRemaining,
                    CurrentHp = 0,
                };
                s = ReplaceActor(s, aid, diedActor);
                events.Add(new BattleEvent(
                    BattleEventKind.ActorDeath, Order: order++,
                    TargetInstanceId: aid, Note: "lifetime"));
            }
            else
            {
                s = ReplaceActor(s, aid, actor with { RemainingLifetimeTurns = newRemaining });
            }
        }
        return s;
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
