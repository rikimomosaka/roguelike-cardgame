# Phase 8 — 図鑑・プレイ履歴 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** メイン画面「実績」からアカウント単位の図鑑（カード / レリック / ポーション / モンスター）と過去ランの履歴一覧を閲覧できるようにする。

**Architecture:** Core に純粋な `BestiaryState` / `BestiaryTracker` / `BestiaryUpdater` を追加。RunState 内で 4 つの発見セット（`SeenCardBaseIds` など）を保持し、ラン終了時に RunHistoryRecord にコピーしてから Server の `FileBestiaryRepository` 経由でアカウント単位 Bestiary にマージする。Client は新規 `AchievementsScreen`（5 タブ）を追加し、MainMenuScreen の「実績」ボタンから遷移する。

**Tech Stack:** C# .NET 10（Core / Server）、xUnit、ASP.NET Core 10 Controllers、React 19 + TypeScript + Vite + vitest、`System.Text.Json`（`JsonNode` 経由のマイグレーション）。

**Spec:** `docs/superpowers/specs/2026-04-23-phase08-bestiary-history-design.md`

---

## プロジェクト前提

- 作業ブランチは既存の `phase08-bestiary-history`（brainstorming で作成済み）。
- コミット / プッシュは Git 自動化 Option B（タスク完了ごとに commit + push）。
- `dotnet test`（Core.Tests / Server.Tests）と `cd src/Client && npm test`（vitest）を各タスクで実行。
- UI はデバッグスタイル維持（Phase 8 完了後に aidesigner で一括リデザイン予定）。

---

## File Structure

### Core（新規）

- `src/Core/Bestiary/BestiaryState.cs` — 発見済み 4 カテゴリを保持する不変 record。
- `src/Core/Bestiary/BestiaryStateSerializer.cs` — `BestiaryState` ⇔ JSON。ID は昇順ソート済みで出力。
- `src/Core/Bestiary/BestiaryTracker.cs` — RunState の 4 セットに ID を和集合で追加する純関数群。
- `src/Core/Bestiary/BestiaryUpdater.cs` — `BestiaryState` と `RunHistoryRecord` をマージ。

### Core（既存修正）

- `src/Core/Run/RunState.cs` — SchemaVersion を 5→6、4 フィールド追加、`NewSoloRun` で初期デッキを tracker に通す。
- `src/Core/Run/RunStateSerializer.cs` — `MigrateV5ToV6` を追加。
- `src/Core/History/RunHistoryRecord.cs` — SchemaVersion を 1→2、4 フィールド追加。
- `src/Core/History/RunHistoryBuilder.cs` — 4 セットを RunState からコピー。
- `src/Core/Rewards/RewardApplier.cs` — `ApplyPotion` / `ClaimRelic` で tracker 呼び出し。
- `src/Core/Merchant/MerchantActions.cs` — `BuyRelic` / `BuyPotion` で tracker 呼び出し。
- `src/Core/Events/EventResolver.cs` — `GainRelic` / `GrantCardReward` で tracker 呼び出し。
- `src/Core/Run/ActStartActions.cs` — `ChooseRelic` で tracker 呼び出し。
- `src/Core/Battle/BattlePlaceholder.cs` — `Start` でエンカウンターの `EnemyIds` を tracker に通す。

### Server（新規）

- `src/Server/Abstractions/IBestiaryRepository.cs` — Load / Save / Merge の 3 メソッド。
- `src/Server/Services/FileBacked/FileBestiaryRepository.cs` — `{root}/bestiary/{accountId}.json`。
- `src/Server/Controllers/BestiaryController.cs` — `GET /api/v1/bestiary`。
- `src/Server/Dtos/BestiaryDto.cs` — 発見済み + AllKnown の 8 配列。

### Server（既存修正）

- `src/Server/Dtos/RunSnapshotDto.cs` — `RunResultDto` に 4 フィールド追加、`ToResultDto` でマッピング。
- `src/Server/Services/FileBacked/FileHistoryRepository.cs` — v1 → v2 マイグレーション（JsonNode 経由で空配列注入）。
- `src/Server/Controllers/RunsController.cs` — 各ラン終了時に `bestiary.MergeAsync` を追加。報酬生成後に `BestiaryTracker.NoteCardsSeen` を呼ぶ。
- `src/Server/Controllers/MerchantController.cs` — 在庫生成後に `BestiaryTracker.NoteCardsSeen` を呼ぶ。
- `src/Server/Program.cs` — `FileBestiaryRepository` を DI 登録。

### Client（新規）

- `src/Client/src/api/bestiary.ts` — `fetchBestiary(accountId)`。
- `src/Client/src/screens/AchievementsScreen.tsx` — 5 タブの図鑑 / 履歴画面。
- `tests/Client/src/api/bestiary.test.ts`（ないし該当 vitest 配下）。
- `tests/Client/src/screens/AchievementsScreen.test.tsx`。

### Client（既存修正）

- `src/Client/src/api/types.ts` — `BestiaryDto` 追加、`RunResultDto` に 4 フィールド追加。
- `src/Client/src/screens/MainMenuScreen.tsx` — `onAchievements` prop、「実績」ボタンを発火させる。
- `src/Client/src/App.tsx` — `{ kind: 'achievements' }` スクリーン追加。

---

## タスク目次

1. BestiaryState 追加
2. BestiaryStateSerializer 追加
3. RunState v5→v6 拡張（4 フィールド）
4. RunStateSerializer V5→V6 migration
5. BestiaryTracker 追加
6. RunState.NewSoloRun で初期デッキを tracker に通す
7. RunHistoryRecord v1→v2 拡張
8. RunHistoryBuilder で 4 セットをコピー
9. BestiaryUpdater.Merge 追加
10. IBestiaryRepository + FileBestiaryRepository
11. BestiaryDto + BestiaryController
12. FileHistoryRepository の v1→v2 migration
13. RunResultDto 拡張 + マッパー更新
14. RewardApplier の tracker 呼び出し
15. MerchantActions の tracker 呼び出し
16. EventResolver の tracker 呼び出し
17. ActStartActions の tracker 呼び出し
18. BattlePlaceholder.Start の tracker 呼び出し
19. RunsController: 報酬カード choices を tracker に通す
20. MerchantController: 在庫カードを tracker に通す
21. RunsController: ラン終了時に bestiary.MergeAsync
22. Program.cs DI 登録
23. Client API 層（bestiary.ts + types.ts）
24. Client: AchievementsScreen 実装
25. Client: MainMenuScreen / App.tsx 導線

---

## Task 1: BestiaryState

**Files:**
- Create: `src/Core/Bestiary/BestiaryState.cs`
- Test: `tests/Core.Tests/Bestiary/BestiaryStateTests.cs`

- [ ] **Step 1.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Bestiary;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Bestiary;

public class BestiaryStateTests
{
    [Fact]
    public void Empty_HasCurrentSchemaVersion_AndEmptySets()
    {
        var s = BestiaryState.Empty;
        Assert.Equal(BestiaryState.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Empty(s.DiscoveredCardBaseIds);
        Assert.Empty(s.DiscoveredRelicIds);
        Assert.Empty(s.DiscoveredPotionIds);
        Assert.Empty(s.EncounteredEnemyIds);
    }

    [Fact]
    public void RecordEquality_ByValue()
    {
        var a = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("strike"),
        };
        var b = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("strike"),
        };
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 1.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BestiaryStateTests`
Expected: FAIL（`BestiaryState` 型が存在しない）

- [ ] **Step 1.3: Implement BestiaryState**

```csharp
// src/Core/Bestiary/BestiaryState.cs
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Bestiary;

/// <summary>アカウント単位で発見済み ID を蓄積する図鑑ステート。</summary>
public sealed record BestiaryState(
    int SchemaVersion,
    ImmutableHashSet<string> DiscoveredCardBaseIds,
    ImmutableHashSet<string> DiscoveredRelicIds,
    ImmutableHashSet<string> DiscoveredPotionIds,
    ImmutableHashSet<string> EncounteredEnemyIds)
{
    public const int CurrentSchemaVersion = 1;

    public static BestiaryState Empty { get; } = new(
        CurrentSchemaVersion,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty);
}
```

- [ ] **Step 1.4: Run test to verify it passes**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BestiaryStateTests`
Expected: PASS

- [ ] **Step 1.5: Commit**

```bash
git add src/Core/Bestiary/BestiaryState.cs tests/Core.Tests/Bestiary/BestiaryStateTests.cs
git commit -m "feat(core): add BestiaryState record"
git push
```

---

## Task 2: BestiaryStateSerializer

**Files:**
- Create: `src/Core/Bestiary/BestiaryStateSerializer.cs`
- Test: `tests/Core.Tests/Bestiary/BestiaryStateSerializerTests.cs`

- [ ] **Step 2.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Bestiary;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Bestiary;

public class BestiaryStateSerializerTests
{
    [Fact]
    public void Roundtrip_PreservesSets()
    {
        var original = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("strike", "defend"),
            DiscoveredRelicIds = ImmutableHashSet.Create("burning_blood"),
            DiscoveredPotionIds = ImmutableHashSet.Create("fire_potion"),
            EncounteredEnemyIds = ImmutableHashSet.Create("jaw_worm"),
        };
        var json = BestiaryStateSerializer.Serialize(original);
        var restored = BestiaryStateSerializer.Deserialize(json);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Serialize_EmitsIdsInAscendingOrder()
    {
        var s = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("zap", "anger", "strike"),
        };
        var json = BestiaryStateSerializer.Serialize(s);
        int iAnger = json.IndexOf("\"anger\"", System.StringComparison.Ordinal);
        int iStrike = json.IndexOf("\"strike\"", System.StringComparison.Ordinal);
        int iZap = json.IndexOf("\"zap\"", System.StringComparison.Ordinal);
        Assert.True(iAnger < iStrike && iStrike < iZap, $"IDs not sorted: {json}");
    }
}
```

- [ ] **Step 2.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BestiaryStateSerializerTests`
Expected: FAIL（型が存在しない）

- [ ] **Step 2.3: Implement BestiaryStateSerializer**

```csharp
// src/Core/Bestiary/BestiaryStateSerializer.cs
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Bestiary;

public sealed class BestiaryStateSerializerException : Exception
{
    public BestiaryStateSerializerException(string message) : base(message) { }
    public BestiaryStateSerializerException(string message, Exception inner) : base(message, inner) { }
}

public static class BestiaryStateSerializer
{
    public static string Serialize(BestiaryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var dto = new
        {
            schemaVersion = state.SchemaVersion,
            discoveredCardBaseIds = state.DiscoveredCardBaseIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            discoveredRelicIds = state.DiscoveredRelicIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            discoveredPotionIds = state.DiscoveredPotionIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            encounteredEnemyIds = state.EncounteredEnemyIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
        };
        return JsonSerializer.Serialize(dto, JsonOptions.Default);
    }

    public static BestiaryState Deserialize(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex)
        { throw new BestiaryStateSerializerException("Bestiary JSON のパースに失敗しました。", ex); }

        if (node is not JsonObject obj)
            throw new BestiaryStateSerializerException("Bestiary JSON のルートがオブジェクトではありません。");

        int version = obj["schemaVersion"]?.GetValue<int>()
            ?? throw new BestiaryStateSerializerException("schemaVersion が存在しません。");
        if (version != BestiaryState.CurrentSchemaVersion)
            throw new BestiaryStateSerializerException(
                $"未対応の schemaVersion: {version} (対応: {BestiaryState.CurrentSchemaVersion})");

        return new BestiaryState(
            SchemaVersion: version,
            DiscoveredCardBaseIds: ReadSet(obj, "discoveredCardBaseIds"),
            DiscoveredRelicIds: ReadSet(obj, "discoveredRelicIds"),
            DiscoveredPotionIds: ReadSet(obj, "discoveredPotionIds"),
            EncounteredEnemyIds: ReadSet(obj, "encounteredEnemyIds"));
    }

    private static ImmutableHashSet<string> ReadSet(JsonObject obj, string key)
    {
        if (obj[key] is not JsonArray arr) return ImmutableHashSet<string>.Empty;
        var builder = ImmutableHashSet.CreateBuilder<string>();
        foreach (var n in arr)
        {
            var s = n?.GetValue<string>();
            if (!string.IsNullOrEmpty(s)) builder.Add(s);
        }
        return builder.ToImmutable();
    }
}
```

- [ ] **Step 2.4: Run test to verify it passes**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BestiaryStateSerializerTests`
Expected: PASS

- [ ] **Step 2.5: Commit**

```bash
git add src/Core/Bestiary/BestiaryStateSerializer.cs tests/Core.Tests/Bestiary/BestiaryStateSerializerTests.cs
git commit -m "feat(core): add BestiaryStateSerializer with sorted JSON output"
git push
```

---

## Task 3: RunState に 4 フィールドを追加（v5→v6）

**Files:**
- Modify: `src/Core/Run/RunState.cs`
- Test: `tests/Core.Tests/Run/RunStateTests.cs`（既存ファイル、あれば append）

- [ ] **Step 3.1: Write the failing test**

新規ファイル `tests/Core.Tests/Run/RunStateBestiaryFieldsTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateBestiaryFieldsTests
{
    [Fact]
    public void CurrentSchemaVersion_Is6()
    {
        Assert.Equal(6, RunState.CurrentSchemaVersion);
    }

    [Fact]
    public void NewSoloRun_InitializesBestiarySets_NonDefault_Empty()
    {
        var state = RunStateTestFixtures.NewSoloRunDefault();
        Assert.False(state.SeenCardBaseIds.IsDefault);
        Assert.False(state.AcquiredRelicIds.IsDefault);
        Assert.False(state.AcquiredPotionIds.IsDefault);
        Assert.False(state.EncounteredEnemyIds.IsDefault);
    }
}
```

また、既存テストフィクスチャを確認して足りないヘルパ `RunStateTestFixtures.NewSoloRunDefault()` を作成（既存 RunState テストが同等のものを持つ場合は流用）。新規追加する場合:

```csharp
// tests/Core.Tests/Run/RunStateTestFixtures.cs
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Infra;

namespace RoguelikeCardGame.Core.Tests.Run;

internal static class RunStateTestFixtures
{
    public static RunState NewSoloRunDefault()
    {
        var catalog = TestCatalog.Load();
        return RunState.NewSoloRun(
            catalog,
            rngSeed: 42UL,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray.Create("enc_weak_01"),
            encounterQueueStrong: ImmutableArray.Create("enc_strong_01"),
            encounterQueueElite: ImmutableArray.Create("enc_elite_01"),
            encounterQueueBoss: ImmutableArray.Create("enc_boss_01"),
            nowUtc: DateTimeOffset.UnixEpoch);
    }
}
```

`TestCatalog.Load()` は既存のテストヘルパを使う（`tests/Core.Tests/Infra/TestCatalog.cs` などが既にあるはず。なければ既存の他のテストがどう DataCatalog を得ているか参考にして揃える）。

- [ ] **Step 3.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunStateBestiaryFieldsTests`
Expected: FAIL（`SeenCardBaseIds` 等のプロパティが存在しない、`CurrentSchemaVersion == 5`）

- [ ] **Step 3.3: Add fields to RunState**

`src/Core/Run/RunState.cs` に以下修正を加える:

1. コンストラクタ引数に 4 フィールドを追加（既存順序を保ち、末尾の `DiscardUsesSoFar = 0` の直前に追加）:

```csharp
// --- Phase 8 additions ---
ImmutableArray<string> SeenCardBaseIds,
ImmutableArray<string> AcquiredRelicIds,
ImmutableArray<string> AcquiredPotionIds,
ImmutableArray<string> EncounteredEnemyIds,

int DiscardUsesSoFar = 0)
```

2. `CurrentSchemaVersion` を `5` → `6` に変更:

```csharp
public const int CurrentSchemaVersion = 6;
```

3. `NewSoloRun` の `return new RunState(` 内で、既存 `ActiveActStartRelicChoice: null` の直後に以下を追加（`DiscardUsesSoFar` はデフォルト値 0 が使われる）:

```csharp
SeenCardBaseIds: ImmutableArray<string>.Empty,
AcquiredRelicIds: ImmutableArray<string>.Empty,
AcquiredPotionIds: ImmutableArray<string>.Empty,
EncounteredEnemyIds: ImmutableArray<string>.Empty);
```

- [ ] **Step 3.4: Run test to verify it passes**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunStateBestiaryFieldsTests`
Expected: PASS

既存の RunState 関連テストも全て回す:

Run: `dotnet test tests/Core.Tests`
Expected: PASS（既存の RunState デシリアライズ系テストが v5→v6 で落ちる場合は次の Task 4 で直す。もしここで先に落ちたら Task 4 を先に実装）

- [ ] **Step 3.5: Commit**

```bash
git add src/Core/Run/RunState.cs tests/Core.Tests/Run/RunStateBestiaryFieldsTests.cs tests/Core.Tests/Run/RunStateTestFixtures.cs
git commit -m "feat(core): add bestiary tracking fields to RunState (schema v6)"
git push
```

---

## Task 4: RunStateSerializer V5→V6 migration

**Files:**
- Modify: `src/Core/Run/RunStateSerializer.cs`
- Test: 既存の `tests/Core.Tests/Run/RunStateSerializerMigrationTests.cs`（あれば append、無ければ作成）

- [ ] **Step 4.1: Write the failing test**

```csharp
// tests/Core.Tests/Run/RunStateSerializerMigrationTests.cs 追加分:
[Fact]
public void V5_To_V6_FillsEmptyBestiarySets()
{
    // v5 形式の最小 JSON（他フィールドは既存 migration テストと同じ作り方で用意）
    var v5Json = """
    {
      "schemaVersion": 5,
      "currentAct": 1,
      "currentNodeId": 0,
      "visitedNodeIds": [0],
      "unknownResolutions": {},
      "characterId": "default",
      "currentHp": 80,
      "maxHp": 80,
      "gold": 99,
      "deck": [],
      "potions": ["", "", ""],
      "potionSlotCount": 3,
      "activeBattle": null,
      "activeReward": null,
      "encounterQueueWeak": [],
      "encounterQueueStrong": [],
      "encounterQueueElite": [],
      "encounterQueueBoss": [],
      "rewardRngState": { "currentPotionPercent": 40, "epicBonus": 0 },
      "activeMerchant": null,
      "activeEvent": null,
      "activeRestPending": false,
      "activeRestCompleted": false,
      "relics": [],
      "playSeconds": 0,
      "rngSeed": 42,
      "savedAtUtc": "1970-01-01T00:00:00+00:00",
      "progress": "InProgress",
      "runId": "00000000-0000-0000-0000-000000000000",
      "activeActStartRelicChoice": null,
      "discardUsesSoFar": 0
    }
    """;

    var state = RunStateSerializer.Deserialize(v5Json);
    Assert.Equal(RunState.CurrentSchemaVersion, state.SchemaVersion);
    Assert.Empty(state.SeenCardBaseIds);
    Assert.Empty(state.AcquiredRelicIds);
    Assert.Empty(state.AcquiredPotionIds);
    Assert.Empty(state.EncounteredEnemyIds);
    Assert.False(state.SeenCardBaseIds.IsDefault);
}
```

- [ ] **Step 4.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~V5_To_V6`
Expected: FAIL（`未対応の schemaVersion: 5`）

- [ ] **Step 4.3: Implement migration**

`src/Core/Run/RunStateSerializer.cs` を更新:

1. `Deserialize` 内のマイグレーションチェーンに V5→V6 を追加:

```csharp
if (version == 3) { obj = MigrateV3ToV4(obj); version = 4; }
if (version == 4) { obj = MigrateV4ToV5(obj); version = 5; }
if (version == 5) { obj = MigrateV5ToV6(obj); version = 6; }
```

2. 新メソッドを追加:

```csharp
private static JsonObject MigrateV5ToV6(JsonObject obj)
{
    obj["seenCardBaseIds"] = new JsonArray();
    obj["acquiredRelicIds"] = new JsonArray();
    obj["acquiredPotionIds"] = new JsonArray();
    obj["encounteredEnemyIds"] = new JsonArray();
    obj["schemaVersion"] = RunState.CurrentSchemaVersion;
    return obj;
}
```

- [ ] **Step 4.4: Run test to verify it passes**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunStateSerializer`
Expected: PASS。続けて既存全体:
Run: `dotnet test tests/Core.Tests`
Expected: PASS

- [ ] **Step 4.5: Commit**

```bash
git add src/Core/Run/RunStateSerializer.cs tests/Core.Tests/Run/RunStateSerializerMigrationTests.cs
git commit -m "feat(core): add RunState v5→v6 migration filling bestiary sets"
git push
```

---

## Task 5: BestiaryTracker（4 純関数）

**Files:**
- Create: `src/Core/Bestiary/BestiaryTracker.cs`
- Test: `tests/Core.Tests/Bestiary/BestiaryTrackerTests.cs`

- [ ] **Step 5.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Bestiary;

public class BestiaryTrackerTests
{
    private static RunState Fresh() => RunStateTestFixtures.NewSoloRunDefault();

    [Fact]
    public void NoteCardsSeen_AddsIds_Deduplicated()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteCardsSeen(s, new[] { "strike", "defend", "strike" });
        Assert.Equal(new[] { "defend", "strike" }, Sorted(s.SeenCardBaseIds));
    }

    [Fact]
    public void NoteCardsSeen_Null_NoOp()
    {
        var s = Fresh();
        var after = BestiaryTracker.NoteCardsSeen(s, null);
        Assert.Same(s, after);
    }

    [Fact]
    public void NoteCardsSeen_Empty_NoOp()
    {
        var s = Fresh();
        var after = BestiaryTracker.NoteCardsSeen(s, System.Array.Empty<string>());
        Assert.Same(s, after);
    }

    [Fact]
    public void NoteCardsSeen_Idempotent()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteCardsSeen(s, new[] { "strike" });
        var again = BestiaryTracker.NoteCardsSeen(s, new[] { "strike" });
        Assert.Equal(s, again);
    }

    [Fact]
    public void NoteRelicsAcquired_AddsAndDedupes()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteRelicsAcquired(s, new[] { "burning_blood" });
        s = BestiaryTracker.NoteRelicsAcquired(s, new[] { "burning_blood", "anchor" });
        Assert.Equal(new[] { "anchor", "burning_blood" }, Sorted(s.AcquiredRelicIds));
    }

    [Fact]
    public void NotePotionsAcquired_AddsAndDedupes()
    {
        var s = Fresh();
        s = BestiaryTracker.NotePotionsAcquired(s, new[] { "fire_potion", "fire_potion" });
        Assert.Equal(new[] { "fire_potion" }, Sorted(s.AcquiredPotionIds));
    }

    [Fact]
    public void NoteEnemiesEncountered_AddsAndDedupes()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteEnemiesEncountered(s, new[] { "jaw_worm", "cultist" });
        s = BestiaryTracker.NoteEnemiesEncountered(s, new[] { "jaw_worm" });
        Assert.Equal(new[] { "cultist", "jaw_worm" }, Sorted(s.EncounteredEnemyIds));
    }

    [Fact]
    public void NoteCardsSeen_NullOrEmptyStrings_Skipped()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteCardsSeen(s, new[] { null!, "", "strike" });
        Assert.Equal(new[] { "strike" }, Sorted(s.SeenCardBaseIds));
    }

    private static string[] Sorted(ImmutableArray<string> arr)
    {
        var a = arr.ToArray();
        System.Array.Sort(a, System.StringComparer.Ordinal);
        return a;
    }
}
```

- [ ] **Step 5.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BestiaryTrackerTests`
Expected: FAIL（`BestiaryTracker` 型が存在しない）

- [ ] **Step 5.3: Implement BestiaryTracker**

```csharp
// src/Core/Bestiary/BestiaryTracker.cs
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Bestiary;

/// <summary>RunState の発見セットに ID を和集合で追加する純関数群。</summary>
public static class BestiaryTracker
{
    public static RunState NoteCardsSeen(RunState s, IEnumerable<string>? baseIds)
        => WithUnion(s, baseIds, s.SeenCardBaseIds, a => s with { SeenCardBaseIds = a });

    public static RunState NoteRelicsAcquired(RunState s, IEnumerable<string>? ids)
        => WithUnion(s, ids, s.AcquiredRelicIds, a => s with { AcquiredRelicIds = a });

    public static RunState NotePotionsAcquired(RunState s, IEnumerable<string>? ids)
        => WithUnion(s, ids, s.AcquiredPotionIds, a => s with { AcquiredPotionIds = a });

    public static RunState NoteEnemiesEncountered(RunState s, IEnumerable<string>? ids)
        => WithUnion(s, ids, s.EncounteredEnemyIds, a => s with { EncounteredEnemyIds = a });

    private static RunState WithUnion(
        RunState s,
        IEnumerable<string>? incoming,
        ImmutableArray<string> current,
        System.Func<ImmutableArray<string>, RunState> apply)
    {
        if (incoming is null) return s;
        var existing = current.IsDefault
            ? ImmutableHashSet<string>.Empty
            : current.ToImmutableHashSet();
        var builder = existing.ToBuilder();
        bool changed = false;
        foreach (var id in incoming)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (builder.Add(id)) changed = true;
        }
        if (!changed) return s;
        var result = builder.ToImmutable();
        return apply(ImmutableArray.CreateRange(result));
    }
}
```

- [ ] **Step 5.4: Run test to verify it passes**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BestiaryTrackerTests`
Expected: PASS

- [ ] **Step 5.5: Commit**

```bash
git add src/Core/Bestiary/BestiaryTracker.cs tests/Core.Tests/Bestiary/BestiaryTrackerTests.cs
git commit -m "feat(core): add BestiaryTracker pure functions"
git push
```

---

## Task 6: RunState.NewSoloRun で初期デッキを tracker に通す

**Files:**
- Modify: `src/Core/Run/RunState.cs`
- Test: `tests/Core.Tests/Run/RunStateNewSoloRunTests.cs`（新規、または既存に append）

- [ ] **Step 6.1: Write the failing test**

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateNewSoloRunTests
{
    [Fact]
    public void NewSoloRun_SeedsSeenCardsWithInitialDeckBaseIds()
    {
        var state = RunStateTestFixtures.NewSoloRunDefault();
        var deckIds = state.Deck.Select(c => c.Id).Distinct().OrderBy(s => s).ToArray();
        var seen = state.SeenCardBaseIds.OrderBy(s => s).ToArray();
        Assert.Equal(deckIds, seen);
    }
}
```

- [ ] **Step 6.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunStateNewSoloRunTests`
Expected: FAIL（`SeenCardBaseIds` が空）

- [ ] **Step 6.3: Seed SeenCardBaseIds in NewSoloRun**

`src/Core/Run/RunState.cs` の `NewSoloRun` の `return new RunState(` 呼び出しで、`SeenCardBaseIds:` を `ImmutableArray<string>.Empty` から以下に変更:

```csharp
SeenCardBaseIds: ImmutableArray.CreateRange(ch.Deck.Distinct()),
```

（`using System.Linq;` が既に存在するので `Distinct()` は利用可。ch.Deck は `IReadOnlyList<string>`）

- [ ] **Step 6.4: Run test to verify it passes**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunStateNewSoloRunTests`
Expected: PASS

- [ ] **Step 6.5: Commit**

```bash
git add src/Core/Run/RunState.cs tests/Core.Tests/Run/RunStateNewSoloRunTests.cs
git commit -m "feat(core): seed SeenCardBaseIds with initial deck in NewSoloRun"
git push
```

---

## Task 7: RunHistoryRecord v1→v2 拡張

**Files:**
- Modify: `src/Core/History/RunHistoryRecord.cs`
- Test: `tests/Core.Tests/History/RunHistoryRecordTests.cs`

- [ ] **Step 7.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.History;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.History;

public class RunHistoryRecordTests
{
    [Fact]
    public void CurrentSchemaVersion_Is2()
    {
        Assert.Equal(2, RunHistoryRecord.CurrentSchemaVersion);
    }

    [Fact]
    public void Record_HasBestiaryFields()
    {
        // デフォルト構築できることだけ確認（型シェイプのテスト）
        var rec = new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: "a",
            RunId: "r",
            Outcome: RoguelikeCardGame.Core.Run.RunProgress.Cleared,
            ActReached: 1,
            NodesVisited: 0,
            PlaySeconds: 0L,
            CharacterId: "default",
            FinalHp: 80,
            FinalMaxHp: 80,
            FinalGold: 99,
            FinalDeck: ImmutableArray<RoguelikeCardGame.Core.Cards.CardInstance>.Empty,
            FinalRelics: ImmutableArray<string>.Empty,
            EndedAtUtc: System.DateTimeOffset.UnixEpoch,
            SeenCardBaseIds: ImmutableArray.Create("strike"),
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty);
        Assert.Contains("strike", rec.SeenCardBaseIds);
    }
}
```

- [ ] **Step 7.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunHistoryRecordTests`
Expected: FAIL（CurrentSchemaVersion == 1、4 フィールド未定義）

- [ ] **Step 7.3: Expand RunHistoryRecord**

`src/Core/History/RunHistoryRecord.cs`:

```csharp
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.History;

public sealed record RunHistoryRecord(
    int SchemaVersion,
    string AccountId,
    string RunId,
    RunProgress Outcome,
    int ActReached,
    int NodesVisited,
    long PlaySeconds,
    string CharacterId,
    int FinalHp,
    int FinalMaxHp,
    int FinalGold,
    ImmutableArray<CardInstance> FinalDeck,
    ImmutableArray<string> FinalRelics,
    DateTimeOffset EndedAtUtc,
    ImmutableArray<string> SeenCardBaseIds,
    ImmutableArray<string> AcquiredRelicIds,
    ImmutableArray<string> AcquiredPotionIds,
    ImmutableArray<string> EncounteredEnemyIds)
{
    public const int CurrentSchemaVersion = 2;
}
```

- [ ] **Step 7.4: Run test to verify it passes**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunHistoryRecordTests`
Expected: PASS。ここで `RunHistoryBuilder.From` のビルドエラーが出る（次タスクで修正）ので、**ビルドが通らない可能性** → Task 8 を続けてから通しテスト。

- [ ] **Step 7.5: Commit**

```bash
git add src/Core/History/RunHistoryRecord.cs tests/Core.Tests/History/RunHistoryRecordTests.cs
git commit -m "feat(core): extend RunHistoryRecord to v2 with bestiary fields"
git push
```

---

## Task 8: RunHistoryBuilder で 4 セットをコピー

**Files:**
- Modify: `src/Core/History/RunHistoryBuilder.cs`
- Test: `tests/Core.Tests/History/RunHistoryBuilderTests.cs`

- [ ] **Step 8.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.History;

public class RunHistoryBuilderTests
{
    [Fact]
    public void From_CopiesBestiaryFields()
    {
        var state = RunStateTestFixtures.NewSoloRunDefault() with
        {
            SeenCardBaseIds = ImmutableArray.Create("strike", "defend"),
            AcquiredRelicIds = ImmutableArray.Create("burning_blood"),
            AcquiredPotionIds = ImmutableArray.Create("fire_potion"),
            EncounteredEnemyIds = ImmutableArray.Create("jaw_worm"),
        };
        var rec = RunHistoryBuilder.From("acct", state, nodesVisited: 3, outcome: RunProgress.Cleared);
        Assert.Equal(new[] { "strike", "defend" }, rec.SeenCardBaseIds.ToArray());
        Assert.Equal(new[] { "burning_blood" }, rec.AcquiredRelicIds.ToArray());
        Assert.Equal(new[] { "fire_potion" }, rec.AcquiredPotionIds.ToArray());
        Assert.Equal(new[] { "jaw_worm" }, rec.EncounteredEnemyIds.ToArray());
        Assert.Equal(RunHistoryRecord.CurrentSchemaVersion, rec.SchemaVersion);
    }
}
```

- [ ] **Step 8.2: Run test to verify it fails（または現状ビルド不可）**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RunHistoryBuilderTests`
Expected: BUILD FAIL（`RunHistoryRecord` のコンストラクタ引数不足）

- [ ] **Step 8.3: Update RunHistoryBuilder**

`src/Core/History/RunHistoryBuilder.cs`:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.History;

public static class RunHistoryBuilder
{
    public static RunHistoryRecord From(
        string accountId, RunState state, int nodesVisited, RunProgress outcome)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(state);
        return new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: accountId,
            RunId: state.RunId,
            Outcome: outcome,
            ActReached: state.CurrentAct,
            NodesVisited: nodesVisited,
            PlaySeconds: state.PlaySeconds,
            CharacterId: state.CharacterId,
            FinalHp: state.CurrentHp,
            FinalMaxHp: state.MaxHp,
            FinalGold: state.Gold,
            FinalDeck: state.Deck,
            FinalRelics: state.Relics.ToImmutableArray(),
            EndedAtUtc: DateTimeOffset.UtcNow,
            SeenCardBaseIds: state.SeenCardBaseIds.IsDefault ? ImmutableArray<string>.Empty : state.SeenCardBaseIds,
            AcquiredRelicIds: state.AcquiredRelicIds.IsDefault ? ImmutableArray<string>.Empty : state.AcquiredRelicIds,
            AcquiredPotionIds: state.AcquiredPotionIds.IsDefault ? ImmutableArray<string>.Empty : state.AcquiredPotionIds,
            EncounteredEnemyIds: state.EncounteredEnemyIds.IsDefault ? ImmutableArray<string>.Empty : state.EncounteredEnemyIds);
    }
}
```

- [ ] **Step 8.4: Run tests**

Run: `dotnet test tests/Core.Tests`
Expected: PASS（ここで Core 全体がビルド・パスする状態になる）

- [ ] **Step 8.5: Commit**

```bash
git add src/Core/History/RunHistoryBuilder.cs tests/Core.Tests/History/RunHistoryBuilderTests.cs
git commit -m "feat(core): copy bestiary fields from RunState in RunHistoryBuilder"
git push
```

---

## Task 9: BestiaryUpdater.Merge

**Files:**
- Create: `src/Core/Bestiary/BestiaryUpdater.cs`
- Test: `tests/Core.Tests/Bestiary/BestiaryUpdaterTests.cs`

- [ ] **Step 9.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Bestiary;

public class BestiaryUpdaterTests
{
    private static RunHistoryRecord MakeRecord(
        string[] cards, string[] relics, string[] potions, string[] enemies)
        => new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: "a", RunId: "r", Outcome: RunProgress.Cleared,
            ActReached: 1, NodesVisited: 0, PlaySeconds: 0L,
            CharacterId: "default", FinalHp: 0, FinalMaxHp: 0, FinalGold: 0,
            FinalDeck: ImmutableArray<CardInstance>.Empty,
            FinalRelics: ImmutableArray<string>.Empty,
            EndedAtUtc: System.DateTimeOffset.UnixEpoch,
            SeenCardBaseIds: cards.ToImmutableArray(),
            AcquiredRelicIds: relics.ToImmutableArray(),
            AcquiredPotionIds: potions.ToImmutableArray(),
            EncounteredEnemyIds: enemies.ToImmutableArray());

    [Fact]
    public void Merge_EmptyPlusRecord_AddsAllCategories()
    {
        var rec = MakeRecord(new[] { "strike" }, new[] { "bb" }, new[] { "fp" }, new[] { "jw" });
        var merged = BestiaryUpdater.Merge(BestiaryState.Empty, rec);
        Assert.Contains("strike", merged.DiscoveredCardBaseIds);
        Assert.Contains("bb", merged.DiscoveredRelicIds);
        Assert.Contains("fp", merged.DiscoveredPotionIds);
        Assert.Contains("jw", merged.EncounteredEnemyIds);
    }

    [Fact]
    public void Merge_Idempotent()
    {
        var rec = MakeRecord(new[] { "strike" }, new[] { "bb" }, new[] { "fp" }, new[] { "jw" });
        var once = BestiaryUpdater.Merge(BestiaryState.Empty, rec);
        var twice = BestiaryUpdater.Merge(once, rec);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Merge_PreservesCurrent()
    {
        var start = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("defend"),
        };
        var rec = MakeRecord(new[] { "strike" }, System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>());
        var merged = BestiaryUpdater.Merge(start, rec);
        Assert.Contains("defend", merged.DiscoveredCardBaseIds);
        Assert.Contains("strike", merged.DiscoveredCardBaseIds);
    }
}
```

- [ ] **Step 9.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BestiaryUpdaterTests`
Expected: FAIL（`BestiaryUpdater` 型が存在しない）

- [ ] **Step 9.3: Implement BestiaryUpdater**

```csharp
// src/Core/Bestiary/BestiaryUpdater.cs
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.History;

namespace RoguelikeCardGame.Core.Bestiary;

public static class BestiaryUpdater
{
    public static BestiaryState Merge(BestiaryState current, RunHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(record);
        return new BestiaryState(
            SchemaVersion: BestiaryState.CurrentSchemaVersion,
            DiscoveredCardBaseIds: Union(current.DiscoveredCardBaseIds, record.SeenCardBaseIds),
            DiscoveredRelicIds: Union(current.DiscoveredRelicIds, record.AcquiredRelicIds),
            DiscoveredPotionIds: Union(current.DiscoveredPotionIds, record.AcquiredPotionIds),
            EncounteredEnemyIds: Union(current.EncounteredEnemyIds, record.EncounteredEnemyIds));
    }

    private static ImmutableHashSet<string> Union(ImmutableHashSet<string> current, ImmutableArray<string> incoming)
    {
        if (incoming.IsDefaultOrEmpty) return current;
        var b = current.ToBuilder();
        foreach (var id in incoming)
            if (!string.IsNullOrEmpty(id)) b.Add(id);
        return b.ToImmutable();
    }
}
```

- [ ] **Step 9.4: Run test to verify it passes**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BestiaryUpdaterTests`
Expected: PASS

- [ ] **Step 9.5: Commit**

```bash
git add src/Core/Bestiary/BestiaryUpdater.cs tests/Core.Tests/Bestiary/BestiaryUpdaterTests.cs
git commit -m "feat(core): add BestiaryUpdater.Merge(current, record)"
git push
```

---

## Task 10: IBestiaryRepository + FileBestiaryRepository

**Files:**
- Create: `src/Server/Abstractions/IBestiaryRepository.cs`
- Create: `src/Server/Services/FileBacked/FileBestiaryRepository.cs`
- Test: `tests/Server.Tests/FileBacked/FileBestiaryRepositoryTests.cs`

- [ ] **Step 10.1: Write the failing test**

```csharp
using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.FileBacked;

public class FileBestiaryRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly FileBestiaryRepository _repo;

    public FileBestiaryRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "roguelike-bestiary-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var opts = Options.Create(new DataStorageOptions { RootDirectory = _root });
        _repo = new FileBestiaryRepository(opts);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        var loaded = await _repo.LoadAsync("aaa", default);
        Assert.Equal(BestiaryState.Empty, loaded);
    }

    [Fact]
    public async Task Save_Then_Load_Roundtrip()
    {
        var state = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("strike"),
        };
        await _repo.SaveAsync("bbb", state, default);
        var loaded = await _repo.LoadAsync("bbb", default);
        Assert.Equal(state, loaded);
    }

    [Fact]
    public async Task MergeAsync_AppliesRecordToCurrent()
    {
        var rec = new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: "ccc", RunId: "r", Outcome: RunProgress.Cleared,
            ActReached: 1, NodesVisited: 0, PlaySeconds: 0L, CharacterId: "default",
            FinalHp: 80, FinalMaxHp: 80, FinalGold: 99,
            FinalDeck: ImmutableArray<CardInstance>.Empty,
            FinalRelics: ImmutableArray<string>.Empty,
            EndedAtUtc: DateTimeOffset.UnixEpoch,
            SeenCardBaseIds: ImmutableArray.Create("strike"),
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray.Create("jaw_worm"));
        await _repo.MergeAsync("ccc", rec, default);
        var loaded = await _repo.LoadAsync("ccc", default);
        Assert.Contains("strike", loaded.DiscoveredCardBaseIds);
        Assert.Contains("jaw_worm", loaded.EncounteredEnemyIds);
    }
}
```

- [ ] **Step 10.2: Run test to verify it fails**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~FileBestiaryRepositoryTests`
Expected: FAIL（型が存在しない）

- [ ] **Step 10.3: Implement IBestiaryRepository**

```csharp
// src/Server/Abstractions/IBestiaryRepository.cs
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.History;

namespace RoguelikeCardGame.Server.Abstractions;

public interface IBestiaryRepository
{
    Task<BestiaryState> LoadAsync(string accountId, CancellationToken ct);
    Task SaveAsync(string accountId, BestiaryState state, CancellationToken ct);
    Task MergeAsync(string accountId, RunHistoryRecord record, CancellationToken ct);
}
```

- [ ] **Step 10.4: Implement FileBestiaryRepository**

```csharp
// src/Server/Services/FileBacked/FileBestiaryRepository.cs
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/bestiary/{accountId}.json</c> にアカウント単位 Bestiary を保存。</summary>
public sealed class FileBestiaryRepository : IBestiaryRepository
{
    private readonly string _root;
    private static readonly System.Collections.Generic.Dictionary<string, SemaphoreSlim> _locks = new();

    public FileBestiaryRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory 未設定", nameof(options));
        _root = Path.Combine(root, "bestiary");
    }

    public async Task<BestiaryState> LoadAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return BestiaryState.Empty;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return BestiaryStateSerializer.Deserialize(json);
    }

    public async Task SaveAsync(string accountId, BestiaryState state, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(_root);
        var json = BestiaryStateSerializer.Serialize(state);
        await File.WriteAllTextAsync(PathFor(accountId), json, new UTF8Encoding(false), ct);
    }

    public async Task MergeAsync(string accountId, RunHistoryRecord record, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ArgumentNullException.ThrowIfNull(record);
        var sem = GetLock(accountId);
        await sem.WaitAsync(ct);
        try
        {
            var current = await LoadAsync(accountId, ct);
            var merged = BestiaryUpdater.Merge(current, record);
            await SaveAsync(accountId, merged, ct);
        }
        finally { sem.Release(); }
    }

    private string PathFor(string accountId) => Path.Combine(_root, accountId + ".json");

    private static SemaphoreSlim GetLock(string accountId)
    {
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out var s))
                _locks[accountId] = s = new SemaphoreSlim(1, 1);
            return s;
        }
    }
}
```

- [ ] **Step 10.5: Run test to verify it passes**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~FileBestiaryRepositoryTests`
Expected: PASS

- [ ] **Step 10.6: Commit**

```bash
git add src/Server/Abstractions/IBestiaryRepository.cs src/Server/Services/FileBacked/FileBestiaryRepository.cs tests/Server.Tests/FileBacked/FileBestiaryRepositoryTests.cs
git commit -m "feat(server): add FileBestiaryRepository with Load/Save/Merge"
git push
```

---

## Task 11: BestiaryDto + BestiaryController

**Files:**
- Create: `src/Server/Dtos/BestiaryDto.cs`
- Create: `src/Server/Controllers/BestiaryController.cs`
- Test: `tests/Server.Tests/Controllers/BestiaryControllerTests.cs`

- [ ] **Step 11.1: Write the failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class BestiaryControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BestiaryControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Get_WithoutHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/bestiary");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownAccount_Returns404()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Account-Id", "does-not-exist-" + System.Guid.NewGuid().ToString("N"));
        var resp = await client.GetAsync("/api/v1/bestiary");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_KnownAccount_NoBestiaryFile_Returns200_EmptyDiscovered_WithAllKnown()
    {
        var accountId = await TestAccounts.CreateAsync(_factory);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Account-Id", accountId);
        var dto = await client.GetFromJsonAsync<BestiaryDto>("/api/v1/bestiary");
        Assert.NotNull(dto);
        Assert.Empty(dto!.DiscoveredCardBaseIds);
        Assert.NotEmpty(dto.AllKnownCardBaseIds);
        // AllKnown* は昇順
        for (int i = 1; i < dto.AllKnownCardBaseIds.Count; i++)
            Assert.True(string.CompareOrdinal(dto.AllKnownCardBaseIds[i - 1], dto.AllKnownCardBaseIds[i]) <= 0);
    }
}
```

※ `TestAccounts.CreateAsync(factory)` は既存の Server.Tests インフラを使用（`tests/Server.Tests/Infra/` 配下に類似ヘルパが存在するはず。無い場合は既存の AccountsControllerTests などが使っているパターンを流用）。

- [ ] **Step 11.2: Run test to verify it fails**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~BestiaryControllerTests`
Expected: FAIL（404/Not routed）

- [ ] **Step 11.3: Implement BestiaryDto**

```csharp
// src/Server/Dtos/BestiaryDto.cs
using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BestiaryDto(
    int SchemaVersion,
    IReadOnlyList<string> DiscoveredCardBaseIds,
    IReadOnlyList<string> DiscoveredRelicIds,
    IReadOnlyList<string> DiscoveredPotionIds,
    IReadOnlyList<string> EncounteredEnemyIds,
    IReadOnlyList<string> AllKnownCardBaseIds,
    IReadOnlyList<string> AllKnownRelicIds,
    IReadOnlyList<string> AllKnownPotionIds,
    IReadOnlyList<string> AllKnownEnemyIds);
```

- [ ] **Step 11.4: Implement BestiaryController**

```csharp
// src/Server/Controllers/BestiaryController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/bestiary")]
public sealed class BestiaryController : ControllerBase
{
    private readonly IAccountRepository _accounts;
    private readonly IBestiaryRepository _bestiary;
    private readonly DataCatalog _data;

    public BestiaryController(IAccountRepository accounts, IBestiaryRepository bestiary, DataCatalog data)
    {
        _accounts = accounts;
        _bestiary = bestiary;
        _data = data;
    }

    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryAcc(out var acc, out var err)) return err!;
        if (!await _accounts.ExistsAsync(acc, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "アカウントなし");

        var state = await _bestiary.LoadAsync(acc, ct);
        var dto = new BestiaryDto(
            SchemaVersion: state.SchemaVersion,
            DiscoveredCardBaseIds: Sorted(state.DiscoveredCardBaseIds),
            DiscoveredRelicIds: Sorted(state.DiscoveredRelicIds),
            DiscoveredPotionIds: Sorted(state.DiscoveredPotionIds),
            EncounteredEnemyIds: Sorted(state.EncounteredEnemyIds),
            AllKnownCardBaseIds: _data.Cards.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            AllKnownRelicIds: _data.Relics.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            AllKnownPotionIds: _data.Potions.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            AllKnownEnemyIds: _data.Enemies.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        return Ok(dto);
    }

    private static IReadOnlyList<string> Sorted(System.Collections.Immutable.ImmutableHashSet<string> set)
        => set.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    private bool TryAcc(out string id, out IActionResult? err)
    {
        id = string.Empty; err = null;
        if (!Request.Headers.TryGetValue(RunsController.AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            err = Problem(statusCode: StatusCodes.Status400BadRequest, title: "account header missing");
            return false;
        }
        id = raw.ToString();
        try { AccountIdValidator.Validate(id); }
        catch (ArgumentException ex)
        {
            err = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            return false;
        }
        return true;
    }
}
```

- [ ] **Step 11.5: DI 登録（ここでは Task 22 を先取り）**

Program.cs に以下を追加（`AddSingleton<IHistoryRepository, FileHistoryRepository>` の直下）:

```csharp
builder.Services.AddSingleton<IBestiaryRepository, FileBestiaryRepository>();
```

- [ ] **Step 11.6: Run test to verify it passes**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~BestiaryControllerTests`
Expected: PASS

- [ ] **Step 11.7: Commit**

```bash
git add src/Server/Dtos/BestiaryDto.cs src/Server/Controllers/BestiaryController.cs src/Server/Program.cs tests/Server.Tests/Controllers/BestiaryControllerTests.cs
git commit -m "feat(server): add BestiaryController + DI registration"
git push
```

---

## Task 12: FileHistoryRepository の v1→v2 migration

**Files:**
- Modify: `src/Server/Services/FileBacked/FileHistoryRepository.cs`
- Test: `tests/Server.Tests/FileBacked/FileHistoryRepositoryMigrationTests.cs`

- [ ] **Step 12.1: Write the failing test**

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.FileBacked;

public class FileHistoryRepositoryMigrationTests : IDisposable
{
    private readonly string _root;
    private readonly FileHistoryRepository _repo;

    public FileHistoryRepositoryMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "roguelike-history-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var opts = Options.Create(new DataStorageOptions { RootDirectory = _root });
        _repo = new FileHistoryRepository(opts);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task Load_V1_File_FillsEmptyBestiarySets()
    {
        var accountDir = Path.Combine(_root, "history", "acct");
        Directory.CreateDirectory(accountDir);
        var v1 = """
        {
          "schemaVersion": 1,
          "accountId": "acct",
          "runId": "r1",
          "outcome": "Cleared",
          "actReached": 3,
          "nodesVisited": 15,
          "playSeconds": 1200,
          "characterId": "default",
          "finalHp": 40,
          "finalMaxHp": 80,
          "finalGold": 150,
          "finalDeck": [],
          "finalRelics": [],
          "endedAtUtc": "2025-01-01T00:00:00+00:00"
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(accountDir, "20250101T000000000Z_r1.json"), v1, new UTF8Encoding(false));

        var list = await _repo.ListAsync("acct", default);
        Assert.Single(list);
        var rec = list[0];
        Assert.Empty(rec.SeenCardBaseIds);
        Assert.Empty(rec.AcquiredRelicIds);
        Assert.Empty(rec.AcquiredPotionIds);
        Assert.Empty(rec.EncounteredEnemyIds);
        Assert.False(rec.SeenCardBaseIds.IsDefault);
    }
}
```

- [ ] **Step 12.2: Run test to verify it fails**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~FileHistoryRepositoryMigrationTests`
Expected: FAIL（`IsDefault == true` or deserializer exception）

- [ ] **Step 12.3: Update FileHistoryRepository to migrate v1 → v2**

`src/Server/Services/FileBacked/FileHistoryRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Json;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

public sealed class FileHistoryRepository : IHistoryRepository
{
    private readonly string _root;

    public FileHistoryRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory 未設定", nameof(options));
        _root = Path.Combine(root, "history");
    }

    public async Task AppendAsync(string accountId, RunHistoryRecord record, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ArgumentNullException.ThrowIfNull(record);
        var dir = Path.Combine(_root, accountId);
        Directory.CreateDirectory(dir);
        var stamp = record.EndedAtUtc.ToString("yyyyMMddTHHmmssfffZ");
        var fileName = $"{stamp}_{record.RunId}.json";
        var path = Path.Combine(dir, fileName);
        var json = JsonSerializer.Serialize(record, JsonOptions.Default);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), ct);
    }

    public async Task<IReadOnlyList<RunHistoryRecord>> ListAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var dir = Path.Combine(_root, accountId);
        if (!Directory.Exists(dir)) return Array.Empty<RunHistoryRecord>();
        var list = new List<RunHistoryRecord>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            var rec = DeserializeRecord(json);
            if (rec is not null) list.Add(rec);
        }
        list.Sort((a, b) => b.EndedAtUtc.CompareTo(a.EndedAtUtc));
        return list;
    }

    private static RunHistoryRecord? DeserializeRecord(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        if (node is not JsonObject obj) return null;
        int version = obj["schemaVersion"]?.GetValue<int>() ?? 1;
        if (version == 1) { obj = MigrateV1ToV2(obj); version = 2; }
        if (version != RunHistoryRecord.CurrentSchemaVersion) return null;
        return JsonSerializer.Deserialize<RunHistoryRecord>(obj.ToJsonString(), JsonOptions.Default);
    }

    private static JsonObject MigrateV1ToV2(JsonObject obj)
    {
        obj["seenCardBaseIds"] = new JsonArray();
        obj["acquiredRelicIds"] = new JsonArray();
        obj["acquiredPotionIds"] = new JsonArray();
        obj["encounteredEnemyIds"] = new JsonArray();
        obj["schemaVersion"] = RunHistoryRecord.CurrentSchemaVersion;
        return obj;
    }
}
```

- [ ] **Step 12.4: Run test to verify it passes**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~FileHistoryRepositoryMigrationTests`
Expected: PASS。続いて全体:
Run: `dotnet test tests/Server.Tests`
Expected: PASS

- [ ] **Step 12.5: Commit**

```bash
git add src/Server/Services/FileBacked/FileHistoryRepository.cs tests/Server.Tests/FileBacked/FileHistoryRepositoryMigrationTests.cs
git commit -m "feat(server): migrate v1 history files by filling empty bestiary arrays"
git push
```

---

## Task 13: RunResultDto 拡張 + マッパー更新

**Files:**
- Modify: `src/Server/Dtos/RunResultDto.cs`
- Modify: `src/Server/Dtos/RunSnapshotDto.cs`（`ToResultDto`）
- Test: `tests/Server.Tests/Dtos/RunSnapshotDtoMapperTests.cs`（新規または既存に append）

- [ ] **Step 13.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Dtos;

public class RunSnapshotDtoMapperBestiaryTests
{
    [Fact]
    public void ToResultDto_MapsBestiaryFields()
    {
        var rec = new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: "a", RunId: "r", Outcome: RunProgress.Cleared,
            ActReached: 3, NodesVisited: 15, PlaySeconds: 1000,
            CharacterId: "default", FinalHp: 40, FinalMaxHp: 80, FinalGold: 200,
            FinalDeck: ImmutableArray<CardInstance>.Empty,
            FinalRelics: ImmutableArray<string>.Empty,
            EndedAtUtc: System.DateTimeOffset.UnixEpoch,
            SeenCardBaseIds: ImmutableArray.Create("strike", "defend"),
            AcquiredRelicIds: ImmutableArray.Create("burning_blood"),
            AcquiredPotionIds: ImmutableArray.Create("fire_potion"),
            EncounteredEnemyIds: ImmutableArray.Create("jaw_worm"));
        var dto = RunSnapshotDtoMapper.ToResultDto(rec);
        Assert.Equal(new[] { "strike", "defend" }, dto.SeenCardBaseIds);
        Assert.Equal(new[] { "burning_blood" }, dto.AcquiredRelicIds);
        Assert.Equal(new[] { "fire_potion" }, dto.AcquiredPotionIds);
        Assert.Equal(new[] { "jaw_worm" }, dto.EncounteredEnemyIds);
    }
}
```

- [ ] **Step 13.2: Run test to verify it fails**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~RunSnapshotDtoMapperBestiaryTests`
Expected: BUILD FAIL（DTO に該当プロパティ無し）

- [ ] **Step 13.3: Expand RunResultDto**

`src/Server/Dtos/RunResultDto.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RunResultCardDto(string Id, bool Upgraded);

public sealed record RunResultDto(
    int SchemaVersion,
    string AccountId,
    string RunId,
    string Outcome,
    int ActReached,
    int NodesVisited,
    long PlaySeconds,
    string CharacterId,
    int FinalHp,
    int FinalMaxHp,
    int FinalGold,
    IReadOnlyList<RunResultCardDto> FinalDeck,
    IReadOnlyList<string> FinalRelics,
    string EndedAtUtc,
    IReadOnlyList<string> SeenCardBaseIds,
    IReadOnlyList<string> AcquiredRelicIds,
    IReadOnlyList<string> AcquiredPotionIds,
    IReadOnlyList<string> EncounteredEnemyIds);
```

- [ ] **Step 13.4: Update ToResultDto mapper**

`src/Server/Dtos/RunSnapshotDto.cs` の `ToResultDto` 本体:

```csharp
public static RunResultDto ToResultDto(RunHistoryRecord rec)
{
    var deck = new List<RunResultCardDto>();
    foreach (var c in rec.FinalDeck) deck.Add(new RunResultCardDto(c.Id, c.Upgraded));
    return new RunResultDto(
        rec.SchemaVersion, rec.AccountId, rec.RunId,
        rec.Outcome.ToString(), rec.ActReached, rec.NodesVisited,
        rec.PlaySeconds, rec.CharacterId, rec.FinalHp, rec.FinalMaxHp, rec.FinalGold,
        deck, System.Linq.Enumerable.ToList(rec.FinalRelics),
        rec.EndedAtUtc.ToString("O"),
        SafeList(rec.SeenCardBaseIds),
        SafeList(rec.AcquiredRelicIds),
        SafeList(rec.AcquiredPotionIds),
        SafeList(rec.EncounteredEnemyIds));
}

private static IReadOnlyList<string> SafeList(System.Collections.Immutable.ImmutableArray<string> arr)
    => arr.IsDefault ? System.Array.Empty<string>() : (IReadOnlyList<string>)arr.ToArray();
```

（`SafeList` は同クラス内に `private static` で追加。既に `using System;` などがある前提。無ければ足す）

- [ ] **Step 13.5: Run tests**

Run: `dotnet test tests/Server.Tests`
Expected: PASS

- [ ] **Step 13.6: Commit**

```bash
git add src/Server/Dtos/RunResultDto.cs src/Server/Dtos/RunSnapshotDto.cs tests/Server.Tests/Dtos/RunSnapshotDtoMapperBestiaryTests.cs
git commit -m "feat(server): map bestiary fields in RunResultDto"
git push
```

---

## Task 14: RewardApplier の tracker 呼び出し

**Files:**
- Modify: `src/Core/Rewards/RewardApplier.cs`
- Test: `tests/Core.Tests/Rewards/RewardApplierBestiaryTests.cs`

- [ ] **Step 14.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardApplierBestiaryTests
{
    [Fact]
    public void ApplyPotion_TracksPotion()
    {
        var s = RunStateTestFixtures.NewSoloRunDefault() with
        {
            ActiveReward = new RewardState(
                Gold: 0, GoldClaimed: true,
                PotionId: "fire_potion", PotionClaimed: false,
                CardChoices: ImmutableArray<string>.Empty,
                CardStatus: CardRewardStatus.Claimed),
        };
        var after = RewardApplier.ApplyPotion(s);
        Assert.Contains("fire_potion", after.AcquiredPotionIds);
    }

    [Fact]
    public void ClaimRelic_TracksRelic()
    {
        var catalog = RoguelikeCardGame.Core.Tests.Infra.TestCatalog.Load();
        var relicId = System.Linq.Enumerable.First(catalog.Relics.Keys);
        var s = RunStateTestFixtures.NewSoloRunDefault() with
        {
            ActiveReward = new RewardState(
                Gold: 0, GoldClaimed: true,
                PotionId: null, PotionClaimed: true,
                CardChoices: ImmutableArray<string>.Empty,
                CardStatus: CardRewardStatus.Claimed,
                RelicId: relicId,
                RelicClaimed: false),
        };
        var after = RewardApplier.ClaimRelic(s, catalog);
        Assert.Contains(relicId, after.AcquiredRelicIds);
    }
}
```

- [ ] **Step 14.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~RewardApplierBestiaryTests`
Expected: FAIL

- [ ] **Step 14.3: Insert tracker calls**

`src/Core/Rewards/RewardApplier.cs`:

1. `ApplyPotion` メソッドの `return s with { ... }` を差し替え:

```csharp
public static RunState ApplyPotion(RunState s)
{
    var r = Require(s);
    if (r.PotionClaimed) throw new InvalidOperationException("Potion already claimed");
    if (r.PotionId is null) throw new InvalidOperationException("No potion to claim");

    int idx = -1;
    for (int i = 0; i < s.Potions.Length; i++) if (s.Potions[i] == "") { idx = i; break; }
    if (idx < 0) throw new InvalidOperationException("All potion slots are full");

    var newPotions = s.Potions.SetItem(idx, r.PotionId);
    var next = s with
    {
        Potions = newPotions,
        ActiveReward = r with { PotionClaimed = true },
    };
    return RoguelikeCardGame.Core.Bestiary.BestiaryTracker.NotePotionsAcquired(next, new[] { r.PotionId });
}
```

2. `ClaimRelic` メソッドの末尾:

```csharp
public static RunState ClaimRelic(RunState s, DataCatalog catalog)
{
    var r = Require(s);
    if (r.RelicId is null) throw new InvalidOperationException("No relic to claim");
    if (r.RelicClaimed) throw new InvalidOperationException("Relic already claimed");
    var newRelics = s.Relics.Append(r.RelicId).ToList();
    var s1 = s with
    {
        Relics = newRelics,
        ActiveReward = r with { RelicClaimed = true },
    };
    s1 = Relics.NonBattleRelicEffects.ApplyOnPickup(s1, r.RelicId, catalog);
    return RoguelikeCardGame.Core.Bestiary.BestiaryTracker.NoteRelicsAcquired(s1, new[] { r.RelicId });
}
```

- [ ] **Step 14.4: Run tests**

Run: `dotnet test tests/Core.Tests`
Expected: PASS

- [ ] **Step 14.5: Commit**

```bash
git add src/Core/Rewards/RewardApplier.cs tests/Core.Tests/Rewards/RewardApplierBestiaryTests.cs
git commit -m "feat(core): track relic/potion acquisition in RewardApplier"
git push
```

---

## Task 15: MerchantActions の tracker 呼び出し

**Files:**
- Modify: `src/Core/Merchant/MerchantActions.cs`
- Test: `tests/Core.Tests/Merchant/MerchantActionsBestiaryTests.cs`

- [ ] **Step 15.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Infra;
using RoguelikeCardGame.Core.Tests.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Merchant;

public class MerchantActionsBestiaryTests
{
    [Fact]
    public void BuyRelic_TracksRelic()
    {
        var catalog = TestCatalog.Load();
        var relicId = catalog.Relics.Keys.First();
        var inv = new MerchantInventory(
            Cards: ImmutableArray<MerchantOffer>.Empty,
            Relics: ImmutableArray.Create(new MerchantOffer("relic", relicId, Price: 0, Sold: false)),
            Potions: ImmutableArray<MerchantOffer>.Empty,
            DiscardSlotUsed: false, DiscardPrice: 0);
        var s = RunStateTestFixtures.NewSoloRunDefault() with { Gold = 999, ActiveMerchant = inv };
        var after = MerchantActions.BuyRelic(s, relicId, catalog);
        Assert.Contains(relicId, after.AcquiredRelicIds);
    }

    [Fact]
    public void BuyPotion_TracksPotion()
    {
        var catalog = TestCatalog.Load();
        var potionId = catalog.Potions.Keys.First();
        var inv = new MerchantInventory(
            Cards: ImmutableArray<MerchantOffer>.Empty,
            Relics: ImmutableArray<MerchantOffer>.Empty,
            Potions: ImmutableArray.Create(new MerchantOffer("potion", potionId, Price: 0, Sold: false)),
            DiscardSlotUsed: false, DiscardPrice: 0);
        var s = RunStateTestFixtures.NewSoloRunDefault() with { Gold = 999, ActiveMerchant = inv };
        var after = MerchantActions.BuyPotion(s, potionId, catalog);
        Assert.Contains(potionId, after.AcquiredPotionIds);
    }
}
```

※ `MerchantInventory` / `MerchantOffer` のコンストラクタ引数は既存定義に合わせる。既存テストを参考にシェイプを再確認（`src/Core/Merchant/MerchantInventory.cs`）。必要なら上記の `new MerchantInventory(...)` を既存の同等テストからコピー。

- [ ] **Step 15.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~MerchantActionsBestiaryTests`
Expected: FAIL

- [ ] **Step 15.3: Insert tracker calls**

`src/Core/Merchant/MerchantActions.cs`:

1. `BuyRelic` の末尾:

```csharp
public static RunState BuyRelic(RunState s, string relicId, DataCatalog catalog)
{
    var (inv, offer, idx) = RequireOffer(s, "relic", relicId);
    if (s.Gold < offer.Price)
        throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
    if (!catalog.TryGetRelic(relicId, out _))
        throw new ArgumentException($"unknown relic id \"{relicId}\"", nameof(relicId));
    var soldOffer = offer with { Sold = true };
    var s1 = s with
    {
        Gold = s.Gold - offer.Price,
        Relics = s.Relics.Append(relicId).ToList(),
        ActiveMerchant = inv with { Relics = inv.Relics.SetItem(idx, soldOffer) },
    };
    s1 = NonBattleRelicEffects.ApplyOnPickup(s1, relicId, catalog);
    return Bestiary.BestiaryTracker.NoteRelicsAcquired(s1, new[] { relicId });
}
```

2. `BuyPotion` の末尾:

```csharp
public static RunState BuyPotion(RunState s, string potionId, DataCatalog catalog)
{
    var (inv, offer, idx) = RequireOffer(s, "potion", potionId);
    if (s.Gold < offer.Price)
        throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
    if (!catalog.TryGetPotion(potionId, out _))
        throw new ArgumentException($"unknown potion id \"{potionId}\"", nameof(potionId));
    int slot = -1;
    for (int i = 0; i < s.Potions.Length; i++) if (s.Potions[i] == "") { slot = i; break; }
    if (slot < 0) throw new InvalidOperationException("All potion slots full");
    var soldOffer = offer with { Sold = true };
    var next = s with
    {
        Gold = s.Gold - offer.Price,
        Potions = s.Potions.SetItem(slot, potionId),
        ActiveMerchant = inv with { Potions = inv.Potions.SetItem(idx, soldOffer) },
    };
    return Bestiary.BestiaryTracker.NotePotionsAcquired(next, new[] { potionId });
}
```

（必要なら `using RoguelikeCardGame.Core.Bestiary;` を追加）

- [ ] **Step 15.4: Run tests**

Run: `dotnet test tests/Core.Tests`
Expected: PASS

- [ ] **Step 15.5: Commit**

```bash
git add src/Core/Merchant/MerchantActions.cs tests/Core.Tests/Merchant/MerchantActionsBestiaryTests.cs
git commit -m "feat(core): track relic/potion acquisition in MerchantActions"
git push
```

---

## Task 16: EventResolver の tracker 呼び出し

**Files:**
- Modify: `src/Core/Events/EventResolver.cs`
- Test: `tests/Core.Tests/Events/EventResolverBestiaryTests.cs`

- [ ] **Step 16.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Infra;
using RoguelikeCardGame.Core.Tests.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventResolverBestiaryTests
{
    [Fact]
    public void GainRelicRandom_TracksRelic()
    {
        var catalog = TestCatalog.Load();
        var rng = new SystemRng(42);
        var s = RunStateTestFixtures.NewSoloRunDefault() with
        {
            ActiveEvent = new EventInstance(
                EventId: "evt", Name: "n", Description: "d",
                Choices: ImmutableArray.Create(new EventChoice(
                    Label: "c",
                    Condition: null,
                    Effects: ImmutableArray.Create<EventEffect>(
                        new EventEffect.GainRelicRandom(Cards.CardRarity.Common)))),
                ChosenIndex: null),
        };
        var after = EventResolver.ApplyChoice(s, 0, catalog, rng);
        Assert.NotEmpty(after.AcquiredRelicIds);
        Assert.Contains(after.AcquiredRelicIds[0], after.Relics);
    }

    [Fact]
    public void GrantCardReward_TracksCardChoices()
    {
        var catalog = TestCatalog.Load();
        var rng = new SystemRng(7);
        var s = RunStateTestFixtures.NewSoloRunDefault() with
        {
            ActiveEvent = new EventInstance(
                EventId: "evt", Name: "n", Description: "d",
                Choices: ImmutableArray.Create(new EventChoice(
                    Label: "c",
                    Condition: null,
                    Effects: ImmutableArray.Create<EventEffect>(new EventEffect.GrantCardReward()))),
                ChosenIndex: null),
        };
        var after = EventResolver.ApplyChoice(s, 0, catalog, rng);
        Assert.NotNull(after.ActiveReward);
        foreach (var cardId in after.ActiveReward!.CardChoices)
            Assert.Contains(cardId, after.SeenCardBaseIds);
    }
}
```

※ `EventInstance` / `EventChoice` / `EventEffect` のコンストラクタは既存シェイプに合わせる（`src/Core/Events/*`）。

- [ ] **Step 16.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~EventResolverBestiaryTests`
Expected: FAIL

- [ ] **Step 16.3: Insert tracker calls**

`src/Core/Events/EventResolver.cs`:

1. `GainRelic` の末尾:

```csharp
private static RunState GainRelic(RunState s, CardRarity rarity, DataCatalog catalog, IRng rng)
{
    var pool = catalog.Relics.Values
        .Where(r => r.Rarity == rarity && !s.Relics.Contains(r.Id))
        .OrderBy(r => r.Id)
        .ToArray();
    if (pool.Length == 0) return s;
    var chosen = pool[rng.NextInt(0, pool.Length)];
    var newRelics = s.Relics.Append(chosen.Id).ToList();
    var s1 = s with { Relics = newRelics };
    s1 = NonBattleRelicEffects.ApplyOnPickup(s1, chosen.Id, catalog);
    return Bestiary.BestiaryTracker.NoteRelicsAcquired(s1, new[] { chosen.Id });
}
```

2. `GrantCardReward` の末尾:

```csharp
private static RunState GrantCardReward(RunState s, DataCatalog catalog, IRng rng)
{
    var rt = catalog.RewardTables["act1"];
    var excl = ImmutableArray.CreateRange(s.Deck.Select(c => c.Id));
    var (reward, newRngState) = RewardGenerator.Generate(
        new RewardContext.FromEnemy(new Enemy.EnemyPool(s.CurrentAct, Enemy.EnemyTier.Weak)),
        s.RewardRngState, excl, rt, catalog, rng);
    var cardOnly = new RewardState(
        Gold: 0, GoldClaimed: true,
        PotionId: null, PotionClaimed: true,
        CardChoices: reward.CardChoices,
        CardStatus: CardRewardStatus.Pending);
    var next = s with { ActiveReward = cardOnly, RewardRngState = newRngState };
    return Bestiary.BestiaryTracker.NoteCardsSeen(next, reward.CardChoices);
}
```

（`using RoguelikeCardGame.Core.Bestiary;` を追加）

- [ ] **Step 16.4: Run tests**

Run: `dotnet test tests/Core.Tests`
Expected: PASS

- [ ] **Step 16.5: Commit**

```bash
git add src/Core/Events/EventResolver.cs tests/Core.Tests/Events/EventResolverBestiaryTests.cs
git commit -m "feat(core): track relic gain and card reward choices in EventResolver"
git push
```

---

## Task 17: ActStartActions の tracker 呼び出し

**Files:**
- Modify: `src/Core/Run/ActStartActions.cs`
- Test: `tests/Core.Tests/Run/ActStartActionsBestiaryTests.cs`

- [ ] **Step 17.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Infra;
using RoguelikeCardGame.Core.Tests.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActStartActionsBestiaryTests
{
    [Fact]
    public void ChooseRelic_TracksRelic()
    {
        var catalog = TestCatalog.Load();
        var relicId = catalog.Relics.Keys.First();
        var s = RunStateTestFixtures.NewSoloRunDefault() with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create(relicId, catalog.Relics.Keys.Skip(1).First(), catalog.Relics.Keys.Skip(2).First())),
        };
        var after = ActStartActions.ChooseRelic(s, relicId, catalog);
        Assert.Contains(relicId, after.AcquiredRelicIds);
    }
}
```

- [ ] **Step 17.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~ActStartActionsBestiaryTests`
Expected: FAIL

- [ ] **Step 17.3: Insert tracker call**

`src/Core/Run/ActStartActions.cs` `ChooseRelic` 末尾:

```csharp
public static RunState ChooseRelic(RunState state, string relicId, DataCatalog catalog)
{
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(relicId);
    ArgumentNullException.ThrowIfNull(catalog);
    if (state.ActiveActStartRelicChoice is null)
        throw new InvalidOperationException("ActiveActStartRelicChoice is null");
    if (!state.ActiveActStartRelicChoice.RelicIds.Contains(relicId))
        throw new ArgumentException($"relicId '{relicId}' is not among current choices", nameof(relicId));

    var newRelics = state.Relics.Append(relicId).ToList();
    var next = state with
    {
        Relics = newRelics,
        ActiveActStartRelicChoice = null,
    };
    next = NonBattleRelicEffects.ApplyOnPickup(next, relicId, catalog);
    return Bestiary.BestiaryTracker.NoteRelicsAcquired(next, new[] { relicId });
}
```

（`using RoguelikeCardGame.Core.Bestiary;` を追加）

- [ ] **Step 17.4: Run tests**

Run: `dotnet test tests/Core.Tests`
Expected: PASS

- [ ] **Step 17.5: Commit**

```bash
git add src/Core/Run/ActStartActions.cs tests/Core.Tests/Run/ActStartActionsBestiaryTests.cs
git commit -m "feat(core): track act-start relic choice in ActStartActions"
git push
```

---

## Task 18: BattlePlaceholder.Start の tracker 呼び出し

**Files:**
- Modify: `src/Core/Battle/BattlePlaceholder.cs`
- Test: `tests/Core.Tests/Battle/BattlePlaceholderBestiaryTests.cs`

- [ ] **Step 18.1: Write the failing test**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Infra;
using RoguelikeCardGame.Core.Tests.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle;

public class BattlePlaceholderBestiaryTests
{
    [Fact]
    public void Start_TracksAllEnemyIds()
    {
        var catalog = TestCatalog.Load();
        var s = RunStateTestFixtures.NewSoloRunDefault();
        var pool = new EnemyPool(1, EnemyTier.Weak);
        var rng = new SystemRng(42);
        var after = BattlePlaceholder.Start(s, pool, catalog, rng);
        var enc = catalog.Encounters[after.ActiveBattle!.EncounterId];
        foreach (var eid in enc.EnemyIds)
            Assert.Contains(eid, after.EncounteredEnemyIds);
    }
}
```

- [ ] **Step 18.2: Run test to verify it fails**

Run: `dotnet test tests/Core.Tests --filter FullyQualifiedName~BattlePlaceholderBestiaryTests`
Expected: FAIL

- [ ] **Step 18.3: Insert tracker call**

`src/Core/Battle/BattlePlaceholder.cs` `Start` 最後の `return` を変更:

```csharp
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
        int hp = def.HpMin + rng.NextInt(0, def.HpMax - def.HpMin + 1);
        enemies.Add(new EnemyInstance(eid, hp, hp, def.InitialMoveId));
    }
    var battle = new BattleState(encounterId, enemies.ToImmutable(), BattleOutcome.Pending);
    var next = selector(state, queueAfter) with { ActiveBattle = battle };
    return Bestiary.BestiaryTracker.NoteEnemiesEncountered(next, encounter.EnemyIds);
}
```

（`using RoguelikeCardGame.Core.Bestiary;` を追加）

- [ ] **Step 18.4: Run tests**

Run: `dotnet test tests/Core.Tests`
Expected: PASS

- [ ] **Step 18.5: Commit**

```bash
git add src/Core/Battle/BattlePlaceholder.cs tests/Core.Tests/Battle/BattlePlaceholderBestiaryTests.cs
git commit -m "feat(core): track encountered enemies in BattlePlaceholder.Start"
git push
```

---

## Task 19: RunsController — 報酬カード choices を tracker に通す

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`
- Test: `tests/Server.Tests/Controllers/RunsControllerBestiaryTests.cs`

**背景:** `PostBattleWin` で通常エンカウンター報酬を生成した直後、`updated` の `CardChoices` を RunState の `SeenCardBaseIds` に追加する必要がある。ボス報酬の場合は BossRewardFlow が `CardChoices` を設定しないので no-op。

- [ ] **Step 19.1: Write the failing test**

```csharp
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class RunsControllerBestiaryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RunsControllerBestiaryTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task AfterBattleWin_CardChoicesAddedToSeenCards()
    {
        var accountId = await TestAccounts.CreateAsync(_factory);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Account-Id", accountId);

        // 新規ラン → エネミーマスへ移動 → battle/win
        var snap = await client.PostAsJsonAsync("/api/v1/runs/new?force=true", new { });
        snap.EnsureSuccessStatusCode();
        await RunFlowHelpers.NavigateToFirstEnemy(client);
        var winResp = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win", new { elapsedSeconds = 0 });
        winResp.EnsureSuccessStatusCode();

        // current 取得して RunState.SeenCardBaseIds と ActiveReward.CardChoices を突き合わせ
        var cur = await client.GetFromJsonAsync<RunSnapshotDto>("/api/v1/runs/current");
        var choices = cur!.Run.ActiveReward!.CardChoices;
        // Seen は Snapshot DTO に露出していないため、結果を abandon → history 経由で確認
        var abandon = await client.PostAsJsonAsync("/api/v1/runs/current/abandon", new { elapsedSeconds = 0 });
        var result = await abandon.Content.ReadFromJsonAsync<RunResultDto>();
        foreach (var id in choices)
            Assert.Contains(id, result!.SeenCardBaseIds);
    }
}
```

※ `RunFlowHelpers.NavigateToFirstEnemy` は既存 test infra が無ければ作成（マップ fetch → Enemy タイルを移動）。既存の Server.Tests に類似ヘルパがある想定。

- [ ] **Step 19.2: Run test to verify it fails**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~RunsControllerBestiaryTests`
Expected: FAIL

- [ ] **Step 19.3: Insert tracker call in PostBattleWin**

`src/Server/Controllers/RunsController.cs`: `updated = afterWin with { ... };` の直後（`await _saves.SaveAsync(...)` の前）に以下を挿入:

```csharp
if (reward.CardChoices.Length > 0)
    updated = Core.Bestiary.BestiaryTracker.NoteCardsSeen(updated, reward.CardChoices);
```

`using RoguelikeCardGame.Core.Bestiary;` も `using` ブロックに追加。

- [ ] **Step 19.4: Run tests**

Run: `dotnet test tests/Server.Tests`
Expected: PASS

- [ ] **Step 19.5: Commit**

```bash
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/RunsControllerBestiaryTests.cs
git commit -m "feat(server): track reward card choices via BestiaryTracker in RunsController"
git push
```

---

## Task 20: MerchantController — 在庫カードを tracker に通す

**Files:**
- Modify: `src/Server/Controllers/MerchantController.cs`
- Test: `tests/Server.Tests/Controllers/MerchantControllerBestiaryTests.cs`

**背景:** `MerchantController.Enter`（または該当する生成エンドポイント）で `ActiveMerchant` を設定する直前、`inv.Cards` の ID を `BestiaryTracker.NoteCardsSeen` に通す。

- [ ] **Step 20.1: Read MerchantController**

`src/Server/Controllers/MerchantController.cs` を開き、`ActiveMerchant` を設定している箇所（`s with { ActiveMerchant = inv, ... }` などの記述）を特定する。以下のパッチはその箇所の直前に挿入する形を想定。

- [ ] **Step 20.2: Write the failing test**

```csharp
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class MerchantControllerBestiaryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public MerchantControllerBestiaryTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task MerchantEnter_InventoryCards_AddedToSeenCards()
    {
        var accountId = await TestAccounts.CreateAsync(_factory);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Account-Id", accountId);

        await client.PostAsJsonAsync("/api/v1/runs/new?force=true", new { });
        await RunFlowHelpers.NavigateToFirstMerchant(client);

        var cur = await client.GetFromJsonAsync<RunSnapshotDto>("/api/v1/runs/current");
        var invCards = cur!.Run.ActiveMerchant!.Cards;

        var abandon = await client.PostAsJsonAsync("/api/v1/runs/current/abandon", new { elapsedSeconds = 0 });
        var result = await abandon.Content.ReadFromJsonAsync<RunResultDto>();
        foreach (var offer in invCards)
            Assert.Contains(offer.Id, result!.SeenCardBaseIds);
    }
}
```

（`RunFlowHelpers.NavigateToFirstMerchant` は既存 infra に無ければ新設。）

- [ ] **Step 20.3: Run test to verify it fails**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~MerchantControllerBestiaryTests`
Expected: FAIL

- [ ] **Step 20.4: Insert tracker call**

`src/Server/Controllers/MerchantController.cs`: 在庫生成後 `state with { ActiveMerchant = inv, ... }` した直後（`SaveAsync` 前）に以下を挿入:

```csharp
updated = Core.Bestiary.BestiaryTracker.NoteCardsSeen(updated, inv.Cards.Select(o => o.Id));
```

（変数名は既存コードに合わせて調整。`using RoguelikeCardGame.Core.Bestiary;` を追加）

- [ ] **Step 20.5: Run tests**

Run: `dotnet test tests/Server.Tests`
Expected: PASS

- [ ] **Step 20.6: Commit**

```bash
git add src/Server/Controllers/MerchantController.cs tests/Server.Tests/Controllers/MerchantControllerBestiaryTests.cs
git commit -m "feat(server): track merchant inventory cards via BestiaryTracker"
git push
```

---

## Task 21: RunsController — ラン終了時に bestiary.MergeAsync

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`
- Modify: `src/Server/Controllers/DebugController.cs`（GameOver 分岐があるため）
- Test: `tests/Server.Tests/Controllers/RunsControllerMergeTests.cs`

- [ ] **Step 21.1: Write the failing test**

```csharp
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class RunsControllerMergeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public RunsControllerMergeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Abandon_MergesBestiary()
    {
        var accountId = await TestAccounts.CreateAsync(_factory);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Account-Id", accountId);

        await client.PostAsJsonAsync("/api/v1/runs/new?force=true", new { });
        await client.PostAsJsonAsync("/api/v1/runs/current/abandon", new { elapsedSeconds = 0 });

        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IBestiaryRepository>();
        var loaded = await repo.LoadAsync(accountId, default);
        Assert.NotEmpty(loaded.DiscoveredCardBaseIds); // 初期デッキ分
    }
}
```

- [ ] **Step 21.2: Run test to verify it fails**

Run: `dotnet test tests/Server.Tests --filter FullyQualifiedName~RunsControllerMergeTests`
Expected: FAIL

- [ ] **Step 21.3: Inject IBestiaryRepository and call MergeAsync**

`src/Server/Controllers/RunsController.cs`:

1. フィールド / コンストラクタに追加:

```csharp
private readonly IBestiaryRepository _bestiary;

public RunsController(IAccountRepository accounts, ISaveRepository saves, RunStartService runStart,
    DataCatalog data, IHistoryRepository history, IBestiaryRepository bestiary)
{
    _accounts = accounts;
    _saves = saves;
    _runStart = runStart;
    _data = data;
    _history = history;
    _bestiary = bestiary;
}
```

2. `PostBattleWin` の `Cleared` 分岐で `_history.AppendAsync(accountId, rec, ct);` の直後に:

```csharp
await _bestiary.MergeAsync(accountId, rec, ct);
```

3. `PostAbandon` で `_history.AppendAsync(accountId, rec, ct);` の直後にも同じ呼び出しを追加。

4. DebugController に GameOver 分岐がある場合（Grep `RunHistoryBuilder.From` したところ 1 箇所）、同様に `_bestiary.MergeAsync` を呼ぶ。`DebugController` のコンストラクタにも `IBestiaryRepository` を注入。

- [ ] **Step 21.4: Run tests**

Run: `dotnet test tests/Server.Tests`
Expected: PASS

- [ ] **Step 21.5: Commit**

```bash
git add src/Server/Controllers/RunsController.cs src/Server/Controllers/DebugController.cs tests/Server.Tests/Controllers/RunsControllerMergeTests.cs
git commit -m "feat(server): merge bestiary on Cleared/Abandon/GameOver"
git push
```

---

## Task 22: Program.cs DI 登録（確認）

**Files:**
- Modify: `src/Server/Program.cs`

- [ ] **Step 22.1: Verify DI is registered**

Task 11.5 で既に以下が追加されているはず:

```csharp
builder.Services.AddSingleton<IBestiaryRepository, FileBestiaryRepository>();
```

無ければ Task 11.5 の内容をここで反映する。

- [ ] **Step 22.2: Run all server tests**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 22.3: Commit (if anything changed)**

```bash
git add src/Server/Program.cs
git commit -m "chore(server): confirm FileBestiaryRepository DI registration"
git push
```

---

## Task 23: Client API 層（bestiary.ts + types.ts）

**Files:**
- Modify: `src/Client/src/api/types.ts`
- Create: `src/Client/src/api/bestiary.ts`
- Test: `src/Client/src/api/bestiary.test.ts`

- [ ] **Step 23.1: Write the failing test**

```typescript
// src/Client/src/api/bestiary.test.ts
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { fetchBestiary } from './bestiary'

const mockFetch = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', mockFetch)
  mockFetch.mockReset()
})

describe('fetchBestiary', () => {
  it('GETs /bestiary with X-Account-Id header', async () => {
    mockFetch.mockResolvedValueOnce(new Response(JSON.stringify({
      schemaVersion: 1,
      discoveredCardBaseIds: [],
      discoveredRelicIds: [],
      discoveredPotionIds: [],
      encounteredEnemyIds: [],
      allKnownCardBaseIds: ['strike'],
      allKnownRelicIds: [],
      allKnownPotionIds: [],
      allKnownEnemyIds: [],
    }), { status: 200 }))
    const dto = await fetchBestiary('acct-1')
    expect(mockFetch).toHaveBeenCalledTimes(1)
    const [url, init] = mockFetch.mock.calls[0]
    expect(String(url)).toMatch(/\/bestiary$/)
    expect((init.headers as Record<string, string>)['X-Account-Id']).toBe('acct-1')
    expect(dto.allKnownCardBaseIds).toEqual(['strike'])
  })
})
```

- [ ] **Step 23.2: Run test to verify it fails**

Run: `cd src/Client && npm test -- bestiary`
Expected: FAIL（`fetchBestiary` が存在しない）

- [ ] **Step 23.3: Add BestiaryDto type + extend RunResultDto**

`src/Client/src/api/types.ts` を編集:

1. `RunResultDto` に 4 フィールドを追加（既存の `endedAtUtc` の直後）:

```typescript
export type RunResultDto = {
  schemaVersion: number
  accountId: string
  runId: string
  outcome: RunProgress
  actReached: number
  nodesVisited: number
  playSeconds: number
  characterId: string
  finalHp: number
  finalMaxHp: number
  finalGold: number
  finalDeck: RunResultCardDto[]
  finalRelics: string[]
  endedAtUtc: string
  seenCardBaseIds: string[]
  acquiredRelicIds: string[]
  acquiredPotionIds: string[]
  encounteredEnemyIds: string[]
}
```

2. ファイル末尾に `BestiaryDto` を追加:

```typescript
export type BestiaryDto = {
  schemaVersion: number
  discoveredCardBaseIds: string[]
  discoveredRelicIds: string[]
  discoveredPotionIds: string[]
  encounteredEnemyIds: string[]
  allKnownCardBaseIds: string[]
  allKnownRelicIds: string[]
  allKnownPotionIds: string[]
  allKnownEnemyIds: string[]
}
```

- [ ] **Step 23.4: Implement fetchBestiary**

```typescript
// src/Client/src/api/bestiary.ts
import { apiRequest } from './client'
import type { BestiaryDto } from './types'

export async function fetchBestiary(accountId: string): Promise<BestiaryDto> {
  return apiRequest<BestiaryDto>('GET', '/bestiary', { accountId })
}
```

※ 呼び出し規約は既存 `src/Client/src/api/history.ts` を参照。もし `apiRequest` のシグネチャが異なる場合は history.ts と同じパターンを完全にコピーする:

```typescript
// history.ts が使っている形式の例
import { apiRequest } from './client'
import type { RunResultDto } from './types'
export async function listHistory(accountId: string): Promise<RunResultDto[]> {
  return apiRequest<RunResultDto[]>('GET', '/history', { accountId })
}
```

- [ ] **Step 23.5: Run test**

Run: `cd src/Client && npm test`
Expected: PASS

- [ ] **Step 23.6: Commit**

```bash
git add src/Client/src/api/bestiary.ts src/Client/src/api/bestiary.test.ts src/Client/src/api/types.ts
git commit -m "feat(client): add fetchBestiary API and BestiaryDto/RunResultDto extension"
git push
```

---

## Task 24: AchievementsScreen 実装

**Files:**
- Create: `src/Client/src/screens/AchievementsScreen.tsx`
- Test: `src/Client/src/screens/AchievementsScreen.test.tsx`

- [ ] **Step 24.1: Write the failing test**

```typescript
// src/Client/src/screens/AchievementsScreen.test.tsx
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { AchievementsScreen } from './AchievementsScreen'
import * as bestiaryApi from '../api/bestiary'
import * as historyApi from '../api/history'
import type { BestiaryDto, RunResultDto } from '../api/types'

const emptyBestiary: BestiaryDto = {
  schemaVersion: 1,
  discoveredCardBaseIds: ['strike'],
  discoveredRelicIds: [],
  discoveredPotionIds: [],
  encounteredEnemyIds: [],
  allKnownCardBaseIds: ['defend', 'strike', 'zap'],
  allKnownRelicIds: ['burning_blood'],
  allKnownPotionIds: ['fire_potion'],
  allKnownEnemyIds: ['jaw_worm'],
}

const oneRun: RunResultDto = {
  schemaVersion: 2, accountId: 'a', runId: 'r1', outcome: 'Cleared',
  actReached: 3, nodesVisited: 15, playSeconds: 900, characterId: 'default',
  finalHp: 40, finalMaxHp: 80, finalGold: 200,
  finalDeck: [{ id: 'strike', upgraded: false }], finalRelics: ['burning_blood'],
  endedAtUtc: '2026-04-20T12:00:00Z',
  seenCardBaseIds: ['strike'], acquiredRelicIds: ['burning_blood'],
  acquiredPotionIds: [], encounteredEnemyIds: ['jaw_worm'],
}

describe('AchievementsScreen', () => {
  beforeEach(() => {
    vi.spyOn(bestiaryApi, 'fetchBestiary').mockResolvedValue(emptyBestiary)
    vi.spyOn(historyApi, 'listHistory').mockResolvedValue([oneRun])
  })

  it('fetches bestiary and history on mount in parallel', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await waitFor(() => expect(bestiaryApi.fetchBestiary).toHaveBeenCalledWith('a'))
    expect(historyApi.listHistory).toHaveBeenCalledWith('a')
  })

  it('cards tab shows discovered and undiscovered with count header', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await screen.findByText(/1 \/ 3 発見/)
    // 発見済みは ID を「✓ strike (strike)」形式（display_name が ID なので）
    expect(screen.getByText(/✓.*strike/)).toBeInTheDocument()
    // 未発見は ??? (id)
    expect(screen.getByText(/\?\?\?.*\(defend\)/)).toBeInTheDocument()
  })

  it('history tab shows empty message when no history', async () => {
    vi.spyOn(historyApi, 'listHistory').mockResolvedValueOnce([])
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await screen.findByRole('tab', { name: /履歴/ })
    fireEvent.click(screen.getByRole('tab', { name: /履歴/ }))
    await screen.findByText('履歴なし')
  })

  it('history row expands on click to show acquired/encountered sets', async () => {
    render(<AchievementsScreen accountId="a" onBack={() => { }} />)
    await screen.findByRole('tab', { name: /履歴/ })
    fireEvent.click(screen.getByRole('tab', { name: /履歴/ }))
    const row = await screen.findByText(/Cleared.*Act3/)
    fireEvent.click(row)
    expect(screen.getByText(/jaw_worm/)).toBeInTheDocument()
    expect(screen.getByText(/burning_blood/)).toBeInTheDocument()
  })

  it('back button fires onBack', async () => {
    const onBack = vi.fn()
    render(<AchievementsScreen accountId="a" onBack={onBack} />)
    await screen.findByText('戻る')
    fireEvent.click(screen.getByText('戻る'))
    expect(onBack).toHaveBeenCalled()
  })
})
```

- [ ] **Step 24.2: Run test to verify it fails**

Run: `cd src/Client && npm test -- AchievementsScreen`
Expected: FAIL（モジュール未定義）

- [ ] **Step 24.3: Implement AchievementsScreen**

```tsx
// src/Client/src/screens/AchievementsScreen.tsx
import { useEffect, useState } from 'react'
import { fetchBestiary } from '../api/bestiary'
import { listHistory } from '../api/history'
import type { BestiaryDto, RunResultDto } from '../api/types'
import { Button } from '../components/Button'

type Tab = 'cards' | 'relics' | 'potions' | 'enemies' | 'history'

type Props = {
  accountId: string
  onBack: () => void
}

export function AchievementsScreen({ accountId, onBack }: Props) {
  const [tab, setTab] = useState<Tab>('cards')
  const [bestiary, setBestiary] = useState<BestiaryDto | null>(null)
  const [history, setHistory] = useState<RunResultDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([fetchBestiary(accountId), listHistory(accountId)])
      .then(([b, h]) => {
        if (cancelled) return
        setBestiary(b)
        setHistory(h)
      })
      .catch(() => { if (!cancelled) setError('読み込みに失敗しました') })
    return () => { cancelled = true }
  }, [accountId])

  if (error) return (
    <main className="achievements">
      <p>{error}</p>
      <Button variant="secondary" onClick={onBack}>戻る</Button>
    </main>
  )
  if (bestiary === null || history === null) return (
    <main className="achievements"><p>読み込み中...</p></main>
  )

  return (
    <main className="achievements">
      <header className="achievements__tabs" role="tablist">
        <TabButton label="カード" active={tab === 'cards'} onClick={() => setTab('cards')} />
        <TabButton label="レリック" active={tab === 'relics'} onClick={() => setTab('relics')} />
        <TabButton label="ポーション" active={tab === 'potions'} onClick={() => setTab('potions')} />
        <TabButton label="モンスター" active={tab === 'enemies'} onClick={() => setTab('enemies')} />
        <TabButton label="履歴" active={tab === 'history'} onClick={() => setTab('history')} />
      </header>
      <section className="achievements__content">
        {tab === 'cards' && <BestiaryList
          allIds={bestiary.allKnownCardBaseIds}
          discovered={new Set(bestiary.discoveredCardBaseIds)}
          unknownLabel="???" />}
        {tab === 'relics' && <BestiaryList
          allIds={bestiary.allKnownRelicIds}
          discovered={new Set(bestiary.discoveredRelicIds)}
          unknownLabel="???" />}
        {tab === 'potions' && <BestiaryList
          allIds={bestiary.allKnownPotionIds}
          discovered={new Set(bestiary.discoveredPotionIds)}
          unknownLabel="???" />}
        {tab === 'enemies' && <BestiaryList
          allIds={bestiary.allKnownEnemyIds}
          discovered={new Set(bestiary.encounteredEnemyIds)}
          unknownLabel="???" />}
        {tab === 'history' && <HistoryList history={history} />}
      </section>
      <footer className="achievements__footer">
        <Button variant="secondary" onClick={onBack}>戻る</Button>
      </footer>
    </main>
  )
}

function TabButton({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button role="tab" aria-selected={active} onClick={onClick}
      className={'achievements__tab' + (active ? ' achievements__tab--active' : '')}>
      {label}
    </button>
  )
}

function BestiaryList({ allIds, discovered, unknownLabel }: {
  allIds: string[]; discovered: Set<string>; unknownLabel: string
}) {
  return (
    <div>
      <p className="achievements__count">{discovered.size} / {allIds.length} 発見</p>
      <ul className="achievements__list">
        {allIds.map(id => (
          <li key={id} className="achievements__item">
            {discovered.has(id) ? <span>✓ {id} ({id})</span> : <span>{unknownLabel} ({id})</span>}
          </li>
        ))}
      </ul>
    </div>
  )
}

function HistoryList({ history }: { history: RunResultDto[] }) {
  const [expanded, setExpanded] = useState<string | null>(null)
  if (history.length === 0) return <p>履歴なし</p>
  return (
    <ul className="achievements__history">
      {history.map(run => (
        <li key={run.runId}>
          <button className="achievements__history-summary"
            onClick={() => setExpanded(expanded === run.runId ? null : run.runId)}>
            [{run.outcome}] Act{run.actReached} / {formatPlayTime(run.playSeconds)} / {run.endedAtUtc}
          </button>
          {expanded === run.runId && <HistoryDetail run={run} />}
        </li>
      ))}
    </ul>
  )
}

function HistoryDetail({ run }: { run: RunResultDto }) {
  return (
    <div className="achievements__history-detail">
      <p>最終 HP: {run.finalHp}/{run.finalMaxHp}</p>
      <p>最終 Gold: {run.finalGold}</p>
      <p>最終デッキ: {run.finalDeck.length === 0
        ? '（なし）'
        : run.finalDeck.map(c => c.id + (c.upgraded ? '+' : '')).join(', ')}</p>
      <p>最終レリック: {run.finalRelics.length === 0 ? '（なし）' : run.finalRelics.join(', ')}</p>
      <p>見たカード: {run.seenCardBaseIds.length === 0 ? '（なし）' : run.seenCardBaseIds.join(', ')}</p>
      <p>入手レリック: {run.acquiredRelicIds.length === 0 ? '（なし）' : run.acquiredRelicIds.join(', ')}</p>
      <p>入手ポーション: {run.acquiredPotionIds.length === 0 ? '（なし）' : run.acquiredPotionIds.join(', ')}</p>
      <p>遭遇敵: {run.encounteredEnemyIds.length === 0 ? '（なし）' : run.encounteredEnemyIds.join(', ')}</p>
    </div>
  )
}

function formatPlayTime(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
}
```

- [ ] **Step 24.4: Run test to verify it passes**

Run: `cd src/Client && npm test -- AchievementsScreen`
Expected: PASS

- [ ] **Step 24.5: Commit**

```bash
git add src/Client/src/screens/AchievementsScreen.tsx src/Client/src/screens/AchievementsScreen.test.tsx
git commit -m "feat(client): add AchievementsScreen with 5 tabs (4 bestiary + history)"
git push
```

---

## Task 25: MainMenuScreen + App.tsx 導線

**Files:**
- Modify: `src/Client/src/screens/MainMenuScreen.tsx`
- Modify: `src/Client/src/App.tsx`
- Test: `src/Client/src/screens/MainMenuScreen.test.tsx`（既存 or 新規）

- [ ] **Step 25.1: Write the failing test**

```typescript
// src/Client/src/screens/MainMenuScreen.test.tsx (append / create)
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { MainMenuScreen } from './MainMenuScreen'
import { AccountContext } from '../context/AccountContext'

describe('MainMenuScreen 実績ボタン', () => {
  it('onAchievements fires when clicked', () => {
    const onAchievements = vi.fn()
    render(
      <AccountContext.Provider value={{ accountId: 'a', login: () => { }, logout: () => { } }}>
        <MainMenuScreen
          onOpenSettings={() => { }}
          onLogout={() => { }}
          onAchievements={onAchievements} />
      </AccountContext.Provider>
    )
    fireEvent.click(screen.getByText('実績'))
    expect(onAchievements).toHaveBeenCalled()
  })
})
```

※ `AccountContext` の shape は既存に合わせる。既存テストがあればそれを参照。

- [ ] **Step 25.2: Run test to verify it fails**

Run: `cd src/Client && npm test -- MainMenuScreen`
Expected: FAIL（`onAchievements` prop 未対応、現状は dialog 表示）

- [ ] **Step 25.3: Update MainMenuScreen**

`src/Client/src/screens/MainMenuScreen.tsx`:

1. Props 型に `onAchievements` を追加:

```tsx
type Props = {
  onOpenSettings: () => void
  onLogout: () => void
  onStartRun?: (snapshot: RunSnapshotDto) => void
  hasCurrentRun?: boolean
  onAchievements: () => void
}
```

2. コンポーネント引数を `{ ..., onAchievements }` に追加。

3. `ComingSoonKind` 型から `'achievements'` を削除（残るのは `'multi' | 'quit' | null`）:

```tsx
type ComingSoonKind = 'multi' | 'quit' | null
```

4. 実績ボタンのクリックハンドラを変更:

```tsx
<Button onClick={onAchievements}>実績</Button>
```

- [ ] **Step 25.4: Update App.tsx**

`src/Client/src/App.tsx`:

1. `Screen` union に `{ kind: 'achievements' }` を追加:

```tsx
type Screen =
  | { kind: 'bootstrapping' }
  | { kind: 'login' }
  | { kind: 'main-menu'; hasCurrentRun?: boolean }
  | { kind: 'settings' }
  | { kind: 'achievements' }
  | { kind: 'map'; snapshot: RunSnapshotDto }
  | { kind: 'run-result'; result: RunResultDto }
  | { kind: 'bootstrap-error'; message: string }
```

2. import 追加:

```tsx
import { AchievementsScreen } from './screens/AchievementsScreen'
```

3. MainMenuScreen 呼び出しに `onAchievements` を渡す:

```tsx
<MainMenuScreen
  hasCurrentRun={screen.hasCurrentRun}
  onOpenSettings={() => setScreen({ kind: 'settings' })}
  onLogout={() => { logout(); setScreen({ kind: 'login' }) }}
  onStartRun={(snap) => setScreen({ kind: 'map', snapshot: snap })}
  onAchievements={() => setScreen({ kind: 'achievements' })}
/>
```

4. レンダ分岐に achievements を追加（`settings` 分岐の直前）:

```tsx
if (screen.kind === 'achievements') {
  return (
    <AchievementsScreen
      accountId={accountId!}
      onBack={() => setScreen({ kind: 'main-menu' })} />
  )
}
```

- [ ] **Step 25.5: Run all client tests**

Run: `cd src/Client && npm test`
Expected: PASS

- [ ] **Step 25.6: Manual smoke test**

1. `dotnet run --project src/Server`（別ウィンドウ）
2. `cd src/Client && npm run dev`（別ウィンドウ）
3. ブラウザでログイン → メインメニュー → 「実績」クリック → AchievementsScreen へ遷移
4. 5 タブを切り替え、カード / レリック / ポーション / モンスターの ✓ と ??? が混在表示されること
5. 履歴タブで既存ランが表示 or 「履歴なし」
6. 新規ランを 1 回完走 → 実績画面で履歴が増え、発見済み ID も反映されること

- [ ] **Step 25.7: Commit**

```bash
git add src/Client/src/screens/MainMenuScreen.tsx src/Client/src/App.tsx src/Client/src/screens/MainMenuScreen.test.tsx
git commit -m "feat(client): wire MainMenuScreen 実績 button to AchievementsScreen"
git push
```

---

## 最終確認

- [ ] **Run all tests**

```bash
dotnet test
cd src/Client && npm test
```

Expected: 全テスト PASS。

- [ ] **Manual E2E — Done 判定**

spec の「Done 判定」セクションを満たすことを手動確認:

1. `dotnet build` / `dotnet test` / `npm test` 全 PASS。
2. シングルランを 1 回完走、実績 → 履歴タブで自ランが見える。展開で 4 セットが見える。
3. カード/レリック/ポーション/モンスタータブで遭遇したものが ✓、残りは `???`。
4. 別アカウントで図鑑が独立していること（既存アカウントで貯まり、再ログイン後も維持されること）。
5. v5 RunState / v1 History を持つアカウントで起動 → migration でクラッシュしないこと（既存セーブが残っているアカウントで検証）。

- [ ] **Finishing the branch**

`superpowers:finishing-a-development-branch` を起動してマージ / PR を選択する。

---

## 補足

- spec の Detection Points はすべて Task 14〜20 でカバー。Tracker 呼び出し箇所は合計:
  - カード: NewSoloRun（Task 6）/ PostBattleWin reward（Task 19）/ MerchantController inv（Task 20）/ EventResolver.GrantCardReward（Task 16）
  - レリック: RewardApplier.ClaimRelic（Task 14）/ MerchantActions.BuyRelic（Task 15）/ EventResolver.GainRelic（Task 16）/ ActStartActions.ChooseRelic（Task 17）
  - ポーション: RewardApplier.ApplyPotion（Task 14）/ MerchantActions.BuyPotion（Task 15）
  - モンスター: BattlePlaceholder.Start（Task 18）
- マージ呼び出しは Task 21 で Cleared / Abandon / GameOver の 3 箇所（RunsController x2 + DebugController x1）。
- `BestiaryIds` のような card base_id 正規化は現在の実装では不要（`CardInstance.Id` は既に base_id）。将来 `+` サフィックスを導入する場合は `BestiaryTracker` 側に正規化を追加する。
