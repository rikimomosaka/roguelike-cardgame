using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>
/// 戦闘外（マップ／休憩／取得時）でのレリック効果を適用する純粋関数群。
/// Phase 10 設計書 第 2-7 章参照。Action 文字列で効果を識別する。
/// </summary>
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
                if (eff.Action == "restHealBonus") bonus += eff.Amount;
        }
        return bonus;
    }

    private static RunState ApplyEffects(RunState s, RelicDefinition def)
    {
        foreach (var eff in def.Effects)
        {
            s = eff.Action switch
            {
                "gainMaxHp" => s with { MaxHp = s.MaxHp + eff.Amount, CurrentHp = s.CurrentHp + eff.Amount },
                "gainGold"  => s with { Gold = s.Gold + eff.Amount },
                _           => s,
            };
        }
        return s;
    }
}
