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

            var startId = withEdges.First(n => n.Row == 0).Id;
            var bossId = withEdges.First(n => n.Row == config.RowCount + 1).Id;

            if (!IsBossReachable(withEdges, startId, bossId)) continue;

            var assigned = AssignKinds(rng, config, withEdges);
            if (assigned.IsDefaultOrEmpty) continue;

            // 暫定：MinPerMap 違反なら再試行（Task 8 で正式化）
            bool minOk = true;
            foreach (var kv in config.TileDistribution.MinPerMap)
            {
                if (assigned.Count(n => n.Kind == kv.Key) < kv.Value) { minOk = false; break; }
            }
            if (!minOk) continue;

            return new DungeonMap(assigned, startId, bossId);
        }
    }

    // フェーズ 4.3：タイル種別割当
    private static ImmutableArray<MapNode> AssignKinds(
        IRng rng, MapGenerationConfig config, ImmutableArray<MapNode> nodes)
    {
        var kinds = new TileKind[nodes.Length];

        // Start / Boss
        foreach (var n in nodes)
        {
            if (n.Row == 0) kinds[n.Id] = TileKind.Start;
            else if (n.Row == config.RowCount + 1) kinds[n.Id] = TileKind.Boss;
        }

        // Row 1 = Enemy（Start 直後固定）
        foreach (var n in nodes.Where(n => n.Row == 1))
            kinds[n.Id] = TileKind.Enemy;

        // FixedRows
        foreach (var rule in config.FixedRows)
            foreach (var n in nodes.Where(n => n.Row == rule.Row))
                kinds[n.Id] = rule.Kind;

        // default(TileKind) は Start なので、埋まっているかはフラグ配列で判別する
        var assignedFlag = new bool[nodes.Length];
        foreach (var n in nodes)
        {
            if (n.Row == 0 || n.Row == config.RowCount + 1 || n.Row == 1) assignedFlag[n.Id] = true;
        }
        foreach (var rule in config.FixedRows)
            foreach (var n in nodes.Where(n => n.Row == rule.Row))
                assignedFlag[n.Id] = true;

        // カウンタ初期化
        var counts = new Dictionary<TileKind, int>();
        foreach (var k in System.Enum.GetValues<TileKind>()) counts[k] = 0;
        for (int i = 0; i < nodes.Length; i++)
            if (assignedFlag[i]) counts[kinds[i]]++;

        foreach (var n in nodes.Where(n => !assignedFlag[n.Id]))
        {
            var candidates = new List<TileKind>();
            foreach (var k in new[] { TileKind.Enemy, TileKind.Elite, TileKind.Rest, TileKind.Merchant, TileKind.Unknown })
            {
                // 行ごとの除外
                if (config.RowKindExclusions.Any(x => x.Row == n.Row && x.ExcludedKind == k)) continue;
                // Elite の最小行
                if (k == TileKind.Elite && n.Row < config.PathConstraints.MinEliteRow) continue;
                // MaxPerMap に既に達している Kind
                if (config.TileDistribution.MaxPerMap.TryGetValue(k, out int max) && counts[k] >= max) continue;
                // BaseWeights にエントリがない Kind は 0 重み = 候補に入れない
                if (!config.TileDistribution.BaseWeights.TryGetValue(k, out double w) || w <= 0) continue;
                candidates.Add(k);
            }
            if (candidates.Count == 0)
                return ImmutableArray<MapNode>.Empty; // 生成失敗、再試行

            // 重み付き乱数
            double total = candidates.Sum(k => config.TileDistribution.BaseWeights[k]);
            double r = rng.NextDouble() * total;
            double acc = 0;
            TileKind picked = candidates[candidates.Count - 1];
            foreach (var k in candidates)
            {
                acc += config.TileDistribution.BaseWeights[k];
                if (r < acc) { picked = k; break; }
            }
            kinds[n.Id] = picked;
            counts[picked]++;
            assignedFlag[n.Id] = true;
        }

        return nodes.Select(n => n with { Kind = kinds[n.Id] }).ToImmutableArray();
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
