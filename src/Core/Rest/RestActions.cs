using System;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Rest;

public static class RestActions
{
    public const int HealPercent = 30;

    /// <summary>
    /// 休息を選んだ場合の実結果を、実際の処理と同じレリック補正込みで予測する。
    /// UI はこの結果を表示するだけにし、ゲームルールを重複実装しない。
    /// </summary>
    public static RestHealPreview PreviewHeal(RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!s.ActiveRestPending)
            throw new InvalidOperationException("Rest is not pending");

        return CalculateHeal(s, catalog).Preview;
    }

    public static RunState Heal(RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!s.ActiveRestPending)
            throw new InvalidOperationException("Rest is not pending");
        if (s.ActiveRestCompleted)
            throw new InvalidOperationException("Rest already completed");

        return CalculateHeal(s, catalog).State;
    }

    public static RunState UpgradeCard(RunState s, int deckIndex, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!s.ActiveRestPending)
            throw new InvalidOperationException("Rest is not pending");
        if (s.ActiveRestCompleted)
            throw new InvalidOperationException("Rest already completed");
        if (deckIndex < 0 || deckIndex >= s.Deck.Length)
            throw new ArgumentOutOfRangeException(nameof(deckIndex));

        var card = s.Deck[deckIndex];
        if (!CardUpgrade.CanUpgrade(card, catalog))
            throw new InvalidOperationException(
                $"Card at deck[{deckIndex}] (\"{card.Id}\") cannot be upgraded");

        var upgraded = CardUpgrade.Upgrade(card);
        return s with
        {
            Deck = s.Deck.SetItem(deckIndex, upgraded),
            ActiveRestCompleted = true,
        };
    }

    private static (RunState State, RestHealPreview Preview) CalculateHeal(
        RunState s, DataCatalog catalog)
    {
        int baseAmount = (int)Math.Ceiling(s.MaxHp * (HealPercent / 100.0));
        int total = PassiveModifiers.ApplyPassiveRestHealBonus(baseAmount, s, catalog);
        int passiveBonus = total - baseAmount;
        int newHp = Math.Min(s.MaxHp, Math.Max(0, s.CurrentHp + total));
        var rested = s with { CurrentHp = newHp, ActiveRestCompleted = true };
        var final = NonBattleRelicEffects.ApplyOnRest(rested, catalog);
        return (final, new RestHealPreview(
            s.CurrentHp,
            s.MaxHp,
            HealPercent,
            baseAmount,
            passiveBonus,
            final.CurrentHp,
            final.MaxHp));
    }
}

public sealed record RestHealPreview(
    int CurrentHpBefore,
    int MaxHpBefore,
    int BasePercent,
    int BaseHealAmount,
    int PassiveBonusAmount,
    int CurrentHpAfter,
    int MaxHpAfter);
