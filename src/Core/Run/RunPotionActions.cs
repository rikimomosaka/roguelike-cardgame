using System;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Run;

/// <summary>戦闘外で使用できるポーションをラン状態へ適用する。</summary>
public static class RunPotionActions
{
    public static RunState UseOutsideBattle(RunState state, int slotIndex, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);

        if (state.Progress != RunProgress.InProgress)
            throw new InvalidOperationException("進行中のランではありません。");
        if (state.ActiveBattle is not null)
            throw new InvalidOperationException("戦闘中は戦闘用のポーション操作を使用してください。");
        if (slotIndex < 0 || slotIndex >= state.Potions.Length)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));

        var potionId = state.Potions[slotIndex];
        if (string.IsNullOrEmpty(potionId))
            throw new InvalidOperationException($"ポーションスロット {slotIndex} は空です。");
        if (!catalog.TryGetPotion(potionId, out var definition))
            throw new InvalidOperationException($"ポーション '{potionId}' が見つかりません。");

        var effects = definition.Effects.Where(effect => !effect.BattleOnly).ToArray();
        if (effects.Length == 0)
            throw new InvalidOperationException($"ポーション '{potionId}' は戦闘外では使用できません。");

        var updated = state;
        foreach (var effect in effects)
            updated = ApplyOutsideBattleEffect(updated, effect, potionId);

        return updated with
        {
            Potions = updated.Potions.SetItem(slotIndex, ""),
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static RunState ApplyOutsideBattleEffect(
        RunState state, CardEffect effect, string potionId)
    {
        // A solo run has one ally (the hero). Ally-targeted Single/Random/All
        // effects therefore resolve to that hero when no battle target exists.
        if (effect.Action == "heal"
            && (effect.Scope == EffectScope.Self || effect.Side == EffectSide.Ally))
        {
            return state with
            {
                CurrentHp = Math.Min(state.MaxHp, state.CurrentHp + Math.Max(0, effect.Amount)),
            };
        }

        throw new InvalidOperationException(
            $"ポーション '{potionId}' の効果 '{effect.Action}' は戦闘外処理に対応していません。");
    }
}
