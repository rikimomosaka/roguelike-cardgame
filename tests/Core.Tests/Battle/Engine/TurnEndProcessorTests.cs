using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnEndProcessorTests
{
    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> hand,
        CombatActor? hero = null,
        CombatActor? enemy = null)
    {
        hero ??= BattleFixtures.Hero();
        enemy ??= BattleFixtures.Goblin();
        return new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: ImmutableArray.Create(enemy),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: "enc_test");
    }

    [Fact] public void Resets_block_on_all_actors()
    {
        var hero = BattleFixtures.Hero() with { Block = BlockPool.Empty.Add(5) };
        var enemy = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(3) };
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty, hero, enemy);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Equal(BlockPool.Empty, next.Allies[0].Block);
        Assert.Equal(BlockPool.Empty, next.Enemies[0].Block);
    }

    [Fact] public void Resets_attack_pools_on_all_actors()
    {
        var hero = BattleFixtures.Hero() with {
            AttackSingle = AttackPool.Empty.Add(6),
            AttackAll    = AttackPool.Empty.Add(4) };
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty, hero);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Equal(AttackPool.Empty, next.Allies[0].AttackSingle);
        Assert.Equal(AttackPool.Empty, next.Allies[0].AttackRandom);
        Assert.Equal(AttackPool.Empty, next.Allies[0].AttackAll);
    }

    [Fact] public void Discards_all_hand_cards_to_discard_pile()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"));
        var s = MakeState(hand);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Empty(next.Hand);
        Assert.Equal(2, next.DiscardPile.Length);
        Assert.Equal(new[] { "c1", "c2" }, next.DiscardPile.Select(c => c.InstanceId).ToArray());
    }

    [Fact] public void No_events_emitted_in_phase_10_2_a()
    {
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty);
        var (_, events) = TurnEndProcessor.Process(s);
        Assert.Empty(events);
    }
}
