using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// Phase 10.5.M2-Choose: PendingCardPlay 状態から resume。
    /// 選択済 instance ids で choose effect を適用、残り effect を続行 (必要なら再 pause)、
    /// 完了したら FinalizeCardPlay (PlayCard と共通) を実行する。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) ResolveCardChoice(
        BattleState state, ImmutableArray<string> selectedInstanceIds,
        IRng rng, DataCatalog catalog)
    {
        var pending = state.PendingCardPlay
            ?? throw new InvalidOperationException(
                "Cannot resolve card choice: no PendingCardPlay set");

        // validate selection (count + candidate membership + no duplicates)
        if (selectedInstanceIds.IsDefault)
            throw new InvalidOperationException("selectedInstanceIds must not be default");
        if (selectedInstanceIds.Length != pending.Choice.Count)
            throw new InvalidOperationException(
                $"Expected {pending.Choice.Count} selections, got {selectedInstanceIds.Length}");
        if (selectedInstanceIds.Distinct().Count() != selectedInstanceIds.Length)
            throw new InvalidOperationException("Duplicate selections not allowed");
        foreach (var id in selectedInstanceIds)
        {
            if (!pending.Choice.CandidateInstanceIds.Contains(id))
                throw new InvalidOperationException(
                    $"Selected '{id}' not in choice candidates");
        }

        // pending 中のカード取得 (まだ Hand に居る前提)
        var card = state.Hand.FirstOrDefault(c => c.InstanceId == pending.CardInstanceId)
            ?? throw new InvalidOperationException(
                $"PendingCardPlay card '{pending.CardInstanceId}' not found in Hand");
        if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
            throw new InvalidOperationException(
                $"PendingCardPlay card def '{card.CardDefinitionId}' not in catalog");

        var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
            ? def.UpgradedEffects
            : def.Effects;
        var pendingEffect = effects[pending.EffectIndex];

        // pending クリア + choose effect 適用
        var s = state with { PendingCardPlay = null };
        var caster = s.Allies[0];
        var events = new List<BattleEvent>();
        int order = 0;

        var (afterChose, evsChose) = EffectApplier.ApplyChoseEffect(
            s, caster, pendingEffect, selectedInstanceIds, rng, catalog);
        s = afterChose;
        foreach (var ev in evsChose) { events.Add(ev with { Order = order++ }); }
        caster = s.Allies[0];

        // 残り effect 続行 (再 pause 可能性あり)
        var (afterEffects, effectEvents, summonSucceeded, _) = ApplyEffectsFrom(
            s, caster, card, effects,
            startIndex: pending.EffectIndex + 1,
            newCombo: s.ComboCount,
            summonSucceededIn: pending.SummonSucceededBefore,
            ref order, rng, catalog);
        s = afterEffects;
        events.AddRange(effectEvents);

        // 再 pause していたら早期返却 (新 PendingCardPlay は ApplyEffectsFrom 内で書かれている)
        if (s.PendingCardPlay is not null)
            return (s, events);

        // FinalizeCardPlay (PlayCard と共通)
        return FinalizeCardPlay(s, card, def, summonSucceeded, events, ref order, rng, catalog);
    }
}
