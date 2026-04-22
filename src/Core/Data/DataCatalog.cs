using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Data;

public sealed class DataCatalogException : Exception
{
    public DataCatalogException(string message) : base(message) { }
}

public sealed record DataCatalog(
    IReadOnlyDictionary<string, CardDefinition> Cards,
    IReadOnlyDictionary<string, RelicDefinition> Relics,
    IReadOnlyDictionary<string, PotionDefinition> Potions,
    IReadOnlyDictionary<string, EnemyDefinition> Enemies,
    IReadOnlyDictionary<string, EncounterDefinition> Encounters,
    IReadOnlyDictionary<string, RewardTable> RewardTables,
    IReadOnlyDictionary<string, CharacterDefinition> Characters,
    IReadOnlyDictionary<string, EventDefinition> Events,
    MerchantPrices? MerchantPrices = null)
{
    public static DataCatalog LoadFromStrings(
        IEnumerable<string> cards,
        IEnumerable<string> relics,
        IEnumerable<string> potions,
        IEnumerable<string> enemies,
        IEnumerable<string> encounters,
        IEnumerable<string> rewardTables,
        IEnumerable<string> characters,
        IEnumerable<string>? events = null,
        string? merchantPricesJson = null)
    {
        var cardMap = new Dictionary<string, CardDefinition>();
        foreach (var json in cards)
        {
            var def = CardJsonLoader.Parse(json);
            if (!cardMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"カード ID が重複: {def.Id}");
        }

        var relicMap = new Dictionary<string, RelicDefinition>();
        foreach (var json in relics)
        {
            var def = RelicJsonLoader.Parse(json);
            if (!relicMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"レリック ID が重複: {def.Id}");
        }

        var potionMap = new Dictionary<string, PotionDefinition>();
        foreach (var json in potions)
        {
            var def = PotionJsonLoader.Parse(json);
            if (!potionMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"ポーション ID が重複: {def.Id}");
        }

        var enemyMap = new Dictionary<string, EnemyDefinition>();
        foreach (var json in enemies)
        {
            var def = EnemyJsonLoader.Parse(json);
            if (!enemyMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"敵 ID が重複: {def.Id}");
        }

        var encMap = new Dictionary<string, EncounterDefinition>();
        foreach (var json in encounters)
        {
            var def = EncounterJsonLoader.Parse(json);
            if (!encMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"encounter ID が重複: {def.Id}");
            foreach (var eid in def.EnemyIds)
                if (!enemyMap.ContainsKey(eid))
                    throw new DataCatalogException(
                        $"encounter \"{def.Id}\" が参照する敵 ID \"{eid}\" が存在しません");
        }

        var rtMap = new Dictionary<string, RewardTable>();
        foreach (var json in rewardTables)
        {
            var def = RewardTableJsonLoader.Parse(json);
            if (!rtMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"reward-table ID が重複: {def.Id}");
        }

        var chMap = new Dictionary<string, CharacterDefinition>();
        foreach (var json in characters)
        {
            var def = CharacterJsonLoader.Parse(json);
            if (!chMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"character ID が重複: {def.Id}");
            foreach (var cid in def.Deck)
                if (!cardMap.ContainsKey(cid))
                    throw new DataCatalogException(
                        $"character \"{def.Id}\" のデッキが参照するカード ID \"{cid}\" が存在しません");
        }

        var eventMap = new Dictionary<string, EventDefinition>();
        if (events is not null)
        {
            foreach (var json in events)
            {
                var def = EventJsonLoader.Parse(json);
                if (!eventMap.TryAdd(def.Id, def))
                    throw new DataCatalogException($"event ID が重複: {def.Id}");
            }
        }

        MerchantPrices? mp = null;
        if (merchantPricesJson is not null)
            mp = MerchantPricesJsonLoader.Parse(merchantPricesJson);

        return new DataCatalog(cardMap, relicMap, potionMap, enemyMap, encMap, rtMap, chMap, eventMap, mp);
    }

    public bool TryGetCard(string id, [MaybeNullWhen(false)] out CardDefinition def) => Cards.TryGetValue(id, out def);
    public bool TryGetRelic(string id, [MaybeNullWhen(false)] out RelicDefinition def) => Relics.TryGetValue(id, out def);
    public bool TryGetPotion(string id, [MaybeNullWhen(false)] out PotionDefinition def) => Potions.TryGetValue(id, out def);
    public bool TryGetEnemy(string id, [MaybeNullWhen(false)] out EnemyDefinition def) => Enemies.TryGetValue(id, out def);
    public bool TryGetEncounter(string id, [MaybeNullWhen(false)] out EncounterDefinition def) => Encounters.TryGetValue(id, out def);
    public bool TryGetRewardTable(string id, [MaybeNullWhen(false)] out RewardTable def) => RewardTables.TryGetValue(id, out def);
    public bool TryGetCharacter(string id, [MaybeNullWhen(false)] out CharacterDefinition def) => Characters.TryGetValue(id, out def);
    public bool TryGetEvent(string id, [MaybeNullWhen(false)] out EventDefinition def) => Events.TryGetValue(id, out def);
}
