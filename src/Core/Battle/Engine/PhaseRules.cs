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
    /// <remarks>
    /// 位相を得た（同じ位相を得直した場合も含む）ので、使用時の判定で待っていた「消える」予定は取り消す。
    /// </remarks>
    public static BattleState SetPhase(
        BattleState state,
        string actorInstanceId,
        ActorPhase next,
        string reason,
        ref int order,
        List<BattleEvent> events)
    {
        var actor = Find(state, actorInstanceId);
        if (actor is null) return state;
        if (actor.Phase == next && !actor.PhaseExitPending) return state;
        if (actor.Phase != next)
            events.Add(Changed(actor, next, reason, order++));
        return Replace(state, actor with { Phase = next, PhaseExitPending = false });
    }

    /// <summary>
    /// カード使用時の判定（効果の前）。位相が消える条件なら「消える予定」にするだけで、
    /// 実際に消すのは効果処理が終わってから（<see cref="ResolvePendingExit"/>）。
    /// </summary>
    public static BattleState MarkExitOnUse(BattleState state, string actorInstanceId, CardType usedType)
    {
        var actor = Find(state, actorInstanceId);
        if (actor is null) return state;
        if (AfterCardUsed(actor.Phase, usedType) == actor.Phase) return state;
        return Replace(state, actor with { PhaseExitPending = true });
    }

    /// <summary>効果処理が終わった時点で、まだ消える予定が残っていれば位相をなしにする。</summary>
    public static BattleState ResolvePendingExit(
        BattleState state,
        string actorInstanceId,
        ref int order,
        List<BattleEvent> events)
    {
        var actor = Find(state, actorInstanceId);
        if (actor is null || !actor.PhaseExitPending) return state;
        return SetPhase(state, actorInstanceId, ActorPhase.None, "used", ref order, events);
    }

    private static CombatActor? Find(BattleState state, string actorInstanceId)
    {
        foreach (var actor in state.Allies)
            if (actor.InstanceId == actorInstanceId) return actor;
        foreach (var actor in state.Enemies)
            if (actor.InstanceId == actorInstanceId) return actor;
        return null;
    }

    private static BattleState Replace(BattleState state, CombatActor after)
    {
        for (int i = 0; i < state.Allies.Length; i++)
            if (state.Allies[i].InstanceId == after.InstanceId)
                return state with { Allies = state.Allies.SetItem(i, after) };
        for (int i = 0; i < state.Enemies.Length; i++)
            if (state.Enemies[i].InstanceId == after.InstanceId)
                return state with { Enemies = state.Enemies.SetItem(i, after) };
        return state;
    }

    /// <summary>
    /// 静のターン終了（手札を捨てる前に呼ぶ）。持ち主が静で、特性に「静でターンを終えた時」
    /// （OnCalmTurnEnd）があればその効果を出す（転相拳士シオンの手札の全アタックへの双極など）。
    /// 静の位相そのものはターン終了時に何もしない（2026-11-28 に受けるダメージ軽減へ変更）。
    /// </summary>
    public static BattleState ApplyCalmTurnEnd(
        BattleState projected,
        CombatActor? owner,
        RoguelikeCardGame.Core.Data.DataCatalog catalog,
        RoguelikeCardGame.Core.Random.IRng rng,
        ref int order,
        List<BattleEvent>? events)
    {
        if (owner is null || owner.Phase != ActorPhase.Calm) return projected;
        if (!OwnerHasTrait(projected, catalog, RoguelikeCardGame.Core.Data.CharacterTraitTrigger.OnCalmTurnEnd))
            return projected;
        var (after, traitEvents) = CharacterTraitRuntime.TryTrigger(
            projected, owner.InstanceId,
            RoguelikeCardGame.Core.Data.CharacterTraitTrigger.OnCalmTurnEnd,
            catalog, rng, orderStart: order);
        foreach (var ev in traitEvents)
        {
            var ordered = ev with { Order = order++ };
            events?.Add(ordered);
        }
        return after;
    }

    /// <summary>
    /// 位相が動になった（動から動への上書きも含む）時の特性（OnMotionEntered）を出す。
    /// 対象が参加者のキャラ（主人公・副参加者）の時だけ。
    /// </summary>
    public static BattleState ApplyMotionEntered(
        BattleState state,
        string actorInstanceId,
        RoguelikeCardGame.Core.Data.DataCatalog catalog,
        RoguelikeCardGame.Core.Random.IRng rng,
        ref int order,
        List<BattleEvent> events)
    {
        var actor = Find(state, actorInstanceId);
        if (actor is null || actor.Phase != ActorPhase.Motion || actor.Side != ActorSide.Ally) return state;
        if (!BattleParticipantAccess.TryGetByActor(state, actorInstanceId, out _)
            && !state.Participants.IsDefaultOrEmpty)
            return state;
        if (!OwnerHasTrait(state, catalog, RoguelikeCardGame.Core.Data.CharacterTraitTrigger.OnMotionEntered))
            return state;
        var (after, traitEvents) = CharacterTraitRuntime.TryTrigger(
            state, actorInstanceId,
            RoguelikeCardGame.Core.Data.CharacterTraitTrigger.OnMotionEntered,
            catalog, rng, orderStart: order);
        foreach (var ev in traitEvents)
            events.Add(ev with { Order = order++ });
        return after;
    }

    /// <summary>投影中の参加者のキャラ（state.CharacterId）が、その発動条件の特性を持つか。</summary>
    public static bool OwnerHasTrait(
        BattleState state,
        RoguelikeCardGame.Core.Data.DataCatalog catalog,
        RoguelikeCardGame.Core.Data.CharacterTraitTrigger trigger) =>
        catalog.TryGetCharacter(state.CharacterId, out var character)
        && character.Trait is { } trait
        && trait.HasTrigger(trigger);

    private static BattleEvent Changed(CombatActor actor, ActorPhase next, string reason, int order) =>
        new(BattleEventKind.PhaseChanged, order,
            TargetInstanceId: actor.InstanceId,
            Note: $"{Name(actor.Phase)}->{Name(next)}:{reason}");
}
