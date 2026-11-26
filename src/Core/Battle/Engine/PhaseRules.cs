using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 位相（静・動）のルール。docs/current-phase-spec.md 参照。
/// </summary>
public static class PhaseRules
{
    /// <summary>JSON の名前（none / calm / motion）を位相へ変換する。</summary>
    public static ActorPhase Parse(string name) => name switch
    {
        "none" => ActorPhase.None,
        "calm" => ActorPhase.Calm,
        "motion" => ActorPhase.Motion,
        _ => throw new ArgumentException($"unknown phase '{name}', expected none|calm|motion", nameof(name)),
    };

    /// <summary>JSON・イベント用の名前。</summary>
    public static string Name(ActorPhase phase) => phase switch
    {
        ActorPhase.Calm => "calm",
        ActorPhase.Motion => "motion",
        _ => "none",
    };

    /// <summary>反転: 静⇔動。位相なしはそのまま。</summary>
    public static ActorPhase Invert(ActorPhase phase) => phase switch
    {
        ActorPhase.Calm => ActorPhase.Motion,
        ActorPhase.Motion => ActorPhase.Calm,
        _ => ActorPhase.None,
    };

    /// <summary>
    /// カード使用時の判定（効果を実行する前の位相で行う）。
    /// 静でアタックを使う、または動でアタック以外を使うと位相なしに戻る。
    /// </summary>
    public static ActorPhase AfterCardUsed(ActorPhase phase, CardType usedType) => phase switch
    {
        ActorPhase.Calm when usedType == CardType.Attack => ActorPhase.None,
        ActorPhase.Motion when usedType != CardType.Attack => ActorPhase.None,
        _ => phase,
    };

    /// <summary>
    /// キャラの位相を変える。変化したときだけ actor を置き換え、PhaseChanged を記録する。
    /// reason は "effect"（効果）か "used"（使用時の判定）。
    /// </summary>
    public static BattleState SetPhase(
        BattleState state,
        string actorInstanceId,
        ActorPhase next,
        string reason,
        ref int order,
        List<BattleEvent> events)
    {
        for (int i = 0; i < state.Allies.Length; i++)
        {
            var actor = state.Allies[i];
            if (actor.InstanceId != actorInstanceId) continue;
            if (actor.Phase == next) return state;
            events.Add(Changed(actor, next, reason, order++));
            return state with { Allies = state.Allies.SetItem(i, actor with { Phase = next }) };
        }
        for (int i = 0; i < state.Enemies.Length; i++)
        {
            var actor = state.Enemies[i];
            if (actor.InstanceId != actorInstanceId) continue;
            if (actor.Phase == next) return state;
            events.Add(Changed(actor, next, reason, order++));
            return state with { Enemies = state.Enemies.SetItem(i, actor with { Phase = next }) };
        }
        return state;
    }

    /// <summary>
    /// 静のターン終了: 持ち主が静なら、手札のアタック全てに双極を 1 付ける（手札を捨てる前に呼ぶ）。
    /// events が null でなければ CardAugmented を記録する。
    /// </summary>
    public static BattleState ApplyCalmTurnEnd(
        BattleState projected,
        CombatActor? owner,
        RoguelikeCardGame.Core.Data.DataCatalog catalog,
        ref int order,
        List<BattleEvent>? events)
    {
        if (owner is null || owner.Phase != ActorPhase.Calm) return projected;
        var hand = projected.Hand.ToBuilder();
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (!catalog.TryGetCard(card.CardDefinitionId, out var def) || def.CardType != CardType.Attack)
                continue;
            var augmented = card.AddKeyword(CardKeywords.Bipolar, 1);
            hand[i] = augmented;
            events?.Add(new BattleEvent(
                BattleEventKind.CardAugmented, order++,
                CasterInstanceId: owner.InstanceId,
                TargetInstanceId: card.InstanceId,
                Amount: 1,
                Note: $"{CardKeywords.Bipolar}:{augmented.KeywordCount(CardKeywords.Bipolar)}"));
        }
        return projected with { Hand = hand.ToImmutable() };
    }

    private static BattleEvent Changed(CombatActor actor, ActorPhase next, string reason, int order) =>
        new(BattleEventKind.PhaseChanged, order,
            TargetInstanceId: actor.InstanceId,
            Note: $"{Name(actor.Phase)}->{Name(next)}:{reason}");
}
