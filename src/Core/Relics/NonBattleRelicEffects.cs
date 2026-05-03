using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>
/// 戦闘外（マップ／休憩／取得時／ショップ／報酬生成／デッキ追加時）でのレリック効果を適用する純粋関数群。
/// Phase 10 設計書 第 2-7 章 / Phase 10.5.L1.5 unified-triggers / Phase 10.6.A run-flow triggers 参照。
/// Action 文字列 (gainMaxHp / gainGold / healHp) で効果を識別する。
/// </summary>
public static class NonBattleRelicEffects
{
    public static RunState ApplyOnPickup(RunState s, string relicId, DataCatalog catalog)
    {
        if (!catalog.TryGetRelic(relicId, out var def)) return s;
        if (!def.Implemented) return s;
        return ApplyEffectsForTrigger(s, def, "OnPickup");
    }

    public static RunState ApplyOnMapTileResolved(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnMapTileResolved");

    public static RunState ApplyOnEnterShop(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnEnterShop");

    public static RunState ApplyOnEnterRestSite(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnEnterRestSite");

    public static RunState ApplyOnRest(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnRest");

    public static RunState ApplyOnRewardGenerated(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnRewardGenerated");

    public static RunState ApplyOnCardAddedToDeck(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnCardAddedToDeck");

    private static RunState ApplyForAllOwnedRelics(RunState s, DataCatalog catalog, string trigger)
    {
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (!def.Implemented) continue;
            s = ApplyEffectsForTrigger(s, def, trigger);
        }
        return s;
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
                "healHp"    => s with { CurrentHp = System.Math.Min(s.MaxHp, s.CurrentHp + eff.Amount) },
                _           => s,
            };
        }
        return s;
    }
}
