# Phase 5 Battle Placeholder & Rewards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** マップノード到達 → 戦闘 (placeholder 即勝利) or 非戦闘 placeholder → Gold/Potion/Card 報酬 3択 → マップ復帰、の 1 周が通しで動く状態にする。実戦闘は Phase 6 以降。

**Architecture:** Core を 3 層に分けて拡張する: (1) データ層として既存 `DataCatalog` に `Encounters` / `RewardTables` / `Characters` 辞書を追加し、すべてのコンテンツを JSON 駆動化。`EnemyDefinition` は state-machine 対応に拡張。(2) 純関数サービス層として `EncounterQueue` / `BattlePlaceholder` / `RewardGenerator` / `RewardApplier` / `NodeEffectResolver` を追加。(3) `RunState` を v3 にスキーマ更新し、`ActiveBattle` / `ActiveReward` / `EncounterQueue*` / `RewardRngState` などの新フィールドを受け入れる。Server は `RunsController` に 6 個の新 endpoint (`battle/win`, `reward/gold|potion|card|proceed`, `potion/discard`) を追加し、`move` エンドポイント内で `NodeEffectResolver` を呼ぶ。Client は画面遷移ではなく 3 レイヤー (MapScreen ベース + BattleOverlay + RewardPopup) を重ねる構造に変更。

**Tech Stack:** C# .NET 10（Core/Server）、xUnit、React 19 + TypeScript + Vite + vitest + @testing-library/react（Client）。

**Spec:** [docs/superpowers/specs/2026-04-22-phase05-battle-placeholder-rewards-design.md](../specs/2026-04-22-phase05-battle-placeholder-rewards-design.md)

**Prerequisite:** Phase 4 (`phase04-map-progression` ブランチ) が master にマージされていること。`RunState` v2（`CurrentNodeId` / `VisitedNodeIds` / `UnknownResolutions` 構造）、`RunsController` の `current` / `new` / `move` / `abandon` / `heartbeat` endpoint、`MapScreen` / `InGameMenuScreen` が存在する状態が出発点。

---

## File Structure

### 新規作成

- **Core / Data**
  - `src/Core/Data/Characters/default.json` — 初期キャラ JSON（HP/Gold/PotionSlotCount/Deck）
  - `src/Core/Data/Encounters/*.json` — Encounter マスタ 20+ ファイル
  - `src/Core/Data/RewardTable/act1.json` — 報酬テーブル
  - `src/Core/Data/Cards/reward_common_*.json` x10
  - `src/Core/Data/Cards/reward_rare_*.json` x10
  - `src/Core/Data/Cards/reward_epic_*.json` x10
  - `src/Core/Data/Potions/health_potion.json`, `swift_potion.json`, `energy_potion.json`, `strength_potion.json`, `poison_potion.json`
  - `src/Core/Data/Enemies/*.json` — 追加敵 20+ ファイル（既存 4 体は state-machine 化に書き換え）
- **Core / Records**
  - `src/Core/Enemy/MoveDefinition.cs` — state-machine 遷移を表す record
  - `src/Core/Data/EncounterDefinition.cs`
  - `src/Core/Data/EncounterJsonLoader.cs`
  - `src/Core/Data/CharacterDefinition.cs`
  - `src/Core/Data/CharacterJsonLoader.cs`
  - `src/Core/Data/RewardTable.cs`
  - `src/Core/Data/RewardTableJsonLoader.cs`
- **Core / サービス**
  - `src/Core/Battle/BattleState.cs` — `BattleOutcome` enum, `BattleState`, `EnemyInstance`
  - `src/Core/Battle/EncounterQueue.cs`
  - `src/Core/Battle/BattlePlaceholder.cs`
  - `src/Core/Rewards/RewardState.cs` — `CardRewardStatus` enum, `RewardState`, `RewardRngState`, `RewardContext`, `NonBattleRewardKind`
  - `src/Core/Rewards/RewardGenerator.cs`
  - `src/Core/Rewards/RewardApplier.cs`
  - `src/Core/Run/NodeEffectResolver.cs`
- **Core テスト**
  - `tests/Core.Tests/Data/DataCatalogPhase5Tests.cs`
  - `tests/Core.Tests/Battle/EncounterQueueTests.cs`
  - `tests/Core.Tests/Battle/BattlePlaceholderTests.cs`
  - `tests/Core.Tests/Rewards/RewardGeneratorTests.cs`
  - `tests/Core.Tests/Rewards/RewardApplierTests.cs`
  - `tests/Core.Tests/Run/NodeEffectResolverTests.cs`
- **Server**
  - `src/Server/Dtos/BattleWinRequestDto.cs`
  - `src/Server/Dtos/RewardCardRequestDto.cs`
  - `src/Server/Dtos/PotionDiscardRequestDto.cs`
  - `src/Server/Dtos/RewardProceedRequestDto.cs`
  - `src/Server/Dtos/BattleStateDto.cs` — `EnemyInstanceDto` を含む
  - `src/Server/Dtos/RewardStateDto.cs`
- **Server テスト**
  - `tests/Server.Tests/Controllers/BattleEndpointsTests.cs`
  - `tests/Server.Tests/Controllers/RewardEndpointsTests.cs`
  - `tests/Server.Tests/Controllers/PotionDiscardTests.cs`
  - `tests/Server.Tests/Controllers/NonBattleMoveTests.cs`
- **Client**
  - `src/Client/src/api/battle.ts`
  - `src/Client/src/api/rewards.ts`
  - `src/Client/src/screens/BattleOverlay.tsx`
  - `src/Client/src/screens/BattleOverlay.test.tsx`
  - `src/Client/src/screens/RewardPopup.tsx`
  - `src/Client/src/screens/RewardPopup.test.tsx`
  - `src/Client/src/components/TopBar.tsx` — HP / Gold / Potion スロットのバー
  - `src/Client/src/components/PotionSlot.tsx` — 単一スロット + ホバーで捨てるメニュー

### 変更

- **Core**
  - `src/Core/Enemy/EnemyDefinition.cs` — `Moveset: IReadOnlyList<string>` を `Moves: IReadOnlyList<MoveDefinition>` + `InitialMoveId: string` + `ImageId: string` に置換
  - `src/Core/Enemy/EnemyJsonLoader.cs` — 新スキーマをパース
  - `src/Core/Data/Enemies/jaw_worm.json`, `louse_red.json`, `gremlin_nob.json`, `hexaghost.json` — state-machine 化
  - `src/Core/Data/DataCatalog.cs` — `Encounters` / `RewardTables` / `Characters` フィールド追加、`LoadFromStrings` シグネチャ更新
  - `src/Core/Data/EmbeddedDataLoader.cs` — 新 prefix `Encounters` / `RewardTable` / `Characters` 追加
  - `src/Core/Core.csproj` — `<EmbeddedResource>` エントリ追加（`Data/Encounters/*.json`, `Data/RewardTable/*.json`, `Data/Characters/*.json`）
  - `src/Core/Run/RunState.cs` — v3 スキーマに昇格、新フィールド、`NewSoloRun` シグネチャ変更
  - `src/Core/Player/StarterDeck.cs` — 削除（`CharacterDefinition` に置き換え）
- **Server**
  - `src/Server/Services/RunStartService.cs` — v3 初期化、EncounterQueue 初期化
  - `src/Server/Controllers/RunsController.cs` — 6 endpoint 追加 + `move` 内で `NodeEffectResolver` 呼び出し
  - `src/Server/Dtos/RunSnapshotDto.cs` — `ActiveBattle` / `ActiveReward` の DTO 化
- **Client**
  - `src/Client/src/api/types.ts` — `RunStateDto` v3、`BattleStateDto`、`RewardStateDto`、新 enum 追加
  - `src/Client/src/api/runs.ts` — 既存に influence なし、新関数は `battle.ts` / `rewards.ts` に分割
  - `src/Client/src/screens/MapScreen.tsx` — トップバー、BattleOverlay / RewardPopup の埋め込み、非戦闘 placeholder 反応
  - `src/Client/src/App.tsx` — `MapScreen` に `snapshot` を渡す構造は維持、3 レイヤーは `MapScreen` 内で完結

---

## Part A — Core データ層の拡張

## Task 1: MoveDefinition record

**Files:**
- Create: `src/Core/Enemy/MoveDefinition.cs`

- [ ] **Step 1: Write the record**

```csharp
namespace RoguelikeCardGame.Core.Enemy;

/// <summary>
/// 敵の行動パターン 1 ステップ。Phase 5 では <see cref="Id"/> / <see cref="NextMoveId"/> のみが
/// 使用される（表示用名前 / 次 move への遷移）が、Phase 6 の実戦闘で使う数値フィールドも
/// JSON スキーマ上は保持する。
/// </summary>
public sealed record MoveDefinition(
    string Id,
    string Kind,               // "attack", "block", "buff", "debuff", "multi" etc.
    int? DamageMin,
    int? DamageMax,
    int? Hits,
    int? BlockMin,
    int? BlockMax,
    string? Buff,              // "strength", "weak", ... (Phase 6 で参照)
    int? AmountMin,
    int? AmountMax,
    string NextMoveId);
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Core`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Core/Enemy/MoveDefinition.cs
git commit -m "feat(core-enemy): add MoveDefinition record for state-machine moves"
```

---

## Task 2: EnemyDefinition を state-machine に拡張

**Files:**
- Modify: `src/Core/Enemy/EnemyDefinition.cs`

- [ ] **Step 1: Replace the record body**

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Enemy;

/// <summary>敵のマスター定義。state-machine 形式の行動セットを持つ。</summary>
public sealed record EnemyDefinition(
    string Id,
    string Name,
    string ImageId,
    int HpMin,
    int HpMax,
    EnemyPool Pool,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
```

- [ ] **Step 2: Build (expect failures)**

Run: `dotnet build src/Core`
Expected: FAIL — `EnemyJsonLoader` と既存テストが `Moveset` / 旧コンストラクタを使っているためコンパイルエラー。Task 3 / 4 / 5 で修正する。

- [ ] **Step 3: Commit intermediate state**

このタスクでは commit しない。Task 3 / 4 と合わせて 1 commit にまとめる。

---

## Task 3: EnemyJsonLoader を新スキーマに対応

**Files:**
- Modify: `src/Core/Enemy/EnemyJsonLoader.cs`

- [ ] **Step 1: Rewrite Parse to read moves / initialMoveId / imageId**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Enemy;

public sealed class EnemyJsonException : Exception
{
    public EnemyJsonException(string message) : base(message) { }
    public EnemyJsonException(string message, Exception inner) : base(message, inner) { }
}

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

                var hpMin = GetRequiredInt(root, "hpMin", id);
                var hpMax = GetRequiredInt(root, "hpMax", id);
                if (hpMin > hpMax)
                    throw new EnemyJsonException($"hpMin ({hpMin}) は hpMax ({hpMax}) 以下である必要があります (enemy id={id})。");

                var act = GetRequiredInt(root, "act", id);
                if (act < 1 || act > 3)
                    throw new EnemyJsonException($"act の値 {act} は 1〜3 の範囲外です (enemy id={id})。");

                var tier = ParseTier(GetRequiredString(root, "tier", id), id);

                var initialMoveId = GetRequiredString(root, "initialMoveId", id);
                var moves = ParseMoves(root, "moves", id);
                if (moves.Count == 0)
                    throw new EnemyJsonException($"moves が空です (enemy id={id})。");

                // initialMoveId が moves 内にあるか
                bool found = false;
                foreach (var m in moves) if (m.Id == initialMoveId) { found = true; break; }
                if (!found)
                    throw new EnemyJsonException(
                        $"initialMoveId \"{initialMoveId}\" が moves に存在しません (enemy id={id})。");

                return new EnemyDefinition(id, name, imageId, hpMin, hpMax,
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
        "Weak" => EnemyTier.Weak,
        "Strong" => EnemyTier.Strong,
        "Elite" => EnemyTier.Elite,
        "Boss" => EnemyTier.Boss,
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

            var mid = GetRequiredString(el, "id", id);
            var kind = GetRequiredString(el, "kind", id);
            var nextMoveId = GetRequiredString(el, "nextMoveId", id);

            int? dmin = GetOptionalInt(el, "damageMin");
            int? dmax = GetOptionalInt(el, "damageMax");
            int? hits = GetOptionalInt(el, "hits");
            int? bmin = GetOptionalInt(el, "blockMin");
            int? bmax = GetOptionalInt(el, "blockMax");
            string? buff = GetOptionalString(el, "buff");
            int? amin = GetOptionalInt(el, "amountMin");
            int? amax = GetOptionalInt(el, "amountMax");

            list.Add(new MoveDefinition(mid, kind, dmin, dmax, hits, bmin, bmax, buff, amin, amax, nextMoveId));
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

    private static int? GetOptionalInt(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static string? GetOptionalString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Core`
Expected: PASS（`EnemyDefinition` と loader は一貫した）

---

## Task 4: 既存 4 体の JSON を state-machine に書き換え

**Files:**
- Modify: `src/Core/Data/Enemies/jaw_worm.json`
- Modify: `src/Core/Data/Enemies/louse_red.json`
- Modify: `src/Core/Data/Enemies/gremlin_nob.json` → rename to `hobgoblin.json`
- Modify: `src/Core/Data/Enemies/hexaghost.json` → rename to `six_ghost.json`

- [ ] **Step 1: `jaw_worm.json`**

```json
{
  "id": "jaw_worm",
  "name": "ジョウ・ワーム",
  "imageId": "jaw_worm",
  "hpMin": 40,
  "hpMax": 44,
  "act": 1,
  "tier": "Weak",
  "initialMoveId": "chomp",
  "moves": [
    { "id": "chomp",  "kind": "attack", "damageMin": 11, "damageMax": 11, "hits": 1, "nextMoveId": "thrash" },
    { "id": "thrash", "kind": "multi",  "damageMin": 7,  "damageMax": 7,  "hits": 1,
      "blockMin": 5, "blockMax": 5, "nextMoveId": "bellow" },
    { "id": "bellow", "kind": "buff",   "buff": "strength", "amountMin": 3, "amountMax": 5,
      "blockMin": 6, "blockMax": 6, "nextMoveId": "chomp" }
  ]
}
```

- [ ] **Step 2: `louse_red.json`**

```json
{
  "id": "louse_red",
  "name": "レッド・ラウス",
  "imageId": "louse_red",
  "hpMin": 10,
  "hpMax": 15,
  "act": 1,
  "tier": "Weak",
  "initialMoveId": "bite",
  "moves": [
    { "id": "bite",   "kind": "attack", "damageMin": 5, "damageMax": 7, "hits": 1, "nextMoveId": "curl" },
    { "id": "curl",   "kind": "buff",   "buff": "curl_up", "amountMin": 3, "amountMax": 3, "nextMoveId": "bite" }
  ]
}
```

- [ ] **Step 3: `gremlin_nob` を `hobgoblin.json` にリネーム**

削除: `git rm src/Core/Data/Enemies/gremlin_nob.json`
作成: `src/Core/Data/Enemies/hobgoblin.json`

```json
{
  "id": "hobgoblin",
  "name": "ホブゴブリン",
  "imageId": "hobgoblin",
  "hpMin": 82,
  "hpMax": 86,
  "act": 1,
  "tier": "Elite",
  "initialMoveId": "bellow",
  "moves": [
    { "id": "bellow", "kind": "buff", "buff": "enrage", "amountMin": 2, "amountMax": 2, "nextMoveId": "rush" },
    { "id": "rush",   "kind": "attack", "damageMin": 14, "damageMax": 14, "hits": 1, "nextMoveId": "skull_bash" },
    { "id": "skull_bash", "kind": "attack", "damageMin": 6, "damageMax": 6, "hits": 1, "nextMoveId": "rush" }
  ]
}
```

- [ ] **Step 4: `hexaghost` を `six_ghost.json` にリネーム**

削除: `git rm src/Core/Data/Enemies/hexaghost.json`
作成: `src/Core/Data/Enemies/six_ghost.json`

```json
{
  "id": "six_ghost",
  "name": "シックスゴースト",
  "imageId": "six_ghost",
  "hpMin": 250,
  "hpMax": 250,
  "act": 1,
  "tier": "Boss",
  "initialMoveId": "activate",
  "moves": [
    { "id": "activate", "kind": "buff", "buff": "activate", "amountMin": 1, "amountMax": 1, "nextMoveId": "divider" },
    { "id": "divider",  "kind": "multi", "damageMin": 6, "damageMax": 6, "hits": 6, "nextMoveId": "sear" },
    { "id": "sear",     "kind": "attack", "damageMin": 6, "damageMax": 6, "hits": 1, "nextMoveId": "tackle" },
    { "id": "tackle",   "kind": "attack", "damageMin": 5, "damageMax": 5, "hits": 2, "nextMoveId": "inferno" },
    { "id": "inferno",  "kind": "multi",  "damageMin": 2, "damageMax": 2, "hits": 6, "nextMoveId": "sear" }
  ]
}
```

- [ ] **Step 5: Build & commit**

Run: `dotnet build src/Core`
Expected: PASS

```bash
git add src/Core/Enemy/MoveDefinition.cs src/Core/Enemy/EnemyDefinition.cs src/Core/Enemy/EnemyJsonLoader.cs src/Core/Data/Enemies/
git commit -m "refactor(core-enemy): migrate EnemyDefinition to state-machine moves"
```

---

## Task 5: EnemyJsonLoader テスト更新

**Files:**
- Modify: `tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs`（存在するなら更新、無ければ新規）

- [ ] **Step 1: Update or create tests**

既存テスト（`moveset` を assert しているもの）を **新スキーマ** の期待値に置き換える。必須フィールド欠落／`initialMoveId` が `moves` に無い／`moves` が空、といった異常系も加える。

```csharp
using RoguelikeCardGame.Core.Enemy;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Enemy;

public class EnemyJsonLoaderTests
{
    [Fact]
    public void Parse_ValidJawWorm_ReturnsStateMachine()
    {
        const string json = """
        {
          "id": "jaw_worm", "name": "ジョウ・ワーム", "imageId": "jaw_worm",
          "hpMin": 40, "hpMax": 44, "act": 1, "tier": "Weak",
          "initialMoveId": "chomp",
          "moves": [
            { "id": "chomp", "kind": "attack", "damageMin": 11, "damageMax": 11, "hits": 1, "nextMoveId": "thrash" },
            { "id": "thrash", "kind": "multi", "damageMin": 7, "damageMax": 7, "hits": 1, "nextMoveId": "chomp" }
          ]
        }
        """;

        var def = EnemyJsonLoader.Parse(json);
        Assert.Equal("jaw_worm", def.Id);
        Assert.Equal("chomp", def.InitialMoveId);
        Assert.Equal(2, def.Moves.Count);
        Assert.Equal("thrash", def.Moves[1].Id);
    }

    [Fact]
    public void Parse_InitialMoveIdNotInMoves_Throws()
    {
        const string json = """
        { "id":"x","name":"x","imageId":"x","hpMin":1,"hpMax":1,"act":1,"tier":"Weak",
          "initialMoveId":"missing",
          "moves":[{"id":"a","kind":"attack","nextMoveId":"a"}] }
        """;
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_EmptyMoves_Throws()
    {
        const string json = """
        { "id":"x","name":"x","imageId":"x","hpMin":1,"hpMax":1,"act":1,"tier":"Weak",
          "initialMoveId":"a","moves":[] }
        """;
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~EnemyJsonLoaderTests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs
git commit -m "test(core-enemy): cover state-machine move parsing"
```

---

## Task 6: EncounterDefinition record + loader

**Files:**
- Create: `src/Core/Data/EncounterDefinition.cs`
- Create: `src/Core/Data/EncounterJsonLoader.cs`

- [ ] **Step 1: Write EncounterDefinition**

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Enemy;

namespace RoguelikeCardGame.Core.Data;

/// <summary>
/// 1 回の戦闘で同時に出現する敵 ID の組。Act / Tier（= <see cref="EnemyPool"/>）とひもづく。
/// </summary>
public sealed record EncounterDefinition(
    string Id,
    EnemyPool Pool,
    IReadOnlyList<string> EnemyIds);
```

- [ ] **Step 2: Write EncounterJsonLoader**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Enemy;

namespace RoguelikeCardGame.Core.Data;

public sealed class EncounterJsonException : Exception
{
    public EncounterJsonException(string message) : base(message) { }
    public EncounterJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class EncounterJsonLoader
{
    public static EncounterDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EncounterJsonException("encounter JSON のパース失敗", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            string id = root.GetProperty("id").GetString()!;
            int act = root.GetProperty("act").GetInt32();
            string tierStr = root.GetProperty("tier").GetString()!;
            EnemyTier tier = tierStr switch
            {
                "Weak" => EnemyTier.Weak,
                "Strong" => EnemyTier.Strong,
                "Elite" => EnemyTier.Elite,
                "Boss" => EnemyTier.Boss,
                _ => throw new EncounterJsonException($"tier \"{tierStr}\" は無効 (id={id})"),
            };

            var enemyIds = new List<string>();
            if (!root.TryGetProperty("enemyIds", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new EncounterJsonException($"enemyIds は配列必須 (id={id})");
            foreach (var e in arr.EnumerateArray())
                enemyIds.Add(e.GetString() ?? throw new EncounterJsonException($"enemyIds 要素が string でない (id={id})"));

            if (enemyIds.Count == 0)
                throw new EncounterJsonException($"enemyIds が空 (id={id})");

            return new EncounterDefinition(id, new EnemyPool(act, tier), enemyIds);
        }
    }
}
```

- [ ] **Step 3: Build & commit**

```bash
dotnet build src/Core
git add src/Core/Data/EncounterDefinition.cs src/Core/Data/EncounterJsonLoader.cs
git commit -m "feat(core-data): add EncounterDefinition and JSON loader"
```

---

## Task 7: CharacterDefinition record + loader

**Files:**
- Create: `src/Core/Data/CharacterDefinition.cs`
- Create: `src/Core/Data/CharacterJsonLoader.cs`

- [ ] **Step 1: CharacterDefinition**

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
    IReadOnlyList<string> Deck);
```

- [ ] **Step 2: CharacterJsonLoader**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Data;

public sealed class CharacterJsonException : Exception
{
    public CharacterJsonException(string message) : base(message) { }
    public CharacterJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class CharacterJsonLoader
{
    public static CharacterDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new CharacterJsonException("character JSON のパース失敗", ex); }

        using (doc)
        {
            var r = doc.RootElement;
            string id = r.GetProperty("id").GetString()!;
            string name = r.GetProperty("name").GetString()!;
            int maxHp = r.GetProperty("maxHp").GetInt32();
            int gold = r.GetProperty("startingGold").GetInt32();
            int slots = r.GetProperty("potionSlotCount").GetInt32();
            var deck = new List<string>();
            foreach (var e in r.GetProperty("deck").EnumerateArray())
                deck.Add(e.GetString()!);

            if (maxHp <= 0) throw new CharacterJsonException($"maxHp must be > 0 (id={id})");
            if (slots < 0) throw new CharacterJsonException($"potionSlotCount must be >= 0 (id={id})");
            if (deck.Count == 0) throw new CharacterJsonException($"deck must not be empty (id={id})");

            return new CharacterDefinition(id, name, maxHp, gold, slots, deck);
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
dotnet build src/Core
git add src/Core/Data/CharacterDefinition.cs src/Core/Data/CharacterJsonLoader.cs
git commit -m "feat(core-data): add CharacterDefinition and JSON loader"
```

---

## Task 8: RewardTable record + loader

**Files:**
- Create: `src/Core/Data/RewardTable.cs`
- Create: `src/Core/Data/RewardTableJsonLoader.cs`

- [ ] **Step 1: RewardTable**

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Enemy;

namespace RoguelikeCardGame.Core.Data;

public sealed record RewardTable(
    string Id,
    IReadOnlyDictionary<EnemyTier, RewardPoolEntry> Pools,
    IReadOnlyDictionary<string, NonBattleEntry> NonBattle, // "event", "treasure"
    PotionDynamicConfig PotionDynamic,
    EpicChanceConfig EpicChance,
    EnemyPoolRoutingConfig EnemyPoolRouting);

public sealed record RewardPoolEntry(
    int GoldMin,
    int GoldMax,
    int PotionBasePercent,      // Elite=100, Boss=0, その他=40
    int CommonPercent,
    int RarePercent,
    int EpicPercent);           // 合計 100 になる想定

public sealed record NonBattleEntry(int GoldMin, int GoldMax);

public sealed record PotionDynamicConfig(int InitialPercent, int Step, int Min, int Max);

public sealed record EpicChanceConfig(int InitialBonus, int PerBattleIncrement);

public sealed record EnemyPoolRoutingConfig(int WeakRowsThreshold);
```

- [ ] **Step 2: RewardTableJsonLoader**

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Enemy;

namespace RoguelikeCardGame.Core.Data;

public sealed class RewardTableJsonException : Exception
{
    public RewardTableJsonException(string message) : base(message) { }
    public RewardTableJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class RewardTableJsonLoader
{
    public static RewardTable Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new RewardTableJsonException("reward-table JSON のパース失敗", ex); }

        using (doc)
        {
            var r = doc.RootElement;
            string id = r.GetProperty("id").GetString()!;

            var pools = new Dictionary<EnemyTier, RewardPoolEntry>();
            var poolsEl = r.GetProperty("pools");
            foreach (var kv in poolsEl.EnumerateObject())
            {
                EnemyTier tier = kv.Name switch
                {
                    "weak" => EnemyTier.Weak,
                    "strong" => EnemyTier.Strong,
                    "elite" => EnemyTier.Elite,
                    "boss" => EnemyTier.Boss,
                    _ => throw new RewardTableJsonException($"pools.\"{kv.Name}\" は無効"),
                };
                var p = kv.Value;
                var gold = p.GetProperty("gold");
                int goldMin = gold[0].GetInt32();
                int goldMax = gold[1].GetInt32();
                int potBase = p.GetProperty("potionBase").GetInt32();
                var dist = p.GetProperty("rarityDist");
                int common = dist.GetProperty("common").GetInt32();
                int rare = dist.GetProperty("rare").GetInt32();
                int epic = dist.GetProperty("epic").GetInt32();
                if (common + rare + epic != 100)
                    throw new RewardTableJsonException($"rarityDist sum != 100 at pools.{kv.Name}");
                pools[tier] = new RewardPoolEntry(goldMin, goldMax, potBase, common, rare, epic);
            }

            var nonBattle = new Dictionary<string, NonBattleEntry>();
            var nbEl = r.GetProperty("nonBattle");
            foreach (var kv in nbEl.EnumerateObject())
            {
                var g = kv.Value.GetProperty("gold");
                nonBattle[kv.Name] = new NonBattleEntry(g[0].GetInt32(), g[1].GetInt32());
            }

            var pd = r.GetProperty("potionDynamic");
            var pdCfg = new PotionDynamicConfig(
                pd.GetProperty("initialPercent").GetInt32(),
                pd.GetProperty("step").GetInt32(),
                pd.GetProperty("min").GetInt32(),
                pd.GetProperty("max").GetInt32());

            var ec = r.GetProperty("epicChance");
            var ecCfg = new EpicChanceConfig(
                ec.GetProperty("initialBonus").GetInt32(),
                ec.GetProperty("perBattleIncrement").GetInt32());

            var ep = r.GetProperty("enemyPoolRouting");
            var epCfg = new EnemyPoolRoutingConfig(ep.GetProperty("weakRowsThreshold").GetInt32());

            return new RewardTable(id, pools, nonBattle, pdCfg, ecCfg, epCfg);
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
dotnet build src/Core
git add src/Core/Data/RewardTable.cs src/Core/Data/RewardTableJsonLoader.cs
git commit -m "feat(core-data): add RewardTable and JSON loader"
```

---

## Task 9: DataCatalog / EmbeddedDataLoader 拡張

**Files:**
- Modify: `src/Core/Data/DataCatalog.cs`
- Modify: `src/Core/Data/EmbeddedDataLoader.cs`

- [ ] **Step 1: Extend DataCatalog record**

`Cards`, `Relics`, `Potions`, `Enemies` の後ろに `Encounters`, `RewardTables`, `Characters` を追加。`LoadFromStrings` を拡張し、既存の引数名（`cards`, `relics`, `potions`, `enemies`）に続けて `encounters`, `rewardTables`, `characters` を並べる。

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Data;

public sealed class DataCatalogException : Exception
{
    public DataCatalogException(string message) : base(message) { }
}

public sealed record DataCatalog(
    IReadOnlyDictionary<string, CardDefinition> Cards,
    IReadOnlyDictionary<string, RelicDefinition> Relics,
    IReadOnlyDictionary<string, PotionDefinition> Potions,
    IReadOnlyDictionary<string, EnemyDefinition> Enemies,
    IReadOnlyDictionary<string, EncounterDefinition> Encounters,
    IReadOnlyDictionary<string, RewardTable> RewardTables,
    IReadOnlyDictionary<string, CharacterDefinition> Characters)
{
    public static DataCatalog LoadFromStrings(
        IEnumerable<string> cards,
        IEnumerable<string> relics,
        IEnumerable<string> potions,
        IEnumerable<string> enemies,
        IEnumerable<string> encounters,
        IEnumerable<string> rewardTables,
        IEnumerable<string> characters)
    {
        var cardMap = new Dictionary<string, CardDefinition>();
        foreach (var json in cards)
        {
            var def = CardJsonLoader.Parse(json);
            if (!cardMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"カード ID が重複: {def.Id}");
        }

        var relicMap = new Dictionary<string, RelicDefinition>();
        foreach (var json in relics)
        {
            var def = RelicJsonLoader.Parse(json);
            if (!relicMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"レリック ID が重複: {def.Id}");
        }

        var potionMap = new Dictionary<string, PotionDefinition>();
        foreach (var json in potions)
        {
            var def = PotionJsonLoader.Parse(json);
            if (!potionMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"ポーション ID が重複: {def.Id}");
        }

        var enemyMap = new Dictionary<string, EnemyDefinition>();
        foreach (var json in enemies)
        {
            var def = EnemyJsonLoader.Parse(json);
            if (!enemyMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"敵 ID が重複: {def.Id}");
        }

        var encMap = new Dictionary<string, EncounterDefinition>();
        foreach (var json in encounters)
        {
            var def = EncounterJsonLoader.Parse(json);
            if (!encMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"encounter ID が重複: {def.Id}");
            foreach (var eid in def.EnemyIds)
                if (!enemyMap.ContainsKey(eid))
                    throw new DataCatalogException(
                        $"encounter \"{def.Id}\" が参照する敵 ID \"{eid}\" が存在しません");
        }

        var rtMap = new Dictionary<string, RewardTable>();
        foreach (var json in rewardTables)
        {
            var def = RewardTableJsonLoader.Parse(json);
            if (!rtMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"reward-table ID が重複: {def.Id}");
        }

        var chMap = new Dictionary<string, CharacterDefinition>();
        foreach (var json in characters)
        {
            var def = CharacterJsonLoader.Parse(json);
            if (!chMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"character ID が重複: {def.Id}");
            foreach (var cid in def.Deck)
                if (!cardMap.ContainsKey(cid))
                    throw new DataCatalogException(
                        $"character \"{def.Id}\" のデッキが参照するカード ID \"{cid}\" が存在しません");
        }

        return new DataCatalog(cardMap, relicMap, potionMap, enemyMap, encMap, rtMap, chMap);
    }

    public bool TryGetCard(string id, [MaybeNullWhen(false)] out CardDefinition def) => Cards.TryGetValue(id, out def);
    public bool TryGetRelic(string id, [MaybeNullWhen(false)] out RelicDefinition def) => Relics.TryGetValue(id, out def);
    public bool TryGetPotion(string id, [MaybeNullWhen(false)] out PotionDefinition def) => Potions.TryGetValue(id, out def);
    public bool TryGetEnemy(string id, [MaybeNullWhen(false)] out EnemyDefinition def) => Enemies.TryGetValue(id, out def);
    public bool TryGetEncounter(string id, [MaybeNullWhen(false)] out EncounterDefinition def) => Encounters.TryGetValue(id, out def);
    public bool TryGetRewardTable(string id, [MaybeNullWhen(false)] out RewardTable def) => RewardTables.TryGetValue(id, out def);
    public bool TryGetCharacter(string id, [MaybeNullWhen(false)] out CharacterDefinition def) => Characters.TryGetValue(id, out def);
}
```

- [ ] **Step 2: Extend EmbeddedDataLoader**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RoguelikeCardGame.Core.Data;

public static class EmbeddedDataLoader
{
    private const string CardsPrefix = "RoguelikeCardGame.Core.Data.Cards.";
    private const string RelicsPrefix = "RoguelikeCardGame.Core.Data.Relics.";
    private const string PotionsPrefix = "RoguelikeCardGame.Core.Data.Potions.";
    private const string EnemiesPrefix = "RoguelikeCardGame.Core.Data.Enemies.";
    private const string EncountersPrefix = "RoguelikeCardGame.Core.Data.Encounters.";
    private const string RewardTablePrefix = "RoguelikeCardGame.Core.Data.RewardTable.";
    private const string CharactersPrefix = "RoguelikeCardGame.Core.Data.Characters.";

    public static DataCatalog LoadCatalog()
    {
        var asm = typeof(EmbeddedDataLoader).Assembly;
        return DataCatalog.LoadFromStrings(
            cards: ReadAllWithPrefix(asm, CardsPrefix),
            relics: ReadAllWithPrefix(asm, RelicsPrefix),
            potions: ReadAllWithPrefix(asm, PotionsPrefix),
            enemies: ReadAllWithPrefix(asm, EnemiesPrefix),
            encounters: ReadAllWithPrefix(asm, EncountersPrefix),
            rewardTables: ReadAllWithPrefix(asm, RewardTablePrefix),
            characters: ReadAllWithPrefix(asm, CharactersPrefix));
    }

    private static IEnumerable<string> ReadAllWithPrefix(Assembly asm, string prefix)
    {
        var names = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix) && n.EndsWith(".json"))
            .OrderBy(n => n);
        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
dotnet build src/Core
git add src/Core/Data/DataCatalog.cs src/Core/Data/EmbeddedDataLoader.cs
git commit -m "feat(core-data): extend DataCatalog with Encounters/RewardTables/Characters"
```

---

## Task 10: csproj に新 EmbeddedResource glob 追加

**Files:**
- Modify: `src/Core/Core.csproj`

- [ ] **Step 1: Add ItemGroup entries**

既存の `Data/Cards/*.json` のような `<EmbeddedResource>` の近くに以下を追加:

```xml
<ItemGroup>
  <EmbeddedResource Include="Data/Encounters/*.json" />
  <EmbeddedResource Include="Data/RewardTable/*.json" />
  <EmbeddedResource Include="Data/Characters/*.json" />
</ItemGroup>
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Core`
Expected: PASS（この時点では JSON ファイル自体がまだ無いので空 prefix として正常）

- [ ] **Step 3: Commit**

```bash
git add src/Core/Core.csproj
git commit -m "build(core): include Encounters/RewardTable/Characters as embedded resources"
```

---

## Task 11: `Data/Characters/default.json` 作成

**Files:**
- Create: `src/Core/Data/Characters/default.json`

- [ ] **Step 1: Write the JSON**

```json
{
  "id": "default",
  "name": "見習い冒険者",
  "maxHp": 80,
  "startingGold": 99,
  "potionSlotCount": 3,
  "deck": [
    "strike","strike","strike","strike","strike",
    "defend","defend","defend","defend","defend"
  ]
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Core`
Expected: PASS（既存の `strike` / `defend` カードが存在するので参照整合性 OK）

- [ ] **Step 3: Commit**

```bash
git add src/Core/Data/Characters/default.json
git commit -m "feat(core-data): add default character JSON"
```

---

## Task 12: 追加 Potion 5 種の JSON

**Files:**
- Create: `src/Core/Data/Potions/health_potion.json`
- Create: `src/Core/Data/Potions/swift_potion.json`
- Create: `src/Core/Data/Potions/energy_potion.json`
- Create: `src/Core/Data/Potions/strength_potion.json`
- Create: `src/Core/Data/Potions/poison_potion.json`

スキーマは既存 `block_potion.json` と同じ。`rarity` は Common=1, Rare=2。

- [ ] **Step 1: `health_potion.json`**

```json
{
  "id": "health_potion",
  "name": "ヘルスポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": true,
  "effects": [{ "type": "heal", "amount": 15 }]
}
```

- [ ] **Step 2: `swift_potion.json`**

```json
{
  "id": "swift_potion",
  "name": "スウィフトポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [{ "type": "drawCards", "amount": 3 }]
}
```

- [ ] **Step 3: `energy_potion.json`**

```json
{
  "id": "energy_potion",
  "name": "エナジーポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [{ "type": "gainEnergy", "amount": 2 }]
}
```

- [ ] **Step 4: `strength_potion.json`**

```json
{
  "id": "strength_potion",
  "name": "ストレングスポーション",
  "rarity": 2,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [{ "type": "gainStrength", "amount": 2 }]
}
```

- [ ] **Step 5: `poison_potion.json`**

```json
{
  "id": "poison_potion",
  "name": "ポイズンポーション",
  "rarity": 2,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [{ "type": "applyPoison", "amount": 6 }]
}
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Core`
Expected: PASS（`CardEffectParser` が未知 effect type に失敗する場合、先に parser を拡張してテストを追加する）

- [ ] **Step 7: Commit**

```bash
git add src/Core/Data/Potions/
git commit -m "feat(core-data): add 5 placeholder potions for reward pool"
```

---

## Task 13: 30 枚の reward カード JSON 追加

**Files:**
- Create: `src/Core/Data/Cards/reward_common_01.json` 〜 `reward_common_10.json`
- Create: `src/Core/Data/Cards/reward_rare_01.json` 〜 `reward_rare_10.json`
- Create: `src/Core/Data/Cards/reward_epic_01.json` 〜 `reward_epic_10.json`

Phase 5 では全てダメージ / ブロック固定値の placeholder 効果でよい（実装は Phase 6 で拡充）。`upgradedEffects` は省略可。

**作業者は最初に `src/Core/Cards/CardEffectParser.cs` を確認し、使う `type` が全てサポート済みか調べる。未サポートの type があれば、parser 拡張タスクを先に実施してから JSON を作成する。**

- [ ] **Step 1: Common 10 枚（rarity=1）**

下表のパターンで 10 ファイル作成。`name` は placeholder として自由に日本語で設定。

| id | cardType | cost | effects |
|---|---|---|---|
| reward_common_01 | Attack | 1 | damage 8 |
| reward_common_02 | Attack | 1 | damage 5 hits=2（effect 2 個） |
| reward_common_03 | Skill  | 1 | gainBlock 7 |
| reward_common_04 | Skill  | 0 | drawCards 2 |
| reward_common_05 | Attack | 2 | damage 14 |
| reward_common_06 | Skill  | 1 | gainBlock 5 + gainBlock 3 |
| reward_common_07 | Attack | 1 | damage 6 + gainBlock 3 |
| reward_common_08 | Skill  | 0 | gainBlock 3 |
| reward_common_09 | Attack | 0 | damage 4 |
| reward_common_10 | Skill  | 1 | gainEnergy 1 |

例: `reward_common_01.json`

```json
{
  "id": "reward_common_01",
  "name": "鋭い一撃",
  "rarity": 1,
  "cardType": "Attack",
  "cost": 1,
  "effects": [{ "type": "damage", "amount": 8 }]
}
```

例（複数 effect）: `reward_common_07.json`

```json
{
  "id": "reward_common_07",
  "name": "防御撃",
  "rarity": 1,
  "cardType": "Attack",
  "cost": 1,
  "effects": [
    { "type": "damage", "amount": 6 },
    { "type": "gainBlock", "amount": 3 }
  ]
}
```

- [ ] **Step 2: Rare 10 枚（rarity=2、STS Uncommon 相当）**

| id | cardType | cost | effects |
|---|---|---|---|
| reward_rare_01 | Attack | 2 | damage 18 |
| reward_rare_02 | Skill  | 1 | gainBlock 12 |
| reward_rare_03 | Skill  | 0 | drawCards 3 |
| reward_rare_04 | Attack | 1 | damage 5 hits=3 |
| reward_rare_05 | Skill  | 1 | gainEnergy 2 |
| reward_rare_06 | Attack | 1 | damage 9 + drawCards 1 |
| reward_rare_07 | Skill  | 0 | gainBlock 5 + drawCards 1 |
| reward_rare_08 | Attack | 2 | damage 8 hits=2 |
| reward_rare_09 | Power  | 1 | gainStrength 2 |
| reward_rare_10 | Skill  | 2 | gainBlock 20 |

- [ ] **Step 3: Epic 10 枚（rarity=3、STS Rare 相当）**

| id | cardType | cost | effects |
|---|---|---|---|
| reward_epic_01 | Attack | 3 | damage 32 |
| reward_epic_02 | Power  | 2 | gainStrength 3 |
| reward_epic_03 | Skill  | 0 | drawCards 5 |
| reward_epic_04 | Attack | 2 | damage 12 hits=3 |
| reward_epic_05 | Skill  | 1 | gainBlock 18 |
| reward_epic_06 | Power  | 2 | gainDexterity 3 |
| reward_epic_07 | Attack | 1 | damage 14 |
| reward_epic_08 | Skill  | 2 | gainEnergy 3 |
| reward_epic_09 | Attack | 2 | damage 10 + gainBlock 10 |
| reward_epic_10 | Skill  | 1 | drawCards 2 + gainBlock 8 |

- [ ] **Step 4: Build**

Run: `dotnet build src/Core`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Data/Cards/reward_*.json
git commit -m "feat(core-data): add 30 placeholder reward cards (common/rare/epic)"
```

---

## Task 14: 追加敵 JSON（Weak / Strong / Elite / Boss）

**Files:** (new enemies)
- Weak (+5): `slime_acid_s`, `slime_spike_s`, `dark_cultist`, `cave_bat_a`, `cave_bat_b`
- Strong (+11): `blue_orc`, `red_orc`, `goblin_a`, `goblin_b`, `goblin_c`, `big_slime`, `dire_wolf`, `mushroom_a`, `mushroom_b`, `bandit`, `ogre`
- Elite (+4): `sleeping_dragon`, `iron_golem_a`, `iron_golem_b`, `iron_golem_c`
- Boss (+2): `slime_king`, `guardian_golem`

各敵は最低 2〜3 の move を持ち、state-machine が循環するように `nextMoveId` を設定する。数値は STS A0 を参考（参考値で良い）。

- [ ] **Step 1: Weak 5 体の例**

`src/Core/Data/Enemies/slime_acid_s.json`:

```json
{
  "id": "slime_acid_s",
  "name": "アシッド・スライム",
  "imageId": "slime_green_s",
  "hpMin": 8, "hpMax": 12,
  "act": 1, "tier": "Weak",
  "initialMoveId": "tackle",
  "moves": [
    { "id": "tackle", "kind": "attack", "damageMin": 3, "damageMax": 3, "hits": 1, "nextMoveId": "lick" },
    { "id": "lick",   "kind": "debuff", "buff": "weak", "amountMin": 1, "amountMax": 1, "nextMoveId": "tackle" }
  ]
}
```

他 4 体（`slime_spike_s`, `dark_cultist`, `cave_bat_a`, `cave_bat_b`）も同じ形式で 2〜3 move を割り当てる。

- [ ] **Step 2: Strong 11 体の例**

`src/Core/Data/Enemies/red_orc.json`:

```json
{
  "id": "red_orc",
  "name": "レッド・オーク",
  "imageId": "orc_red",
  "hpMin": 40, "hpMax": 44,
  "act": 1, "tier": "Strong",
  "initialMoveId": "smash",
  "moves": [
    { "id": "smash", "kind": "attack", "damageMin": 10, "damageMax": 10, "hits": 1, "nextMoveId": "guard" },
    { "id": "guard", "kind": "block",  "blockMin": 8, "blockMax": 8, "nextMoveId": "rage" },
    { "id": "rage",  "kind": "buff",   "buff": "strength", "amountMin": 2, "amountMax": 2, "nextMoveId": "smash" }
  ]
}
```

- [ ] **Step 3: Elite 4 体の例**

`src/Core/Data/Enemies/sleeping_dragon.json`:

```json
{
  "id": "sleeping_dragon",
  "name": "眠れる竜",
  "imageId": "dragon_sleep",
  "hpMin": 68, "hpMax": 72,
  "act": 1, "tier": "Elite",
  "initialMoveId": "wake",
  "moves": [
    { "id": "wake", "kind": "buff", "buff": "strength", "amountMin": 3, "amountMax": 3, "nextMoveId": "fire_breath" },
    { "id": "fire_breath", "kind": "attack", "damageMin": 18, "damageMax": 18, "hits": 1, "nextMoveId": "tail_swipe" },
    { "id": "tail_swipe", "kind": "attack", "damageMin": 8, "damageMax": 8, "hits": 2, "nextMoveId": "fire_breath" }
  ]
}
```

- [ ] **Step 4: Boss 2 体の例**

`src/Core/Data/Enemies/slime_king.json`:

```json
{
  "id": "slime_king",
  "name": "キング・スライム",
  "imageId": "slime_king",
  "hpMin": 140, "hpMax": 140,
  "act": 1, "tier": "Boss",
  "initialMoveId": "roar",
  "moves": [
    { "id": "roar",  "kind": "buff", "buff": "enrage", "amountMin": 5, "amountMax": 5, "nextMoveId": "slam" },
    { "id": "slam",  "kind": "attack", "damageMin": 35, "damageMax": 35, "hits": 1, "nextMoveId": "split" },
    { "id": "split", "kind": "buff", "buff": "split", "amountMin": 1, "amountMax": 1, "nextMoveId": "slam" }
  ]
}
```

- [ ] **Step 5: Build & commit**

```bash
dotnet build src/Core
git add src/Core/Data/Enemies/
git commit -m "feat(core-data): add Act1 enemies (weak/strong/elite/boss) with state-machine moves"
```

---

## Task 15: Encounter JSON 追加

**Files:**
- Create: `src/Core/Data/Encounters/enc_w_*.json` (Weak, 5)
- Create: `src/Core/Data/Encounters/enc_s_*.json` (Strong, 10)
- Create: `src/Core/Data/Encounters/enc_e_*.json` (Elite, 3)
- Create: `src/Core/Data/Encounters/enc_b_*.json` (Boss, 3)

- [ ] **Step 1: Weak 5 件**

```json
{ "id": "enc_w_jaw_worm",     "act": 1, "tier": "Weak", "enemyIds": ["jaw_worm"] }
{ "id": "enc_w_louse_pair",   "act": 1, "tier": "Weak", "enemyIds": ["louse_red", "louse_red"] }
{ "id": "enc_w_dark_cultist", "act": 1, "tier": "Weak", "enemyIds": ["dark_cultist"] }
{ "id": "enc_w_cave_bats",    "act": 1, "tier": "Weak", "enemyIds": ["cave_bat_a", "cave_bat_b"] }
{ "id": "enc_w_small_slimes", "act": 1, "tier": "Weak", "enemyIds": ["slime_acid_s", "slime_spike_s"] }
```

- [ ] **Step 2: Strong 10 件**

```json
{ "id": "enc_s_goblin_gang", "act": 1, "tier": "Strong", "enemyIds": ["goblin_a","goblin_b","goblin_c"] }
{ "id": "enc_s_big_slime",   "act": 1, "tier": "Strong", "enemyIds": ["big_slime"] }
{ "id": "enc_s_slime_rush",  "act": 1, "tier": "Strong", "enemyIds": ["slime_acid_s","slime_acid_s","slime_spike_s"] }
{ "id": "enc_s_blue_orc",    "act": 1, "tier": "Strong", "enemyIds": ["blue_orc"] }
{ "id": "enc_s_red_orc",     "act": 1, "tier": "Strong", "enemyIds": ["red_orc"] }
{ "id": "enc_s_cave_bats3",  "act": 1, "tier": "Strong", "enemyIds": ["cave_bat_a","cave_bat_a","cave_bat_b"] }
{ "id": "enc_s_mushrooms",   "act": 1, "tier": "Strong", "enemyIds": ["mushroom_a","mushroom_b"] }
{ "id": "enc_s_bandit",      "act": 1, "tier": "Strong", "enemyIds": ["bandit"] }
{ "id": "enc_s_thugs",       "act": 1, "tier": "Strong", "enemyIds": ["bandit","ogre"] }
{ "id": "enc_s_wildlife",    "act": 1, "tier": "Strong", "enemyIds": ["dire_wolf","dire_wolf"] }
```

- [ ] **Step 3: Elite 3 件**

```json
{ "id": "enc_e_hobgoblin", "act": 1, "tier": "Elite", "enemyIds": ["hobgoblin"] }
{ "id": "enc_e_dragon",    "act": 1, "tier": "Elite", "enemyIds": ["sleeping_dragon"] }
{ "id": "enc_e_golems",    "act": 1, "tier": "Elite", "enemyIds": ["iron_golem_a","iron_golem_b","iron_golem_c"] }
```

- [ ] **Step 4: Boss 3 件**

```json
{ "id": "enc_b_six_ghost",  "act": 1, "tier": "Boss", "enemyIds": ["six_ghost"] }
{ "id": "enc_b_slime_king", "act": 1, "tier": "Boss", "enemyIds": ["slime_king"] }
{ "id": "enc_b_guardian",   "act": 1, "tier": "Boss", "enemyIds": ["guardian_golem"] }
```

- [ ] **Step 5: Build & commit**

```bash
dotnet build src/Core
git add src/Core/Data/Encounters/
git commit -m "feat(core-data): add Act1 encounter definitions"
```

---

## Task 16: RewardTable `act1.json`

**Files:**
- Create: `src/Core/Data/RewardTable/act1.json`

- [ ] **Step 1: Write the JSON**

```json
{
  "id": "act1",
  "pools": {
    "weak":   { "gold": [10, 20], "potionBase": 40,
                "rarityDist": { "common": 60, "rare": 37, "epic": 3 } },
    "strong": { "gold": [15, 25], "potionBase": 40,
                "rarityDist": { "common": 60, "rare": 37, "epic": 3 } },
    "elite":  { "gold": [25, 35], "potionBase": 100,
                "rarityDist": { "common": 50, "rare": 40, "epic": 10 } },
    "boss":   { "gold": [95, 105], "potionBase": 0,
                "rarityDist": { "common": 0, "rare": 40, "epic": 60 } }
  },
  "nonBattle": {
    "event":    { "gold": [10, 20] },
    "treasure": { "gold": [25, 35] }
  },
  "potionDynamic": { "initialPercent": 40, "step": 10, "min": 0, "max": 100 },
  "epicChance":    { "initialBonus": 0, "perBattleIncrement": 1 },
  "enemyPoolRouting": { "weakRowsThreshold": 3 }
}
```

- [ ] **Step 2: Commit**

```bash
dotnet build src/Core
git add src/Core/Data/RewardTable/act1.json
git commit -m "feat(core-data): add act1 reward table"
```

---

## Task 17: DataCatalog Phase 5 統合テスト

**Files:**
- Create: `tests/Core.Tests/Data/DataCatalogPhase5Tests.cs`

- [ ] **Step 1: Write tests**

```csharp
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class DataCatalogPhase5Tests
{
    private static DataCatalog Load() => EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void Loads_DefaultCharacter()
    {
        var cat = Load();
        Assert.True(cat.TryGetCharacter("default", out var ch));
        Assert.Equal(80, ch!.MaxHp);
        Assert.Equal(99, ch.StartingGold);
        Assert.Equal(3, ch.PotionSlotCount);
        Assert.Equal(10, ch.Deck.Count);
    }

    [Fact]
    public void Loads_Act1RewardTable()
    {
        var cat = Load();
        Assert.True(cat.TryGetRewardTable("act1", out var rt));
        Assert.Equal(100, rt!.Pools[EnemyTier.Elite].PotionBasePercent);
        Assert.Equal(0, rt.Pools[EnemyTier.Boss].PotionBasePercent);
        Assert.Equal(3, rt.EnemyPoolRouting.WeakRowsThreshold);
    }

    [Fact]
    public void Encounters_AllReferencedEnemiesExist()
    {
        var cat = Load();
        Assert.NotEmpty(cat.Encounters);
        foreach (var enc in cat.Encounters.Values)
            foreach (var eid in enc.EnemyIds)
                Assert.True(cat.Enemies.ContainsKey(eid),
                    $"encounter {enc.Id} references missing enemy {eid}");
    }

    [Fact]
    public void Encounters_CoverAllFourTiers()
    {
        var cat = Load();
        Assert.Contains(cat.Encounters.Values, e => e.Pool.Tier == EnemyTier.Weak);
        Assert.Contains(cat.Encounters.Values, e => e.Pool.Tier == EnemyTier.Strong);
        Assert.Contains(cat.Encounters.Values, e => e.Pool.Tier == EnemyTier.Elite);
        Assert.Contains(cat.Encounters.Values, e => e.Pool.Tier == EnemyTier.Boss);
    }

    [Fact]
    public void RewardCards_Exist_ForAllThreeRarities()
    {
        var cat = Load();
        int common = cat.Cards.Values.Count(c => c.Id.StartsWith("reward_common_"));
        int rare   = cat.Cards.Values.Count(c => c.Id.StartsWith("reward_rare_"));
        int epic   = cat.Cards.Values.Count(c => c.Id.StartsWith("reward_epic_"));
        Assert.Equal(10, common);
        Assert.Equal(10, rare);
        Assert.Equal(10, epic);
    }

    [Fact]
    public void EnemyDefinitions_HaveInitialMoveInMoves()
    {
        var cat = Load();
        foreach (var e in cat.Enemies.Values)
            Assert.Contains(e.Moves, m => m.Id == e.InitialMoveId);
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~DataCatalogPhase5Tests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add tests/Core.Tests/Data/DataCatalogPhase5Tests.cs
git commit -m "test(core-data): cover Phase 5 catalog integrity"
```

---

## Part B — Core サービス層

## Task 18: Battle / Reward state 用 record 型

**Files:**
- Create: `src/Core/Battle/BattleState.cs`
- Create: `src/Core/Rewards/RewardState.cs`

- [ ] **Step 1: `BattleState.cs`**

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle;

public enum BattleOutcome { Pending, Victory }

public sealed record BattleState(
    string EncounterId,
    ImmutableArray<EnemyInstance> Enemies,
    BattleOutcome Outcome);

public sealed record EnemyInstance(
    string EnemyDefinitionId,
    int CurrentHp,
    int MaxHp,
    string CurrentMoveId);
```

- [ ] **Step 2: `RewardState.cs`**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Enemy;

namespace RoguelikeCardGame.Core.Rewards;

public enum CardRewardStatus { Pending, Claimed, Skipped }

public enum NonBattleRewardKind { Event, Treasure }

public abstract record RewardContext
{
    public sealed record FromEnemy(EnemyPool Pool) : RewardContext;
    public sealed record FromNonBattle(NonBattleRewardKind Kind) : RewardContext;
}

public sealed record RewardState(
    int Gold,
    bool GoldClaimed,
    string? PotionId,
    bool PotionClaimed,
    ImmutableArray<string> CardChoices,
    CardRewardStatus CardStatus);

public sealed record RewardRngState(
    int PotionChancePercent,
    int RareChanceBonusPercent);
```

- [ ] **Step 3: Build & commit**

```bash
dotnet build src/Core
git add src/Core/Battle/BattleState.cs src/Core/Rewards/RewardState.cs
git commit -m "feat(core): add BattleState and RewardState records"
```

---

## Task 19: EncounterQueue テスト（failing）

**Files:**
- Create: `tests/Core.Tests/Battle/EncounterQueueTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle;

public class EncounterQueueTests
{
    private static DataCatalog Cat() => EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void Initialize_ContainsAllEncountersOfThatPool()
    {
        var cat = Cat();
        var pool = new EnemyPool(1, EnemyTier.Weak);
        var q = EncounterQueue.Initialize(pool, cat, new SystemRng(42));
        var expected = cat.Encounters.Values.Where(e => e.Pool == pool).Select(e => e.Id).OrderBy(s => s).ToArray();
        var actual = q.OrderBy(s => s).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Initialize_SameSeed_SameOrder()
    {
        var cat = Cat();
        var pool = new EnemyPool(1, EnemyTier.Strong);
        var a = EncounterQueue.Initialize(pool, cat, new SystemRng(7));
        var b = EncounterQueue.Initialize(pool, cat, new SystemRng(7));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Draw_RotatesHeadToTail()
    {
        var q = ImmutableArray.Create("a", "b", "c");
        var (id, next) = EncounterQueue.Draw(q);
        Assert.Equal("a", id);
        Assert.Equal(ImmutableArray.Create("b", "c", "a"), next);
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~EncounterQueueTests`
Expected: FAIL（型が存在しないため）

---

## Task 20: EncounterQueue 実装

**Files:**
- Create: `src/Core/Battle/EncounterQueue.cs`

- [ ] **Step 1: Implement**

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle;

/// <summary>
/// プール単位の encounter 非重複キュー。Run 開始時にシャッフルして格納し、
/// Draw で先頭を取りつつ末尾に push することで「一巡するまで同じ encounter が再出現しない」。
/// </summary>
public static class EncounterQueue
{
    public static ImmutableArray<string> Initialize(EnemyPool pool, DataCatalog data, IRng rng)
    {
        var ids = data.Encounters.Values.Where(e => e.Pool == pool).Select(e => e.Id).ToList();
        // Fisher-Yates
        for (int i = ids.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }
        return ids.ToImmutableArray();
    }

    public static (string encounterId, ImmutableArray<string> newQueue) Draw(ImmutableArray<string> queue)
    {
        if (queue.IsEmpty) throw new System.InvalidOperationException("encounter queue is empty");
        var head = queue[0];
        var rest = queue.RemoveAt(0).Add(head);
        return (head, rest);
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~EncounterQueueTests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Core/Battle/EncounterQueue.cs tests/Core.Tests/Battle/EncounterQueueTests.cs
git commit -m "feat(core-battle): add EncounterQueue for non-repeating encounter rotation"
```

---

## Task 21: BattlePlaceholder テスト + 実装

**Files:**
- Create: `tests/Core.Tests/Battle/BattlePlaceholderTests.cs`
- Create: `src/Core/Battle/BattlePlaceholder.cs`

**前提:** `RunState` v3 はまだ導入されていない（Task 27 で対応）。このタスクでは `BattlePlaceholder.Start` / `Win` は `RunState` の新フィールドを使うため、**Task 27 完了後** に着手する（またはこのタスクでは record 型が欠けている状態で **コンパイルエラーで failing する** のを受け入れて、Task 27 で解消する構成にしてもよい）。

**サブエージェントへの指示:** 実装順序として、(A) Task 27 (RunState v3) を先に終わらせてからこのタスクに戻る、または (B) このタスクで `BattlePlaceholder` のみ `RunState` 非依存の API（`BattleState` を入出力とする関数）にして、RunState への反映はサービス層に委譲する、のどちらかを選ぶ。**デフォルトは (A)**（RunState v3 を先に実装、その後このタスクに戻る）。

- [ ] **Step 1: テスト（RunState v3 前提）**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle;

public class BattlePlaceholderTests
{
    [Fact]
    public void Start_SetsActiveBattleWithEnemiesAndPendingOutcome()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var state = TestRunStates.FreshDefault(cat);  // Task 27 で追加する test helper
        var pool = new EnemyPool(1, EnemyTier.Weak);
        state = state with { EncounterQueueWeak = EncounterQueue.Initialize(pool, cat, new SystemRng(1)) };

        var next = BattlePlaceholder.Start(state, pool, cat, new SystemRng(1));

        Assert.NotNull(next.ActiveBattle);
        Assert.Equal(BattleOutcome.Pending, next.ActiveBattle!.Outcome);
        Assert.NotEmpty(next.ActiveBattle.Enemies);
        foreach (var e in next.ActiveBattle.Enemies)
        {
            var def = cat.Enemies[e.EnemyDefinitionId];
            Assert.InRange(e.CurrentHp, def.HpMin, def.HpMax);
            Assert.Equal(e.CurrentHp, e.MaxHp);
            Assert.Equal(def.InitialMoveId, e.CurrentMoveId);
        }
        // Queue rotated (head → tail)
        Assert.NotEqual(state.EncounterQueueWeak, next.EncounterQueueWeak);
    }

    [Fact]
    public void Win_SetsOutcomeToVictory()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var state = TestRunStates.FreshDefault(cat) with
        {
            ActiveBattle = new BattleState("enc_w_jaw_worm",
                ImmutableArray.Create(new EnemyInstance("jaw_worm", 42, 42, "chomp")),
                BattleOutcome.Pending)
        };

        var next = BattlePlaceholder.Win(state);
        Assert.Equal(BattleOutcome.Victory, next.ActiveBattle!.Outcome);
    }
}
```

- [ ] **Step 2: 実装**

```csharp
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle;

public static class BattlePlaceholder
{
    /// <summary>
    /// 指定プールの encounter キューから 1 件 draw して ActiveBattle をセット。
    /// 敵の HP は hpMin..hpMax の一様乱数で確定、CurrentMoveId は InitialMoveId。
    /// </summary>
    public static RunState Start(RunState state, EnemyPool pool, DataCatalog data, IRng rng)
    {
        if (state.ActiveBattle is not null)
            throw new InvalidOperationException("ActiveBattle already present");
        if (state.ActiveReward is not null)
            throw new InvalidOperationException("ActiveReward already present");

        var (queueBefore, selector) = SelectQueue(state, pool);
        var (encounterId, queueAfter) = EncounterQueue.Draw(queueBefore);
        var encounter = data.Encounters[encounterId];

        var enemies = ImmutableArray.CreateBuilder<EnemyInstance>(encounter.EnemyIds.Count);
        foreach (var eid in encounter.EnemyIds)
        {
            var def = data.Enemies[eid];
            int hp = def.HpMin + rng.NextInt(def.HpMax - def.HpMin + 1);
            enemies.Add(new EnemyInstance(eid, hp, hp, def.InitialMoveId));
        }
        var battle = new BattleState(encounterId, enemies.ToImmutable(), BattleOutcome.Pending);
        return selector(state, queueAfter) with { ActiveBattle = battle };
    }

    public static RunState Win(RunState state)
    {
        if (state.ActiveBattle is null)
            throw new InvalidOperationException("No ActiveBattle to win");
        return state with
        {
            ActiveBattle = state.ActiveBattle with { Outcome = BattleOutcome.Victory }
        };
    }

    private static (ImmutableArray<string> queue, Func<RunState, ImmutableArray<string>, RunState> updater)
        SelectQueue(RunState s, EnemyPool pool) => pool.Tier switch
    {
        EnemyTier.Weak   => (s.EncounterQueueWeak,   (st, q) => st with { EncounterQueueWeak = q }),
        EnemyTier.Strong => (s.EncounterQueueStrong, (st, q) => st with { EncounterQueueStrong = q }),
        EnemyTier.Elite  => (s.EncounterQueueElite,  (st, q) => st with { EncounterQueueElite = q }),
        EnemyTier.Boss   => (s.EncounterQueueBoss,   (st, q) => st with { EncounterQueueBoss = q }),
        _ => throw new ArgumentOutOfRangeException(nameof(pool))
    };
}
```

- [ ] **Step 3: Run & commit**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BattlePlaceholderTests`
Expected: PASS

```bash
git add src/Core/Battle/BattlePlaceholder.cs tests/Core.Tests/Battle/BattlePlaceholderTests.cs
git commit -m "feat(core-battle): add BattlePlaceholder Start/Win"
```

---

## Task 22: RewardGenerator テスト（failing）

**Files:**
- Create: `tests/Core.Tests/Rewards/RewardGeneratorTests.cs`

- [ ] **Step 1: Write tests**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardGeneratorTests
{
    private static readonly ImmutableArray<string> StarterExclusions =
        ImmutableArray.Create("strike", "defend");

    private static DataCatalog Cat() => EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void Generate_FromWeakPool_GoldInRange()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));
        var (reward, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
            StarterExclusions, rt, cat, new SystemRng(1));
        Assert.InRange(reward.Gold, rt.Pools[EnemyTier.Weak].GoldMin, rt.Pools[EnemyTier.Weak].GoldMax);
    }

    [Fact]
    public void Generate_EliteAlwaysHasPotion()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Elite));
        for (int seed = 0; seed < 20; seed++)
        {
            var (r, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, cat, new SystemRng(seed));
            Assert.NotNull(r.PotionId);
        }
    }

    [Fact]
    public void Generate_BossNeverHasPotion()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Boss));
        for (int seed = 0; seed < 20; seed++)
        {
            var (r, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, cat, new SystemRng(seed));
            Assert.Null(r.PotionId);
        }
    }

    [Fact]
    public void Generate_CardChoicesHaveNoStarterCards()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));
        for (int seed = 0; seed < 10; seed++)
        {
            var (r, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, cat, new SystemRng(seed));
            Assert.Equal(3, r.CardChoices.Length);
            Assert.DoesNotContain("strike", r.CardChoices);
            Assert.DoesNotContain("defend", r.CardChoices);
            Assert.Equal(3, r.CardChoices.Distinct().Count());
        }
    }

    [Fact]
    public void Generate_NonBattle_NoCardChoices()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromNonBattle(NonBattleRewardKind.Event);
        var (r, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
            StarterExclusions, rt, cat, new SystemRng(1));
        Assert.Empty(r.CardChoices);
        Assert.Equal(CardRewardStatus.Claimed, r.CardStatus);
    }

    [Fact]
    public void Generate_PotionDynamicChance_DecreasesOnDrop_IncreasesOnMiss()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));

        // force potion drop using chance=100
        var (_, next1) = RewardGenerator.Generate(ctx, new RewardRngState(100, 0),
            StarterExclusions, rt, cat, new SystemRng(0));
        Assert.Equal(90, next1.PotionChancePercent);

        // force miss using chance=0
        var (_, next2) = RewardGenerator.Generate(ctx, new RewardRngState(0, 0),
            StarterExclusions, rt, cat, new SystemRng(0));
        Assert.Equal(10, next2.PotionChancePercent);
    }

    [Fact]
    public void Generate_RareBonus_ResetsOnRare_IncrementsOnMiss()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));

        bool sawAny = false;
        for (int seed = 0; seed < 50; seed++)
        {
            var (r, next) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, cat, new SystemRng(seed));
            bool hasRare = r.CardChoices.Any(id => cat.Cards[id].Rarity == Cards.CardRarity.Rare);
            if (hasRare) { Assert.Equal(0, next.RareChanceBonusPercent); sawAny = true; }
            else         { Assert.Equal(1, next.RareChanceBonusPercent); }
        }
        Assert.True(sawAny, "at least one seed should produce a Rare");
    }
}
```

- [ ] **Step 2: Run**

Expected: FAIL（`RewardGenerator` 未定義）

---

## Task 23: RewardGenerator 実装

**Files:**
- Create: `src/Core/Rewards/RewardGenerator.cs`

- [ ] **Step 1: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Rewards;

public static class RewardGenerator
{
    public static (RewardState reward, RewardRngState newRng) Generate(
        RewardContext context,
        RewardRngState rngState,
        ImmutableArray<string> cardExclusions,
        RewardTable table,
        DataCatalog data,
        IRng rng)
    {
        return context switch
        {
            RewardContext.FromEnemy fe => GenerateFromEnemy(fe.Pool, rngState, cardExclusions, table, data, rng),
            RewardContext.FromNonBattle nb => GenerateFromNonBattle(nb.Kind, rngState, table, rng),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    private static (RewardState, RewardRngState) GenerateFromEnemy(
        EnemyPool pool, RewardRngState rngState,
        ImmutableArray<string> excl, RewardTable table, DataCatalog data, IRng rng)
    {
        var entry = table.Pools[pool.Tier];

        // Gold
        int gold = entry.GoldMin + rng.NextInt(entry.GoldMax - entry.GoldMin + 1);

        // Potion
        string? potionId = null;
        var newRng = rngState;
        int potionBase = entry.PotionBasePercent;
        if (potionBase == 100)
        {
            potionId = PickRandomPotion(data, rng);
        }
        else if (potionBase == 0)
        {
            // Boss: never drop, do not touch dynamic chance
        }
        else
        {
            int chance = rngState.PotionChancePercent;
            if (rng.NextInt(100) < chance)
            {
                potionId = PickRandomPotion(data, rng);
                newRng = newRng with
                {
                    PotionChancePercent = Math.Max(table.PotionDynamic.Min,
                        chance - table.PotionDynamic.Step)
                };
            }
            else
            {
                newRng = newRng with
                {
                    PotionChancePercent = Math.Min(table.PotionDynamic.Max,
                        chance + table.PotionDynamic.Step)
                };
            }
        }

        // Cards
        int commonPct = entry.CommonPercent;
        int rarePct = entry.RarePercent;
        int epicPct = entry.EpicPercent;
        int bonus = rngState.RareChanceBonusPercent;

        // Apply bonus as increment to rare, proportionally subtracting from common/uncommon
        int rareFinal = Math.Min(100, rarePct + bonus);
        int take = rareFinal - rarePct;
        int commonFinal = Math.Max(0, commonPct - take);
        int epicFinal = Math.Max(0, 100 - rareFinal - commonFinal);

        var picks = new List<string>();
        var seen = new HashSet<string>();
        while (picks.Count < 3)
        {
            var r = rng.NextInt(100);
            CardRarity rarity;
            if (r < commonFinal) rarity = CardRarity.Common;
            else if (r < commonFinal + rareFinal) rarity = CardRarity.Rare;
            else rarity = CardRarity.Epic;

            var pool2 = data.Cards.Values
                .Where(c => c.Rarity == rarity && c.Id.StartsWith("reward_"))
                .Where(c => !excl.Contains(c.Id) && !seen.Contains(c.Id))
                .Select(c => c.Id)
                .ToList();
            if (pool2.Count == 0) continue;
            var pick = pool2[rng.NextInt(pool2.Count)];
            picks.Add(pick);
            seen.Add(pick);
        }

        bool hasRare = picks.Any(id => data.Cards[id].Rarity == CardRarity.Rare);
        newRng = newRng with
        {
            RareChanceBonusPercent = hasRare ? 0 : rngState.RareChanceBonusPercent + table.EpicChance.PerBattleIncrement
        };

        var reward = new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: potionId, PotionClaimed: potionId is null,
            CardChoices: picks.ToImmutableArray(),
            CardStatus: CardRewardStatus.Pending);
        return (reward, newRng);
    }

    private static (RewardState, RewardRngState) GenerateFromNonBattle(
        NonBattleRewardKind kind, RewardRngState rngState, RewardTable table, IRng rng)
    {
        string key = kind == NonBattleRewardKind.Event ? "event" : "treasure";
        var entry = table.NonBattle[key];
        int gold = entry.GoldMin + rng.NextInt(entry.GoldMax - entry.GoldMin + 1);
        var reward = new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed);
        return (reward, rngState);
    }

    private static string PickRandomPotion(DataCatalog data, IRng rng)
    {
        var ids = data.Potions.Keys.OrderBy(s => s).ToArray();
        return ids[rng.NextInt(ids.Length)];
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RewardGeneratorTests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/Core/Rewards/RewardGenerator.cs tests/Core.Tests/Rewards/RewardGeneratorTests.cs
git commit -m "feat(core-rewards): add RewardGenerator with dynamic potion/rare chance"
```

---

## Task 24: RewardApplier テスト + 実装

**Files:**
- Create: `tests/Core.Tests/Rewards/RewardApplierTests.cs`
- Create: `src/Core/Rewards/RewardApplier.cs`

- [ ] **Step 1: テスト**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardApplierTests
{
    private static RunState StateWithReward(RewardState r)
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        return TestRunStates.FreshDefault(cat) with { ActiveReward = r };
    }

    [Fact]
    public void ApplyGold_AddsGoldAndMarksClaimed()
    {
        var s = StateWithReward(new RewardState(15, false, null, true,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed));
        var next = RewardApplier.ApplyGold(s);
        Assert.Equal(s.Gold + 15, next.Gold);
        Assert.True(next.ActiveReward!.GoldClaimed);
    }

    [Fact]
    public void ApplyPotion_FullSlots_Throws()
    {
        var s = StateWithReward(new RewardState(0, true, "health_potion", false,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed));
        s = s with { Potions = ImmutableArray.Create("a","b","c") };
        Assert.Throws<System.InvalidOperationException>(() => RewardApplier.ApplyPotion(s));
    }

    [Fact]
    public void ApplyPotion_EmptySlot_Receives()
    {
        var s = StateWithReward(new RewardState(0, true, "health_potion", false,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed));
        s = s with { Potions = ImmutableArray.Create("a","","") };
        var next = RewardApplier.ApplyPotion(s);
        Assert.Equal("health_potion", next.Potions[1]);
        Assert.True(next.ActiveReward!.PotionClaimed);
    }

    [Fact]
    public void PickCard_AddsToDeckAndMarksClaimed()
    {
        var choices = ImmutableArray.Create("reward_common_01","reward_common_02","reward_common_03");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Pending));
        var next = RewardApplier.PickCard(s, "reward_common_02");
        Assert.Contains("reward_common_02", next.Deck);
        Assert.Equal(CardRewardStatus.Claimed, next.ActiveReward!.CardStatus);
    }

    [Fact]
    public void PickCard_UnknownChoice_Throws()
    {
        var choices = ImmutableArray.Create("reward_common_01");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Pending));
        Assert.Throws<System.ArgumentException>(() => RewardApplier.PickCard(s, "reward_common_99"));
    }

    [Fact]
    public void Proceed_AllComplete_ClearsActiveReward()
    {
        var s = StateWithReward(new RewardState(0, true, null, true,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed));
        var next = RewardApplier.Proceed(s);
        Assert.Null(next.ActiveReward);
    }

    [Fact]
    public void Proceed_IncompleteCard_Throws()
    {
        var choices = ImmutableArray.Create("reward_common_01","reward_common_02","reward_common_03");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Pending));
        Assert.Throws<System.InvalidOperationException>(() => RewardApplier.Proceed(s));
    }

    [Fact]
    public void DiscardPotion_EmptySlot_Throws()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        { Potions = ImmutableArray.Create("health_potion","","") };
        Assert.Throws<System.ArgumentException>(() => RewardApplier.DiscardPotion(s, 1));
    }

    [Fact]
    public void DiscardPotion_OccupiedSlot_Empties()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        { Potions = ImmutableArray.Create("health_potion","swift_potion","") };
        var next = RewardApplier.DiscardPotion(s, 0);
        Assert.Equal("", next.Potions[0]);
    }
}
```

- [ ] **Step 2: 実装**

```csharp
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Rewards;

public static class RewardApplier
{
    public static RunState ApplyGold(RunState s)
    {
        var r = Require(s);
        if (r.GoldClaimed) throw new InvalidOperationException("Gold already claimed");
        return s with
        {
            Gold = s.Gold + r.Gold,
            ActiveReward = r with { GoldClaimed = true },
        };
    }

    public static RunState ApplyPotion(RunState s)
    {
        var r = Require(s);
        if (r.PotionClaimed) throw new InvalidOperationException("Potion already claimed");
        if (r.PotionId is null) throw new InvalidOperationException("No potion to claim");

        int idx = -1;
        for (int i = 0; i < s.Potions.Length; i++) if (s.Potions[i] == "") { idx = i; break; }
        if (idx < 0) throw new InvalidOperationException("All potion slots are full");

        var newPotions = s.Potions.SetItem(idx, r.PotionId);
        return s with
        {
            Potions = newPotions,
            ActiveReward = r with { PotionClaimed = true },
        };
    }

    public static RunState PickCard(RunState s, string cardId)
    {
        var r = Require(s);
        if (r.CardStatus != CardRewardStatus.Pending)
            throw new InvalidOperationException("Card already resolved");
        if (!r.CardChoices.Contains(cardId))
            throw new ArgumentException($"cardId \"{cardId}\" is not in CardChoices", nameof(cardId));

        return s with
        {
            Deck = ImmutableArray.CreateRange(s.Deck).Add(cardId),
            ActiveReward = r with { CardStatus = CardRewardStatus.Claimed },
        };
    }

    public static RunState SkipCard(RunState s)
    {
        var r = Require(s);
        if (r.CardStatus != CardRewardStatus.Pending)
            throw new InvalidOperationException("Card already resolved");
        return s with { ActiveReward = r with { CardStatus = CardRewardStatus.Skipped } };
    }

    public static RunState Proceed(RunState s)
    {
        var r = Require(s);
        if (!r.GoldClaimed) throw new InvalidOperationException("Gold not claimed");
        if (r.PotionId is not null && !r.PotionClaimed) throw new InvalidOperationException("Potion not claimed");
        if (r.CardStatus == CardRewardStatus.Pending) throw new InvalidOperationException("Card not resolved");
        return s with { ActiveReward = null };
    }

    public static RunState DiscardPotion(RunState s, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= s.Potions.Length)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (s.Potions[slotIndex] == "")
            throw new ArgumentException("Slot is already empty", nameof(slotIndex));
        return s with { Potions = s.Potions.SetItem(slotIndex, "") };
    }

    private static RewardState Require(RunState s)
        => s.ActiveReward ?? throw new InvalidOperationException("No ActiveReward");
}
```

- [ ] **Step 3: Run & commit**

```bash
dotnet test tests/Core.Tests --filter FullyQualifiedName~RewardApplierTests
git add src/Core/Rewards/RewardApplier.cs tests/Core.Tests/Rewards/RewardApplierTests.cs
git commit -m "feat(core-rewards): add RewardApplier for gold/potion/card/proceed/discard"
```

---

## Task 25: NodeEffectResolver テスト + 実装

**Files:**
- Create: `tests/Core.Tests/Run/NodeEffectResolverTests.cs`
- Create: `src/Core/Run/NodeEffectResolver.cs`

- [ ] **Step 1: テスト**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class NodeEffectResolverTests
{
    [Fact]
    public void Resolve_Enemy_WeakRow_StartsBattleWithWeakPool()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        // simulate "weak row" (row < weakRowsThreshold)
        var next = NodeEffectResolver.Resolve(s, TileKind.Enemy, currentRow: 2, cat, new SystemRng(1));
        Assert.NotNull(next.ActiveBattle);
    }

    [Fact]
    public void Resolve_Rest_FullyHealsHp()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentHp = 5 };
        var next = NodeEffectResolver.Resolve(s, TileKind.Rest, currentRow: 5, cat, new SystemRng(1));
        Assert.Equal(s.MaxHp, next.CurrentHp);
        Assert.Null(next.ActiveBattle);
        Assert.Null(next.ActiveReward);
    }

    [Fact]
    public void Resolve_Treasure_CreatesActiveReward_NoCards()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Treasure, currentRow: 5, cat, new SystemRng(1));
        Assert.NotNull(next.ActiveReward);
        Assert.Empty(next.ActiveReward!.CardChoices);
    }

    [Fact]
    public void Resolve_Shop_DoesNothing()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Merchant, currentRow: 5, cat, new SystemRng(1));
        Assert.Null(next.ActiveBattle);
        Assert.Null(next.ActiveReward);
        Assert.Equal(s.CurrentHp, next.CurrentHp);
    }

    [Fact]
    public void Resolve_Boss_StartsBattleWithBossPool()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Boss, currentRow: 15, cat, new SystemRng(1));
        Assert.NotNull(next.ActiveBattle);
    }
}
```

- [ ] **Step 2: 実装**

```csharp
using System;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

/// <summary>
/// NodeKind に応じて RunState を遷移させる。戦闘マスは BattlePlaceholder.Start を呼び、
/// Event/Treasure は RewardGenerator で ActiveReward を立て、Rest は HP 全回復、
/// Shop/Start は副作用なし。
/// </summary>
public static class NodeEffectResolver
{
    public static RunState Resolve(
        RunState state,
        TileKind kind,
        int currentRow,
        DataCatalog data,
        IRng rng)
    {
        var table = data.RewardTables["act1"];
        return kind switch
        {
            TileKind.Start => state,
            TileKind.Enemy => state.Mutated(BattlePlaceholder.Start(state,
                RouteEnemyPool(table, state.CurrentAct, currentRow), data, rng)),
            TileKind.Elite => BattlePlaceholder.Start(state,
                new EnemyPool(state.CurrentAct, EnemyTier.Elite), data, rng),
            TileKind.Boss => BattlePlaceholder.Start(state,
                new EnemyPool(state.CurrentAct, EnemyTier.Boss), data, rng),
            TileKind.Rest => state with { CurrentHp = state.MaxHp },
            TileKind.Merchant => state,
            TileKind.Treasure => ApplyNonBattleReward(state, NonBattleRewardKind.Treasure, table, data, rng),
            // NOTE: Unknown はマップ生成時に解決済みなので、ここには来ない想定。
            TileKind.Unknown => throw new ArgumentException("Unknown tile should be pre-resolved"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>Enemy マスの tier を row で振り分ける（Q6 A: row ベース）。</summary>
    private static EnemyPool RouteEnemyPool(RewardTable table, int act, int row)
    {
        var tier = row < table.EnemyPoolRouting.WeakRowsThreshold
            ? EnemyTier.Weak
            : EnemyTier.Strong;
        return new EnemyPool(act, tier);
    }

    private static RunState ApplyNonBattleReward(RunState s, NonBattleRewardKind kind,
        RewardTable table, DataCatalog data, IRng rng)
    {
        var (reward, newRng) = RewardGenerator.Generate(
            new RewardContext.FromNonBattle(kind),
            s.RewardRngState,
            System.Collections.Immutable.ImmutableArray.Create("strike", "defend"),
            table, data, rng);
        return s with { ActiveReward = reward, RewardRngState = newRng };
    }

    // Mutated は BattlePlaceholder.Start がすでに RunState を返すため使わないが、
    // switch expression の型合わせ用の identity ヘルパ。実際には BattlePlaceholder.Start の結果を直接返す。
    private static RunState Mutated(this RunState _, RunState next) => next;
}
```

- [ ] **Step 3: Run & commit**

```bash
dotnet test tests/Core.Tests --filter FullyQualifiedName~NodeEffectResolverTests
git add src/Core/Run/NodeEffectResolver.cs tests/Core.Tests/Run/NodeEffectResolverTests.cs
git commit -m "feat(core-run): add NodeEffectResolver to dispatch TileKind to battle or reward"
```

---

## Part C — RunState v3 migration

## Task 26: RunState v3 スキーマ + StarterDeck 削除

**Files:**
- Modify: `src/Core/Run/RunState.cs`
- Delete: `src/Core/Player/StarterDeck.cs`
- Create: `tests/Core.Tests/TestHelpers/TestRunStates.cs`

- [ ] **Step 1: RunState v3**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    ImmutableArray<int> VisitedNodeIds,
    ImmutableDictionary<int, TileKind> UnknownResolutions,

    // --- Phase 5 additions ---
    string CharacterId,
    int CurrentHp,
    int MaxHp,
    int Gold,
    ImmutableArray<string> Deck,
    ImmutableArray<string> Potions,
    int PotionSlotCount,
    BattleState? ActiveBattle,
    RewardState? ActiveReward,
    ImmutableArray<string> EncounterQueueWeak,
    ImmutableArray<string> EncounterQueueStrong,
    ImmutableArray<string> EncounterQueueElite,
    ImmutableArray<string> EncounterQueueBoss,
    RewardRngState RewardRngState,

    // --- existing ---
    IReadOnlyList<string> Relics,   // Phase 5 では未使用、Phase 7 で拡張
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress)
{
    public const int CurrentSchemaVersion = 3;

    public static RunState NewSoloRun(
        DataCatalog catalog,
        ulong rngSeed,
        int startNodeId,
        ImmutableDictionary<int, TileKind> unknownResolutions,
        ImmutableArray<string> encounterQueueWeak,
        ImmutableArray<string> encounterQueueStrong,
        ImmutableArray<string> encounterQueueElite,
        ImmutableArray<string> encounterQueueBoss,
        DateTimeOffset nowUtc,
        string characterId = "default")
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(unknownResolutions);

        if (!catalog.TryGetCharacter(characterId, out var ch))
            throw new InvalidOperationException($"Character \"{characterId}\" が DataCatalog に存在しません");

        foreach (var id in ch.Deck)
            if (!catalog.TryGetCard(id, out _))
                throw new InvalidOperationException(
                    $"Character \"{characterId}\" のデッキが参照するカード ID \"{id}\" が存在しません");

        var potions = ImmutableArray.CreateRange(new string[ch.PotionSlotCount]).WithFill("");
        var rt = catalog.RewardTables["act1"];

        return new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentNodeId: startNodeId,
            VisitedNodeIds: ImmutableArray.Create(startNodeId),
            UnknownResolutions: unknownResolutions,
            CharacterId: characterId,
            CurrentHp: ch.MaxHp,
            MaxHp: ch.MaxHp,
            Gold: ch.StartingGold,
            Deck: ImmutableArray.CreateRange(ch.Deck),
            Potions: potions,
            PotionSlotCount: ch.PotionSlotCount,
            ActiveBattle: null,
            ActiveReward: null,
            EncounterQueueWeak: encounterQueueWeak,
            EncounterQueueStrong: encounterQueueStrong,
            EncounterQueueElite: encounterQueueElite,
            EncounterQueueBoss: encounterQueueBoss,
            RewardRngState: new RewardRngState(
                rt.PotionDynamic.InitialPercent, rt.EpicChance.InitialBonus),
            Relics: Array.Empty<string>(),
            PlaySeconds: 0L,
            RngSeed: rngSeed,
            SavedAtUtc: nowUtc,
            Progress: RunProgress.InProgress);
    }

    public string? Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            return $"SchemaVersion must be {CurrentSchemaVersion} (got {SchemaVersion})";
        if (VisitedNodeIds.IsDefault) return "VisitedNodeIds must not be default";
        if (!VisitedNodeIds.Contains(CurrentNodeId))
            return $"VisitedNodeIds must contain CurrentNodeId ({CurrentNodeId})";
        if (Potions.Length != PotionSlotCount)
            return $"Potions.Length ({Potions.Length}) != PotionSlotCount ({PotionSlotCount})";
        if (ActiveBattle is not null && ActiveReward is not null)
            return "ActiveBattle and ActiveReward must not both be non-null";
        if (ActiveReward is { CardChoices: var cc } && cc.Length != 0 && cc.Length != 3)
            return $"CardChoices must have length 0 or 3 (got {cc.Length})";
        return null;
    }
}

// helper extension to fill an immutable array with a default value
file static class ImmutableArrayExtensions
{
    public static ImmutableArray<string> WithFill(this ImmutableArray<string> arr, string value)
    {
        var builder = ImmutableArray.CreateBuilder<string>(arr.Length);
        for (int i = 0; i < arr.Length; i++) builder.Add(value);
        return builder.ToImmutable();
    }
}
```

- [ ] **Step 2: Delete StarterDeck.cs**

```bash
git rm src/Core/Player/StarterDeck.cs
```

`src/Core/Player/` 以下の他ファイルが参照している箇所を検索し、全て `DataCatalog.Characters["default"].Deck` に置換する。

- [ ] **Step 3: TestRunStates helper**

`tests/Core.Tests/TestHelpers/TestRunStates.cs`:

```csharp
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Tests;

public static class TestRunStates
{
    public static RunState FreshDefault(DataCatalog cat)
    {
        return RunState.NewSoloRun(
            cat,
            rngSeed: 0,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: DateTimeOffset.UnixEpoch);
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: FAIL — 既存の `RunState` を使っているテスト / サービス（`RunStartService`, `RunActions` 等）がシグネチャ変更を受けてコンパイルエラー。次タスクで修正する。

- [ ] **Step 5: Commit the record + helper, NOT build-green yet**

```bash
git add src/Core/Run/RunState.cs tests/Core.Tests/TestHelpers/TestRunStates.cs
git rm src/Core/Player/StarterDeck.cs
git commit -m "feat(core-run): bump RunState to v3 schema with battle/reward/encounter fields"
```

---

## Task 27: 既存 RunStateSerializer / RunActions / MapGeneration テスト群を v3 に更新

**Files:**
- Modify: `src/Core/Run/RunStateSerializer.cs`（もし hand-written serializer の場合）
- Modify: `tests/Core.Tests/Run/RunStateSerializerTests.cs`
- Modify: `src/Core/Run/RunActions.cs`（RunState の record フィールド追加により直接は影響しない想定だが確認）
- Modify: 影響を受ける全ての test で `NewSoloRun` の呼び出しを新シグネチャに合わせる

- [ ] **Step 1: Serializer**

`System.Text.Json` に任せている場合は record 拡張で自動対応。hand-written の場合は新フィールド全てを扱うよう拡張し、`SchemaVersion != 3` なら `null` を返す（既存パターン）。

- [ ] **Step 2: 既存テスト更新**

`git grep -l "NewSoloRun" tests/` で該当箇所を洗い出し、新シグネチャに合わせる。既存テストが `CurrentHp/MaxHp/Gold/Deck` など v2 でも使っていたフィールドをそのまま assert している場合は、`cat.Characters["default"]` から期待値を引く形に変更。

- [ ] **Step 3: Build & run all tests**

```bash
dotnet build
dotnet test
```

Expected: 全 PASS（Phase 4 のテストを含む）

- [ ] **Step 4: Commit**

```bash
git add -u src/Core/ tests/Core.Tests/
git commit -m "refactor(core): align existing tests and services with RunState v3"
```

---

## Part D — Server 層

## Task 28: RunStartService 拡張

**Files:**
- Modify: `src/Server/Services/RunStartService.cs`

- [ ] **Step 1: Update StartAsync**

`EmbeddedDataLoader.LoadCatalog()` の結果を既存通り取得し、4 プール分の `EncounterQueue.Initialize` を呼び、`RunState.NewSoloRun` の新シグネチャに渡す。`EncounterQueue.Initialize` に使う RNG は `unchecked(seed + 2)`, `+3`, `+4`, `+5` のように分ける（map 生成は `seed`、unknown 解決は `seed+1`、encounter 4 プールで `+2..+5`）。

```csharp
var catalog = EmbeddedDataLoader.LoadCatalog();
var qWeak   = EncounterQueue.Initialize(new EnemyPool(1, EnemyTier.Weak),   catalog, new SystemRng(unchecked(seed + 2)));
var qStrong = EncounterQueue.Initialize(new EnemyPool(1, EnemyTier.Strong), catalog, new SystemRng(unchecked(seed + 3)));
var qElite  = EncounterQueue.Initialize(new EnemyPool(1, EnemyTier.Elite),  catalog, new SystemRng(unchecked(seed + 4)));
var qBoss   = EncounterQueue.Initialize(new EnemyPool(1, EnemyTier.Boss),   catalog, new SystemRng(unchecked(seed + 5)));

var state = RunState.NewSoloRun(
    catalog,
    rngSeed: unchecked((ulong)(uint)seed),
    startNodeId: map.StartNodeId,
    unknownResolutions: resolutions,
    encounterQueueWeak: qWeak,
    encounterQueueStrong: qStrong,
    encounterQueueElite: qElite,
    encounterQueueBoss: qBoss,
    nowUtc: _now());
```

- [ ] **Step 2: Build & existing tests**

```bash
dotnet build
dotnet test tests/Server.Tests
```

Expected: 既存 Phase 4 テスト全 PASS（RunStartService の新シグネチャに合わせた RunSnapshotDto の後方互換を Task 31 で検証）

- [ ] **Step 3: Commit**

```bash
git add src/Server/Services/RunStartService.cs
git commit -m "feat(server): initialize encounter queues and Phase 5 RunState fields on run start"
```

---

## Task 29: DTOs 追加（BattleStateDto / RewardStateDto / request bodies）

**Files:**
- Create: `src/Server/Dtos/BattleStateDto.cs`
- Create: `src/Server/Dtos/RewardStateDto.cs`
- Create: `src/Server/Dtos/BattleWinRequestDto.cs`
- Create: `src/Server/Dtos/RewardCardRequestDto.cs`
- Create: `src/Server/Dtos/RewardProceedRequestDto.cs`
- Create: `src/Server/Dtos/PotionDiscardRequestDto.cs`
- Modify: `src/Server/Dtos/RunSnapshotDto.cs`

- [ ] **Step 1: BattleStateDto.cs**

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleStateDto(
    string EncounterId,
    IReadOnlyList<EnemyInstanceDto> Enemies,
    string Outcome);

public sealed record EnemyInstanceDto(
    string EnemyDefinitionId,
    string Name,
    string ImageId,
    int CurrentHp,
    int MaxHp,
    string CurrentMoveId);
```

- [ ] **Step 2: RewardStateDto.cs**

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RewardStateDto(
    int Gold, bool GoldClaimed,
    string? PotionId, bool PotionClaimed,
    IReadOnlyList<string> CardChoices,
    string CardStatus);
```

- [ ] **Step 3: Request DTOs**

```csharp
// BattleWinRequestDto.cs
namespace RoguelikeCardGame.Server.Dtos;
public sealed record BattleWinRequestDto(long ElapsedSeconds);

// RewardCardRequestDto.cs
public sealed record RewardCardRequestDto(string? CardId, bool? Skip);

// RewardProceedRequestDto.cs
public sealed record RewardProceedRequestDto(long ElapsedSeconds);

// PotionDiscardRequestDto.cs
public sealed record PotionDiscardRequestDto(int SlotIndex);
```

- [ ] **Step 4: Extend RunSnapshotDto**

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Rewards;
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
            s.CharacterId, s.CurrentHp, s.MaxHp, s.Gold, s.Deck, s.Potions, s.PotionSlotCount,
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
```

- [ ] **Step 5: Commit**

```bash
dotnet build
git add src/Server/Dtos/
git commit -m "feat(server-dtos): add BattleStateDto/RewardStateDto and extend RunSnapshotDto"
```

---

## Task 30: RunsController — move 内で NodeEffectResolver を呼ぶ

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`

- [ ] **Step 1: Inject DataCatalog**

`DataCatalog` を DI から受けるようにコンストラクタへ追加。Program.cs で `services.AddSingleton(EmbeddedDataLoader.LoadCatalog())` を登録（未登録なら）。

- [ ] **Step 2: Extend PostMove**

```csharp
[HttpPost("current/move")]
public async Task<IActionResult> PostMove([FromBody] MoveRequestDto body, CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;
    if (body is null) return BadRequest();
    if (!await _accounts.ExistsAsync(accountId, ct))
        return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

    var state = await _saves.TryLoadAsync(accountId, ct);
    if (state is null || state.Progress != RunProgress.InProgress)
        return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
    if (state.ActiveBattle is not null || state.ActiveReward is not null)
        return Problem(statusCode: StatusCodes.Status409Conflict,
            title: "戦闘中または報酬未受取のため移動できません。");

    var map = _runStart.RehydrateMap(state.RngSeed);
    RunState advanced;
    try { advanced = RunActions.SelectNextNode(state, map, body.NodeId); }
    catch (ArgumentException ex)
    {
        return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
    }

    var node = map.GetNode(body.NodeId);
    var actualKind = advanced.UnknownResolutions.TryGetValue(node.Id, out var resolved) ? resolved : node.Kind;

    var effectRng = new SystemRng(unchecked((int)advanced.RngSeed ^ (node.Id * 31) ^ (int)advanced.PlaySeconds));
    advanced = NodeEffectResolver.Resolve(advanced, actualKind, node.Row, _data, effectRng);

    long elapsed = Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
    advanced = advanced with
    {
        PlaySeconds = advanced.PlaySeconds + elapsed,
        SavedAtUtc = DateTimeOffset.UtcNow,
    };
    await _saves.SaveAsync(accountId, advanced, ct);
    return NoContent();
}
```

- [ ] **Step 3: GetCurrent を DTO mapper 経由にする**

```csharp
return Ok(RunSnapshotDtoMapper.From(state, map, _data));
```

- [ ] **Step 4: Build & existing tests**

```bash
dotnet build
dotnet test tests/Server.Tests --filter FullyQualifiedName~RunsControllerTests
```

既存の `PostMove_*` が Weak row の Enemy に移動した場合 ActiveBattle が立つため、**既存テストのアサーション** を state ミスマッチで再移動 409 になるのを受け入れる形に変更する必要がある。あるいは Phase 4 テストが Start → 次の非 Enemy マスに移動するパターンなら既存通り PASS。影響するテストを手動で確認して修正する。

- [ ] **Step 5: Commit**

```bash
git add src/Server/Controllers/RunsController.cs src/Server/Program.cs
git commit -m "feat(server): invoke NodeEffectResolver on move and expose battle/reward state in snapshot"
```

---

## Task 31: battle/win エンドポイント + テスト

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`
- Create: `tests/Server.Tests/Controllers/BattleEndpointsTests.cs`

- [ ] **Step 1: エンドポイント**

```csharp
[HttpPost("current/battle/win")]
public async Task<IActionResult> PostBattleWin([FromBody] BattleWinRequestDto body, CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;
    if (!await _accounts.ExistsAsync(accountId, ct))
        return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

    var s = await _saves.TryLoadAsync(accountId, ct);
    if (s is null || s.Progress != RunProgress.InProgress || s.ActiveBattle is null)
        return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中の戦闘がありません。");

    var afterWin = BattlePlaceholder.Win(s);
    var pool = GetPoolFromEncounter(afterWin.ActiveBattle!.EncounterId);
    var rewardRng = new SystemRng(unchecked((int)s.RngSeed ^ (int)s.PlaySeconds ^ 0x5EED));
    var (reward, newRng) = RewardGenerator.Generate(
        new RewardContext.FromEnemy(pool),
        afterWin.RewardRngState,
        ImmutableArray.Create("strike", "defend"),
        _data.RewardTables["act1"], _data, rewardRng);

    long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
    var updated = afterWin with
    {
        ActiveBattle = null,
        ActiveReward = reward,
        RewardRngState = newRng,
        PlaySeconds = afterWin.PlaySeconds + elapsed,
        SavedAtUtc = DateTimeOffset.UtcNow,
    };
    await _saves.SaveAsync(accountId, updated, ct);
    return NoContent();
}

private EnemyPool GetPoolFromEncounter(string encounterId)
    => _data.Encounters[encounterId].Pool;
```

- [ ] **Step 2: テスト**

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class BattleEndpointsTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public BattleEndpointsTests(TempDataFactory f) => _factory = f;

    [Fact]
    public async Task PostBattleWin_NoActiveBattle_Returns409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, "alice");
        BattleTestHelpers.WithAccount(client, "alice");
        await client.PostAsync("/api/v1/runs/new", null);

        var res = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task PostBattleWin_WithActiveBattle_SetsActiveReward()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await BattleTestHelpers.EnsureAccountAsync(client, "bob");
        BattleTestHelpers.WithAccount(client, "bob");
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(client);

        var res = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win", new { elapsedSeconds = 1 });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var cur = await client.GetAsync("/api/v1/runs/current");
        var doc = JsonDocument.Parse(await cur.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("run").GetProperty("activeBattle").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, doc.RootElement.GetProperty("run").GetProperty("activeReward").ValueKind);
    }
}
```

`BattleTestHelpers` は各テストで共有する helper ファイル（`tests/Server.Tests/Controllers/BattleTestHelpers.cs`）として切り出し、`EnsureAccountAsync` / `WithAccount` / `StartRunAndMoveToEnemyAsync` を実装する。`StartRunAndMoveToEnemyAsync` は `POST /runs/new` → `GET /runs/current` → マップから start の outgoing のうち `kind=Enemy` のノードを探して `POST /runs/current/move` するロジックを書く。

- [ ] **Step 3: Run & commit**

```bash
dotnet test tests/Server.Tests --filter FullyQualifiedName~BattleEndpointsTests
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/
git commit -m "feat(server): add battle/win endpoint and tests"
```

---

## Task 32: reward/gold + reward/potion エンドポイント + テスト

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`
- Create: `tests/Server.Tests/Controllers/RewardEndpointsTests.cs`

- [ ] **Step 1: エンドポイント**

```csharp
[HttpPost("current/reward/gold")]
public async Task<IActionResult> PostRewardGold(CancellationToken ct)
    => await ApplyReward(s => RewardApplier.ApplyGold(s), ct);

[HttpPost("current/reward/potion")]
public async Task<IActionResult> PostRewardPotion(CancellationToken ct)
    => await ApplyReward(s => RewardApplier.ApplyPotion(s), ct);

private async Task<IActionResult> ApplyReward(Func<RunState, RunState> action, CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;
    if (!await _accounts.ExistsAsync(accountId, ct))
        return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

    var s = await _saves.TryLoadAsync(accountId, ct);
    if (s is null || s.Progress != RunProgress.InProgress || s.ActiveReward is null)
        return Problem(statusCode: StatusCodes.Status409Conflict, title: "報酬画面がありません。");

    RunState updated;
    try { updated = action(s); }
    catch (InvalidOperationException ex)
    { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message); }

    updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
    await _saves.SaveAsync(accountId, updated, ct);
    return NoContent();
}
```

- [ ] **Step 2: テスト**

```csharp
public class RewardEndpointsTests : IClassFixture<TempDataFactory>
{
    // setup identical to BattleEndpointsTests

    [Fact]
    public async Task RewardGold_Claims()
    {
        // start run → move to Enemy → battle/win → reward/gold 204 → GET snapshot shows goldClaimed=true
    }

    [Fact]
    public async Task RewardGold_Twice_Returns409()
    {
        // After first claim, second returns 409.
    }

    [Fact]
    public async Task RewardPotion_FullSlots_Returns409()
    {
        // Pre-seed potions full, then trigger a reward with potion, expect 409.
    }

    [Fact]
    public async Task RewardPotion_WithEmptySlot_Claims()
    {
        // Normal flow.
    }
}
```

- [ ] **Step 3: Run & commit**

```bash
dotnet test tests/Server.Tests --filter FullyQualifiedName~RewardEndpointsTests
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/RewardEndpointsTests.cs
git commit -m "feat(server): add reward/gold and reward/potion endpoints with tests"
```

---

## Task 33: reward/card + reward/proceed エンドポイント + テスト

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`
- Modify: `tests/Server.Tests/Controllers/RewardEndpointsTests.cs`

- [ ] **Step 1: reward/card**

```csharp
[HttpPost("current/reward/card")]
public async Task<IActionResult> PostRewardCard([FromBody] RewardCardRequestDto body, CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;
    if (body is null) return BadRequest();
    bool hasCard = !string.IsNullOrEmpty(body.CardId);
    bool skip = body.Skip == true;
    if (hasCard == skip)
        return Problem(statusCode: StatusCodes.Status400BadRequest,
            title: "cardId と skip のうち片方のみ指定してください。");

    if (!await _accounts.ExistsAsync(accountId, ct))
        return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

    var s = await _saves.TryLoadAsync(accountId, ct);
    if (s is null || s.Progress != RunProgress.InProgress || s.ActiveReward is null)
        return Problem(statusCode: StatusCodes.Status409Conflict, title: "報酬画面がありません。");

    RunState updated;
    try
    {
        updated = skip ? RewardApplier.SkipCard(s) : RewardApplier.PickCard(s, body.CardId!);
    }
    catch (ArgumentException ex)
    { return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message); }
    catch (InvalidOperationException ex)
    { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message); }

    updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
    await _saves.SaveAsync(accountId, updated, ct);
    return NoContent();
}
```

- [ ] **Step 2: reward/proceed**

```csharp
[HttpPost("current/reward/proceed")]
public async Task<IActionResult> PostRewardProceed([FromBody] RewardProceedRequestDto body, CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;
    if (!await _accounts.ExistsAsync(accountId, ct))
        return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

    var s = await _saves.TryLoadAsync(accountId, ct);
    if (s is null || s.Progress != RunProgress.InProgress || s.ActiveReward is null)
        return Problem(statusCode: StatusCodes.Status409Conflict, title: "報酬画面がありません。");

    RunState updated;
    try { updated = RewardApplier.Proceed(s); }
    catch (InvalidOperationException ex)
    { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message); }

    long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
    updated = updated with
    {
        PlaySeconds = updated.PlaySeconds + elapsed,
        SavedAtUtc = DateTimeOffset.UtcNow,
    };
    await _saves.SaveAsync(accountId, updated, ct);
    return NoContent();
}
```

- [ ] **Step 3: テスト追加**

- `RewardCard_Pick_AddsToDeck_AndBlocksRepeat`（2 回目 409）
- `RewardCard_Skip_MarksSkipped`
- `RewardCard_BothCardIdAndSkip_Returns400`
- `RewardCard_UnknownCardId_Returns400`
- `RewardProceed_IncompleteRewards_Returns409`
- `RewardProceed_AllDone_ClearsReward_AndAllowsNextMove`

- [ ] **Step 4: Run & commit**

```bash
dotnet test tests/Server.Tests --filter FullyQualifiedName~RewardEndpointsTests
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/RewardEndpointsTests.cs
git commit -m "feat(server): add reward/card and reward/proceed endpoints with tests"
```

---

## Task 34: potion/discard エンドポイント + テスト

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`
- Create: `tests/Server.Tests/Controllers/PotionDiscardTests.cs`

- [ ] **Step 1: エンドポイント**

```csharp
[HttpPost("current/potion/discard")]
public async Task<IActionResult> PostPotionDiscard([FromBody] PotionDiscardRequestDto body, CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;
    if (body is null) return BadRequest();
    if (!await _accounts.ExistsAsync(accountId, ct))
        return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

    var s = await _saves.TryLoadAsync(accountId, ct);
    if (s is null || s.Progress != RunProgress.InProgress)
        return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");

    RunState updated;
    try { updated = RewardApplier.DiscardPotion(s, body.SlotIndex); }
    catch (ArgumentException ex)
    { return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message); }

    updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
    await _saves.SaveAsync(accountId, updated, ct);
    return NoContent();
}
```

- [ ] **Step 2: テスト**

- `PotionDiscard_EmptySlot_Returns400`
- `PotionDiscard_OutOfRange_Returns400`
- `PotionDiscard_OccupiedSlot_Returns204_AndSlotBecomesEmpty`
- `PotionDiscard_ThenReceivePotion_Succeeds`（満杯 → discard → 受取可能）

`PotionDiscard_ThenReceivePotion_Succeeds` は、実装のしやすさのために：ポーション枠を手動で満杯に埋める helper（`ApiTestFixture` に `PreseedPotionsAsync(accountId, potions)` みたいなテスト専用の path があると良い。もしくは、test helper として `_saves` に直接書き込む）を用意する。

- [ ] **Step 3: Run & commit**

```bash
dotnet test tests/Server.Tests --filter FullyQualifiedName~PotionDiscardTests
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/PotionDiscardTests.cs
git commit -m "feat(server): add potion/discard endpoint with tests"
```

---

## Task 35: 非戦闘 Move テスト（NonBattleMoveTests）

**Files:**
- Create: `tests/Server.Tests/Controllers/NonBattleMoveTests.cs`

- [ ] **Step 1: Write tests**

各 NodeKind について、move 成功後の `GET /current` の結果を assert する:

- `Rest_Move_FullyHeals`: 事前に `CurrentHp` を下げて Rest へ move すると `CurrentHp == MaxHp`
- `Treasure_Move_CreatesActiveReward_WithoutCards`
- `Event_Move_CreatesActiveReward_WithoutCards`（Unknown→Event に解決した map を条件付きで用意する）
- `Merchant_Move_DoesNothing`

マップから特定 kind のノードを探すには、`GET /current` の `unknownResolutions` および map から該当 kind の outgoing ノードを探す。Unknown から解決された kind も `UnknownResolutions` で表現されているので、そちら優先で読む。

- [ ] **Step 2: Run & commit**

```bash
dotnet test tests/Server.Tests --filter FullyQualifiedName~NonBattleMoveTests
git add tests/Server.Tests/Controllers/NonBattleMoveTests.cs
git commit -m "test(server): verify non-battle move effects (Rest/Treasure/Event/Merchant)"
```

---

## Part E — Client 層

## Task 36: types.ts を v3 に更新

**Files:**
- Modify: `src/Client/src/api/types.ts`

- [ ] **Step 1: Update RunStateDto and add new types**

```typescript
export type TileKind =
  | 'Start' | 'Enemy' | 'Elite' | 'Rest' | 'Merchant'
  | 'Treasure' | 'Unknown' | 'Boss'

export type RunProgress = 'InProgress' | 'Cleared' | 'GameOver' | 'Abandoned'

export type BattleOutcome = 'Pending' | 'Victory'
export type CardRewardStatus = 'Pending' | 'Claimed' | 'Skipped'

export type EnemyInstanceDto = {
  enemyDefinitionId: string
  name: string
  imageId: string
  currentHp: number
  maxHp: number
  currentMoveId: string
}

export type BattleStateDto = {
  encounterId: string
  enemies: EnemyInstanceDto[]
  outcome: BattleOutcome
}

export type RewardStateDto = {
  gold: number
  goldClaimed: boolean
  potionId: string | null
  potionClaimed: boolean
  cardChoices: string[]
  cardStatus: CardRewardStatus
}

export type RunStateDto = {
  schemaVersion: number
  currentAct: number
  currentNodeId: number
  visitedNodeIds: number[]
  unknownResolutions: Record<number, TileKind>
  characterId: string
  currentHp: number
  maxHp: number
  gold: number
  deck: string[]
  potions: string[]
  potionSlotCount: number
  activeBattle: BattleStateDto | null
  activeReward: RewardStateDto | null
  relics: string[]
  playSeconds: number
  progress: RunProgress
  savedAtUtc: string
}

export type MapNodeDto = {
  id: number; row: number; column: number
  kind: TileKind; outgoingNodeIds: number[]
}
export type MapDto = { startNodeId: number; bossNodeId: number; nodes: MapNodeDto[] }
export type RunSnapshotDto = { run: RunStateDto; map: MapDto }
```

- [ ] **Step 2: Build**

Run: `cd src/Client && npm run build`
Expected: 既存 `MapScreen` 等で tile kind を扱うコードは、型差分に引っ張られる変更のみ。TypeScript コンパイルが通る状態にする（次タスクで UI 更新）。

- [ ] **Step 3: Commit**

```bash
git add src/Client/src/api/types.ts
git commit -m "feat(client-types): add BattleStateDto/RewardStateDto and Phase 5 RunStateDto fields"
```

---

## Task 37: battle.ts / rewards.ts API 関数

**Files:**
- Create: `src/Client/src/api/battle.ts`
- Create: `src/Client/src/api/rewards.ts`

- [ ] **Step 1: battle.ts**

```typescript
import { apiRequest } from './client'

export async function winBattle(accountId: string, elapsedSeconds: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/battle/win', {
    accountId, body: { elapsedSeconds },
  })
}
```

- [ ] **Step 2: rewards.ts**

```typescript
import { apiRequest } from './client'

export async function claimGold(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/gold', { accountId })
}

export async function claimPotion(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/potion', { accountId })
}

export async function pickCard(accountId: string, cardId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/card', {
    accountId, body: { cardId },
  })
}

export async function skipCard(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/card', {
    accountId, body: { skip: true },
  })
}

export async function proceedReward(accountId: string, elapsedSeconds: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/proceed', {
    accountId, body: { elapsedSeconds },
  })
}

export async function discardPotion(accountId: string, slotIndex: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/potion/discard', {
    accountId, body: { slotIndex },
  })
}
```

- [ ] **Step 3: Build & commit**

```bash
cd src/Client && npm run build && cd -
git add src/Client/src/api/battle.ts src/Client/src/api/rewards.ts
git commit -m "feat(client-api): add battle and reward API client functions"
```

---

## Task 38: TopBar + PotionSlot コンポーネント

**Files:**
- Create: `src/Client/src/components/TopBar.tsx`
- Create: `src/Client/src/components/PotionSlot.tsx`

- [ ] **Step 1: TopBar.tsx**

```tsx
import { PotionSlot } from './PotionSlot'

type Props = {
  currentHp: number
  maxHp: number
  gold: number
  potions: string[]
  onDiscardPotion: (slotIndex: number) => void
}

export function TopBar({ currentHp, maxHp, gold, potions, onDiscardPotion }: Props) {
  return (
    <div className="topbar" role="status">
      <span className="topbar__hp">HP {currentHp}/{maxHp}</span>
      <span className="topbar__gold">Gold {gold}</span>
      <div className="topbar__potions">
        {potions.map((id, i) => (
          <PotionSlot key={i} slotIndex={i} potionId={id}
            onDiscard={() => onDiscardPotion(i)} />
        ))}
      </div>
    </div>
  )
}
```

- [ ] **Step 2: PotionSlot.tsx**

```tsx
import { useState } from 'react'

type Props = {
  slotIndex: number
  potionId: string
  onDiscard: () => void
}

export function PotionSlot({ slotIndex, potionId, onDiscard }: Props) {
  const [menuOpen, setMenuOpen] = useState(false)
  const filled = potionId !== ''

  if (!filled) return <div className="potion-slot potion-slot--empty" aria-label={`スロット ${slotIndex + 1} (空)`} />

  return (
    <div className="potion-slot" aria-label={`スロット ${slotIndex + 1}: ${potionId}`}>
      <button className="potion-slot__icon" onClick={() => setMenuOpen(v => !v)}>🧪</button>
      {menuOpen && (
        <div className="potion-slot__menu" role="menu">
          <button onClick={() => { onDiscard(); setMenuOpen(false) }}>捨てる</button>
          <button onClick={() => setMenuOpen(false)}>キャンセル</button>
        </div>
      )}
    </div>
  )
}
```

- [ ] **Step 3: Commit**

```bash
cd src/Client && npm run build && cd -
git add src/Client/src/components/TopBar.tsx src/Client/src/components/PotionSlot.tsx
git commit -m "feat(client-ui): add TopBar and PotionSlot components"
```

---

## Task 39: BattleOverlay コンポーネント + テスト

**Files:**
- Create: `src/Client/src/screens/BattleOverlay.tsx`
- Create: `src/Client/src/screens/BattleOverlay.test.tsx`

- [ ] **Step 1: BattleOverlay.tsx**

```tsx
import { useState } from 'react'
import type { BattleStateDto } from '../api/types'
import { Button } from '../components/Button'

type Props = {
  battle: BattleStateDto
  onWin: () => Promise<void> | void
}

export function BattleOverlay({ battle, onWin }: Props) {
  const [peeking, setPeeking] = useState(false)
  const [busy, setBusy] = useState(false)

  if (peeking) {
    return (
      <div className="battle-peek" onClick={() => setPeeking(false)}>
        <span>クリックで戦闘画面に戻る</span>
      </div>
    )
  }

  return (
    <div className="battle-overlay" role="dialog" aria-modal="true">
      <div className="battle-overlay__enemies">
        {battle.enemies.map((e, i) => (
          <div className="battle-enemy" key={i}>
            <div className="battle-enemy__image" data-image-id={e.imageId}>{e.imageId}</div>
            <div className="battle-enemy__name">{e.name}</div>
            <div className="battle-enemy__hp">HP {e.currentHp}/{e.maxHp}</div>
          </div>
        ))}
      </div>
      <div className="battle-overlay__actions">
        <Button disabled={busy} onClick={async () => {
          setBusy(true); try { await onWin() } finally { setBusy(false) }
        }}>勝利</Button>
        <Button onClick={() => setPeeking(true)}>マップを見る</Button>
      </div>
    </div>
  )
}
```

- [ ] **Step 2: BattleOverlay.test.tsx**

基本の render テスト:
- 敵の name / HP / imageId を表示
- 勝利ボタンクリックで `onWin` が呼ばれる
- peek ボタンで戦闘画面が隠れ、クリックで戻る

- [ ] **Step 3: Run & commit**

```bash
cd src/Client && npm run test BattleOverlay && cd -
git add src/Client/src/screens/BattleOverlay.tsx src/Client/src/screens/BattleOverlay.test.tsx
git commit -m "feat(client-ui): add BattleOverlay with placeholder victory button"
```

---

## Task 40: RewardPopup コンポーネント + テスト

**Files:**
- Create: `src/Client/src/screens/RewardPopup.tsx`
- Create: `src/Client/src/screens/RewardPopup.test.tsx`

- [ ] **Step 1: RewardPopup.tsx**

```tsx
import { useState } from 'react'
import type { RewardStateDto } from '../api/types'
import { Button } from '../components/Button'
import { PotionSlot } from '../components/PotionSlot'

type Props = {
  reward: RewardStateDto
  potions: string[]
  potionSlotCount: number
  onClaimGold: () => Promise<void>
  onClaimPotion: () => Promise<void>
  onPickCard: (cardId: string) => Promise<void>
  onSkipCard: () => Promise<void>
  onProceed: () => Promise<void>
  onDiscardPotion: (slotIndex: number) => Promise<void>
  onPotionFullAlert: () => void
}

export function RewardPopup(p: Props) {
  const [cardView, setCardView] = useState(false)
  const r = p.reward
  const canProceed = r.goldClaimed
    && (r.potionId === null || r.potionClaimed)
    && r.cardStatus !== 'Pending'

  const handleClaimPotion = async () => {
    try { await p.onClaimPotion() }
    catch (e) {
      if ((e as { status?: number }).status === 409) p.onPotionFullAlert()
      else throw e
    }
  }

  if (cardView && r.cardStatus === 'Pending') {
    return (
      <div className="reward-popup" role="dialog" aria-modal="true">
        <h2>カードを選ぶ</h2>
        <div className="reward-card-choices">
          {r.cardChoices.map(cid => (
            <Button key={cid} onClick={async () => { await p.onPickCard(cid); setCardView(false) }}>
              {cid}
            </Button>
          ))}
        </div>
        <Button onClick={async () => { await p.onSkipCard(); setCardView(false) }}>Skip</Button>
      </div>
    )
  }

  return (
    <div className="reward-popup" role="dialog" aria-modal="true">
      <h2>報酬</h2>
      <ul className="reward-list">
        <li>
          <Button disabled={r.goldClaimed} onClick={() => p.onClaimGold()}>
            {r.goldClaimed ? '✓' : '＋'} {r.gold} Gold
          </Button>
        </li>
        {r.potionId && (
          <li>
            <Button disabled={r.potionClaimed} onClick={handleClaimPotion}>
              {r.potionClaimed ? '✓' : '🧪'} {r.potionId}
            </Button>
          </li>
        )}
        {r.cardChoices.length > 0 && (
          <li>
            <Button disabled={r.cardStatus !== 'Pending'} onClick={() => setCardView(true)}>
              {r.cardStatus !== 'Pending' ? '✓' : '✨'} カードの報酬
            </Button>
          </li>
        )}
      </ul>
      <div className="reward-popup__potion-slots">
        {p.potions.map((id, i) => (
          <PotionSlot key={i} slotIndex={i} potionId={id}
            onDiscard={() => p.onDiscardPotion(i)} />
        ))}
      </div>
      <Button disabled={!canProceed} onClick={() => p.onProceed()}>進む</Button>
    </div>
  )
}
```

- [ ] **Step 2: RewardPopup.test.tsx**

- 全 claimed → Proceed 有効化、ハンドラ呼び出し
- Potion full → alert flow（onPotionFullAlert が呼ばれる）
- Card 行クリックで card view に切替、Pick で戻る

- [ ] **Step 3: Run & commit**

```bash
cd src/Client && npm run test RewardPopup && cd -
git add src/Client/src/screens/RewardPopup.tsx src/Client/src/screens/RewardPopup.test.tsx
git commit -m "feat(client-ui): add RewardPopup with gold/potion/card rows and proceed"
```

---

## Task 41: MapScreen 統合

**Files:**
- Modify: `src/Client/src/screens/MapScreen.tsx`
- Modify: `src/Client/src/screens/MapScreen.test.tsx`

- [ ] **Step 1: Integrate TopBar + BattleOverlay + RewardPopup**

`MapScreen` は:
- トップバーを常時表示
- `snapshot.run.activeBattle` が non-null のとき `BattleOverlay` を表示
- `snapshot.run.activeReward` が non-null のとき `RewardPopup` を表示
- Shop を踏んだら一時メッセージ（placeholder）を表示

`winBattle` / `claimGold` / `claimPotion` / `pickCard` / `skipCard` / `proceedReward` / `discardPotion` を呼び、各成功後に `getCurrentRun` で snapshot を refetch する helper（`refresh` 関数）を用意する。

```tsx
const refresh = useCallback(async () => {
  const next = await getCurrentRun(accountId)
  if (next) onSnapshot(next)
}, [accountId, onSnapshot])
```

`move` 成功後も必ず `refresh()` を呼ぶように既存コードを更新（`activeBattle` / `activeReward` を snapshot で取得するため）。

- [ ] **Step 2: Shop placeholder メッセージ**

move 成功後、`TileKind === 'Merchant'` の場合 React state で transient message を 2-3 秒表示する。

- [ ] **Step 3: MapScreen.test.tsx 更新**

- 戦闘中は BattleOverlay が重ねて表示される
- Reward 状態では RewardPopup が表示される
- トップバーに HP/Gold/Potion が表示される

- [ ] **Step 4: Build & manual verify**

```bash
cd src/Client && npm run build && npm run test && cd -
```

- [ ] **Step 5: Commit**

```bash
git add src/Client/src/screens/MapScreen.tsx src/Client/src/screens/MapScreen.test.tsx
git commit -m "feat(client-map): integrate TopBar/BattleOverlay/RewardPopup into MapScreen"
```

---

## Part F — 最終検証

## Task 42: 手動ブラウザ確認

Phase 5 は UI が主役なので、全自動テストだけでは不足。以下を実機確認する。

**手順:**

1. `dotnet run --project src/Server`
2. 別ターミナルで `cd src/Client && npm run dev`
3. ブラウザで http://localhost:5173 を開く

**確認項目（チェックリスト）:**

- [ ] 新規ログイン → MainMenu → "新しいラン" でマップ表示
- [ ] トップバーに HP 80/80 / Gold 99 / 空の potion スロット 3 が表示される
- [ ] Start マスの隣の Enemy マスをクリック → BattleOverlay が出て、敵名・HP・imageId が見える
- [ ] BattleOverlay の「マップを見る」で戦闘が隠れ、クリックで戻る
- [ ] 「勝利」ボタンで RewardPopup が表示される（Gold / Potion（ある時）/ Card 3 択）
- [ ] Gold 行クリックで Gold が加算され、checkmark
- [ ] Potion が出た場合、スロット満杯の状態（事前に任意で手動セット）なら「満杯」alert が出て discard できる
- [ ] Card 行クリックで 3 枚提示 → 1 枚選ぶとデッキに追加 / Skip で skipped
- [ ] 全 claimed で「進む」が有効、クリックでマップに戻る
- [ ] Rest マスで HP が全回復する（バーが Max まで戻る）
- [ ] Treasure / Event マスで RewardPopup が出る（Card 選択肢なし）
- [ ] Shop マスで placeholder メッセージが表示される
- [ ] Elite マス（ある時）で Elite pool から encounter が draw される（敵名が Weak と違う）
- [ ] Boss マスで Boss encounter（six_ghost / slime_king / guardian_golem のいずれか）が出る
- [ ] リロード（F5）で戦闘途中 / 報酬途中から復帰できる
- [ ] ラン放棄（InGameMenu）ができ、MainMenu から新規ラン開始可能

- [ ] 確認完了後、失敗項目があれば該当タスクに戻って修正。全 PASS なら commit なし（確認ログだけ）。

---

## Task 43: 最終クリーンアップ & レビュー

- [ ] **Step 1: Format**

```bash
dotnet format
cd src/Client && npm run lint -- --fix && cd -
```

- [ ] **Step 2: Full test run**

```bash
dotnet test
cd src/Client && npm run test && cd -
```

Expected: 全 PASS

- [ ] **Step 3: Diff 全体レビュー**

`git diff main...HEAD` で差分を通読し、spec と照合する。残っている TODO / placeholder 注釈、未使用コード、コメントアウト箇所があれば解消する。

- [ ] **Step 4: Commit any fixup**

```bash
git add -u
git commit -m "polish(phase05): final formatting and cleanup"
```

- [ ] **Step 5: Finish**

`superpowers:finishing-a-development-branch` skill に従い、PR 作成 or merge を提案する。

---

## 付録: Phase 5 の主要な判断サマリ（レビュー用）

- **JSON 駆動**: Cards / Potions / Relics / Enemies / Encounters / RewardTables / Characters、全て `Data/` 配下の embedded resource。コンテンツ追加は JSON 編集のみで可能。
- **CardRarity**: 既存の 5 段 enum のうち Phase 5 では `Common` / `Rare` / `Epic` の 3 段のみ使う（Promo / Legendary は手付かず）。STS の Common / Uncommon / Rare に相当。
- **敵の state machine**: `EnemyDefinition.Moves` (MoveDefinition[]) + `InitialMoveId` + `ImageId`。同一 imageId でも move パターンが異なれば別 id を作る（複数体同時出現時のコピー挙動回避）。
- **RunState v3**: v2 セーブデータは破棄（`SchemaVersion != 3` で null 返却）。
- **UI レイヤー**: 画面遷移ではなく MapScreen + BattleOverlay + RewardPopup の 3 レイヤー重ね。
- **動的確率**: Potion は drop で -10、miss で +10、[0,100] でクランプ。Elite/Boss は動的補正外。Rare 確率は Rare 出現でリセット、miss で +1。
- **非戦闘 placeholder**: Rest=HP 全回復、Event/Treasure=Gold 報酬のみ（Card 無し）、Shop=何もしない、Start=何もしない。

