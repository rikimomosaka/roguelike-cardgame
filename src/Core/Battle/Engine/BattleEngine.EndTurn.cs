using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (BattleState, IReadOnlyList<BattleEvent>) EndTurn(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException($"EndTurn requires Phase=PlayerInput, got {state.Phase}");

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.EndTurn, Order: 0),
        };
        int order = 1;

        // 1. PlayerAttacking
        var s = state with { Phase = BattlePhase.PlayerAttacking };
        var (afterPA, evsPA) = PlayerAttackingResolver.Resolve(s, rng, catalog);
        s = afterPA;
        AddWithOrder(events, evsPA, ref order);

        // 2. 死亡判定 + 自動切替
        s = TargetingAutoSwitch.Apply(s);
        if (!s.Enemies.Any(e => e.IsAlive))
        {
            return ResolveOutcome(s, RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory, events, ref order);
        }

        // 2.5. 味方側 status countdown (= player turn の終わり)。
        //  weak/vulnerable/poison 等の Decrement 系 status を 1 減らす。
        //  ここで countdown するのは「ターンを終えた側」だけ — 敵が後続の
        //  EnemyAttacking で player に新規 debuff を付与しても、その debuff は
        //  player の次のターンで活きるようになる (旧仕様は TurnStart で全
        //  actor 一斉 countdown だったため即削除されてバグになっていた)。
        var (afterAllyCd, evsAllyCd) = SideStatusCountdown.ApplyForSide(s, ActorSide.Ally, order);
        s = afterAllyCd;
        order += evsAllyCd.Count;
        events.AddRange(evsAllyCd);

        // 3. EnemyAttacking
        s = s with { Phase = BattlePhase.EnemyAttacking };
        var (afterEA, evsEA) = EnemyAttackingResolver.Resolve(s, rng, catalog);
        s = afterEA;
        AddWithOrder(events, evsEA, ref order);

        // 4. 死亡判定 + 自動切替
        s = TargetingAutoSwitch.Apply(s);
        if (!s.Allies.Any(a => a.IsAlive))
        {
            return ResolveOutcome(s, RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat, events, ref order);
        }

        // 4.5. 敵側 status countdown (= enemy turn の終わり)。
        //  player が PlayerAttacking で敵に付与した weak/vulnerable は、敵が
        //  EnemyAttacking で実際に弱体化された後に countdown される (vulnerable=2
        //  ならこの enemy turn で 1 回弱体化を受けて、countdown で 1 → 次の enemy
        //  turn でもう 1 回弱体化を受けて、countdown で 0、合計 2 ターン分)。
        var (afterEnemyCd, evsEnemyCd) = SideStatusCountdown.ApplyForSide(s, ActorSide.Enemy, order);
        s = afterEnemyCd;
        order += evsEnemyCd.Count;
        events.AddRange(evsEnemyCd);

        // 5. ターン終了処理
        var (afterEnd, evsEnd) = TurnEndProcessor.Process(s, rng, catalog);
        s = afterEnd;
        AddWithOrder(events, evsEnd, ref order);

        // 6. ターン開始処理
        var (afterStart, evsStart) = TurnStartProcessor.Process(s, rng, catalog);
        AddWithOrder(events, evsStart, ref order);

        // TurnStart 中に毒死などで Outcome 確定した場合、Phase 上書きをスキップ
        if (afterStart.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            return (afterStart, events);

        s = afterStart with { Phase = BattlePhase.PlayerInput };
        return (s, events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ResolveOutcome(
        BattleState s, RoguelikeCardGame.Core.Battle.State.BattleOutcome outcome,
        List<BattleEvent> events, ref int order)
    {
        s = s with { Phase = BattlePhase.Resolved, Outcome = outcome };
        events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order, Note: outcome.ToString()));
        order++;
        return (s, events);
    }

    private static void AddWithOrder(List<BattleEvent> dest, IReadOnlyList<BattleEvent> src, ref int order)
    {
        foreach (var ev in src)
        {
            dest.Add(ev with { Order = order });
            order++;
        }
    }
}
