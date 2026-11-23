using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunPartyAccessTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewSoloRun_initializes_one_host_member()
    {
        var state = CreateParty(EmbeddedDataLoader.LoadCatalog(), "default");

        Assert.Equal("player:0", state.HostMemberId);
        var member = Assert.Single(state.PartyMembers);
        Assert.Equal("default", member.CharacterId);
        Assert.Equal(RunMemberControlMode.Human, member.ControlMode);
        Assert.Equal(state.Gold, member.Gold);
        Assert.Equal(state.Deck.AsEnumerable(), member.Deck.AsEnumerable());
    }

    [Fact]
    public void NewPartyRun_preserves_pick_order_and_independent_initial_values()
    {
        var catalog = CatalogWithCharacters("default", "guardian", "tactician", "enhancer");
        var state = CreateParty(catalog, "default", "guardian", "tactician", "enhancer");

        Assert.Equal(4, state.PartyMembers.Length);
        Assert.Equal(
            new[] { "default", "guardian", "tactician", "enhancer" },
            state.PartyMembers.Select(member => member.CharacterId));
        Assert.Equal(new[] { 0, 1, 2, 3 }, state.PartyMembers.Select(member => member.SlotIndex));
        Assert.Equal(RunMemberControlMode.Human, state.PartyMembers[0].ControlMode);
        Assert.All(state.PartyMembers.Skip(1), member =>
            Assert.Equal(RunMemberControlMode.Cpu, member.ControlMode));
        Assert.Equal(99, state.PartyMembers[0].Gold);
        Assert.Equal(100, state.PartyMembers[1].Gold);
        Assert.Equal(103, state.PartyMembers[1].MaxHp);
    }

    [Fact]
    public void NewPartyRun_rejects_invalid_selection()
    {
        var catalog = CatalogWithCharacters("default", "guardian", "tactician", "enhancer", "extra");
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateParty(catalog));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateParty(catalog, "default", "guardian", "tactician", "enhancer", "extra"));
        Assert.Throws<ArgumentException>(() => CreateParty(catalog, "default", "default"));
        Assert.Throws<InvalidOperationException>(() => CreateParty(catalog, "missing"));
    }

    [Fact]
    public void Project_and_commit_changes_only_the_selected_member_then_restores_host_projection()
    {
        var catalog = CatalogWithCharacters("default", "guardian");
        var state = CreateParty(catalog, "default", "guardian");

        var projected = RunPartyAccess.ProjectMemberToRoot(state, "player:1");
        Assert.Equal("guardian", projected.CharacterId);
        var committed = RunPartyAccess.CommitRootToMember(
            projected with { Gold = projected.Gold + 25 },
            "player:1");

        Assert.Equal("default", committed.CharacterId);
        Assert.Equal(99, committed.Gold);
        Assert.Equal(125, committed.PartyMembers[1].Gold);
        Assert.Equal(99, committed.PartyMembers[0].Gold);
    }

    [Fact]
    public void Projecting_another_member_first_captures_pending_host_root_changes()
    {
        var catalog = CatalogWithCharacters("default", "guardian");
        var state = CreateParty(catalog, "default", "guardian") with { Gold = 144 };

        var projected = RunPartyAccess.ProjectMemberToRoot(state, "player:1");
        var committed = RunPartyAccess.CommitRootToMember(
            projected with { Gold = 222 },
            "player:1");

        Assert.Equal(144, committed.Gold);
        Assert.Equal(144, committed.PartyMembers[0].Gold);
        Assert.Equal(222, committed.PartyMembers[1].Gold);
    }

    [Fact]
    public void Four_member_party_round_trips_with_stable_ownership()
    {
        var catalog = CatalogWithCharacters("default", "guardian", "tactician", "enhancer");
        var state = CreateParty(catalog, "default", "guardian", "tactician", "enhancer");

        var restored = RunStateSerializer.Deserialize(RunStateSerializer.Serialize(state));

        Assert.Equal("player:0", restored.HostMemberId);
        Assert.Equal(
            state.PartyMembers.Select(member => (
                member.MemberId,
                member.SlotIndex,
                member.CharacterId,
                member.ControlMode,
                member.Gold,
                member.MaxHp)),
            restored.PartyMembers.Select(member => (
                member.MemberId,
                member.SlotIndex,
                member.CharacterId,
                member.ControlMode,
                member.Gold,
                member.MaxHp)));
    }

    [Fact]
    public void Serialize_wraps_an_unknown_host_as_a_serializer_error()
    {
        var state = CreateParty(EmbeddedDataLoader.LoadCatalog(), "default") with
        {
            HostMemberId = "missing",
        };

        Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Serialize(state));
    }

    [Fact]
    public void Current_schema_requires_an_explicit_non_empty_party()
    {
        var state = CreateParty(EmbeddedDataLoader.LoadCatalog(), "default");
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            RunStateSerializer.Serialize(state))!.AsObject();
        json.Remove("partyMembers");

        Assert.Throws<RunStateSerializerException>(() =>
            RunStateSerializer.Deserialize(json.ToJsonString()));
    }

    [Fact]
    public void V7_json_migrates_legacy_owner_fields_into_player_zero()
    {
        var state = CreateParty(EmbeddedDataLoader.LoadCatalog(), "default") with
        {
            Gold = 321,
        };
        var currentJson = RunStateSerializer.Serialize(state);
        var v7 = System.Text.Json.Nodes.JsonNode.Parse(currentJson)!.AsObject();
        v7["schemaVersion"] = 7;
        v7.Remove("hostMemberId");
        v7.Remove("partyMembers");

        var migrated = RunStateSerializer.Deserialize(v7.ToJsonString());

        Assert.Equal(8, migrated.SchemaVersion);
        Assert.Equal("player:0", migrated.HostMemberId);
        var member = Assert.Single(migrated.PartyMembers);
        Assert.Equal(321, member.Gold);
        Assert.Equal(migrated.Deck.AsEnumerable(), member.Deck.AsEnumerable());
        Assert.Equal(migrated.Relics, member.Relics.AsEnumerable());
    }

    private static RunState CreateParty(DataCatalog catalog, params string[] ids) =>
        RunState.NewPartyRun(
            catalog,
            ids,
            rngSeed: 42,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: Now);

    private static DataCatalog CatalogWithCharacters(params string[] ids)
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var template = catalog.Characters["default"];
        var characters = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
        for (var i = 0; i < ids.Length; i++)
        {
            characters[ids[i]] = template with
            {
                Id = ids[i],
                Name = ids[i],
                MaxHp = 80 + (i * 23),
                StartingGold = 99 + i,
            };
        }
        return catalog with { Characters = characters };
    }
}
