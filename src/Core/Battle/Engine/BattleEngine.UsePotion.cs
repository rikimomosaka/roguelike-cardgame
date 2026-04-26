using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// 戦闘内でポーションを使用する。第 6 公開 API。
    /// Phase=PlayerInput 限定、cost なし、コンボ更新なし、捨札移動なし。
    /// effects は EffectApplier.Apply で順次適用、消費スロットは空文字に置換。
    /// 親 spec §7-3 / §8-1 / 10.2.E spec §4 参照。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) UsePotion(
        BattleState state,
        int potionIndex,
        int? targetEnemyIndex,
        int? targetAllyIndex,
        IRng rng,
        DataCatalog catalog)
    {
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException(
                $"UsePotion requires Phase=PlayerInput, got {state.Phase}");

        if (potionIndex < 0 || potionIndex >= state.Potions.Length)
            throw new InvalidOperationException(
                $"potionIndex {potionIndex} out of range [0, {state.Potions.Length})");

        var potionId = state.Potions[potionIndex];
        if (potionId == "")
            throw new InvalidOperationException($"potion slot {potionIndex} is empty");

        if (!catalog.TryGetPotion(potionId, out var def))
            throw new InvalidOperationException($"potion '{potionId}' not in catalog");

        var caster = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
        if (caster is null || !caster.IsAlive)
            throw new InvalidOperationException("hero not available");

        var s = state with
        {
            TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
            TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
        };

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.UsePotion, Order: 0,
                CasterInstanceId: caster.InstanceId,
                CardId: def.Id,
                Amount: potionIndex),
        };
        int order = 1;

        foreach (var eff in def.Effects)
        {
            var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
            s = afterEff;
            foreach (var ev in evs) { events.Add(ev with { Order = order++ }); }
            caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
            if (caster is null || !caster.IsAlive) break;
        }

        s = s with { Potions = s.Potions.SetItem(potionIndex, "") };

        return (s, events);
    }
}
