using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// SummonCleanup の単体テスト。
/// 死亡 summon の AssociatedSummonHeldInstanceId を辿り、
/// 対応カードを SummonHeld → DiscardPile に移動する処理を検証する。
/// 親 spec §5-4 / Phase 10.2.D spec §4-4 参照。
/// </summary>
public class SummonCleanupTests
{
    private static BattleState MakeState(
        ImmutableArray<CombatActor> allies,
        ImmutableArray<BattleCardInstance> summonHeld) =>
        new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: allies,
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: summonHeld,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");

    [Fact]
    public void Dead_summon_moves_card_to_discard()
    {
        var hero = BattleFixtures.Hero();
        // 死亡 summon (HP=0) かつ associated card あり
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, associatedCardId: "card1")
            with { CurrentHp = 0 };
        var card = BattleFixtures.MakeBattleCard("minion_card", "card1");
        var s = MakeState(
            ImmutableArray.Create(hero, summon),
            ImmutableArray.Create(card));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Single(next.DiscardPile);
        Assert.Equal("card1", next.DiscardPile[0].InstanceId);
        // ally の AssociatedSummonHeldInstanceId が null 化されている
        Assert.DoesNotContain(next.Allies, a => a.InstanceId == "s1");
    }

    [Fact]
    public void Simultaneously_dead_ownership_group_returns_its_card_exactly_once()
    {
        var hero = BattleFixtures.Hero();
        var first = BattleFixtures.SummonActor(
                "group-1", "minion", 1, hp: 10,
                associatedCardId: "group-card")
            with { CurrentHp = 0 };
        var second = BattleFixtures.SummonActor(
                "group-2", "minion", 2, hp: 10,
                associatedCardId: "group-card")
            with { CurrentHp = 0 };
        var card = BattleFixtures.MakeBattleCard(
            "minion_card", "group-card");
        var state = MakeState(
            ImmutableArray.Create(hero, first, second),
            ImmutableArray.Create(card));
        var events = new List<BattleEvent>();
        int order = 0;

        var next = SummonCleanup.Apply(state, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Equal(
            "group-card",
            Assert.Single(next.DiscardPile).InstanceId);
        Assert.DoesNotContain(
            next.Allies,
            actor => actor.InstanceId is "group-1" or "group-2");
    }

    [Fact]
    public void Alive_summon_not_processed()
    {
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, associatedCardId: "card1");
        var card = BattleFixtures.MakeBattleCard("minion_card", "card1");
        var s = MakeState(
            ImmutableArray.Create(hero, summon),
            ImmutableArray.Create(card));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Single(next.SummonHeld);
        Assert.Empty(next.DiscardPile);
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Equal("card1", summonNext.AssociatedSummonHeldInstanceId);
    }

    [Fact]
    public void Dead_hero_not_processed()
    {
        // hero は AssociatedSummonHeldInstanceId が null（spec invariant）。
        // 仮に hero が死亡しても cleanup の対象にならない。
        var hero = BattleFixtures.Hero() with { CurrentHp = 0 };
        var s = MakeState(
            ImmutableArray.Create(hero),
            ImmutableArray<BattleCardInstance>.Empty);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Empty(next.DiscardPile);
        Assert.Equal(s, next);
    }

    [Fact]
    public void Already_cleaned_summon_not_reprocessed()
    {
        // 死亡しているが AssociatedSummonHeldInstanceId が既に null（過去 cleanup 済み）
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, associatedCardId: null)
            with { CurrentHp = 0 };
        var s = MakeState(
            ImmutableArray.Create(hero, summon),
            ImmutableArray<BattleCardInstance>.Empty);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Empty(next.DiscardPile);
        Assert.Equal(s, next);
    }

    [Fact]
    public void Multiple_dead_summons_all_processed()
    {
        var hero = BattleFixtures.Hero();
        var summon1 = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, associatedCardId: "card1")
            with { CurrentHp = 0 };
        var summon2 = BattleFixtures.SummonActor("s2", "minion", 2, hp: 10, associatedCardId: "card2")
            with { CurrentHp = 0 };
        var card1 = BattleFixtures.MakeBattleCard("minion_card", "card1");
        var card2 = BattleFixtures.MakeBattleCard("minion_card", "card2");
        var s = MakeState(
            ImmutableArray.Create(hero, summon1, summon2),
            ImmutableArray.Create(card1, card2));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Equal(2, next.DiscardPile.Length);
        var ids = next.DiscardPile.Select(c => c.InstanceId).ToList();
        Assert.Contains("card1", ids);
        Assert.Contains("card2", ids);
        Assert.DoesNotContain(next.Allies, a => a.InstanceId == "s1");
        Assert.DoesNotContain(next.Allies, a => a.InstanceId == "s2");
    }

    [Fact]
    public void Dead_summon_returns_held_card_to_the_owning_secondary_participant()
    {
        var hero = BattleFixtures.Hero();
        var cpuOwner = BattleFixtures.SummonActor("cpu-owner", "wisp", 1, hp: 30);
        var child = BattleFixtures.SummonActor(
                "cpu-child", "minion", 2, hp: 10, associatedCardId: "cpu-card")
            with { CurrentHp = 0 };
        var card = BattleFixtures.MakeBattleCard("minion_card", "cpu-card");
        var state = MakeState(
            ImmutableArray.Create(hero, cpuOwner, child),
            ImmutableArray<BattleCardInstance>.Empty);
        state = BattleParticipantAccess.InitializePrimary(state);
        var secondary = state.Participants[0] with
        {
            ParticipantId = "cpu:owner",
            ActorInstanceId = cpuOwner.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = cpuOwner.SlotIndex,
            CharacterId = cpuOwner.DefinitionId,
            DrawPile = ImmutableArray<BattleCardInstance>.Empty,
            Hand = ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile = ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile = ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld = ImmutableArray.Create(card),
            PowerCards = ImmutableArray<BattleCardInstance>.Empty,
        };
        state = BattleParticipantAccess.Add(state, secondary);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(state, events, ref order);

        var updatedSecondary = next.Participants.Single(participant =>
            participant.ParticipantId == secondary.ParticipantId);
        Assert.Empty(updatedSecondary.SummonHeld);
        Assert.Equal("cpu-card", Assert.Single(updatedSecondary.DiscardPile).InstanceId);
        Assert.Empty(next.SummonHeld);
        Assert.Empty(next.DiscardPile);
        Assert.DoesNotContain(next.Allies, actor =>
            actor.InstanceId == child.InstanceId);
        Assert.DoesNotContain(next.Participants, participant =>
            participant.ActorInstanceId == child.InstanceId);
    }

    [Fact]
    public void Dead_card_controlled_summon_releases_slot_and_removes_its_participant()
    {
        var hero = BattleFixtures.Hero();
        var wisp = BattleFixtures.SummonActor(
                "dead-wisp", "wisp", 1, hp: 30, associatedCardId: "summon-card")
            with { CurrentHp = 0 };
        var summonCard = BattleFixtures.MakeBattleCard(
            "starter_summon_3", "summon-card");
        var state = MakeState(
            ImmutableArray.Create(hero, wisp),
            ImmutableArray.Create(summonCard));
        state = BattleParticipantAccess.InitializePrimary(state);
        var cpu = state.Participants[0] with
        {
            ParticipantId = "cpu:dead-wisp",
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = wisp.SlotIndex,
            CharacterId = wisp.DefinitionId,
            DrawPile = ImmutableArray<BattleCardInstance>.Empty,
            Hand = ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile = ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile = ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld = ImmutableArray<BattleCardInstance>.Empty,
            PowerCards = ImmutableArray<BattleCardInstance>.Empty,
        };
        state = BattleParticipantAccess.Add(state, cpu);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(state, events, ref order);

        Assert.DoesNotContain(next.Allies, actor => actor.InstanceId == wisp.InstanceId);
        Assert.DoesNotContain(next.Participants, participant =>
            participant.ActorInstanceId == wisp.InstanceId);
        Assert.Equal("summon-card", Assert.Single(next.DiscardPile).InstanceId);
        Assert.Empty(next.SummonHeld);
        Assert.DoesNotContain(next.Allies, actor => actor.SlotIndex == 1);
    }

    [Fact]
    public void Removing_middle_summon_keeps_target_on_same_actor_and_remaps_array_index()
    {
        var hero = BattleFixtures.Hero();
        var dead = BattleFixtures.SummonActor(
                "dead-middle", "minion", 1, associatedCardId: "dead-card")
            with { CurrentHp = 0 };
        var originalTarget = BattleFixtures.SummonActor(
            "target-slot-2", "minion", 2, associatedCardId: "target-card");
        var state = MakeState(
                ImmutableArray.Create(hero, dead, originalTarget),
                ImmutableArray<BattleCardInstance>.Empty)
            with { TargetAllyIndex = 2 };
        state = BattleParticipantAccess.InitializePrimary(state);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(state, events, ref order);

        Assert.Equal(1, next.TargetAllyIndex);
        Assert.Equal(
            originalTarget.InstanceId,
            next.Allies[next.TargetAllyIndex!.Value].InstanceId);
        var primary = BattleParticipantAccess.GetPrimary(next);
        Assert.Equal(1, primary.TargetAllyIndex);
        Assert.Equal(
            originalTarget.InstanceId,
            next.Allies[primary.TargetAllyIndex!.Value].InstanceId);
    }

    [Fact]
    public void Removing_summon_remaps_secondary_human_participant_target()
    {
        var hero = BattleFixtures.Hero();
        var dead = BattleFixtures.SummonActor(
                "dead-middle", "minion", 1, associatedCardId: "dead-card")
            with { CurrentHp = 0 };
        var humanAlly = BattleFixtures.SummonActor(
            "human-ally", "future-human", 2);
        var state = MakeState(
            ImmutableArray.Create(hero, dead, humanAlly),
            ImmutableArray<BattleCardInstance>.Empty);
        state = BattleParticipantAccess.InitializePrimary(state);
        var secondary = state.Participants[0] with
        {
            ParticipantId = "player:1",
            ActorInstanceId = humanAlly.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Human,
            OwnerPlayerSlot = humanAlly.SlotIndex,
            CharacterId = humanAlly.DefinitionId,
            TargetAllyIndex = 2,
        };
        state = BattleParticipantAccess.Add(state, secondary);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(state, events, ref order);

        var updatedSecondary = next.Participants.Single(participant =>
            participant.ParticipantId == secondary.ParticipantId);
        Assert.Equal(1, updatedSecondary.TargetAllyIndex);
        Assert.Equal(
            humanAlly.InstanceId,
            next.Allies[updatedSecondary.TargetAllyIndex!.Value].InstanceId);
        Assert.Equal(
            next.Participants[0].TargetAllyIndex,
            next.TargetAllyIndex);
    }

    [Fact]
    public void Removed_target_falls_back_to_living_ally_with_lowest_slot()
    {
        var hero = BattleFixtures.Hero(slotIndex: 5);
        var deadTarget = BattleFixtures.SummonActor(
                "dead-target", "minion", 1, associatedCardId: "dead-card")
            with { CurrentHp = 0 };
        var lowestSlot = BattleFixtures.SummonActor(
            "fallback-slot-2", "minion", 2, associatedCardId: "live-card");
        var state = MakeState(
                ImmutableArray.Create(hero, deadTarget, lowestSlot),
                ImmutableArray<BattleCardInstance>.Empty)
            with { TargetAllyIndex = 1 };
        state = BattleParticipantAccess.InitializePrimary(state);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(state, events, ref order);

        Assert.Equal(1, next.TargetAllyIndex);
        Assert.Equal(
            lowestSlot.InstanceId,
            next.Allies[next.TargetAllyIndex!.Value].InstanceId);
        Assert.Equal(
            next.Participants[0].TargetAllyIndex,
            next.TargetAllyIndex);
    }
}
