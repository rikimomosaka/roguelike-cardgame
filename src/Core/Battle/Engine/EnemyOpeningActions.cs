using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 初期行動（2026-11-28 改訂）。戦闘開始時、ターン開始の処理（特性・有事・レリック・パワー・戦闘開始時のレリック）が
/// すべて終わり、手札が配られた後、プレイヤーが操作を始める直前に使う。順番は「敵の初期行動 → 味方の初期行動」。
/// - 敵: 定義の openingActions のカードを、通常の行動と同じ処理（EnemyAttackingResolver）でその場で実行する。
///   攻撃・ブロック・バフ・デバフなど、どの効果のカードでも使える。敵の 1 ターン目の行動予約はこの後に行う。
/// - 味方（プレイヤーキャラ）: キャラ定義の openingActions のカードの効果を、そのキャラが使ったものとしてその場で実行する。
///   攻撃はほかのカードと同じく予約され、ターン終了時に当たる。
/// どちらもカードはデッキ・手札の外から使い、使った後も捨札・除外のどこにも入らない（コスト・コンボ・使用回数も無関係）。
/// </summary>
internal static class EnemyOpeningActions
{
    public static BattleState Apply(
        BattleState state,
        List<BattleEvent> events,
        ref int order,
        IRng rng,
        DataCatalog catalog)
    {
        var result = ApplyEnemies(state, events, ref order, rng, catalog);
        if (result.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            return result;
        result = ApplyAllies(result, events, ref order, rng, catalog);
        return TargetingAutoSwitch.Apply(result);
    }

    private static BattleState ApplyEnemies(
        BattleState state, List<BattleEvent> events, ref int order, IRng rng, DataCatalog catalog)
    {
        var result = state;
        var enemyIds = result.Enemies.OrderBy(e => e.SlotIndex).Select(e => e.InstanceId).ToArray();
        foreach (var enemyId in enemyIds)
        {
            var enemy = result.Enemies.FirstOrDefault(e => e.InstanceId == enemyId);
            if (enemy is null || !enemy.IsAlive) continue;
            if (!catalog.TryGetEnemy(enemy.DefinitionId, out var def)) continue;
            if (def.EffectiveOpeningActions.Count == 0) continue;

            // 初期行動のカードを 1 枚ずつ、その敵の予約として置き、通常の敵の行動と同じ処理で実行する。
            // 実行の処理は予約を空にするので、本来の予約（1 ターン目の行動）はこの後で作る。
            int ordinal = 0;
            foreach (var cardId in def.EffectiveOpeningActions)
            {
                if (!catalog.TryGetCard(cardId, out var card)) continue;
                var caster = result.Enemies.FirstOrDefault(e => e.InstanceId == enemyId);
                if (caster is null || !caster.IsAlive) break;
                string instanceId = $"opening:{enemyId}:{ordinal++}";
                var planned = card.Effects.Select((effect, effectIndex) => new PlannedCardEffect(effect, new CombatSource(
                    CombatSourceKind.CardEffect,
                    OwnerSlot: caster.SlotIndex,
                    OwnerInstanceId: caster.InstanceId,
                    ActorInstanceId: caster.InstanceId,
                    DefinitionId: card.Id,
                    InstanceId: instanceId,
                    EffectIndex: effectIndex,
                    Scope: effect.Scope.ToString().ToLowerInvariant()))).ToImmutableArray();
                events.Add(OpeningEvent(caster.InstanceId, card, instanceId, ref order));
                int index = result.Enemies.IndexOf(caster);
                result = result with { Enemies = result.Enemies.SetItem(index, caster with { PlannedEffects = planned }) };
                var (after, actionEvents) = EnemyAttackingResolver.ResolveRemaining(
                    result, new[] { enemyId }, rng, catalog,
                    // 1 ターン目の初期行動の前に、ブロックを消す処理は要らない（まだ何も持っていない）。
                    firstEnemyBlockAlreadyReset: true,
                    firstEnemySingleTargetActorInstanceId: result.Allies
                        .Where(a => a.IsAlive).OrderBy(a => a.SlotIndex)
                        .Select(a => a.InstanceId).FirstOrDefault());
                result = after;
                foreach (var ev in actionEvents)
                    events.Add(ev with { Order = order++ });
                if (result.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending) break;
            }
            var (afterOutcome, outcomeEvents) = BattleOutcomeResolver.ApplyIfTerminal(result, order);
            result = afterOutcome;
            foreach (var ev in outcomeEvents)
                events.Add(ev with { Order = order++ });
            if (result.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
                return result;
        }
        return result;
    }

    private static BattleState ApplyAllies(
        BattleState state, List<BattleEvent> events, ref int order, IRng rng, DataCatalog catalog)
    {
        var result = state;
        foreach (var participant in BattleParticipantAccess.EffectiveParticipants(result).ToArray())
        {
            if (!catalog.Characters.TryGetValue(participant.CharacterId, out var character)) continue;
            if (character.EffectiveOpeningActions.Count == 0) continue;
            string actorId = participant.ActorInstanceId;
            foreach (var cardId in character.EffectiveOpeningActions)
            {
                if (!catalog.TryGetCard(cardId, out var card)) continue;
                var caster = result.Allies.FirstOrDefault(a => a.InstanceId == actorId);
                if (caster is null || !caster.IsAlive) break;
                events.Add(OpeningEvent(actorId, card, $"opening:{actorId}:{card.Id}", ref order));
                for (int effectIndex = 0; effectIndex < card.Effects.Count; effectIndex++)
                {
                    caster = result.Allies.FirstOrDefault(a => a.InstanceId == actorId);
                    if (caster is null || !caster.IsAlive) break;
                    var source = new CombatSource(
                        CombatSourceKind.CardEffect,
                        OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
                        OwnerInstanceId: caster.InstanceId,
                        ActorInstanceId: caster.InstanceId,
                        DefinitionId: card.Id,
                        EffectIndex: effectIndex);
                    var effect = EffectApplier.ResolveActorStatAtExecution(card.Effects[effectIndex], result, caster);
                    var (after, applied) = EffectApplier.Apply(result, caster, effect, rng, catalog, source);
                    result = after;
                    foreach (var ev in applied)
                        events.Add(ev with { Order = order++, Source = ev.Source ?? source });
                }
            }
        }
        return result;
    }

    /// <summary>初期行動を使ったことを示すイベント（画面で中央にカードを出すための写し付き）。</summary>
    private static BattleEvent OpeningEvent(string actorId, Cards.CardDefinition card, string instanceId, ref int order) =>
        new(BattleEventKind.OpeningAction, Order: order++,
            CasterInstanceId: actorId,
            CardId: card.Id,
            ReenactedCard: new PlayedCardEffectSnapshot(
                card.Id, instanceId, actorId, card.CardType,
                card.Cost ?? 0, PaidCost: 0, IsUpgraded: false,
                (card.Keywords ?? System.Array.Empty<string>()).ToImmutableArray(),
                card.Effects.ToImmutableArray(),
                PlaySequence: 0)
            {
                PoolClass = card.PoolClass,
            });
}
