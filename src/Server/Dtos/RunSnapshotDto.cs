using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Rest;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RunSnapshotDto(RunStateDto Run, MapDto Map);

public sealed record CardInstanceDto(string Id, bool Upgraded);

public sealed record RunStateDto(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    IReadOnlyList<int> VisitedNodeIds,
    IReadOnlyDictionary<int, string> UnknownResolutions,
    string CharacterId,
    int CurrentHp, int MaxHp, int Gold,
    IReadOnlyList<CardInstanceDto> Deck,
    IReadOnlyList<string> Potions,
    int PotionSlotCount,
    BattlePlaceholderStateDto? ActiveBattle,
    RewardStateDto? ActiveReward,
    MerchantInventoryDto? ActiveMerchant,
    IReadOnlyList<string> Relics,
    long PlaySeconds,
    string Progress,
    EventInstanceDto? ActiveEvent,
    bool ActiveRestPending,
    bool ActiveRestCompleted,
    string SavedAtUtc,
    ActStartRelicChoiceDto? ActiveActStartRelicChoice,
    RestHealPreviewDto? RestHealPreview,
    string HostMemberId,
    IReadOnlyList<RunPartyMemberStateDto> PartyMembers);

public sealed record RunPartyMemberStateDto(
    string MemberId,
    int SlotIndex,
    string CharacterId,
    string ControlMode,
    int CurrentHp,
    int MaxHp,
    int Gold,
    IReadOnlyList<CardInstanceDto> Deck,
    IReadOnlyList<string> Potions,
    int PotionSlotCount,
    IReadOnlyList<string> Relics,
    RewardStateDto? ActiveReward,
    MerchantInventoryDto? ActiveMerchant,
    EventInstanceDto? ActiveEvent,
    bool ActiveRestPending,
    bool ActiveRestCompleted,
    ActStartRelicChoiceDto? ActiveActStartRelicChoice,
    RestHealPreviewDto? RestHealPreview,
    int DiscardUsesSoFar);

public sealed record RestHealPreviewDto(
    int CurrentHpBefore,
    int MaxHpBefore,
    int BasePercent,
    int BaseHealAmount,
    int PassiveBonusAmount,
    int CurrentHpAfter,
    int MaxHpAfter);

public sealed record EventChoiceSnapshotDto(
    string Label,
    string? ConditionSummary,
    bool ConditionMet,
    string ResultMessage);

public sealed record EventInstanceDto(
    string EventId,
    string Name,
    string StartMessage,
    IReadOnlyList<EventChoiceSnapshotDto> Choices,
    int? ChosenIndex);

public static class RunSnapshotDtoMapper
{
    public static RunSnapshotDto From(RunState s, DungeonMap map, DataCatalog data)
    {
        // Legacy run actions still mutate the host projection on the root. Capture
        // that view before returning the authoritative member list in the same response.
        s = RunPartyAccess.CaptureRootIntoHost(s);

        BattlePlaceholderStateDto? battle = null;
        if (s.ActiveBattle is { } b)
        {
            var enemies = new List<PlaceholderEnemyInstanceDto>();
            foreach (var e in b.Enemies)
            {
                var def = data.Enemies[e.EnemyDefinitionId];
                enemies.Add(new PlaceholderEnemyInstanceDto(e.EnemyDefinitionId, def.Name, def.ImageId,
                    e.CurrentHp, e.MaxHp, e.CurrentMoveId));
            }
            battle = new BattlePlaceholderStateDto(b.EncounterId, enemies, b.Outcome.ToString());
        }

        var reward = ToRewardDto(s, data);
        var merchant = s.ActiveMerchant is { } m ? MerchantInventoryDto.From(m) : null;

        var resolutions = new Dictionary<int, string>();
        foreach (var kv in s.UnknownResolutions) resolutions[kv.Key] = kv.Value.ToString();

        var activeEvent = ToEventDto(s, data);
        var restHealPreview = ToRestHealPreviewDto(s, data);
        var partyMembers = RunPartyAccess.EffectiveMembers(s)
            .OrderBy(member => member.SlotIndex)
            .Select(member => ToPartyMemberDto(
                member,
                RunPartyAccess.ProjectMemberToRoot(s, member.MemberId),
                data))
            .ToArray();

        var run = new RunStateDto(
            s.SchemaVersion, s.CurrentAct, s.CurrentNodeId, s.VisitedNodeIds, resolutions,
            s.CharacterId, s.CurrentHp, s.MaxHp, s.Gold,
            s.Deck.Select(c => new CardInstanceDto(c.Id, c.Upgraded)).ToArray(),
            s.Potions, s.PotionSlotCount,
            battle, reward, merchant, s.Relics, s.PlaySeconds, s.Progress.ToString(),
            activeEvent, s.ActiveRestPending, s.ActiveRestCompleted,
            s.SavedAtUtc.ToString("O"),
            s.ActiveActStartRelicChoice is null ? null : new ActStartRelicChoiceDto(s.ActiveActStartRelicChoice.RelicIds),
            restHealPreview,
            RunPartyAccess.EffectiveHostMemberId(s),
            partyMembers);
        return new RunSnapshotDto(run, MapDtoMapper.From(map));
    }

    private static RunPartyMemberStateDto ToPartyMemberDto(
        RunPartyMemberState member,
        RunState memberView,
        DataCatalog data) =>
        new(
            member.MemberId,
            member.SlotIndex,
            member.CharacterId,
            member.ControlMode.ToString(),
            member.CurrentHp,
            member.MaxHp,
            member.Gold,
            member.Deck.Select(card => new CardInstanceDto(card.Id, card.Upgraded)).ToArray(),
            member.Potions,
            member.PotionSlotCount,
            member.Relics,
            ToRewardDto(memberView, data),
            member.ActiveMerchant is { } merchant ? MerchantInventoryDto.From(merchant) : null,
            ToEventDto(memberView, data),
            member.ActiveRestPending,
            member.ActiveRestCompleted,
            member.ActiveActStartRelicChoice is null
                ? null
                : new ActStartRelicChoiceDto(member.ActiveActStartRelicChoice.RelicIds),
            ToRestHealPreviewDto(memberView, data),
            member.DiscardUsesSoFar);

    private static RewardStateDto? ToRewardDto(RunState state, DataCatalog data)
    {
        if (state.ActiveReward is not { } reward) return null;
        bool rerollAvailable = reward.CardPoolKind == CardRewardPoolKind.Class
            && PassiveModifiers.HasPassiveCapability("rewardRerollAvailable", state, data);
        return new RewardStateDto(
            reward.Gold,
            reward.GoldClaimed,
            reward.PotionId,
            reward.PotionClaimed,
            reward.CardChoices,
            reward.CardStatus.ToString(),
            reward.RelicId,
            reward.RelicClaimed,
            reward.IsBossReward,
            reward.RerollUsed,
            rerollAvailable);
    }

    private static EventInstanceDto? ToEventDto(RunState state, DataCatalog data) =>
        state.ActiveEvent is null
            ? null
            : EventInstanceDtoFactory.From(state.ActiveEvent, state, data);

    private static RestHealPreviewDto? ToRestHealPreviewDto(
        RunState state,
        DataCatalog data)
    {
        if (!state.ActiveRestPending || state.ActiveRestCompleted) return null;
        var preview = RestActions.PreviewHeal(state, data);
        return new RestHealPreviewDto(
            preview.CurrentHpBefore,
            preview.MaxHpBefore,
            preview.BasePercent,
            preview.BaseHealAmount,
            preview.PassiveBonusAmount,
            preview.CurrentHpAfter,
            preview.MaxHpAfter);
    }

    public static RunResultDto ToResult(string accountId, RunState s, DungeonMap currentMap, RunProgress outcome)
    {
        var rec = RunHistoryBuilder.From(accountId, s, currentMap, outcome);
        return ToResultDto(rec);
    }

    public static RunResultDto ToResultDto(RunHistoryRecord rec)
    {
        var deck = new List<RunResultCardDto>();
        foreach (var c in rec.FinalDeck) deck.Add(new RunResultCardDto(c.Id, c.Upgraded));
        var journey = new List<RunResultJourneyEntryDto>();
        if (!rec.JourneyLog.IsDefault)
            foreach (var j in rec.JourneyLog)
                journey.Add(new RunResultJourneyEntryDto(j.Act, j.NodeId, j.Kind.ToString(), j.ResolvedKind?.ToString()));
        return new RunResultDto(
            rec.SchemaVersion, rec.AccountId, rec.RunId,
            rec.Outcome.ToString(), rec.ActReached, rec.NodesVisited,
            rec.PlaySeconds, rec.CharacterId, rec.FinalHp, rec.FinalMaxHp, rec.FinalGold,
            deck, System.Linq.Enumerable.ToList(rec.FinalRelics),
            rec.EndedAtUtc.ToString("O"),
            SafeList(rec.SeenCardBaseIds),
            SafeList(rec.AcquiredRelicIds),
            SafeList(rec.AcquiredPotionIds),
            SafeList(rec.EncounteredEnemyIds),
            journey);
    }

    private static IReadOnlyList<string> SafeList(System.Collections.Immutable.ImmutableArray<string> arr)
        => arr.IsDefault ? System.Array.Empty<string>() : (IReadOnlyList<string>)arr.ToArray();
}

public sealed record MerchantInventoryDto(
    IReadOnlyList<MerchantOfferDto> Cards,
    IReadOnlyList<MerchantOfferDto> Relics,
    IReadOnlyList<MerchantOfferDto> Potions,
    bool DiscardSlotUsed,
    int DiscardPrice)
{
    public static MerchantInventoryDto From(MerchantInventory inv)
    {
        var cards = inv.Cards.Select(o => new MerchantOfferDto(o.Kind, o.Id, o.Price, o.Sold)).ToArray();
        var relics = inv.Relics.Select(o => new MerchantOfferDto(o.Kind, o.Id, o.Price, o.Sold)).ToArray();
        var potions = inv.Potions.Select(o => new MerchantOfferDto(o.Kind, o.Id, o.Price, o.Sold)).ToArray();
        return new MerchantInventoryDto(cards, relics, potions, inv.DiscardSlotUsed, inv.DiscardPrice);
    }
}

public sealed record MerchantOfferDto(
    string Kind,
    string Id,
    int Price,
    bool Sold);

public sealed record MapDto(int StartNodeId, int BossNodeId, IReadOnlyList<MapNodeDto> Nodes);
public sealed record MapNodeDto(int Id, int Row, int Column, TileKind Kind, IReadOnlyList<int> OutgoingNodeIds);

public static class MapDtoMapper
{
    public static MapDto From(DungeonMap map)
    {
        var nodes = new List<MapNodeDto>(map.Nodes.Length);
        foreach (var n in map.Nodes)
            nodes.Add(new MapNodeDto(n.Id, n.Row, n.Column, n.Kind, n.OutgoingNodeIds.ToArray()));
        return new MapDto(map.StartNodeId, map.BossNodeId, nodes);
    }
}
