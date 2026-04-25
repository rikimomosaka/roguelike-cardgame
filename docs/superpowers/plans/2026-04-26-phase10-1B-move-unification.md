# Phase 10.1.B — MoveDefinition 統一 + CombatActorDefinition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 旧 `MoveDefinition`（数値フィールド散在型）を破棄し、Phase 10.1.A 統一済みの `CardEffect` を再利用する `Effects: List<CardEffect>` 形式に統一。`CombatActorDefinition` 抽象基底を新設し `EnemyDefinition` / `UnitDefinition` を派生として整理。敵 JSON 34 ファイルを全書き換え。Phase 5 placeholder バトルが従来通り動作することで完了判定。

**Architecture:** 「rewrite-in-place + 段階的修復」方針（Phase 10.1.A 踏襲）。新型を新 namespace（`RoguelikeCardGame.Core.Battle.Definitions`）に建てつつ旧 `Core.Enemy` の中身を `git mv` で移動・書換する。Tasks 2〜10 で旧型が消滅し依存先がコンパイル不可になるが、Tasks 11〜13 で production / test / JSON を一括修正して緑に戻す。Tasks 14〜16 で grep 検証・親 spec 補記・最終確認＋タグ。

**Tech Stack:** C# .NET 10 / xUnit / `System.Text.Json`

**完了判定:**
- `dotnet build` 警告 0、エラー 0
- `dotnet test` 全テスト緑（Phase 10.1.B 開始前 + 本 plan 追加分）
- 旧 `MoveDefinition` のフィールド名（`DamageMin` / `DamageMax` / `Hits` / `BlockMin` / `BlockMax` / `Buff` / `AmountMin` / `AmountMax`）が production / tests に対する grep で 0 件
- `HpMin` / `HpMax` が production / tests に対する grep で 0 件（spec 文書を除く）
- 敵 JSON 34 ファイルが新形式（旧形式の `damageMin` / `damageMax` / `hits` / `blockMin` / `blockMax` / `buff` / `amountMin` / `amountMax` / `hpMin` / `hpMax` を grep で `src/Core/Data/Enemies/*.json` 範囲内では 0 件）
- 未対応 buff 名（`ritual` / `enrage` / `curl_up` / `activate` / `split`）が `src/Core/Data/Enemies/*.json` 範囲内で grep 0 件
- `src/Core/Enemy/` ディレクトリが消滅
- Phase 5 placeholder バトルが手動で動作確認済み（敵マス → 即勝利 → 報酬画面）
- 親 spec（Phase 10）の該当章が新方針に合わせて修正済み
- ブランチに `phase10-1B-complete` タグを切り、origin に push

---

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `src/Core/Battle/Definitions/MoveKind.cs` | enum 7 値（Attack/Defend/Buff/Debuff/Heal/Multi/Unknown） | **新規** |
| `src/Core/Battle/Definitions/MoveDefinition.cs` | 旧 `src/Core/Enemy/MoveDefinition.cs` を `git mv` + 全面書換 | **移動+書換** |
| `src/Core/Battle/Definitions/CombatActorDefinition.cs` | abstract record 共通基底 | **新規** |
| `src/Core/Battle/Definitions/EnemyTier.cs` | 旧 `EnemyPool.cs` 内の enum を新ファイルへ分離 | **新規（分離）** |
| `src/Core/Battle/Definitions/EnemyPool.cs` | 旧 `src/Core/Enemy/EnemyPool.cs` を `git mv` + namespace 修正（enum は除外済） | **移動+書換** |
| `src/Core/Battle/Definitions/EnemyDefinition.cs` | 旧 `src/Core/Enemy/EnemyDefinition.cs` を `git mv` + 全面書換（CombatActor 継承、単一 Hp） | **移動+書換** |
| `src/Core/Battle/Definitions/UnitDefinition.cs` | 召喚キャラ定義 | **新規** |
| `src/Core/Battle/Definitions/Loaders/MoveJsonLoader.cs` | 1 個分の move JSON → MoveDefinition 共通 helper | **新規** |
| `src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs` | 旧 `src/Core/Enemy/EnemyJsonLoader.cs` を `git mv` + 全面書換 | **移動+書換** |
| `src/Core/Battle/Definitions/Loaders/UnitJsonLoader.cs` | unit JSON → UnitDefinition | **新規** |
| `src/Core/Data/Enemies/*.json` (34 ファイル) | 全件新形式に書換 | **一括書換** |
| `src/Core/Data/Units/.gitkeep` | 空フォルダ保持 | **新規** |
| `src/Core/Battle/BattlePlaceholder.cs` | HpMin/Max ロール → 単一 Hp 参照に修正 + namespace 更新 | 修正 |
| 各種 production ファイル | `using RoguelikeCardGame.Core.Enemy;` → `using RoguelikeCardGame.Core.Battle.Definitions;` | **using 更新** |
| `tests/Core.Tests/Battle/Definitions/*.cs` | 新型のテスト群を新規追加 | **新規** |
| `tests/Core.Tests/Battle/Definitions/Loaders/*.cs` | 新ローダーのテスト + 旧 EnemyJsonLoaderTests を移動 | **新規＋移動** |
| `tests/Core.Tests/Enemy/*` | 移動 or 削除 | **移動/削除** |
| `tests/Core.Tests/Battle/BattlePlaceholderTests.cs` | `Assert.InRange` を `Assert.Equal` へ + namespace | 修正 |
| `tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs` | 新形式・空 Units 対応・migration completeness grep | 修正 |
| `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` | 6 箇所補記 | 修正 |

`src/Core/Enemy/` ディレクトリは Task 11 以降で空になり、削除される。

---

## Task 1: MoveKind enum を追加

**Files:**
- Create: `src/Core/Battle/Definitions/MoveKind.cs`
- Test: `tests/Core.Tests/Battle/Definitions/MoveKindTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/MoveKindTests.cs` を新規作成:

```csharp
using RoguelikeCardGame.Core.Battle.Definitions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class MoveKindTests
{
    [Fact]
    public void Attack_value_is_zero() => Assert.Equal(0, (int)MoveKind.Attack);

    [Fact]
    public void Defend_value_is_one() => Assert.Equal(1, (int)MoveKind.Defend);

    [Fact]
    public void Buff_value_is_two() => Assert.Equal(2, (int)MoveKind.Buff);

    [Fact]
    public void Debuff_value_is_three() => Assert.Equal(3, (int)MoveKind.Debuff);

    [Fact]
    public void Heal_value_is_four() => Assert.Equal(4, (int)MoveKind.Heal);

    [Fact]
    public void Multi_value_is_five() => Assert.Equal(5, (int)MoveKind.Multi);

    [Fact]
    public void Unknown_value_is_six() => Assert.Equal(6, (int)MoveKind.Unknown);
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~MoveKindTests"`
Expected: コンパイルエラー「型または名前空間 'MoveKind' が見つかりません」

- [ ] **Step 3: 最小実装**

`src/Core/Battle/Definitions/MoveKind.cs`:
```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 敵 / 召喚キャラの move を intent UI 上どのカテゴリで表示するかの分類。
/// 値は battle-v10.html の .is-{attack|defend|buff|debuff|heal|unknown} CSS クラスへ対応。
/// </summary>
public enum MoveKind
{
    Attack  = 0,
    Defend  = 1,
    Buff    = 2,
    Debuff  = 3,
    Heal    = 4,
    Multi   = 5,
    Unknown = 6,
}
```

- [ ] **Step 4: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~MoveKindTests"`
Expected: 7 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/MoveKind.cs tests/Core.Tests/Battle/Definitions/MoveKindTests.cs
git commit -m "feat(battle): add MoveKind enum (Attack/Defend/Buff/Debuff/Heal/Multi/Unknown)"
```

---

## Task 2: 新 MoveDefinition record（旧を `git mv` + 全面書換）

**Files:**
- Move + rewrite: `src/Core/Enemy/MoveDefinition.cs` → `src/Core/Battle/Definitions/MoveDefinition.cs`
- Test: `tests/Core.Tests/Battle/Definitions/MoveDefinitionTests.cs`

> **重要**: このタスク完了後、`EnemyDefinition` / `EnemyJsonLoader` および 11 ファイル前後の依存箇所がコンパイル不可になる。Tasks 3〜13 で順次修正する。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/MoveDefinitionTests.cs` を新規作成:

```csharp
using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class MoveDefinitionTests
{
    [Fact]
    public void Records_with_same_field_values_are_equal()
    {
        var effects = new List<CardEffect>
        {
            new("attack", EffectScope.All, EffectSide.Enemy, 5),
        };
        var a = new MoveDefinition("m1", MoveKind.Attack, effects, "m2");
        var b = new MoveDefinition("m1", MoveKind.Attack, effects, "m2");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Empty_effects_array_is_allowed()
    {
        var def = new MoveDefinition("idle", MoveKind.Unknown, Array.Empty<CardEffect>(), "idle");
        Assert.Empty(def.Effects);
    }

    [Fact]
    public void Multiple_effects_preserve_order()
    {
        var effects = new List<CardEffect>
        {
            new("attack", EffectScope.All, EffectSide.Enemy, 7),
            new("block",  EffectScope.Self, null, 5),
        };
        var def = new MoveDefinition("thrash", MoveKind.Multi, effects, "bellow");
        Assert.Equal(2, def.Effects.Count);
        Assert.Equal("attack", def.Effects[0].Action);
        Assert.Equal("block",  def.Effects[1].Action);
    }
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet build src/Core`
Expected: コンパイルエラー（新 `MoveDefinition` が `Core.Battle.Definitions` namespace に未定義）

- [ ] **Step 3: ファイル移動 + 全面書換**

```bash
git mv src/Core/Enemy/MoveDefinition.cs src/Core/Battle/Definitions/MoveDefinition.cs
```

`src/Core/Battle/Definitions/MoveDefinition.cs` を以下に全面置換:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 敵 / 召喚キャラの行動 1 ステップ。state-machine 形式の遷移を持つ。
/// Phase 10 設計書（10.1.B）第 3-2 章参照。
/// </summary>
public sealed record MoveDefinition(
    string Id,
    MoveKind Kind,
    IReadOnlyList<CardEffect> Effects,
    string NextMoveId);
```

- [ ] **Step 4: 新型のテストだけ走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~MoveDefinitionTests"`
Expected: 3 件 PASS

> リポジトリ全体のビルドはまだエラー（旧 `MoveDefinition` を参照していた `EnemyDefinition` / `EnemyJsonLoader` ほかが破綻中）。これは正常で Tasks 3〜13 で修正する。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/MoveDefinition.cs tests/Core.Tests/Battle/Definitions/MoveDefinitionTests.cs
git commit -m "refactor(battle): move MoveDefinition to Battle.Definitions with new effect-based shape"
```

> ⚠ このコミットの時点でリポジトリは**ビルド不可**状態。Tasks 3〜13 で順次修正する。

---

## Task 3: CombatActorDefinition 抽象基底

**Files:**
- Create: `src/Core/Battle/Definitions/CombatActorDefinition.cs`
- Test: `tests/Core.Tests/Battle/Definitions/CombatActorDefinitionTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/CombatActorDefinitionTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class CombatActorDefinitionTests
{
    /// <summary>テスト用最小の派生 record。</summary>
    private sealed record TestActor(
        string Id, string Name, string ImageId, int Hp,
        string InitialMoveId, IReadOnlyList<MoveDefinition> Moves)
        : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);

    [Fact]
    public void Derived_record_inherits_fields()
    {
        var moves = new List<MoveDefinition>
        {
            new("a", MoveKind.Attack, Array.Empty<CardEffect>(), "a"),
        };
        var actor = new TestActor("x", "X", "img", 30, "a", moves);
        Assert.Equal("x", actor.Id);
        Assert.Equal(30, actor.Hp);
        Assert.Equal("a", actor.InitialMoveId);
        Assert.Single(actor.Moves);
    }

    [Fact]
    public void Two_derived_records_with_same_values_are_equal()
    {
        var moves = new List<MoveDefinition>();
        var a = new TestActor("x", "X", "img", 30, "a", moves);
        var b = new TestActor("x", "X", "img", 30, "a", moves);
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CombatActorDefinitionTests"`
Expected: コンパイルエラー（`CombatActorDefinition` 未定義）

- [ ] **Step 3: 最小実装**

`src/Core/Battle/Definitions/CombatActorDefinition.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 戦闘に参加するキャラクターの静的定義（敵・召喚キャラの共通基底）。
/// HP は単一値。乱数化は将来拡張ポイント。
/// Phase 10 設計書（10.1.B）第 3-3 章参照。
/// </summary>
public abstract record CombatActorDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CombatActorDefinitionTests"`
Expected: 2 件 PASS（ただしリポジトリ全体ビルドはまだ赤）

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/CombatActorDefinition.cs tests/Core.Tests/Battle/Definitions/CombatActorDefinitionTests.cs
git commit -m "feat(battle): add CombatActorDefinition abstract base record"
```

---

## Task 4: EnemyTier enum 分離

**Files:**
- Create: `src/Core/Battle/Definitions/EnemyTier.cs`
- Test: `tests/Core.Tests/Battle/Definitions/EnemyTierTests.cs`

旧 `src/Core/Enemy/EnemyPool.cs` には enum + record が同居している。Phase 10.1.B では「1 ファイル 1 型」を徹底するため、enum 部分を新ファイルに分離する。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/EnemyTierTests.cs`:

```csharp
using RoguelikeCardGame.Core.Battle.Definitions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class EnemyTierTests
{
    [Fact]
    public void Weak_value_is_zero() => Assert.Equal(0, (int)EnemyTier.Weak);

    [Fact]
    public void Strong_value_is_one() => Assert.Equal(1, (int)EnemyTier.Strong);

    [Fact]
    public void Elite_value_is_two() => Assert.Equal(2, (int)EnemyTier.Elite);

    [Fact]
    public void Boss_value_is_three() => Assert.Equal(3, (int)EnemyTier.Boss);
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyTierTests"`
Expected: コンパイルエラー（`Battle.Definitions.EnemyTier` 未定義。なお `Core.Enemy.EnemyTier` はまだ存在しているがテストの `using` 先が違う）

- [ ] **Step 3: 最小実装**

`src/Core/Battle/Definitions/EnemyTier.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>敵の強さ区分。</summary>
public enum EnemyTier
{
    Weak   = 0,
    Strong = 1,
    Elite  = 2,
    Boss   = 3,
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyTierTests"`
Expected: 4 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/EnemyTier.cs tests/Core.Tests/Battle/Definitions/EnemyTierTests.cs
git commit -m "feat(battle): split EnemyTier enum into its own file"
```

---

## Task 5: EnemyPool record（旧から `git mv` + namespace 更新 + enum 削除）

**Files:**
- Move + edit: `src/Core/Enemy/EnemyPool.cs` → `src/Core/Battle/Definitions/EnemyPool.cs`
- Test: `tests/Core.Tests/Battle/Definitions/EnemyPoolTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/EnemyPoolTests.cs`:

```csharp
using RoguelikeCardGame.Core.Battle.Definitions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class EnemyPoolTests
{
    [Fact]
    public void Pool_holds_act_and_tier()
    {
        var p = new EnemyPool(2, EnemyTier.Elite);
        Assert.Equal(2, p.Act);
        Assert.Equal(EnemyTier.Elite, p.Tier);
    }

    [Fact]
    public void Two_pools_with_same_values_are_equal()
    {
        Assert.Equal(new EnemyPool(1, EnemyTier.Weak), new EnemyPool(1, EnemyTier.Weak));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyPoolTests"`
Expected: コンパイルエラー（新 `Battle.Definitions.EnemyPool` 未定義）

- [ ] **Step 3: ファイル移動 + 中身書換**

```bash
git mv src/Core/Enemy/EnemyPool.cs src/Core/Battle/Definitions/EnemyPool.cs
```

`src/Core/Battle/Definitions/EnemyPool.cs` を以下に全面置換（enum は Task 4 で別ファイルに分離済なのでここでは削除）:

```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>敵が出現するアクトと強さ区分の組み合わせ。</summary>
public sealed record EnemyPool(int Act, EnemyTier Tier);
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyPoolTests"`
Expected: 2 件 PASS（リポジトリ全体ビルドはまだ赤）

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/EnemyPool.cs tests/Core.Tests/Battle/Definitions/EnemyPoolTests.cs
git commit -m "refactor(battle): move EnemyPool to Battle.Definitions and drop embedded enum"
```

---

## Task 6: EnemyDefinition record（旧から `git mv` + 全面書換）

**Files:**
- Move + rewrite: `src/Core/Enemy/EnemyDefinition.cs` → `src/Core/Battle/Definitions/EnemyDefinition.cs`
- Test: `tests/Core.Tests/Battle/Definitions/EnemyDefinitionTests.cs`（新規作成、旧 `tests/Core.Tests/Enemy/EnemyDefinitionTests.cs` は Task 13 で削除）

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/EnemyDefinitionTests.cs` を新規作成:

```csharp
using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class EnemyDefinitionTests
{
    private static EnemyDefinition Sample(int hp = 30) => new(
        Id: "test_enemy",
        Name: "テスト敵",
        ImageId: "test",
        Hp: hp,
        Pool: new EnemyPool(1, EnemyTier.Weak),
        InitialMoveId: "m1",
        Moves: new List<MoveDefinition>
        {
            new("m1", MoveKind.Attack,
                new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 5) },
                "m1"),
        });

    [Fact]
    public void Inherits_CombatActorDefinition()
    {
        var def = Sample();
        Assert.IsAssignableFrom<CombatActorDefinition>(def);
    }

    [Fact]
    public void Hp_is_single_value()
    {
        var def = Sample(hp: 42);
        Assert.Equal(42, def.Hp);
    }

    [Fact]
    public void Pool_holds_act_and_tier()
    {
        var def = Sample();
        Assert.Equal(1, def.Pool.Act);
        Assert.Equal(EnemyTier.Weak, def.Pool.Tier);
    }

    [Fact]
    public void Records_with_same_values_are_equal()
    {
        Assert.Equal(Sample(), Sample());
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyDefinitionTests"`
Expected: コンパイルエラー（新 namespace の `EnemyDefinition` 未定義 or 旧シグネチャと混在）

- [ ] **Step 3: ファイル移動 + 全面書換**

```bash
git mv src/Core/Enemy/EnemyDefinition.cs src/Core/Battle/Definitions/EnemyDefinition.cs
```

`src/Core/Battle/Definitions/EnemyDefinition.cs` を以下に全面置換:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 敵のマスター定義。state-machine 形式の行動セットを持つ。
/// Phase 10 設計書（10.1.B）第 3-4 章参照。
/// </summary>
public sealed record EnemyDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    EnemyPool Pool,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves)
    : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyDefinitionTests"`
Expected: 4 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/EnemyDefinition.cs tests/Core.Tests/Battle/Definitions/EnemyDefinitionTests.cs
git commit -m "refactor(battle): rewrite EnemyDefinition to inherit CombatActorDefinition with single Hp"
```

---

## Task 7: UnitDefinition record（新規）

**Files:**
- Create: `src/Core/Battle/Definitions/UnitDefinition.cs`
- Test: `tests/Core.Tests/Battle/Definitions/UnitDefinitionTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/UnitDefinitionTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class UnitDefinitionTests
{
    private static UnitDefinition Sample(int? lifetime = null) => new(
        Id: "wolf_summon",
        Name: "召喚狼",
        ImageId: "wolf",
        Hp: 12,
        InitialMoveId: "bite",
        Moves: new List<MoveDefinition>
        {
            new("bite", MoveKind.Attack,
                new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4) },
                "bite"),
        },
        LifetimeTurns: lifetime);

    [Fact]
    public void Inherits_CombatActorDefinition()
    {
        Assert.IsAssignableFrom<CombatActorDefinition>(Sample());
    }

    [Fact]
    public void LifetimeTurns_defaults_to_null()
    {
        Assert.Null(Sample().LifetimeTurns);
    }

    [Fact]
    public void LifetimeTurns_accepts_positive_value()
    {
        Assert.Equal(3, Sample(lifetime: 3).LifetimeTurns);
    }

    [Fact]
    public void Hp_is_single_value()
    {
        Assert.Equal(12, Sample().Hp);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~UnitDefinitionTests"`
Expected: コンパイルエラー

- [ ] **Step 3: 最小実装**

`src/Core/Battle/Definitions/UnitDefinition.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 召喚キャラのマスター定義。
/// LifetimeTurns: null = 永続、N = N ターン経過で自動消滅。
/// Phase 10 設計書（10.1.B）第 3-5 章参照。
/// </summary>
public sealed record UnitDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves,
    int? LifetimeTurns = null)
    : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~UnitDefinitionTests"`
Expected: 4 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/UnitDefinition.cs tests/Core.Tests/Battle/Definitions/UnitDefinitionTests.cs
git commit -m "feat(battle): add UnitDefinition record (CombatActor + LifetimeTurns)"
```

---

## Task 8: MoveJsonLoader（共通 helper）

**Files:**
- Create: `src/Core/Battle/Definitions/Loaders/MoveJsonLoader.cs`
- Test: `tests/Core.Tests/Battle/Definitions/Loaders/MoveJsonLoaderTests.cs`

`MoveDefinition` 1 個分を JSON から読む共通 helper。`EnemyJsonLoader` と `UnitJsonLoader` の両方から利用される。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/Loaders/MoveJsonLoaderTests.cs`:

```csharp
using System;
using System.Text.Json;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Definitions.Loaders;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions.Loaders;

public class MoveJsonLoaderTests
{
    private static MoveDefinition Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return MoveJsonLoader.ParseMove(doc.RootElement, msg => new Exception(msg));
    }

    [Fact]
    public void Parse_attack_move_with_one_effect()
    {
        var m = Parse("""
        {"id":"chomp","kind":"Attack","nextMoveId":"thrash",
         "effects":[{"action":"attack","scope":"all","side":"enemy","amount":11}]}
        """);
        Assert.Equal("chomp", m.Id);
        Assert.Equal(MoveKind.Attack, m.Kind);
        Assert.Equal("thrash", m.NextMoveId);
        Assert.Single(m.Effects);
        Assert.Equal("attack", m.Effects[0].Action);
        Assert.Equal(11, m.Effects[0].Amount);
    }

    [Fact]
    public void Parse_multi_move_with_multiple_effects()
    {
        var m = Parse("""
        {"id":"thrash","kind":"Multi","nextMoveId":"bellow",
         "effects":[
           {"action":"attack","scope":"all","side":"enemy","amount":7},
           {"action":"block","scope":"self","amount":5}
         ]}
        """);
        Assert.Equal(MoveKind.Multi, m.Kind);
        Assert.Equal(2, m.Effects.Count);
        Assert.Equal("attack", m.Effects[0].Action);
        Assert.Equal("block",  m.Effects[1].Action);
    }

    [Fact]
    public void Parse_defend_kind()
    {
        var m = Parse("""
        {"id":"d","kind":"Defend","nextMoveId":"d","effects":[]}
        """);
        Assert.Equal(MoveKind.Defend, m.Kind);
    }

    [Fact]
    public void Parse_buff_kind()
    {
        var m = Parse("""
        {"id":"b","kind":"Buff","nextMoveId":"b","effects":[]}
        """);
        Assert.Equal(MoveKind.Buff, m.Kind);
    }

    [Fact]
    public void Parse_debuff_kind()
    {
        var m = Parse("""
        {"id":"x","kind":"Debuff","nextMoveId":"x","effects":[]}
        """);
        Assert.Equal(MoveKind.Debuff, m.Kind);
    }

    [Fact]
    public void Parse_heal_kind()
    {
        var m = Parse("""
        {"id":"h","kind":"Heal","nextMoveId":"h","effects":[]}
        """);
        Assert.Equal(MoveKind.Heal, m.Kind);
    }

    [Fact]
    public void Parse_unknown_kind()
    {
        var m = Parse("""
        {"id":"u","kind":"Unknown","nextMoveId":"u","effects":[]}
        """);
        Assert.Equal(MoveKind.Unknown, m.Kind);
    }

    [Fact]
    public void Parse_empty_effects_array()
    {
        var m = Parse("""
        {"id":"idle","kind":"Unknown","nextMoveId":"idle","effects":[]}
        """);
        Assert.Empty(m.Effects);
    }

    [Fact]
    public void Parse_missing_effects_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"id":"x","kind":"Attack","nextMoveId":"x"}"""));
    }

    [Fact]
    public void Parse_unknown_kind_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"id":"x","kind":"Weird","nextMoveId":"x","effects":[]}"""));
    }

    [Fact]
    public void Parse_missing_id_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"kind":"Attack","nextMoveId":"x","effects":[]}"""));
    }

    [Fact]
    public void Parse_missing_nextMoveId_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"id":"x","kind":"Attack","effects":[]}"""));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~MoveJsonLoaderTests"`
Expected: コンパイルエラー（`MoveJsonLoader` 未定義）

- [ ] **Step 3: 最小実装**

`src/Core/Battle/Definitions/Loaders/MoveJsonLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Definitions.Loaders;

/// <summary>
/// 1 個分の Move JSON を MoveDefinition に変換する純粋関数群。
/// 敵 / 召喚キャラの両方の loader から利用される。
/// Phase 10 設計書（10.1.B）第 4-1 章参照。
/// </summary>
public static class MoveJsonLoader
{
    /// <summary>
    /// 単一 move オブジェクトを MoveDefinition に変換する。
    /// 必須フィールド (id / kind / nextMoveId / effects) が欠落していれば makeException 経由で送出。
    /// </summary>
    public static MoveDefinition ParseMove(JsonElement el, Func<string, Exception> makeException)
    {
        var id = GetRequiredString(el, "id", makeException);
        var kindStr = GetRequiredString(el, "kind", makeException);
        var nextMoveId = GetRequiredString(el, "nextMoveId", makeException);
        var kind = ParseKind(kindStr, makeException);

        if (!el.TryGetProperty("effects", out var effectsEl) || effectsEl.ValueKind != JsonValueKind.Array)
            throw makeException($"必須フィールド \"effects\" (array) がありません (move id={id})。");

        var effects = new List<CardEffect>();
        int idx = 0;
        foreach (var effEl in effectsEl.EnumerateArray())
        {
            effects.Add(CardEffectParser.ParseEffect(
                effEl,
                msg => makeException($"{msg} (move id={id}, effects[{idx}])")));
            idx++;
        }

        return new MoveDefinition(id, kind, effects, nextMoveId);
    }

    private static MoveKind ParseKind(string s, Func<string, Exception> makeException) => s switch
    {
        "Attack"  => MoveKind.Attack,
        "Defend"  => MoveKind.Defend,
        "Buff"    => MoveKind.Buff,
        "Debuff"  => MoveKind.Debuff,
        "Heal"    => MoveKind.Heal,
        "Multi"   => MoveKind.Multi,
        "Unknown" => MoveKind.Unknown,
        _ => throw makeException(
            $"未知の MoveKind 値: \"{s}\"。'Attack'/'Defend'/'Buff'/'Debuff'/'Heal'/'Multi'/'Unknown' のいずれか。"),
    };

    private static string GetRequiredString(JsonElement el, string key, Func<string, Exception> mk)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            throw mk($"必須フィールド \"{key}\" (string) がありません。");
        return v.GetString()!;
    }
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~MoveJsonLoaderTests"`
Expected: 12 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/Loaders/MoveJsonLoader.cs tests/Core.Tests/Battle/Definitions/Loaders/MoveJsonLoaderTests.cs
git commit -m "feat(battle): add MoveJsonLoader (shared move parser)"
```

---

## Task 9: 新 EnemyJsonLoader（旧から `git mv` + 全面書換）

**Files:**
- Move + rewrite: `src/Core/Enemy/EnemyJsonLoader.cs` → `src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs`
- Test: `tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs`（新規作成、旧テストは Task 13 で削除）

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs`:

```csharp
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Definitions.Loaders;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions.Loaders;

public class EnemyJsonLoaderTests
{
    [Fact]
    public void Parse_minimal_enemy()
    {
        var def = EnemyJsonLoader.Parse("""
        {
          "id":"e1","name":"敵 1","imageId":"e1",
          "hp":42,"act":1,"tier":"Weak",
          "initialMoveId":"a",
          "moves":[
            {"id":"a","kind":"Attack","nextMoveId":"a",
             "effects":[{"action":"attack","scope":"all","side":"enemy","amount":5}]}
          ]
        }""");
        Assert.Equal("e1", def.Id);
        Assert.Equal(42, def.Hp);
        Assert.Equal(1, def.Pool.Act);
        Assert.Equal(EnemyTier.Weak, def.Pool.Tier);
        Assert.Equal("a", def.InitialMoveId);
        Assert.Single(def.Moves);
    }

    [Fact]
    public void Parse_with_multiple_moves()
    {
        var def = EnemyJsonLoader.Parse("""
        {
          "id":"e2","name":"敵 2","imageId":"e2",
          "hp":80,"act":2,"tier":"Boss",
          "initialMoveId":"a",
          "moves":[
            {"id":"a","kind":"Attack","nextMoveId":"b",
             "effects":[{"action":"attack","scope":"all","side":"enemy","amount":10}]},
            {"id":"b","kind":"Defend","nextMoveId":"a",
             "effects":[{"action":"block","scope":"self","amount":8}]}
          ]
        }""");
        Assert.Equal(2, def.Moves.Count);
        Assert.Equal(EnemyTier.Boss, def.Pool.Tier);
    }

    [Fact]
    public void Parse_throws_when_initialMoveId_not_in_moves()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"act":1,"tier":"Weak",
          "initialMoveId":"missing",
          "moves":[
            {"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}
          ]
        }"""));
    }

    [Fact]
    public void Parse_throws_when_moves_empty()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"act":1,"tier":"Weak",
          "initialMoveId":"a",
          "moves":[]
        }"""));
    }

    [Fact]
    public void Parse_throws_when_act_out_of_range()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"act":4,"tier":"Weak",
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }"""));
    }

    [Fact]
    public void Parse_throws_when_unknown_tier()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"act":1,"tier":"Mythic",
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }"""));
    }

    [Fact]
    public void Parse_throws_on_missing_hp()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "act":1,"tier":"Weak",
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }"""));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyJsonLoaderTests"`
Expected: コンパイルエラー（旧 EnemyJsonLoader が `Core.Enemy` namespace に存在し、テストの新 namespace 期待と齟齬）

- [ ] **Step 3: ファイル移動 + 全面書換**

```bash
git mv src/Core/Enemy/EnemyJsonLoader.cs src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs
```

`src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs` を以下に全面置換:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Battle.Definitions.Loaders;

public sealed class EnemyJsonException : Exception
{
    public EnemyJsonException(string message) : base(message) { }
    public EnemyJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>敵 JSON 文字列を EnemyDefinition に変換する純粋関数群。</summary>
public static class EnemyJsonLoader
{
    public static EnemyDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EnemyJsonException("敵 JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);
                var imageId = GetRequiredString(root, "imageId", id);
                var hp = GetRequiredInt(root, "hp", id);

                var act = GetRequiredInt(root, "act", id);
                if (act < 1 || act > 3)
                    throw new EnemyJsonException($"act の値 {act} は 1〜3 の範囲外です (enemy id={id})。");

                var tier = ParseTier(GetRequiredString(root, "tier", id), id);

                var initialMoveId = GetRequiredString(root, "initialMoveId", id);
                var moves = ParseMoves(root, "moves", id);
                if (moves.Count == 0)
                    throw new EnemyJsonException($"moves が空です (enemy id={id})。");

                bool found = false;
                foreach (var m in moves) if (m.Id == initialMoveId) { found = true; break; }
                if (!found)
                    throw new EnemyJsonException(
                        $"initialMoveId \"{initialMoveId}\" が moves に存在しません (enemy id={id})。");

                return new EnemyDefinition(id, name, imageId, hp,
                    new EnemyPool(act, tier), initialMoveId, moves);
            }
            catch (EnemyJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(enemy id unknown)" : $"(enemy id={id})";
                throw new EnemyJsonException($"敵 JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static EnemyTier ParseTier(string s, string? id) => s switch
    {
        "Weak"   => EnemyTier.Weak,
        "Strong" => EnemyTier.Strong,
        "Elite"  => EnemyTier.Elite,
        "Boss"   => EnemyTier.Boss,
        _ => throw new EnemyJsonException($"tier の値 \"{s}\" は無効です (enemy id={id})。"),
    };

    private static IReadOnlyList<MoveDefinition> ParseMoves(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new EnemyJsonException($"moves は配列である必要があります (enemy id={id})。");

        var list = new List<MoveDefinition>();
        int index = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new EnemyJsonException(
                    $"moves[{index}] はオブジェクトである必要があります (enemy id={id})。");

            var ctx = $" (enemy id={id}, moves[{index}])";
            list.Add(MoveJsonLoader.ParseMove(el, msg => new EnemyJsonException($"{msg}{ctx}")));
            index++;
        }
        return list;
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyJsonLoaderTests"`
Expected: 7 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs
git commit -m "refactor(battle): rewrite EnemyJsonLoader for new effect-based format"
```

---

## Task 10: UnitJsonLoader（新規）

**Files:**
- Create: `src/Core/Battle/Definitions/Loaders/UnitJsonLoader.cs`
- Test: `tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs`:

```csharp
using RoguelikeCardGame.Core.Battle.Definitions.Loaders;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions.Loaders;

public class UnitJsonLoaderTests
{
    [Fact]
    public void Parse_unit_without_lifetime()
    {
        var def = UnitJsonLoader.Parse("""
        {
          "id":"wolf","name":"狼","imageId":"wolf",
          "hp":12,
          "initialMoveId":"bite",
          "moves":[
            {"id":"bite","kind":"Attack","nextMoveId":"bite",
             "effects":[{"action":"attack","scope":"single","side":"enemy","amount":4}]}
          ]
        }""");
        Assert.Equal("wolf", def.Id);
        Assert.Equal(12, def.Hp);
        Assert.Null(def.LifetimeTurns);
    }

    [Fact]
    public void Parse_unit_with_lifetime()
    {
        var def = UnitJsonLoader.Parse("""
        {
          "id":"spirit","name":"精霊","imageId":"spirit",
          "hp":8,
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}],
          "lifetimeTurns":3
        }""");
        Assert.Equal(3, def.LifetimeTurns);
    }

    [Fact]
    public void Parse_throws_when_moves_empty()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"initialMoveId":"a","moves":[]
        }"""));
    }

    [Fact]
    public void Parse_throws_when_initialMoveId_not_in_moves()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"initialMoveId":"missing",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }"""));
    }

    [Fact]
    public void Parse_throws_on_missing_hp()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }"""));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~UnitJsonLoaderTests"`
Expected: コンパイルエラー

- [ ] **Step 3: 最小実装**

`src/Core/Battle/Definitions/Loaders/UnitJsonLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Battle.Definitions.Loaders;

public sealed class UnitJsonException : Exception
{
    public UnitJsonException(string message) : base(message) { }
    public UnitJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>召喚キャラ JSON 文字列を UnitDefinition に変換する純粋関数群。</summary>
public static class UnitJsonLoader
{
    public static UnitDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new UnitJsonException("召喚キャラ JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);
                var imageId = GetRequiredString(root, "imageId", id);
                var hp = GetRequiredInt(root, "hp", id);
                var initialMoveId = GetRequiredString(root, "initialMoveId", id);

                var moves = ParseMoves(root, "moves", id);
                if (moves.Count == 0)
                    throw new UnitJsonException($"moves が空です (unit id={id})。");

                bool found = false;
                foreach (var m in moves) if (m.Id == initialMoveId) { found = true; break; }
                if (!found)
                    throw new UnitJsonException(
                        $"initialMoveId \"{initialMoveId}\" が moves に存在しません (unit id={id})。");

                int? lifetime = null;
                if (root.TryGetProperty("lifetimeTurns", out var ltEl) && ltEl.ValueKind == JsonValueKind.Number)
                    lifetime = ltEl.GetInt32();

                return new UnitDefinition(id, name, imageId, hp, initialMoveId, moves, lifetime);
            }
            catch (UnitJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(unit id unknown)" : $"(unit id={id})";
                throw new UnitJsonException($"召喚キャラ JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static IReadOnlyList<MoveDefinition> ParseMoves(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new UnitJsonException($"moves は配列である必要があります (unit id={id})。");

        var list = new List<MoveDefinition>();
        int index = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new UnitJsonException(
                    $"moves[{index}] はオブジェクトである必要があります (unit id={id})。");

            var ctx = $" (unit id={id}, moves[{index}])";
            list.Add(MoveJsonLoader.ParseMove(el, msg => new UnitJsonException($"{msg}{ctx}")));
            index++;
        }
        return list;
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~UnitJsonLoaderTests"`
Expected: 5 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Battle/Definitions/Loaders/UnitJsonLoader.cs tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs
git commit -m "feat(battle): add UnitJsonLoader (with optional lifetimeTurns)"
```

---

## Task 11: 敵 JSON 34 ファイルを新形式に一括変換

**Files:**
- Modify: `src/Core/Data/Enemies/*.json` (34 ファイル)

旧形式から新形式への変換アルゴリズム:

1. **トップレベル**:
   - `hpMin` / `hpMax` を 1 つの `hp` に統合（同値ならその値、異なれば中央値で小数点以下切り上げ）
   - `act` / `tier` / `initialMoveId` / `name` / `imageId` / `id` はそのまま
2. **`moves` 配列内の各 move**:
   - `kind`: 旧文字列を新パスカルケースへ変換
     - `attack` → `Attack`
     - `block` → `Defend`
     - `buff` → `Buff`
     - `debuff` → `Debuff`
     - `multi` → `Multi`
   - `id` / `nextMoveId` はそのまま
   - 旧 `damageMin/Max` / `hits` / `blockMin/Max` / `buff` / `amountMin/Max` フィールドを `effects` 配列に集約

3. **effects 配列の作成ルール**:
   - **damageMin/Max がある場合（min==max 前提、異なれば中央値）**: `hits` 個の attack effect を生成
     ```json
     {"action":"attack","scope":"all","side":"enemy","amount":<damage>}
     ```
   - **blockMin/Max がある場合**: 1 個の block effect を追加
     ```json
     {"action":"block","scope":"self","amount":<block>}
     ```
   - **buff フィールドがある場合**:
     - `kind == "buff"` → action は `"buff"`、`scope` は `"self"`
     - `kind == "debuff"` → action は `"debuff"`、`scope` は `"all"`、`side` は `"enemy"`
     - 未対応 buff 名は下表で置換（buff name と amount のセット）
     ```json
     {"action":"buff|debuff","scope":"self|all","side":"enemy?","name":<status>,"amount":<amount>}
     ```

4. **未対応 buff 名置換表（合計 6 移行ポイント）**:

| 旧 buff (amount) | 出現する敵 / move | 新形式 |
|---|---|---|
| `ritual` (3) | dark_cultist / incantation | `{"action":"buff","scope":"self","name":"strength","amount":3}` |
| `enrage` (2) | hobgoblin / bellow | `{"action":"buff","scope":"self","name":"strength","amount":2}` |
| `enrage` (5) | slime_king / roar | `{"action":"buff","scope":"self","name":"strength","amount":5}` |
| `curl_up` (3) | louse_red / curl | `{"action":"block","scope":"self","amount":3}`（buff ではなく block effect 化） |
| `activate` (1) | six_ghost / activate | `{"action":"buff","scope":"self","name":"strength","amount":1}` |
| `split` (1) | slime_king / split | `{"action":"buff","scope":"self","name":"strength","amount":1}` |

5. **HP の collapse 規則（決定論的）**:

各ファイルで `hpMin` と `hpMax` を読み、以下の規則で単一 `hp` 値を決定:

```
hp = (hpMin == hpMax) ? hpMin : (hpMin + hpMax + 1) / 2     // 整数除算 = 切り上げ中央値
```

参考早見表（差分のあるケース、実装時に元 JSON を opening して計算ベース確認）:

| ファイル | 旧 hpMin/hpMax | 新 hp |
|---|---|---|
| `act2_brute.json` | 52 / 58 | 55 |
| `act2_elite.json` | 88 / 96 | 92 |
| `act2_grunt.json` | 28 / 32 | 30 |
| `act3_brute.json` | 78 / 84 | 81 |
| `act3_elite.json` | 130 / 140 | 135 |
| `act3_grunt.json` | 42 / 46 | 44 |
| `bandit.json` | 40 / 44 | 42 |
| `big_slime.json` | 44 / 48 | 46 |
| `blue_orc.json` | 36 / 40 | 38 |
| `cave_bat_a.json` | 10 / 14 | 12 |
| `cave_bat_b.json` | 12 / 16 | 14 |
| `dark_cultist.json` | 14 / 18 | 16 |
| `dire_wolf.json` | 38 / 42 | 40 |
| `goblin_a.json` | 30 / 34 | 32 |
| `goblin_b.json` | 32 / 36 | 34 |
| `goblin_c.json` | 34 / 38 | 36 |
| `hobgoblin.json` | 82 / 86 | 84 |
| `iron_golem_a.json` | 60 / 64 | 62 |
| `iron_golem_b.json` | 72 / 76 | 74 |
| `iron_golem_c.json` | 82 / 86 | 84 |
| `jaw_worm.json` | 40 / 44 | 42 |
| `louse_red.json` | 10 / 15 | 13 |
| `mushroom_a.json` | 30 / 34 | 32 |
| `mushroom_b.json` | 34 / 38 | 36 |
| `ogre.json` | 46 / 50 | 48 |
| その他（min==max のファイル） | — | その値そのまま |

> 早見表に列挙されていないファイル（`red_orc.json` / `six_ghost.json` / `sleeping_dragon.json` / `slime_acid_s.json` / `slime_king.json` / `slime_spike_s.json` / `act2_boss.json` / `act3_boss.json` / `guardian_golem.json` 等）は元 JSON を開いて上記式で計算する。同値の場合はその値、異なる場合のみ計算が必要。

6. **buff のレンジ（amountMin != amountMax）**: jaw_worm の bellow が `amountMin: 3, amountMax: 5` の唯一例 → 中央値 4 を採用。

7. **damageMin != damageMax のケース**: 一部 act2/act3 系（act2_brute: 14-16、act2_elite: 18-20、act2_grunt: 8-10、act3_brute: 20-22、act3_elite: 26-28、act3_grunt: 12-14、louse_red bite: 5-7）→ 中央値で固定。

- [ ] **Step 1: 1 ファイル試験変換（jaw_worm.json）**

`src/Core/Data/Enemies/jaw_worm.json` を以下に書換:

```json
{
  "id": "jaw_worm",
  "name": "ジョウ・ワーム",
  "imageId": "jaw_worm",
  "hp": 42,
  "act": 1,
  "tier": "Weak",
  "initialMoveId": "chomp",
  "moves": [
    { "id": "chomp",  "kind": "Attack", "nextMoveId": "thrash",
      "effects": [
        { "action": "attack", "scope": "all", "side": "enemy", "amount": 11 }
      ] },
    { "id": "thrash", "kind": "Multi",  "nextMoveId": "bellow",
      "effects": [
        { "action": "attack", "scope": "all", "side": "enemy", "amount": 7 },
        { "action": "block",  "scope": "self", "amount": 5 }
      ] },
    { "id": "bellow", "kind": "Buff",   "nextMoveId": "chomp",
      "effects": [
        { "action": "buff",  "scope": "self", "name": "strength", "amount": 4 },
        { "action": "block", "scope": "self", "amount": 6 }
      ] }
  ]
}
```

- [ ] **Step 2: 残り 33 ファイルを上記アルゴリズムで一括変換**

| ファイル | 変換上の注意 |
|---|---|
| `act2_boss.json` | volley の hits=3 → attack effect を 3 個 |
| `act2_brute.json` | smash damage 14-16 → 15 |
| `act2_elite.json` | heavy damage 18-20 → 19 |
| `act2_grunt.json` | slash damage 8-10 → 9 |
| `act3_boss.json` | storm の hits=4 → attack effect 4 個 |
| `act3_brute.json` | crush damage 20-22 → 21 |
| `act3_elite.json` | execute damage 26-28 → 27 |
| `act3_grunt.json` | pierce damage 12-14 → 13 |
| `bandit.json` | 全 move 単純変換 |
| `big_slime.json` | bounce は multi（attack+block）|
| `blue_orc.json` | war_cry は buff strength 1 |
| `cave_bat_a.json` | screech は **kind:Debuff**, 旧 `buff:weak` → `{"action":"debuff","scope":"all","side":"enemy","name":"weak","amount":1}` |
| `cave_bat_b.json` | 単純変換 |
| `dark_cultist.json` | incantation は **未対応 buff 置換**（ritual → strength 3）|
| `dire_wolf.json` | snarl は **kind:Debuff**, weak |
| `goblin_a.json` | taunt は **kind:Debuff**, weak 2 |
| `goblin_b.json` | 単純変換 |
| `goblin_c.json` | throw_knife の hits=2 → attack effect 2 個。rally は buff strength 1 |
| `guardian_golem.json` | twin_slam の hits=2 |
| `hobgoblin.json` | bellow は **未対応 buff 置換**（enrage → strength 2）|
| `iron_golem_a/b/c.json` | overheat は buff strength 2 |
| `louse_red.json` | bite damage 5-7 → 6。curl は **未対応 buff 置換**（curl_up → block 3）。**kind は `"Defend"` に変更**（effect が block-only なので意味的に Defend が正しい） |
| `mushroom_a.json` | spore_cloud は **kind:Debuff**, vulnerable 2 |
| `mushroom_b.json` | 単純変換 |
| `ogre.json` | stomp は multi。roar は buff strength 2 |
| `red_orc.json` | rage は buff strength 2 |
| `six_ghost.json` | activate は **未対応 buff 置換**（activate → strength 1）|
| `sleeping_dragon.json` | wake は buff strength 3 |
| `slime_acid_s.json` | lick は **kind:Debuff**, weak 1 |
| `slime_king.json` | roar は **未対応 buff 置換**（enrage → strength 5）。split は **未対応 buff 置換**（split → strength 1）|
| `slime_spike_s.json` | 単純変換（要確認） |

> `louse_red.json` の curl move（旧 `kind:"buff", buff:"curl_up"`）は curl_up を block に置換するため、kind も `"Defend"` に揃える。これは「effect 内容と kind の意味が一致するように」という整合性ルールの適用。他の未対応 buff（ritual / enrage / activate / split → strength buff）は kind が変わらない。

- [ ] **Step 3: 旧フィールド名 grep で 0 件確認**

Run:
```bash
grep -l '"hpMin"\|"hpMax"\|"damageMin"\|"damageMax"\|"hits"\|"blockMin"\|"blockMax"\|"buff"\|"amountMin"\|"amountMax"' src/Core/Data/Enemies/*.json
```
Expected: ヒットなし

- [ ] **Step 4: 未対応 buff 名 grep で 0 件確認**

Run:
```bash
grep -l '"ritual"\|"enrage"\|"curl_up"\|"activate"\|"split"' src/Core/Data/Enemies/*.json
```
Expected: ヒットなし

- [ ] **Step 5: コミット**

```bash
git add src/Core/Data/Enemies/
git commit -m "data(enemies): migrate 34 enemy JSONs to new MoveDefinition format"
```

> リポジトリ全体ビルドはまだエラー（Tasks 12〜13 で production / test 修復後に緑復帰）。

---

## Task 12: production コードの namespace 更新と BattlePlaceholder 修正

**Files:**
- Modify: `src/Core/Battle/BattlePlaceholder.cs`
- Modify: `src/Core/Battle/EncounterQueue.cs`
- Modify: `src/Core/Data/EncounterDefinition.cs`
- Modify: `src/Core/Data/EncounterJsonLoader.cs`
- Modify: `src/Core/Data/DataCatalog.cs`
- Modify: `src/Core/Data/RewardTable.cs`
- Modify: `src/Core/Data/RewardTableJsonLoader.cs`
- Modify: `src/Core/Run/ActTransition.cs`
- Modify: `src/Core/Run/NodeEffectResolver.cs`
- Modify: `src/Core/Run/BossRewardFlow.cs`
- Modify: `src/Core/Rewards/RewardState.cs`
- Modify: `src/Core/Rewards/RewardGenerator.cs`
- Modify: `src/Server/Controllers/RunsController.cs`
- Modify: `src/Server/Services/RunStartService.cs`

production 側はほぼ `using` 行の置換だけ。BattlePlaceholder のみ HP ロール処理の書換が必要。

- [ ] **Step 1: 各ファイルで `using RoguelikeCardGame.Core.Enemy;` を `using RoguelikeCardGame.Core.Battle.Definitions;` に置換**

各ファイルを開き、`using RoguelikeCardGame.Core.Enemy;` を `using RoguelikeCardGame.Core.Battle.Definitions;` に書き換える（一括置換）。複数 using がある場合は該当行のみ。

- [ ] **Step 2: BattlePlaceholder.cs の HP ロール処理を修正**

`src/Core/Battle/BattlePlaceholder.cs` の旧:
```csharp
int hp = def.HpMin + rng.NextInt(0, def.HpMax - def.HpMin + 1);
```
を以下に置換:
```csharp
int hp = def.Hp;
```

`rng` パラメータがこの行で唯一使われている場合、未使用警告に注意。他箇所でも使われていれば変更不要。

- [ ] **Step 3: ビルド確認（production のみ、tests はまだ赤）**

Run: `dotnet build src/Core src/Server`
Expected: warning 0 / error 0

> tests は Task 13 で修正するためビルドエラー残り。

- [ ] **Step 4: コミット**

```bash
git add src/Core src/Server
git commit -m "refactor(production): migrate Core.Enemy → Core.Battle.Definitions namespace"
```

---

## Task 13: tests 全件の namespace 更新 + テストファイル移動 + 旧テスト削除

**Files:**
- Move: `tests/Core.Tests/Enemy/EnemyDefinitionTests.cs` → 削除（Task 6 で新版作成済み）
- Move: `tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs` → 削除（Task 9 で新版作成済み）
- Modify: `tests/Core.Tests/Battle/BattlePlaceholderTests.cs`
- Modify: `tests/Core.Tests/Battle/BattlePlaceholderBestiaryTests.cs`
- Modify: `tests/Core.Tests/Battle/EncounterQueueTests.cs`
- Modify: `tests/Core.Tests/Cards/CardUpgradeTests.cs`
- Modify: `tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs`
- Modify: `tests/Core.Tests/Data/DataCatalogPhase5Tests.cs`
- Modify: `tests/Core.Tests/Data/ActEncountersTests.cs`
- Modify: `tests/Core.Tests/Rewards/RewardGeneratorTests.cs`
- Modify: `tests/Core.Tests/Run/ActTransitionTests.cs`
- Modify: `tests/Core.Tests/Run/NodeEffectResolverTests.cs`
- Modify: `tests/Server.Tests/Controllers/RewardProceedActTransitionTests.cs`
- Modify: `tests/Server.Tests/Controllers/BossWinFlowTests.cs`
- Delete: `tests/Core.Tests/Enemy/` ディレクトリ（旧 2 ファイル削除後）

- [ ] **Step 1: 各 test ファイルの namespace を更新**

`using RoguelikeCardGame.Core.Enemy;` を `using RoguelikeCardGame.Core.Battle.Definitions;` に書き換える（11 ファイル）。

- [ ] **Step 2: BattlePlaceholderTests.cs の InRange を Equal に修正**

`tests/Core.Tests/Battle/BattlePlaceholderTests.cs:30` 周辺の旧:
```csharp
Assert.InRange(e.CurrentHp, def.HpMin, def.HpMax);
```
を以下に置換:
```csharp
Assert.Equal(def.Hp, e.CurrentHp);
```

- [ ] **Step 3: テストデータ生成箇所の修正（HpMin/Max → Hp 単一）**

11 のテストファイル内で `new EnemyDefinition(...)` を直接生成している箇所を全件 grep し、新シグネチャ（`Hp:` 単一引数）に書き換え。

Run（探索）: `grep -rn "new EnemyDefinition" tests/`
各ヒットで:
```csharp
new EnemyDefinition(..., HpMin: 40, HpMax: 44, ...)   // 旧
↓
new EnemyDefinition(..., Hp: 42, ...)                 // 新（中央値）
```

または旧 MoveDefinition を直接 `new` していたら、新シグネチャ（`Kind` enum + `Effects` 配列）に書換。

- [ ] **Step 4: 旧テストファイル削除**

```bash
git rm tests/Core.Tests/Enemy/EnemyDefinitionTests.cs
git rm tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs
```

`tests/Core.Tests/Enemy/` が空になったらディレクトリも削除（git は空ディレクトリを追跡しないので追加操作不要、ただし作業ディレクトリで `rmdir tests/Core.Tests/Enemy 2>/dev/null` を実行）。

- [ ] **Step 5: 全テスト実行**

Run: `dotnet test`
Expected: 全テスト緑

> もし `EmbeddedDataLoaderTests` で「34 敵 JSON が新形式でロード成功する」テストが失敗するなら、Task 11 の JSON 移行に漏れがある。grep して旧フィールド残存を再確認。

- [ ] **Step 6: 旧 src/Core/Enemy/ ディレクトリを削除**

このタスクの時点で `src/Core/Enemy/` は Tasks 2/5/6/9 の `git mv` により全ファイル移動済。空ディレクトリの削除:

```bash
rmdir src/Core/Enemy 2>/dev/null || true
```

`git status` で確認、変更があれば commit に含める（実際には空ディレクトリなので git は感知しない）。

- [ ] **Step 7: コミット**

```bash
git add tests/ src/
git commit -m "refactor(tests): migrate tests to Battle.Definitions namespace and new EnemyDefinition shape"
```

---

## Task 14: Units 空フォルダの追加 + EmbeddedDataLoader 対応 + migration completeness テスト

**Files:**
- Create: `src/Core/Data/Units/.gitkeep`
- Modify: `src/Core/Data/EmbeddedDataLoader.cs`（必要なら Units フォルダの enumerate 対応）
- Modify: `tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs`

> Phase 10.1.B では Unit 実データは 0 件。`src/Core/Data/Units/` フォルダだけ作って Phase 10.2 に備える。`EmbeddedDataLoader` が Units フォルダを enumerate する機能まで先行追加するか、Phase 10.2 まで先送りするかは実装時判断。**Phase 10.1.B では先送り**（フォルダ作成のみ）とし、`EmbeddedDataLoader` の Units 対応は Phase 10.2 で `UnitDefinition` 実データ追加と一緒に。

- [ ] **Step 1: Units フォルダと .gitkeep を作成**

```bash
mkdir -p src/Core/Data/Units
touch src/Core/Data/Units/.gitkeep
```

- [ ] **Step 2: EmbeddedDataLoaderTests に新形式ロード検証を追加**

`tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs` の末尾（または適切な箇所）に以下のテストを追加:

```csharp
    [Fact]
    public void All_enemy_JSONs_load_with_new_format()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Equal(34, catalog.Enemies.Count);
        foreach (var (id, def) in catalog.Enemies)
        {
            Assert.NotNull(def);
            Assert.True(def.Hp > 0, $"Enemy {id} has non-positive Hp");
            Assert.NotEmpty(def.Moves);
        }
    }
```

> `EmbeddedDataLoader.LoadCatalog()` の API（`catalog.Enemies` 等）は既存実装に合わせて参照すること。違う名前なら adjustments する。
> Units フォルダ用の test は Phase 10.2（`UnitDefinition` 実データ追加と `EmbeddedDataLoader` の Units enumerate 対応）で書くのでここでは追加しない。

- [ ] **Step 3: 全テスト実行**

Run: `dotnet test`
Expected: 全テスト緑（追加 2 件 + 既存全件 PASS）

- [ ] **Step 4: コミット**

```bash
git add src/Core/Data/Units/.gitkeep tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs
git commit -m "data(units): add empty Units directory + migration completeness test"
```

---

## Task 15: migration completeness grep テスト

**Files:**
- Create: `tests/Core.Tests/Battle/Definitions/EnemyJsonMigrationTests.cs`

旧フィールド名・未対応 buff 名が embedded JSON に残っていないことを CI で保証する static text-search テスト。

- [ ] **Step 1: テストを書く**

`tests/Core.Tests/Battle/Definitions/EnemyJsonMigrationTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class EnemyJsonMigrationTests
{
    private static string EnemyDir =>
        Path.Combine(FindRepoRoot(), "src", "Core", "Data", "Enemies");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoguelikeCardGame.sln")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repo root not found");
        return dir.FullName;
    }

    public static IEnumerable<object[]> EnemyFiles()
        => Directory.EnumerateFiles(EnemyDir, "*.json").Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(EnemyFiles))]
    public void No_legacy_field_names(string path)
    {
        var content = File.ReadAllText(path);
        var legacy = new[] { "\"hpMin\"", "\"hpMax\"", "\"damageMin\"", "\"damageMax\"",
                             "\"hits\"", "\"blockMin\"", "\"blockMax\"", "\"buff\"",
                             "\"amountMin\"", "\"amountMax\"" };
        foreach (var key in legacy)
            Assert.False(content.Contains(key), $"{Path.GetFileName(path)} contains legacy key {key}");
    }

    [Theory]
    [MemberData(nameof(EnemyFiles))]
    public void No_unsupported_buff_names(string path)
    {
        var content = File.ReadAllText(path);
        var unsupported = new[] { "\"ritual\"", "\"enrage\"", "\"curl_up\"", "\"activate\"", "\"split\"" };
        foreach (var name in unsupported)
            Assert.False(content.Contains(name), $"{Path.GetFileName(path)} contains unsupported buff {name}");
    }
}
```

- [ ] **Step 2: 失敗を確認**

旧 JSON が残っていなければこのテストは緑になる想定。一応念押しで:
Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyJsonMigrationTests"`
Expected: 全件 PASS（34 ファイル × 2 テスト = 68 件）

> もし FAIL するファイルがあれば、Task 11 の JSON 移行漏れ。手動で該当ファイルを修正して再実行。

- [ ] **Step 3: コミット**

```bash
git add tests/Core.Tests/Battle/Definitions/EnemyJsonMigrationTests.cs
git commit -m "test(battle): add grep-based migration completeness tests for enemy JSON"
```

---

## Task 16: 親 Phase 10 spec の 6 箇所補記

**Files:**
- Modify: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`

設計書（10.1.B）第 7 章で列挙した 6 項目を反映:

- [ ] **Step 1: 第 2-4 章の `HpMin / HpMax` を単一 `Hp` に変更**

`docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` の第 2-4 章で:
```csharp
public abstract record CombatActorDefinition(
    string Id, string Name, string ImageId,
    int HpMin, int HpMax,                           // ← 旧
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
```
を以下に置換:
```csharp
public abstract record CombatActorDefinition(
    string Id, string Name, string ImageId,
    int Hp,                                          // 単一値（Phase 10.1.B 時点）
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
```

- [ ] **Step 2: 第 2-4 章の EnemyDefinition の重複 `Act` 削除**

`EnemyDefinition` 定義部分:
```csharp
public sealed record EnemyDefinition(...) : CombatActorDefinition(...) {
    public required EnemyPool Pool { get; init; }
    public required int Act { get; init; }                  // ← 削除
}
```
を以下に修正（`Pool.Act` が canonical）:
```csharp
public sealed record EnemyDefinition(
    string Id, string Name, string ImageId,
    int Hp,
    EnemyPool Pool,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves)
    : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);
```

- [ ] **Step 3: 第 2-5 章の `MoveKind` を 7 値に拡張（`Debuff` 追加）**

```csharp
public enum MoveKind { Attack, Defend, Buff, Heal, Multi, Unknown }
```
を以下に置換:
```csharp
public enum MoveKind { Attack, Defend, Buff, Debuff, Heal, Multi, Unknown }
```

`Kind` のマッピング解説部分も更新（`.is-debuff` を含める）:
```
`Kind` は `battle-v10.html` の `.is-attack/.is-defend/.is-buff/.is-debuff/.is-heal/.is-unknown` へマッピング。
```

> battle-v10.html に `.is-debuff` CSS が存在しない場合、Phase 10.4 で追加する旨を spec の脚注に追記。

- [ ] **Step 4: 第 4-5 章の「敵 attack scope=all 強制」記述を修正**

旧記述:
> 「敵 attack の scope は強制 all」は Effect 正規化レイヤーで対応。Move JSON に `scope: "single"` の attack があってもロード時に `all` に書き換える。

新記述:
> 敵 attack effect の `scope` は **JSON 段階で `"all"` を直書きする運用**（Phase 10.1.B 移行で全敵 JSON 適用済み）。ロード時の自動書換は行わない。

- [ ] **Step 5: 第 10-1 章の HP ロール記述を修正**

旧:
> 2. 敵を `EncounterDefinition.EnemyIds` から生成、HP は `[HpMin, HpMax]` 範囲の乱数

新:
> 2. 敵を `EncounterDefinition.EnemyIds` から生成、HP は `EnemyDefinition.Hp` をそのまま採用（乱数化は将来拡張ポイント）

- [ ] **Step 6: 第 5-2 / 5-3 章に「敵 / プレイヤーの attack 発射タイミング非対称」を補記**

第 5-2 章（Attack effect の蓄積仕様）の末尾に以下の段落を追加:

```
### 5-2-1. 敵 attack と プレイヤー attack の発射タイミング非対称

- **プレイヤー attack**: AttackPool に蓄積され、ターン終了時 (`PlayerAttacking` フェーズ) に
  「Single → Random → All」の順で 1 回ずつ発射される（同 scope 内は 1 まとめ）。
- **敵 attack**: `EnemyAttacking` フェーズで move の各 attack effect が **per-effect 即時発射**。
  `Effects: [{attack 11}, {attack 11}]`（hits=2 相当）は 2 回別々のダメージ判定として処理される。
  AttackPool の利用は内部実装の詳細だが、ターン終了で都度ドレインされる扱い。

この非対称により、プレイヤーは「複数 attack カードをコンボで蓄積→単発の大きな一撃」、
敵は「1 ターン中に複数の独立した連撃」というゲーム体験になる。
```

- [ ] **Step 7: コミット**

```bash
git add docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md
git commit -m "docs(spec): amend Phase 10 spec for 10.1.B decisions (single Hp, MoveKind 7, enemy/player attack asymmetry)"
```

---

## Task 17: 全テスト緑確認 + Phase 5 placeholder 動作確認 + タグ付け

**Files:** なし（最終確認のみ）

- [ ] **Step 1: 全プロジェクトビルド**

Run: `dotnet build`
Expected: 警告 0、エラー 0

- [ ] **Step 2: 全テスト実行**

Run: `dotnet test`
Expected: 全テスト緑（Phase 10.1.B 開始前の数 + 本 plan 追加分）

- [ ] **Step 3: 旧フィールド名 / 旧 namespace の grep 確認**

```bash
grep -rn "RoguelikeCardGame\.Core\.Enemy" src tests --include="*.cs"
```
Expected: マッチなし（残りは spec 文書とプラン文書のみ）

```bash
grep -rn "HpMin\|HpMax" src tests --include="*.cs"
```
Expected: マッチなし

```bash
grep -rn "DamageMin\|DamageMax\|BlockMin\|BlockMax\|AmountMin\|AmountMax" src tests --include="*.cs"
```
Expected: マッチなし

- [ ] **Step 4: 敵 JSON 旧形式の grep 確認**

```bash
grep -l '"hpMin"\|"hpMax"\|"damageMin"\|"damageMax"\|"hits"\|"blockMin"\|"blockMax"\|"amountMin"\|"amountMax"' src/Core/Data/Enemies/*.json
```
Expected: ヒットなし

```bash
grep -l '"buff"\|"ritual"\|"enrage"\|"curl_up"\|"activate"\|"split"' src/Core/Data/Enemies/*.json
```
Expected: ヒットなし（旧 JSON の `"buff"` キーも、未対応 buff 名も両方ゼロ）

- [ ] **Step 5: src/Core/Enemy/ ディレクトリ消滅確認**

```bash
test ! -d src/Core/Enemy && echo "OK: src/Core/Enemy is gone"
```
Expected: `OK: src/Core/Enemy is gone`

- [ ] **Step 6: Phase 5 placeholder 動作確認（手動）**

ターミナル 1:
```bash
dotnet run --project src/Server
```

ターミナル 2:
```bash
cd src/Client && npm run dev
```

ブラウザで:
1. ログイン
2. 新規ラン開始
3. マップで敵マスを選択
4. 戦闘画面が出て即勝利（Phase 5 placeholder 仕様）
5. 報酬画面でカード選択
6. マップに戻る

Expected: Phase 10.1.B 着手前と同じ挙動で動く

- [ ] **Step 7: タグ付け + push**

```bash
git tag phase10-1B-complete
git push origin master
git push origin phase10-1B-complete
```

完了。次は Phase 10.1.C（Potion / Relic 拡張）の brainstorming/writing-plans サイクル。

---

## 完了判定チェックリスト

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全テスト緑
- [ ] 旧フィールド名 (`HpMin/Max`, `DamageMin/Max`, `Hits`, `BlockMin/Max`, `AmountMin/Max`) が production / test に対する grep で 0 件
- [ ] 旧 JSON フィールド (`hpMin`, `hpMax`, `damageMin`, `damageMax`, `hits`, `blockMin`, `blockMax`, `buff`, `amountMin`, `amountMax`) が `src/Core/Data/Enemies/*.json` に 0 件
- [ ] 未対応 buff 名 (`ritual`, `enrage`, `curl_up`, `activate`, `split`) が embedded JSON に 0 件
- [ ] `src/Core/Enemy/` ディレクトリ消滅
- [ ] `src/Core/Data/Units/` 空フォルダ存在
- [ ] 親 Phase 10 spec の 6 箇所補記済み
- [ ] Phase 5 placeholder バトルが手動で動作確認済み
- [ ] `phase10-1B-complete` タグが切られて origin に push 済み

---

## 補足: Phase 10.1.B で**変更しない**もの

- `BattleState` / 実行時 `CombatActor` / `AttackPool` / `BlockPool` （Phase 10.2）
- `StatusDefinition` 静的リスト （Phase 10.2）
- 実行時の attack 発射ロジック（pool 蓄積 vs per-effect 即時発射）（Phase 10.2）
- `RelicDefinition` の Trigger 拡張 / `Implemented` フラグ （Phase 10.1.C）
- `PotionDefinition.IsUsableOutsideBattle` / `BattleOnly` 効果のスキップ（Phase 10.1.C）
- 召喚カード（`CardType.Unit`）の具体データ（Phase 10.2）
- `BattleHub` / `BattleStateDto` （Phase 10.3）
- `BattleScreen.tsx` / battle-v10.html ポート（Phase 10.4）
- `BattlePlaceholder.cs` の削除（Phase 10.5、最終 cleanup）

---

## 参照

- 設計書（10.1.B）: [`../specs/2026-04-26-phase10-1B-move-unification-design.md`](../specs/2026-04-26-phase10-1B-move-unification-design.md)
- 親 spec（Phase 10）: [`../specs/2026-04-25-phase10-battle-system-design.md`](../specs/2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン plan: [`2026-04-25-phase10-1A-card-effect-unification.md`](2026-04-25-phase10-1A-card-effect-unification.md)
- ロードマップ: [`2026-04-20-roadmap.md`](2026-04-20-roadmap.md)
