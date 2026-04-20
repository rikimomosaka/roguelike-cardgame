using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Data;

public sealed class DataCatalogException : Exception
{
    public DataCatalogException(string message) : base(message) { }
}

/// <summary>ゲーム全体のマスターデータ。ラン状態やマップ生成から参照される読み取り専用の辞書束。</summary>
public sealed record DataCatalog(
    IReadOnlyDictionary<string, CardDefinition> Cards,
    IReadOnlyDictionary<string, RelicDefinition> Relics,
    IReadOnlyDictionary<string, PotionDefinition> Potions,
    IReadOnlyDictionary<string, EnemyDefinition> Enemies)
{
    public static DataCatalog LoadFromStrings(
        IEnumerable<string> cards,
        IEnumerable<string> relics,
        IEnumerable<string> potions,
        IEnumerable<string> enemies)
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

        return new DataCatalog(cardMap, relicMap, potionMap, enemyMap);
    }

    public bool TryGetCard(string id, out CardDefinition? def) => Cards.TryGetValue(id, out def);
    public bool TryGetRelic(string id, out RelicDefinition? def) => Relics.TryGetValue(id, out def);
    public bool TryGetPotion(string id, out PotionDefinition? def) => Potions.TryGetValue(id, out def);
    public bool TryGetEnemy(string id, out EnemyDefinition? def) => Enemies.TryGetValue(id, out def);
}
