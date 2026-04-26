using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 戦闘内 4 Trigger（OnBattleStart / OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）の
/// レリック発動を統一的に処理する internal static helper。
/// 所持順発動 (state.OwnedRelicIds 配列順) + Implemented:false スキップ + caster=hero を集約。
/// 親 spec §8-2 / 10.2.E spec §3 参照。
/// </summary>
internal static class RelicTriggerProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Fire(
        BattleState state, RelicTrigger trigger,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, trigger, deadEnemyInstanceId: null, catalog, rng, orderStart);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnEnemyDeath(
        BattleState state, string deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, RelicTrigger.OnEnemyDeath, deadEnemyInstanceId, catalog, rng, orderStart);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireInternal(
        BattleState state, RelicTrigger trigger,
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
            if (def.Trigger != trigger) continue;

            foreach (var eff in def.Effects)
            {
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
