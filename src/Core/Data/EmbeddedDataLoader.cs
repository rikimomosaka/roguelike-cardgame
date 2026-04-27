using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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
            units: ReadAllWithPrefix(asm, UnitsPrefix));
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
