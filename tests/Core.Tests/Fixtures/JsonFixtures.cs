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
}
