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
        var (afterPA, evsPA) = PlayerAttackingResolver.Resolve(s, rng);
        s = afterPA;
        AddWithOrder(events, evsPA, ref order);

        // 2. 死亡判定 + 自動切替
        s = TargetingAutoSwitch.Apply(s);
        if (!s.Enemies.Any(e => e.IsAlive))
        {
            return ResolveOutcome(s, RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory, events, ref order);
        }

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

        // 5. ターン終了処理
        var (afterEnd, evsEnd) = TurnEndProcessor.Process(s);
        s = afterEnd;
        AddWithOrder(events, evsEnd, ref order);

        // 6. ターン開始処理
        var (afterStart, evsStart) = TurnStartProcessor.Process(s, rng);
        s = afterStart with { Phase = BattlePhase.PlayerInput };
        AddWithOrder(events, evsStart, ref order);

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
