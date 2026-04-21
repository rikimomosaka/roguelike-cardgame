namespace RoguelikeCardGame.Core.Tests.Fixtures;

public static class JsonFixtures
{
    public const string StrikeJson = """
    {
      "id": "strike",
      "name": "ストライク",
      "rarity": 1,
      "cardType": "Attack",
      "cost": 1,
      "effects": [ { "type": "damage", "amount": 6 } ],
      "upgradedEffects": [ { "type": "damage", "amount": 9 } ]
    }
    """;

    public const string DefendJson = """
    {
      "id": "defend",
      "name": "防御",
      "rarity": 1,
      "cardType": "Skill",
      "cost": 1,
      "effects": [ { "type": "gainBlock", "amount": 5 } ],
      "upgradedEffects": [ { "type": "gainBlock", "amount": 8 } ]
    }
    """;

    public const string StrikePromoJson = """
    {
      "id": "strike_promo_anniversary",
      "name": "ストライク",
      "displayName": "ストライク(周年記念)",
      "rarity": 0,
      "cardType": "Attack",
      "cost": 1,
      "effects": [ { "type": "damage", "amount": 6 } ]
    }
    """;

    public const string UnplayableCurseJson = """
    {
      "id": "curse_doubt",
      "name": "Doubt",
      "rarity": 1,
      "cardType": "Skill",
      "cost": null,
      "effects": []
    }
    """;

    public const string UnknownEffectJson = """
    {
      "id": "mystery",
      "name": "謎の一撃",
      "rarity": 2,
      "cardType": "Attack",
      "cost": 2,
      "effects": [ { "type": "summonUnit", "unitId": "imp" } ]
    }
    """;

    public const string BrokenJson = """
    { "id": "strike", "name": "ストライク"
    """;

    // --- 新規：エラーケース用フィクスチャ ---

    /// <summary>id フィールドが存在しない。</summary>
    public const string MissingIdJson = """
    {
      "name": "ストライク",
      "rarity": 1,
      "cardType": "Attack",
      "cost": 1,
      "effects": []
    }
    """;

    /// <summary>name フィールドが存在しない。</summary>
    public const string MissingNameJson = """
    {
      "id": "strike",
      "rarity": 1,
      "cardType": "Attack",
      "cost": 1,
      "effects": []
    }
    """;

    /// <summary>cardType が未知の値 "Creature"。</summary>
    public const string UnknownCardTypeJson = """
    {
      "id": "creature_card",
      "name": "クリーチャー",
      "rarity": 1,
      "cardType": "Creature",
      "cost": 1,
      "effects": []
    }
    """;

    /// <summary>rarity が範囲外の値 99。</summary>
    public const string RarityOutOfRangeJson = """
    {
      "id": "bad_rarity",
      "name": "レアリティ不正",
      "rarity": 99,
      "cardType": "Attack",
      "cost": 1,
      "effects": []
    }
    """;

    /// <summary>upgradedEffects が配列でなく文字列。</summary>
    public const string UpgradedEffectsWrongTypeJson = """
    {
      "id": "wrong_upgraded",
      "name": "不正アップグレード",
      "rarity": 1,
      "cardType": "Attack",
      "cost": 1,
      "effects": [],
      "upgradedEffects": "not-an-array"
    }
    """;

    /// <summary>upgradedEffects が明示的な null。</summary>
    public const string UpgradedEffectsExplicitNullJson = """
    {
      "id": "null_upgraded",
      "name": "ヌルアップグレード",
      "rarity": 1,
      "cardType": "Attack",
      "cost": 1,
      "effects": [],
      "upgradedEffects": null
    }
    """;

    // --- Relic フィクスチャ ---

    public const string BurningBloodJson = """
    {
      "id": "burning_blood",
      "name": "燃え盛る血",
      "rarity": 1,
      "trigger": "OnBattleEnd",
      "effects": [ { "type": "healPercent", "amount": 6 } ]
    }
    """;

    public const string LanternJson = """
    {
      "id": "lantern",
      "name": "ランタン",
      "rarity": 1,
      "trigger": "Passive",
      "effects": []
    }
    """;

    public const string RelicBrokenRarityJson = """
    {
      "id": "bad_relic",
      "name": "バッド",
      "rarity": 99,
      "trigger": "Passive",
      "effects": []
    }
    """;

    public const string RelicUnknownTriggerJson = """
    {
      "id": "bad_relic_trigger",
      "name": "バッド",
      "rarity": 1,
      "trigger": "OnMidnight",
      "effects": []
    }
    """;

    public const string RelicWithDamageEffectJson = """
    {
      "id": "damage_relic",
      "name": "ダメージレリック",
      "rarity": 1,
      "trigger": "OnBattleStart",
      "effects": [ { "type": "damage", "amount": 7 } ]
    }
    """;

    // --- Potion フィクスチャ ---

    public const string BlockPotionJson = """
    {
      "id": "block_potion",
      "name": "ブロックポーション",
      "rarity": 1,
      "usableInBattle": true,
      "usableOutOfBattle": false,
      "effects": [ { "type": "gainBlock", "amount": 12 } ]
    }
    """;

    public const string FirePotionJson = """
    {
      "id": "fire_potion",
      "name": "ファイアポーション",
      "rarity": 1,
      "usableInBattle": true,
      "usableOutOfBattle": false,
      "effects": [ { "type": "damage", "amount": 20 } ]
    }
    """;

    public const string PotionMissingUsableFlagsJson = """
    {
      "id": "bad_potion",
      "name": "バッド",
      "rarity": 1,
      "effects": []
    }
    """;

    // --- Enemy フィクスチャ ---

    public const string JawWormJson = """
    {
      "id": "jaw_worm",
      "name": "ジョウ・ワーム",
      "imageId": "jaw_worm",
      "hpMin": 40,
      "hpMax": 44,
      "act": 1,
      "tier": "Weak",
      "initialMoveId": "chomp",
      "moves": [
        { "id": "chomp",  "kind": "attack", "damageMin": 11, "damageMax": 11, "hits": 1, "nextMoveId": "thrash" },
        { "id": "thrash", "kind": "multi",  "damageMin": 7,  "damageMax": 7,  "hits": 1, "nextMoveId": "bellow" },
        { "id": "bellow", "kind": "buff",   "buff": "strength", "amountMin": 3, "amountMax": 5, "nextMoveId": "chomp" }
      ]
    }
    """;

    public const string HobgoblinJson = """
    {
      "id": "hobgoblin",
      "name": "ホブゴブリン",
      "imageId": "hobgoblin",
      "hpMin": 82,
      "hpMax": 86,
      "act": 1,
      "tier": "Elite",
      "initialMoveId": "bellow",
      "moves": [
        { "id": "bellow", "kind": "buff", "buff": "enrage", "amountMin": 2, "amountMax": 2, "nextMoveId": "rush" },
        { "id": "rush",   "kind": "attack", "damageMin": 14, "damageMax": 14, "hits": 1, "nextMoveId": "skull_bash" },
        { "id": "skull_bash", "kind": "attack", "damageMin": 6, "damageMax": 6, "hits": 1, "nextMoveId": "rush" }
      ]
    }
    """;
}
