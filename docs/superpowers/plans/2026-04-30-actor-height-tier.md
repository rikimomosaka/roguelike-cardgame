# Actor Height Tier + シルエット placeholder 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 全 combat actor 系 JSON (enemy / unit / playable character) に `heightTier: int` (1〜10、未指定で 5) フィールドを追加し、立ち絵未配置時にシルエット矩形 placeholder を tier 連動の高さで描画。各キャラ名を HP ゲージ直下に表示（hero は accountId）。

**Architecture:** Core (C#) record + JSON loader 拡張 → JSON 36 ファイル更新 → Server CatalogController DTO 拡張 + 新 `/catalog/characters` endpoint → Client TS types + 新 `useCharacterCatalog` hook + dtoAdapter で hero に accountId・heightTier wiring → BattleScreen に silhouette 分岐と status-name 要素追加。実画像が `image` に設定された時点で placeholder は自動的に上書きされる。

**Tech Stack:** C# .NET 10 + System.Text.Json (Core), ASP.NET Core (Server), React 19 + TypeScript + Vite (Client), xUnit / vitest.

**Spec:** `docs/superpowers/specs/2026-04-30-actor-height-tier-design.md`

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Battle/Definitions/CombatActorDefinition.cs` | Modify | 基底 record に `HeightTier` 追加 |
| `src/Core/Battle/Definitions/EnemyDefinition.cs` | Modify | 末尾に `HeightTier=5` 追加、base に pass-through |
| `src/Core/Battle/Definitions/UnitDefinition.cs` | Modify | 同上 |
| `src/Core/Data/CharacterDefinition.cs` | Modify | record 末尾に `HeightTier=5` 追加 |
| `src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs` | Modify | `heightTier` 任意パース + 範囲検証 |
| `src/Core/Battle/Definitions/Loaders/UnitJsonLoader.cs` | Modify | 同上 |
| `src/Core/Data/CharacterJsonLoader.cs` | Modify | 同上 |
| `tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs` | Modify | TDD: heightTier 未指定 / 値あり / 範囲外 |
| `tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs` | Modify | 同上 |
| `tests/Core.Tests/Data/CharacterJsonLoaderTests.cs` | Modify | 同上 |
| `src/Core/Data/Enemies/*.json` (34 files) | Modify | `heightTier` 値追加 |
| `src/Core/Data/Units/wisp.json` | Modify | `heightTier: 3` 追加 |
| `src/Core/Data/Characters/default.json` | Modify | `heightTier: 5` 追加 |
| `src/Server/Controllers/CatalogController.cs` | Modify | enemies/units DTO に HeightTier、新 GetCharacters endpoint |
| `tests/Server.Tests/CatalogControllerTests.cs` | Modify | heightTier 露出と /catalog/characters エンドポイントのテスト |
| `src/Client/src/api/types.ts` | Modify | EnemyCatalogEntryDto / UnitCatalogEntryDto に heightTier、新 CharacterCatalogEntryDto |
| `src/Client/src/api/catalog.ts` | Modify | fetchCharacterCatalog + CharacterCatalog 型 export |
| `src/Client/src/hooks/useCharacterCatalog.ts` | Create | useUnitCatalog の mirror |
| `src/Client/src/screens/battleScreen/dtoAdapter.ts` | Modify | toCharacterDemo に accountId + characterCatalog 引数、hero name=accountId |
| `src/Client/src/screens/battleScreen/dtoAdapter.test.ts` | Create | toCharacterDemo の名前/heightTier 単体テスト |
| `src/Client/src/screens/BattleScreen.tsx` | Modify | sprite 3 分岐 + status-name 要素 + 呼び出し側 |
| `src/Client/src/screens/BattleScreen.css` | Modify | `.sprite--silhouette` と `.status-name` スタイル |

---

## Task 1: Core records に HeightTier を追加

**Files:**
- Modify: `src/Core/Battle/Definitions/CombatActorDefinition.cs`
- Modify: `src/Core/Battle/Definitions/EnemyDefinition.cs`
- Modify: `src/Core/Battle/Definitions/UnitDefinition.cs`
- Modify: `src/Core/Data/CharacterDefinition.cs`

`HeightTier` を末尾 + default 5 で追加することで既存呼び出し全てを互換維持する。

### Step 1.1: `CombatActorDefinition` に HeightTier 追加

- [ ] `src/Core/Battle/Definitions/CombatActorDefinition.cs` を以下に置き換え：

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 戦闘に参加するキャラクターの静的定義（敵・召喚キャラの共通基底）。
/// HP は単一値。乱数化は将来拡張ポイント。
/// HeightTier は立ち絵の高さ段階 (1〜10、5 が標準)。
/// Phase 10 設計書（10.1.B）第 3-3 章参照。
/// </summary>
public abstract record CombatActorDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    int HeightTier,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
```

### Step 1.2: `EnemyDefinition` に HeightTier=5 を末尾追加

- [ ] `src/Core/Battle/Definitions/EnemyDefinition.cs` を以下に置き換え：

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
    IReadOnlyList<MoveDefinition> Moves,
    int HeightTier = 5)
    : CombatActorDefinition(Id, Name, ImageId, Hp, HeightTier, InitialMoveId, Moves);
```

### Step 1.3: `UnitDefinition` に HeightTier=5 を末尾追加

- [ ] `src/Core/Battle/Definitions/UnitDefinition.cs` を以下に置き換え：

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
    int? LifetimeTurns = null,
    int HeightTier = 5)
    : CombatActorDefinition(Id, Name, ImageId, Hp, HeightTier, InitialMoveId, Moves);
```

### Step 1.4: `CharacterDefinition` に HeightTier=5 を末尾追加

- [ ] `src/Core/Data/CharacterDefinition.cs` を以下に置き換え：

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Data;

/// <summary>プレイアブルキャラのマスター定義。Phase 5 では "default" のみ使用。</summary>
public sealed record CharacterDefinition(
    string Id,
    string Name,
    int MaxHp,
    int StartingGold,
    int PotionSlotCount,
    IReadOnlyList<string> Deck,
    int HeightTier = 5);
```

### Step 1.5: dotnet build と既存テストで互換性確認

- [ ] Run: `dotnet build`
  Expected: 0 警告 0 エラー。既存の Definition 呼び出しは positional/named どちらでも互換。
- [ ] Run: `dotnet test --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: Core 986/986、Server 190/190 (skipped 2)。

### Step 1.6: コミット

- [ ] Run:

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame
git add src/Core/Battle/Definitions/CombatActorDefinition.cs src/Core/Battle/Definitions/EnemyDefinition.cs src/Core/Battle/Definitions/UnitDefinition.cs src/Core/Data/CharacterDefinition.cs
git commit -m "$(cat <<'EOF'
feat(core): add HeightTier to actor / character definitions

Adds an int HeightTier field to CombatActorDefinition (and propagates
through EnemyDefinition / UnitDefinition with default 5 at the leaf
record so existing call sites remain source-compatible). Also adds
HeightTier=5 default to CharacterDefinition. Loaders + JSON wiring land
in subsequent commits.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: JSON loaders に heightTier パースを追加（TDD）

**Files:**
- Modify: `src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs`
- Modify: `src/Core/Battle/Definitions/Loaders/UnitJsonLoader.cs`
- Modify: `src/Core/Data/CharacterJsonLoader.cs`
- Modify: `tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs`
- Modify: `tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs` (存在しなければ Create)
- Modify: `tests/Core.Tests/Data/CharacterJsonLoaderTests.cs`

### Step 2.1: EnemyJsonLoader テストに heightTier ケース追加（RED）

- [ ] まず既存テストファイルの構造を確認：`grep -n "Parse\|HeightTier" tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs | head`

- [ ] テストを 4 件追加（既存の `[Fact]` 群の末尾に）：

```csharp
[Fact]
public void Parse_heightTier_missing_defaults_to_5()
{
    var json = """
    {
      "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
      "act": 1, "tier": "Weak", "initialMoveId": "m",
      "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
        "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
    }
    """;
    var def = EnemyJsonLoader.Parse(json);
    Assert.Equal(5, def.HeightTier);
}

[Fact]
public void Parse_heightTier_value_is_preserved()
{
    var json = """
    {
      "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
      "act": 1, "tier": "Weak", "initialMoveId": "m", "heightTier": 7,
      "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
        "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
    }
    """;
    var def = EnemyJsonLoader.Parse(json);
    Assert.Equal(7, def.HeightTier);
}

[Fact]
public void Parse_heightTier_below_range_throws()
{
    var json = """
    {
      "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
      "act": 1, "tier": "Weak", "initialMoveId": "m", "heightTier": 0,
      "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
        "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
    }
    """;
    Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
}

[Fact]
public void Parse_heightTier_above_range_throws()
{
    var json = """
    {
      "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
      "act": 1, "tier": "Weak", "initialMoveId": "m", "heightTier": 11,
      "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
        "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
    }
    """;
    Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
}
```

### Step 2.2: テスト実行で RED 確認

- [ ] Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyJsonLoaderTests" --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 4 件 FAIL（最初は default 5 が来ない、ほか throw されない / そもそも `HeightTier` プロパティが record に出てくるが loader が値をセットしない）。

### Step 2.3: EnemyJsonLoader に heightTier パース実装（GREEN）

- [ ] `src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs` の `Parse` メソッド内、`var moves = ParseMoves(...);` の直後（コンストラクタ呼び出しの直前）に追加：

```csharp
                var heightTier = ParseOptionalIntInRange(root, "heightTier", 5, 1, 10, id);
```

- [ ] 同ファイルの末尾（`GetRequiredInt` 直後）に新ヘルパーを追加：

```csharp
    private static int ParseOptionalIntInRange(
        JsonElement el, string key, int defaultValue, int min, int max, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException(
                $"\"{key}\" は数値である必要があります。{ctx}");
        }
        int n = v.GetInt32();
        if (n < min || n > max)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException(
                $"\"{key}\" の値 {n} は {min}..{max} の範囲外です。{ctx}");
        }
        return n;
    }
```

- [ ] `Parse` のコンストラクタ呼び出しを `heightTier` 渡しに置き換える（既存の `return new EnemyDefinition(...)` 行を編集）：

```csharp
                return new EnemyDefinition(id, name, imageId, hp,
                    new EnemyPool(act, tier), initialMoveId, moves, heightTier);
```

### Step 2.4: テスト実行で GREEN 確認

- [ ] Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EnemyJsonLoaderTests" --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 全テスト PASS（既存 + 新規 4 件）。

### Step 2.5: UnitJsonLoader にも同じパターン適用（RED → GREEN）

- [ ] まず `tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs` が存在するか確認：

  Run: `ls tests/Core.Tests/Battle/Definitions/Loaders/`

- [ ] **存在する場合**：4 件のテストを追加（Step 2.1 と同じパターン、ただし JSON は unit 形式）：

```csharp
[Fact]
public void Parse_heightTier_missing_defaults_to_5_unit()
{
    var json = """
    {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m",
     "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
       "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
    """;
    var def = UnitJsonLoader.Parse(json);
    Assert.Equal(5, def.HeightTier);
}

[Fact]
public void Parse_heightTier_value_is_preserved_unit()
{
    var json = """
    {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m","heightTier":3,
     "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
       "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
    """;
    var def = UnitJsonLoader.Parse(json);
    Assert.Equal(3, def.HeightTier);
}

[Fact]
public void Parse_heightTier_below_range_throws_unit()
{
    var json = """
    {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m","heightTier":0,
     "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
       "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
    """;
    Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse(json));
}

[Fact]
public void Parse_heightTier_above_range_throws_unit()
{
    var json = """
    {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m","heightTier":11,
     "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
       "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
    """;
    Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse(json));
}
```

- [ ] **存在しない場合**：Create `tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs` with the 4 tests above wrapped in:

```csharp
using RoguelikeCardGame.Core.Battle.Definitions.Loaders;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions.Loaders;

public class UnitJsonLoaderTests
{
    // [4 tests above]
}
```

- [ ] Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~UnitJsonLoaderTests" --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 4 件 FAIL（heightTier 未対応）。

- [ ] `src/Core/Battle/Definitions/Loaders/UnitJsonLoader.cs` の `Parse` を編集：moves パース後に
```csharp
        var heightTier = ParseOptionalIntInRange(root, "heightTier", 5, 1, 10, id);
```
を追加し、`return new UnitDefinition(...)` の最後に `, HeightTier: heightTier` を named arg で追加。

- [ ] 同ファイル末尾に EnemyJsonLoader と同じヘルパーを `UnitJsonException` で投げる版として追加（コピー＆エラー型のみ差し替え）：

```csharp
    private static int ParseOptionalIntInRange(
        JsonElement el, string key, int defaultValue, int min, int max, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException(
                $"\"{key}\" は数値である必要があります。{ctx}");
        }
        int n = v.GetInt32();
        if (n < min || n > max)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException(
                $"\"{key}\" の値 {n} は {min}..{max} の範囲外です。{ctx}");
        }
        return n;
    }
```

- [ ] Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~UnitJsonLoaderTests" --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 全テスト PASS。

### Step 2.6: CharacterJsonLoader にも同じパターン適用（RED → GREEN）

- [ ] `tests/Core.Tests/Data/CharacterJsonLoaderTests.cs` の有無確認：

  Run: `ls tests/Core.Tests/Data/`

- [ ] テストを 4 件追加（Step 2.1 と同じ pattern、JSON は character 形式）：

```csharp
[Fact]
public void Parse_heightTier_missing_defaults_to_5_character()
{
    var json = """
    {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
     "deck":["strike"]}
    """;
    var def = CharacterJsonLoader.Parse(json);
    Assert.Equal(5, def.HeightTier);
}

[Fact]
public void Parse_heightTier_value_is_preserved_character()
{
    var json = """
    {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
     "deck":["strike"],"heightTier":4}
    """;
    var def = CharacterJsonLoader.Parse(json);
    Assert.Equal(4, def.HeightTier);
}

[Fact]
public void Parse_heightTier_below_range_throws_character()
{
    var json = """
    {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
     "deck":["strike"],"heightTier":0}
    """;
    // CharacterJsonLoader が独自 Exception を持つか確認、無ければ JsonException
    Assert.ThrowsAny<System.Exception>(() => CharacterJsonLoader.Parse(json));
}

[Fact]
public void Parse_heightTier_above_range_throws_character()
{
    var json = """
    {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
     "deck":["strike"],"heightTier":11}
    """;
    Assert.ThrowsAny<System.Exception>(() => CharacterJsonLoader.Parse(json));
}
```

- [ ] Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CharacterJsonLoaderTests" --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 4 件 FAIL（heightTier 未対応）。

- [ ] `src/Core/Data/CharacterJsonLoader.cs` を読み込み、既存の Parse 構造を確認：

  Run: `cat src/Core/Data/CharacterJsonLoader.cs | head -80`

- [ ] CharacterJsonLoader の `Parse` 内、現在の `return new CharacterDefinition(...)` の直前に：

```csharp
        var heightTier = ParseOptionalIntInRange(root, "heightTier", 5, 1, 10, id);
```

を追加し、return 文の最後に `, HeightTier: heightTier` を named arg で追加。例外型は CharacterJsonLoader に既存のものがあれば使う、なければ `InvalidOperationException`：

```csharp
    private static int ParseOptionalIntInRange(
        JsonElement el, string key, int defaultValue, int min, int max, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (v.ValueKind != JsonValueKind.Number)
            throw new InvalidOperationException(
                $"\"{key}\" は数値である必要があります (character id={id})。");
        int n = v.GetInt32();
        if (n < min || n > max)
            throw new InvalidOperationException(
                $"\"{key}\" の値 {n} は {min}..{max} の範囲外です (character id={id})。");
        return n;
    }
```

CharacterJsonLoader 既存の例外型がある場合（`CharacterJsonException` 等）はそちらで throw。

- [ ] Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CharacterJsonLoaderTests" --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 全テスト PASS。

### Step 2.7: 全 Core.Tests で緑確認

- [ ] Run: `dotnet test tests/Core.Tests --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 986 + 12 (新規) = **998 passed** (各 loader 4 件 × 3 loader)。

### Step 2.8: コミット

- [ ] Run:

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame
git add src/Core/Battle/Definitions/Loaders/EnemyJsonLoader.cs src/Core/Battle/Definitions/Loaders/UnitJsonLoader.cs src/Core/Data/CharacterJsonLoader.cs tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs tests/Core.Tests/Data/CharacterJsonLoaderTests.cs
git commit -m "$(cat <<'EOF'
feat(core): parse heightTier in enemy / unit / character JSON loaders

heightTier is optional (default 5) and must lie in [1, 10]; out-of-range
values throw the loader-specific exception so bad data fails loudly at
load time. Adds 12 tests across the three loaders covering the missing /
present / below / above paths.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: 全 36 JSON ファイルに heightTier を埋める

**Files (Modify):**
- `src/Core/Data/Enemies/*.json` (34 files)
- `src/Core/Data/Units/wisp.json`
- `src/Core/Data/Characters/default.json`

各ファイルに **新フィールド `heightTier` を `act` または同等の数値フィールドの直後に挿入**（既存フィールドの順序は維持）。

### Step 3.1: tier 1 の 3 ファイル

- [ ] `slime_acid_s.json`：`"act"` の直後に `"heightTier": 1,` を挿入
- [ ] `slime_spike_s.json`：同上、`"heightTier": 1,`
- [ ] `louse_red.json`：同上、`"heightTier": 1,`

### Step 3.2: tier 2 の 2 ファイル

- [ ] `cave_bat_a.json`：`"heightTier": 2,`
- [ ] `cave_bat_b.json`：`"heightTier": 2,`

### Step 3.3: tier 3 の 6 ファイル + wisp

- [ ] `big_slime.json`：`"heightTier": 3,`
- [ ] `mushroom_a.json`：`"heightTier": 3,`
- [ ] `mushroom_b.json`：`"heightTier": 3,`
- [ ] `goblin_a.json`：`"heightTier": 3,`
- [ ] `goblin_b.json`：`"heightTier": 3,`
- [ ] `goblin_c.json`：`"heightTier": 3,`
- [ ] `src/Core/Data/Units/wisp.json`：`"hp"` の直後に `"heightTier": 3,` を挿入

### Step 3.4: tier 4 の 4 ファイル

- [ ] `bandit.json`：`"heightTier": 4,`
- [ ] `jaw_worm.json`：`"heightTier": 4,`
- [ ] `act2_grunt.json`：`"heightTier": 4,`
- [ ] `act3_grunt.json`：`"heightTier": 4,`

### Step 3.5: tier 5 の 2 ファイル + default character

- [ ] `dark_cultist.json`：`"heightTier": 5,`
- [ ] `six_ghost.json`：`"heightTier": 5,`
- [ ] `src/Core/Data/Characters/default.json`：`"potionSlotCount"` の直後に `"heightTier": 5,` を挿入

### Step 3.6: tier 6 の 4 ファイル

- [ ] `blue_orc.json`：`"heightTier": 6,`
- [ ] `red_orc.json`：`"heightTier": 6,`
- [ ] `hobgoblin.json`：`"heightTier": 6,`
- [ ] `dire_wolf.json`：`"heightTier": 6,`

### Step 3.7: tier 7 の 3 ファイル

- [ ] `ogre.json`：`"heightTier": 7,`
- [ ] `act2_brute.json`：`"heightTier": 7,`
- [ ] `act3_brute.json`：`"heightTier": 7,`

### Step 3.8: tier 8 の 5 ファイル

- [ ] `iron_golem_a.json`：`"heightTier": 8,`
- [ ] `iron_golem_b.json`：`"heightTier": 8,`
- [ ] `iron_golem_c.json`：`"heightTier": 8,`
- [ ] `act2_elite.json`：`"heightTier": 8,`
- [ ] `act3_elite.json`：`"heightTier": 8,`

### Step 3.9: tier 9 の 2 ファイル

- [ ] `guardian_golem.json`：`"heightTier": 9,`
- [ ] `sleeping_dragon.json`：`"heightTier": 9,`

### Step 3.10: tier 10 の 3 ファイル

- [ ] `slime_king.json`：`"heightTier": 10,`
- [ ] `act2_boss.json`：`"heightTier": 10,`
- [ ] `act3_boss.json`：`"heightTier": 10,`

### Step 3.11: 全 Core.Tests で緑確認（JSON ロードが回る）

- [ ] Run: `dotnet test tests/Core.Tests --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 998 passed（loader テストは default 5 / 値あり両方カバー、json fixture roundtrip も pass）。

### Step 3.12: コミット

- [ ] Run:

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame
git add src/Core/Data/Enemies/*.json src/Core/Data/Units/wisp.json src/Core/Data/Characters/default.json
git commit -m "$(cat <<'EOF'
data(actors): annotate all 36 actor JSON files with curated heightTier

Sizes follow the spec tier table: small slimes/lice = 1, bats = 2,
goblins/mushrooms/wisps = 3, bandits/grunts = 4, hero/cultists = 5,
orcs/wolves = 6, ogres/brutes = 7, golems/elites = 8, guardian/dragon
= 9, slime king/act bosses = 10. Loaders default missing values to 5,
so any future addition without an explicit heightTier still loads.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Server CatalogController に HeightTier を露出 + GetCharacters endpoint

**Files:**
- Modify: `src/Server/Controllers/CatalogController.cs`
- Modify: `tests/Server.Tests/CatalogControllerTests.cs` (存在しなければ確認、構造に合わせて Modify or Create)

### Step 4.1: テストを書く（RED）

- [ ] まず既存テストファイルを確認：

  Run: `grep -n "GetEnemies\|GetUnits\|/catalog/enemies\|/catalog/units\|/catalog/characters" tests/Server.Tests/*.cs`

- [ ] `tests/Server.Tests/CatalogControllerTests.cs` がある場合はそこに、なければ Create。3 件追加：

```csharp
[Fact]
public async Task GetEnemies_returns_heightTier()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();
    var resp = await client.GetAsync("/api/v1/catalog/enemies");
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(body);
    // dire_wolf は spec で tier 6
    var wolf = doc.RootElement.GetProperty("dire_wolf");
    Assert.Equal(6, wolf.GetProperty("heightTier").GetInt32());
}

[Fact]
public async Task GetUnits_returns_heightTier()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();
    var resp = await client.GetAsync("/api/v1/catalog/units");
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(body);
    var wisp = doc.RootElement.GetProperty("wisp");
    Assert.Equal(3, wisp.GetProperty("heightTier").GetInt32());
}

[Fact]
public async Task GetCharacters_returns_default_with_heightTier()
{
    using var factory = new TestApiFactory();
    using var client = factory.CreateClient();
    var resp = await client.GetAsync("/api/v1/catalog/characters");
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(body);
    var def = doc.RootElement.GetProperty("default");
    Assert.Equal("default", def.GetProperty("id").GetString());
    Assert.Equal(5, def.GetProperty("heightTier").GetInt32());
}
```

`TestApiFactory` の正確な name は既存テストを参考に揃える（`grep -n "WebApplicationFactory\|TestApi" tests/Server.Tests/*.cs` で確認）。

- [ ] Run: `dotnet test tests/Server.Tests --filter "FullyQualifiedName~CatalogController" --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 3 件 FAIL（heightTier プロパティなし、/characters endpoint なし）。

### Step 4.2: EnemyCatalogEntryDto / UnitCatalogEntryDto に HeightTier 追加

- [ ] `src/Server/Controllers/CatalogController.cs` の nested record を編集：

before:
```csharp
    public sealed record EnemyCatalogEntryDto(
        string Id,
        string Name,
        string ImageId,
        int Hp,
        string InitialMoveId);

    public sealed record UnitCatalogEntryDto(
        string Id,
        string Name,
        string ImageId,
        int Hp,
        string InitialMoveId,
        int? LifetimeTurns);
```

after:
```csharp
    public sealed record EnemyCatalogEntryDto(
        string Id,
        string Name,
        string ImageId,
        int Hp,
        string InitialMoveId,
        int HeightTier);

    public sealed record UnitCatalogEntryDto(
        string Id,
        string Name,
        string ImageId,
        int Hp,
        string InitialMoveId,
        int? LifetimeTurns,
        int HeightTier);
```

### Step 4.3: GetEnemies / GetUnits の生成箇所で HeightTier を渡す

- [ ] `GetEnemies` の `result[id] = new EnemyCatalogEntryDto(...)` を：
```csharp
            result[id] = new EnemyCatalogEntryDto(
                def.Id, def.Name, def.ImageId, def.Hp, def.InitialMoveId, def.HeightTier);
```

- [ ] `GetUnits` の `result[id] = new UnitCatalogEntryDto(...)` を：
```csharp
            result[id] = new UnitCatalogEntryDto(
                def.Id, def.Name, def.ImageId, def.Hp, def.InitialMoveId, def.LifetimeTurns, def.HeightTier);
```

### Step 4.4: 新 CharacterCatalogEntryDto + GetCharacters endpoint

- [ ] `CatalogController.cs` の他の nested record と並ぶ位置（`UnitCatalogEntryDto` の直後）に新 record を追加：

```csharp
    public sealed record CharacterCatalogEntryDto(
        string Id,
        string Name,
        int MaxHp,
        int StartingGold,
        int PotionSlotCount,
        int HeightTier);
```

- [ ] `GetUnits` の直後に新 endpoint を追加：

```csharp
    [HttpGet("characters")]
    public IActionResult GetCharacters()
    {
        var result = new Dictionary<string, CharacterCatalogEntryDto>(_data.Characters.Count);
        foreach (var (id, def) in _data.Characters)
        {
            result[id] = new CharacterCatalogEntryDto(
                def.Id, def.Name, def.MaxHp, def.StartingGold, def.PotionSlotCount, def.HeightTier);
        }
        return Ok(result);
    }
```

### Step 4.5: テスト実行で GREEN 確認

- [ ] Run: `dotnet test tests/Server.Tests --filter "FullyQualifiedName~CatalogController" --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: 全テスト PASS。

- [ ] Run: `dotnet test --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: Core 998 / Server 193 (190 + 3 新規) で全緑。

### Step 4.6: コミット

- [ ] Run:

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame
git add src/Server/Controllers/CatalogController.cs tests/Server.Tests/CatalogControllerTests.cs
git commit -m "$(cat <<'EOF'
feat(server): expose heightTier on catalog endpoints + add /catalog/characters

EnemyCatalogEntryDto / UnitCatalogEntryDto gain HeightTier; a new
CharacterCatalogEntryDto + GET /api/v1/catalog/characters endpoint lets
the client read the playable character roster (currently just default).
Three new server tests cover heightTier round-trip from JSON to API.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Client TS types + useCharacterCatalog hook

**Files:**
- Modify: `src/Client/src/api/types.ts`
- Modify: `src/Client/src/api/catalog.ts`
- Create: `src/Client/src/hooks/useCharacterCatalog.ts`

### Step 5.1: api/types.ts を編集

- [ ] `EnemyCatalogEntryDto` と `UnitCatalogEntryDto` の export type 定義を見つけて `heightTier: number` を追加：

  Run: `grep -n "EnemyCatalogEntryDto\|UnitCatalogEntryDto\|CharacterCatalogEntryDto" src/Client/src/api/types.ts`

- [ ] before（例）:
```ts
export type EnemyCatalogEntryDto = {
  id: string
  name: string
  imageId: string
  hp: number
  initialMoveId: string
}
```

  after:
```ts
export type EnemyCatalogEntryDto = {
  id: string
  name: string
  imageId: string
  hp: number
  initialMoveId: string
  heightTier: number
}
```

- [ ] `UnitCatalogEntryDto` も同様に `heightTier: number` を末尾追加。

- [ ] `UnitCatalogEntryDto` の直後に新 type を追加：

```ts
export type CharacterCatalogEntryDto = {
  id: string
  name: string
  maxHp: number
  startingGold: number
  potionSlotCount: number
  heightTier: number
}
```

### Step 5.2: api/catalog.ts に CharacterCatalog 関連を追加

- [ ] `src/Client/src/api/catalog.ts` の末尾（fetchUnitCatalog の直後）に追加：

```ts
import type { CharacterCatalogEntryDto } from './types'

export type CharacterCatalog = Record<string, CharacterCatalogEntryDto>

export async function fetchCharacterCatalog(): Promise<CharacterCatalog> {
  return await apiRequest<CharacterCatalog>('GET', '/catalog/characters', {})
}
```

`CharacterCatalogEntryDto` の import は file 先頭の既存 `import type { EnemyCatalogEntryDto, UnitCatalogEntryDto } from './types'` 行に追加：

```ts
import type {
  CharacterCatalogEntryDto,
  EnemyCatalogEntryDto,
  UnitCatalogEntryDto,
} from './types'
```

### Step 5.3: useCharacterCatalog hook を新設（useUnitCatalog の mirror）

- [ ] まず `src/Client/src/hooks/useUnitCatalog.ts` を読んで構造を確認：

  Run: `cat src/Client/src/hooks/useUnitCatalog.ts`

- [ ] Create: `src/Client/src/hooks/useCharacterCatalog.ts`：

```ts
import { useEffect, useState } from 'react'
import { fetchCharacterCatalog, type CharacterCatalog } from '../api/catalog'

export type CharacterNameMap = Record<string, string>

export function useCharacterCatalog(): {
  catalog: CharacterCatalog | null
  names: CharacterNameMap
  loading: boolean
} {
  const [catalog, setCatalog] = useState<CharacterCatalog | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    fetchCharacterCatalog()
      .then((c) => {
        if (!cancelled) {
          setCatalog(c)
          setLoading(false)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setCatalog(null)
          setLoading(false)
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  const names: CharacterNameMap = {}
  if (catalog) {
    for (const [id, entry] of Object.entries(catalog)) {
      names[id] = entry.name
    }
  }
  return { catalog, names, loading }
}
```

### Step 5.4: 型チェック + build

- [ ] Run: `cd src/Client && npm run build`
  Expected: 0 error / 0 warning。

### Step 5.5: vitest 全件 (regression check)

- [ ] Run: `cd src/Client && npx vitest run 2>&1 | tail -8`
  Expected: 24 files / 135 tests passed（既存テストは catalog 形が変わっただけで挙動変わらず）。

### Step 5.6: コミット

- [ ] Run:

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame
git add src/Client/src/api/types.ts src/Client/src/api/catalog.ts src/Client/src/hooks/useCharacterCatalog.ts
git commit -m "$(cat <<'EOF'
feat(client): add heightTier to catalog DTOs and useCharacterCatalog hook

Mirrors the server's catalog change: EnemyCatalogEntryDto /
UnitCatalogEntryDto gain heightTier, a new CharacterCatalogEntryDto
type lands in api/types, and fetchCharacterCatalog + useCharacterCatalog
follow the existing useUnitCatalog shape for the new /catalog/characters
endpoint.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: dtoAdapter で hero name=accountId / heightTier wiring（TDD）

**Files:**
- Modify: `src/Client/src/screens/battleScreen/dtoAdapter.ts`
- Create: `src/Client/src/screens/battleScreen/dtoAdapter.test.ts`

### Step 6.1: テストを新規作成（RED）

- [ ] Create: `src/Client/src/screens/battleScreen/dtoAdapter.test.ts`：

```ts
import { describe, expect, it } from 'vitest'
import type { CombatActorDto } from '../../api/types'
import type { CharacterCatalog, EnemyCatalog, UnitCatalog } from '../../api/catalog'
import { toCharacterDemo } from './dtoAdapter'

function heroActor(): CombatActorDto {
  return {
    instanceId: 'hero_inst',
    definitionId: 'hero',
    side: 'Ally',
    slotIndex: 0,
    currentHp: 80,
    maxHp: 80,
    blockDisplay: 0,
    statuses: {},
    intent: null,
  } as unknown as CombatActorDto
}

function enemyActor(definitionId: string): CombatActorDto {
  return {
    instanceId: `${definitionId}_inst`,
    definitionId,
    side: 'Enemy',
    slotIndex: 0,
    currentHp: 10,
    maxHp: 10,
    blockDisplay: 0,
    statuses: {},
    intent: null,
  } as unknown as CombatActorDto
}

function summonActor(definitionId: string): CombatActorDto {
  return {
    instanceId: `${definitionId}_inst`,
    definitionId,
    side: 'Ally',
    slotIndex: 1,
    currentHp: 30,
    maxHp: 30,
    blockDisplay: 0,
    statuses: {},
    intent: null,
  } as unknown as CombatActorDto
}

const characterCatalog: CharacterCatalog = {
  default: {
    id: 'default', name: '見習い冒険者',
    maxHp: 80, startingGold: 99, potionSlotCount: 3, heightTier: 5,
  },
}

const enemyCatalog: EnemyCatalog = {
  dire_wolf: {
    id: 'dire_wolf', name: 'ダイア・ウルフ', imageId: 'wolf_dire',
    hp: 40, initialMoveId: 'howl', heightTier: 6,
  },
}

const unitCatalog: UnitCatalog = {
  wisp: {
    id: 'wisp', name: 'ウィスプ', imageId: 'wisp',
    hp: 30, initialMoveId: 'wisp_strike', lifetimeTurns: 3, heightTier: 3,
  },
}

describe('toCharacterDemo', () => {
  it('hero name uses accountId', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.name).toBe('alice')
    expect(demo.spriteKind).toBe('hero')
  })

  it('hero heightTier comes from character catalog (default=5)', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(5)
  })

  it('hero falls back to heightTier=5 if character catalog is null', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: null,
    }, 'alice')
    expect(demo.heightTier).toBe(5)
  })

  it('enemy heightTier comes from enemy catalog', () => {
    const demo = toCharacterDemo(enemyActor('dire_wolf'), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(6)
    expect(demo.name).toBe('ダイア・ウルフ')
  })

  it('summon heightTier comes from unit catalog', () => {
    const demo = toCharacterDemo(summonActor('wisp'), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(3)
    expect(demo.name).toBe('ウィスプ')
  })

  it('enemy with missing catalog entry falls back to heightTier=5', () => {
    const demo = toCharacterDemo(enemyActor('unknown'), {
      enemies: {}, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(5)
  })
})
```

### Step 6.2: テスト実行で RED 確認

- [ ] Run: `cd src/Client && npx vitest run src/screens/battleScreen/dtoAdapter.test.ts 2>&1 | tail -15`
  Expected: 6 件 FAIL（toCharacterDemo は accountId 第 3 引数を受けてない / characters catalog を見ていない / 名前が "主人公" になってる）。

### Step 6.3: dtoAdapter を改修

- [ ] `src/Client/src/screens/battleScreen/dtoAdapter.ts` を編集：

  type 拡張：
```ts
type CharacterCatalogs = {
  enemies: EnemyCatalog | null
  units: UnitCatalog | null
  characters: CharacterCatalog | null
}
```

  signature：
```ts
export function toCharacterDemo(
  actor: CombatActorDto,
  catalogs: CharacterCatalogs,
  accountId: string,
): CharacterDemo {
```

  内部のロジック更新（既存の hero / enemy / summon 分岐をリファクタ）：
```ts
  const isHero = actor.definitionId === HERO_DEFINITION_ID
  const enemyDef = !isHero && actor.side === 'Enemy'
    ? catalogs.enemies?.[actor.definitionId]
    : undefined
  const unitDef = !isHero && actor.side === 'Ally'
    ? catalogs.units?.[actor.definitionId]
    : undefined
  const characterDef = isHero
    ? catalogs.characters?.[actor.definitionId]
    : undefined

  const name = isHero
    ? accountId
    : enemyDef?.name ?? unitDef?.name ?? actor.definitionId
  const sprite = isHero
    ? HERO_FALLBACK.imageId
    : enemyDef?.imageId ?? unitDef?.imageId ?? '?'
  const desc = isHero ? HERO_FALLBACK.description : ''

  const spriteKind: CharacterDemo['spriteKind'] = isHero
    ? 'hero'
    : actor.side === 'Ally'
      ? 'ally'
      : 'enemy'

  const intents = actor.intent ? toIntentDemos(actor.intent) : undefined

  // Why: 立ち絵 PNG は hero のみ既存配置 (player_stand.png)。enemy / summon は
  // 実アセットが配置されたら image を流し込むため、現状 undefined。
  // heightTier はカタログ (catalog 取得失敗時 5) から引く。
  const image = isHero ? '/characters/player_stand.png' : undefined
  const heightTier = isHero
    ? characterDef?.heightTier ?? 5
    : enemyDef?.heightTier ?? unitDef?.heightTier ?? 5
```

  imports に `CharacterCatalog` を追加：
```ts
import type {
  CardCatalog,
  CharacterCatalog,
  EnemyCatalog,
  RelicCatalog,
  UnitCatalog,
} from '../../api/catalog'
```

  既存の `import type { ... } from '../../api/catalog'` 行を上記に置き換える。

### Step 6.4: テスト実行で GREEN 確認

- [ ] Run: `cd src/Client && npx vitest run src/screens/battleScreen/dtoAdapter.test.ts 2>&1 | tail -10`
  Expected: 6 件 PASS。

### Step 6.5: 既存 vitest が壊れていないか確認

- [ ] Run: `cd src/Client && npx vitest run 2>&1 | tail -8`
  Expected: 既存 135 + 新規 6 = **141 passed**。**ただし** Task 7 で BattleScreen の呼び出し側を更新するまでは型エラー / テスト失敗が出る可能性あり（toCharacterDemo に第 3 引数を渡してないため）。

  **もし型エラーが出たら：** `BattleScreen.tsx` の 2 箇所の `toCharacterDemo(a, ...)` 呼び出しに第 3 引数として `accountId` を**この commit のうちに**追加する（spec 通り。既に Props にある変数）。Step 7.1 に先取りして以下を実施：

  - `BattleScreen.tsx` line 1066, 1069 付近：
    - `toCharacterDemo(a, { enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog }, accountId)`
  - また `useCharacterCatalog` を import + 呼び出し：
    ```ts
    import { useCharacterCatalog } from '../hooks/useCharacterCatalog'
    // 関数本体内、他の hooks 群と並べて：
    const { catalog: characterCatalog } = useCharacterCatalog()
    ```

  これで build と既存テスト両方緑になる。

### Step 6.6: コミット

- [ ] Run:

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame
git add src/Client/src/screens/battleScreen/dtoAdapter.ts src/Client/src/screens/battleScreen/dtoAdapter.test.ts src/Client/src/screens/BattleScreen.tsx
git commit -m "$(cat <<'EOF'
feat(battle): wire heightTier from catalogs and accountId as hero name

toCharacterDemo now takes a third accountId argument and a characters
catalog so the hero's CharacterDemo.name reflects the logged-in account
(placeholder until proper character names land) and its heightTier is
data-driven via /catalog/characters. Enemy / summon heightTier flows
from the corresponding catalogs with a 5 fallback.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: BattleScreen sprite 3 分岐 + 名前表示

**Files:**
- Modify: `src/Client/src/screens/BattleScreen.tsx`
- Modify: `src/Client/src/screens/BattleScreen.css`

### Step 7.1: sprite render 分岐を 3 分岐に拡張

- [ ] `src/Client/src/screens/BattleScreen.tsx` の sprite render 部（`{char.image ? (` 周辺、line 351 前後）を以下に置き換え：

```tsx
      {/* Why: image があれば <img>、無くても heightTier があればシルエット
          placeholder（実アセット届くまでの仮置き）、両方無ければ text sprite。
          --tier-height CSS 変数で高さを段階制御 (1〜10)。 */}
      {char.image ? (
        <div
          className={`sprite sprite--image sprite--${char.spriteKind}`}
          style={
            char.heightTier !== undefined
              ? ({ '--tier-height': `${heightForTier(char.heightTier)}px` } as CSSProperties)
              : undefined
          }
        >
          <img src={char.image} alt={char.name} draggable={false} />
        </div>
      ) : char.heightTier !== undefined ? (
        <div
          className={`sprite sprite--silhouette sprite--${char.spriteKind}`}
          style={{ '--tier-height': `${heightForTier(char.heightTier)}px` } as CSSProperties}
          aria-label={`${char.name} (placeholder)`}
        />
      ) : (
        <div
          className={`sprite sprite--${char.spriteKind}${char.sprite.length > 2 ? ' sprite--text' : ''}`}
        >
          {char.sprite}
        </div>
      )}
```

### Step 7.2: HP ゲージ直下に status-name 要素を追加

- [ ] 同ファイルの `<div className="status-hp">` ブロックの**閉じタグ直後**（status-buffs より前）に追加：

```tsx
      <div className="status-name">{char.name}</div>
```

### Step 7.3: BattleScreen.css に silhouette + status-name スタイル追加

- [ ] `src/Client/src/screens/BattleScreen.css` の末尾に以下を追加：

```css
/* Silhouette placeholder: 実アセット未配置時の仮置き矩形 */
.sprite--silhouette {
  height: var(--tier-height, 148px);
  /* Why: 人型寄りの縦長シルエット (高さ × 0.55)。tier=10 でも幅は ~143px に収まる。 */
  width: calc(var(--tier-height, 148px) * 0.55);
  border-radius: 12px 12px 8px 8px / 16px 16px 8px 8px;
  border: 1px solid;
}
.sprite--silhouette.sprite--enemy {
  background: rgba(220, 80, 80, 0.45);
  border-color: rgba(220, 80, 80, 0.85);
}
.sprite--silhouette.sprite--ally {
  background: rgba(80, 180, 100, 0.45);
  border-color: rgba(80, 180, 100, 0.85);
}
.sprite--silhouette.sprite--hero {
  background: rgba(100, 140, 220, 0.45);
  border-color: rgba(100, 140, 220, 0.85);
}
.sprite--silhouette.sprite--elite {
  background: rgba(220, 140, 80, 0.45);
  border-color: rgba(220, 140, 80, 0.85);
}

/* Character name display below HP gauge */
.status-name {
  margin-top: 2px;
  font-size: 12px;
  text-align: center;
  color: rgba(255, 255, 255, 0.85);
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.6);
  /* Why: 長い名前 (account ID 例: rikimomosaka@gmail.com) は省略表示。 */
  max-width: 12ch;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  margin-left: auto;
  margin-right: auto;
}
```

### Step 7.4: build + 既存 vitest

- [ ] Run: `cd src/Client && npm run build`
  Expected: 0 error / 0 warning。

- [ ] Run: `cd src/Client && npx vitest run 2>&1 | tail -8`
  Expected: 141 passed（追加テストなし、既存に影響なし）。

### Step 7.5: コミット

- [ ] Run:

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame
git add src/Client/src/screens/BattleScreen.tsx src/Client/src/screens/BattleScreen.css
git commit -m "$(cat <<'EOF'
feat(battle): silhouette placeholder + character name below HP gauge

Sprite render now branches three ways: real image, tier-sized silhouette
placeholder when only heightTier is set, or text sprite as the last
fallback. Silhouette colors per spriteKind (enemy=red / ally=green /
hero=blue / elite=orange) so the size hierarchy is immediately readable
at runtime even before real stand-image PNGs land. Each character also
gets a name label below its HP bar (truncated for long account IDs).

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: 全体ビルド・テスト・push + 手動スモーク

**Files:** なし（検証のみ）

### Step 8.1: dotnet build + dotnet test

- [ ] Run: `dotnet build --nologo 2>&1 | tail -8`
  Expected: 0 警告 0 エラー。
- [ ] Run: `dotnet test --nologo --verbosity quiet 2>&1 | tail -10`
  Expected: Core 998/998、Server 193/193 (skipped 2)。

### Step 8.2: client build + vitest

- [ ] Run: `cd src/Client && npm run build 2>&1 | tail -10`
  Expected: 0 error / 0 warning。
- [ ] Run: `cd src/Client && npx vitest run 2>&1 | tail -8`
  Expected: 24+ files / 141 passed。

### Step 8.3: 手動スモーク（チェックリスト）

ユーザに依頼：

- [ ] `dotnet run --project src/Server` 起動 → `cd src/Client && npm run dev` 起動 → ブラウザ。
- [ ] ラン開始 → バトル進入。
- [ ] **プレイヤー：** `/characters/player_stand.png` の立ち絵がそのまま、HP バー下に**自分の accountId** が出る。
- [ ] **敵：** カラフルなシルエット（赤系）が表示。スライム系（slime_acid_s 等、tier 1）と キング・スライム（tier 10）で**目に見えて高さ差**がある。
- [ ] HP バー直下に各敵の日本語名（例：ダイア・ウルフ）が出る。
- [ ] **召喚：** ウィスプ召喚 → 緑系シルエット (tier 3) が出る、名前「ウィスプ」が下に出る。
- [ ] 敵が死亡 → シルエットがフェードアウト。
- [ ] 名前が長い場合（アカウント ID メアド等）省略表示（`...`）になる。

### Step 8.4: push

- [ ] Run: `git push 2>&1 | tail -5`
  Expected: master が remote に push される（commits 7 個）。

### Step 8.5: メモリ更新（任意）

- [ ] `MEMORY.md` の `project_phase_status.md` を 1 行更新（actor heightTier 完了）。**ユーザ要望時のみ**。

---

## 完了条件

- 上記すべての `- [ ]` がチェック済み。
- `dotnet test` 緑 (Core 998 / Server 193 + 2 skip)、`vitest run` 緑 (141)、`npm run build` / `dotnet build` 0 警告 0 エラー。
- master に 7 commits push 済み（Task 1〜7）。
- 手動スモーク全項目 OK。

## ロールバック手順

`git revert HEAD~7..HEAD` で全タスクを打ち消す。データだけ戻したいなら Task 3 だけ revert（loader が default 5 なので互換）。

## 関連ドキュメント

- spec: `docs/superpowers/specs/2026-04-30-actor-height-tier-design.md`
- 親 spec: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`
