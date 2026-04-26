using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン開始処理。10.2.B で 毒ダメージ tick / status countdown / 死亡判定で Outcome 確定 を実装。
/// 召喚 Lifetime tick / OnTurnStart レリックは後続 phase。
/// 親 spec §4-2 / Phase 10.2.B spec §5 参照。
/// </summary>
internal static class TurnStartProcessor
{
    public const int DrawPerTurn = 5;
    public const int HandCap = 10;

    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng)
    {
        var s = state with { Turn = state.Turn + 1 };
        var events = new List<BattleEvent>();
        int order = 0;

        // Step 2: 毒ダメージ tick（Allies → Enemies、SlotIndex 順、InstanceId 検索で更新）
        s = ApplyPoisonTick(s, events, ref order);

        // Step 3-7（死亡判定 / countdown / Energy / Draw / TurnStart event）は Task 14, 15 以降で追加

        s = s with { Energy = s.EnergyMax };
        s = DrawCards(s, DrawPerTurn, rng);
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

    private static BattleState DrawCards(BattleState state, int count, IRng rng)
    {
        var hand = state.Hand.ToBuilder();
        var draw = state.DrawPile.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();

        for (int i = 0; i < count; i++)
        {
            if (hand.Count >= HandCap) break;
            if (draw.Count == 0)
            {
                if (discard.Count == 0) break;
                ShuffleInto(discard, draw, rng);
                discard.Clear();
            }
            // 山札先頭から取り出す
            var top = draw[0];
            draw.RemoveAt(0);
            hand.Add(top);
        }

        return state with
        {
            Hand = hand.ToImmutable(),
            DrawPile = draw.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
    }

    /// <summary>
    /// Fisher-Yates シャッフル。`source` の中身を `dest` に移しながらランダム順で並べる。
    /// </summary>
    private static void ShuffleInto(
        ImmutableArray<BattleCardInstance>.Builder source,
        ImmutableArray<BattleCardInstance>.Builder dest,
        IRng rng)
    {
        var arr = source.ToArray();
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        foreach (var c in arr) dest.Add(c);
    }
}
