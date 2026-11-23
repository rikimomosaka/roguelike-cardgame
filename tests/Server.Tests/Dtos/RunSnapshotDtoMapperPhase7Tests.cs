using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Dtos;

public class RunSnapshotDtoMapperPhase7Tests
{
    private static RunState FreshDefault(DataCatalog cat)
    {
        return RunState.NewSoloRun(
            cat,
            rngSeed: 0,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Maps_ActiveActStartRelicChoice_WhenPresent()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create("a", "b", "c")),
        };
        var map = new DungeonMap(
            StartNodeId: s.CurrentNodeId,
            BossNodeId: s.CurrentNodeId + 100,
            Nodes: ImmutableArray.Create(new MapNode(
                Id: s.CurrentNodeId, Row: 0, Column: 0,
                Kind: TileKind.Start,
                OutgoingNodeIds: ImmutableArray<int>.Empty)));
        var dto = RunSnapshotDtoMapper.From(s, map, cat);
        Assert.NotNull(dto.Run.ActiveActStartRelicChoice);
        Assert.Equal(new[] { "a", "b", "c" }, dto.Run.ActiveActStartRelicChoice!.RelicIds);
    }

    [Fact]
    public void Maps_authoritative_rest_heal_preview_with_relic_bonus()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = FreshDefault(cat) with
        {
            CurrentHp = 30,
            MaxHp = 80,
            Relics = new[] { "warm_blanket" },
            ActiveRestPending = true,
            ActiveRestCompleted = false,
        };
        var map = new DungeonMap(
            StartNodeId: s.CurrentNodeId,
            BossNodeId: s.CurrentNodeId + 100,
            Nodes: ImmutableArray.Create(new MapNode(
                Id: s.CurrentNodeId, Row: 0, Column: 0,
                Kind: TileKind.Rest,
                OutgoingNodeIds: ImmutableArray<int>.Empty)));

        var preview = RunSnapshotDtoMapper.From(s, map, cat).Run.RestHealPreview;

        Assert.NotNull(preview);
        Assert.Equal(30, preview!.CurrentHpBefore);
        Assert.Equal(64, preview.CurrentHpAfter);
        Assert.Equal(30, preview.BasePercent);
        Assert.Equal(10, preview.PassiveBonusAmount);
    }

    [Fact]
    public void Maps_reroll_available_only_for_class_card_rewards()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var classReward = new RewardState(
            Gold: 0,
            GoldClaimed: true,
            PotionId: null,
            PotionClaimed: true,
            CardChoices: ImmutableArray.Create("reward_common_01"),
            CardStatus: CardRewardStatus.Pending,
            CardPoolKind: CardRewardPoolKind.Class);
        var state = FreshDefault(cat) with
        {
            Relics = new[] { "magic_pouch" },
            ActiveReward = classReward,
        };
        var map = new DungeonMap(
            StartNodeId: state.CurrentNodeId,
            BossNodeId: state.CurrentNodeId + 100,
            Nodes: ImmutableArray.Create(new MapNode(
                Id: state.CurrentNodeId,
                Row: 0,
                Column: 0,
                Kind: TileKind.Enemy,
                OutgoingNodeIds: ImmutableArray<int>.Empty)));

        var classDto = RunSnapshotDtoMapper.From(state, map, cat);
        var universalDto = RunSnapshotDtoMapper.From(
            state with
            {
                ActiveReward = classReward with
                {
                    CardPoolKind = CardRewardPoolKind.Universal,
                },
            },
            map,
            cat);

        Assert.True(classDto.Run.ActiveReward!.RerollAvailable);
        Assert.False(universalDto.Run.ActiveReward!.RerollAvailable);
    }

    [Fact]
    public void Captures_latest_legacy_root_values_into_host_party_projection()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var original = FreshDefault(cat);
        var staleHost = Assert.Single(original.PartyMembers);
        var changed = original with
        {
            CurrentHp = original.CurrentHp - 7,
            Gold = original.Gold + 31,
        };
        Assert.NotEqual(changed.CurrentHp, staleHost.CurrentHp);
        Assert.NotEqual(changed.Gold, staleHost.Gold);
        var map = new DungeonMap(
            StartNodeId: changed.CurrentNodeId,
            BossNodeId: changed.CurrentNodeId + 100,
            Nodes: ImmutableArray.Create(new MapNode(
                Id: changed.CurrentNodeId,
                Row: 0,
                Column: 0,
                Kind: TileKind.Start,
                OutgoingNodeIds: ImmutableArray<int>.Empty)));

        var dto = RunSnapshotDtoMapper.From(changed, map, cat);

        var host = Assert.Single(dto.Run.PartyMembers);
        Assert.Equal(dto.Run.HostMemberId, host.MemberId);
        Assert.Equal(changed.CurrentHp, host.CurrentHp);
        Assert.Equal(changed.Gold, host.Gold);
        Assert.Equal(dto.Run.CurrentHp, host.CurrentHp);
        Assert.Equal(dto.Run.Gold, host.Gold);
    }
}
