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
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        return FireInternal(
            state, trigger, comboCount: null, catalog, rng, orderStart,
            playingCardInstanceId);
    }

    /// <summary>
    /// OnCombo 専用エントリ。閾値 (ComboMin) 評価で combo count を渡す。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnCombo(
        BattleState state, int comboCount,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        return FireInternal(
            state, "OnCombo", comboCount, catalog, rng, orderStart,
            playingCardInstanceId);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnDamageReceived(
        BattleState state, DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        return FireInternal(
            state, "OnDamageReceived", comboCount: null, catalog, rng,
            orderStart, playingCardInstanceId);
    }

    /// <summary>
    /// Fires once for one actual target hit. ChancePercent is rolled independently
    /// for every target, including fully Blocked or Barrier-prevented hits.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnHit(
        BattleState state, string targetInstanceId, HitContext hitContext,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        var events = new List<BattleEvent>();
        var s = state;
        int order = orderStart;
        var caster = ProvisionalSoloHeroOwnerResolver.Resolve(s)?.Hero;
        if (caster is null) return (s, events);

        var snapshot = s.PowerCards.ToArray();
        foreach (var card in snapshot)
        {
            if (!catalog.Cards.TryGetValue(card.CardDefinitionId, out var def)) continue;
            var effects = card.IsUpgraded && def.UpgradedEffects is not null
                ? def.UpgradedEffects
                : def.Effects;

            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                var eff = effects[effectIndex];
                if (eff.Trigger != "OnHit") continue;
                int chance = eff.ChancePercent ?? 100;
                if (chance <= 0 || (chance < 100 && rng.NextInt(0, 100) >= chance)) continue;

                int? oldEnemyTarget = s.TargetEnemyIndex;
                int? oldAllyTarget = s.TargetAllyIndex;
                int enemyIndex = FindIndex(s.Enemies, targetInstanceId);
                int allyIndex = FindIndex(s.Allies, targetInstanceId);
                if (enemyIndex >= 0) s = s with { TargetEnemyIndex = enemyIndex };
                if (allyIndex >= 0) s = s with { TargetAllyIndex = allyIndex };

                var source = new CombatSource(
                    CombatSourceKind.PowerEffect, hitContext.Source.OwnerSlot,
                    hitContext.Source.OwnerInstanceId, caster.InstanceId,
                    DefinitionId: card.CardDefinitionId,
                    InstanceId: card.InstanceId,
                    EffectIndex: effectIndex);
                var triggeredContext = hitContext.CreateChild(
                    hitContext.DamageType, source, "OnHit");
                var targetedEffect = eff.Scope == RoguelikeCardGame.Core.Cards.EffectScope.Self
                    ? eff
                    : eff with
                    {
                        Scope = RoguelikeCardGame.Core.Cards.EffectScope.Single,
                        Side = enemyIndex >= 0
                            ? RoguelikeCardGame.Core.Cards.EffectSide.Enemy
                            : RoguelikeCardGame.Core.Cards.EffectSide.Ally,
                    };
                var (afterEff, evs) = EffectApplier.Apply(
                    s, caster, targetedEffect, rng, catalog, source,
                    triggeredContext.CauseChain, playingCardInstanceId);
                s = afterEff with
                {
                    TargetEnemyIndex = oldEnemyTarget,
                    TargetAllyIndex = oldAllyTarget,
                };
                foreach (var ev in evs)
                {
                    var note = string.IsNullOrEmpty(ev.Note)
                        ? $"power:{card.CardDefinitionId};OnHit"
                        : $"{ev.Note};power:{card.CardDefinitionId};OnHit";
                    events.Add(ev with
                    {
                        Order = order++, Note = note, Source = source,
                        CauseChain = triggeredContext.CauseChain,
                    });
                }
                caster = ProvisionalSoloHeroOwnerResolver.Resolve(s)?.Hero;
                if (caster is null) return (s, events);
            }
        }
        return (s, events);
    }

    private static int FindIndex(System.Collections.Immutable.ImmutableArray<CombatActor> actors, string id)
    {
        for (int i = 0; i < actors.Length; i++)
            if (actors[i].InstanceId == id) return i;
        return -1;
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireInternal(
        BattleState state, string trigger, int? comboCount,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId)
    {
        var events = new List<BattleEvent>();
        var s = state;
        int order = orderStart;

        var caster = ProvisionalSoloHeroOwnerResolver.Resolve(s)?.Hero;
        if (caster is null || !caster.IsAlive) return (s, events);

        // Apply 中に PowerCards が変動する可能性に備えてスナップショット
        var snapshot = s.PowerCards.ToArray();
        foreach (var card in snapshot)
        {
            if (!catalog.Cards.TryGetValue(card.CardDefinitionId, out var def)) continue;
            var effects = card.IsUpgraded && def.UpgradedEffects is not null
                ? def.UpgradedEffects
                : def.Effects;

            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                var eff = effects[effectIndex];
                if (string.IsNullOrEmpty(eff.Trigger)) continue;
                if (eff.Trigger != trigger) continue;
                // OnCombo は閾値判定
                if (trigger == "OnCombo")
                {
                    if (comboCount is null) continue;
                    var min = eff.ComboMin ?? 1;
                    if (comboCount.Value < min) continue;
                }

                var source = new CombatSource(
                    CombatSourceKind.PowerEffect,
                    OwnerSlot: caster.SlotIndex,
                    OwnerInstanceId: caster.InstanceId,
                    ActorInstanceId: caster.InstanceId,
                    DefinitionId: card.CardDefinitionId,
                    InstanceId: card.InstanceId,
                    EffectIndex: effectIndex,
                    Scope: eff.Scope.ToString().ToLowerInvariant());
                var (afterEff, evs) = EffectApplier.Apply(
                    s, caster, eff, rng, catalog, source,
                    playingCardInstanceId: playingCardInstanceId);
                s = afterEff;
                foreach (var ev in evs)
                {
                    var basePrefix = $"power:{card.CardDefinitionId}";
                    var newNote = string.IsNullOrEmpty(ev.Note)
                        ? basePrefix
                        : $"{ev.Note};{basePrefix}";
                    events.Add(ev with
                    {
                        Order = order,
                        Note = newNote,
                        Source = ev.Source ?? source,
                    });
                    order++;
                }
                caster = ProvisionalSoloHeroOwnerResolver.Resolve(s)?.Hero;
                if (caster is null || !caster.IsAlive) break;
            }

            if (caster is null || !caster.IsAlive) break;
        }

        return (s, events);
    }
}
