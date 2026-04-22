using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

public static class NonBattleRelicEffects
{
    public static RunState ApplyOnPickup(RunState s, string relicId, DataCatalog catalog)
    {
        if (!catalog.TryGetRelic(relicId, out var def)) return s;
        if (def.Trigger != RelicTrigger.OnPickup) return s;
        return ApplyEffects(s, def);
    }

    public static RunState ApplyOnMapTileResolved(RunState s, DataCatalog catalog)
    {
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (def.Trigger != RelicTrigger.OnMapTileResolved) continue;
            s = ApplyEffects(s, def);
        }
        return s;
    }

    public static int ApplyPassiveRestHealBonus(int baseBonus, RunState s, DataCatalog catalog)
    {
        int bonus = baseBonus;
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (def.Trigger != RelicTrigger.Passive) continue;
            foreach (var eff in def.Effects)
                if (eff is RestHealBonusEffect rhb) bonus += rhb.Amount;
        }
        return bonus;
    }

    private static RunState ApplyEffects(RunState s, RelicDefinition def)
    {
        foreach (var eff in def.Effects)
        {
            s = eff switch
            {
                GainMaxHpEffect gm => s with { MaxHp = s.MaxHp + gm.Amount, CurrentHp = s.CurrentHp + gm.Amount },
                GainGoldEffect gg => s with { Gold = s.Gold + gg.Amount },
                _ => s,
            };
        }
        return s;
    }
}
