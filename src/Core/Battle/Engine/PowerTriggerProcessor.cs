using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// PowerCards の各 effect を Trigger 値で発火させる純関数群。RelicTriggerProcessor mirror。
/// 親 spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §1-3 Q1/Q4.
/// 10.5.E: OnTurnStart / OnPlayCard / OnDamageReceived / OnCombo の 4 trigger に対応。
/// caster=hero、不在/死亡時 skip。複数 power は state.PowerCards 配列順発火。
/// </summary>
internal static class PowerTriggerProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Fire(
        BattleState state, string trigger,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, trigger, comboCount: null, catalog, rng, orderStart);
    }

    /// <summary>
    /// OnCombo 専用エントリ。閾値 (ComboMin) 評価で combo count を渡す。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnCombo(
        BattleState state, int comboCount,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, "OnCombo", comboCount, catalog, rng, orderStart);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnDamageReceived(
        BattleState state, DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, "OnDamageReceived", comboCount: null, catalog, rng, orderStart);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireInternal(
        BattleState state, string trigger, int? comboCount,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        var events = new List<BattleEvent>();
        var s = state;
        int order = orderStart;

        var caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
        if (caster is null || !caster.IsAlive) return (s, events);

        // Apply 中に PowerCards が変動する可能性に備えてスナップショット
        var snapshot = s.PowerCards.ToArray();
        foreach (var card in snapshot)
        {
            if (!catalog.Cards.TryGetValue(card.CardDefinitionId, out var def)) continue;
            var effects = card.IsUpgraded && def.UpgradedEffects is not null
                ? def.UpgradedEffects
                : def.Effects;

            foreach (var eff in effects)
            {
                if (string.IsNullOrEmpty(eff.Trigger)) continue;
                if (eff.Trigger != trigger) continue;
                // OnCombo は閾値判定
                if (trigger == "OnCombo")
                {
                    if (comboCount is null) continue;
                    var min = eff.ComboMin ?? 1;
                    if (comboCount.Value < min) continue;
                }

                var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
                s = afterEff;
                foreach (var ev in evs)
                {
                    var basePrefix = $"power:{card.CardDefinitionId}";
                    var newNote = string.IsNullOrEmpty(ev.Note)
                        ? basePrefix
                        : $"{ev.Note};{basePrefix}";
                    events.Add(ev with { Order = order, Note = newNote });
                    order++;
                }
                caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
                if (caster is null || !caster.IsAlive) break;
            }

            if (caster is null || !caster.IsAlive) break;
        }

        return (s, events);
    }
}
