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
        var cards = ReadAllWithPrefix(asm, CardsPrefix).ToList();
        var relics = ReadAllWithPrefix(asm, RelicsPrefix).ToList();
        EmbeddedDataCatalogValidator.ValidateCards(cards);
        EmbeddedDataCatalogValidator.ValidateRelics(relics);
        string? merchantPricesJson = ReadSingle(asm, MerchantPricesResourceName);
        var unknownConfig = MapGenerationConfigLoader.LoadAct1().UnknownResolutionWeights;
        return DataCatalog.LoadFromStrings(
            cards: cards,
            relics: relics,
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
        IReadOnlyDictionary<string, string>? relicOverrides = null,
        IReadOnlyDictionary<string, string>? potionOverrides = null,
        IReadOnlyDictionary<string, string>? enemyOverrides = null,
        IReadOnlyDictionary<string, string>? unitOverrides = null,
        IReadOnlyDictionary<string, string>? encounterOverrides = null,
        IReadOnlyDictionary<string, string>? rewardTableOverrides = null,
        IReadOnlyDictionary<string, string>? characterOverrides = null,
        IReadOnlyDictionary<string, string>? eventOverrides = null,
        IReadOnlyDictionary<string, string>? actStartRelicPoolOverrides = null,
        string? merchantPricesOverride = null,
        MapGenerationConfig? mapConfigOverride = null)
    {
        var asm = typeof(EmbeddedDataLoader).Assembly;
        var baseCards = ReadAllWithPrefix(asm, CardsPrefix).ToList();
        var baseRelics = ReadAllWithPrefix(asm, RelicsPrefix).ToList();
        EmbeddedDataCatalogValidator.ValidateCards(baseCards);
        EmbeddedDataCatalogValidator.ValidateRelics(baseRelics);

        var mergedCards = MergeWith(baseCards, cardOverrides, CardOverrideMerger.Merge).ToList();
        EmbeddedDataCatalogValidator.ValidateCards(mergedCards);
        IEnumerable<string> mergedRelics = relicOverrides is null
            ? baseRelics
            : MergeWith(baseRelics, relicOverrides, RelicOverrideMerger.Merge);

        var mergedPotions = MergeReplacements(ReadAllWithPrefix(asm, PotionsPrefix), potionOverrides, ReadId);
        var mergedEnemies = MergeReplacements(ReadAllWithPrefix(asm, EnemiesPrefix), enemyOverrides, ReadId);
        var mergedUnits = MergeReplacements(ReadAllWithPrefix(asm, UnitsPrefix), unitOverrides, ReadId);
        var mergedEncounters = MergeReplacements(ReadAllWithPrefix(asm, EncountersPrefix), encounterOverrides, ReadId);
        var mergedRewardTables = MergeReplacements(ReadAllWithPrefix(asm, RewardTablePrefix), rewardTableOverrides, ReadId);
        var mergedCharacters = MergeReplacements(ReadAllWithPrefix(asm, CharactersPrefix), characterOverrides, ReadId);
        var mergedEvents = MergeReplacements(ReadAllWithPrefix(asm, EventsPrefix), eventOverrides, ReadId);
        var mergedActStartPools = MergeReplacements(
            ReadAllWithPrefix(asm, RelicsActStartPrefix), actStartRelicPoolOverrides, ReadActPoolId);

        string? merchantPricesJson = merchantPricesOverride ?? ReadSingle(asm, MerchantPricesResourceName);
        var mapConfig = mapConfigOverride ?? MapGenerationConfigLoader.LoadAct1();
        return DataCatalog.LoadFromStrings(
            cards: mergedCards,
            relics: mergedRelics,
            potions: mergedPotions,
            enemies: mergedEnemies,
            encounters: mergedEncounters,
            rewardTables: mergedRewardTables,
            characters: mergedCharacters,
            events: mergedEvents,
            actStartRelicPools: mergedActStartPools,
            merchantPricesJson: merchantPricesJson,
            units: mergedUnits,
            unknownConfig: mapConfig.UnknownResolutionWeights);
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
        var merged = new List<string>(baseEntries.Count + overrides.Count);
        var consumed = new HashSet<string>(StringComparer.Ordinal);
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
            {
                merged.Add(merger(json, ovr));
                consumed.Add(id);
            }
            else
                merged.Add(json);
        }
        foreach (var pair in overrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            // Old development folders may contain partial overrides whose embedded base was
            // later deleted. They are not standalone definitions. A newly-authored entity is
            // appended only when it is a complete document (at minimum it owns its name).
            if (!consumed.Contains(pair.Key) && IsStandaloneDefinition(pair.Value))
                merged.Add(pair.Value);
        }
        return merged;
    }

    private static IReadOnlyList<string> MergeReplacements(
        IEnumerable<string> baseEntries,
        IReadOnlyDictionary<string, string>? overrides,
        System.Func<string, string?> keySelector)
    {
        var baseList = baseEntries.ToList();
        if (overrides is null || overrides.Count == 0) return baseList;

        var result = new List<string>(baseList.Count + overrides.Count);
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var json in baseList)
        {
            var key = keySelector(json);
            if (key is not null && overrides.TryGetValue(key, out var replacement))
            {
                result.Add(replacement);
                consumed.Add(key);
            }
            else
            {
                result.Add(json);
            }
        }
        foreach (var pair in overrides.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!consumed.Contains(pair.Key))
                result.Add(pair.Value);
        }
        return result;
    }

    private static string? ReadId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object &&
               doc.RootElement.TryGetProperty("id", out var id) &&
               id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }

    private static bool IsStandaloneDefinition(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object &&
               doc.RootElement.TryGetProperty("name", out var name) &&
               name.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(name.GetString());
    }

    private static string? ReadActPoolId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object &&
               doc.RootElement.TryGetProperty("act", out var act) &&
               act.TryGetInt32(out var value)
            ? $"act{value}"
            : null;
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
