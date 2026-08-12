using System;
using System.Collections.Generic;
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
        RejectWhileCardChoiceIsPending(state, "UsePotion");

        if (potionIndex < 0 || potionIndex >= state.Potions.Length)
            throw new InvalidOperationException(
                $"potionIndex {potionIndex} out of range [0, {state.Potions.Length})");

        var potionId = state.Potions[potionIndex];
        if (potionId == "")
            throw new InvalidOperationException($"potion slot {potionIndex} is empty");

        if (!catalog.TryGetPotion(potionId, out var def))
            throw new InvalidOperationException($"potion '{potionId}' not in catalog");

        var owner = ProvisionalSoloHeroOwnerResolver.Resolve(state);
        if (owner is null)
            throw new InvalidOperationException("hero not available");
        var caster = owner.Hero;

        var s = state with
        {
            TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
            TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
        };

        var useSource = new CombatSource(
            CombatSourceKind.PotionEffect,
            OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
            OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: def.Id);
        var events = new List<BattleEvent>
        {
            new(BattleEventKind.UsePotion, Order: 0,
                CasterInstanceId: caster.InstanceId,
                CardId: def.Id,
                Amount: potionIndex,
                Source: useSource),
        };
        int order = 1;

        for (int effectIndex = 0; effectIndex < def.Effects.Count; effectIndex++)
        {
            var eff = def.Effects[effectIndex];
            var source = useSource with
            {
                EffectIndex = effectIndex,
                Scope = eff.Scope.ToString().ToLowerInvariant(),
            };
            var (afterEff, evs) = EffectApplier.Apply(
                s, caster, eff, rng, catalog, source);
            s = afterEff;
            foreach (var ev in evs)
            {
                events.Add(ev with
                {
                    Order = order++,
                    Source = ev.Source ?? source,
                });
            }
            owner = ProvisionalSoloHeroOwnerResolver.Resolve(s);
            if (owner is null) break;
            caster = owner.Hero;
        }

        s = s with { Potions = s.Potions.SetItem(potionIndex, "") };

        return (s, events);
    }
}
