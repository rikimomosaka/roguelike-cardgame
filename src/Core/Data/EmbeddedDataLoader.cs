using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Data;

public static class EmbeddedDataLoader
{
    private const string CardsPrefix = "RoguelikeCardGame.Core.Data.Cards.";
    private const string RelicsPrefix = "RoguelikeCardGame.Core.Data.Relics.";
    private const string PotionsPrefix = "RoguelikeCardGame.Core.Data.Potions.";
    private const string EnemiesPrefix = "RoguelikeCardGame.Core.Data.Enemies.";
    private const string UnitsPrefix = "RoguelikeCardGame.Core.Data.Units.";
    private const string EncountersPrefix = "RoguelikeCardGame.Core.Data.Encounters.";
    private const string RewardTablePrefix = "RoguelikeCardGame.Core.Data.RewardTable.";
    private const string CharactersPrefix = "RoguelikeCardGame.Core.Data.Characters.";
    private const string EventsPrefix = "RoguelikeCardGame.Core.Data.Events.";
    private const string RelicsActStartPrefix = "RoguelikeCardGame.Core.Data.RelicsActStart.";
    private const string MerchantPricesResourceName = "RoguelikeCardGame.Core.Data.merchant-prices.json";

    public static DataCatalog LoadCatalog()
    {
        var asm = typeof(EmbeddedDataLoader).Assembly;
        string? merchantPricesJson = ReadSingle(asm, MerchantPricesResourceName);
        var unknownConfig = MapGenerationConfigLoader.LoadAct1().UnknownResolutionWeights;
        return DataCatalog.LoadFromStrings(
            cards: ReadAllWithPrefix(asm, CardsPrefix),
            relics: ReadAllWithPrefix(asm, RelicsPrefix),
            potions: ReadAllWithPrefix(asm, PotionsPrefix),
            enemies: ReadAllWithPrefix(asm, EnemiesPrefix),
            encounters: ReadAllWithPrefix(asm, EncountersPrefix),
            rewardTables: ReadAllWithPrefix(asm, RewardTablePrefix),
            characters: ReadAllWithPrefix(asm, CharactersPrefix),
            events: ReadAllWithPrefix(asm, EventsPrefix),
            actStartRelicPools: ReadAllWithPrefix(asm, RelicsActStartPrefix),
            merchantPricesJson: merchantPricesJson,
            units: ReadAllWithPrefix(asm, UnitsPrefix),
            unknownConfig: unknownConfig);
    }

    /// <summary>
    /// 開発者ローカル override (id → JSON 文字列) を base カード / レリック JSON に
    /// CardOverrideMerger / RelicOverrideMerger でマージしてから DataCatalog を構築する。
    /// Phase 10.5.H (cards) → 10.5.L1 で relicOverrides 引数を追加。
    ///
    /// 引数は文字列辞書のみで file I/O は触らないため Core 内に置ける。Server 側は
    /// DevOverrideLoader で disk 読込→これを呼び出す。
    /// </summary>
    public static DataCatalog LoadCatalogWithOverrides(
        IReadOnlyDictionary<string, string> cardOverrides,
        IReadOnlyDictionary<string, string>? relicOverrides = null)
    {
        var asm = typeof(EmbeddedDataLoader).Assembly;
        var baseCards = ReadAllWithPrefix(asm, CardsPrefix).ToList();
        var baseRelics = ReadAllWithPrefix(asm, RelicsPrefix).ToList();

        IEnumerable<string> mergedCards = MergeWith(baseCards, cardOverrides, CardOverrideMerger.Merge);
        IEnumerable<string> mergedRelics = relicOverrides is null
            ? baseRelics
            : MergeWith(baseRelics, relicOverrides, RelicOverrideMerger.Merge);

        string? merchantPricesJson = ReadSingle(asm, MerchantPricesResourceName);
        var unknownConfig = MapGenerationConfigLoader.LoadAct1().UnknownResolutionWeights;
        return DataCatalog.LoadFromStrings(
            cards: mergedCards,
            relics: mergedRelics,
            potions: ReadAllWithPrefix(asm, PotionsPrefix),
            enemies: ReadAllWithPrefix(asm, EnemiesPrefix),
            encounters: ReadAllWithPrefix(asm, EncountersPrefix),
            rewardTables: ReadAllWithPrefix(asm, RewardTablePrefix),
            characters: ReadAllWithPrefix(asm, CharactersPrefix),
            events: ReadAllWithPrefix(asm, EventsPrefix),
            actStartRelicPools: ReadAllWithPrefix(asm, RelicsActStartPrefix),
            merchantPricesJson: merchantPricesJson,
            units: ReadAllWithPrefix(asm, UnitsPrefix),
            unknownConfig: unknownConfig);
    }

    /// <summary>
    /// base entries を id でマッチさせて override が存在すれば <paramref name="merger"/> を呼ぶ。
    /// override count=0 なら base そのままを返す。
    /// </summary>
    private static IReadOnlyList<string> MergeWith(
        IReadOnlyList<string> baseEntries,
        IReadOnlyDictionary<string, string> overrides,
        System.Func<string, string, string> merger)
    {
        if (overrides.Count == 0) return baseEntries;
        var merged = new List<string>(baseEntries.Count);
        foreach (var json in baseEntries)
        {
            using var doc = JsonDocument.Parse(json);
            string? id = null;
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("id", out var idEl) &&
                idEl.ValueKind == JsonValueKind.String)
            {
                id = idEl.GetString();
            }

            if (id is not null && overrides.TryGetValue(id, out var ovr))
                merged.Add(merger(json, ovr));
            else
                merged.Add(json);
        }
        return merged;
    }

    private static string? ReadSingle(Assembly asm, string resourceName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> ReadAllWithPrefix(Assembly asm, string prefix)
    {
        var names = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix) && n.EndsWith(".json"))
            .OrderBy(n => n);
        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }
}
