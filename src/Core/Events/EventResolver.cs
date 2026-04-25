using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Events;

public static class EventResolver
{
    public static RunState ApplyChoice(
        RunState s, int choiceIndex, DataCatalog catalog, IRng rng)
    {
        if (s.ActiveEvent is null) throw new InvalidOperationException("No active event");
        var inst = s.ActiveEvent;
        if (inst.ChosenIndex is not null) throw new InvalidOperationException("Event already resolved");
        if (choiceIndex < 0 || choiceIndex >= inst.Choices.Length)
            throw new ArgumentOutOfRangeException(nameof(choiceIndex));
        var choice = inst.Choices[choiceIndex];
        if (choice.Condition is not null && !choice.Condition.IsSatisfied(s))
            throw new InvalidOperationException($"Condition not met for choice {choiceIndex}");

        foreach (var eff in choice.Effects)
            s = Apply(s, eff, catalog, rng);

        return s with { ActiveEvent = inst with { ChosenIndex = choiceIndex } };
    }

    private static RunState Apply(RunState s, EventEffect eff, DataCatalog catalog, IRng rng)
    {
        switch (eff)
        {
            case EventEffect.GainGold gg:
                return s with { Gold = s.Gold + gg.Amount };
            case EventEffect.PayGold pg:
                return s with { Gold = Math.Max(0, s.Gold - pg.Amount) };
            case EventEffect.Heal h:
                return s with { CurrentHp = Math.Min(s.MaxHp, s.CurrentHp + h.Amount) };
            case EventEffect.TakeDamage td:
                return s with { CurrentHp = Math.Max(0, s.CurrentHp - td.Amount) };
            case EventEffect.GainMaxHp gm:
                return s with { MaxHp = s.MaxHp + gm.Amount, CurrentHp = s.CurrentHp + gm.Amount };
            case EventEffect.LoseMaxHp lm:
                int newMax = Math.Max(1, s.MaxHp - lm.Amount);
                return s with { MaxHp = newMax, CurrentHp = Math.Min(newMax, s.CurrentHp) };
            case EventEffect.GainRelicRandom gr:
                return GainRelic(s, gr.Rarity, catalog, rng);
            case EventEffect.GrantCardReward:
                return GrantCardReward(s, catalog, rng);
            default:
                return s;
        }
    }

    private static RunState GainRelic(RunState s, CardRarity rarity, DataCatalog catalog, IRng rng)
    {
        var pool = catalog.Relics.Values
            .Where(r => r.Rarity == rarity && !s.Relics.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToArray();
        if (pool.Length == 0) return s;
        var chosen = pool[rng.NextInt(0, pool.Length)];
        var newRelics = s.Relics.Append(chosen.Id).ToList();
        var s1 = s with { Relics = newRelics };
        s1 = NonBattleRelicEffects.ApplyOnPickup(s1, chosen.Id, catalog);
        return BestiaryTracker.NoteRelicsAcquired(s1, new[] { chosen.Id });
    }

    private static RunState GrantCardReward(RunState s, DataCatalog catalog, IRng rng)
    {
        var rt = catalog.RewardTables["act1"];
        var excl = ImmutableArray.CreateRange(s.Deck.Select(c => c.Id));
        var (reward, newRngState) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(new EnemyPool(s.CurrentAct, EnemyTier.Weak)),
            s.RewardRngState, excl, rt, catalog, rng);
        // Event からのカード報酬は Gold / Potion を含めない（CardChoices のみ提示）
        var cardOnly = new RewardState(
            Gold: 0, GoldClaimed: true,
            PotionId: null, PotionClaimed: true,
            CardChoices: reward.CardChoices,
            CardStatus: CardRewardStatus.Pending);
        var next = s with { ActiveReward = cardOnly, RewardRngState = newRngState };
        return BestiaryTracker.NoteCardsSeen(next, reward.CardChoices);
    }
}
