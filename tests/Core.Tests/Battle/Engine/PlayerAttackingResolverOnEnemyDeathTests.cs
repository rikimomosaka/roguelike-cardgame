using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PlayerAttackingResolverOnEnemyDeathTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void Single_attack_killing_one_enemy_fires_OnEnemyDeath_once()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with {
            AttackSingle = AttackPool.Empty.Add(100), // overkill
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(BattleFixtures.Goblin(slotIndex: 0, hp: 5)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, MakeRng(), catalog);

        Assert.False(after.Enemies[0].IsAlive);
        Assert.Equal(1, after.Allies[0].Block.RawTotal);
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Single(relicEvs);
    }

    [Fact]
    public void All_attack_killing_three_enemies_fires_OnEnemyDeath_in_slot_order()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with {
            AttackAll = AttackPool.Empty.Add(100),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(
                BattleFixtures.Goblin(slotIndex: 0, hp: 5),
                BattleFixtures.Goblin(slotIndex: 1, hp: 5),
                BattleFixtures.Goblin(slotIndex: 2, hp: 5)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, MakeRng(), catalog);

        Assert.All(after.Enemies, e => Assert.False(e.IsAlive));
        // 3 回 fire
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Equal(3, relicEvs.Count);
        // slot 順 (内側→外側)
        Assert.Contains("deadEnemy:goblin_inst_0", relicEvs[0].Note);
        Assert.Contains("deadEnemy:goblin_inst_1", relicEvs[1].Note);
        Assert.Contains("deadEnemy:goblin_inst_2", relicEvs[2].Note);
        Assert.Equal(3, after.Allies[0].Block.RawTotal);
    }

    [Fact]
    public void Attack_killing_zero_enemies_fires_no_OnEnemyDeath()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with {
            AttackSingle = AttackPool.Empty.Add(2),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(BattleFixtures.Goblin(hp: 100)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, MakeRng(), catalog);

        Assert.True(after.Enemies[0].IsAlive);
        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.DoesNotContain(events, e => e.Note != null && e.Note.Contains("relic:od"));
    }

    [Fact]
    public void Already_dead_enemy_is_not_re_fired()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with {
            AttackAll = AttackPool.Empty.Add(100),
        };
        var deadEnemy = BattleFixtures.Goblin(slotIndex: 0, hp: 5) with { CurrentHp = 0 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(
                deadEnemy,
                BattleFixtures.Goblin(slotIndex: 1, hp: 5)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, MakeRng(), catalog);

        // 既に死んでた enemy は fire 対象外、生きてた slot=1 の方だけ fire
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Single(relicEvs);
        Assert.Contains("deadEnemy:goblin_inst_1", relicEvs[0].Note);
    }
}
