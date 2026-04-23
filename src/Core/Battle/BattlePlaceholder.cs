using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle;

public static class BattlePlaceholder
{
    public static RunState Start(RunState state, EnemyPool pool, DataCatalog data, IRng rng)
    {
        if (state.ActiveBattle is not null)
            throw new InvalidOperationException("ActiveBattle already present");
        if (state.ActiveReward is not null)
            throw new InvalidOperationException("ActiveReward already present");

        var (queueBefore, selector) = SelectQueue(state, pool);
        var (encounterId, queueAfter) = EncounterQueue.Draw(queueBefore);
        var encounter = data.Encounters[encounterId];

        var enemies = ImmutableArray.CreateBuilder<EnemyInstance>(encounter.EnemyIds.Count);
        foreach (var eid in encounter.EnemyIds)
        {
            var def = data.Enemies[eid];
            int hp = def.HpMin + rng.NextInt(0, def.HpMax - def.HpMin + 1);
            enemies.Add(new EnemyInstance(eid, hp, hp, def.InitialMoveId));
        }
        var battle = new BattleState(encounterId, enemies.ToImmutable(), BattleOutcome.Pending);
        var next = selector(state, queueAfter) with { ActiveBattle = battle };
        return Bestiary.BestiaryTracker.NoteEnemiesEncountered(next, encounter.EnemyIds);
    }

    public static RunState Win(RunState state)
    {
        if (state.ActiveBattle is null)
            throw new InvalidOperationException("No ActiveBattle to win");
        return state with
        {
            ActiveBattle = state.ActiveBattle with { Outcome = BattleOutcome.Victory }
        };
    }

    private static (ImmutableArray<string> queue, Func<RunState, ImmutableArray<string>, RunState> updater)
        SelectQueue(RunState s, EnemyPool pool) => pool.Tier switch
        {
            EnemyTier.Weak => (s.EncounterQueueWeak, (st, q) => st with { EncounterQueueWeak = q }),
            EnemyTier.Strong => (s.EncounterQueueStrong, (st, q) => st with { EncounterQueueStrong = q }),
            EnemyTier.Elite => (s.EncounterQueueElite, (st, q) => st with { EncounterQueueElite = q }),
            EnemyTier.Boss => (s.EncounterQueueBoss, (st, q) => st with { EncounterQueueBoss = q }),
            _ => throw new ArgumentOutOfRangeException(nameof(pool))
        };
}
