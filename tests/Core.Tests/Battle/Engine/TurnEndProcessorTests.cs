using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnEndProcessorTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[10], System.Array.Empty<double>());

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
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");
    }

    [Fact] public void Resets_block_on_allies_only()
    {
        // 仕様変更: TurnEndProcessor は ally の block のみリセットする。
        // 敵 block は EnemyAttackingResolver 内で「自分の次の move 実行直前」に
        // リセットするため、TurnEnd では保持される。
        // (block を「次の player turn の PlayerAttacking で消費」する設計のため)
        var hero = BattleFixtures.Hero() with { Block = BlockPool.Empty.Add(5) };
        var enemy = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(3) };
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty, hero, enemy);
        var (next, _) = TurnEndProcessor.Process(s, MakeRng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(BlockPool.Empty, next.Allies[0].Block);
        Assert.Equal(BlockPool.Empty.Add(3), next.Enemies[0].Block);
    }

    [Fact] public void Resets_attack_pools_on_all_actors()
    {
        var hero = BattleFixtures.Hero() with {
            AttackSingle = AttackPool.Empty.Add(6),
            AttackAll    = AttackPool.Empty.Add(4) };
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty, hero);
        var (next, _) = TurnEndProcessor.Process(s, MakeRng(), BattleFixtures.MinimalCatalog());
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
        var (next, _) = TurnEndProcessor.Process(s, MakeRng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Equal(2, next.DiscardPile.Length);
        Assert.Equal(new[] { "c1", "c2" }, next.DiscardPile.Select(c => c.InstanceId).ToArray());
    }

    [Fact]
    public void Primary_retains_wait_keyword_cards_by_the_same_rule_as_secondary_participants()
    {
        var wait = new CardDefinition(
            "wait", "Wait", null, CardRarity.Token, CardType.Skill,
            0, null, [], null, ["wait"]);
        var card = new BattleCardInstance("wait-instance", wait.Id, false, null);
        var state = MakeState([card]);
        var catalog = BattleFixtures.MinimalCatalog(cards: [wait]);

        var (next, _) = TurnEndProcessor.Process(state, MakeRng(), catalog);

        Assert.Equal(card, Assert.Single(next.Hand));
        Assert.Empty(next.DiscardPile);
    }

    [Fact]
    public void Turn_end_resets_the_primary_cpu_safety_count()
    {
        var state = BattleParticipantAccess.InitializePrimary(
            MakeState(ImmutableArray<BattleCardInstance>.Empty),
            controllerKind: BattleParticipantControllerKind.Cpu);
        var primary = BattleParticipantAccess.GetPrimary(state);
        state = BattleParticipantAccess.Replace(
            state,
            primary with
            {
                CpuCardPlaysThisTurn =
                    BattleCpuPlanningCoordinator.MaxPlaysPerActor,
            });

        var (next, _) = TurnEndProcessor.Process(
            state, MakeRng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, BattleParticipantAccess.GetPrimary(next)
            .CpuCardPlaysThisTurn);
    }

    [Fact] public void No_events_emitted_in_phase_10_2_a()
    {
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty);
        var (_, events) = TurnEndProcessor.Process(s, MakeRng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(events);
    }
}
