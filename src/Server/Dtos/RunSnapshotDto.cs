using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RunSnapshotDto(RunStateDto Run, MapDto Map);

public sealed record RunStateDto(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    IReadOnlyList<int> VisitedNodeIds,
    IReadOnlyDictionary<int, string> UnknownResolutions,
    string CharacterId,
    int CurrentHp, int MaxHp, int Gold,
    IReadOnlyList<string> Deck,
    IReadOnlyList<string> Potions,
    int PotionSlotCount,
    BattleStateDto? ActiveBattle,
    RewardStateDto? ActiveReward,
    IReadOnlyList<string> Relics,
    long PlaySeconds,
    string Progress,
    string SavedAtUtc);

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
                r.CardChoices, r.CardStatus.ToString());

        var resolutions = new Dictionary<int, string>();
        foreach (var kv in s.UnknownResolutions) resolutions[kv.Key] = kv.Value.ToString();

        var run = new RunStateDto(
            s.SchemaVersion, s.CurrentAct, s.CurrentNodeId, s.VisitedNodeIds, resolutions,
            s.CharacterId, s.CurrentHp, s.MaxHp, s.Gold,
            // TODO(G3): replace with CardInstanceDto; currently drops Upgraded flag
            s.Deck.Select(c => c.Id).ToArray(), s.Potions, s.PotionSlotCount,
            battle, reward, s.Relics, s.PlaySeconds, s.Progress.ToString(),
            s.SavedAtUtc.ToString("O"));
        return new RunSnapshotDto(run, MapDtoMapper.From(map));
    }
}

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
