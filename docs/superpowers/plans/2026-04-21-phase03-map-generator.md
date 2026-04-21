# Phase 3 Dungeon Map Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Core ライブラリに決定論的なダンジョンマップ生成器を実装する。固定 5 列グリッド、±1 隣接エッジ、1 ルート制約検証、JSON 設定、再生成ループまで含む。

**Architecture:** `IRng` 抽象化で決定性を確保し、`DungeonMapGenerator` が純粋関数的に `MapGenerationConfig` を受け取って `DungeonMap` を返す。生成は「ノード配置→エッジ貼り付け→種別割当→全体分布検証→ルート制約検証」の 5 フェーズで、制約違反時は最大 100 回まで最初から再生成。設定は埋め込み JSON (`map-act1.json`) から読む。

**Tech Stack:** .NET 10 / C# (record, `ImmutableArray<T>`, `ImmutableDictionary<K,V>`) / `System.Text.Json` / xUnit 2.9 / 既存 `JsonOptions.Default` を再利用

**Reference:** 設計書 [`docs/superpowers/specs/2026-04-21-phase03-map-generator-design.md`](../specs/2026-04-21-phase03-map-generator-design.md)

---

## ファイル構成

### 新規作成
- `src/Core/Random/IRng.cs` — 乱数抽象インターフェイス
- `src/Core/Random/SystemRng.cs` — `System.Random` ラッパー
- `src/Core/Random/FakeRng.cs` — テスト用の事前シーケンス Rng
- `src/Core/Map/TileKind.cs` — タイル種別 enum
- `src/Core/Map/MapNode.cs` — ノード record
- `src/Core/Map/DungeonMap.cs` — マップ全体 record
- `src/Core/Map/IntRange.cs` / `TileKindPair.cs` — 値オブジェクト
- `src/Core/Map/EdgeCountWeights.cs` — 出次数の重み
- `src/Core/Map/TileDistributionRule.cs` — マップ全体分布ルール
- `src/Core/Map/FixedRowRule.cs` / `RowKindExclusion.cs` — 行固有ルール
- `src/Core/Map/PathConstraintRule.cs` — 1 ルート制約
- `src/Core/Map/MapGenerationConfig.cs` — 上記をまとめた config ルート
- `src/Core/Map/MapGenerationException.cs` — 再生成失敗例外
- `src/Core/Map/IDungeonMapGenerator.cs` — 生成器インターフェイス
- `src/Core/Map/DungeonMapGenerator.cs` — 実装（5 フェーズ）
- `src/Core/Map/MapGenerationConfigLoader.cs` — 埋め込み JSON → Config
- `src/Core/Map/Config/map-act1.json` — Act 1 用既定値（埋め込みリソース）
- `tests/Core.Tests/Random/IRngTests.cs`
- `tests/Core.Tests/Map/DungeonMapGeneratorTests.cs`
- `tests/Core.Tests/Map/MapGenerationConfigLoaderTests.cs`

### 変更
- `src/Core/Core.csproj` — `Map\Config\*.json` を `EmbeddedResource` に追加
- `src/Server/Program.cs` — `IDungeonMapGenerator` と `MapGenerationConfig` を DI 登録

---

## Task 1: IRng と実装クラス

**Files:**
- Create: `src/Core/Random/IRng.cs`
- Create: `src/Core/Random/SystemRng.cs`
- Create: `src/Core/Random/FakeRng.cs`
- Test: `tests/Core.Tests/Random/IRngTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Random/IRngTests.cs`:
```csharp
using System;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Random;

public class IRngTests
{
    [Fact]
    public void SystemRng_SameSeed_ProducesSameSequence()
    {
        var a = new SystemRng(42);
        var b = new SystemRng(42);
        for (int i = 0; i < 50; i++)
            Assert.Equal(a.NextInt(0, 100), b.NextInt(0, 100));
    }

    [Fact]
    public void SystemRng_NextInt_ReturnsInRange()
    {
        var rng = new SystemRng(1);
        for (int i = 0; i < 100; i++)
        {
            var v = rng.NextInt(5, 10);
            Assert.InRange(v, 5, 9);
        }
    }

    [Fact]
    public void SystemRng_NextDouble_ReturnsUnitInterval()
    {
        var rng = new SystemRng(1);
        for (int i = 0; i < 100; i++)
        {
            var v = rng.NextDouble();
            Assert.InRange(v, 0.0, 0.9999999999);
        }
    }

    [Fact]
    public void FakeRng_ReturnsIntsInOrder()
    {
        var rng = new FakeRng(new[] { 3, 1, 4 }, Array.Empty<double>());
        Assert.Equal(3, rng.NextInt(0, 10));
        Assert.Equal(1, rng.NextInt(0, 10));
        Assert.Equal(4, rng.NextInt(0, 10));
    }

    [Fact]
    public void FakeRng_ReturnsDoublesInOrder()
    {
        var rng = new FakeRng(Array.Empty<int>(), new[] { 0.1, 0.5, 0.9 });
        Assert.Equal(0.1, rng.NextDouble());
        Assert.Equal(0.5, rng.NextDouble());
        Assert.Equal(0.9, rng.NextDouble());
    }

    [Fact]
    public void FakeRng_ExhaustedSequence_Throws()
    {
        var rng = new FakeRng(new[] { 1 }, Array.Empty<double>());
        rng.NextInt(0, 10);
        Assert.Throws<InvalidOperationException>(() => rng.NextInt(0, 10));
    }

    [Fact]
    public void FakeRng_IntOutOfRange_Throws()
    {
        var rng = new FakeRng(new[] { 42 }, Array.Empty<double>());
        Assert.Throws<InvalidOperationException>(() => rng.NextInt(0, 10));
    }
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter FullyQualifiedName~IRngTests`
Expected: ビルドエラー（`IRng` などが未定義）

- [ ] **Step 3: `IRng` を実装**

`src/Core/Random/IRng.cs`:
```csharp
namespace RoguelikeCardGame.Core.Random;

/// <summary>決定論的なマップ生成やその他のランダム処理に使う乱数抽象。</summary>
/// <remarks>
/// VR (Udon#) 移植時は本インターフェイスを削除し、呼び出し側が UnityEngine.Random を直接叩く。
/// Phase 3 のマップ生成は VR 側では「事前生成済み JSON を読み込む」運用に切り替わる想定。
/// </remarks>
public interface IRng
{
    /// <summary>[minInclusive, maxExclusive) の範囲で整数を返す。</summary>
    int NextInt(int minInclusive, int maxExclusive);

    /// <summary>[0.0, 1.0) の範囲で double を返す。</summary>
    double NextDouble();
}
```

- [ ] **Step 4: `SystemRng` を実装**

`src/Core/Random/SystemRng.cs`:
```csharp
using SysRandom = System.Random;

namespace RoguelikeCardGame.Core.Random;

/// <summary><see cref="SysRandom"/> をラップする <see cref="IRng"/> 実装。</summary>
public sealed class SystemRng : IRng
{
    private readonly SysRandom _random;

    public SystemRng(int seed)
    {
        _random = new SysRandom(seed);
    }

    public int NextInt(int minInclusive, int maxExclusive) =>
        _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();
}
```

- [ ] **Step 5: `FakeRng` を実装**

`src/Core/Random/FakeRng.cs`:
```csharp
using System;

namespace RoguelikeCardGame.Core.Random;

/// <summary>テスト用：事前に用意した int/double シーケンスを順に返す <see cref="IRng"/>。</summary>
public sealed class FakeRng : IRng
{
    private readonly int[] _ints;
    private readonly double[] _doubles;
    private int _intIndex;
    private int _doubleIndex;

    public FakeRng(int[] intSequence, double[] doubleSequence)
    {
        _ints = intSequence ?? throw new ArgumentNullException(nameof(intSequence));
        _doubles = doubleSequence ?? throw new ArgumentNullException(nameof(doubleSequence));
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (_intIndex >= _ints.Length)
            throw new InvalidOperationException("FakeRng int sequence exhausted.");
        var v = _ints[_intIndex++];
        if (v < minInclusive || v >= maxExclusive)
            throw new InvalidOperationException(
                $"FakeRng value {v} out of range [{minInclusive}, {maxExclusive}).");
        return v;
    }

    public double NextDouble()
    {
        if (_doubleIndex >= _doubles.Length)
            throw new InvalidOperationException("FakeRng double sequence exhausted.");
        return _doubles[_doubleIndex++];
    }
}
```

- [ ] **Step 6: テストを走らせて成功を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter FullyQualifiedName~IRngTests`
Expected: すべてのテストが PASS

- [ ] **Step 7: コミット**

```bash
git add src/Core/Random tests/Core.Tests/Random
git commit -m "feat(core): add IRng abstraction with SystemRng and FakeRng"
```

---

## Task 2: TileKind / MapNode / DungeonMap

**Files:**
- Create: `src/Core/Map/TileKind.cs`
- Create: `src/Core/Map/MapNode.cs`
- Create: `src/Core/Map/DungeonMap.cs`
- Test: `tests/Core.Tests/Map/DungeonMapTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Map/DungeonMapTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Map;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class DungeonMapTests
{
    [Fact]
    public void MapNode_EqualsByValue()
    {
        var a = new MapNode(0, 1, 2, TileKind.Enemy, ImmutableArray.Create(1, 2));
        var b = new MapNode(0, 1, 2, TileKind.Enemy, ImmutableArray.Create(1, 2));
        Assert.Equal(a, b);
    }

    [Fact]
    public void DungeonMap_GetNode_ReturnsNodeById()
    {
        var nodes = ImmutableArray.Create(
            new MapNode(0, 0, 2, TileKind.Start, ImmutableArray.Create(1)),
            new MapNode(1, 1, 2, TileKind.Enemy, ImmutableArray<int>.Empty));
        var map = new DungeonMap(nodes, StartNodeId: 0, BossNodeId: 1);
        Assert.Equal(TileKind.Start, map.GetNode(0).Kind);
        Assert.Equal(TileKind.Enemy, map.GetNode(1).Kind);
    }

    [Fact]
    public void DungeonMap_NodesInRow_FiltersByRow()
    {
        var nodes = ImmutableArray.Create(
            new MapNode(0, 0, 2, TileKind.Start, ImmutableArray.Create(1, 2)),
            new MapNode(1, 1, 1, TileKind.Enemy, ImmutableArray<int>.Empty),
            new MapNode(2, 1, 3, TileKind.Enemy, ImmutableArray<int>.Empty));
        var map = new DungeonMap(nodes, 0, 2);
        var row1 = map.NodesInRow(1).ToList();
        Assert.Equal(2, row1.Count);
        Assert.All(row1, n => Assert.Equal(1, n.Row));
    }

    [Fact]
    public void TileKind_EnumValues_Exist()
    {
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Start));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Enemy));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Elite));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Rest));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Merchant));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Treasure));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Unknown));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Boss));
    }
}
```

- [ ] **Step 2: テストを走らせて失敗確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapTests`
Expected: ビルドエラー

- [ ] **Step 3: `TileKind` を実装**

`src/Core/Map/TileKind.cs`:
```csharp
namespace RoguelikeCardGame.Core.Map;

/// <summary>ダンジョンマップ上のマス種別。</summary>
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

- [ ] **Step 4: `MapNode` を実装**

`src/Core/Map/MapNode.cs`:
```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// ダンジョンマップの 1 マス。
/// </summary>
/// <remarks>
/// VR (Udon#) 移植時：record → sealed class、ImmutableArray&lt;int&gt; → int[] に置換。
/// </remarks>
public sealed record MapNode(
    int Id,
    int Row,
    int Column,
    TileKind Kind,
    ImmutableArray<int> OutgoingNodeIds);
```

- [ ] **Step 5: `DungeonMap` を実装**

`src/Core/Map/DungeonMap.cs`:
```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// 生成済みのダンジョンマップ。ノード集合と Start/Boss の Id を保持する。
/// </summary>
public sealed record DungeonMap(
    ImmutableArray<MapNode> Nodes,
    int StartNodeId,
    int BossNodeId)
{
    /// <summary>Id でノードを取得。Id は <see cref="Nodes"/> の index と一致する想定。</summary>
    public MapNode GetNode(int id) => Nodes[id];

    /// <summary>指定行のノードを列挙する（単純走査）。</summary>
    public IEnumerable<MapNode> NodesInRow(int row) => Nodes.Where(n => n.Row == row);
}
```

- [ ] **Step 6: テスト成功を確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapTests`
Expected: すべて PASS

- [ ] **Step 7: コミット**

```bash
git add src/Core/Map/TileKind.cs src/Core/Map/MapNode.cs src/Core/Map/DungeonMap.cs tests/Core.Tests/Map/DungeonMapTests.cs
git commit -m "feat(core): add TileKind, MapNode, DungeonMap domain models"
```

---

## Task 3: 設定 record 群

**Files:**
- Create: `src/Core/Map/IntRange.cs`
- Create: `src/Core/Map/TileKindPair.cs`
- Create: `src/Core/Map/EdgeCountWeights.cs`
- Create: `src/Core/Map/TileDistributionRule.cs`
- Create: `src/Core/Map/FixedRowRule.cs`
- Create: `src/Core/Map/RowKindExclusion.cs`
- Create: `src/Core/Map/PathConstraintRule.cs`
- Create: `src/Core/Map/MapGenerationConfig.cs`
- Test: `tests/Core.Tests/Map/MapGenerationConfigTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Map/MapGenerationConfigTests.cs`:
```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Map;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class MapGenerationConfigTests
{
    [Fact]
    public void IntRange_EqualsByValue()
    {
        Assert.Equal(new IntRange(1, 3), new IntRange(1, 3));
    }

    [Fact]
    public void TileKindPair_EqualsByValue()
    {
        Assert.Equal(
            new TileKindPair(TileKind.Rest, TileKind.Rest),
            new TileKindPair(TileKind.Rest, TileKind.Rest));
    }

    [Fact]
    public void MapGenerationConfig_ConstructsWithAllFields()
    {
        var config = new MapGenerationConfig(
            RowCount: 15,
            ColumnCount: 5,
            RowNodeCountMin: 2,
            RowNodeCountMax: 4,
            EdgeWeights: new EdgeCountWeights(82, 16, 2),
            TileDistribution: new TileDistributionRule(
                BaseWeights: ImmutableDictionary<TileKind, double>.Empty,
                MinPerMap: ImmutableDictionary<TileKind, int>.Empty,
                MaxPerMap: ImmutableDictionary<TileKind, int>.Empty),
            FixedRows: ImmutableArray.Create(new FixedRowRule(9, TileKind.Treasure)),
            RowKindExclusions: ImmutableArray.Create(new RowKindExclusion(14, TileKind.Rest)),
            PathConstraints: new PathConstraintRule(
                PerPathCount: ImmutableDictionary<TileKind, IntRange>.Empty,
                MinEliteRow: 6,
                ForbiddenConsecutive: ImmutableArray<TileKindPair>.Empty),
            MaxRegenerationAttempts: 100);

        Assert.Equal(15, config.RowCount);
        Assert.Equal(9, config.FixedRows[0].Row);
    }
}
```

- [ ] **Step 2: テスト失敗確認**

Run: `dotnet test --filter FullyQualifiedName~MapGenerationConfigTests`
Expected: ビルドエラー

- [ ] **Step 3: 値オブジェクト record を作成**

`src/Core/Map/IntRange.cs`:
```csharp
namespace RoguelikeCardGame.Core.Map;

/// <summary>整数の閉区間 [Min, Max]。</summary>
public sealed record IntRange(int Min, int Max);
```

`src/Core/Map/TileKindPair.cs`:
```csharp
namespace RoguelikeCardGame.Core.Map;

/// <summary>タイル種別のペア。エッジ First → Second の連続を表現する。</summary>
public sealed record TileKindPair(TileKind First, TileKind Second);
```

`src/Core/Map/EdgeCountWeights.cs`:
```csharp
namespace RoguelikeCardGame.Core.Map;

/// <summary>出次数 1/2/3 それぞれの重み（確率は Weight1/(W1+W2+W3) のように正規化して使う）。</summary>
public sealed record EdgeCountWeights(double Weight1, double Weight2, double Weight3);
```

`src/Core/Map/FixedRowRule.cs`:
```csharp
namespace RoguelikeCardGame.Core.Map;

/// <summary>指定行の全ノードを単一の <see cref="TileKind"/> に固定するルール。</summary>
public sealed record FixedRowRule(int Row, TileKind Kind);
```

`src/Core/Map/RowKindExclusion.cs`:
```csharp
namespace RoguelikeCardGame.Core.Map;

/// <summary>指定行で <see cref="ExcludedKind"/> を割り当てないルール。</summary>
public sealed record RowKindExclusion(int Row, TileKind ExcludedKind);
```

- [ ] **Step 4: 複合 record を作成**

`src/Core/Map/TileDistributionRule.cs`:
```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// マップ全体のタイル分布ルール。
/// </summary>
/// <param name="BaseWeights">各 Kind の割当時重み（合計は任意、内部で正規化）。キー欠落の Kind は重み 0 = 選ばれない。</param>
/// <param name="MinPerMap">マップ全体での最小個数。下回ったら再生成。キー欠落は制約なし。</param>
/// <param name="MaxPerMap">マップ全体での最大個数。超えたら再生成（割当時にも候補から除外）。キー欠落は制約なし。</param>
public sealed record TileDistributionRule(
    ImmutableDictionary<TileKind, double> BaseWeights,
    ImmutableDictionary<TileKind, int> MinPerMap,
    ImmutableDictionary<TileKind, int> MaxPerMap);
```

`src/Core/Map/PathConstraintRule.cs`:
```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// start → boss の 1 ルート上に課される制約。
/// </summary>
/// <param name="PerPathCount">1 ルートでの Kind 別許容個数 [Min, Max]。キー欠落 = 制約なし。</param>
/// <param name="MinEliteRow">Elite を配置できる最小行。これ未満の行では Elite を候補から外す。</param>
/// <param name="ForbiddenConsecutive">First → Second の順にエッジで隣接することを禁止。</param>
public sealed record PathConstraintRule(
    ImmutableDictionary<TileKind, IntRange> PerPathCount,
    int MinEliteRow,
    ImmutableArray<TileKindPair> ForbiddenConsecutive);
```

`src/Core/Map/MapGenerationConfig.cs`:
```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// ダンジョンマップ生成の全設定。JSON から deserialize して <see cref="IDungeonMapGenerator.Generate"/> に渡す。
/// </summary>
public sealed record MapGenerationConfig(
    int RowCount,
    int ColumnCount,
    int RowNodeCountMin,
    int RowNodeCountMax,
    EdgeCountWeights EdgeWeights,
    TileDistributionRule TileDistribution,
    ImmutableArray<FixedRowRule> FixedRows,
    ImmutableArray<RowKindExclusion> RowKindExclusions,
    PathConstraintRule PathConstraints,
    int MaxRegenerationAttempts);
```

- [ ] **Step 5: テスト成功確認**

Run: `dotnet test --filter FullyQualifiedName~MapGenerationConfigTests`
Expected: すべて PASS

- [ ] **Step 6: コミット**

```bash
git add src/Core/Map tests/Core.Tests/Map/MapGenerationConfigTests.cs
git commit -m "feat(core): add MapGenerationConfig record family"
```

---

## Task 4: MapGenerationException と IDungeonMapGenerator

**Files:**
- Create: `src/Core/Map/MapGenerationException.cs`
- Create: `src/Core/Map/IDungeonMapGenerator.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Map/MapGenerationExceptionTests.cs`:
```csharp
using System;
using RoguelikeCardGame.Core.Map;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class MapGenerationExceptionTests
{
    [Fact]
    public void Constructor_SetsAttemptCountAndReason()
    {
        var ex = new MapGenerationException(100, "path-constraint:Enemy=7>6");
        Assert.Equal(100, ex.AttemptCount);
        Assert.Equal("path-constraint:Enemy=7>6", ex.FailureReason);
        Assert.Contains("path-constraint:Enemy=7>6", ex.Message);
    }

    [Fact]
    public void Constructor_WithInner_SetsInnerException()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new MapGenerationException(5, "inner-failure", inner);
        Assert.Same(inner, ex.InnerException);
    }
}
```

- [ ] **Step 2: テスト失敗確認**

Run: `dotnet test --filter FullyQualifiedName~MapGenerationExceptionTests`
Expected: ビルドエラー

- [ ] **Step 3: `MapGenerationException` を実装**

`src/Core/Map/MapGenerationException.cs`:
```csharp
using System;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// 指定回数の再生成試行でも制約を満たすマップが生成できなかったことを示す例外。
/// </summary>
public sealed class MapGenerationException : Exception
{
    public int AttemptCount { get; }
    public string FailureReason { get; }

    public MapGenerationException(int attemptCount, string failureReason)
        : base($"Map generation failed after {attemptCount} attempts: {failureReason}")
    {
        AttemptCount = attemptCount;
        FailureReason = failureReason;
    }

    public MapGenerationException(int attemptCount, string failureReason, Exception inner)
        : base($"Map generation failed after {attemptCount} attempts: {failureReason}", inner)
    {
        AttemptCount = attemptCount;
        FailureReason = failureReason;
    }
}
```

- [ ] **Step 4: `IDungeonMapGenerator` を実装**

`src/Core/Map/IDungeonMapGenerator.cs`:
```csharp
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Map;

/// <summary>ダンジョンマップ生成器のインターフェイス。</summary>
public interface IDungeonMapGenerator
{
    /// <summary>
    /// 指定の乱数と設定でマップを生成する。制約を満たすまで内部で再生成を試行し、
    /// <see cref="MapGenerationConfig.MaxRegenerationAttempts"/> を超えたら <see cref="MapGenerationException"/> を投げる。
    /// </summary>
    DungeonMap Generate(IRng rng, MapGenerationConfig config);
}
```

- [ ] **Step 5: テスト成功確認**

Run: `dotnet test --filter FullyQualifiedName~MapGenerationExceptionTests`
Expected: すべて PASS

- [ ] **Step 6: コミット**

```bash
git add src/Core/Map/MapGenerationException.cs src/Core/Map/IDungeonMapGenerator.cs tests/Core.Tests/Map/MapGenerationExceptionTests.cs
git commit -m "feat(core): add MapGenerationException and IDungeonMapGenerator"
```

---

## Task 5: DungeonMapGenerator — ノード配置フェーズ

**Files:**
- Create: `src/Core/Map/DungeonMapGenerator.cs`
- Test: `tests/Core.Tests/Map/DungeonMapGeneratorTests.cs`

このタスクではまず「ノード配置のみ」行う Generator スケルトンを書き、エッジ・種別は次タスク以降で追加する。

- [ ] **Step 1: ノード配置テストを書く**

`tests/Core.Tests/Map/DungeonMapGeneratorTests.cs` を新規作成：
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class DungeonMapGeneratorTests
{
    private static MapGenerationConfig BaseConfig() => new(
        RowCount: 15,
        ColumnCount: 5,
        RowNodeCountMin: 2,
        RowNodeCountMax: 4,
        EdgeWeights: new EdgeCountWeights(82, 16, 2),
        TileDistribution: new TileDistributionRule(
            BaseWeights: new[]
            {
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Enemy, 45),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Elite, 6),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Rest, 12),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Merchant, 5),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Unknown, 32),
            }.ToImmutableDictionary(),
            MinPerMap: new[]
            {
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Merchant, 3),
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Elite, 2),
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Unknown, 6),
            }.ToImmutableDictionary(),
            MaxPerMap: new[]
            {
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Merchant, 3),
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Elite, 4),
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Unknown, 10),
            }.ToImmutableDictionary()),
        FixedRows: ImmutableArray.Create(
            new FixedRowRule(9, TileKind.Treasure),
            new FixedRowRule(15, TileKind.Rest)),
        RowKindExclusions: ImmutableArray.Create(
            new RowKindExclusion(14, TileKind.Rest)),
        PathConstraints: new PathConstraintRule(
            PerPathCount: new[]
            {
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Enemy, new IntRange(4, 6)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Elite, new IntRange(0, 2)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Rest, new IntRange(1, 3)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Merchant, new IntRange(1, 2)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Treasure, new IntRange(1, 1)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Unknown, new IntRange(3, 5)),
            }.ToImmutableDictionary(),
            MinEliteRow: 6,
            ForbiddenConsecutive: ImmutableArray.Create(new TileKindPair(TileKind.Rest, TileKind.Rest))),
        MaxRegenerationAttempts: 100);

    [Fact]
    public void Generate_HasStartAtRow0Column2()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var start = map.GetNode(map.StartNodeId);
        Assert.Equal(0, start.Row);
        Assert.Equal(2, start.Column);
        Assert.Equal(TileKind.Start, start.Kind);
    }

    [Fact]
    public void Generate_HasBossAtRow16Column2()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var boss = map.GetNode(map.BossNodeId);
        Assert.Equal(16, boss.Row);
        Assert.Equal(2, boss.Column);
        Assert.Equal(TileKind.Boss, boss.Kind);
    }

    [Fact]
    public void Generate_MiddleRowsHaveNodeCountInConfigRange()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int r = 1; r <= 15; r++)
        {
            var count = map.NodesInRow(r).Count();
            Assert.InRange(count, 2, 4);
        }
    }

    [Fact]
    public void Generate_AllColumnsInRange()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.Nodes, n => Assert.InRange(n.Column, 0, 4));
    }

    [Fact]
    public void Generate_NodeIdsAreSequential()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int i = 0; i < map.Nodes.Length; i++)
            Assert.Equal(i, map.Nodes[i].Id);
    }

    [Fact]
    public void Generate_NodesOrderedByRowThenColumn()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int i = 1; i < map.Nodes.Length; i++)
        {
            var prev = map.Nodes[i - 1];
            var curr = map.Nodes[i];
            Assert.True(
                prev.Row < curr.Row || (prev.Row == curr.Row && prev.Column < curr.Column),
                $"Nodes not ordered: idx {i - 1}={prev.Row},{prev.Column} idx {i}={curr.Row},{curr.Column}");
        }
    }
}
```

- [ ] **Step 2: テスト失敗確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: ビルドエラー（`DungeonMapGenerator` が未定義）

- [ ] **Step 3: Generator スケルトン + ノード配置を実装**

`src/Core/Map/DungeonMapGenerator.cs`:
```csharp
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
```

- [ ] **Step 4: テスト成功確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: すべて PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Map/DungeonMapGenerator.cs tests/Core.Tests/Map/DungeonMapGeneratorTests.cs
git commit -m "feat(core): add DungeonMapGenerator node placement phase"
```

---

## Task 6: エッジ貼り付けフェーズ（±1 隣接 + Start/Boss 例外 + 到達性）

**Files:**
- Modify: `src/Core/Map/DungeonMapGenerator.cs`
- Modify: `tests/Core.Tests/Map/DungeonMapGeneratorTests.cs`

- [ ] **Step 1: エッジ関連テストを追加**

`tests/Core.Tests/Map/DungeonMapGeneratorTests.cs` の `DungeonMapGeneratorTests` クラスに追加：
```csharp
    [Fact]
    public void Generate_StartConnectsToAllRow1Nodes()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var start = map.GetNode(map.StartNodeId);
        var row1Ids = map.NodesInRow(1).Select(n => n.Id).OrderBy(i => i).ToArray();
        Assert.Equal(row1Ids, start.OutgoingNodeIds.OrderBy(i => i));
    }

    [Fact]
    public void Generate_Row15AllConnectToBoss()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        foreach (var n in map.NodesInRow(15))
        {
            Assert.Single(n.OutgoingNodeIds);
            Assert.Equal(map.BossNodeId, n.OutgoingNodeIds[0]);
        }
    }

    [Fact]
    public void Generate_BossHasNoOutgoingEdges()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.Empty(map.GetNode(map.BossNodeId).OutgoingNodeIds);
    }

    [Fact]
    public void Generate_MiddleEdgesRespectColumnAdjacency()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int r = 1; r <= 14; r++) // Row 15 → Boss は例外なので除外
        {
            foreach (var n in map.NodesInRow(r))
            {
                foreach (var dstId in n.OutgoingNodeIds)
                {
                    var dst = map.GetNode(dstId);
                    Assert.Equal(r + 1, dst.Row);
                    Assert.True(
                        System.Math.Abs(n.Column - dst.Column) <= 1,
                        $"Edge {n.Id}(row={r}, col={n.Column}) -> {dst.Id}(col={dst.Column}) violates ±1 adjacency");
                }
            }
        }
    }

    [Fact]
    public void Generate_MiddleOutDegreeBetween1And3()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int r = 1; r <= 14; r++)
        {
            foreach (var n in map.NodesInRow(r))
                Assert.InRange(n.OutgoingNodeIds.Length, 1, 3);
        }
    }

    [Fact]
    public void Generate_BossReachableFromStart()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var visited = new System.Collections.Generic.HashSet<int>();
        var stack = new System.Collections.Generic.Stack<int>();
        stack.Push(map.StartNodeId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id)) continue;
            foreach (var next in map.GetNode(id).OutgoingNodeIds)
                stack.Push(next);
        }
        Assert.Contains(map.BossNodeId, visited);
    }

    [Fact]
    public void Generate_OutgoingIdsAreSorted()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        foreach (var n in map.Nodes)
        {
            var sorted = n.OutgoingNodeIds.OrderBy(i => i).ToArray();
            Assert.Equal(sorted, n.OutgoingNodeIds.ToArray());
        }
    }
```

- [ ] **Step 2: テスト失敗確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: 新規テスト全失敗（エッジ未実装）

- [ ] **Step 3: エッジ貼り付けを実装**

`src/Core/Map/DungeonMapGenerator.cs` の `Generate` を以下に置き換え（`PlaceNodes` はそのまま）。候補ゼロや到達性違反時は一旦 `while` ループで無限リトライする（`MaxRegenerationAttempts` による打ち切りは Task 8 で追加）：

```csharp
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
        int lastMiddleRow = config.RowCount;   // = 15
        int bossRow = config.RowCount + 1;     // = 16
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
                    // 隣接候補ゼロ → 生成失敗扱い。再試行（後続タスクで再生成ループに昇格）。
                    return ImmutableArray<MapNode>.Empty;
                }
                int d = PickOutDegree(rng, config.EdgeWeights, candidates.Count);
                // Fisher-Yates で前 d 個を選ぶ
                for (int i = 0; i < d; i++)
                {
                    int j = rng.NextInt(i, candidates.Count);
                    (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                }
                foreach (var dst in candidates.Take(d))
                    outgoing[src.Id].Add(dst.Id);
            }
        }

        // Row 15 → Boss（列制約なし、出次数 1 固定）
        foreach (var r15 in byRow[lastMiddleRow])
            outgoing[r15.Id].Add(bossId);

        // 元の nodes を更新して返す
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
```

注記：`ConnectEdges` が候補ゼロで `ImmutableArray<MapNode>.Empty` を返した場合、`Generate` はそれを検知して再試行する。Task 8 で `MaxRegenerationAttempts` による打ち切り・失敗理由の記録に置き換える。

- [ ] **Step 4: テスト成功確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: すべて PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Map/DungeonMapGenerator.cs tests/Core.Tests/Map/DungeonMapGeneratorTests.cs
git commit -m "feat(core): implement edge placement with column adjacency and reachability"
```

---

## Task 7: タイル種別割当フェーズ

**Files:**
- Modify: `src/Core/Map/DungeonMapGenerator.cs`
- Modify: `tests/Core.Tests/Map/DungeonMapGeneratorTests.cs`

- [ ] **Step 1: 種別割当テストを追加**

`DungeonMapGeneratorTests` に追加：
```csharp
    [Fact]
    public void Generate_Row1AllEnemy()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.NodesInRow(1), n => Assert.Equal(TileKind.Enemy, n.Kind));
    }

    [Fact]
    public void Generate_Row9AllTreasure()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.NodesInRow(9), n => Assert.Equal(TileKind.Treasure, n.Kind));
    }

    [Fact]
    public void Generate_Row15AllRest()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.NodesInRow(15), n => Assert.Equal(TileKind.Rest, n.Kind));
    }

    [Fact]
    public void Generate_Row14HasNoRest()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.NodesInRow(14), n => Assert.NotEqual(TileKind.Rest, n.Kind));
    }

    [Fact]
    public void Generate_EliteOnlyInRow6OrLater()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        foreach (var n in map.Nodes.Where(n => n.Kind == TileKind.Elite))
            Assert.True(n.Row >= 6, $"Elite at row {n.Row} (< 6)");
    }

    [Fact]
    public void Generate_TileDistributionMinMaxPerMap()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        int merchants = map.Nodes.Count(n => n.Kind == TileKind.Merchant);
        int elites = map.Nodes.Count(n => n.Kind == TileKind.Elite);
        int unknowns = map.Nodes.Count(n => n.Kind == TileKind.Unknown);
        Assert.InRange(merchants, 3, 3);
        Assert.InRange(elites, 2, 4);
        Assert.InRange(unknowns, 6, 10);
    }
```

- [ ] **Step 2: テスト失敗確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: 新規テスト失敗（現状は全ノード Enemy なので Row 9/15 等の検証が落ちる）

- [ ] **Step 3: 種別割当ロジックを `DungeonMapGenerator` に追加**

`Generate` 内の暫定 `withKinds` ロジックを削除し、以下の `AssignKinds` メソッドを追加して呼び出す：

```csharp
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
```

- [ ] **Step 4: テスト成功確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: すべて PASS

注：`TileDistribution.MinPerMap` の検証は Task 8 で追加（この段階では MaxPerMap のクリップしか効いていないため、Min 未達が残る可能性あり。ただしベース重みが現実的なら既定 config では通るはず）。もしこの時点で `Generate_TileDistributionMinMaxPerMap` が `InRange(merchants, 3, 3)` で落ちる場合は、種別割当の後に Min 検証して失敗時に再試行する暫定ロジックを追加する：

```csharp
            // 暫定：MinPerMap 違反なら再試行（Task 8 で正式な再生成ループに統合）
            bool minOk = true;
            foreach (var kv in config.TileDistribution.MinPerMap)
            {
                if (assigned.Count(n => n.Kind == kv.Key) < kv.Value) { minOk = false; break; }
            }
            if (!minOk) continue;
```

- [ ] **Step 5: コミット**

```bash
git add src/Core/Map/DungeonMapGenerator.cs tests/Core.Tests/Map/DungeonMapGeneratorTests.cs
git commit -m "feat(core): implement tile kind assignment with fixed rows, exclusions, and weighted draw"
```

---

## Task 8: ルート制約検証と再生成ループの正式化

**Files:**
- Modify: `src/Core/Map/DungeonMapGenerator.cs`
- Modify: `tests/Core.Tests/Map/DungeonMapGeneratorTests.cs`

このタスクで「全体分布検証 → ルート列挙 → PerPathCount/ForbiddenConsecutive 検証 → 再生成ループ & MaxRegenerationAttempts」を組み込む。

- [ ] **Step 1: ルート制約テストを追加**

`DungeonMapGeneratorTests` に追加：
```csharp
    [Fact]
    public void Generate_AllPathsSatisfyPerPathCount()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        foreach (var path in EnumeratePaths(map))
        {
            var counts = path.GroupBy(n => n.Kind).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kv in BaseConfig().PathConstraints.PerPathCount)
            {
                int c = counts.GetValueOrDefault(kv.Key, 0);
                Assert.InRange(c, kv.Value.Min, kv.Value.Max);
            }
        }
    }

    [Fact]
    public void Generate_NoForbiddenConsecutivePairs()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var forbidden = BaseConfig().PathConstraints.ForbiddenConsecutive;
        foreach (var path in EnumeratePaths(map))
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                foreach (var pair in forbidden)
                    Assert.False(
                        path[i].Kind == pair.First && path[i + 1].Kind == pair.Second,
                        $"Forbidden pair {pair.First}->{pair.Second} found at {path[i].Id}->{path[i + 1].Id}");
            }
        }
    }

    [Fact]
    public void Generate_Impossible_ThrowsMapGenerationException()
    {
        var baseConfig = BaseConfig();
        var impossible = baseConfig with
        {
            PathConstraints = baseConfig.PathConstraints with
            {
                PerPathCount = baseConfig.PathConstraints.PerPathCount
                    .SetItem(TileKind.Enemy, new IntRange(20, 30)),
            },
            MaxRegenerationAttempts = 5,
        };
        var ex = Assert.Throws<MapGenerationException>(
            () => new DungeonMapGenerator().Generate(new SystemRng(1), impossible));
        Assert.Equal(5, ex.AttemptCount);
        Assert.Contains("path-constraint", ex.FailureReason);
    }

    private static System.Collections.Generic.List<System.Collections.Generic.List<MapNode>> EnumeratePaths(DungeonMap map)
    {
        var results = new System.Collections.Generic.List<System.Collections.Generic.List<MapNode>>();
        var current = new System.Collections.Generic.List<MapNode>();

        void Dfs(int id)
        {
            var n = map.GetNode(id);
            current.Add(n);
            if (id == map.BossNodeId) results.Add(new System.Collections.Generic.List<MapNode>(current));
            else
                foreach (var next in n.OutgoingNodeIds) Dfs(next);
            current.RemoveAt(current.Count - 1);
        }

        Dfs(map.StartNodeId);
        return results;
    }
```

- [ ] **Step 2: テスト失敗確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: `Generate_Impossible_ThrowsMapGenerationException` が失敗（無限ループで timeout するか、例外型が違う）

- [ ] **Step 3: 再生成ループと検証を正式化**

`DungeonMapGenerator.Generate` を以下に置き換え、`ValidateDistribution` / `ValidatePathConstraints` / `EnumeratePaths` を追加：

```csharp
    public DungeonMap Generate(IRng rng, MapGenerationConfig config)
    {
        string lastReason = "no-attempt";
        for (int attempt = 1; attempt <= config.MaxRegenerationAttempts; attempt++)
        {
            var nodes = PlaceNodes(rng, config);
            var withEdges = ConnectEdges(rng, config, nodes);
            if (withEdges.IsDefaultOrEmpty) { lastReason = "edge-candidates-empty"; continue; }

            var startId = withEdges.First(n => n.Row == 0).Id;
            var bossId = withEdges.First(n => n.Row == config.RowCount + 1).Id;

            if (!IsBossReachable(withEdges, startId, bossId)) { lastReason = "boss-unreachable"; continue; }

            var assigned = AssignKinds(rng, config, withEdges);
            if (assigned.IsDefaultOrEmpty) { lastReason = "kind-candidates-empty"; continue; }

            var distReason = ValidateDistribution(assigned, config.TileDistribution);
            if (distReason is not null) { lastReason = distReason; continue; }

            var map = new DungeonMap(assigned, startId, bossId);
            var pathReason = ValidatePathConstraints(map, config.PathConstraints);
            if (pathReason is not null) { lastReason = pathReason; continue; }

            return map;
        }
        throw new MapGenerationException(config.MaxRegenerationAttempts, lastReason);
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

    private static IEnumerable<List<MapNode>> EnumeratePaths(DungeonMap map)
    {
        var results = new List<List<MapNode>>();
        var current = new List<MapNode>();

        void Dfs(int id)
        {
            var n = map.GetNode(id);
            current.Add(n);
            if (id == map.BossNodeId) results.Add(new List<MapNode>(current));
            else
                foreach (var next in n.OutgoingNodeIds) Dfs(next);
            current.RemoveAt(current.Count - 1);
        }

        Dfs(map.StartNodeId);
        return results;
    }
```

Task 7 で入れた暫定 MinPerMap 検証ブロックは削除（`ValidateDistribution` が引き継ぐ）。

- [ ] **Step 4: テスト成功確認**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: すべて PASS（`Generate_Impossible_ThrowsMapGenerationException` も PASS）

- [ ] **Step 5: コミット**

```bash
git add src/Core/Map/DungeonMapGenerator.cs tests/Core.Tests/Map/DungeonMapGeneratorTests.cs
git commit -m "feat(core): add distribution/path validation and regeneration loop"
```

---

## Task 9: 決定性テスト（seed + config → 同じマップ）

**Files:**
- Modify: `tests/Core.Tests/Map/DungeonMapGeneratorTests.cs`

- [ ] **Step 1: 決定性テストを追加**

`DungeonMapGeneratorTests` に追加：
```csharp
    [Fact]
    public void Generate_SameSeedAndConfig_ProducesIdenticalMap()
    {
        var cfg = BaseConfig();
        var a = new DungeonMapGenerator().Generate(new SystemRng(12345), cfg);
        var b = new DungeonMapGenerator().Generate(new SystemRng(12345), cfg);
        Assert.Equal(a.Nodes.Length, b.Nodes.Length);
        for (int i = 0; i < a.Nodes.Length; i++)
            Assert.Equal(a.Nodes[i], b.Nodes[i]);
        Assert.Equal(a.StartNodeId, b.StartNodeId);
        Assert.Equal(a.BossNodeId, b.BossNodeId);
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentMaps()
    {
        var cfg = BaseConfig();
        var a = new DungeonMapGenerator().Generate(new SystemRng(1), cfg);
        var b = new DungeonMapGenerator().Generate(new SystemRng(2), cfg);
        // ノード総数とか列構成で何かしら違うはず
        bool anyDiff =
            a.Nodes.Length != b.Nodes.Length ||
            !a.Nodes.Select(n => (n.Row, n.Column, n.Kind)).SequenceEqual(
                 b.Nodes.Select(n => (n.Row, n.Column, n.Kind)));
        Assert.True(anyDiff, "Different seeds produced identical map (extremely unlikely)");
    }
```

- [ ] **Step 2: テスト成功確認（実装変更なし）**

Run: `dotnet test --filter FullyQualifiedName~DungeonMapGeneratorTests`
Expected: すべて PASS

- [ ] **Step 3: コミット**

```bash
git add tests/Core.Tests/Map/DungeonMapGeneratorTests.cs
git commit -m "test(core): verify deterministic map generation from seed + config"
```

---

## Task 10: map-act1.json と MapGenerationConfigLoader

**Files:**
- Create: `src/Core/Map/Config/map-act1.json`
- Create: `src/Core/Map/MapGenerationConfigLoader.cs`
- Modify: `src/Core/Core.csproj`
- Test: `tests/Core.Tests/Map/MapGenerationConfigLoaderTests.cs`

- [ ] **Step 1: `map-act1.json` を作成**

`src/Core/Map/Config/map-act1.json`:
```json
{
  "rowCount": 15,
  "columnCount": 5,
  "rowNodeCountMin": 2,
  "rowNodeCountMax": 4,
  "edgeWeights": { "weight1": 82, "weight2": 16, "weight3": 2 },
  "tileDistribution": {
    "baseWeights": {
      "Enemy": 45,
      "Elite": 6,
      "Rest": 12,
      "Merchant": 5,
      "Unknown": 32
    },
    "minPerMap": { "Merchant": 3, "Elite": 2, "Unknown": 6 },
    "maxPerMap": { "Merchant": 3, "Elite": 4, "Unknown": 10 }
  },
  "fixedRows": [
    { "row": 9, "kind": "Treasure" },
    { "row": 15, "kind": "Rest" }
  ],
  "rowKindExclusions": [
    { "row": 14, "excludedKind": "Rest" }
  ],
  "pathConstraints": {
    "perPathCount": {
      "Enemy":    { "min": 4, "max": 6 },
      "Elite":    { "min": 0, "max": 2 },
      "Rest":     { "min": 1, "max": 3 },
      "Merchant": { "min": 1, "max": 2 },
      "Treasure": { "min": 1, "max": 1 },
      "Unknown":  { "min": 3, "max": 5 }
    },
    "minEliteRow": 6,
    "forbiddenConsecutive": [
      { "first": "Rest", "second": "Rest" }
    ]
  },
  "maxRegenerationAttempts": 100
}
```

- [ ] **Step 2: `Core.csproj` に埋め込みリソース設定を追加**

`src/Core/Core.csproj` の `<ItemGroup>` に 1 行追加：
```xml
    <EmbeddedResource Include="Map\Config\*.json" />
```

最終的な `<ItemGroup>` は以下：
```xml
  <ItemGroup>
    <EmbeddedResource Include="Data\Cards\*.json" />
    <EmbeddedResource Include="Data\Relics\*.json" />
    <EmbeddedResource Include="Data\Potions\*.json" />
    <EmbeddedResource Include="Data\Enemies\*.json" />
    <EmbeddedResource Include="Map\Config\*.json" />
  </ItemGroup>
```

- [ ] **Step 3: 失敗テストを書く**

`tests/Core.Tests/Map/MapGenerationConfigLoaderTests.cs`:
```csharp
using System;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class MapGenerationConfigLoaderTests
{
    [Fact]
    public void LoadAct1_ReturnsNonNullConfig()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        Assert.Equal(15, cfg.RowCount);
        Assert.Equal(5, cfg.ColumnCount);
        Assert.Equal(9, cfg.FixedRows[0].Row);
        Assert.Equal(TileKind.Treasure, cfg.FixedRows[0].Kind);
    }

    [Fact]
    public void LoadAct1_ConfigIsUsableByGenerator()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        var map = new DungeonMapGenerator().Generate(new SystemRng(7), cfg);
        Assert.Equal(0, map.GetNode(map.StartNodeId).Row);
        Assert.Equal(16, map.GetNode(map.BossNodeId).Row);
    }

    [Fact]
    public void Parse_UnknownField_Throws()
    {
        var badJson = "{\"rowCount\":15,\"extra\":1}";
        Assert.Throws<MapGenerationConfigException>(() => MapGenerationConfigLoader.Parse(badJson));
    }

    [Fact]
    public void Parse_MissingRequiredField_Throws()
    {
        var badJson = "{\"rowCount\":15}";
        Assert.Throws<MapGenerationConfigException>(() => MapGenerationConfigLoader.Parse(badJson));
    }
}
```

- [ ] **Step 4: テスト失敗確認**

Run: `dotnet test --filter FullyQualifiedName~MapGenerationConfigLoaderTests`
Expected: ビルドエラー

- [ ] **Step 5: `MapGenerationConfigLoader` を実装**

`src/Core/Map/MapGenerationConfigLoader.cs`:
```csharp
using System;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Map;

/// <summary>Map config JSON のパース失敗を表す例外。</summary>
public sealed class MapGenerationConfigException : Exception
{
    public MapGenerationConfigException(string message) : base(message) { }
    public MapGenerationConfigException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>埋め込み JSON から <see cref="MapGenerationConfig"/> をロードする。</summary>
public static class MapGenerationConfigLoader
{
    private const string Act1ResourceName = "RoguelikeCardGame.Core.Map.Config.map-act1.json";

    public static MapGenerationConfig LoadAct1()
    {
        var asm = typeof(MapGenerationConfigLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(Act1ResourceName)
            ?? throw new MapGenerationConfigException(
                $"Embedded resource not found: {Act1ResourceName}");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static MapGenerationConfig Parse(string json)
    {
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new MapGenerationConfigException("map-config JSON のパースに失敗しました。", ex);
        }
        if (dto is null) throw new MapGenerationConfigException("map-config JSON が null でした。");

        try
        {
            return dto.ToConfig();
        }
        catch (Exception ex) when (ex is not MapGenerationConfigException)
        {
            throw new MapGenerationConfigException("map-config の値変換に失敗しました。", ex);
        }
    }

    // JSON 受け入れ用の DTO。public record だが外部公開は Loader 経由のみの想定。
    private sealed record Dto(
        int RowCount,
        int ColumnCount,
        int RowNodeCountMin,
        int RowNodeCountMax,
        EdgeDto EdgeWeights,
        TileDistDto TileDistribution,
        FixedRowDto[] FixedRows,
        ExclusionDto[] RowKindExclusions,
        PathDto PathConstraints,
        int MaxRegenerationAttempts)
    {
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
            MaxRegenerationAttempts);
    }

    private sealed record EdgeDto(double Weight1, double Weight2, double Weight3);
    private sealed record TileDistDto(
        System.Collections.Generic.Dictionary<TileKind, double> BaseWeights,
        System.Collections.Generic.Dictionary<TileKind, int> MinPerMap,
        System.Collections.Generic.Dictionary<TileKind, int> MaxPerMap);
    private sealed record FixedRowDto(int Row, TileKind Kind);
    private sealed record ExclusionDto(int Row, TileKind ExcludedKind);
    private sealed record PathDto(
        System.Collections.Generic.Dictionary<TileKind, RangeDto> PerPathCount,
        int MinEliteRow,
        ConsecutiveDto[] ForbiddenConsecutive);
    private sealed record RangeDto(int Min, int Max);
    private sealed record ConsecutiveDto(TileKind First, TileKind Second);
}
```

注：`using System.Linq;` を追加する必要がある。上のコードでは省略したが、実装時に先頭 using に含める。

- [ ] **Step 6: テスト成功確認**

Run: `dotnet test --filter FullyQualifiedName~MapGenerationConfigLoaderTests`
Expected: すべて PASS

- [ ] **Step 7: コミット**

```bash
git add src/Core/Map/Config/map-act1.json src/Core/Map/MapGenerationConfigLoader.cs src/Core/Core.csproj tests/Core.Tests/Map/MapGenerationConfigLoaderTests.cs
git commit -m "feat(core): add MapGenerationConfigLoader and embedded map-act1.json"
```

---

## Task 11: Server 側 DI 登録

**Files:**
- Modify: `src/Server/Program.cs`
- Test: `tests/Server.Tests/MapGeneratorDiTests.cs`（存在しなければ新規作成）

- [ ] **Step 1: 現状の Program.cs を確認**

Read: `src/Server/Program.cs`

`IDungeonMapGenerator` と `MapGenerationConfig` の DI 登録場所を決める（既存の `AddSingleton` 群と同じブロック）。

- [ ] **Step 2: Server.Tests が存在するか確認**

Glob: `tests/Server.Tests/*.csproj`

存在しない場合はこのタスクの Step 3 で DI 登録の smoke テストは `dotnet build` + 手動確認に留め、統合テスト追加はこの計画書の範囲外とする。

- [ ] **Step 3: DI 登録テストを書く（Server.Tests が存在する場合のみ）**

`tests/Server.Tests/MapGeneratorDiTests.cs`:
```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Map;
using Xunit;

namespace RoguelikeCardGame.Server.Tests;

public class MapGeneratorDiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public MapGeneratorDiTests(WebApplicationFactory<Program> factory) { _factory = factory; }

    [Fact]
    public void IDungeonMapGenerator_Resolves()
    {
        using var scope = _factory.Services.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IDungeonMapGenerator>();
        Assert.NotNull(gen);
    }

    [Fact]
    public void MapGenerationConfig_Resolves()
    {
        using var scope = _factory.Services.CreateScope();
        var cfg = scope.ServiceProvider.GetRequiredService<MapGenerationConfig>();
        Assert.Equal(15, cfg.RowCount);
    }
}
```

- [ ] **Step 4: テスト失敗確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~MapGeneratorDiTests`
Expected: `InvalidOperationException` などで失敗（サービスが未登録）

（Server.Tests が存在しない場合は本 Step をスキップし、Step 5 で `dotnet build` のみ）

- [ ] **Step 5: `Program.cs` に DI 登録を追加**

`src/Server/Program.cs` の既存の `AddSingleton` 群の近くに以下を追加：
```csharp
builder.Services.AddSingleton<RoguelikeCardGame.Core.Map.MapGenerationConfig>(_ =>
    RoguelikeCardGame.Core.Map.MapGenerationConfigLoader.LoadAct1());
builder.Services.AddSingleton<
    RoguelikeCardGame.Core.Map.IDungeonMapGenerator,
    RoguelikeCardGame.Core.Map.DungeonMapGenerator>();
```

（using を整えて短く書いても OK。既存スタイルに合わせる）

- [ ] **Step 6: ビルドとテスト**

Run: `dotnet build` → ビルド成功
Run: `dotnet test`（Server.Tests がある場合）→ すべて PASS

- [ ] **Step 7: コミット**

```bash
git add src/Server/Program.cs
# Server.Tests を追加した場合は以下も
# git add tests/Server.Tests/MapGeneratorDiTests.cs
git commit -m "feat(server): register IDungeonMapGenerator and MapGenerationConfig in DI"
```

---

## 完了判定

全タスク完了後、以下を確認：

- [ ] `dotnet build` がエラー・警告なしで通る。
- [ ] `dotnet test` で `Core.Tests` の全テストが PASS（新規テストを含めて）。
- [ ] `MapGenerationConfigLoader.LoadAct1()` が既定値の `MapGenerationConfig` を返し、`DungeonMapGenerator.Generate` が正常にマップを返す。
- [ ] Phase 3 完了タグ `phase3-complete` を master へのマージ後に打つ（タグ付けは別ブランチ or 別 PR で）。

---

## 備考

- Task 6 の `Generate` は「無限 while リトライ」を暫定採用し、Task 8 で `MaxRegenerationAttempts` による打ち切りと失敗理由付き例外に昇格する。段階的に正しくしていく方式。
- Task 7 の暫定 MinPerMap チェックも Task 8 で `ValidateDistribution` に吸収される。
- `TileDistribution.BaseWeights` にない Kind（Treasure, Boss, Start）は候補に含まれないため、割当フェーズで勝手に Treasure になることはない（Row 9 の FixedRow 経由のみ）。
- VR 移植時は `IRng` / `IDungeonMapGenerator` / `MapGenerationConfigLoader` は VR 側で使わない。VR 版は事前生成済みの `DungeonMap` を JSON で読み込む運用。
