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
}
