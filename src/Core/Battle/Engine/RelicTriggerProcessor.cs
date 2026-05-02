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
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, trigger, deadEnemyInstanceId: null, catalog, rng, orderStart);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnEnemyDeath(
        BattleState state, string deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, "OnEnemyDeath", deadEnemyInstanceId, catalog, rng, orderStart);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireInternal(
        BattleState state, string trigger,
        string? deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        var events = new List<BattleEvent>();
        var s = state;
        int order = orderStart;

        var caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
        if (caster is null || !caster.IsAlive) return (s, events);

        foreach (var relicId in s.OwnedRelicIds)
        {
            if (!catalog.TryGetRelic(relicId, out var def)) continue;
            if (!def.Implemented) continue;

            // Phase 10.5.L1.5: per-effect Trigger フィルタ (relic-level Trigger 廃止)
            foreach (var eff in def.Effects)
            {
                if (string.IsNullOrEmpty(eff.Trigger)) continue;
                if (eff.Trigger != trigger) continue;

                var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
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
