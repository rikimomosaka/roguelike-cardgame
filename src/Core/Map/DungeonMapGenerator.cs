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
        while (true)
        {
            var nodes = PlaceNodes(rng, config);
            var withEdges = ConnectEdges(rng, config, nodes);
            if (withEdges.IsDefaultOrEmpty) continue;

            var withKinds = withEdges
                .Select(n => n with
                {
                    Kind = n.Row == 0 ? TileKind.Start
                        : n.Row == config.RowCount + 1 ? TileKind.Boss
                        : TileKind.Enemy,
                })
                .ToImmutableArray();

            var startId = withKinds.First(n => n.Row == 0).Id;
            var bossId = withKinds.First(n => n.Row == config.RowCount + 1).Id;

            if (!IsBossReachable(withKinds, startId, bossId)) continue;

            return new DungeonMap(withKinds, startId, bossId);
        }
    }

    // フェーズ 4.2：エッジ貼り付け
    private static ImmutableArray<MapNode> ConnectEdges(
        IRng rng, MapGenerationConfig config, ImmutableArray<MapNode> nodes)
    {
        var byRow = nodes.GroupBy(n => n.Row).ToDictionary(g => g.Key, g => g.ToList());
        int lastMiddleRow = config.RowCount;
        int bossRow = config.RowCount + 1;
        int bossId = byRow[bossRow][0].Id;

        var outgoing = new Dictionary<int, List<int>>();
        foreach (var n in nodes) outgoing[n.Id] = new List<int>();

        // Start → Row 1 全ノード
        int startId = byRow[0][0].Id;
        foreach (var r1 in byRow[1]) outgoing[startId].Add(r1.Id);

        // Row 1..(lastMiddleRow-1) → Row r+1（±1 隣接）
        for (int r = 1; r < lastMiddleRow; r++)
        {
            foreach (var src in byRow[r])
            {
                var candidates = byRow[r + 1]
                    .Where(dst => System.Math.Abs(src.Column - dst.Column) <= 1)
                    .ToList();
                if (candidates.Count == 0)
                {
                    return ImmutableArray<MapNode>.Empty;
                }
                int d = PickOutDegree(rng, config.EdgeWeights, candidates.Count);
                for (int i = 0; i < d; i++)
                {
                    int j = rng.NextInt(i, candidates.Count);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }
                foreach (var dst in candidates.Take(d))
                    outgoing[src.Id].Add(dst.Id);
            }
        }

        // Row 15 → Boss
        foreach (var r15 in byRow[lastMiddleRow])
            outgoing[r15.Id].Add(bossId);

        return nodes
            .Select(n => n with
            {
                OutgoingNodeIds = outgoing[n.Id].OrderBy(i => i).ToImmutableArray(),
            })
            .ToImmutableArray();
    }

    private static int PickOutDegree(IRng rng, EdgeCountWeights weights, int maxCandidates)
    {
        double total = weights.Weight1 + weights.Weight2 + weights.Weight3;
        double r = rng.NextDouble() * total;
        int picked;
        if (r < weights.Weight1) picked = 1;
        else if (r < weights.Weight1 + weights.Weight2) picked = 2;
        else picked = 3;
        return System.Math.Min(picked, maxCandidates);
    }

    private static bool IsBossReachable(ImmutableArray<MapNode> nodes, int startId, int bossId)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(startId);
        while (stack.Count > 0)
        {
            int id = stack.Pop();
            if (!visited.Add(id)) continue;
            foreach (var n in nodes[id].OutgoingNodeIds) stack.Push(n);
        }
        return visited.Contains(bossId);
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

        // Id は Row 昇順 → 同一 Row 内は Column 昇順で割り当てる。
        // ループ順で Row 昇順にはなっているが、念のため明示的にソートする。
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
