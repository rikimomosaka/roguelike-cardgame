using System;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Rest;

public static class RestActions
{
    public static RunState Heal(RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!s.ActiveRestPending)
            throw new InvalidOperationException("Rest is not pending");
        if (s.ActiveRestCompleted)
            throw new InvalidOperationException("Rest already completed");

        int baseAmount = (int)Math.Ceiling(s.MaxHp * 0.30);
        int total = NonBattleRelicEffects.ApplyPassiveRestHealBonus(baseAmount, s, catalog);
        int newHp = Math.Min(s.MaxHp, s.CurrentHp + total);
        return s with { CurrentHp = newHp, ActiveRestCompleted = true };
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
}
