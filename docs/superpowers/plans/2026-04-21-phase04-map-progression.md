# Phase 4 Map Progression Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** プレイヤーが Phase 3 マップ上を移動でき、セーブ／再開／放棄できる状態までを完成させる（戦闘マスに入ったら Phase 4 ではそこで止まる）。

**Architecture:** Core に Unknown 解決と移動ロジック（純関数）を置き、サーバは `RunStartService` と 5 エンドポイントの薄い REST レイヤで Core を呼ぶ。クライアントは SVG で描く `MapScreen` とモーダル `InGameMenuScreen` を追加し、`MainMenu` → `Map` を行き来する。RunState は v2 にスキーマ更新し、既存 v1 セーブは「セーブ無し」として扱う（互換ロジック無し、MVP の割り切り）。

**Tech Stack:** C# .NET 10（Core/Server）、xUnit、React 19 + TypeScript + Vite + vitest + @testing-library/react（Client）。

**Spec:** [docs/superpowers/specs/2026-04-21-phase04-map-progression-design.md](../specs/2026-04-21-phase04-map-progression-design.md)

---

## File Structure

### 新規作成

- **Core**
  - `src/Core/Map/UnknownResolutionConfig.cs` — record, `ImmutableDictionary<TileKind, double> Weights`
  - `src/Core/Map/UnknownResolver.cs` — `static ResolveAll(DungeonMap, UnknownResolutionConfig, IRng)`
  - `src/Core/Run/RunActions.cs` — `static SelectNextNode(RunState, DungeonMap, int)`
  - `tests/Core.Tests/Map/UnknownResolverTests.cs`
  - `tests/Core.Tests/Run/RunActionsTests.cs`
- **Server**
  - `src/Server/Services/RunStartService.cs` — 新規ラン構築（seed → map → resolutions → RunState → save）
  - `src/Server/Dtos/RunSnapshotDto.cs` — `{ run, map }` レスポンス DTO
  - `src/Server/Dtos/MoveRequestDto.cs` / `HeartbeatRequestDto.cs` — リクエスト DTO
- **Client**
  - `src/Client/src/screens/MapScreen.tsx`
  - `src/Client/src/screens/InGameMenuScreen.tsx`
  - `src/Client/src/screens/MapScreen.test.tsx`
  - `src/Client/src/screens/InGameMenuScreen.test.tsx`

### 変更

- **Core**
  - `src/Core/Run/RunState.cs` — v2 スキーマ（`CurrentTileIndex` 削除、3 フィールド追加、`Validate`、`NewSoloRun` 署名変更）
  - `src/Core/Map/MapGenerationConfig.cs` — `UnknownResolutionConfig` フィールド追加、`Validate` 拡張
  - `src/Core/Map/MapGenerationConfigLoader.cs` — DTO 拡張、JSON キー追加
  - `src/Core/Map/Config/map-act1.json` — `unknownResolutionWeights` ブロック追加
  - `tests/Core.Tests/Run/RunStateSerializerTests.cs`（存在しない場合は新規）
  - `tests/Core.Tests/Map/MapGenerationConfigLoaderTests.cs` — 新フィールドの検証
- **Server**
  - `src/Server/Controllers/RunsController.cs` — `/latest` 削除、5 エンドポイント追加
  - `src/Server/Services/FileBacked/FileSaveRepository.cs` — `TryLoadAsync` でスキーマ不一致キャッチ
  - `src/Server/Program.cs` — `RunStartService` DI 登録
  - `tests/Server.Tests/Controllers/RunsControllerTests.cs` — 全面書き直し
  - `tests/Server.Tests/Services/FileSaveRepositoryTests.cs` — v1 JSON を null 扱いするテスト追加
- **Client**
  - `src/Client/src/api/types.ts` — `RunStateDto` v2 化、`MapDto` / `MapNodeDto` / `RunSnapshotDto` 追加
  - `src/Client/src/api/runs.ts` — 5 関数実装
  - `src/Client/src/screens/MainMenuScreen.tsx` — シングルプレイ確認ダイアログ
  - `src/Client/src/screens/MainMenuScreen.test.tsx` — 新挙動テスト
  - `src/Client/src/App.tsx` — `MapScreen` ルート追加

---

## Task 1: UnknownResolutionConfig record

**Files:**
- Create: `src/Core/Map/UnknownResolutionConfig.cs`

- [ ] **Step 1: Create the record**

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// Unknown マスを具体 TileKind に解決するための重み設定。
/// 抽選先は Enemy / Elite / Merchant / Rest / Treasure のみ許可。
/// </summary>
public sealed record UnknownResolutionConfig(
    ImmutableDictionary<TileKind, double> Weights)
{
    /// <summary>不変条件を検査する。違反があれば理由文字列、問題なければ null。</summary>
    public string? Validate()
    {
        if (Weights.IsEmpty) return "UnknownResolutionConfig.Weights must not be empty";
        foreach (var kv in Weights)
        {
            if (kv.Key is TileKind.Unknown or TileKind.Start or TileKind.Boss)
                return $"UnknownResolutionConfig.Weights cannot contain {kv.Key}";
            if (kv.Value < 0)
                return $"UnknownResolutionConfig.Weights[{kv.Key}] must be >= 0 (got {kv.Value})";
        }
        if (Weights.Values.Sum() <= 0) return "UnknownResolutionConfig.Weights sum must be > 0";
        return null;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Core`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Core/Map/UnknownResolutionConfig.cs
git commit -m "feat(core): add UnknownResolutionConfig record"
```

---

## Task 2: UnknownResolver — 決定性テスト（failing）

**Files:**
- Create: `tests/Core.Tests/Map/UnknownResolverTests.cs`

- [ ] **Step 1: Write the failing test file**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class UnknownResolverTests
{
    private static UnknownResolutionConfig SampleConfig() =>
        new(ImmutableDictionary<TileKind, double>.Empty
            .Add(TileKind.Enemy, 48)
            .Add(TileKind.Merchant, 24)
            .Add(TileKind.Rest, 24)
            .Add(TileKind.Treasure, 4));

    private static DungeonMap GenerateMapWithUnknowns()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        return new DungeonMapGenerator().Generate(new SystemRng(58), cfg);
    }

    [Fact]
    public void ResolveAll_SameSeed_SameResult()
    {
        var map = GenerateMapWithUnknowns();
        var cfg = SampleConfig();
        var a = UnknownResolver.ResolveAll(map, cfg, new SystemRng(123));
        var b = UnknownResolver.ResolveAll(map, cfg, new SystemRng(123));
        Assert.Equal(a.Count, b.Count);
        foreach (var kv in a) Assert.Equal(kv.Value, b[kv.Key]);
    }

    [Fact]
    public void ResolveAll_OnlyUnknownNodesPresent()
    {
        var map = GenerateMapWithUnknowns();
        var cfg = SampleConfig();
        var result = UnknownResolver.ResolveAll(map, cfg, new SystemRng(123));
        foreach (var nodeId in result.Keys)
            Assert.Equal(TileKind.Unknown, map.GetNode(nodeId).Kind);
    }

    [Fact]
    public void ResolveAll_ZeroWeightKindNeverSelected()
    {
        var map = GenerateMapWithUnknowns();
        var cfg = new UnknownResolutionConfig(ImmutableDictionary<TileKind, double>.Empty
            .Add(TileKind.Enemy, 1)
            .Add(TileKind.Merchant, 0));
        var result = UnknownResolver.ResolveAll(map, cfg, new SystemRng(123));
        Assert.All(result.Values, v => Assert.Equal(TileKind.Enemy, v));
    }
}
```

- [ ] **Step 2: Run tests to verify fail**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~UnknownResolverTests`
Expected: FAIL（`UnknownResolver` 型が存在しないためコンパイルエラー）

---

## Task 3: UnknownResolver 実装（passing）

**Files:**
- Create: `src/Core/Map/UnknownResolver.cs`

- [ ] **Step 1: Write minimal implementation**

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// Map 内の全 Unknown ノードについて、重み付きランダムで具体 kind を抽選する。
/// ノードを Id 昇順で走査するため、同じ IRng 状態で呼べば同じ結果になる（決定的）。
/// </summary>
public static class UnknownResolver
{
    public static ImmutableDictionary<int, TileKind> ResolveAll(
        DungeonMap map, UnknownResolutionConfig config, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(rng);

        var invalid = config.Validate();
        if (invalid is not null)
            throw new MapGenerationConfigException($"UnknownResolutionConfig 不変条件違反: {invalid}");

        var entries = config.Weights.Where(kv => kv.Value > 0).ToArray();
        double totalWeight = entries.Sum(kv => kv.Value);

        var builder = ImmutableDictionary.CreateBuilder<int, TileKind>();
        foreach (var node in map.Nodes.OrderBy(n => n.Id))
        {
            if (node.Kind != TileKind.Unknown) continue;
            double r = rng.NextDouble() * totalWeight;
            double acc = 0;
            TileKind picked = entries[^1].Key;
            foreach (var kv in entries)
            {
                acc += kv.Value;
                if (r < acc) { picked = kv.Key; break; }
            }
            builder.Add(node.Id, picked);
        }
        return builder.ToImmutable();
    }
}
```

- [ ] **Step 2: Run tests to verify pass**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~UnknownResolverTests`
Expected: PASS（3/3）

- [ ] **Step 3: Commit**

```bash
git add src/Core/Map/UnknownResolver.cs tests/Core.Tests/Map/UnknownResolverTests.cs
git commit -m "feat(core): add UnknownResolver for mapping Unknown tiles to concrete kinds"
```

---

## Task 4: UnknownResolver — 禁止 kind バリデーションテスト

**Files:**
- Modify: `tests/Core.Tests/Map/UnknownResolverTests.cs`

- [ ] **Step 1: Append failing test**

ファイル末尾の最後の `}` の直前に以下を追加：

```csharp
    [Fact]
    public void ResolveAll_ForbiddenKindInWeights_Throws()
    {
        var map = GenerateMapWithUnknowns();
        var badCfg = new UnknownResolutionConfig(
            ImmutableDictionary<TileKind, double>.Empty.Add(TileKind.Boss, 1));
        Assert.Throws<MapGenerationConfigException>(
            () => UnknownResolver.ResolveAll(map, badCfg, new SystemRng(1)));
    }

    [Fact]
    public void ResolveAll_NullArgs_Throw()
    {
        var map = GenerateMapWithUnknowns();
        var cfg = SampleConfig();
        Assert.Throws<ArgumentNullException>(() => UnknownResolver.ResolveAll(null!, cfg, new SystemRng(1)));
        Assert.Throws<ArgumentNullException>(() => UnknownResolver.ResolveAll(map, null!, new SystemRng(1)));
        Assert.Throws<ArgumentNullException>(() => UnknownResolver.ResolveAll(map, cfg, null!));
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~UnknownResolverTests`
Expected: PASS（既存実装でカバー済み、5/5）

- [ ] **Step 3: Commit**

```bash
git add tests/Core.Tests/Map/UnknownResolverTests.cs
git commit -m "test(core): cover UnknownResolver validation paths"
```

---

## Task 5: MapGenerationConfig に UnknownResolutionConfig を組み込む

**Files:**
- Modify: `src/Core/Map/MapGenerationConfig.cs`

- [ ] **Step 1: Add field + validation**

`public sealed record MapGenerationConfig(` の末尾引数 `int MaxRegenerationAttempts)` の**後ろ**に以下を追加（直前にカンマを付ける）：

```csharp
    int MaxRegenerationAttempts,
    UnknownResolutionConfig UnknownResolutionWeights)
```

`Validate()` メソッドの末尾 `return null;` の**直前**に以下を追加：

```csharp
        var unknownInvalid = UnknownResolutionWeights.Validate();
        if (unknownInvalid is not null) return unknownInvalid;
```

- [ ] **Step 2: Build expected to fail**

Run: `dotnet build src/Core`
Expected: FAIL（`MapGenerationConfigLoader` と既存テストのコンストラクタ呼び出しがブローク）

- [ ] **Step 3: 既存コンストラクタ呼び出し箇所を一時的に null を渡さない形で修正**

`src/Core/Map/MapGenerationConfigLoader.cs` の `Dto.ToConfig()` を以下に置換：

```csharp
        public MapGenerationConfig ToConfig() => new(
            RowCount,
            ColumnCount,
            RowNodeCountMin,
            RowNodeCountMax,
            new EdgeCountWeights(EdgeWeights.Weight1, EdgeWeights.Weight2, EdgeWeights.Weight3),
            new TileDistributionRule(
                BaseWeights: TileDistribution.BaseWeights.ToImmutableDictionary(),
                MinPerMap: TileDistribution.MinPerMap.ToImmutableDictionary(),
                MaxPerMap: TileDistribution.MaxPerMap.ToImmutableDictionary()),
            FixedRows.Select(f => new FixedRowRule(f.Row, f.Kind)).ToImmutableArray(),
            RowKindExclusions.Select(x => new RowKindExclusion(x.Row, x.ExcludedKind)).ToImmutableArray(),
            new PathConstraintRule(
                PerPathCount: PathConstraints.PerPathCount.ToImmutableDictionary(
                    kv => kv.Key,
                    kv => new IntRange(kv.Value.Min, kv.Value.Max)),
                MinEliteRow: PathConstraints.MinEliteRow,
                ForbiddenConsecutive: PathConstraints.ForbiddenConsecutive
                    .Select(p => new TileKindPair(p.First, p.Second))
                    .ToImmutableArray()),
            MaxRegenerationAttempts,
            new UnknownResolutionConfig(
                UnknownResolutionWeights?.ToImmutableDictionary() ?? ImmutableDictionary<TileKind, double>.Empty));
```

同じ `Dto` record の引数リスト `int MaxRegenerationAttempts)` の**前**にカンマ区切りで以下を追加して、末尾で閉じる：

```csharp
        int MaxRegenerationAttempts,
        System.Collections.Generic.Dictionary<TileKind, double>? UnknownResolutionWeights)
```

- [ ] **Step 4: Build still expected to fail（既存テスト）**

Run: `dotnet build`
Expected: FAIL — 既存 `DungeonMapGeneratorTests.BaseConfig()` と `MapGenerationConfigTests` の直接コンストラクタ呼び出しがブローク。

該当 2 箇所の末尾引数に以下を追加（`MaxRegenerationAttempts` の後ろにカンマで）：

- `tests/Core.Tests/Map/DungeonMapGeneratorTests.cs` の `BaseConfig()` メソッド内 `new(` — `MaxRegenerationAttempts: 100)` の直前を `MaxRegenerationAttempts: 100,` にして末尾行を追加
- `tests/Core.Tests/Map/MapGenerationConfigTests.cs` 内 `new MapGenerationConfig(` — 同様

追加する行（どちらも同じ）：

```csharp
            UnknownResolutionWeights: new UnknownResolutionConfig(
                System.Collections.Immutable.ImmutableDictionary<TileKind, double>.Empty
                    .Add(TileKind.Enemy, 1))
```

必要な using: `using RoguelikeCardGame.Core.Map;`（既にあれば不要）

- [ ] **Step 5: Build**

Run: `dotnet build`
Expected: PASS

- [ ] **Step 6: Run all Core tests**

Run: `dotnet test tests/Core.Tests`
Expected: PASS（既存テスト群はそのまま、UnknownResolver tests 含めて全緑）

- [ ] **Step 7: Commit**

```bash
git add src/Core/Map/MapGenerationConfig.cs src/Core/Map/MapGenerationConfigLoader.cs tests/Core.Tests
git commit -m "feat(core): thread UnknownResolutionConfig through MapGenerationConfig"
```

---

## Task 6: map-act1.json に unknownResolutionWeights を追加

**Files:**
- Modify: `src/Core/Map/Config/map-act1.json`

- [ ] **Step 1: JSON 追記**

現在のファイル末尾 `"maxRegenerationAttempts": 500` の後にカンマを付け、以下のブロックを追加：

```json
  "maxRegenerationAttempts": 500,
  "unknownResolutionWeights": {
    "Enemy": 48,
    "Merchant": 24,
    "Rest": 24,
    "Treasure": 4
  }
```

- [ ] **Step 2: Loader テスト追加**

`tests/Core.Tests/Map/MapGenerationConfigLoaderTests.cs` の `LoadAct1_ReturnsNonNullConfig` 内の `Assert.Equal(TileKind.Treasure, row9.Kind);` の**次の行**に追加：

```csharp
        Assert.Equal(48, cfg.UnknownResolutionWeights.Weights[TileKind.Enemy]);
        Assert.False(cfg.UnknownResolutionWeights.Weights.ContainsKey(TileKind.Elite));
```

- [ ] **Step 3: Run**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~MapGenerationConfigLoaderTests`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Core/Map/Config/map-act1.json tests/Core.Tests/Map/MapGenerationConfigLoaderTests.cs
git commit -m "feat(core): add unknownResolutionWeights block to map-act1"
```

---

## Task 7: RunState v2 スキーマ（failing test 先行）

**Files:**
- Create: `tests/Core.Tests/Run/RunStateSerializerTests.cs`

- [ ] **Step 1: 新しい serializer round-trip テストを書く**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateSerializerTests
{
    private static RunState SampleV2()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: 42UL,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty.Add(5, TileKind.Enemy),
            nowUtc: new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero));
        return state;
    }

    [Fact]
    public void RoundTrip_V2_Preserves()
    {
        var original = SampleV2();
        var json = RunStateSerializer.Serialize(original);
        var loaded = RunStateSerializer.Deserialize(json);
        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal(0, loaded.CurrentNodeId);
        Assert.Contains(0, loaded.VisitedNodeIds);
        Assert.Equal(TileKind.Enemy, loaded.UnknownResolutions[5]);
    }

    [Fact]
    public void Deserialize_V1Json_ThrowsSerializerException()
    {
        var v1 = "{\"schemaVersion\":1,\"currentAct\":1,\"currentTileIndex\":0,\"currentHp\":80,\"maxHp\":80,\"gold\":99,\"deck\":[],\"relics\":[],\"potions\":[],\"playSeconds\":0,\"rngSeed\":0,\"savedAtUtc\":\"2026-04-21T00:00:00+00:00\",\"progress\":\"InProgress\"}";
        Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Deserialize(v1));
    }
}
```

- [ ] **Step 2: Run — expected compile fail**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunStateSerializerTests`
Expected: FAIL（RunState の v2 フィールド・新 `NewSoloRun` 署名がない）

---

## Task 8: RunState v2 実装

**Files:**
- Modify: `src/Core/Run/RunState.cs`

- [ ] **Step 1: Rewrite RunState**

ファイル全文を以下で置換：

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Player;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ソロ／マルチ共通のラン 1 回分の状態。ソロのみ ISaveRepository で永続化される。</summary>
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    ImmutableArray<int> VisitedNodeIds,
    ImmutableDictionary<int, TileKind> UnknownResolutions,
    int CurrentHp,
    int MaxHp,
    int Gold,
    IReadOnlyList<string> Deck,
    IReadOnlyList<string> Relics,
    IReadOnlyList<string> Potions,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress)
{
    /// <summary>Phase 4 の JSON スキーマバージョン。</summary>
    public const int CurrentSchemaVersion = 2;

    public const int StartingMaxHp = 80;
    public const int StartingGold = 99;

    public static RunState NewSoloRun(
        DataCatalog catalog,
        ulong rngSeed,
        int startNodeId,
        ImmutableDictionary<int, TileKind> unknownResolutions,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(unknownResolutions);

        foreach (var id in StarterDeck.DefaultCardIds)
        {
            if (!catalog.TryGetCard(id, out _))
                throw new InvalidOperationException(
                    $"StarterDeck が参照するカード ID が DataCatalog に存在しません: {id}");
        }

        var deck = StarterDeck.DefaultCardIds.ToArray();

        return new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentNodeId: startNodeId,
            VisitedNodeIds: ImmutableArray.Create(startNodeId),
            UnknownResolutions: unknownResolutions,
            CurrentHp: StartingMaxHp,
            MaxHp: StartingMaxHp,
            Gold: StartingGold,
            Deck: deck,
            Relics: Array.Empty<string>(),
            Potions: Array.Empty<string>(),
            PlaySeconds: 0L,
            RngSeed: rngSeed,
            SavedAtUtc: nowUtc,
            Progress: RunProgress.InProgress);
    }

    /// <summary>
    /// 構造的不変条件を検査する。違反があれば理由文字列、問題なければ null。
    /// </summary>
    public string? Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            return $"SchemaVersion must be {CurrentSchemaVersion} (got {SchemaVersion})";
        if (VisitedNodeIds.IsDefault) return "VisitedNodeIds must not be default";
        if (!VisitedNodeIds.Contains(CurrentNodeId))
            return $"VisitedNodeIds must contain CurrentNodeId ({CurrentNodeId})";
        if (VisitedNodeIds.Length != VisitedNodeIds.Distinct().Count())
            return "VisitedNodeIds must not contain duplicates";
        foreach (var kv in UnknownResolutions)
        {
            if (kv.Value is TileKind.Unknown or TileKind.Start or TileKind.Boss)
                return $"UnknownResolutions[{kv.Key}]={kv.Value} is not a valid resolved kind";
        }
        return null;
    }
}
```

- [ ] **Step 2: Build — expect fails in callers**

Run: `dotnet build`
Expected: FAIL — `RunState.NewSoloRun` の呼び出し箇所（テスト・RunsController など）が古い署名のまま。

- [ ] **Step 3: コンパイル失敗箇所を修正**

`Grep "NewSoloRun" --output_mode content` で洗い出し、以下のように差し替える：

旧: `RunState.NewSoloRun(catalog, rngSeed: 777UL, nowUtc: ...)`

新: `RunState.NewSoloRun(catalog, rngSeed: 777UL, startNodeId: 0, unknownResolutions: ImmutableDictionary<int, TileKind>.Empty, nowUtc: ...)`

該当ファイル（既知）:
- `tests/Server.Tests/Controllers/RunsControllerTests.cs` — `Get_AccountWithSavedRun_Returns200WithState`（Task 14 で丸ごと書き直すので気にしなくてもよい）
- `tests/Server.Tests/Services/FileSaveRepositoryTests.cs` の `FreshRun` helper を以下に置換：

```csharp
    private RunState FreshRun(ulong seed = 42UL) =>
        RunState.NewSoloRun(
            _catalog,
            rngSeed: seed,
            startNodeId: 0,
            unknownResolutions: System.Collections.Immutable.ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
            nowUtc: FixedNow);
```

必要な `using` を追加: `using RoguelikeCardGame.Core.Map;`（既にあれば不要）

- [ ] **Step 4: Run**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunStateSerializerTests`
Expected: PASS（2/2）

Run: `dotnet test tests/Core.Tests`
Expected: PASS（全体）

- [ ] **Step 5: Commit**

```bash
git add src/Core/Run/RunState.cs tests/Core.Tests/Run/RunStateSerializerTests.cs tests/Server.Tests
git commit -m "feat(core): bump RunState to v2 schema with map progression fields"
```

---

## Task 9: RunActions.SelectNextNode — failing tests

**Files:**
- Create: `tests/Core.Tests/Run/RunActionsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunActionsTests
{
    private static (DungeonMap map, RunState state) SetUp()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        var map = new DungeonMapGenerator().Generate(new SystemRng(58), cfg);
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: 58UL,
            startNodeId: map.StartNodeId,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            nowUtc: new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero));
        return (map, state);
    }

    [Fact]
    public void SelectNextNode_ValidAdjacent_UpdatesCurrentAndAppendsVisited()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];
        var next = RunActions.SelectNextNode(state, map, target);
        Assert.Equal(target, next.CurrentNodeId);
        Assert.Equal(new[] { map.StartNodeId, target }, next.VisitedNodeIds.ToArray());
    }

    [Fact]
    public void SelectNextNode_NonAdjacent_Throws()
    {
        var (map, state) = SetUp();
        int start = map.StartNodeId;
        int nonAdjacent = Enumerable.Range(0, map.Nodes.Length)
            .First(id => id != start && !map.GetNode(start).OutgoingNodeIds.Contains(id));
        Assert.Throws<ArgumentException>(() => RunActions.SelectNextNode(state, map, nonAdjacent));
    }

    [Fact]
    public void SelectNextNode_OutOfRange_Throws()
    {
        var (map, state) = SetUp();
        Assert.Throws<ArgumentException>(() => RunActions.SelectNextNode(state, map, -1));
        Assert.Throws<ArgumentException>(() => RunActions.SelectNextNode(state, map, map.Nodes.Length));
    }

    [Fact]
    public void SelectNextNode_DoesNotMutatePlaySeconds()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];
        var next = RunActions.SelectNextNode(state, map, target);
        Assert.Equal(state.PlaySeconds, next.PlaySeconds);
    }
}
```

- [ ] **Step 2: Run — expect compile fail**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunActionsTests`
Expected: FAIL（`RunActions` 未定義）

---

## Task 10: RunActions 実装

**Files:**
- Create: `src/Core/Run/RunActions.cs`

- [ ] **Step 1: Write minimal implementation**

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Map;

namespace RoguelikeCardGame.Core.Run;

/// <summary>
/// RunState を純関数で遷移させるアクション群。UI・通信非依存。
/// </summary>
public static class RunActions
{
    /// <summary>
    /// 現在地から target ノードへの移動を反映した新しい RunState を返す。
    /// target は現在ノードの OutgoingNodeIds に含まれる必要がある。違反時 <see cref="ArgumentException"/>。
    /// </summary>
    public static RunState SelectNextNode(RunState state, DungeonMap map, int targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);

        if (targetNodeId < 0 || targetNodeId >= map.Nodes.Length)
            throw new ArgumentException(
                $"targetNodeId {targetNodeId} is out of range [0..{map.Nodes.Length - 1}]",
                nameof(targetNodeId));

        var current = map.GetNode(state.CurrentNodeId);
        if (!current.OutgoingNodeIds.Contains(targetNodeId))
            throw new ArgumentException(
                $"targetNodeId {targetNodeId} is not adjacent to current node {state.CurrentNodeId}",
                nameof(targetNodeId));

        return state with
        {
            CurrentNodeId = targetNodeId,
            VisitedNodeIds = state.VisitedNodeIds.Add(targetNodeId),
        };
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunActionsTests`
Expected: PASS（4/4）

- [ ] **Step 3: Commit**

```bash
git add src/Core/Run/RunActions.cs tests/Core.Tests/Run/RunActionsTests.cs
git commit -m "feat(core): add RunActions.SelectNextNode"
```

---

## Task 11: FileSaveRepository が v1 JSON を null 扱いに

**Files:**
- Modify: `src/Server/Services/FileBacked/FileSaveRepository.cs`
- Modify: `tests/Server.Tests/Services/FileSaveRepositoryTests.cs`

- [ ] **Step 1: Write failing test**

`tests/Server.Tests/Services/FileSaveRepositoryTests.cs` の末尾（class の最後の `}` の直前）に追加：

```csharp
    [Fact]
    public async Task TryLoad_V1Json_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "saves"));
        var v1 = "{\"schemaVersion\":1,\"currentAct\":1,\"currentTileIndex\":0,\"currentHp\":80,\"maxHp\":80,\"gold\":99,\"deck\":[],\"relics\":[],\"potions\":[],\"playSeconds\":0,\"rngSeed\":0,\"savedAtUtc\":\"2026-04-21T00:00:00+00:00\",\"progress\":\"InProgress\"}";
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "saves", "v1user.json"), v1);
        var loaded = await _repo.TryLoadAsync("v1user", CancellationToken.None);
        Assert.Null(loaded);
    }
```

- [ ] **Step 2: Run — expect fail**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~FileSaveRepositoryTests.TryLoadAsync_V1Json`
Expected: FAIL（throw される）

- [ ] **Step 3: Update repository**

`src/Server/Services/FileBacked/FileSaveRepository.cs` の `TryLoadAsync` を以下に置換：

```csharp
    public async Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        try
        {
            return RunStateSerializer.Deserialize(json);
        }
        catch (RunStateSerializerException)
        {
            // スキーマ不一致や破損セーブは「セーブ無し」扱いにして新規扱いで始められるようにする。
            return null;
        }
    }
```

- [ ] **Step 4: Run**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~FileSaveRepositoryTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Server/Services/FileBacked/FileSaveRepository.cs tests/Server.Tests/Services/FileSaveRepositoryTests.cs
git commit -m "fix(server): treat schema-mismatch saves as missing"
```

---

## Task 12: RunStartService

**Files:**
- Create: `src/Server/Services/RunStartService.cs`

- [ ] **Step 1: Implement service**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services;

/// <summary>新規ソロランを構築し永続化するサービス。seed → map → unknown 解決 → RunState → save。</summary>
public sealed class RunStartService
{
    private readonly IDungeonMapGenerator _generator;
    private readonly MapGenerationConfig _mapConfig;
    private readonly ISaveRepository _saves;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<int> _seedSource;

    public RunStartService(
        IDungeonMapGenerator generator,
        MapGenerationConfig mapConfig,
        ISaveRepository saves,
        Func<DateTimeOffset>? now = null,
        Func<int>? seedSource = null)
    {
        _generator = generator;
        _mapConfig = mapConfig;
        _saves = saves;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _seedSource = seedSource ?? (() => System.Random.Shared.Next());
    }

    public async Task<(RunState state, DungeonMap map)> StartAsync(string accountId, CancellationToken ct)
    {
        int seed = _seedSource();
        var map = _generator.Generate(new SystemRng(seed), _mapConfig);
        var resolutions = UnknownResolver.ResolveAll(
            map, _mapConfig.UnknownResolutionWeights, new SystemRng(seed + 1));
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: unchecked((ulong)(uint)seed),
            startNodeId: map.StartNodeId,
            unknownResolutions: resolutions,
            nowUtc: _now());
        await _saves.SaveAsync(accountId, state, ct);
        return (state, map);
    }

    /// <summary>保存済み seed から map を再生成して返す（move / current 用）。</summary>
    public DungeonMap RehydrateMap(ulong rngSeed)
    {
        int seed = unchecked((int)(uint)rngSeed);
        return _generator.Generate(new SystemRng(seed), _mapConfig);
    }
}
```

- [ ] **Step 2: Register DI + configure JSON enum serialization**

`src/Server/Program.cs` の `builder.Services.AddSingleton<IDungeonMapGenerator, DungeonMapGenerator>();` の**後**に追加：

```csharp
builder.Services.AddSingleton<RunStartService>();
```

既存の `builder.Services.AddControllers();` 行を以下に置換（enum を数値ではなく文字列として JSON 化するため。Client 側は `'InProgress'` や `'Enemy'` を文字列として期待している）：

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Server/Services/RunStartService.cs src/Server/Program.cs
git commit -m "feat(server): add RunStartService and configure string enum JSON"
```

---

## Task 13: サーバ DTO を追加

**Files:**
- Create: `src/Server/Dtos/RunSnapshotDto.cs`
- Create: `src/Server/Dtos/MoveRequestDto.cs`
- Create: `src/Server/Dtos/HeartbeatRequestDto.cs`

- [ ] **Step 1: Create DTOs**

`src/Server/Dtos/RunSnapshotDto.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RunSnapshotDto(RunState Run, MapDto Map);

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
```

`src/Server/Dtos/MoveRequestDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record MoveRequestDto(int NodeId, long ElapsedSeconds);
```

`src/Server/Dtos/HeartbeatRequestDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record HeartbeatRequestDto(long ElapsedSeconds);
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Server/Dtos
git commit -m "feat(server): add DTOs for run snapshot and move/heartbeat requests"
```

---

## Task 14: RunsController を 5 エンドポイントに書き換え — failing tests

**Files:**
- Modify: `tests/Server.Tests/Controllers/RunsControllerTests.cs`

- [ ] **Step 1: Overwrite test file with new expectations**

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class RunsControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public RunsControllerTests(TempDataFactory factory) => _factory = factory;

    private static HttpClient WithAccount(HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        client.DefaultRequestHeaders.Add("X-Account-Id", id);
        return client;
    }

    private async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
            res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetCurrent_NoSave_Returns204()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "alice");
        WithAccount(client, "alice");
        var res = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task PostNew_CreatesRunReturnsSnapshot()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "bob");
        WithAccount(client, "bob");
        var res = await client.PostAsync("/api/v1/runs/new", content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("run", out _));
        Assert.True(doc.RootElement.TryGetProperty("map", out _));
    }

    [Fact]
    public async Task PostNew_ExistingInProgress_Returns409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "carol");
        WithAccount(client, "carol");
        await client.PostAsync("/api/v1/runs/new", content: null);
        var res = await client.PostAsync("/api/v1/runs/new", content: null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task PostNew_ForceTrue_Overwrites()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "dan");
        WithAccount(client, "dan");
        await client.PostAsync("/api/v1/runs/new", content: null);
        var res = await client.PostAsync("/api/v1/runs/new?force=true", content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task PostMove_AdjacentNode_Returns204AndAdvances()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "eve");
        WithAccount(client, "eve");
        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        var doc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        int startId = doc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32();
        int targetId = -1;
        foreach (var n in doc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
        {
            if (n.GetProperty("id").GetInt32() == startId)
            {
                targetId = n.GetProperty("outgoingNodeIds")[0].GetInt32();
                break;
            }
        }
        Assert.True(targetId >= 0);
        var moveRes = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = targetId, elapsedSeconds = 5 });
        Assert.Equal(HttpStatusCode.NoContent, moveRes.StatusCode);

        var curRes = await client.GetAsync("/api/v1/runs/current");
        var curDoc = JsonDocument.Parse(await curRes.Content.ReadAsStringAsync());
        Assert.Equal(targetId, curDoc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32());
        Assert.True(curDoc.RootElement.GetProperty("run").GetProperty("playSeconds").GetInt64() >= 5);
    }

    [Fact]
    public async Task PostMove_NonAdjacent_Returns400()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "fay");
        WithAccount(client, "fay");
        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        var doc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        int startId = doc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32();
        int bad = -1;
        foreach (var n in doc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
        {
            int id = n.GetProperty("id").GetInt32();
            if (id == startId) continue;
            bool isAdj = false;
            foreach (var adj in doc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
            {
                if (adj.GetProperty("id").GetInt32() == startId)
                {
                    foreach (var out_ in adj.GetProperty("outgoingNodeIds").EnumerateArray())
                        if (out_.GetInt32() == id) isAdj = true;
                }
            }
            if (!isAdj) { bad = id; break; }
        }
        Assert.True(bad >= 0);
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = bad, elapsedSeconds = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task PostAbandon_TransitionsAndHidesFromCurrent()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "gus");
        WithAccount(client, "gus");
        await client.PostAsync("/api/v1/runs/new", content: null);
        var abandon = await client.PostAsJsonAsync("/api/v1/runs/current/abandon", new { elapsedSeconds = 3 });
        Assert.Equal(HttpStatusCode.NoContent, abandon.StatusCode);
        var cur = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.NoContent, cur.StatusCode);
    }

    [Fact]
    public async Task PostHeartbeat_AddsPlaySeconds()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "hal");
        WithAccount(client, "hal");
        await client.PostAsync("/api/v1/runs/new", content: null);
        var res = await client.PostAsJsonAsync("/api/v1/runs/current/heartbeat", new { elapsedSeconds = 7 });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var cur = await client.GetAsync("/api/v1/runs/current");
        var doc = JsonDocument.Parse(await cur.Content.ReadAsStringAsync());
        Assert.Equal(7, doc.RootElement.GetProperty("run").GetProperty("playSeconds").GetInt64());
    }

    [Fact]
    public async Task NoHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/runs/current");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
```

- [ ] **Step 2: Run — expect fail**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~RunsControllerTests`
Expected: FAIL（旧 `/latest` のまま、新エンドポイント未実装）

---

## Task 15: RunsController 実装

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`

- [ ] **Step 1: Rewrite controller**

ファイル全文を以下で置換：

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/runs")]
public sealed class RunsController : ControllerBase
{
    public const string AccountHeader = "X-Account-Id";
    private const long MaxElapsedSecondsPerRequest = 86400L;

    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly RunStartService _runStart;

    public RunsController(IAccountRepository accounts, ISaveRepository saves, RunStartService runStart)
    {
        _accounts = accounts;
        _saves = saves;
        _runStart = runStart;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress) return NoContent();

        var map = _runStart.RehydrateMap(state.RngSeed);
        return Ok(new RunSnapshotDto(state, MapDtoMapper.From(map)));
    }

    [HttpPost("new")]
    public async Task<IActionResult> PostNew([FromQuery] bool force, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var existing = await _saves.TryLoadAsync(accountId, ct);
        if (!force && existing is not null && existing.Progress == RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがあります。force=true で上書き可能。");

        var (state, map) = await _runStart.StartAsync(accountId, ct);
        return Ok(new RunSnapshotDto(state, MapDtoMapper.From(map)));
    }

    [HttpPost("current/move")]
    public async Task<IActionResult> PostMove([FromBody] MoveRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");

        var map = _runStart.RehydrateMap(state.RngSeed);
        RunState updated;
        try
        {
            updated = RunActions.SelectNextNode(state, map, body.NodeId);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        long elapsed = Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
        updated = updated with
        {
            PlaySeconds = state.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        await _saves.SaveAsync(accountId, updated, ct);
        return NoContent();
    }

    [HttpPost("current/abandon")]
    public async Task<IActionResult> PostAbandon([FromBody] HeartbeatRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");

        long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
        var updated = state with
        {
            Progress = RunProgress.Abandoned,
            PlaySeconds = state.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        await _saves.SaveAsync(accountId, updated, ct);
        return NoContent();
    }

    [HttpPost("current/heartbeat")]
    public async Task<IActionResult> PostHeartbeat([FromBody] HeartbeatRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");

        long elapsed = Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
        var updated = state with
        {
            PlaySeconds = state.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        await _saves.SaveAsync(accountId, updated, ct);
        return NoContent();
    }

    private bool TryGetAccountId(out string accountId, out IActionResult? error)
    {
        accountId = string.Empty;
        error = null;
        if (!Request.Headers.TryGetValue(AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            error = Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"ヘッダ {AccountHeader} が必要です。");
            return false;
        }
        var candidate = raw.ToString();
        try { AccountIdValidator.Validate(candidate); }
        catch (ArgumentException ex)
        {
            error = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            return false;
        }
        accountId = candidate;
        return true;
    }
}
```

- [ ] **Step 2: Run all Server tests**

Run: `dotnet test tests/Server.Tests`
Expected: PASS（RunsControllerTests 全緑、他は変更なしで通る）

- [ ] **Step 3: Commit**

```bash
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/RunsControllerTests.cs
git commit -m "feat(server): implement map progression endpoints (current/new/move/abandon/heartbeat)"
```

---

## Task 16: Client API types + runs.ts 更新

**Files:**
- Modify: `src/Client/src/api/types.ts`
- Modify: `src/Client/src/api/runs.ts`

- [ ] **Step 1: Rewrite types.ts**

```typescript
export type AccountDto = {
  id: string
  createdAt: string
}

export type AudioSettingsDto = {
  schemaVersion: number
  master: number
  bgm: number
  se: number
  ambient: number
}

export type TileKind =
  | 'Start'
  | 'Enemy'
  | 'Elite'
  | 'Rest'
  | 'Merchant'
  | 'Treasure'
  | 'Unknown'
  | 'Boss'

export type RunProgress = 'InProgress' | 'Cleared' | 'GameOver' | 'Abandoned'

export type RunStateDto = {
  schemaVersion: number
  currentAct: number
  currentNodeId: number
  visitedNodeIds: number[]
  unknownResolutions: Record<number, TileKind>
  currentHp: number
  maxHp: number
  gold: number
  deck: string[]
  relics: string[]
  potions: string[]
  playSeconds: number
  rngSeed: number
  savedAtUtc: string
  progress: RunProgress
}

export type MapNodeDto = {
  id: number
  row: number
  column: number
  kind: TileKind
  outgoingNodeIds: number[]
}

export type MapDto = {
  startNodeId: number
  bossNodeId: number
  nodes: MapNodeDto[]
}

export type RunSnapshotDto = {
  run: RunStateDto
  map: MapDto
}
```

- [ ] **Step 2: Rewrite runs.ts**

```typescript
import { ApiError, apiRequest } from './client'
import type { RunSnapshotDto } from './types'

export async function getCurrentRun(accountId: string): Promise<RunSnapshotDto | null> {
  try {
    const result = await apiRequest<RunSnapshotDto | undefined>('GET', '/runs/current', { accountId })
    return result ?? null
  } catch (err) {
    if (err instanceof ApiError && err.status === 204) return null
    throw err
  }
}

export async function startNewRun(accountId: string, force = false): Promise<RunSnapshotDto> {
  const path = force ? '/runs/new?force=true' : '/runs/new'
  return await apiRequest<RunSnapshotDto>('POST', path, { accountId })
}

export async function moveToNode(accountId: string, nodeId: number, elapsedSeconds: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/move', {
    accountId,
    body: { nodeId, elapsedSeconds },
  })
}

export async function abandonRun(accountId: string, elapsedSeconds: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/abandon', {
    accountId,
    body: { elapsedSeconds },
  })
}

export async function heartbeat(accountId: string, elapsedSeconds: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/heartbeat', {
    accountId,
    body: { elapsedSeconds },
  })
}
```

- [ ] **Step 3: Typecheck**

Run: `cd src/Client && npm run build`
Expected: FAIL — 既存 `MainMenuScreen.tsx` が `getLatestRun` を参照している

- [ ] **Step 4: Fix MainMenuScreen.tsx import temporarily**

`src/Client/src/screens/MainMenuScreen.tsx` の `import { getLatestRun } from '../api/runs'` を以下に変更：

```typescript
import { getCurrentRun } from '../api/runs'
```

また `getLatestRun(accountId).then((run) => { if (!cancelled) setHasRun(run !== null) })` を以下に変更：

```typescript
getCurrentRun(accountId)
  .then((snap) => { if (!cancelled) setHasRun(snap !== null) })
```

- [ ] **Step 5: Typecheck**

Run: `cd src/Client && npm run build`
Expected: PASS

- [ ] **Step 6: Run tests**

Run: `cd src/Client && npm test -- --run`
Expected: PASS（`MainMenuScreen.test.tsx` はまだ旧挙動のテストのみ、全緑）

- [ ] **Step 7: Commit**

```bash
git add src/Client/src/api src/Client/src/screens/MainMenuScreen.tsx
git commit -m "feat(client): update runs API for map progression endpoints"
```

---

## Task 17: MainMenuScreen にシングルプレイ確認ダイアログ — failing test

**Files:**
- Modify: `src/Client/src/screens/MainMenuScreen.test.tsx`

- [ ] **Step 1: Replace coming-soon test for 'single' with new expectations**

既存の "shows coming-soon dialog for multiplayer / achievements" テストは残すが、以下 2 つのテストを追加（describe ブロック内、末尾）：

```typescript
  it('shows confirm dialog when single-play clicked with existing InProgress run', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          run: { schemaVersion: 2, progress: 'InProgress', currentNodeId: 0 },
          map: { startNodeId: 0, bossNodeId: 1, nodes: [] },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    renderScreen()
    await waitFor(() => expect(screen.getByText('保存済みラン有り')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'シングルプレイ' }))
    expect(await screen.findByText(/進行中のランがあります/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '続きから' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '新規で上書き' })).toBeInTheDocument()
  })

  it('starts new run directly when no InProgress save exists', async () => {
    const onStart = vi.fn()
    // /runs/current → 204 (no save)
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    // /runs/new → 200
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          run: { schemaVersion: 2, currentNodeId: 0, progress: 'InProgress' },
          map: { startNodeId: 0, bossNodeId: 1, nodes: [] },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    localStorage.setItem('rcg.accountId', 'alice')
    render(
      <AccountProvider>
        <MainMenuScreen
          onOpenSettings={() => {}}
          onLogout={() => {}}
          onStartRun={onStart}
        />
      </AccountProvider>,
    )
    fireEvent.click(await screen.findByRole('button', { name: 'シングルプレイ' }))
    await waitFor(() => expect(onStart).toHaveBeenCalled())
  })
```

既存の "shows coming-soon dialog for multiplayer / achievements" テストで `'single'` を含む参照があれば、その部分をマルチプレイのみに絞るよう修正（シングルは coming-soon でなくなる）。

- [ ] **Step 2: Run — expect fail**

Run: `cd src/Client && npm test -- --run MainMenuScreen`
Expected: FAIL

---

## Task 18: MainMenuScreen 実装更新

**Files:**
- Modify: `src/Client/src/screens/MainMenuScreen.tsx`

- [ ] **Step 1: Rewrite screen**

```tsx
import { useEffect, useState } from 'react'
import { getCurrentRun, startNewRun } from '../api/runs'
import type { RunSnapshotDto } from '../api/types'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'

type Props = {
  onOpenSettings: () => void
  onLogout: () => void
  onStartRun?: (snapshot: RunSnapshotDto) => void
}

type ComingSoonKind = 'multi' | 'achievements' | 'quit' | null

export function MainMenuScreen({ onOpenSettings, onLogout, onStartRun }: Props) {
  const { accountId } = useAccount()
  const [snapshot, setSnapshot] = useState<RunSnapshotDto | null>(null)
  const [dialog, setDialog] = useState<ComingSoonKind>(null)
  const [singleDialog, setSingleDialog] = useState(false)
  const [pending, setPending] = useState(false)

  useEffect(() => {
    if (!accountId) return
    let cancelled = false
    getCurrentRun(accountId)
      .then((snap) => { if (!cancelled) setSnapshot(snap) })
      .catch(() => { /* hasRun=false のまま */ })
    return () => { cancelled = true }
  }, [accountId])

  async function startFresh(force: boolean) {
    if (!accountId || pending) return
    setPending(true)
    try {
      const snap = await startNewRun(accountId, force)
      onStartRun?.(snap)
    } finally {
      setPending(false)
      setSingleDialog(false)
    }
  }

  function handleSingle() {
    if (snapshot && snapshot.run.progress === 'InProgress') {
      setSingleDialog(true)
    } else {
      void startFresh(false)
    }
  }

  function continueRun() {
    if (snapshot) onStartRun?.(snapshot)
    setSingleDialog(false)
  }

  return (
    <main className="main-menu">
      <header className="main-menu__header">
        <span className="main-menu__account">{accountId}</span>
        <button className="btn btn--secondary" onClick={onLogout}>ログアウト</button>
      </header>

      <nav className="main-menu__buttons">
        <Button onClick={handleSingle}>シングルプレイ</Button>
        <Button onClick={() => setDialog('multi')}>マルチプレイ</Button>
        <Button onClick={onOpenSettings}>設定</Button>
        <Button onClick={() => setDialog('achievements')}>実績</Button>
        <Button variant="danger" onClick={() => setDialog('quit')}>終了</Button>
      </nav>

      {snapshot && <p className="main-menu__badge">保存済みラン有り</p>}

      {dialog && (
        <div role="dialog" aria-label="準備中" className="main-menu__dialog">
          <p>準備中です。</p>
          {dialog === 'quit' && <p>このタブを閉じてください。</p>}
          <Button variant="secondary" onClick={() => setDialog(null)}>閉じる</Button>
        </div>
      )}

      {singleDialog && (
        <div role="dialog" aria-label="シングルプレイ" className="main-menu__dialog">
          <p>進行中のランがあります。どうしますか？</p>
          <Button onClick={continueRun}>続きから</Button>
          <Button variant="danger" onClick={() => void startFresh(true)}>新規で上書き</Button>
          <Button variant="secondary" onClick={() => setSingleDialog(false)}>キャンセル</Button>
        </div>
      )}
    </main>
  )
}
```

- [ ] **Step 2: Run tests**

Run: `cd src/Client && npm test -- --run MainMenuScreen`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Client/src/screens/MainMenuScreen.tsx src/Client/src/screens/MainMenuScreen.test.tsx
git commit -m "feat(client): add single-play confirmation dialog and new-run dispatch"
```

---

## Task 19: MapScreen — failing test skeleton

**Files:**
- Create: `src/Client/src/screens/MapScreen.test.tsx`

- [ ] **Step 1: Write test**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountProvider } from '../context/AccountContext'
import type { RunSnapshotDto } from '../api/types'
import { MapScreen } from './MapScreen'

function sampleSnapshot(): RunSnapshotDto {
  return {
    run: {
      schemaVersion: 2,
      currentAct: 1,
      currentNodeId: 0,
      visitedNodeIds: [0],
      unknownResolutions: {},
      currentHp: 80, maxHp: 80, gold: 99,
      deck: [], relics: [], potions: [],
      playSeconds: 0, rngSeed: 42,
      savedAtUtc: '2026-04-21T00:00:00Z',
      progress: 'InProgress',
    },
    map: {
      startNodeId: 0,
      bossNodeId: 2,
      nodes: [
        { id: 0, row: 0, column: 2, kind: 'Start', outgoingNodeIds: [1] },
        { id: 1, row: 1, column: 2, kind: 'Enemy', outgoingNodeIds: [2] },
        { id: 2, row: 16, column: 2, kind: 'Boss', outgoingNodeIds: [] },
      ],
    },
  }
}

describe('MapScreen', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    localStorage.setItem('rcg.accountId', 'alice')
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('renders nodes from snapshot and highlights current node', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot()}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByTestId('map-node-0')).toHaveAttribute('data-current', 'true')
    expect(screen.getByTestId('map-node-1')).toHaveAttribute('data-selectable', 'true')
    expect(screen.getByTestId('map-node-2')).toHaveAttribute('data-selectable', 'false')
  })

  it('calls move API when clicking a selectable node', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot()}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByTestId('map-node-1'))
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toContain('/runs/current/move')
    expect(init.body).toContain('"nodeId":1')
  })

  it('opens in-game menu when gear icon is clicked', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot()}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'メニュー' }))
    expect(screen.getByRole('dialog', { name: 'ゲームメニュー' })).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run — expect fail**

Run: `cd src/Client && npm test -- --run MapScreen`
Expected: FAIL

---

## Task 20: MapScreen 実装

**Files:**
- Create: `src/Client/src/screens/MapScreen.tsx`

- [ ] **Step 1: Write MapScreen**

```tsx
import { useEffect, useRef, useState } from 'react'
import { heartbeat, moveToNode } from '../api/runs'
import type { MapNodeDto, RunSnapshotDto, TileKind } from '../api/types'
import { useAccount } from '../context/AccountContext'
import { InGameMenuScreen } from './InGameMenuScreen'

type Props = {
  snapshot: RunSnapshotDto
  onExitToMenu: () => void
  onAbandon: () => void
}

const NODE_R = 20
const COL_W = 100
const ROW_H = 50
const LEFT_PAD = 50
const TOP_PAD = 30

function iconFor(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Start': return '●'
    case 'Enemy': return '⚔'
    case 'Elite': return '⚔⚔'
    case 'Merchant': return '商'
    case 'Rest': return '火'
    case 'Treasure': return '宝'
    case 'Unknown': return '?'
    case 'Boss': return '王'
  }
}

export function MapScreen({ snapshot, onExitToMenu, onAbandon }: Props) {
  const { accountId } = useAccount()
  const [snap, setSnap] = useState<RunSnapshotDto>(snapshot)
  const [menuOpen, setMenuOpen] = useState(false)
  const [busy, setBusy] = useState(false)
  const mountedAt = useRef<number>(performance.now())

  useEffect(() => {
    return () => {
      if (!accountId) return
      const elapsed = Math.floor((performance.now() - mountedAt.current) / 1000)
      if (elapsed > 0) void heartbeat(accountId, elapsed).catch(() => {})
    }
  }, [accountId])

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setMenuOpen((v) => !v)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const currentNode = snap.map.nodes.find((n) => n.id === snap.run.currentNodeId)!
  const visited = new Set(snap.run.visitedNodeIds)

  function isSelectable(n: MapNodeDto): boolean {
    return currentNode.outgoingNodeIds.includes(n.id)
  }

  function posOf(n: MapNodeDto): { cx: number; cy: number } {
    const maxRow = 16
    return {
      cx: LEFT_PAD + n.column * COL_W,
      cy: TOP_PAD + (maxRow - n.row) * ROW_H,
    }
  }

  async function handleClick(n: MapNodeDto) {
    if (!accountId || busy || !isSelectable(n)) return
    setBusy(true)
    const elapsed = Math.floor((performance.now() - mountedAt.current) / 1000)
    try {
      await moveToNode(accountId, n.id, Math.max(0, elapsed))
      mountedAt.current = performance.now()
      // optimistic update
      setSnap((prev) => ({
        ...prev,
        run: {
          ...prev.run,
          currentNodeId: n.id,
          visitedNodeIds: [...prev.run.visitedNodeIds, n.id],
          playSeconds: prev.run.playSeconds + Math.max(0, elapsed),
        },
      }))
    } finally {
      setBusy(false)
    }
  }

  const resolved = snap.run.unknownResolutions
  const maxCol = Math.max(...snap.map.nodes.map((n) => n.column))
  const width = LEFT_PAD * 2 + maxCol * COL_W
  const height = TOP_PAD * 2 + 16 * ROW_H
  const atBoss = currentNode.kind === 'Boss'

  return (
    <main className="map-screen">
      <header className="map-screen__top">
        <span>HP {snap.run.currentHp}/{snap.run.maxHp}</span>
        <span>Gold {snap.run.gold}</span>
        <button aria-label="メニュー" onClick={() => setMenuOpen(true)}>⚙</button>
      </header>

      <svg viewBox={`0 0 ${width} ${height}`} className="map-screen__svg">
        {snap.map.nodes.map((n) =>
          n.outgoingNodeIds.map((toId) => {
            const to = snap.map.nodes.find((x) => x.id === toId)!
            const a = posOf(n)
            const b = posOf(to)
            const visitedEdge = visited.has(n.id) && visited.has(toId)
            return (
              <line
                key={`${n.id}-${toId}`}
                x1={a.cx} y1={a.cy} x2={b.cx} y2={b.cy}
                stroke={visitedEdge ? '#888' : '#444'}
                strokeWidth={visitedEdge ? 3 : 2}
              />
            )
          }),
        )}
        {snap.map.nodes.map((n) => {
          const { cx, cy } = posOf(n)
          const isCurrent = n.id === snap.run.currentNodeId
          const isVisited = visited.has(n.id)
          const selectable = isSelectable(n)
          const resolvedKind: TileKind | null = isVisited ? (resolved[n.id] ?? null) : null
          return (
            <g
              key={n.id}
              data-testid={`map-node-${n.id}`}
              data-current={isCurrent ? 'true' : 'false'}
              data-selectable={selectable ? 'true' : 'false'}
              data-visited={isVisited ? 'true' : 'false'}
              onClick={() => handleClick(n)}
              style={{ cursor: selectable ? 'pointer' : 'default' }}
            >
              <circle
                cx={cx} cy={cy} r={NODE_R}
                fill={isVisited ? '#444' : '#222'}
                stroke={isCurrent ? 'gold' : selectable ? '#4ae' : '#666'}
                strokeWidth={isCurrent ? 4 : selectable ? 3 : 1}
              />
              <text
                x={cx} y={cy + 5}
                textAnchor="middle"
                fill={isVisited ? '#aaa' : '#eee'}
                fontSize="14"
              >
                {iconFor(n.kind, resolvedKind)}
              </text>
            </g>
          )
        })}
      </svg>

      {atBoss && (
        <p className="map-screen__dev-note">
          ボスに到達しました。ここから先は Phase 5 以降で実装されます。
        </p>
      )}

      {menuOpen && (
        <InGameMenuScreen
          onClose={() => setMenuOpen(false)}
          onExitToMenu={onExitToMenu}
          onAbandon={onAbandon}
          elapsedSecondsRef={mountedAt}
        />
      )}
    </main>
  )
}
```

- [ ] **Step 2: Run — InGameMenuScreen 未実装のため compile fail**

Run: `cd src/Client && npm test -- --run MapScreen`
Expected: FAIL

---

## Task 21: InGameMenuScreen 実装

**Files:**
- Create: `src/Client/src/screens/InGameMenuScreen.tsx`
- Create: `src/Client/src/screens/InGameMenuScreen.test.tsx`

- [ ] **Step 1: Write test**

```tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useRef } from 'react'
import { AccountProvider } from '../context/AccountContext'
import { InGameMenuScreen } from './InGameMenuScreen'

function Wrapper({ onExitToMenu, onAbandon, onClose }: {
  onExitToMenu: () => void; onAbandon: () => void; onClose: () => void
}) {
  const ref = useRef(performance.now())
  return (
    <AccountProvider>
      <InGameMenuScreen
        onClose={onClose}
        onExitToMenu={onExitToMenu}
        onAbandon={onAbandon}
        elapsedSecondsRef={ref}
      />
    </AccountProvider>
  )
}

describe('InGameMenuScreen', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    localStorage.setItem('rcg.accountId', 'alice')
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('calls onClose when continue is clicked', () => {
    const onClose = vi.fn()
    render(<Wrapper onExitToMenu={() => {}} onAbandon={() => {}} onClose={onClose} />)
    fireEvent.click(screen.getByRole('button', { name: '続ける' }))
    expect(onClose).toHaveBeenCalled()
  })

  it('sends heartbeat and calls onExitToMenu when exit clicked', async () => {
    const onExit = vi.fn()
    render(<Wrapper onExitToMenu={onExit} onAbandon={() => {}} onClose={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'メニューに戻る' }))
    await waitFor(() => {
      const urls = fetchMock.mock.calls.map((c) => c[0] as string)
      expect(urls.some((u) => u.includes('/runs/current/heartbeat'))).toBe(true)
    })
    await waitFor(() => expect(onExit).toHaveBeenCalled())
  })

  it('asks confirmation before abandon', async () => {
    const onAbandon = vi.fn()
    render(<Wrapper onExitToMenu={() => {}} onAbandon={onAbandon} onClose={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'あきらめる' }))
    expect(screen.getByText(/本当にこのランを放棄/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '放棄する' }))
    await waitFor(() => {
      const urls = fetchMock.mock.calls.map((c) => c[0] as string)
      expect(urls.some((u) => u.includes('/runs/current/abandon'))).toBe(true)
    })
    await waitFor(() => expect(onAbandon).toHaveBeenCalled())
  })
})
```

- [ ] **Step 2: Write implementation**

```tsx
import { MutableRefObject, useState } from 'react'
import { abandonRun, heartbeat } from '../api/runs'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'
import { SettingsScreen } from './SettingsScreen'

type Props = {
  onClose: () => void
  onExitToMenu: () => void
  onAbandon: () => void
  elapsedSecondsRef: MutableRefObject<number>
}

type Mode = 'main' | 'settings' | 'confirm-abandon'

export function InGameMenuScreen({ onClose, onExitToMenu, onAbandon, elapsedSecondsRef }: Props) {
  const { accountId } = useAccount()
  const [mode, setMode] = useState<Mode>('main')
  const [busy, setBusy] = useState(false)

  function currentElapsed(): number {
    return Math.max(0, Math.floor((performance.now() - elapsedSecondsRef.current) / 1000))
  }

  async function exit() {
    if (!accountId || busy) return
    setBusy(true)
    const e = currentElapsed()
    try {
      if (e > 0) await heartbeat(accountId, e).catch(() => {})
      onExitToMenu()
    } finally {
      setBusy(false)
    }
  }

  async function confirmAbandon() {
    if (!accountId || busy) return
    setBusy(true)
    try {
      await abandonRun(accountId, currentElapsed()).catch(() => {})
      onAbandon()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="ゲームメニュー"
      className="in-game-menu"
    >
      <div className="in-game-menu__card">
        {mode === 'main' && (
          <>
            <Button onClick={onClose}>続ける</Button>
            <Button onClick={() => setMode('settings')}>音量設定</Button>
            <Button onClick={() => void exit()}>メニューに戻る</Button>
            <Button variant="danger" onClick={() => setMode('confirm-abandon')}>あきらめる</Button>
          </>
        )}
        {mode === 'settings' && (
          <SettingsScreen onBack={() => setMode('main')} />
        )}
        {mode === 'confirm-abandon' && (
          <>
            <p>本当にこのランを放棄しますか？</p>
            <Button variant="danger" onClick={() => void confirmAbandon()}>放棄する</Button>
            <Button variant="secondary" onClick={() => setMode('main')}>キャンセル</Button>
          </>
        )}
      </div>
    </div>
  )
}
```

- [ ] **Step 3: Run tests**

Run: `cd src/Client && npm test -- --run InGameMenuScreen MapScreen`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Client/src/screens/InGameMenuScreen.tsx src/Client/src/screens/InGameMenuScreen.test.tsx src/Client/src/screens/MapScreen.tsx src/Client/src/screens/MapScreen.test.tsx
git commit -m "feat(client): add MapScreen and InGameMenuScreen"
```

---

## Task 22: App.tsx に MapScreen ルート追加

**Files:**
- Modify: `src/Client/src/App.tsx`

- [ ] **Step 1: Rewrite routing**

```tsx
import { useEffect, useState } from 'react'
import { getAccount } from './api/accounts'
import { ApiError } from './api/client'
import type { RunSnapshotDto } from './api/types'
import { Button } from './components/Button'
import { useAccount } from './context/AccountContext'
import { LoginScreen } from './screens/LoginScreen'
import { MainMenuScreen } from './screens/MainMenuScreen'
import { MapScreen } from './screens/MapScreen'
import { SettingsScreen } from './screens/SettingsScreen'

type Screen =
  | { kind: 'bootstrapping' }
  | { kind: 'login' }
  | { kind: 'main-menu' }
  | { kind: 'settings' }
  | { kind: 'map'; snapshot: RunSnapshotDto }
  | { kind: 'bootstrap-error'; message: string }

export default function App() {
  const { accountId, logout } = useAccount()
  const [screen, setScreen] = useState<Screen>({ kind: 'bootstrapping' })

  useEffect(() => {
    let cancelled = false
    async function bootstrap() {
      if (!accountId) {
        if (!cancelled) setScreen({ kind: 'login' })
        return
      }
      try {
        await getAccount(accountId)
        if (!cancelled) setScreen({ kind: 'main-menu' })
      } catch (e) {
        if (cancelled) return
        if (e instanceof ApiError && e.status === 404) {
          logout()
          setScreen({ kind: 'login' })
        } else {
          setScreen({ kind: 'bootstrap-error', message: 'サーバに接続できませんでした。' })
        }
      }
    }
    void bootstrap()
    return () => { cancelled = true }
  }, [accountId, logout])

  if (screen.kind === 'bootstrapping') {
    return <main className="bootstrap"><p>起動中…</p></main>
  }
  if (screen.kind === 'bootstrap-error') {
    return (
      <main className="bootstrap-error">
        <p>{screen.message}</p>
        <Button onClick={() => setScreen({ kind: 'bootstrapping' })}>再試行</Button>
      </main>
    )
  }
  if (screen.kind === 'login') {
    return <LoginScreen onLoggedIn={() => setScreen({ kind: 'main-menu' })} />
  }
  if (screen.kind === 'main-menu') {
    return (
      <MainMenuScreen
        onOpenSettings={() => setScreen({ kind: 'settings' })}
        onLogout={() => { logout(); setScreen({ kind: 'login' }) }}
        onStartRun={(snap) => setScreen({ kind: 'map', snapshot: snap })}
      />
    )
  }
  if (screen.kind === 'map') {
    return (
      <MapScreen
        snapshot={screen.snapshot}
        onExitToMenu={() => setScreen({ kind: 'main-menu' })}
        onAbandon={() => setScreen({ kind: 'main-menu' })}
      />
    )
  }
  return <SettingsScreen onBack={() => setScreen({ kind: 'main-menu' })} />
}
```

- [ ] **Step 2: Typecheck**

Run: `cd src/Client && npm run build`
Expected: PASS

- [ ] **Step 3: Run all client tests**

Run: `cd src/Client && npm test -- --run`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/Client/src/App.tsx
git commit -m "feat(client): route MainMenu → MapScreen on run start"
```

---

## Task 23: 統合テスト — フルフロー

**Files:**
- Modify: `tests/Server.Tests/Controllers/RunsControllerTests.cs`

- [ ] **Step 1: Append integration test**

describe ブロック末尾 `[Fact] public async Task NoHeader_Returns400` の直前に追加：

```csharp
    [Fact]
    public async Task FullFlow_NewMoveHeartbeatCurrent_AccumulatesPlaySeconds()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "ivy");
        WithAccount(client, "ivy");

        var newRes = await client.PostAsync("/api/v1/runs/new", content: null);
        var newDoc = JsonDocument.Parse(await newRes.Content.ReadAsStringAsync());
        int startId = newDoc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32();
        int firstTarget = -1;
        foreach (var n in newDoc.RootElement.GetProperty("map").GetProperty("nodes").EnumerateArray())
            if (n.GetProperty("id").GetInt32() == startId)
                firstTarget = n.GetProperty("outgoingNodeIds")[0].GetInt32();

        await client.PostAsJsonAsync("/api/v1/runs/current/move",
            new { nodeId = firstTarget, elapsedSeconds = 4 });
        await client.PostAsJsonAsync("/api/v1/runs/current/heartbeat", new { elapsedSeconds = 3 });

        var cur = await client.GetAsync("/api/v1/runs/current");
        var doc = JsonDocument.Parse(await cur.Content.ReadAsStringAsync());
        Assert.Equal(firstTarget, doc.RootElement.GetProperty("run").GetProperty("currentNodeId").GetInt32());
        Assert.Equal(7, doc.RootElement.GetProperty("run").GetProperty("playSeconds").GetInt64());
    }
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Server.Tests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add tests/Server.Tests/Controllers/RunsControllerTests.cs
git commit -m "test(server): add full-flow integration test for run progression"
```

---

## Task 24: 手動確認とドキュメント更新

**Files:**
- Modify: `CLAUDE.md`（任意）

- [ ] **Step 1: ビルド + 全テスト**

Run: `dotnet test`
Expected: PASS（すべての Core / Server テスト）

Run: `cd src/Client && npm test -- --run && npm run build`
Expected: PASS

- [ ] **Step 2: 起動して手動確認**

Server 起動: `dotnet run --project src/Server`
Client 起動: `cd src/Client && npm run dev`

ブラウザで以下を確認：
1. ログイン → メニュー → シングルプレイ → MapScreen 表示
2. スタートノードから隣接ノードをクリック → 移動
3. ブラウザリロード → 同じ状態から復帰
4. ⚙ → メニューに戻る → 続きから → 同じ状態
5. ⚙ → あきらめる → 放棄する → メニューで保存済みバッジが消える
6. シングルプレイをもう一度 → ダイアログ無し・即新規
7. 進めてボスノードに到達 → 「ここから先は Phase 5 以降」メッセージ

- [ ] **Step 3: Tag phase4-complete**

```bash
git tag phase4-complete
```

- [ ] **Step 4: Finishing skill 呼び出し**

実装者は `superpowers:finishing-a-development-branch` を呼び出して、merge / PR / keep / discard を選択する。

---

## Self-Review Notes

このプランが spec のどの部分をカバーしているか：

- §3 RunState v2 → Task 7, 8
- §4 UnknownResolver → Task 1-5, 6（act1 JSON）
- §5 RunActions.SelectNextNode → Task 9, 10
- §6 サーバ API → Task 13, 14, 15, 23（統合）
- §6 FileSaveRepository のスキーマ不一致 → Task 11
- §6 RunStartService → Task 12
- §7 プレイ秒数 → Task 12, 15（クランプ）, 20（MapScreen heartbeat）, 21（InGameMenu）
- §8 MapScreen → Task 19, 20
- §8 InGameMenuScreen → Task 21
- §8 MainMenuScreen 更新 → Task 17, 18
- §8 App.tsx ルーティング → Task 22
- §9 Core.Tests / Server.Tests / Client.Tests → 各 Task 内で TDD
- §10 Done 判定 → Task 24
