# Phase 3 — ダンジョンマップ生成 設計書

**日付:** 2026-04-21
**スコープ:** 1 Act 分のダンジョンマップを決定論的に生成する Core ライブラリの実装。生成結果の受け渡し API、JSON 設定、乱数抽象化、制約検証、再生成ロジックまで。
**前提タグ:** `phase2-complete`。

---

## 1. スコープとゴール

**含む**
- `src/Core/Map/` 配下にマップ生成 API 一式を新設
- `IRng` 乱数抽象化（Core 共通、以後のフェーズでも再利用）
- 固定 5 列グリッド・±1 隣接エッジのマップ構造
- タイル種別（Enemy / Elite / Rest / Merchant / Treasure / Unknown / Start / Boss）の分布
- 1 ルートあたりの制約検証（敵 4–6 マス、? 3–5 マス、商人 最大 2、連続 Rest 禁止、等）
- 制約違反時の「全生成やり直し」方式での再試行（最大回数で例外）
- JSON で差し替え可能な `MapGenerationConfig`（Act 差分は呼び出し側が config を切り替える）
- xUnit テスト：決定性、エッジ規則、分布、制約違反シナリオ、再生成上限

**含まない（後続フェーズ）**
- マップ UI（Phase 4 以降。クライアント側の SVG 描画等は別設計書）
- 実際のバトル／敵湧き（Phase 5 以降）
- Act 2／Act 3 用 config の作成と Act 進行のつなぎ込み（Phase 3 では Act 1 相当 config 1 本のみ）
- プレイヤーのマップ踏破状態（どのノードまで進んだか）の保存 — Phase 4 以降
- ボス選出の詳細ロジック（Phase 3 では TileKind.Boss のマスを 1 つ置くだけ）

**運用上の到達点**
- `DungeonMapGenerator.Generate(IRng, MapGenerationConfig)` を呼ぶだけで完結したマップが返る
- 同じ seed + 同じ config → 常に同じマップ（決定論）
- JSON を書き換えるだけで「1 ルートでの敵の本数」「エッジ本数比率」などが調整できる

---

## 2. トポロジー

### 2.1 固定 5 列グリッド

- 行 (Row) は `0..16` の 17 行。
  - Row 0: Start 1 個（列 2 固定）
  - Row 1..15: 中間 15 行（各行 0..N 個の通常ノードを列 0..4 のいずれかに配置）
  - Row 16: Boss 1 個（列 2 固定）
- 列 (Column) は全行で共通の `0..4` の 5 列。
- 各中間行のノード数は config の範囲で乱数決定（デフォルト 2–4 個）。
- ノード Id は生成後に 0 から連番で振る（配列 index と一致）。

### 2.2 エッジ規則

- エッジは「行 `r` のノード → 行 `r+1` のノード」の一方向のみ。
- **隣接制約（Row 1..14 → Row 2..15 のエッジ）：** エッジで結べるのは `|src.Column - dst.Column| <= 1` のペアのみ（同列・左隣・右隣）。
- **例外 1（Start からの分岐）：** Row 0 (Start, 列 2) → Row 1 の全ノードへエッジを貼る（列制約は適用しない）。出次数は Row 1 のノード数に等しい。
- **例外 2（Boss への合流）：** Row 15 の全ノード → Row 16 (Boss, 列 2) へエッジを貼る（列制約は適用しない）。Row 15 の各ノードの出次数は 1 固定。
- 中間行 (Row 1..14) の出次数は 1..3。`EdgeCountWeights` により `1` が出やすく `3` が出づらくなる（デフォルト w1:w2:w3 = 82:16:2）。
- **到達性保証：** Row 0 から Row 16 まで辿れるパスが必ず存在する（ノード生成後のエッジ貼り付けフェーズで到達性を保証する）。

### 2.3 ノード数の扱い

- Row 1..15 の各行のノード数は `[RowNodeCountMin, RowNodeCountMax]` の一様乱数（デフォルト 2..4）。
- Row 9 と Row 15 は「そのルートで必ず 1 マス通る」必要があるため、行の全ノードが Treasure / Rest に固定される（種別固定、個数は他行と同様に乱数）。
- Row 14 は Rest を置かない（Row 15 と連続 Rest になるため）。

---

## 3. ドメインモデル（`src/Core/Map/`）

### 3.1 `TileKind`

```csharp
namespace RoguelikeCardGame.Core.Map;

public enum TileKind
{
    Start,
    Enemy,
    Elite,
    Rest,
    Merchant,
    Treasure,
    Unknown,
    Boss,
}
```

### 3.2 `MapNode`

```csharp
public sealed record MapNode(
    int Id,
    int Row,
    int Column,
    TileKind Kind,
    ImmutableArray<int> OutgoingNodeIds);
```

- `Id` はマップ内でユニーク。`DungeonMap.Nodes` の index と一致（`Nodes[i].Id == i`）。
- `OutgoingNodeIds` は昇順（決定性のため）。空配列のときは Boss ノードのみ許容。
- VR 移植ノート：record は `sealed class` に置換、`ImmutableArray<int>` は `int[]` に置換。

### 3.3 `DungeonMap`

```csharp
public sealed record DungeonMap(
    ImmutableArray<MapNode> Nodes,
    int StartNodeId,
    int BossNodeId)
{
    public MapNode GetNode(int id) => Nodes[id];
    public IEnumerable<MapNode> NodesInRow(int row);   // 単純に Where で実装
}
```

- `Nodes` は Row 昇順 → 同一 Row 内は Column 昇順で格納（= index 一致）。
- `NodesInRow` はテスト・デバッグ用の便宜メソッドで、性能は要求しない（最大でも 17 行 × 5 列 = 85 ノード程度なので O(n) で十分）。
- `DungeonMap` 自身には「生成ロジック」を持たせない（生成は Generator 側）。

### 3.4 `IRng` と `SystemRng` / `FakeRng`

```csharp
namespace RoguelikeCardGame.Core.Random;

public interface IRng
{
    int NextInt(int minInclusive, int maxExclusive);
    double NextDouble();   // [0.0, 1.0)
}

public sealed class SystemRng : IRng
{
    public SystemRng(int seed);
    // System.Random をラップ
}

public sealed class FakeRng : IRng
{
    // テスト用：int/double の事前シーケンスを順に返す
    public FakeRng(int[] intSequence, double[] doubleSequence);
}
```

- `IRng` は Core 共通抽象。Phase 3 以外（カード効果・敵 AI の乱数）でも再利用する前提で、最小 API に留める。
- `SystemRng` は `System.Random(seed)` を内部で保持。スレッドセーフ性は要求しない（Core は single-thread 前提）。
- `FakeRng` はテスト用。シーケンスを超えて呼ばれたら `InvalidOperationException`。
- VR 移植ノート：`IRng` は廃止し、`UdonSharp` 実装では `Random.Range` を直接使う。Phase 3 の `DungeonMapGenerator` は VR 側では「生成済み JSON を読み込む」運用に切り替わる見込み（生成をサーバ側のみで行う）。

### 3.5 `MapGenerationConfig` と付随 record

```csharp
namespace RoguelikeCardGame.Core.Map;

public sealed record MapGenerationConfig(
    int RowCount,                                   // 通常 15
    int ColumnCount,                                // 通常 5
    int RowNodeCountMin,                            // 通常 2
    int RowNodeCountMax,                            // 通常 4
    EdgeCountWeights EdgeWeights,
    TileDistributionRule TileDistribution,
    ImmutableArray<FixedRowRule> FixedRows,         // Row 9=全 Treasure, Row 15=全 Rest 等
    ImmutableArray<RowKindExclusion> RowKindExclusions, // Row 14 に Rest 禁止 等
    PathConstraintRule PathConstraints,
    int MaxRegenerationAttempts);                   // 通常 100

public sealed record EdgeCountWeights(
    double Weight1,   // 出次数 1 の重み
    double Weight2,   // 出次数 2 の重み
    double Weight3);  // 出次数 3 の重み

public sealed record TileDistributionRule(
    ImmutableDictionary<TileKind, double> BaseWeights,   // 割当時の重み（候補に対する相対値、合計は任意）
    ImmutableDictionary<TileKind, int> MinPerMap,        // マップ全体の最小個数（下回ったら再生成）
    ImmutableDictionary<TileKind, int> MaxPerMap);       // マップ全体の最大個数（超えたら再生成）

public sealed record FixedRowRule(
    int Row,
    TileKind Kind);   // その行の通常ノードを全てこの Kind にする

public sealed record RowKindExclusion(
    int Row,
    TileKind ExcludedKind);   // その行にこの Kind を置かない

public sealed record PathConstraintRule(
    ImmutableDictionary<TileKind, IntRange> PerPathCount,   // 1 ルートでの許容個数レンジ（キー欠落=制約なし）
    int MinEliteRow,                                        // Elite を置ける最小行（通常 6）
    ImmutableArray<TileKindPair> ForbiddenConsecutive);     // 「First → Second の順で隣接する」ことを禁止

public sealed record IntRange(int Min, int Max);

public sealed record TileKindPair(TileKind First, TileKind Second);
```

- 全て record（不変）。JSON から deserialize して使う。
- `EdgeCountWeights` は重みであって確率ではない（正規化は Generator 側で行う）。
- `TileDistribution` と `PathConstraints` は役割が違う：
  - `TileDistribution` = マップ全体（全ノード）での min/max
  - `PathConstraints` = 1 つのルート（start→boss の経路）で満たすべき min/max
- `MaxRegenerationAttempts` を超えても制約を満たせなければ `MapGenerationException` を投げる。

### 3.6 `DungeonMapGenerator`

```csharp
public interface IDungeonMapGenerator
{
    DungeonMap Generate(IRng rng, MapGenerationConfig config);
}

public sealed class DungeonMapGenerator : IDungeonMapGenerator
{
    public DungeonMap Generate(IRng rng, MapGenerationConfig config);
}

public sealed class MapGenerationException : Exception
{
    public int AttemptCount { get; }
    public string FailureReason { get; }   // "path-constraint:Enemy=7>6" など
    public MapGenerationException(int attemptCount, string failureReason);
    public MapGenerationException(int attemptCount, string failureReason, Exception inner);
}
```

---

## 4. 生成アルゴリズム

生成は以下の 6 段階で行う。いずれかの段階で制約違反が検出されたら最初からやり直し（試行回数をカウント）。

### 4.1 ノード配置フェーズ

1. Row 0 に Start ノード 1 個（列 2）を置く。
2. Row 1..RowCount (=15) の各行について：
   1. `rng.NextInt(RowNodeCountMin, RowNodeCountMax + 1)` で行内ノード数 `k` を決定。
   2. 列 0..ColumnCount-1 のうち `k` 個を**重複なく**選ぶ（`rng` で `Shuffle` し先頭 k 個、等）。
3. Row 16 に Boss ノード 1 個（列 2）を置く。
4. Id を 0 から振り直す（Row 昇順 → 同一 Row 内は Column 昇順）。

### 4.2 エッジ貼り付けフェーズ

1. Row 0 → Row 1：Start から Row 1 の全ノードへエッジを貼る（全点分岐）。
2. Row r = 1..RowCount-1 の各ノード `n`：
   1. `n` から接続可能な Row `r+1` のノード集合 `C` を列挙（`|n.Col - c.Col| <= 1` を満たすもの）。
   2. `C` が空なら **配置違反**としてやり直し。
   3. `EdgeCountWeights` に従って出次数 `d` を重み付き乱数で決定（ただし `d <= |C|` にクランプ）。
   4. `C` から重複なく `d` 個を選んで `OutgoingNodeIds` に設定。
3. Row RowCount = 15 の全ノード → Row 16 (Boss) に接続（Row 15 は FixedRow = Rest 固定）。
4. **到達性チェック**：Row 16 の Boss に Row 0 の Start から到達できない場合はやり直し。
   - 入次数 0 のノード（Row 1 以降で誰からも接続されていない）があってもそれ自体は NG ではない（生成アルゴリズム上は到達不能ノードが発生し得る）。ただし **制約検証では「生成したノードは必ず使用されるべき」は要求しない**。シンプルさを優先。

### 4.3 タイル種別割当フェーズ

1. Start → `TileKind.Start`、Boss → `TileKind.Boss` を設定。
2. Row 1 の全ノードを `TileKind.Enemy` に設定（Start 直後は必ず敵戦闘）。
3. `FixedRows` で指定された行（Row 9, Row 15）の全ノードを対応する `Kind` に設定。
4. 残りの中間ノードに種別を割り当てる：
   1. 候補 Kind = `{Enemy, Elite, Rest, Merchant, Unknown}` から次を差し引いた集合：
      - その行が `RowKindExclusions` に該当する Kind（Row 14 = Rest 除外）
      - `PathConstraints.MinEliteRow` 未満の行での Elite
      - マップ全体で `TileDistribution.MaxPerMap[kind]` に既に達している Kind
   2. 候補が空なら制約違反としてやり直し。
   3. 候補それぞれに対応する `TileDistribution.BaseWeights[kind]` を取得し、重み付き乱数で Kind を 1 つ選ぶ。
   4. 選んだ Kind を当該ノードに設定し、次のノードへ。

### 4.4 マップ全体分布検証フェーズ

- `TileDistribution.MinPerMap` / `MaxPerMap` を走査し、範囲外があれば **制約違反**としてやり直し。

### 4.5 ルート制約検証フェーズ

- `start → boss` の全ルート（DFS で列挙）を走査。
- 各ルートについて：
  - Kind 別カウントが `PathConstraints.PerPathCount[kind]` の `[Min, Max]` に入っているか検証。
  - `ForbiddenConsecutive` に該当する連続が無いか検証。
- どれか 1 ルートでも違反 → やり直し。

ルート数は最大でも数千オーダー（5 列 × 15 行 × エッジ 1–3）。列挙は DFS で十分。

### 4.6 再生成上限

- 上記いずれかで試行が失敗したら `attempts++` し、`attempts >= MaxRegenerationAttempts` なら `MapGenerationException` を投げる。
- 失敗理由（どの制約で落ちたか）の最後の 1 件を `FailureReason` に格納（デバッグ用）。

---

## 5. 既定値（Phase 3 の Act 1 想定 config）

```jsonc
{
  "rowCount": 15,
  "columnCount": 5,
  "rowNodeCountMin": 2,
  "rowNodeCountMax": 4,
  "edgeWeights": { "weight1": 82, "weight2": 16, "weight3": 2 },
  "tileDistribution": {
    "baseWeights": {
      "enemy": 45,
      "elite": 6,
      "rest": 12,
      "merchant": 5,
      "unknown": 32
    },
    "minPerMap": { "merchant": 3, "elite": 2, "unknown": 6 },
    "maxPerMap": { "merchant": 3, "elite": 4, "unknown": 10 }
  },
  "fixedRows": [
    { "row": 9, "kind": "treasure" },
    { "row": 15, "kind": "rest" }
  ],
  "rowKindExclusions": [
    { "row": 14, "excludedKind": "rest" }
  ],
  "pathConstraints": {
    "perPathCount": {
      "enemy":    { "min": 4, "max": 6 },
      "elite":    { "min": 0, "max": 2 },
      "rest":     { "min": 1, "max": 3 },
      "merchant": { "min": 1, "max": 2 },
      "treasure": { "min": 1, "max": 1 },
      "unknown":  { "min": 3, "max": 5 }
    },
    "minEliteRow": 6,
    "forbiddenConsecutive": [
      { "first": "rest", "second": "rest" }
    ]
  },
  "maxRegenerationAttempts": 100
}
```

- `treasure` の per-path min/max が共に 1 なのは Row 9 に必ず宝箱があり、かつ行内の全ノードが Treasure なのでルート上常に 1 つ踏むため。
- `rest` の min が 1 なのは Row 15（休憩固定）があるため（1 ルートでも必ず 1 回は休憩を通る）。
- `merchant` の per-path max が 2 は、3 個配置しても 1 ルートで最大 2 個までしか踏めない配置になることを保証する制約（sample map v2 で検証済み）。
- この JSON は `src/Core/Map/Config/map-act1.json` に埋め込みリソースとして同梱し、`MapGenerationConfigLoader` で読み込む（`Phase 2 の EmbeddedDataLoader` と同系統）。

---

## 6. サーバ／クライアント連携（Phase 3 のスコープ）

Phase 3 では **マップ生成 Core 単体** に閉じ、Server／Client への露出は最小限。

- Server：新規 API は作らない。`Program.cs` の DI に `services.AddSingleton<IDungeonMapGenerator, DungeonMapGenerator>()` を追加するだけ（Phase 4 で API／SignalR イベントを追加する際の下準備）。`MapGenerationConfig` は起動時に埋め込み JSON からロードして Singleton 登録。
- Client：影響なし。
- 統合テスト（Server.Tests）は Phase 3 では不要。

---

## 7. テスト戦略（`tests/Core.Tests/Map/`）

### 7.1 決定論

- 同じ seed + 同じ config で同じ `DungeonMap` が返る（ノード配列を直列化して比較）。
- 異なる seed では結果が異なる（sanity check）。

### 7.2 トポロジー不変

- 全エッジが `|col diff| <= 1` を満たす。
- 全ノードが Row 0 から Row 16 まで到達可能。
- Row 0 は Start 1 個、Row 16 は Boss 1 個。

### 7.3 Fixed / Exclusion

- Row 9 の全ノードが `TileKind.Treasure`。
- Row 15 の全ノードが `TileKind.Rest`。
- Row 14 に `TileKind.Rest` が存在しない。
- Row 1 の全ノードが `TileKind.Enemy`。

### 7.4 エッジ数分布

- 大量試行（例：1000 個のマップ生成）で、Row 0/15 を除く出次数の比率が `EdgeCountWeights` から許容誤差内。

### 7.5 ルート制約

- 各ルートで `PerPathCount` 違反なし。
- 各ルートで `ForbiddenConsecutive` 違反なし。

### 7.6 再生成・例外

- 実現不可能な config（例：`PerPathCount.enemy.min = 20`）を与えると `MaxRegenerationAttempts` 到達後 `MapGenerationException` が投げられる。
- `FakeRng` を用いた小規模シナリオで、途中で制約違反→再試行→成功の流れを直接確認。

### 7.7 Config Loader

- 同梱 JSON が正しくデシリアライズされる。
- 不正 JSON（未知フィールド、型不一致、min > max）で `MapGenerationConfigException` が投げられる（`JsonOptions.Default` の `Disallow` に乗る）。

---

## 8. Core の VR 移植配慮

- `IRng` と Generator は VR 側では使わない前提（VR は事前生成 JSON を読むだけ）。そのため VR 移植ノートは「Phase 3 の Generator 本体は VR に持ち込まない」と明記。
- `MapNode` / `DungeonMap` / `TileKind` / `MapGenerationConfig` は VR 側でも使う（record → sealed class 置換、`ImmutableArray<T>` → `T[]`）。
- LINQ の深いチェーンは生成アルゴリズム側に留め、公開 API（`DungeonMap.NodesInRow` 等）は `IEnumerable<T>` を返すが VR 移植時は単純 `foreach` で置換可能な形に保つ。
- `async/await` は使わない（Phase 2 の `AudioSettings` と同じ方針）。

---

## 9. 実装順序と成果物

Phase 3 は以下の順で実装する想定（詳細は計画書で分解）：

1. `IRng` / `SystemRng` / `FakeRng`（Core.Random）
2. `TileKind` / `MapNode` / `DungeonMap`（Core.Map のドメインモデル）
3. `MapGenerationConfig` 群と `MapGenerationConfigLoader`（JSON 埋め込み込み）
4. `DungeonMapGenerator` のノード配置・エッジ貼り付け
5. 種別割当と FixedRow / RowKindExclusion 適用
6. マップ全体分布検証とルート制約検証
7. 再生成ループと `MapGenerationException`
8. JSON config（Act 1）同梱と Loader テスト
9. ロードマップ更新（Section 10 参照）

---

## 10. ロードマップ更新

- `docs/superpowers/plans/2026-04-20-roadmap.md` の Phase 3 項を本設計書の内容で上書き。
- 具体的な更新点：
  - 「9 マス目 = 各層唯一の宝箱」 → 「Row 9 = その行の全ノードが宝箱」に修正。
  - 「3 商人／Act」 → 「マップ全体で Merchant 3 個、1 ルートでは最大 2 個」に明記。
  - トポロジー規則（±1 隣接、固定 5 列グリッド）を追記。
  - Phase 3 完了タグ `phase3-complete` を設定：Core に Map 一式 + JSON config + テスト緑。

---

## 11. スコープ外／オープン論点

- **Map UI**：Phase 4 以降（マップ描画、ルート選択ハイライト、現在地表示など）。
- **Act 2／Act 3 config**：Phase 3 では作らない。Act 1 用 JSON 1 本のみ同梱。Phase 6 以降で Act 2／3 の config を足す想定。
- **プレイヤーの現在ノード保存**：Phase 4 のラン状態モデルに含める（`RunState` に `currentMapNodeId` を足す）。Phase 3 では生成だけ。
- **ボスタイプの決定**：Phase 3 では `TileKind.Boss` のマスを置くだけ。どのボスが出るかは後続フェーズ。
- **生成ロジックの性能**：`MaxRegenerationAttempts = 100` は勘。実測で 100 回以内にまず収束する前提だが、もし現実的に頻繁に 100 を超えるようなら config の制約を緩めるか、部分的な再生成に切り替える検討を後続で行う（Phase 3 のスコープ外）。

---
