using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Tests.Battle.Fixtures;

/// <summary>Phase 10.2.A バトルテスト用の共通 factory。インライン生成方針 (spec Q5)。</summary>
public static class BattleFixtures
{
    // ===== CombatActor factories =====

    public static CombatActor Hero(int hp = 70, int slotIndex = 0) =>
        new("hero_inst", "hero", ActorSide.Ally, slotIndex, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, null);

    public static CombatActor Goblin(int slotIndex = 0, int hp = 20, string moveId = "swing") =>
        new($"goblin_inst_{slotIndex}", "goblin", ActorSide.Enemy, slotIndex, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, moveId);

    // ===== CardDefinition factories =====

    public static CardDefinition Strike(int amount = 6) =>
        new("strike", "Strike", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, amount) },
            UpgradedEffects: null, Keywords: null);

    public static CardDefinition Defend(int amount = 5) =>
        new("defend", "Defend", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("block", EffectScope.Self, null, amount) },
            UpgradedEffects: null, Keywords: null);

    public static CardDefinition Cleave(int amount = 4) =>
        new("cleave", "Cleave", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, amount) },
            UpgradedEffects: null, Keywords: null);

    // ===== EnemyDefinition factories =====

    public static EnemyDefinition GoblinDef(int hp = 20, int attack = 5) =>
        new("goblin", "Goblin", "img_goblin", hp, new EnemyPool(1, EnemyTier.Weak),
            "swing",
            new[] {
                new MoveDefinition("swing", MoveKind.Attack,
                    new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, attack) },
                    "swing")
            });

    public static EncounterDefinition SingleGoblinEncounter() =>
        new("enc_test", new EnemyPool(1, EnemyTier.Weak), new[] { "goblin" });

    // ===== DataCatalog factory =====

    /// <summary>テスト用最小限の DataCatalog。必要に応じて defs を上書き可能。</summary>
    public static DataCatalog MinimalCatalog(
        IEnumerable<CardDefinition>? cards = null,
        IEnumerable<EnemyDefinition>? enemies = null,
        IEnumerable<EncounterDefinition>? encounters = null)
    {
        var cardDict = (cards ?? new[] { Strike(), Defend() })
            .ToDictionary(c => c.Id);
        var enemyDict = (enemies ?? new[] { GoblinDef() })
            .ToDictionary(e => e.Id);
        var encDict = (encounters ?? new[] { SingleGoblinEncounter() })
            .ToDictionary(e => e.Id);
        return new DataCatalog(
            Cards: cardDict,
            Relics: new Dictionary<string, RoguelikeCardGame.Core.Relics.RelicDefinition>(),
            Potions: new Dictionary<string, RoguelikeCardGame.Core.Potions.PotionDefinition>(),
            Enemies: enemyDict,
            Encounters: encDict,
            RewardTables: new Dictionary<string, RewardTable>(),
            Characters: new Dictionary<string, CharacterDefinition>(),
            Events: new Dictionary<string, RoguelikeCardGame.Core.Events.EventDefinition>());
    }

    // ===== BattleCardInstance helpers =====

    public static BattleCardInstance MakeBattleCard(string defId, string instId, bool upgraded = false) =>
        new(instId, defId, upgraded, null);
}
