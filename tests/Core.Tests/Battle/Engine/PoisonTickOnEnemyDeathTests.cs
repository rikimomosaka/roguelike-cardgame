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

public class PoisonTickOnEnemyDeathTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void Enemy_dying_from_poison_fires_OnEnemyDeath()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        var poisonedEnemy = BattleFixtures.WithPoison(
            BattleFixtures.Goblin(slotIndex: 0, hp: 1), 5);
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(poisonedEnemy),
            ownedRelicIds: ImmutableArray.Create("od")) with { Turn = 1 };

        var (after, events) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        // poison 5 → enemy 死亡 → OnEnemyDeath 発火
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Single(relicEvs);
        Assert.Contains("deadEnemy:goblin_inst_0", relicEvs[0].Note);
    }

    [Fact]
    public void Hero_dying_from_poison_does_not_fire_OnEnemyDeath()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        var poisonedHero = BattleFixtures.WithPoison(
            BattleFixtures.Hero(hp: 1), 5);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(poisonedHero),
            ownedRelicIds: ImmutableArray.Create("od")) with { Turn = 1 };

        var (after, events) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Empty(relicEvs);
    }

    [Fact]
    public void Multiple_enemies_dying_simultaneously_fire_in_slot_order()
    {
        var relic = BattleFixtures.Relic("od", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        var e0 = BattleFixtures.WithPoison(BattleFixtures.Goblin(slotIndex: 0, hp: 1), 5);
        var e1 = BattleFixtures.WithPoison(BattleFixtures.Goblin(slotIndex: 1, hp: 1), 5);
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(e0, e1),
            ownedRelicIds: ImmutableArray.Create("od")) with { Turn = 1 };

        var (after, events) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        // 両方死亡 → Outcome=Victory に確定 (TurnStartProcessor 内 death detection で early return)
        // OnEnemyDeath fire は ApplyPoisonTick 中に発火するので Victory 確定前
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Equal(2, relicEvs.Count);
        Assert.Contains("deadEnemy:goblin_inst_0", relicEvs[0].Note);
        Assert.Contains("deadEnemy:goblin_inst_1", relicEvs[1].Note);
    }
}
