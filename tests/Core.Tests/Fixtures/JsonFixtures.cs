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
      "effects": [ { "action": "attack", "scope": "single", "side": "enemy", "amount": 6 } ],
      "upgradedEffects": [ { "action": "attack", "scope": "single", "side": "enemy", "amount": 9 } ]
    }
    """;

    public const string DefendJson = """
    {
      "id": "defend",
      "name": "防御",
      "rarity": 1,
      "cardType": "Skill",
      "cost": 1,
      "effects": [ { "action": "block", "scope": "self", "amount": 5 } ],
      "upgradedEffects": [ { "action": "block", "scope": "self", "amount": 8 } ]
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
      "effects": [ { "action": "attack", "scope": "single", "side": "enemy", "amount": 6 } ]
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
      "effects": [ { "action": "summonUnit", "scope": "self", "amount": 0, "unitId": "imp" } ]
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

    // Phase 10.5.L1.5: relic-level "trigger" は loader が無視するため、
    // 必要に応じて effects[].trigger に移行する。
    public const string BurningBloodJson = """
    {
      "id": "burning_blood",
      "name": "燃え盛る血",
      "rarity": 1,
      "effects": [ { "action": "healPercent", "scope": "self", "amount": 6, "trigger": "OnBattleEnd" } ]
    }
    """;

    public const string LanternJson = """
    {
      "id": "lantern",
      "name": "ランタン",
      "rarity": 1,
      "effects": []
    }
    """;

    public const string RelicBrokenRarityJson = """
    {
      "id": "bad_relic",
      "name": "バッド",
      "rarity": 99,
      "effects": []
    }
    """;

    public const string RelicWithDamageEffectJson = """
    {
      "id": "damage_relic",
      "name": "ダメージレリック",
      "rarity": 1,
      "effects": [ { "action": "attack", "scope": "single", "side": "enemy", "amount": 7, "trigger": "OnBattleStart" } ]
    }
    """;

    // --- Potion フィクスチャ ---

    public const string BlockPotionJson = """
    {
      "id": "block_potion",
      "name": "ブロックポーション",
      "rarity": 1,
      "effects": [ { "action": "block", "scope": "self", "amount": 12, "battleOnly": true } ]
    }
    """;

    public const string FirePotionJson = """
    {
      "id": "fire_potion",
      "name": "ファイアポーション",
      "rarity": 1,
      "effects": [ { "action": "attack", "scope": "single", "side": "enemy", "amount": 20, "battleOnly": true } ]
    }
    """;

    // --- Enemy フィクスチャ ---

    public const string JawWormJson = """
    {
      "id": "jaw_worm",
      "name": "ジョウ・ワーム",
      "imageId": "jaw_worm",
      "hp": 42,
      "act": 1,
      "tier": "Weak",
      "initialMoveId": "chomp",
      "moves": [
        { "id": "chomp",  "kind": "Attack", "nextMoveId": "thrash",
          "effects": [ { "action": "attack", "scope": "all", "side": "enemy", "amount": 11 } ] },
        { "id": "thrash", "kind": "Multi",  "nextMoveId": "bellow",
          "effects": [ { "action": "attack", "scope": "all", "side": "enemy", "amount": 7 } ] },
        { "id": "bellow", "kind": "Buff",   "nextMoveId": "chomp",
          "effects": [ { "action": "buff", "scope": "self", "name": "strength", "amount": 4 } ] }
      ]
    }
    """;

    public const string HobgoblinJson = """
    {
      "id": "hobgoblin",
      "name": "ホブゴブリン",
      "imageId": "hobgoblin",
      "hp": 84,
      "act": 1,
      "tier": "Elite",
      "initialMoveId": "bellow",
      "moves": [
        { "id": "bellow", "kind": "Buff", "nextMoveId": "rush",
          "effects": [ { "action": "buff", "scope": "self", "name": "enrage", "amount": 2 } ] },
        { "id": "rush",   "kind": "Attack", "nextMoveId": "skull_bash",
          "effects": [ { "action": "attack", "scope": "all", "side": "enemy", "amount": 14 } ] },
        { "id": "skull_bash", "kind": "Attack", "nextMoveId": "rush",
          "effects": [ { "action": "attack", "scope": "all", "side": "enemy", "amount": 6 } ] }
      ]
    }
    """;
}
