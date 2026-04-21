using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Map;

/// <summary>5 フェーズでダンジョンマップを生成する。フィールドを持たず stateless。呼び出しごとに <see cref="IRng"/> を受け取るため、DI で singleton 登録しても複数スレッドから安全に共用できる。</summary>
public sealed class DungeonMapGenerator : IDungeonMapGenerator
{
    public DungeonMap Generate(IRng rng, MapGenerationConfig config)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(config);

        string lastReason = "no-attempt";
        for (int attempt = 1; attempt <= config.MaxRegenerationAttempts; attempt++)
        {
            var nodes = PlaceNodes(rng, config);
            if (nodes.IsDefaultOrEmpty) { lastReason = "place-nodes-incompatible"; continue; }
            var withEdges = ConnectEdges(rng, config, nodes);
            if (withEdges.IsDefaultOrEmpty) { lastReason = "edge-candidates-empty"; continue; }

            var startId = withEdges.First(n => n.Row == 0).Id;
            var bossId = withEdges.First(n => n.Row == config.RowCount + 1).Id;

            var reachReason = ValidateAllNodesReachable(withEdges, startId, bossId);
            if (reachReason is not null) { lastReason = reachReason; continue; }

            if (HasCrossingEdges(withEdges)) { lastReason = "edge-crossing"; continue; }

            // 同一トポロジに対して AssignKinds を複数回試行する。PlaceNodes + ConnectEdges は
            // 比較的コストが高いが、Reachability + Crossing が通ったトポロジは種別割当だけの
            // ランダム性で config 制約を満たす確率が高い。Act1 のような厳しい path 制約
            // (Enemy 4-6/path, Rest 1-3/path) では 1 回の AssignKinds では probability が低いため、
            // 内側で 20 回ほど再試行してから外側 Generate ループに戻る。
            const int innerAssignAttempts = 20;
            for (int inner = 0; inner < innerAssignAttempts; inner++)
            {
                var assigned = AssignKinds(rng, config, withEdges);
                if (assigned.IsDefaultOrEmpty) { lastReason = "kind-candidates-empty"; continue; }

                var distReason = ValidateDistribution(assigned, config.TileDistribution);
                if (distReason is not null) { lastReason = distReason; continue; }

                var map = new DungeonMap(assigned, startId, bossId);
                var pathReason = ValidatePathConstraints(map, config.PathConstraints);
                if (pathReason is not null) { lastReason = pathReason; continue; }

                var dupReason = ValidateNoDuplicateSiblings(assigned);
                if (dupReason is not null) { lastReason = dupReason; continue; }

                return map;
            }
        }
        throw new MapGenerationException(config.MaxRegenerationAttempts, lastReason);
    }

    // 親 P の子たちが (Kind, OutgoingIds) で互いに区別できない場合、その親から見て選択肢が
    // 実質的に同一となり「どちらを選んでも同じ」という体験になる。これを禁止する。
    // 違反時は失敗理由文字列、成功時は null。
    private static string? ValidateNoDuplicateSiblings(ImmutableArray<MapNode> nodes)
    {
        foreach (var parent in nodes)
        {
            if (parent.OutgoingNodeIds.Length < 2) continue;
            var seen = new HashSet<string>();
            foreach (var childId in parent.OutgoingNodeIds)
            {
                var child = nodes[childId];
                string outKey = string.Join(",", child.OutgoingNodeIds.OrderBy(x => x));
                string key = $"{child.Kind}|{outKey}";
                if (!seen.Add(key))
                    return $"duplicate-siblings:parent={parent.Id}";
            }
        }
        return null;
    }

    // フェーズ 4.4：マップ全体分布検証。違反時は失敗理由文字列、成功時は null。
    private static string? ValidateDistribution(ImmutableArray<MapNode> nodes, TileDistributionRule rule)
    {
        var counts = nodes.GroupBy(n => n.Kind).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kv in rule.MinPerMap)
        {
            int c = counts.TryGetValue(kv.Key, out int v) ? v : 0;
            if (c < kv.Value) return $"distribution:{kv.Key}<{kv.Value}(got {c})";
        }
        foreach (var kv in rule.MaxPerMap)
        {
            int c = counts.TryGetValue(kv.Key, out int v) ? v : 0;
            if (c > kv.Value) return $"distribution:{kv.Key}>{kv.Value}(got {c})";
        }
        return null;
    }

    // フェーズ 4.5：ルート制約検証
    private static string? ValidatePathConstraints(DungeonMap map, PathConstraintRule rule)
    {
        foreach (var path in EnumeratePaths(map))
        {
            var counts = path.GroupBy(n => n.Kind).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kv in rule.PerPathCount)
            {
                int c = counts.TryGetValue(kv.Key, out int v) ? v : 0;
                if (c < kv.Value.Min) return $"path-constraint:{kv.Key}<{kv.Value.Min}(got {c})";
                if (c > kv.Value.Max) return $"path-constraint:{kv.Key}>{kv.Value.Max}(got {c})";
            }
            for (int i = 0; i < path.Count - 1; i++)
            {
                foreach (var pair in rule.ForbiddenConsecutive)
                {
                    if (path[i].Kind == pair.First && path[i + 1].Kind == pair.Second)
                        return $"forbidden-consecutive:{pair.First}->{pair.Second}";
                }
            }
        }
        return null;
    }

    // 反復 DFS + yield return。呼び出し側が早期 return した時点で走査が打ち切られるため、
    // 全パス数が指数的に増える設定でも制約違反が早く見つかるケースでは早期離脱できる。
    private static IEnumerable<IReadOnlyList<MapNode>> EnumeratePaths(DungeonMap map)
    {
        var path = new List<MapNode>();
        var childIndex = new Stack<int>();

        path.Add(map.GetNode(map.StartNodeId));
        childIndex.Push(0);

        while (path.Count > 0)
        {
            var node = path[^1];
            if (node.Id == map.BossNodeId)
            {
                yield return path.ToArray();
                path.RemoveAt(path.Count - 1);
                childIndex.Pop();
                continue;
            }

            int i = childIndex.Pop();
            if (i >= node.OutgoingNodeIds.Length)
            {
                path.RemoveAt(path.Count - 1);
                continue;
            }

            childIndex.Push(i + 1);
            int nextId = node.OutgoingNodeIds[i];
            path.Add(map.GetNode(nextId));
            childIndex.Push(0);
        }
    }

    // フェーズ 4.3：タイル種別割当
    private static ImmutableArray<MapNode> AssignKinds(
        IRng rng, MapGenerationConfig config, ImmutableArray<MapNode> nodes)
    {
        var kinds = new TileKind[nodes.Length];
        var assignedFlag = new bool[nodes.Length];

        // Start / Boss / Row 1 = Enemy（1 パスで Kind とフラグを同時に設定）
        foreach (var n in nodes)
        {
            if (n.Row == 0) { kinds[n.Id] = TileKind.Start; assignedFlag[n.Id] = true; }
            else if (n.Row == config.RowCount + 1) { kinds[n.Id] = TileKind.Boss; assignedFlag[n.Id] = true; }
            else if (n.Row == 1) { kinds[n.Id] = TileKind.Enemy; assignedFlag[n.Id] = true; }
        }

        // FixedRows（Row 1 と衝突するルールを書いた場合は FixedRows が優先される）
        foreach (var rule in config.FixedRows)
        {
            foreach (var n in nodes.Where(n => n.Row == rule.Row))
            {
                kinds[n.Id] = rule.Kind;
                assignedFlag[n.Id] = true;
            }
        }

        // カウンタ初期化
        var counts = new Dictionary<TileKind, int>();
        foreach (var k in System.Enum.GetValues<TileKind>()) counts[k] = 0;
        for (int i = 0; i < nodes.Length; i++)
            if (assignedFlag[i]) counts[kinds[i]]++;

        // MinPerMap を確実に満たすため、先に min 数ぶんだけ eligible な未割当ノードから
        // ランダムに予約する。Merchant min=max=3 のような厳しい分布で、重み付き random assign が
        // 確率的に足りないケースを排除する。
        foreach (var kv in config.TileDistribution.MinPerMap)
        {
            var kind = kv.Key;
            int need = kv.Value - counts[kind];
            if (need <= 0) continue;
            var eligible = nodes.Where(n => !assignedFlag[n.Id] && IsKindAllowed(kind, n, config)).ToList();
            if (eligible.Count < need) return ImmutableArray<MapNode>.Empty;
            for (int i = 0; i < need; i++)
            {
                int j = rng.NextInt(i, eligible.Count);
                (eligible[i], eligible[j]) = (eligible[j], eligible[i]);
            }
            foreach (var n in eligible.Take(need))
            {
                kinds[n.Id] = kind;
                assignedFlag[n.Id] = true;
                counts[kind]++;
            }
        }

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

    private static bool IsKindAllowed(TileKind kind, MapNode node, MapGenerationConfig config)
    {
        if (config.RowKindExclusions.Any(x => x.Row == node.Row && x.ExcludedKind == kind)) return false;
        if (kind == TileKind.Elite && node.Row < config.PathConstraints.MinEliteRow) return false;
        return true;
    }

    // フェーズ 4.2：エッジ貼り付け。srcs 列昇順に処理し、各 src は直前 src が選んだ
    // dst 最大列以降の dst からしか選ばない (非交差の構築時保証)。その後、入次数 0 の
    // dst を隣接 src から救済する (orphan 防止)。候補枯渇時は空配列を返し、上位 Generate
    // の retry loop が PlaceNodes を振り直す。
    //
    // この構築で以下を同時に保証:
    //   1. 全辺が ±1 列隣接。
    //   2. 非交差 (srcA.col < srcB.col なら max(srcA dsts) <= min(srcB dsts))。
    //   3. 全 dst に入次数 >= 1。
    //   4. 全 src に出次数 >= 1 (Phase 1 で各 src が候補枯渇時に失敗 = 出次数 0 を許さない)。
    private static ImmutableArray<MapNode> ConnectEdges(
        IRng rng, MapGenerationConfig config, ImmutableArray<MapNode> nodes)
    {
        var byRow = nodes.GroupBy(n => n.Row)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Column).ToList());
        int lastMiddleRow = config.RowCount;
        int bossRow = config.RowCount + 1;
        int bossId = byRow[bossRow][0].Id;

        var outgoing = new Dictionary<int, List<int>>();
        foreach (var n in nodes) outgoing[n.Id] = new List<int>();

        // Start → Row 1 全ノード (Start は中央列単一 src、扇状ファンアウト)
        int startId = byRow[0][0].Id;
        foreach (var r1 in byRow[1]) outgoing[startId].Add(r1.Id);

        for (int r = 1; r < lastMiddleRow; r++)
        {
            var srcs = byRow[r];
            var dsts = byRow[r + 1];

            int minDstCol = -1;
            foreach (var src in srcs)
            {
                var cands = dsts
                    .Where(d => System.Math.Abs(src.Column - d.Column) <= 1)
                    .Where(d => d.Column >= minDstCol)
                    .ToList();
                if (cands.Count == 0) return ImmutableArray<MapNode>.Empty;

                int degree = PickOutDegree(rng, config.EdgeWeights, cands.Count);
                // 同列 (delta=0) の dst を優先して選ぶことで斜め辺を減らしマップを読みやすくする。
                // degree=1 の場合は必ず同列を優先、degree>=2 の場合は同列+隣接を組み合わせる。
                var picked = PickDstsPreferStraight(rng, cands, src.Column, degree);
                foreach (var p in picked) outgoing[src.Id].Add(p.Id);
                minDstCol = picked.Max(p => p.Column);
            }

            var incoming = new HashSet<int>();
            foreach (var s in srcs)
                foreach (var dId in outgoing[s.Id]) incoming.Add(dId);
            foreach (var dst in dsts)
            {
                if (incoming.Contains(dst.Id)) continue;
                MapNode? rescue = null;
                foreach (var src in srcs)
                {
                    if (System.Math.Abs(src.Column - dst.Column) > 1) continue;
                    if (WouldCross(srcs, outgoing, src, dst, nodes)) continue;
                    rescue = src;
                    break;
                }
                if (rescue is null) return ImmutableArray<MapNode>.Empty;
                outgoing[rescue.Id].Add(dst.Id);
            }
        }

        foreach (var r15 in byRow[lastMiddleRow])
            outgoing[r15.Id].Add(bossId);

        return nodes
            .Select(n => n with
            {
                OutgoingNodeIds = outgoing[n.Id].OrderBy(i => i).ToImmutableArray(),
            })
            .ToImmutableArray();
    }

    private static bool WouldCross(
        List<MapNode> srcs,
        Dictionary<int, List<int>> outgoing,
        MapNode newSrc, MapNode newDst,
        ImmutableArray<MapNode> allNodes)
    {
        foreach (var s in srcs)
        {
            if (s.Column == newSrc.Column) continue;
            foreach (var exDstId in outgoing[s.Id])
            {
                var exDst = allNodes[exDstId];
                if (s.Column < newSrc.Column && exDst.Column > newDst.Column) return true;
                if (s.Column > newSrc.Column && exDst.Column < newDst.Column) return true;
            }
        }
        return false;
    }

    // 同列を最優先、次に ±1 隣接から選ぶ。|colDiff|=0 の dst に重み 3、|colDiff|=1 に重み 1 を
    // 与えた重み付き Fisher-Yates で先頭 degree 個を選出する。これで edge 分布が straight 寄りになる。
    private static List<MapNode> PickDstsPreferStraight(IRng rng, List<MapNode> cands, int srcCol, int degree)
    {
        double WeightOf(MapNode n) => System.Math.Abs(n.Column - srcCol) == 0 ? 3.0 : 1.0;
        var pool = new List<MapNode>(cands);
        var result = new List<MapNode>(degree);
        for (int i = 0; i < degree && pool.Count > 0; i++)
        {
            double total = pool.Sum(WeightOf);
            double r = rng.NextDouble() * total;
            double acc = 0;
            int pickIdx = pool.Count - 1;
            for (int j = 0; j < pool.Count; j++)
            {
                acc += WeightOf(pool[j]);
                if (r < acc) { pickIdx = j; break; }
            }
            result.Add(pool[pickIdx]);
            pool.RemoveAt(pickIdx);
        }
        return result;
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

    // 全ノードが「start から到達可能」かつ「boss に到達可能」であることを要求する。
    // いずれかに含まれないノードは orphan となり、プレイヤーが辿り着けない / 抜けられないマスになる。
    // 違反時は失敗理由文字列、成功時は null。
    private static string? ValidateAllNodesReachable(
        ImmutableArray<MapNode> nodes, int startId, int bossId)
    {
        var forward = ReachableSet(nodes, startId, reverse: false);
        if (!forward.Contains(bossId)) return "boss-unreachable";
        var backward = ReachableSet(nodes, bossId, reverse: true);
        foreach (var n in nodes)
        {
            if (!forward.Contains(n.Id)) return $"orphan-node:unreachable-from-start({n.Id})";
            if (!backward.Contains(n.Id)) return $"orphan-node:cannot-reach-boss({n.Id})";
        }
        return null;
    }

    private static HashSet<int> ReachableSet(ImmutableArray<MapNode> nodes, int rootId, bool reverse)
    {
        List<int>[]? incoming = null;
        if (reverse)
        {
            incoming = new List<int>[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) incoming[i] = new List<int>();
            foreach (var n in nodes)
                foreach (var dst in n.OutgoingNodeIds) incoming[dst].Add(n.Id);
        }
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(rootId);
        while (stack.Count > 0)
        {
            int id = stack.Pop();
            if (!visited.Add(id)) continue;
            IEnumerable<int> next = reverse ? incoming![id] : nodes[id].OutgoingNodeIds;
            foreach (var nxt in next) stack.Push(nxt);
        }
        return visited;
    }

    // 同じ行から次の行へ向かう 2 辺 (src_a→dst_a), (src_b→dst_b) について、
    // src_a.Column < src_b.Column かつ dst_a.Column > dst_b.Column（または逆）なら幾何的に交差する。
    // Start(row 0) / Boss(row N+1) は単一中央列なのでこの行の辺は交差し得ない。
    private static bool HasCrossingEdges(ImmutableArray<MapNode> nodes)
    {
        var byRow = nodes.GroupBy(n => n.Row).ToDictionary(g => g.Key, g => g.ToList());
        int maxRow = byRow.Keys.Max();
        for (int r = 0; r < maxRow; r++)
        {
            if (!byRow.TryGetValue(r, out var srcs)) continue;
            var edges = new List<(int SrcCol, int DstCol)>();
            foreach (var s in srcs)
                foreach (var dstId in s.OutgoingNodeIds)
                    edges.Add((s.Column, nodes[dstId].Column));
            for (int i = 0; i < edges.Count; i++)
            {
                for (int j = i + 1; j < edges.Count; j++)
                {
                    var a = edges[i];
                    var b = edges[j];
                    if (a.SrcCol == b.SrcCol) continue; // 同じソースは線分として交差しない
                    if (a.SrcCol < b.SrcCol && a.DstCol > b.DstCol) return true;
                    if (a.SrcCol > b.SrcCol && a.DstCol < b.DstCol) return true;
                }
            }
        }
        return false;
    }

    // フェーズ 4.1：ノード配置。連続する行が bipartite 隣接 (各 src は ±1 列に dst を 1 つ以上、
    // 各 dst は ±1 列に src を 1 つ以上) となるように列を選ぶ。これで ConnectEdges の monotonic
    // 非交差構築が edge-candidates-empty で失敗することがなくなる。
    //
    // Row 1 は前行が Start 中央列のみ (bipartite 不要) のため任意 k 列を選ぶ。
    // Row r+1 は Row r の列集合に対し bipartite を満たす k-subset を全列挙して一様ランダムに選ぶ。
    // 候補が存在しない行があれば空配列で PlaceNodes 失敗 (上位 Generate の retry loop が救済)。
    private static ImmutableArray<MapNode> PlaceNodes(IRng rng, MapGenerationConfig config)
    {
        var raw = new List<(int Row, int Column)>();
        raw.Add((0, config.ColumnCount / 2)); // Start：中央列（5 列なら列 2）

        int[]? prevCols = null;
        for (int r = 1; r <= config.RowCount; r++)
        {
            int kTarget = rng.NextInt(config.RowNodeCountMin, config.RowNodeCountMax + 1);
            int[]? chosen;
            if (prevCols is null)
            {
                var pool = Enumerable.Range(0, config.ColumnCount).ToList();
                for (int i = 0; i < kTarget; i++)
                {
                    int j = rng.NextInt(i, pool.Count);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }
                chosen = pool.Take(kTarget).OrderBy(c => c).ToArray();
            }
            else
            {
                chosen = TryPickBipartiteSubset(rng, config.ColumnCount, kTarget, prevCols);
                if (chosen is null)
                {
                    for (int kk = config.RowNodeCountMin; kk <= config.RowNodeCountMax; kk++)
                    {
                        if (kk == kTarget) continue;
                        chosen = TryPickBipartiteSubset(rng, config.ColumnCount, kk, prevCols);
                        if (chosen is not null) break;
                    }
                }
                if (chosen is null) return ImmutableArray<MapNode>.Empty;
            }

            foreach (var c in chosen) raw.Add((r, c));
            prevCols = chosen;
        }
        raw.Add((config.RowCount + 1, config.ColumnCount / 2));

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

    // [0, C) の k-subset のうち prevCols と bipartite 隣接を満たすものを全列挙し、
    // prevCols との列重なりを重視した重み付きランダムで 1 つ返す。
    // 重み = 10^overlap。これで「1 本道なのに斜めに曲がる」のようなパターンを大幅に抑制する
    // (overlap=k の subset は overlap=k-1 の subset より 10 倍選ばれやすい)。
    // C <= 8 程度なら subset 数は最大 C(C, k) <= 70 なので全列挙は十分軽量。
    private static int[]? TryPickBipartiteSubset(IRng rng, int C, int k, int[] prevCols)
    {
        var valid = new List<int[]>();
        var weights = new List<double>();
        var prevSet = new HashSet<int>(prevCols);
        var subset = new int[k];
        void Gen(int start, int depth)
        {
            if (depth == k)
            {
                foreach (var c in subset)
                {
                    if (!prevSet.Contains(c - 1) && !prevSet.Contains(c) && !prevSet.Contains(c + 1))
                        return;
                }
                var ss = new HashSet<int>(subset);
                foreach (var p in prevCols)
                {
                    if (!ss.Contains(p - 1) && !ss.Contains(p) && !ss.Contains(p + 1))
                        return;
                }
                int overlap = 0;
                foreach (var c in subset) if (prevSet.Contains(c)) overlap++;
                valid.Add(subset.ToArray());
                weights.Add(System.Math.Pow(10, overlap));
                return;
            }
            for (int v = start; v < C; v++)
            {
                subset[depth] = v;
                Gen(v + 1, depth + 1);
            }
        }
        Gen(0, 0);
        if (valid.Count == 0) return null;
        double total = 0;
        foreach (var w in weights) total += w;
        double r = rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < valid.Count; i++)
        {
            acc += weights[i];
            if (r < acc) return valid[i];
        }
        return valid[valid.Count - 1];
    }
}
