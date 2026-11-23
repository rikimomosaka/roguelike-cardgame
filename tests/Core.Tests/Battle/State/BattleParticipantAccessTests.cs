using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattleParticipantAccessTests
{
    [Fact]
    public void Legacy_state_is_exposed_as_one_synthesized_primary_participant()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "strike-hand"));
        var state = BattleFixtures.MinimalState(energy: 7, hand: hand) with
        {
            ComboCount = 3,
            MaxComboThisTurn = 5,
            ComboGauge = 8,
        };

        var participants = BattleParticipantAccess.EffectiveParticipants(state);
        var primary = Assert.Single(participants);

        Assert.Empty(state.Participants);
        Assert.Equal(BattleParticipantAccess.DefaultPrimaryParticipantId, primary.ParticipantId);
        Assert.Equal("hero_inst", primary.ActorInstanceId);
        Assert.Equal(BattleParticipantControllerKind.Human, primary.ControllerKind);
        Assert.Equal(7, primary.Energy);
        Assert.Equal(hand, primary.Hand);
        Assert.Equal(3, primary.ComboCount);
        Assert.Equal(5, primary.MaxComboThisTurn);
        Assert.Equal(8, primary.ComboGauge);
        Assert.False(primary.IsReady);
    }

    [Fact]
    public void Replace_promotes_legacy_primary_and_synchronizes_root_mirror()
    {
        var state = BattleFixtures.MinimalState();
        var newHand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("defend", "defend-hand"));
        var updated = BattleParticipantAccess.GetPrimary(state) with
        {
            Energy = 9,
            Hand = newHand,
            ComboCount = 2,
            LastPlayedOrigCost = 1,
            TargetEnemyIndex = null,
            IsReady = true,
        };

        var result = BattleParticipantAccess.Replace(state, updated);

        Assert.Single(result.Participants);
        Assert.Equal(updated, result.Participants[0]);
        Assert.Equal(9, result.Energy);
        Assert.Equal(newHand, result.Hand);
        Assert.Equal(2, result.ComboCount);
        Assert.Equal(1, result.LastPlayedOrigCost);
        Assert.Null(result.TargetEnemyIndex);
        Assert.Equal(0, result.ActionRevision);
    }

    [Fact]
    public void Replacing_secondary_participant_does_not_change_primary_root_mirror()
    {
        var summon = BattleFixtures.SummonActor("wisp_inst", "wisp", slotIndex: 1);
        var state = BattleFixtures.MinimalState() with
        {
            Allies = ImmutableArray.Create(BattleFixtures.Hero(), summon),
        };
        state = BattleParticipantAccess.InitializePrimary(state);
        var primary = state.Participants[0];
        var secondary = primary with
        {
            ParticipantId = "cpu:wisp_inst",
            ActorInstanceId = summon.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = null,
            CharacterId = summon.DefinitionId,
            Energy = 2,
            Hand = ImmutableArray<BattleCardInstance>.Empty,
            ComboCount = 0,
            MaxComboThisTurn = 0,
            ComboGauge = 0,
            IsReady = false,
        };
        state = BattleParticipantAccess.Add(state, secondary);

        var changedSecondary = secondary with
        {
            Energy = 0,
            ComboCount = 4,
            IsReady = true,
        };
        var result = BattleParticipantAccess.Replace(state, changedSecondary);

        Assert.Equal(2, result.Participants.Length);
        Assert.Equal(changedSecondary, result.Participants[1]);
        Assert.Equal(primary.Energy, result.Energy);
        Assert.Equal(primary.ComboCount, result.ComboCount);
        Assert.Equal(primary.Hand, result.Hand);
        Assert.Equal(primary, result.Participants[0]);
    }

    [Fact]
    public void Add_rejects_a_second_participant_for_the_same_actor()
    {
        var state = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState());
        var duplicate = state.Participants[0] with
        {
            ParticipantId = "cpu:duplicate",
            ControllerKind = BattleParticipantControllerKind.Cpu,
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => BattleParticipantAccess.Add(state, duplicate));

        Assert.Contains("already has a battle participant", error.Message);
    }

    [Fact]
    public void Projection_bridge_commits_secondary_resources_and_restores_primary_root()
    {
        var summon = BattleFixtures.SummonActor("wisp_inst", "wisp", slotIndex: 1);
        var state = BattleFixtures.MinimalState(energy: 6) with
        {
            Allies = ImmutableArray.Create(BattleFixtures.Hero(), summon),
        };
        state = BattleParticipantAccess.InitializePrimary(state);
        var primary = state.Participants[0];
        var secondaryHand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "wisp-strike"));
        var secondary = primary with
        {
            ParticipantId = "cpu:wisp_inst",
            ActorInstanceId = summon.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = null,
            CharacterId = summon.DefinitionId,
            Energy = 2,
            Hand = secondaryHand,
            ComboCount = 1,
        };
        state = BattleParticipantAccess.Add(state, secondary);

        var projected = BattleParticipantAccess.ProjectToRoot(
            state, secondary.ParticipantId);

        Assert.Equal(2, projected.Energy);
        Assert.Equal(secondaryHand, projected.Hand);
        Assert.Equal("wisp", projected.CharacterId);
        Assert.Equal(state.Participants, projected.Participants);

        var processed = projected with
        {
            Turn = 2,
            Energy = 0,
            Hand = ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile = secondaryHand,
            ComboCount = 2,
        };
        var committed = BattleParticipantAccess.CommitRootToParticipant(
            processed, secondary.ParticipantId);

        Assert.Equal(2, committed.Turn);
        Assert.Equal(0, committed.Participants[1].Energy);
        Assert.Empty(committed.Participants[1].Hand);
        Assert.Equal(secondaryHand, committed.Participants[1].DiscardPile);
        Assert.Equal(2, committed.Participants[1].ComboCount);
        Assert.Equal(primary.Energy, committed.Energy);
        Assert.Equal(primary.Hand, committed.Hand);
        Assert.Equal(primary.CharacterId, committed.CharacterId);
    }

    [Fact]
    public void Legacy_json_without_participants_or_revision_restores_safe_defaults()
    {
        var json = JsonNode.Parse(
            JsonSerializer.Serialize(BattleFixtures.MinimalState()))!.AsObject();
        json.Remove(nameof(BattleState.Participants));
        json.Remove(nameof(BattleState.ActionRevision));

        var restored = JsonSerializer.Deserialize<BattleState>(json.ToJsonString());

        Assert.NotNull(restored);
        Assert.Empty(restored.Participants);
        Assert.Equal(0, restored.ActionRevision);
        Assert.Equal("hero_inst", BattleParticipantAccess.GetPrimary(restored).ActorInstanceId);
    }
}
