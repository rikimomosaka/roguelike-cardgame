using System;
using System.Collections.Immutable;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Merchant;

public sealed class MerchantPricesJsonException : Exception
{
    public MerchantPricesJsonException(string message) : base(message) { }
    public MerchantPricesJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class MerchantPricesJsonLoader
{
    public static MerchantPrices Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        { throw new MerchantPricesJsonException("merchant-prices JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            var r = doc.RootElement;
            return new MerchantPrices(
                Cards: ParseRarityMap(r, "cards"),
                Relics: ParseRarityMap(r, "relics"),
                Potions: ParseRarityMap(r, "potions"),
                DiscardSlotPrice: r.GetProperty("discardSlotPrice").GetInt32());
        }
    }

    private static ImmutableDictionary<CardRarity, int> ParseRarityMap(JsonElement r, string key)
    {
        if (!r.TryGetProperty(key, out var obj) || obj.ValueKind != JsonValueKind.Object)
            throw new MerchantPricesJsonException($"\"{key}\" object が欠落しています。");
        var b = ImmutableDictionary.CreateBuilder<CardRarity, int>();
        foreach (var rarity in new[] { CardRarity.Common, CardRarity.Rare, CardRarity.Epic })
        {
            if (!obj.TryGetProperty(rarity.ToString(), out var v) || v.ValueKind != JsonValueKind.Number)
                throw new MerchantPricesJsonException($"\"{key}.{rarity}\" が欠落しています。");
            b.Add(rarity, v.GetInt32());
        }
        return b.ToImmutable();
    }
}
