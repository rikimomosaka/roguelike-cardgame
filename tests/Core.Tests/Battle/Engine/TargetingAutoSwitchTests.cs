using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TargetingAutoSwitchTests
{
    private static BattleState Make(int? tgtE, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerAttacking, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero()),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: tgtE,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
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

    [Fact] public void Dead_target_switches_to_innermost_alive()
    {
        var dead = BattleFixtures.Goblin(slotIndex: 0) with { CurrentHp = 0 };
        var alive1 = BattleFixtures.Goblin(slotIndex: 1, hp: 10);
        var alive2 = BattleFixtures.Goblin(slotIndex: 2, hp: 10);
        var s = Make(0, dead, alive1, alive2);
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Equal(1, next.TargetEnemyIndex);
    }

    [Fact] public void All_dead_sets_target_to_null()
    {
        var dead0 = BattleFixtures.Goblin(0) with { CurrentHp = 0 };
        var dead1 = BattleFixtures.Goblin(1) with { CurrentHp = 0 };
        var s = Make(0, dead0, dead1);
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Null(next.TargetEnemyIndex);
    }

    [Fact] public void Live_target_unchanged()
    {
        var s = Make(0, BattleFixtures.Goblin(0, 20));
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Equal(0, next.TargetEnemyIndex);
    }

    [Fact] public void Null_target_remains_null()
    {
        var s = Make(null, BattleFixtures.Goblin(0, 20));
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Null(next.TargetEnemyIndex);
    }

    [Fact]
    public void Dead_target_fallback_uses_array_index_not_slot_index()
    {
        var dead = BattleFixtures.Goblin(slotIndex: 4) with { CurrentHp = 0 };
        var firstLivingInArray = BattleFixtures.Goblin(slotIndex: 9, hp: 10);
        var innermostSlot = BattleFixtures.Goblin(slotIndex: 0, hp: 10);

        var next = TargetingAutoSwitch.Apply(
            Make(0, dead, firstLivingInArray, innermostSlot));

        Assert.Equal(1, next.TargetEnemyIndex);
        Assert.Equal(
            firstLivingInArray.InstanceId,
            next.Enemies[next.TargetEnemyIndex!.Value].InstanceId);
    }

    [Fact]
    public void Primary_and_secondary_dead_targets_switch_and_root_mirrors_primary()
    {
        var deadPrimary = BattleFixtures.Goblin(slotIndex: 7) with { CurrentHp = 0 };
        var firstLiving = BattleFixtures.Goblin(slotIndex: 9, hp: 10);
        var deadSecondary = BattleFixtures.Goblin(slotIndex: 0) with { CurrentHp = 0 };
        var laterLiving = BattleFixtures.Goblin(slotIndex: 1, hp: 10);
        var state = MakeParticipantState(
            [deadPrimary, firstLiving, deadSecondary, laterLiving],
            primaryTargetEnemyIndex: 0,
            secondaryTargetEnemyIndex: 2);

        var next = TargetingAutoSwitch.Apply(state);

        Assert.Equal(1, next.TargetEnemyIndex);
        Assert.Equal(1, next.Participants[0].TargetEnemyIndex);
        Assert.Equal(1, next.Participants[1].TargetEnemyIndex);
        Assert.Equal(
            firstLiving.InstanceId,
            next.Enemies[next.Participants[1].TargetEnemyIndex!.Value].InstanceId);
    }

    [Fact]
    public void Secondary_OnTurnStart_single_trigger_hits_its_switched_target()
    {
        var dead = BattleFixtures.Goblin(slotIndex: 8) with { CurrentHp = 0 };
        var survivor = BattleFixtures.Goblin(slotIndex: 3, hp: 20);
        var startPower = new CardDefinition(
            "secondary-start-target",
            "Secondary start target",
            null,
            CardRarity.Common,
            CardType.Power,
            Cost: 1,
            UpgradedCost: null,
            Effects:
            [
                new CardEffect(
                    "debuff", EffectScope.Single, EffectSide.Enemy, 1,
                    Name: "weak", Trigger: "OnTurnStart"),
            ],
            UpgradedEffects: null,
            Keywords: null);
        var state = MakeParticipantState(
            [dead, survivor],
            primaryTargetEnemyIndex: 0,
            secondaryTargetEnemyIndex: 0,
            secondaryPowers:
            [
                BattleFixtures.MakeBattleCard(
                    startPower.Id, "secondary-start-target-instance"),
            ]);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [BattleFixtures.Strike(), BattleFixtures.Defend(), startPower]);

        var (next, events) = TurnStartProcessor.Process(
            state,
            new FakeRng([], []),
            catalog);

        Assert.Equal(1, next.Participants[1].TargetEnemyIndex);
        var survivorAfter = next.Enemies[1];
        Assert.Equal(1, survivorAfter.GetStatus("weak"));
        Assert.Equal(0, next.Enemies[0].GetStatus("weak"));
        Assert.Contains(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ApplyStatus
            && battleEvent.CasterInstanceId == "wisp-target-owner"
            && battleEvent.TargetInstanceId == survivor.InstanceId);
    }

    private static BattleState MakeParticipantState(
        ImmutableArray<CombatActor> enemies,
        int primaryTargetEnemyIndex,
        int secondaryTargetEnemyIndex,
        ImmutableArray<BattleCardInstance> secondaryPowers = default)
    {
        var hero = BattleFixtures.Hero();
        var wisp = BattleFixtures.SummonActor(
            "wisp-target-owner", "wisp", slotIndex: 1, hp: 20);
        var state = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                allies: [hero, wisp],
                enemies: enemies,
                hand: [],
                draw: []) with
            {
                TargetEnemyIndex = primaryTargetEnemyIndex,
                DrawPerTurn = 0,
            });
        var primary = state.Participants[0] with
        {
            TargetEnemyIndex = primaryTargetEnemyIndex,
            DrawPerTurn = 0,
        };
        state = BattleParticipantAccess.Replace(state, primary);
        var secondary = primary with
        {
            ParticipantId = "cpu:wisp-target-owner",
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            CharacterId = "wisp",
            TargetAllyIndex = 1,
            TargetEnemyIndex = secondaryTargetEnemyIndex,
            Energy = 0,
            EnergyPerTurn = 0,
            DrawPerTurn = 0,
            PowerCards = secondaryPowers.IsDefault
                ? ImmutableArray<BattleCardInstance>.Empty
                : secondaryPowers,
        };
        return BattleParticipantAccess.Add(state, secondary);
    }
}
