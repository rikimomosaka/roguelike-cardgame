using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Rewards;

public static class RewardApplier
{
    public static RunState ApplyGold(RunState s)
    {
        var r = Require(s);
        if (r.GoldClaimed) throw new InvalidOperationException("Gold already claimed");
        return s with
        {
            Gold = s.Gold + r.Gold,
            ActiveReward = r with { GoldClaimed = true },
        };
    }

    public static RunState ApplyPotion(RunState s)
    {
        var r = Require(s);
        if (r.PotionClaimed) throw new InvalidOperationException("Potion already claimed");
        if (r.PotionId is null) throw new InvalidOperationException("No potion to claim");

        int idx = -1;
        for (int i = 0; i < s.Potions.Length; i++) if (s.Potions[i] == "") { idx = i; break; }
        if (idx < 0) throw new InvalidOperationException("All potion slots are full");

        var newPotions = s.Potions.SetItem(idx, r.PotionId);
        return s with
        {
            Potions = newPotions,
            ActiveReward = r with { PotionClaimed = true },
        };
    }

    public static RunState PickCard(RunState s, string cardId)
    {
        var r = Require(s);
        if (r.CardStatus == CardRewardStatus.Claimed)
            throw new InvalidOperationException("Card already claimed");
        if (!r.CardChoices.Contains(cardId))
            throw new ArgumentException($"cardId \"{cardId}\" is not in CardChoices", nameof(cardId));

        return s with
        {
            Deck = s.Deck.Add(cardId),
            ActiveReward = r with { CardStatus = CardRewardStatus.Claimed },
        };
    }

    public static RunState SkipCard(RunState s)
    {
        var r = Require(s);
        if (r.CardStatus != CardRewardStatus.Pending)
            throw new InvalidOperationException("Card already resolved");
        return s with { ActiveReward = r with { CardStatus = CardRewardStatus.Skipped } };
    }

    public static RunState Proceed(RunState s)
    {
        Require(s);
        return s with { ActiveReward = null };
    }

    public static RunState DiscardPotion(RunState s, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= s.Potions.Length)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (s.Potions[slotIndex] == "")
            throw new ArgumentException("Slot is already empty", nameof(slotIndex));
        return s with { Potions = s.Potions.SetItem(slotIndex, "") };
    }

    private static RewardState Require(RunState s)
        => s.ActiveReward ?? throw new InvalidOperationException("No ActiveReward");
}
