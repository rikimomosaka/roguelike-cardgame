using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// EnemyAttacking フェーズ実行。各生存敵の MoveDefinition.Effects を per-effect 即時発射し、
/// NextMoveId へ遷移する。親 spec §5-2-1 参照（敵 attack は per-effect 即時発射）。
/// </summary>
internal static class EnemyAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        var enemies = state.Enemies.OrderBy(e => e.SlotIndex).ToList();
        foreach (var enemy in enemies)
        {
            if (!enemy.IsAlive) continue;
            if (!catalog.TryGetEnemy(enemy.DefinitionId, out var def)) continue;
            var move = def.Moves.FirstOrDefault(m => m.Id == enemy.CurrentMoveId);
            if (move is null) continue;

            var currentEnemyState = state.Enemies.First(e => e.InstanceId == enemy.InstanceId);

            foreach (var eff in move.Effects)
            {
                if (eff.Action == "attack")
                {
                    // 敵 attack は scope=all 直書き運用。生存味方全員に着弾
                    foreach (var ally in state.Allies.Where(a => a.IsAlive).OrderBy(a => a.SlotIndex).ToList())
                    {
                        var (updated, evs, _) = DealDamageHelper.Apply(
                            currentEnemyState, ally, eff.Amount, scopeNote: "enemy_attack", orderBase: order);
                        state = state with
                        {
                            Allies = state.Allies.SetItem(state.Allies.IndexOf(ally), updated),
                        };
                        events.AddRange(evs);
                        order += evs.Count;
                    }
                }
                else if (eff.Action == "block")
                {
                    // 敵 move の block effect は scope=self を前提（10.2.A の制約）
                    var newEnemy = currentEnemyState with { Block = currentEnemyState.Block.Add(eff.Amount) };
                    int idx = state.Enemies.IndexOf(currentEnemyState);
                    state = state with { Enemies = state.Enemies.SetItem(idx, newEnemy) };
                    events.Add(new BattleEvent(
                        BattleEventKind.GainBlock, Order: order,
                        CasterInstanceId: currentEnemyState.InstanceId,
                        TargetInstanceId: currentEnemyState.InstanceId,
                        Amount: eff.Amount));
                    order += 1;
                    currentEnemyState = newEnemy;
                }
                // その他の action は 10.2.B 以降で対応 (no-op)
            }

            // NextMoveId へ遷移
            int enemyIdx = state.Enemies.IndexOf(currentEnemyState);
            if (enemyIdx >= 0)
            {
                var transitioned = state.Enemies[enemyIdx] with { CurrentMoveId = move.NextMoveId };
                state = state with { Enemies = state.Enemies.SetItem(enemyIdx, transitioned) };
            }
        }

        return (state, events);
    }
}
