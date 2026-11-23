using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 敵の先制行動（登場時効果）。通常デッキ・手札・エナジー・コンボとは独立し、
/// 戦闘開始時に最初の手札を配った直後、他の全処理（特性・有事・レリック・パワー・
/// 敵の行動計画）の前に、敵の Slot 順・定義の記載順で即時解決する。
/// 予約（予測のみ）にはせず、その場で適用する。
/// </summary>
internal static class EnemyOpeningActions
{
    /// <summary>先制行動カードで使える効果（戦闘開始時のバフ・特殊状態付与等。攻撃は不可）。</summary>
    public static bool IsAllowedAction(string action) =>
        action is "buff" or "debuff" or "block" or "heal";

    public static BattleState Apply(
        BattleState state,
        List<BattleEvent> events,
        ref int order,
        IRng rng,
        DataCatalog catalog)
    {
        var result = state;
        var enemyIds = state.Enemies
            .OrderBy(enemy => enemy.SlotIndex)
            .Select(enemy => enemy.InstanceId)
            .ToArray();
        foreach (var enemyId in enemyIds)
        {
            var enemy = result.Enemies.FirstOrDefault(candidate =>
                string.Equals(candidate.InstanceId, enemyId, StringComparison.Ordinal));
            if (enemy is null || !enemy.IsAlive) continue;
            if (!catalog.TryGetEnemy(enemy.DefinitionId, out var def)) continue;

            foreach (var cardId in def.EffectiveOpeningActions)
            {
                if (!catalog.TryGetCard(cardId, out var card)) continue;
                for (int effectIndex = 0; effectIndex < card.Effects.Count; effectIndex++)
                {
                    var caster = result.Enemies.FirstOrDefault(candidate =>
                        string.Equals(candidate.InstanceId, enemyId, StringComparison.Ordinal));
                    if (caster is null || !caster.IsAlive) break;
                    var effect = card.Effects[effectIndex];
                    if (!IsAllowedAction(effect.Action)) continue;

                    var source = new CombatSource(
                        CombatSourceKind.CardEffect,
                        OwnerSlot: caster.SlotIndex,
                        OwnerInstanceId: caster.InstanceId,
                        ActorInstanceId: caster.InstanceId,
                        DefinitionId: card.Id,
                        EffectIndex: effectIndex,
                        Scope: effect.Scope.ToString().ToLowerInvariant());
                    var resolved = EffectApplier.ResolveActorStatAtExecution(
                        effect, result, caster);
                    var (after, applied) = EffectApplier.Apply(
                        result, caster, resolved, rng, catalog, source);
                    result = after;
                    foreach (var ev in applied)
                        events.Add(ev with { Order = order++, Source = ev.Source ?? source });
                }
            }
        }
        return result;
    }
}
