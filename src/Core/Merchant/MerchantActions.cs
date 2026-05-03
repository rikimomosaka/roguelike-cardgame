using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Merchant;

public static class MerchantActions
{
    public static RunState BuyCard(RunState s, string cardId, DataCatalog catalog)
    {
        var (inv, offer, idx) = RequireOffer(s, "card", cardId);
        if (s.Gold < offer.Price)
            throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
        if (!catalog.TryGetCard(cardId, out _))
            throw new ArgumentException($"unknown card id \"{cardId}\"", nameof(cardId));
        var soldOffer = offer with { Sold = true };
        // Gold 消費 + Sold フラグを先に適用してから AddCardToDeck 経由でデッキ追加。
        // AddCardToDeck が OnCardAddedToDeck トリガーを持つレリック効果も発火する。
        var s1 = s with
        {
            Gold = s.Gold - offer.Price,
            ActiveMerchant = inv with { Cards = inv.Cards.SetItem(idx, soldOffer) },
        };
        return Run.RunDeckActions.AddCardToDeck(s1, cardId, catalog);
    }

    public static RunState BuyRelic(RunState s, string relicId, DataCatalog catalog)
    {
        var (inv, offer, idx) = RequireOffer(s, "relic", relicId);
        if (s.Gold < offer.Price)
            throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
        if (!catalog.TryGetRelic(relicId, out _))
            throw new ArgumentException($"unknown relic id \"{relicId}\"", nameof(relicId));
        var soldOffer = offer with { Sold = true };
        var s1 = s with
        {
            Gold = s.Gold - offer.Price,
            Relics = s.Relics.Append(relicId).ToList(),
            ActiveMerchant = inv with { Relics = inv.Relics.SetItem(idx, soldOffer) },
        };
        s1 = NonBattleRelicEffects.ApplyOnPickup(s1, relicId, catalog);
        return BestiaryTracker.NoteRelicsAcquired(s1, new[] { relicId });
    }

    public static RunState BuyPotion(RunState s, string potionId, DataCatalog catalog)
    {
        var (inv, offer, idx) = RequireOffer(s, "potion", potionId);
        if (s.Gold < offer.Price)
            throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
        if (!catalog.TryGetPotion(potionId, out _))
            throw new ArgumentException($"unknown potion id \"{potionId}\"", nameof(potionId));
        int slot = -1;
        for (int i = 0; i < s.Potions.Length; i++) if (s.Potions[i] == "") { slot = i; break; }
        if (slot < 0) throw new InvalidOperationException("All potion slots full");
        var soldOffer = offer with { Sold = true };
        var next = s with
        {
            Gold = s.Gold - offer.Price,
            Potions = s.Potions.SetItem(slot, potionId),
            ActiveMerchant = inv with { Potions = inv.Potions.SetItem(idx, soldOffer) },
        };
        return BestiaryTracker.NotePotionsAcquired(next, new[] { potionId });
    }

    public static RunState DiscardCard(RunState s, int deckIndex)
    {
        var inv = RequireInventory(s);
        if (inv.DiscardSlotUsed) throw new InvalidOperationException("Discard slot already used");
        if (s.Gold < inv.DiscardPrice)
            throw new InvalidOperationException($"Not enough gold ({s.Gold} < {inv.DiscardPrice})");
        if (deckIndex < 0 || deckIndex >= s.Deck.Length)
            throw new ArgumentOutOfRangeException(nameof(deckIndex));
        return s with
        {
            Gold = s.Gold - inv.DiscardPrice,
            Deck = s.Deck.RemoveAt(deckIndex),
            ActiveMerchant = inv with { DiscardSlotUsed = true },
            DiscardUsesSoFar = s.DiscardUsesSoFar + 1,
        };
    }

    /// <summary>
    /// 「立ち去る」は副作用なし。次のマスに進むまで商人在庫は保持され、
    /// クライアントは好きなタイミングで再入店できる。
    /// </summary>
    public static RunState Leave(RunState s)
    {
        RequireInventory(s);
        return s;
    }

    private static MerchantInventory RequireInventory(RunState s) =>
        s.ActiveMerchant ?? throw new InvalidOperationException("No active merchant");

    private static (MerchantInventory inv, MerchantOffer offer, int index) RequireOffer(
        RunState s, string kind, string id)
    {
        var inv = RequireInventory(s);
        var list = kind switch
        {
            "card" => inv.Cards,
            "relic" => inv.Relics,
            "potion" => inv.Potions,
            _ => throw new ArgumentException($"unknown kind \"{kind}\"", nameof(kind)),
        };
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].Id != id) continue;
            if (list[i].Sold) throw new InvalidOperationException($"{kind} \"{id}\" already sold");
            return (inv, list[i], i);
        }
        throw new ArgumentException($"{kind} id \"{id}\" not in inventory", nameof(id));
    }
}
