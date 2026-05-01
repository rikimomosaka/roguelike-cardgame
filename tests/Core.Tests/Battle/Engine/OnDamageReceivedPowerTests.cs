using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// 10.5.E: OnDamageReceived power が 3 経路で発火する統合テスト。
/// EnemyAttackingResolver / EffectApplier.ApplySelfDamage / TurnStartProcessor.ApplyPoisonTick.
/// </summary>
public class OnDamageReceivedPowerTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    private static CardDefinition DamageReceivedPower(string id, int blockAmount = 2) =>
        new(id, id, null, CardRarity.Common, CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, blockAmount, Trigger: "OnDamageReceived"),
            },
            UpgradedEffects: null, Keywords: null);

    [Fact]
    public void EnemyAttack_triggers_OnDamageReceived_power()
    {
        var powerDef = DamageReceivedPower("p_dmg");
        var instance = new BattleCardInstance("p_inst", "p_dmg", false, null);

        // Hero hp=20, enemy goblin attacks for 5
        var hero = BattleFixtures.Hero(hp: 20);
        var goblin = BattleFixtures.Goblin();
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(goblin),
            phase: BattlePhase.EnemyAttacking) with
            {
                PowerCards = ImmutableArray.Create(instance),
            };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = EnemyAttackingResolver.Resolve(state, MakeRng(), catalog);

        // hero に damage が入った (hp 減少)
        Assert.True(after.Allies[0].CurrentHp < 20);
        // OnDamageReceived power が block 2 を積んだ
        Assert.Equal(2, after.Allies[0].Block.RawTotal);
        // event Note に power:p_dmg
        Assert.Contains(events, e =>
            e.Kind == BattleEventKind.GainBlock
            && e.Note != null && e.Note.Contains("power:p_dmg"));
    }

    [Fact]
    public void EnemyAttack_with_block_absorbing_all_damage_does_not_fire_OnDamageReceived()
    {
        var powerDef = DamageReceivedPower("p_dmg");
        var instance = new BattleCardInstance("p_inst", "p_dmg", false, null);

        // Hero に大量の Block を持たせて、enemy の全 damage を吸収させる
        var hero = BattleFixtures.Hero(hp: 20) with { Block = BlockPool.Empty.Add(99) };
        var goblin = BattleFixtures.Goblin();
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(goblin),
            phase: BattlePhase.EnemyAttacking) with
            {
                PowerCards = ImmutableArray.Create(instance),
            };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = EnemyAttackingResolver.Resolve(state, MakeRng(), catalog);

        // hp 減らず (block で全吸収)
        Assert.Equal(20, after.Allies[0].CurrentHp);
        // OnDamageReceived は damage が 0 だったため発火せず — power の block (2) は積まれない
        // (元 99 から block 消費分のみ)
        Assert.DoesNotContain(events, e =>
            e.Note != null && e.Note.Contains("power:p_dmg"));
    }

    [Fact]
    public void PoisonTick_triggers_OnDamageReceived_power()
    {
        var powerDef = DamageReceivedPower("p_poison");
        var instance = new BattleCardInstance("p_inst", "p_poison", false, null);

        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(hp: 20), 3);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero)) with
            {
                PowerCards = ImmutableArray.Create(instance),
            };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        // poison で 3 damage 受けた
        Assert.True(after.Allies[0].CurrentHp < 20);
        // OnDamageReceived power が block 2 を積んだ
        Assert.True(after.Allies[0].Block.RawTotal >= 2);
        Assert.Contains(events, e =>
            e.Kind == BattleEventKind.GainBlock
            && e.Note != null && e.Note.Contains("power:p_poison"));
    }

    [Fact]
    public void SelfDamage_triggers_OnDamageReceived_power()
    {
        var powerDef = DamageReceivedPower("p_self");
        var instance = new BattleCardInstance("p_inst", "p_self", false, null);

        // selfDamage 5 を持つカードを定義
        var selfDmgCard = new CardDefinition(
            Id: "self_pain", Name: "self_pain", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("selfDamage", EffectScope.Self, null, 5),
            },
            UpgradedEffects: null, Keywords: null);

        var hero = BattleFixtures.Hero(hp: 30);
        var card = BattleFixtures.MakeBattleCard("self_pain", "c1");
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            hand: ImmutableArray.Create(card)) with
            {
                Energy = 1,
                PowerCards = ImmutableArray.Create(instance),
            };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { selfDmgCard, powerDef });

        var (after, events) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // hp 減 (5 damage)
        Assert.Equal(25, after.Allies[0].CurrentHp);
        // OnDamageReceived power が block 2 を積んだ
        Assert.Equal(2, after.Allies[0].Block.RawTotal);
        Assert.Contains(events, e =>
            e.Kind == BattleEventKind.GainBlock
            && e.Note != null && e.Note.Contains("power:p_self"));
    }

    [Fact]
    public void Hero_killed_by_damage_does_not_fire_OnDamageReceived()
    {
        var powerDef = DamageReceivedPower("p_lethal");
        var instance = new BattleCardInstance("p_inst", "p_lethal", false, null);

        // hero hp=1, selfDamage 5 で死亡 → OnDamageReceived は発火しない (caster 死亡)
        var selfDmgCard = new CardDefinition(
            Id: "lethal_pain", Name: "lethal_pain", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: new[] {
                new CardEffect("selfDamage", EffectScope.Self, null, 5),
            },
            UpgradedEffects: null, Keywords: null);

        var hero = BattleFixtures.Hero(hp: 1);
        var card = BattleFixtures.MakeBattleCard("lethal_pain", "c1");
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            hand: ImmutableArray.Create(card)) with
            {
                Energy = 1,
                PowerCards = ImmutableArray.Create(instance),
            };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { selfDmgCard, powerDef });

        var (after, events) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // hero 死亡
        Assert.False(after.Allies[0].IsAlive);
        // power 由来の event なし (hero 死亡 caster 不在)
        Assert.DoesNotContain(events, e =>
            e.Note != null && e.Note.Contains("power:p_lethal"));
    }
}
