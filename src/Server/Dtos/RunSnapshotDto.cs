using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
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
    BattleStateDto? ActiveBattle,
    RewardStateDto? ActiveReward,
    MerchantInventoryDto? ActiveMerchant,
    IReadOnlyList<string> Relics,
    long PlaySeconds,
    string Progress,
    EventInstanceDto? ActiveEvent,
    bool ActiveRestPending,
    bool ActiveRestCompleted,
    string SavedAtUtc,
    ActStartRelicChoiceDto? ActiveActStartRelicChoice);

public sealed record EventChoiceSnapshotDto(string Label, string? ConditionSummary, bool ConditionMet);

public sealed record EventInstanceDto(
    string EventId,
    string Name,
    string Description,
    IReadOnlyList<EventChoiceSnapshotDto> Choices,
    int? ChosenIndex);

public static class RunSnapshotDtoMapper
{
    public static RunSnapshotDto From(RunState s, DungeonMap map, DataCatalog data)
    {
        BattleStateDto? battle = null;
        if (s.ActiveBattle is { } b)
        {
            var enemies = new List<EnemyInstanceDto>();
            foreach (var e in b.Enemies)
            {
                var def = data.Enemies[e.EnemyDefinitionId];
                enemies.Add(new EnemyInstanceDto(e.EnemyDefinitionId, def.Name, def.ImageId,
                    e.CurrentHp, e.MaxHp, e.CurrentMoveId));
            }
            battle = new BattleStateDto(b.EncounterId, enemies, b.Outcome.ToString());
        }

        RewardStateDto? reward = null;
        if (s.ActiveReward is { } r)
            reward = new RewardStateDto(r.Gold, r.GoldClaimed, r.PotionId, r.PotionClaimed,
                r.CardChoices, r.CardStatus.ToString(), r.RelicId, r.RelicClaimed, r.IsBossReward);

        MerchantInventoryDto? merchant = null;
        if (s.ActiveMerchant is { } m)
            merchant = MerchantInventoryDto.From(m);

        var resolutions = new Dictionary<int, string>();
        foreach (var kv in s.UnknownResolutions) resolutions[kv.Key] = kv.Value.ToString();

        EventInstanceDto? activeEvent = s.ActiveEvent is null
            ? null
            : EventInstanceDtoFactory.From(s.ActiveEvent, s, data);

        var run = new RunStateDto(
            s.SchemaVersion, s.CurrentAct, s.CurrentNodeId, s.VisitedNodeIds, resolutions,
            s.CharacterId, s.CurrentHp, s.MaxHp, s.Gold,
            s.Deck.Select(c => new CardInstanceDto(c.Id, c.Upgraded)).ToArray(),
            s.Potions, s.PotionSlotCount,
            battle, reward, merchant, s.Relics, s.PlaySeconds, s.Progress.ToString(),
            activeEvent, s.ActiveRestPending, s.ActiveRestCompleted,
            s.SavedAtUtc.ToString("O"),
            s.ActiveActStartRelicChoice is null ? null : new ActStartRelicChoiceDto(s.ActiveActStartRelicChoice.RelicIds));
        return new RunSnapshotDto(run, MapDtoMapper.From(map));
    }

    public static RunResultDto ToResult(string accountId, RunState s, int nodesVisited, RunProgress outcome)
    {
        var rec = RunHistoryBuilder.From(accountId, s, nodesVisited, outcome);
        return ToResultDto(rec);
    }

    public static RunResultDto ToResultDto(RunHistoryRecord rec)
    {
        var deck = new List<RunResultCardDto>();
        foreach (var c in rec.FinalDeck) deck.Add(new RunResultCardDto(c.Id, c.Upgraded));
        return new RunResultDto(
            rec.SchemaVersion, rec.AccountId, rec.RunId,
            rec.Outcome.ToString(), rec.ActReached, rec.NodesVisited,
            rec.PlaySeconds, rec.CharacterId, rec.FinalHp, rec.FinalMaxHp, rec.FinalGold,
            deck, System.Linq.Enumerable.ToList(rec.FinalRelics),
            rec.EndedAtUtc.ToString("O"));
    }
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
