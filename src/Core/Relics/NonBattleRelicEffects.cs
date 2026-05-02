using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>
/// 戦闘外（マップ／休憩／取得時）でのレリック効果を適用する純粋関数群。
/// Phase 10 設計書 第 2-7 章参照。Action 文字列で効果を識別する。
/// </summary>
/// <remarks>
/// Phase 10.5.L1.5: relic-level Trigger フィールド廃止に伴い per-effect filter に変更。
/// 各 relic の effects[] をループして eff.Trigger == 対象 trigger のものだけを適用する。
/// </remarks>
public static class NonBattleRelicEffects
{
    public static RunState ApplyOnPickup(RunState s, string relicId, DataCatalog catalog)
    {
        if (!catalog.TryGetRelic(relicId, out var def)) return s;
        if (!def.Implemented) return s;
        return ApplyEffectsForTrigger(s, def, "OnPickup");
    }

    public static RunState ApplyOnMapTileResolved(RunState s, DataCatalog catalog)
    {
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (!def.Implemented) continue;
            s = ApplyEffectsForTrigger(s, def, "OnMapTileResolved");
        }
        return s;
    }

    public static int ApplyPassiveRestHealBonus(int baseBonus, RunState s, DataCatalog catalog)
    {
        int bonus = baseBonus;
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (!def.Implemented) continue;
            foreach (var eff in def.Effects)
            {
                if (eff.Trigger != "Passive") continue;
                if (eff.Action == "restHealBonus") bonus += eff.Amount;
            }
        }
        return bonus;
    }

    private static RunState ApplyEffectsForTrigger(RunState s, RelicDefinition def, string trigger)
    {
        foreach (var eff in def.Effects)
        {
            if (eff.Trigger != trigger) continue;
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
