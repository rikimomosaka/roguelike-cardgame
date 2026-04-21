using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Map;

/// <summary>5 フェーズでダンジョンマップを生成する。本クラスは stateless・single-thread 前提。</summary>
public sealed class DungeonMapGenerator : IDungeonMapGenerator
{
    public DungeonMap Generate(IRng rng, MapGenerationConfig config)
    {
        var nodes = PlaceNodes(rng, config);
        // エッジ貼り付け・種別割当は後続タスクで追加。
        // 現時点では Start/Boss の Kind だけ正しく、他は Enemy 暫定で埋める。
        var withKinds = nodes
            .Select(n => n with
            {
                Kind = n.Row == 0 ? TileKind.Start
                    : n.Row == config.RowCount + 1 ? TileKind.Boss
                    : TileKind.Enemy,
            })
            .ToImmutableArray();
        var startId = withKinds.First(n => n.Row == 0).Id;
        var bossId = withKinds.First(n => n.Row == config.RowCount + 1).Id;
        return new DungeonMap(withKinds, startId, bossId);
    }

    // フェーズ 4.1：ノード配置
    private static ImmutableArray<MapNode> PlaceNodes(IRng rng, MapGenerationConfig config)
    {
        var raw = new List<(int Row, int Column)>();
        raw.Add((0, config.ColumnCount / 2)); // Start：中央列（5 列なら列 2）

        for (int r = 1; r <= config.RowCount; r++)
        {
            int k = rng.NextInt(config.RowNodeCountMin, config.RowNodeCountMax + 1);
            var cols = Enumerable.Range(0, config.ColumnCount).ToList();
            // Fisher-Yates で前 k 個をランダム選択
            for (int i = 0; i < k; i++)
            {
                int j = rng.NextInt(i, cols.Count);
                (cols[i], cols[j]) = (cols[j], cols[i]);
            }
            foreach (var c in cols.Take(k).OrderBy(c => c))
                raw.Add((r, c));
        }
        raw.Add((config.RowCount + 1, config.ColumnCount / 2)); // Boss

        // Id は Row 昇順 → 同一 Row 内は Column 昇順（raw は既にその順）
        var ordered = raw.OrderBy(t => t.Row).ThenBy(t => t.Column).ToList();
        var builder = ImmutableArray.CreateBuilder<MapNode>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            builder.Add(new MapNode(
                Id: i,
                Row: ordered[i].Row,
                Column: ordered[i].Column,
                Kind: TileKind.Enemy, // 暫定。種別割当フェーズで上書き。
                OutgoingNodeIds: ImmutableArray<int>.Empty));
        }
        return builder.ToImmutable();
    }
}
