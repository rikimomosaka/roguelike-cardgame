using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Tests;

/// <summary>
/// フェイクレリックを注入した DataCatalog を構築するテストヘルパ。
/// T1〜T8 の各テストファイルにローカルコピーされていたものを Phase 10.6.A T9 で集約。
/// </summary>
internal static class RelicCatalogTestHelpers
{
    /// <summary>
    /// <paramref name="baseCatalog"/> に対してフェイクレリックを注入した新しい DataCatalog を返す。
    /// </summary>
    /// <param name="baseCatalog">ベースになるカタログ（各テストの static catalog field をそのまま渡す）。</param>
    /// <param name="id">フェイクレリックの ID。</param>
    /// <param name="effects">フェイクレリックに付与するエフェクト一覧。</param>
    /// <param name="implemented">false にすると Implemented=false のレリックを注入する。</param>
    public static DataCatalog BuildCatalogWithFakeRelic(
        DataCatalog baseCatalog,
        string id,
        IReadOnlyList<CardEffect> effects,
        bool implemented = true)
    {
        var fake = new RelicDefinition(
            Id: id,
            Name: $"fake_{id}",
            Rarity: CardRarity.Common,
            Effects: effects,
            Description: "",
            Implemented: implemented);

        var relics = baseCatalog.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return baseCatalog with { Relics = relics };
    }
}
