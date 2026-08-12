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
    public void All_hit_finishes_all_target_events_before_OnEnemyDeath_events()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with
        {
            AttackAll = AttackPool.Empty.Add(100),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(
                BattleFixtures.Goblin(slotIndex: 0, hp: 5),
                BattleFixtures.Goblin(slotIndex: 1, hp: 5)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (_, events) = PlayerAttackingResolver.Resolve(state, MakeRng(), catalog);

        int lastHitEvent = events
            .Select((battleEvent, index) => (battleEvent, index))
            .Where(item => item.battleEvent.HitIndex == 0)
            .Max(item => item.index);
        int firstRelicEvent = events
            .Select((battleEvent, index) => (battleEvent, index))
            .First(item => item.battleEvent.Note?.Contains("relic:od") == true)
            .index;

        Assert.True(firstRelicEvent > lastHitEvent);
    }

    [Fact]
    public void OnEnemyDeath_buff_affects_the_next_All_hit_not_later_targets_of_the_same_hit()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("buff", EffectScope.Self, null, 10, Name: "strength"));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with
        {
            AttackAll = AttackPool.Empty.Add(5).Add(5),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(
                BattleFixtures.Goblin(slotIndex: 0, hp: 5),
                BattleFixtures.Goblin(slotIndex: 1, hp: 100)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, _) = PlayerAttackingResolver.Resolve(state, MakeRng(), catalog);

        // hit 0: 両方へ5。その後Strength +10。hit 1: 生存中のslot 1へ15。
        Assert.Equal(80, after.Enemies[1].CurrentHp);
        Assert.Equal(10, after.Allies[0].GetStatus("strength"));
    }

    [Fact]
    public void Attacker_death_after_an_All_hit_cancels_its_remaining_hits()
    {
        var relic = BattleFixtures.Relic("fatal_recoil", "OnEnemyDeath", true,
            new CardEffect("selfDamage", EffectScope.Self, null, 99));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with
        {
            AttackAll = AttackPool.Empty.Add(5).Add(5),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(
                BattleFixtures.Goblin(slotIndex: 0, hp: 5),
                BattleFixtures.Goblin(slotIndex: 1, hp: 100)),
            ownedRelicIds: ImmutableArray.Create("fatal_recoil"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, MakeRng(), catalog);

        Assert.False(after.Allies[0].IsAlive);
        // hit 0 は開始時点の両対象へ完了。死亡後の hit 1 は不発。
        Assert.Equal(95, after.Enemies[1].CurrentHp);
        var attackEvents = events
            .Where(e => e.Kind == BattleEventKind.AttackFire)
            .ToList();
        Assert.Equal(2, attackEvents.Count);
        Assert.All(attackEvents, e => Assert.Equal(0, e.HitIndex));
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
