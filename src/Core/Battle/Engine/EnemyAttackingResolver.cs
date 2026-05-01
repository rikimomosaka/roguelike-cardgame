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

            // Why: 敵 block は前ターンの move で積んだものを次の player turn の
            // PlayerAttacking で消費する。自分の次の move を実行する直前に
            // ここでリセットすることで、move を 2 回連続実行しても block が
            // 蓄積し続けない仕様。
            {
                var resetBlock = currentEnemyState with { Block = BlockPool.Empty };
                int idxR = -1;
                for (int i = 0; i < state.Enemies.Length; i++)
                    if (state.Enemies[i].InstanceId == currentEnemyState.InstanceId) { idxR = i; break; }
                if (idxR >= 0)
                {
                    state = state with { Enemies = state.Enemies.SetItem(idxR, resetBlock) };
                    currentEnemyState = resetBlock;
                }
            }

            foreach (var eff in move.Effects)
            {
                if (eff.Action == "attack")
                {
                    // 敵 attack は scope=all 直書き運用。生存味方全員に着弾
                    // InstanceId ベースで検索することで、per-effect 状態変更後も正しくインデックスを取得できる
                    // (スナップショット参照の IndexOf はフェーズ 10.2.B 以降のステータス tick で破綻する)
                    var allyIdsAtStart = state.Allies
                        .Where(a => a.IsAlive)
                        .OrderBy(a => a.SlotIndex)
                        .Select(a => a.InstanceId)
                        .ToList();

                    foreach (var allyId in allyIdsAtStart)
                    {
                        // 最新の state からこの ally を再フェッチ
                        int idx = -1;
                        for (int i = 0; i < state.Allies.Length; i++)
                        {
                            if (state.Allies[i].InstanceId == allyId) { idx = i; break; }
                        }
                        if (idx < 0) continue; // ally が削除された（10.2.A では起きないが防御的）
                        var currentAlly = state.Allies[idx];
                        if (!currentAlly.IsAlive) continue; // 前の effect で既に死亡

                        var (updated, evs, _) = DealDamageHelper.Apply(
                            currentEnemyState, currentAlly,
                            baseSum: eff.Amount, addCount: 1,
                            scopeNote: "enemy_attack", orderBase: order);
                        state = state with { Allies = state.Allies.SetItem(idx, updated) };
                        events.AddRange(evs);
                        order += evs.Count;

                        // 10.5.E: hero に damage が入った直後に OnDamageReceived power fire
                        //   - hero (DefinitionId=="hero") のみ対象 (現状 power は hero 専用)
                        //   - damage 0 (block で全吸収) の時は発火しない
                        //   - hero 死亡時は PowerTriggerProcessor 内で skip (caster 不在)
                        bool dealt = evs.Any(e =>
                            e.Kind == BattleEventKind.DealDamage && e.Amount > 0);
                        if (dealt && updated.DefinitionId == "hero" && updated.IsAlive)
                        {
                            var (afterPower, evsPower) = PowerTriggerProcessor.FireOnDamageReceived(
                                state, catalog, rng, orderStart: order);
                            state = afterPower;
                            foreach (var ev in evsPower) { events.Add(ev with { Order = order++ }); }
                            // currentEnemyState は変動しないので再 fetch 不要
                        }
                    }
                }
                else if (eff.Action == "block")
                {
                    // 敵 move の block effect は scope=self を前提（10.2.A の制約）
                    var newEnemy = currentEnemyState with { Block = currentEnemyState.Block.Add(eff.Amount) };
                    int idx = -1;
                    for (int i = 0; i < state.Enemies.Length; i++)
                        if (state.Enemies[i].InstanceId == currentEnemyState.InstanceId) { idx = i; break; }
                    if (idx < 0) continue;
                    state = state with { Enemies = state.Enemies.SetItem(idx, newEnemy) };
                    events.Add(new BattleEvent(
                        BattleEventKind.GainBlock, Order: order,
                        CasterInstanceId: currentEnemyState.InstanceId,
                        TargetInstanceId: currentEnemyState.InstanceId,
                        Amount: eff.Amount));
                    order += 1;
                    currentEnemyState = newEnemy;
                }
                else
                {
                    // buff / debuff / heal / draw / discard / etc. は EffectApplier に委譲。
                    // EffectApplier.ResolveTargets は caster 視点で side を解釈するので、
                    // 敵 move の "side: enemy" は state.Allies に正しく着弾する。
                    var (afterApply, applyEvents) = EffectApplier.Apply(
                        state, currentEnemyState, eff, rng, catalog);
                    state = afterApply;
                    foreach (var ev in applyEvents)
                        events.Add(ev with { Order = order++ });
                    var refreshed = state.Enemies.FirstOrDefault(
                        e => e.InstanceId == currentEnemyState.InstanceId);
                    if (refreshed is null) break;
                    currentEnemyState = refreshed;
                }
            }

            // NextMoveId へ遷移（InstanceId ベースで検索して IndexOf を回避）
            int enemyIdx = -1;
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == currentEnemyState.InstanceId) { enemyIdx = i; break; }
            if (enemyIdx >= 0)
            {
                var transitioned = state.Enemies[enemyIdx] with { CurrentMoveId = move.NextMoveId };
                state = state with { Enemies = state.Enemies.SetItem(enemyIdx, transitioned) };
            }
        }

        // 10.2.D: 死亡 summon のクリーンアップ
        state = SummonCleanup.Apply(state, events, ref order);

        return (state, events);
    }
}
