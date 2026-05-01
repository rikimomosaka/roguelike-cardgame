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
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, null,
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);   // 10.2.D

    /// <summary>10.5.F: テストで CurrentHp / MaxHp を別個に指定したい場合のヘルパー。</summary>
    public static CombatActor Hero(int currentHp, int maxHp, int slotIndex = 0) =>
        new("hero_inst", "hero", ActorSide.Ally, slotIndex, currentHp, maxHp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, null,
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);

    public static CombatActor Goblin(int slotIndex = 0, int hp = 20, string moveId = "swing") =>
        new($"goblin_inst_{slotIndex}", "goblin", ActorSide.Enemy, slotIndex, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, moveId,
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);   // 10.2.D

    /// <summary>10.2.D: 召喚 actor 用 factory（テストで召喚キャラを直接構築する際に使用）。</summary>
    public static CombatActor SummonActor(
        string instanceId, string definitionId, int slotIndex,
        int hp = 10, int? lifetime = null, string? associatedCardId = null,
        string? moveId = null) =>
        new(instanceId, definitionId, ActorSide.Ally, slotIndex, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, moveId,
            RemainingLifetimeTurns: lifetime,
            AssociatedSummonHeldInstanceId: associatedCardId);

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

    // ===== UnitDefinition factory =====

    /// <summary>10.2.D: 召喚キャラ定義 factory。デフォルトは hp=10、wait move 持ち、永続。</summary>
    public static UnitDefinition MinionDef(string id = "minion", int hp = 10, int? lifetime = null) =>
        new(id, id, $"img_{id}", hp,
            InitialMoveId: "wait",
            Moves: new[] {
                new MoveDefinition("wait", MoveKind.Defend,
                    new[] { new CardEffect("block", EffectScope.Self, null, 0) }, "wait")
            },
            LifetimeTurns: lifetime);

    // ===== DataCatalog factory =====

    /// <summary>テスト用最小限の DataCatalog。必要に応じて defs を上書き可能。</summary>
    public static DataCatalog MinimalCatalog(
        IEnumerable<CardDefinition>? cards = null,
        IEnumerable<EnemyDefinition>? enemies = null,
        IEnumerable<EncounterDefinition>? encounters = null,
        IEnumerable<UnitDefinition>? units = null,   // 10.2.D 追加
        IEnumerable<RoguelikeCardGame.Core.Relics.RelicDefinition>? relics = null,    // ← 10.2.E
        IEnumerable<RoguelikeCardGame.Core.Potions.PotionDefinition>? potions = null) // ← 10.2.E
    {
        var cardDict = (cards ?? new[] { Strike(), Defend() })
            .ToDictionary(c => c.Id);
        var enemyDict = (enemies ?? new[] { GoblinDef() })
            .ToDictionary(e => e.Id);
        var encDict = (encounters ?? new[] { SingleGoblinEncounter() })
            .ToDictionary(e => e.Id);
        var unitDict = (units ?? new[] { MinionDef() })
            .ToDictionary(u => u.Id);
        var relicDict = (relics ?? System.Array.Empty<RoguelikeCardGame.Core.Relics.RelicDefinition>())
            .ToDictionary(r => r.Id);
        var potionDict = (potions ?? System.Array.Empty<RoguelikeCardGame.Core.Potions.PotionDefinition>())
            .ToDictionary(p => p.Id);
        return new DataCatalog(
            Cards: cardDict,
            Relics: relicDict,
            Potions: potionDict,
            Enemies: enemyDict,
            Encounters: encDict,
            RewardTables: new Dictionary<string, RewardTable>(),
            Characters: new Dictionary<string, CharacterDefinition>(),
            Events: new Dictionary<string, RoguelikeCardGame.Core.Events.EventDefinition>(),
            Units: unitDict);   // 10.2.D
    }

    // ===== RelicDefinition factory (10.2.E) =====

    /// <summary>テスト用最小限の RelicDefinition。</summary>
    public static RoguelikeCardGame.Core.Relics.RelicDefinition Relic(
        string id = "test_relic",
        RoguelikeCardGame.Core.Relics.RelicTrigger trigger = RoguelikeCardGame.Core.Relics.RelicTrigger.OnTurnStart,
        bool implemented = true,
        params CardEffect[] effects) =>
        new(id, id, CardRarity.Common, trigger, effects, "", implemented);

    // ===== PotionDefinition factory (10.2.E) =====

    /// <summary>テスト用最小限の PotionDefinition。</summary>
    public static RoguelikeCardGame.Core.Potions.PotionDefinition Potion(
        string id = "test_potion",
        params CardEffect[] effects) =>
        new(id, id, CardRarity.Common, effects);

    // ===== MinimalState helper (10.2.E) =====

    /// <summary>OwnedRelicIds / Potions snapshot をデフォルト付きで構築する BattleState ヘルパー。</summary>
    public static BattleState MinimalState(
        ImmutableArray<CombatActor>? allies = null,
        ImmutableArray<CombatActor>? enemies = null,
        int turn = 1,
        BattlePhase phase = BattlePhase.PlayerInput,
        int energy = 3,
        int energyMax = 3,
        ImmutableArray<BattleCardInstance>? hand = null,
        ImmutableArray<BattleCardInstance>? draw = null,
        ImmutableArray<BattleCardInstance>? discard = null,
        ImmutableArray<string>? ownedRelicIds = null,
        ImmutableArray<string>? potions = null)
    {
        return new BattleState(
            Turn: turn,
            Phase: phase,
            Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
            Allies: allies ?? ImmutableArray.Create(Hero()),
            Enemies: enemies ?? ImmutableArray.Create(Goblin()),
            TargetAllyIndex: 0,
            TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: energyMax,
            DrawPile: draw ?? ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand ?? ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: discard ?? ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            OwnedRelicIds: ownedRelicIds ?? ImmutableArray<string>.Empty,
            Potions: potions ?? ImmutableArray<string>.Empty,
            EncounterId: "enc_test");
    }

    // ===== BattleCardInstance helpers =====

    public static BattleCardInstance MakeBattleCard(string defId, string instId, bool upgraded = false) =>
        new(instId, defId, upgraded, null);

    // ===== 10.5.F: BattleState helpers for engine tests =====

    /// <summary>Hero / Goblin / 空 piles の最小 state。</summary>
    public static BattleState MakeMinimalState() => MinimalState();

    /// <summary>指定 hero を含む最小 state。</summary>
    public static BattleState MakeStateWithHero(CombatActor hero) =>
        MinimalState(allies: ImmutableArray.Create(hero));

    /// <summary>指定 cardDefIds で hand を埋めた最小 state。InstanceId は連番。</summary>
    public static BattleState MakeStateWithHand(string[] cardDefIds)
    {
        var hand = ImmutableArray.CreateRange(
            cardDefIds.Select((id, i) => new BattleCardInstance($"{id}-h{i}", id, false, null)));
        return MinimalState(hand: hand);
    }

    /// <summary>指定 cardDefIds で draw pile を埋めた最小 state。先頭が top。</summary>
    public static BattleState MakeStateWithDrawPile(string[] cardDefIds)
    {
        var draw = ImmutableArray.CreateRange(
            cardDefIds.Select((id, i) => new BattleCardInstance($"{id}-d{i}", id, false, null)));
        return MinimalState(draw: draw);
    }

    /// <summary>指定 cardDefIds で discard pile を埋めた最小 state。</summary>
    public static BattleState MakeStateWithDiscardPile(string[] cardDefIds)
    {
        var discard = ImmutableArray.CreateRange(
            cardDefIds.Select((id, i) => new BattleCardInstance($"{id}-x{i}", id, false, null)));
        return MinimalState(discard: discard);
    }

    // ===== Status helpers =====

    /// <summary>actor に status を 1 つ追加した複製を返す。</summary>
    public static CombatActor WithStatus(CombatActor actor, string id, int amount) =>
        actor with { Statuses = actor.Statuses.SetItem(id, amount) };

    public static CombatActor WithStrength(CombatActor actor, int amount = 1) =>
        WithStatus(actor, "strength", amount);

    public static CombatActor WithDexterity(CombatActor actor, int amount = 1) =>
        WithStatus(actor, "dexterity", amount);

    public static CombatActor WithVulnerable(CombatActor actor, int amount = 1) =>
        WithStatus(actor, "vulnerable", amount);

    public static CombatActor WithWeak(CombatActor actor, int amount = 1) =>
        WithStatus(actor, "weak", amount);

    public static CombatActor WithPoison(CombatActor actor, int amount = 1) =>
        WithStatus(actor, "poison", amount);

    public static CombatActor WithOmnistrike(CombatActor actor, int amount = 1) =>
        WithStatus(actor, "omnistrike", amount);
}
