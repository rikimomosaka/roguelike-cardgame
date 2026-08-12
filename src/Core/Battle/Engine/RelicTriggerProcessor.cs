using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 戦闘内 trigger でレリックの per-effect 発動を処理する internal static helper。
/// 所持順発動 (state.OwnedRelicIds 配列順) + Implemented:false スキップ + caster=hero を集約。
/// 親 spec §8-2 / 10.2.E spec §3 / Phase 10.5.L1.5 unified-triggers 参照。
/// </summary>
/// <remarks>
/// Phase 10.5.L1.5: relic-level Trigger フィールド廃止に伴い per-effect filter に変更。
/// 各 relic の effects[] をループして eff.Trigger == trigger のものだけを EffectApplier で適用する。
/// trigger は文字列 ("OnBattleStart" / "OnTurnStart" / etc.) で渡す。
/// </remarks>
internal static class RelicTriggerProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Fire(
        BattleState state, string trigger,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        return FireInternal(
            state, trigger, deadEnemyInstanceId: null, catalog, rng, orderStart,
            playingCardInstanceId);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnEnemyDeath(
        BattleState state, string deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        return FireInternal(
            state, "OnEnemyDeath", deadEnemyInstanceId, catalog, rng,
            orderStart, playingCardInstanceId);
    }

    /// <summary>
    /// Fires relic OnHit effects for one concrete hit target. ChancePercent is
    /// independently rolled for every target hit, including full Block/Barrier.
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

        foreach (var relicId in s.OwnedRelicIds)
        {
            if (!catalog.TryGetRelic(relicId, out var def) || !def.Implemented) continue;
            for (int effectIndex = 0; effectIndex < def.Effects.Count; effectIndex++)
            {
                var eff = def.Effects[effectIndex];
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
                    CombatSourceKind.RelicEffect, hitContext.Source.OwnerSlot,
                    hitContext.Source.OwnerInstanceId, caster.InstanceId,
                    DefinitionId: relicId, EffectIndex: effectIndex);
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
                        ? $"relic:{relicId};OnHit"
                        : $"{ev.Note};relic:{relicId};OnHit";
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
        BattleState state, string trigger,
        string? deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId)
    {
        var events = new List<BattleEvent>();
        var s = state;
        int order = orderStart;

        var caster = ProvisionalSoloHeroOwnerResolver.Resolve(s)?.Hero;
        if (caster is null || !caster.IsAlive) return (s, events);

        foreach (var relicId in s.OwnedRelicIds)
        {
            if (!catalog.TryGetRelic(relicId, out var def)) continue;
            if (!def.Implemented) continue;

            // Phase 10.5.L1.5: per-effect Trigger フィルタ (relic-level Trigger 廃止)
            for (int effectIndex = 0; effectIndex < def.Effects.Count; effectIndex++)
            {
                var eff = def.Effects[effectIndex];
                if (string.IsNullOrEmpty(eff.Trigger)) continue;
                if (eff.Trigger != trigger) continue;

                var source = new CombatSource(
                    CombatSourceKind.RelicEffect,
                    OwnerSlot: caster.SlotIndex,
                    OwnerInstanceId: caster.InstanceId,
                    ActorInstanceId: caster.InstanceId,
                    DefinitionId: relicId,
                    EffectIndex: effectIndex,
                    Scope: eff.Scope.ToString().ToLowerInvariant());
                var (afterEff, evs) = EffectApplier.Apply(
                    s, caster, eff, rng, catalog, source,
                    playingCardInstanceId: playingCardInstanceId);
                s = afterEff;
                foreach (var ev in evs)
                {
                    var basePrefix = $"relic:{relicId}";
                    var suffix = deadEnemyInstanceId is not null
                        ? $";deadEnemy:{deadEnemyInstanceId}"
                        : "";
                    var newNote = string.IsNullOrEmpty(ev.Note)
                        ? basePrefix + suffix
                        : $"{ev.Note};{basePrefix}{suffix}";
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
