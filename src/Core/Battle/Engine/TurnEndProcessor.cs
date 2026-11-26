using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン終了処理。Phase 10.2.C でコンボ状態のリセットを追加。
/// Phase 10.2.D で retainSelf-aware 手札整理 + DataCatalog 引数追加。
/// Phase 10.2.E で OnTurnEnd レリック発火 + IRng 引数追加 (step 3)。
/// 親 spec §4-6 参照。
/// </summary>
internal static class TurnEndProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        // Step 1-2: Block / AttackPool リセット (味方のみ)
        // Why: 敵の block は EnemyAttacking で積んだものを「次のプレイヤーターン
        // (PlayerAttacking) で player の攻撃を軽減する」目的なので、ここで一律
        // クリアすると即時無効化されてしまう。敵 block は EnemyAttackingResolver
        // 内で「自分の move 実行直前」にリセットする (= 次の自分のターンが来る
        // まで保持される)。
        var allies = state.Allies.Select(ResetActor).ToImmutableArray();
        var s = state with { Allies = allies };

        var events = new List<BattleEvent>();
        int order = 0;

        // Step 3: OnTurnEnd レリック発動 (10.2.E / 10.5.L1.5: 文字列 trigger に変更)
        var (afterRelic, evsRelic) =
            RelicTriggerProcessor.FireForAllLivingParticipants(
            s, "OnTurnEnd", catalog, rng, orderStart: order);
        s = afterRelic;
        foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

        var (afterPower, evsPower) =
            PowerTriggerProcessor.FireForAllLivingParticipants(
                s, "OnTurnEnd", catalog, rng, orderStart: order);
        s = afterPower;
        foreach (var ev in evsPower) { events.Add(ev with { Order = order++ }); }

        // Step 4: コンボリセット
        var primaryActor = !s.Participants.IsDefaultOrEmpty
            ? s.Allies.FirstOrDefault(actor =>
                actor.InstanceId == s.Participants[0].ActorInstanceId)
            : ProvisionalSoloHeroOwnerResolver.Resolve(s)?.Hero;
        s = ComboTurnBoundaryRules.ResetAtTurnEnd(s, primaryActor);
        s = BattleCardCostModifierLifecycle.Remove(
            s, CardCostModifierDuration.UntilTurnEnd);

        // Step 4.5: 静の持ち主は手札のアタックに双極を得る（手札を捨てる前）。
        var phaseOwner = primaryActor is null
            ? null
            : s.Allies.FirstOrDefault(actor => actor.InstanceId == primaryActor.InstanceId) ?? primaryActor;
        s = PhaseRules.ApplyCalmTurnEnd(s, phaseOwner, catalog, ref order, events);

        // Step 5: 手札整理 (retainSelf-aware, 10.2.D 既存)
        var keepInHand = ImmutableArray.CreateBuilder<BattleCardInstance>();
        var newDiscard = s.DiscardPile.ToBuilder();
        foreach (var card in s.Hand)
        {
            if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
            {
                newDiscard.Add(card);
                continue;
            }
            if (BattleCardRetention.ShouldRetain(card, def, phaseOwner?.Phase ?? ActorPhase.None))
                keepInHand.Add(card);
            else
                newDiscard.Add(card);
        }

        s = s with
        {
            Hand = keepInHand.ToImmutable(),
            DiscardPile = newDiscard.ToImmutable(),
        };
        s = BattleParticipantTurnLifecycle
            .ResetCpuPlayCountsForAllParticipants(s);

        return (s, events);
    }

    private static CombatActor ResetActor(CombatActor a)
    {
        var carried = GuardianTurnEndRules.ApplyBlockCarry(a);
        return carried with
        {
        AttackSingle = AttackPool.Empty,
        AttackRandom = AttackPool.Empty,
        AttackAll = AttackPool.Empty,
        };
    }

}
