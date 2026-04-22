# Phase 7 — Boss / Act Transition / HP Heal / Run Result Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Slay-the-Spire 風のランが act 1→2→3 と通しで進行し、act 間で HP 全回復 + 特別レリック 3 択、act 3 クリアで Cleared、HP 0 で GameOver、ラン終了時に履歴保存する一連のフローを実装する。

**Architecture:** Approach A — 最小 state 拡張。`ActStartRelicChoice` 1 つだけ追加し、Boss reward は既存 `RewardState` を `IsBossReward` フラグで再利用。act 遷移は `RngSeed * 2654435761UL + act` でマップ seed を決定論的に派生。v4 セーブは v5 へ自動 migrate。

**Tech Stack:** C# .NET 10 (sealed record / ImmutableArray / System.Text.Json), ASP.NET Core 10, React 19 + TypeScript + Vite, xUnit, Vitest.

**Spec:** [docs/superpowers/specs/2026-04-22-phase07-boss-act-transition-design.md](../specs/2026-04-22-phase07-boss-act-transition-design.md)

---

## File Structure

### New Core files
- `src/Core/Run/RunConstants.cs` — MaxAct = 3 などのラン系定数
- `src/Core/Run/ActStartRelicChoice.cs` — 3 択 record
- `src/Core/Run/ActMapSeed.cs` — act 間マップ seed 派生
- `src/Core/Run/ActTransition.cs` — AdvanceAct / FinishRun
- `src/Core/Run/ActStartActions.cs` — GenerateChoices / ChooseRelic
- `src/Core/Run/BossRewardFlow.cs` — Boss 勝利時の分岐
- `src/Core/Run/DebugActions.cs` — ApplyDamage
- `src/Core/History/RunHistoryRecord.cs`
- `src/Core/History/RunHistoryBuilder.cs`
- `src/Core/Data/RelicsActStart/act{1,2,3}.json` — 各 act のプール定義
- `src/Core/Data/Relics/act{1,2,3}_start_{01..05}.json` — 計 15 種
- `src/Core/Data/Enemies/act2_*.json`, `src/Core/Data/Enemies/act3_*.json`
- `src/Core/Data/Encounters/enc_{w,s,e,b}_act{2,3}_*.json`

### Modified Core files
- `src/Core/Run/RunState.cs` — v5 (RunId, ActiveActStartRelicChoice 追加, VisitedNodeIds 不変条件緩和)
- `src/Core/Run/RunStateSerializer.cs` — v4→v5 migration
- `src/Core/Rewards/RewardState.cs` — IsBossReward フラグ
- `src/Core/Run/NodeEffectResolver.cs` — Start tile 処理
- `src/Core/Data/EmbeddedDataLoader.cs` — RelicsActStart prefix 追加
- `src/Core/Data/DataCatalog.cs` — ActStartRelicPools プロパティ追加

### New Server files
- `src/Server/Controllers/ActStartController.cs`
- `src/Server/Controllers/DebugController.cs`
- `src/Server/Controllers/HistoryController.cs`
- `src/Server/Abstractions/IHistoryRepository.cs`
- `src/Server/Services/FileBacked/FileHistoryRepository.cs`
- `src/Server/Dtos/ActStartRelicChoiceDto.cs`
- `src/Server/Dtos/RunResultDto.cs`
- `src/Server/Dtos/DebugDamageRequestDto.cs`
- `src/Server/Dtos/ActStartChooseRequestDto.cs`

### Modified Server files
- `src/Server/Dtos/RunSnapshotDto.cs` — activeActStartRelicChoice
- `src/Server/Dtos/RewardStateDto.cs` — isBossReward
- `src/Server/Controllers/RunsController.cs` — battle/win, reward/proceed, abandon の分岐追加
- `src/Server/Services/RunStartService.cs` — ActMapSeed 採用, AdvanceMapAsync メソッド追加
- `src/Server/Program.cs` — History repo / Debug endpoint 登録

### New Client files
- `src/Client/src/screens/ActStartRelicScreen.tsx` + test
- `src/Client/src/screens/RunResultScreen.tsx` + test
- `src/Client/src/api/actStart.ts`
- `src/Client/src/api/debug.ts`
- `src/Client/src/api/history.ts`

### Modified Client files
- `src/Client/src/api/types.ts` — 新 DTO 型
- `src/Client/src/screens/MapScreen.tsx` — ActStartRelicScreen mount
- `src/Client/src/screens/RewardPopup.tsx` — Boss reward ラベル
- `src/Client/src/screens/TopBar.tsx`（存在しなければ BattleOverlay と同じ追加要領で対応）
- `src/Client/src/screens/BattleOverlay.tsx` — DEBUG -10HP
- `src/Client/src/screens/MainMenuScreen.tsx` — 続きから非表示
- `src/Client/src/App.tsx`（または screen switcher 相当）— RunResultScreen 遷移

---

## Task Ordering Principle

Core → Server → Client の順で進める。各 task は TDD で以下の 5 ステップ構造:

1. Test を書く（赤）
2. Test を実行して失敗を確認
3. 最小実装（緑）
4. Test を実行して成功を確認
5. Commit

CLAUDE.md の `dotnet test` / `cd src/Client && npm test` 規約に従うこと。

---

## Task 1: RunConstants + RewardState に IsBossReward を追加

**Files:**
- Create: `src/Core/Run/RunConstants.cs`
- Modify: `src/Core/Rewards/RewardState.cs`
- Test: `tests/Core.Tests/Run/RunConstantsTests.cs`, `tests/Core.Tests/Rewards/RewardStateTests.cs`（既存なら追記）

- [ ] **Step 1: Write failing test for RunConstants.MaxAct**

Create `tests/Core.Tests/Run/RunConstantsTests.cs`:

```csharp
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunConstantsTests
{
    [Fact]
    public void MaxAct_IsThree()
    {
        Assert.Equal(3, RunConstants.MaxAct);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~RunConstantsTests`
Expected: FAIL (RunConstants 未定義)

- [ ] **Step 3: Create RunConstants**

Create `src/Core/Run/RunConstants.cs`:

```csharp
namespace RoguelikeCardGame.Core.Run;

public static class RunConstants
{
    public const int MaxAct = 3;
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~RunConstantsTests`
Expected: PASS

- [ ] **Step 5: Write failing test for RewardState.IsBossReward default**

Append to `tests/Core.Tests/Rewards/RewardStateTests.cs`（なければ新規作成）:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Rewards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardStateTests
{
    [Fact]
    public void IsBossReward_DefaultsFalse()
    {
        var r = new RewardState(
            Gold: 0, GoldClaimed: true,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Skipped);
        Assert.False(r.IsBossReward);
    }
}
```

- [ ] **Step 6: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~RewardStateTests.IsBossReward_DefaultsFalse`
Expected: FAIL (IsBossReward プロパティ無し)

- [ ] **Step 7: Add IsBossReward to RewardState**

Edit `src/Core/Rewards/RewardState.cs`:

```csharp
public sealed record RewardState(
    int Gold,
    bool GoldClaimed,
    string? PotionId,
    bool PotionClaimed,
    ImmutableArray<string> CardChoices,
    CardRewardStatus CardStatus,
    string? RelicId = null,
    bool RelicClaimed = true,
    bool IsBossReward = false);
```

- [ ] **Step 8: Run all Core tests, expect PASS**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/Core/Run/RunConstants.cs src/Core/Rewards/RewardState.cs tests/Core.Tests/Run/RunConstantsTests.cs tests/Core.Tests/Rewards/RewardStateTests.cs
git commit -m "feat(core): add RunConstants.MaxAct and RewardState.IsBossReward

Phase 7 foundation: expose act-count constant and boss-reward flag used by
later tasks for act transition and reward label switching."
```

---

## Task 2: ActStartRelicChoice record

**Files:**
- Create: `src/Core/Run/ActStartRelicChoice.cs`
- Test: `tests/Core.Tests/Run/ActStartRelicChoiceTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActStartRelicChoiceTests
{
    [Fact]
    public void HoldsThreeRelicIds()
    {
        var c = new ActStartRelicChoice(ImmutableArray.Create("a", "b", "c"));
        Assert.Equal(3, c.RelicIds.Length);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~ActStartRelicChoiceTests`
Expected: FAIL (type 未定義)

- [ ] **Step 3: Create ActStartRelicChoice**

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Run;

public sealed record ActStartRelicChoice(ImmutableArray<string> RelicIds);
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~ActStartRelicChoiceTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Run/ActStartRelicChoice.cs tests/Core.Tests/Run/ActStartRelicChoiceTests.cs
git commit -m "feat(core): add ActStartRelicChoice record"
```

---

## Task 3: RunState v5 — RunId + ActiveActStartRelicChoice + invariant 緩和

**Files:**
- Modify: `src/Core/Run/RunState.cs`
- Modify: `tests/Core.Tests/` 配下の既存テスト（NewSoloRun 呼び出し箇所）

**注意:** `NewSoloRun` の呼び出し箇所に `RunId`, `ActiveActStartRelicChoice` が増えるため、既存テストヘルパ `TestRunStates.FreshDefault` も更新が必要。まず `TestRunStates` を探して構造を把握すること。

- [ ] **Step 1: Read TestRunStates to understand existing helpers**

Run: `grep -rn "FreshDefault" tests/Core.Tests/ | head -5`
Find and Read the file, note its signature.

- [ ] **Step 2: Write failing test for RunState.RunId**

Create `tests/Core.Tests/Run/RunStateIdentityTests.cs`:

```csharp
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateIdentityTests
{
    [Fact]
    public void NewSoloRun_GeneratesRunId()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        Assert.False(string.IsNullOrEmpty(s.RunId));
    }

    [Fact]
    public void NewSoloRun_GeneratesDistinctRunIds()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var a = TestRunStates.FreshDefault(cat);
        var b = TestRunStates.FreshDefault(cat);
        Assert.NotEqual(a.RunId, b.RunId);
    }

    [Fact]
    public void NewSoloRun_VisitedNodeIdsEmpty()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        Assert.Empty(s.VisitedNodeIds);
    }

    [Fact]
    public void NewSoloRun_ActiveActStartRelicChoiceNull()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        Assert.Null(s.ActiveActStartRelicChoice);
    }

    [Fact]
    public void Validate_AllowsStartUnvisitedWhenChoiceActive()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                System.Collections.Immutable.ImmutableArray.Create("a", "b", "c")),
        };
        Assert.Null(s.Validate());
    }
}
```

- [ ] **Step 3: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~RunStateIdentityTests`
Expected: FAIL (RunId / ActiveActStartRelicChoice 未定義)

- [ ] **Step 4: Update RunState record**

Edit `src/Core/Run/RunState.cs`:

```csharp
// (1) positional params に 2 つ追加
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    ImmutableArray<int> VisitedNodeIds,
    ImmutableDictionary<int, TileKind> UnknownResolutions,
    string CharacterId,
    int CurrentHp,
    int MaxHp,
    int Gold,
    ImmutableArray<CardInstance> Deck,
    ImmutableArray<string> Potions,
    int PotionSlotCount,
    BattleState? ActiveBattle,
    RewardState? ActiveReward,
    ImmutableArray<string> EncounterQueueWeak,
    ImmutableArray<string> EncounterQueueStrong,
    ImmutableArray<string> EncounterQueueElite,
    ImmutableArray<string> EncounterQueueBoss,
    RewardRngState RewardRngState,
    MerchantInventory? ActiveMerchant,
    EventInstance? ActiveEvent,
    bool ActiveRestPending,
    bool ActiveRestCompleted,
    IReadOnlyList<string> Relics,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress,
    string RunId,                                   // ← new
    ActStartRelicChoice? ActiveActStartRelicChoice, // ← new
    int DiscardUsesSoFar = 0)
{
    public const int CurrentSchemaVersion = 5;

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
        string characterId = "default",
        string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(unknownResolutions);

        if (!catalog.TryGetCharacter(characterId, out var ch))
            throw new InvalidOperationException($"Character \"{characterId}\" が DataCatalog に存在しません");

        foreach (var id in ch.Deck)
            if (!catalog.TryGetCard(id, out _))
                throw new InvalidOperationException(
                    $"Character \"{characterId}\" のデッキが参照するカード ID \"{id}\" が存在しません");

        var potionBuilder = ImmutableArray.CreateBuilder<string>(ch.PotionSlotCount);
        for (int i = 0; i < ch.PotionSlotCount; i++) potionBuilder.Add("");
        var potions = potionBuilder.ToImmutable();

        if (!catalog.RewardTables.TryGetValue("act1", out var rt))
            throw new InvalidOperationException("RewardTable \"act1\" が DataCatalog に存在しません");

        var deck = ImmutableArray.CreateRange(ch.Deck.Select(id => new CardInstance(id, false)));

        return new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentNodeId: startNodeId,
            VisitedNodeIds: ImmutableArray<int>.Empty,  // ← was [startNodeId]
            UnknownResolutions: unknownResolutions,
            CharacterId: characterId,
            CurrentHp: ch.MaxHp,
            MaxHp: ch.MaxHp,
            Gold: ch.StartingGold,
            Deck: deck,
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
            ActiveMerchant: null,
            ActiveEvent: null,
            ActiveRestPending: false,
            ActiveRestCompleted: false,
            Relics: Array.Empty<string>(),
            PlaySeconds: 0L,
            RngSeed: rngSeed,
            SavedAtUtc: nowUtc,
            Progress: RunProgress.InProgress,
            RunId: runId ?? Guid.NewGuid().ToString(),
            ActiveActStartRelicChoice: null);
    }

    public string? Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            return $"SchemaVersion must be {CurrentSchemaVersion} (got {SchemaVersion})";
        if (VisitedNodeIds.IsDefault) return "VisitedNodeIds must not be default";
        // ← invariant 緩和: Start で ActiveActStartRelicChoice がある間は CurrentNodeId が未 visited でも OK
        if (ActiveActStartRelicChoice is null && VisitedNodeIds.Length > 0
            && !VisitedNodeIds.Contains(CurrentNodeId))
            return $"VisitedNodeIds must contain CurrentNodeId ({CurrentNodeId}) unless act-start relic choice is active";
        if (VisitedNodeIds.Length != VisitedNodeIds.Distinct().Count())
            return "VisitedNodeIds must not contain duplicates";
        foreach (var kv in UnknownResolutions)
        {
            if (kv.Value is TileKind.Unknown or TileKind.Start or TileKind.Boss)
                return $"UnknownResolutions[{kv.Key}]={kv.Value} is not a valid resolved kind";
        }
        if (Potions.Length != PotionSlotCount)
            return $"Potions.Length ({Potions.Length}) != PotionSlotCount ({PotionSlotCount})";

        int activeCount = 0;
        if (ActiveBattle is not null) activeCount++;
        if (ActiveReward is not null) activeCount++;
        if (ActiveMerchant is not null) activeCount++;
        if (ActiveEvent is not null) activeCount++;
        if (ActiveActStartRelicChoice is not null) activeCount++;
        if (activeCount > 1)
            return "at most one of ActiveBattle / ActiveReward / ActiveMerchant / ActiveEvent / ActiveActStartRelicChoice can be non-null";
        if (ActiveRestPending && activeCount > 0)
            return "ActiveRestPending must not coexist with any other Active*";
        if (ActiveRestCompleted && !ActiveRestPending)
            return "ActiveRestCompleted requires ActiveRestPending";

        if (ActiveReward is { CardChoices: var cc } && cc.Length != 0 && cc.Length != 3)
            return $"CardChoices must have length 0 or 3 (got {cc.Length})";
        if (ActiveActStartRelicChoice is { RelicIds: var ids } && ids.Length != 3)
            return $"ActStartRelicChoice.RelicIds must have length 3 (got {ids.Length})";
        return null;
    }
}
```

- [ ] **Step 5: Fix compile errors in existing test helpers**

Build fails at this point because `TestRunStates.FreshDefault`, any `new RunState(...)` call, and existing invariant test may break. Fix them minimally:

Run: `dotnet build src/Core/Core.csproj`
Run: `dotnet build tests/Core.Tests/Core.Tests.csproj`

For any compile error pointing at existing `new RunState(...)` literal, add the two new params (`RunId: "test-run", ActiveActStartRelicChoice: null`) before the trailing `DiscardUsesSoFar` default.

For the existing `VisitedNodeIds.Contains(CurrentNodeId)` invariant assertion test (likely in `RunStateValidateTests`), keep it — it still fires when VisitedNodeIds is non-empty and choice is null. Search for a test expecting the error message and update the message string:

Run: `grep -rn "VisitedNodeIds must contain CurrentNodeId" tests/Core.Tests/`

Update any matching assertion to the new message: `"VisitedNodeIds must contain CurrentNodeId (X) unless act-start relic choice is active"`.

- [ ] **Step 6: Run all Core tests**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj`
Expected: All existing + new tests PASS. If invariant-related tests fail because `VisitedNodeIds` is now empty for fresh runs, update them to assert `s.VisitedNodeIds` is empty (Start no longer pre-visited).

- [ ] **Step 7: Commit**

```bash
git add src/Core/Run/RunState.cs tests/Core.Tests/Run/RunStateIdentityTests.cs tests/Core.Tests/
git commit -m "feat(core)!: bump RunState to schema v5 with RunId and act-start choice

- Add RunId (Guid string) for history key
- Add ActiveActStartRelicChoice
- Relax VisitedNodeIds invariant: Start may be current-but-unvisited while choice is active
- NewSoloRun now starts with empty VisitedNodeIds"
```

---

## Task 4: RunState serializer v4 → v5 migration

**Files:**
- Modify: `src/Core/Run/RunStateSerializer.cs`
- Test: `tests/Core.Tests/Run/RunStateSerializerMigrationTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateSerializerMigrationTests
{
    [Fact]
    public void V4ToV5_FillsRunIdAndDefaults()
    {
        // 最小 v4 JSON。Deck や VisitedNodeIds など既存 v4 スキーマが書けるだけ書く。
        // 既存 test.json を Resources に置いているならそれを使ってもよい。
        var v4json = BuildMinimalV4Json();
        var s = RunStateSerializer.Deserialize(v4json);
        Assert.Equal(RunState.CurrentSchemaVersion, s.SchemaVersion);
        Assert.False(string.IsNullOrEmpty(s.RunId));
        Assert.Null(s.ActiveActStartRelicChoice);
    }

    private static string BuildMinimalV4Json()
    {
        // 作成: 最小 v4 run を NewSoloRun で作り、schemaVersion を 4 に書き換えて serialize。
        var cat = RoguelikeCardGame.Core.Data.EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var json = RunStateSerializer.Serialize(s);
        // schemaVersion を 4 に書き換える
        json = json.Replace("\"schemaVersion\":5", "\"schemaVersion\":4");
        // runId / activeActStartRelicChoice フィールドを削除して v4 体裁に戻す
        json = System.Text.RegularExpressions.Regex.Replace(
            json, ",\"runId\":\"[^\"]*\"", "");
        json = System.Text.RegularExpressions.Regex.Replace(
            json, ",\"activeActStartRelicChoice\":(null|\\{[^}]*\\})", "");
        return json;
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~RunStateSerializerMigrationTests`
Expected: FAIL (v4 は「未対応」で reject される)

- [ ] **Step 3: Add v4→v5 migration**

Edit `src/Core/Run/RunStateSerializer.cs`:

```csharp
public static RunState Deserialize(string json)
{
    JsonNode? node;
    try { node = JsonNode.Parse(json); }
    catch (JsonException ex)
    { throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex); }

    if (node is not JsonObject obj)
        throw new RunStateSerializerException("RunState JSON のルートがオブジェクトではありません。");

    int version = obj["schemaVersion"]?.GetValue<int>()
        ?? throw new RunStateSerializerException("schemaVersion が存在しません。");

    if (version == 3) { obj = MigrateV3ToV4(obj); version = 4; }
    if (version == 4) { obj = MigrateV4ToV5(obj); version = 5; }
    if (version != RunState.CurrentSchemaVersion)
        throw new RunStateSerializerException(
            $"未対応の schemaVersion: {version} (対応: {RunState.CurrentSchemaVersion})");

    RunState? state;
    try { state = JsonSerializer.Deserialize<RunState>(obj.ToJsonString(), JsonOptions.Default); }
    catch (JsonException ex)
    { throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex); }

    if (state is null) throw new RunStateSerializerException("RunState JSON が null でした。");
    return state;
}

private static JsonObject MigrateV4ToV5(JsonObject obj)
{
    obj["runId"] ??= System.Guid.NewGuid().ToString();
    obj["activeActStartRelicChoice"] ??= null;
    // v4 セーブは Start が既に visited 済みなので VisitedNodeIds をそのまま引き継ぐ
    // (自然と act-start relic スキップとして扱われる、spec の migration ルール通り)
    // RewardState.isBossReward は JSON 側で default (false) のまま問題なし
    obj["schemaVersion"] = RunState.CurrentSchemaVersion;
    return obj;
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~RunStateSerializerMigrationTests`
Expected: PASS

- [ ] **Step 5: Verify roundtrip still works**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj`
Expected: All pass (既存 serializer test 含む)

- [ ] **Step 6: Commit**

```bash
git add src/Core/Run/RunStateSerializer.cs tests/Core.Tests/Run/RunStateSerializerMigrationTests.cs
git commit -m "feat(core): migrate v4 saves to v5 (RunId, ActiveActStartRelicChoice)"
```

---

## Task 5: act-start relic 15 種と pool ローダ

**Files:**
- Create: `src/Core/Data/Relics/act1_start_01.json` .. `act3_start_05.json`（15 ファイル）
- Create: `src/Core/Data/RelicsActStart/act1.json`, `act2.json`, `act3.json`
- Modify: `src/Core/Data/EmbeddedDataLoader.cs`
- Modify: `src/Core/Data/DataCatalog.cs`
- Modify: `src/Core/Core.csproj`（EmbeddedResource 追加があるか確認。現状 Prefix で全 json を resource にしている前提）
- Test: `tests/Core.Tests/Data/ActStartRelicPoolsTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class ActStartRelicPoolsTests
{
    [Fact]
    public void LoadCatalog_ExposesActStartRelicPools_ForAllActs()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        Assert.NotNull(cat.ActStartRelicPools);
        Assert.Equal(5, cat.ActStartRelicPools![1].Length);
        Assert.Equal(5, cat.ActStartRelicPools[2].Length);
        Assert.Equal(5, cat.ActStartRelicPools[3].Length);
    }

    [Fact]
    public void Act1StartRelics_AreDefinedInCatalog()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        foreach (var id in cat.ActStartRelicPools![1])
            Assert.True(cat.Relics.ContainsKey(id), $"Relic '{id}' not found");
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~ActStartRelicPoolsTests`
Expected: FAIL (プロパティ未定義)

- [ ] **Step 3: Create 15 relic JSON files**

act 1 用（序盤向け）:
- `src/Core/Data/Relics/act1_start_01.json`:
  ```json
  {"id":"act1_start_01","name":"旅立ちの盾","rarity":1,"trigger":"OnPickup","effects":[{"type":"gainMaxHp","amount":8}]}
  ```
- `src/Core/Data/Relics/act1_start_02.json`:
  ```json
  {"id":"act1_start_02","name":"道銀貨","rarity":1,"trigger":"OnPickup","effects":[{"type":"gainGold","amount":75}]}
  ```
- `src/Core/Data/Relics/act1_start_03.json`:
  ```json
  {"id":"act1_start_03","name":"温石","rarity":1,"trigger":"Passive","effects":[{"type":"restHealBonus","amount":5}]}
  ```
- `src/Core/Data/Relics/act1_start_04.json`:
  ```json
  {"id":"act1_start_04","name":"旅人の懐刀","rarity":1,"trigger":"OnPickup","effects":[{"type":"gainMaxHp","amount":5}]}
  ```
- `src/Core/Data/Relics/act1_start_05.json`:
  ```json
  {"id":"act1_start_05","name":"賢者の導き","rarity":1,"trigger":"OnPickup","effects":[{"type":"gainGold","amount":50}]}
  ```

act 2 用（中盤向け）:
- `src/Core/Data/Relics/act2_start_01.json`:
  ```json
  {"id":"act2_start_01","name":"堅実な鎧","rarity":1,"trigger":"OnPickup","effects":[{"type":"gainMaxHp","amount":12}]}
  ```
- `src/Core/Data/Relics/act2_start_02.json`:
  ```json
  {"id":"act2_start_02","name":"豪商の刻印","rarity":1,"trigger":"OnPickup","effects":[{"type":"gainGold","amount":100}]}
  ```
- `src/Core/Data/Relics/act2_start_03.json`:
  ```json
  {"id":"act2_start_03","name":"狩人の勘","rarity":1,"trigger":"Passive","effects":[{"type":"restHealBonus","amount":8}]}
  ```
- `src/Core/Data/Relics/act2_start_04.json`:
  ```json
  {"id":"act2_start_04","name":"守護者の加護","rarity":1,"trigger":"OnPickup","effects":[{"type":"gainMaxHp","amount":10}]}
  ```
- `src/Core/Data/Relics/act2_start_05.json`:
  ```json
  {"id":"act2_start_05","name":"冒険の記憶","rarity":1,"trigger":"OnPickup","effects":[{"type":"gainGold","amount":80}]}
  ```

act 3 用（終盤向け強効果）:
- `src/Core/Data/Relics/act3_start_01.json`:
  ```json
  {"id":"act3_start_01","name":"英雄の証","rarity":2,"trigger":"OnPickup","effects":[{"type":"gainMaxHp","amount":20}]}
  ```
- `src/Core/Data/Relics/act3_start_02.json`:
  ```json
  {"id":"act3_start_02","name":"王室の印","rarity":2,"trigger":"OnPickup","effects":[{"type":"gainGold","amount":150}]}
  ```
- `src/Core/Data/Relics/act3_start_03.json`:
  ```json
  {"id":"act3_start_03","name":"賢者の羅針盤","rarity":2,"trigger":"Passive","effects":[{"type":"restHealBonus","amount":12}]}
  ```
- `src/Core/Data/Relics/act3_start_04.json`:
  ```json
  {"id":"act3_start_04","name":"決戦の剣","rarity":2,"trigger":"OnPickup","effects":[{"type":"gainMaxHp","amount":15}]}
  ```
- `src/Core/Data/Relics/act3_start_05.json`:
  ```json
  {"id":"act3_start_05","name":"終焉の予言","rarity":2,"trigger":"OnPickup","effects":[{"type":"gainGold","amount":120}]}
  ```

**注意:** effect type `"restHealBonus"` は既存 `NonBattleRelicEffects.ApplyPassiveRestHealBonus` が `RestHealBonusEffect` を参照しているため既に定義済み前提。もし `RelicJsonLoader` で未サポートだった場合は、effect として既存サポート済みの `gainMaxHp`/`gainGold` に差し替えて対応（restHealBonus 系は Phase 8 以降に回す）。

- [ ] **Step 4: Create 3 pool JSON files**

- `src/Core/Data/RelicsActStart/act1.json`:
  ```json
  {"act":1,"relicIds":["act1_start_01","act1_start_02","act1_start_03","act1_start_04","act1_start_05"]}
  ```
- `src/Core/Data/RelicsActStart/act2.json`:
  ```json
  {"act":2,"relicIds":["act2_start_01","act2_start_02","act2_start_03","act2_start_04","act2_start_05"]}
  ```
- `src/Core/Data/RelicsActStart/act3.json`:
  ```json
  {"act":3,"relicIds":["act3_start_01","act3_start_02","act3_start_03","act3_start_04","act3_start_05"]}
  ```

- [ ] **Step 5: Update EmbeddedDataLoader to load pools**

Edit `src/Core/Data/EmbeddedDataLoader.cs`:

```csharp
private const string RelicsActStartPrefix = "RoguelikeCardGame.Core.Data.RelicsActStart.";

public static DataCatalog LoadCatalog()
{
    var asm = typeof(EmbeddedDataLoader).Assembly;
    string? merchantPricesJson = ReadSingle(asm, MerchantPricesResourceName);
    return DataCatalog.LoadFromStrings(
        cards: ReadAllWithPrefix(asm, CardsPrefix),
        relics: ReadAllWithPrefix(asm, RelicsPrefix),
        potions: ReadAllWithPrefix(asm, PotionsPrefix),
        enemies: ReadAllWithPrefix(asm, EnemiesPrefix),
        encounters: ReadAllWithPrefix(asm, EncountersPrefix),
        rewardTables: ReadAllWithPrefix(asm, RewardTablePrefix),
        characters: ReadAllWithPrefix(asm, CharactersPrefix),
        events: ReadAllWithPrefix(asm, EventsPrefix),
        actStartRelicPools: ReadAllWithPrefix(asm, RelicsActStartPrefix),
        merchantPricesJson: merchantPricesJson);
}
```

- [ ] **Step 6: Update DataCatalog**

Edit `src/Core/Data/DataCatalog.cs`:

```csharp
using System.Collections.Immutable;
using System.Text.Json;

public sealed record DataCatalog(
    IReadOnlyDictionary<string, CardDefinition> Cards,
    IReadOnlyDictionary<string, RelicDefinition> Relics,
    IReadOnlyDictionary<string, PotionDefinition> Potions,
    IReadOnlyDictionary<string, EnemyDefinition> Enemies,
    IReadOnlyDictionary<string, EncounterDefinition> Encounters,
    IReadOnlyDictionary<string, RewardTable> RewardTables,
    IReadOnlyDictionary<string, CharacterDefinition> Characters,
    IReadOnlyDictionary<string, EventDefinition> Events,
    MerchantPrices? MerchantPrices = null,
    IReadOnlyDictionary<int, ImmutableArray<string>>? ActStartRelicPools = null)
{
    public static DataCatalog LoadFromStrings(
        IEnumerable<string> cards,
        IEnumerable<string> relics,
        IEnumerable<string> potions,
        IEnumerable<string> enemies,
        IEnumerable<string> encounters,
        IEnumerable<string> rewardTables,
        IEnumerable<string> characters,
        IEnumerable<string>? events = null,
        IEnumerable<string>? actStartRelicPools = null,
        string? merchantPricesJson = null)
    {
        // (既存ロード処理はそのまま)
        // ...
        var pools = new Dictionary<int, ImmutableArray<string>>();
        if (actStartRelicPools is not null)
        {
            foreach (var json in actStartRelicPools)
            {
                using var doc = JsonDocument.Parse(json);
                int act = doc.RootElement.GetProperty("act").GetInt32();
                var ids = doc.RootElement.GetProperty("relicIds").EnumerateArray()
                    .Select(e => e.GetString()!).ToImmutableArray();
                if (!pools.TryAdd(act, ids))
                    throw new DataCatalogException($"act-start relic pool 重複: act={act}");
            }
        }
        return new DataCatalog(cardMap, relicMap, potionMap, enemyMap, encMap, rtMap, chMap, eventMap, mp, pools);
    }
    // (以下既存 TryGet* メソッド)
}
```

- [ ] **Step 7: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~ActStartRelicPoolsTests`
Expected: PASS

- [ ] **Step 8: Run all Core tests**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add src/Core/Data/Relics/act*_start_*.json src/Core/Data/RelicsActStart/ src/Core/Data/EmbeddedDataLoader.cs src/Core/Data/DataCatalog.cs tests/Core.Tests/Data/ActStartRelicPoolsTests.cs
git commit -m "feat(core): add 15 act-start relics and per-act pools"
```

---

## Task 6: act 2 / act 3 encounter + enemy JSON

**Files:**
- Create: `src/Core/Data/Enemies/act2_grunt_a.json` など計 3〜5 種 × 2 act
- Create: `src/Core/Data/Encounters/enc_{w,s,e,b}_act{2,3}_*.json`
- Test: `tests/Core.Tests/Data/ActEncountersTests.cs`

**方針:** 既存 act1 データの単純な数値スケーリング版を用意する。act 2: HP 1.5 倍、damage 1.3 倍程度。act 3: HP 2.2 倍、damage 1.7 倍程度。最小: act ごとに Weak 2, Strong 2, Elite 1, Boss 1 の encounter。

- [ ] **Step 1: Write failing test**

```csharp
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class ActEncountersTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void HasAtLeastOneEncounterPerTier(int act)
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        foreach (var tier in new[] { EnemyTier.Weak, EnemyTier.Strong, EnemyTier.Elite, EnemyTier.Boss })
        {
            var any = cat.Encounters.Values.Any(e => e.Pool.Act == act && e.Pool.Tier == tier);
            Assert.True(any, $"act {act} tier {tier} encounter missing");
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~ActEncountersTests`
Expected: FAIL

- [ ] **Step 3: Create enemies**

最小 6 種（act2×3 + act3×3）。`guardian_golem` 形式に従う:

`src/Core/Data/Enemies/act2_grunt.json`:
```json
{"id":"act2_grunt","name":"鋼兵","imageId":"act2_grunt","hpMin":28,"hpMax":32,"act":2,"tier":"Weak","initialMoveId":"slash","moves":[{"id":"slash","kind":"attack","damageMin":8,"damageMax":10,"hits":1,"nextMoveId":"slash"}]}
```
`src/Core/Data/Enemies/act2_brute.json`:
```json
{"id":"act2_brute","name":"剛拳","imageId":"act2_brute","hpMin":52,"hpMax":58,"act":2,"tier":"Strong","initialMoveId":"smash","moves":[{"id":"smash","kind":"attack","damageMin":14,"damageMax":16,"hits":1,"nextMoveId":"guard"},{"id":"guard","kind":"block","blockMin":10,"blockMax":10,"nextMoveId":"smash"}]}
```
`src/Core/Data/Enemies/act2_boss.json`:
```json
{"id":"act2_boss","name":"機兵隊長","imageId":"act2_boss","hpMin":220,"hpMax":220,"act":2,"tier":"Boss","initialMoveId":"charge","moves":[{"id":"charge","kind":"attack","damageMin":22,"damageMax":22,"hits":1,"nextMoveId":"volley"},{"id":"volley","kind":"attack","damageMin":8,"damageMax":8,"hits":3,"nextMoveId":"fortify"},{"id":"fortify","kind":"block","blockMin":20,"blockMax":20,"nextMoveId":"charge"}]}
```
`src/Core/Data/Enemies/act3_grunt.json`:
```json
{"id":"act3_grunt","name":"闇兵","imageId":"act3_grunt","hpMin":42,"hpMax":46,"act":3,"tier":"Weak","initialMoveId":"pierce","moves":[{"id":"pierce","kind":"attack","damageMin":12,"damageMax":14,"hits":1,"nextMoveId":"pierce"}]}
```
`src/Core/Data/Enemies/act3_brute.json`:
```json
{"id":"act3_brute","name":"闇鎧騎士","imageId":"act3_brute","hpMin":78,"hpMax":84,"act":3,"tier":"Strong","initialMoveId":"crush","moves":[{"id":"crush","kind":"attack","damageMin":20,"damageMax":22,"hits":1,"nextMoveId":"wall"},{"id":"wall","kind":"block","blockMin":14,"blockMax":14,"nextMoveId":"crush"}]}
```
`src/Core/Data/Enemies/act3_boss.json`:
```json
{"id":"act3_boss","name":"終焉の王","imageId":"act3_boss","hpMin":340,"hpMax":340,"act":3,"tier":"Boss","initialMoveId":"wrath","moves":[{"id":"wrath","kind":"attack","damageMin":30,"damageMax":30,"hits":1,"nextMoveId":"storm"},{"id":"storm","kind":"attack","damageMin":12,"damageMax":12,"hits":4,"nextMoveId":"bulwark"},{"id":"bulwark","kind":"block","blockMin":26,"blockMax":26,"nextMoveId":"wrath"}]}
```

Elite 用 enemy はとりあえず Strong 兵を強化したものを 1 種ずつ:

`src/Core/Data/Enemies/act2_elite.json`:
```json
{"id":"act2_elite","name":"重装機兵","imageId":"act2_elite","hpMin":88,"hpMax":96,"act":2,"tier":"Elite","initialMoveId":"heavy","moves":[{"id":"heavy","kind":"attack","damageMin":18,"damageMax":20,"hits":1,"nextMoveId":"stance"},{"id":"stance","kind":"block","blockMin":18,"blockMax":18,"nextMoveId":"heavy"}]}
```
`src/Core/Data/Enemies/act3_elite.json`:
```json
{"id":"act3_elite","name":"深淵の執行者","imageId":"act3_elite","hpMin":130,"hpMax":140,"act":3,"tier":"Elite","initialMoveId":"execute","moves":[{"id":"execute","kind":"attack","damageMin":26,"damageMax":28,"hits":1,"nextMoveId":"rampart"},{"id":"rampart","kind":"block","blockMin":22,"blockMax":22,"nextMoveId":"execute"}]}
```

- [ ] **Step 4: Create encounters**

`src/Core/Data/Encounters/enc_w_act2_grunt.json`: `{"id":"enc_w_act2_grunt","act":2,"tier":"Weak","enemyIds":["act2_grunt"]}`
`src/Core/Data/Encounters/enc_w_act2_pair.json`: `{"id":"enc_w_act2_pair","act":2,"tier":"Weak","enemyIds":["act2_grunt","act2_grunt"]}`
`src/Core/Data/Encounters/enc_s_act2_brute.json`: `{"id":"enc_s_act2_brute","act":2,"tier":"Strong","enemyIds":["act2_brute"]}`
`src/Core/Data/Encounters/enc_s_act2_mixed.json`: `{"id":"enc_s_act2_mixed","act":2,"tier":"Strong","enemyIds":["act2_grunt","act2_brute"]}`
`src/Core/Data/Encounters/enc_e_act2_elite.json`: `{"id":"enc_e_act2_elite","act":2,"tier":"Elite","enemyIds":["act2_elite"]}`
`src/Core/Data/Encounters/enc_b_act2_boss.json`: `{"id":"enc_b_act2_boss","act":2,"tier":"Boss","enemyIds":["act2_boss"]}`

同様 act 3:
`enc_w_act3_grunt.json`: `{"id":"enc_w_act3_grunt","act":3,"tier":"Weak","enemyIds":["act3_grunt"]}`
`enc_w_act3_pair.json`: `{"id":"enc_w_act3_pair","act":3,"tier":"Weak","enemyIds":["act3_grunt","act3_grunt"]}`
`enc_s_act3_brute.json`: `{"id":"enc_s_act3_brute","act":3,"tier":"Strong","enemyIds":["act3_brute"]}`
`enc_s_act3_mixed.json`: `{"id":"enc_s_act3_mixed","act":3,"tier":"Strong","enemyIds":["act3_grunt","act3_brute"]}`
`enc_e_act3_elite.json`: `{"id":"enc_e_act3_elite","act":3,"tier":"Elite","enemyIds":["act3_elite"]}`
`enc_b_act3_boss.json`: `{"id":"enc_b_act3_boss","act":3,"tier":"Boss","enemyIds":["act3_boss"]}`

- [ ] **Step 5: Verify RewardTable has act2/act3 definitions**

Run: `ls src/Core/Data/RewardTable/`

If only act1 exists, copy to act2.json / act3.json (Phase 10 で差別化、今は同形状で問題なし). Test がスキップされるのを避けるためだけに既存 act1 と同じ内容をコピーして `"id": "act2"` / `"id": "act3"` に書き換える。

- [ ] **Step 6: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~ActEncountersTests`
Expected: PASS

- [ ] **Step 7: Run all Core tests**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/Core/Data/Enemies/act* src/Core/Data/Encounters/enc_*_act* src/Core/Data/RewardTable/ tests/Core.Tests/Data/ActEncountersTests.cs
git commit -m "feat(core): add minimal act 2 / act 3 enemies, encounters, reward tables"
```

---

## Task 7: ActMapSeed derivation helper

**Files:**
- Create: `src/Core/Run/ActMapSeed.cs`
- Test: `tests/Core.Tests/Run/ActMapSeedTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActMapSeedTests
{
    [Fact]
    public void Deterministic_ForSameInputs()
    {
        var a = ActMapSeed.Derive(12345UL, 2);
        var b = ActMapSeed.Derive(12345UL, 2);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differs_ForDifferentActs()
    {
        var a = ActMapSeed.Derive(12345UL, 1);
        var b = ActMapSeed.Derive(12345UL, 2);
        var c = ActMapSeed.Derive(12345UL, 3);
        Assert.NotEqual(a, b);
        Assert.NotEqual(b, c);
        Assert.NotEqual(a, c);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~ActMapSeedTests`
Expected: FAIL

- [ ] **Step 3: Create ActMapSeed**

```csharp
namespace RoguelikeCardGame.Core.Run;

public static class ActMapSeed
{
    public static ulong Derive(ulong runSeed, int act)
        => unchecked(runSeed * 2654435761UL + (ulong)act);
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~ActMapSeedTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Run/ActMapSeed.cs tests/Core.Tests/Run/ActMapSeedTests.cs
git commit -m "feat(core): add ActMapSeed.Derive for per-act deterministic map seed"
```

---

## Task 8: ActTransition.AdvanceAct / FinishRun

**Files:**
- Create: `src/Core/Run/ActTransition.cs`
- Test: `tests/Core.Tests/Run/ActTransitionTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActTransitionTests
{
    private static DungeonMap FakeMap(int startNodeId)
        => new DungeonMap(
            StartNodeId: startNodeId,
            BossNodeId: startNodeId + 100,
            Nodes: ImmutableArray.Create(new MapNode(
                Id: startNodeId, Row: 0, Column: 0,
                Kind: TileKind.Start,
                OutgoingNodeIds: ImmutableArray<int>.Empty)));

    [Fact]
    public void AdvanceAct_IncrementsAct_HealsToMax_ResetsVisited()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            CurrentHp = 10,
            VisitedNodeIds = ImmutableArray.Create(1, 2, 3),
            CurrentNodeId = 3,
            ActiveReward = new RoguelikeCardGame.Core.Rewards.RewardState(
                Gold: 0, GoldClaimed: true, PotionId: null, PotionClaimed: true,
                CardChoices: ImmutableArray<string>.Empty,
                CardStatus: RoguelikeCardGame.Core.Rewards.CardRewardStatus.Skipped),
        };
        var newMap = FakeMap(999);
        var next = ActTransition.AdvanceAct(s, newMap, cat, new SystemRng(1));
        Assert.Equal(2, next.CurrentAct);
        Assert.Equal(s.MaxHp, next.CurrentHp);
        Assert.Equal(999, next.CurrentNodeId);
        Assert.Empty(next.VisitedNodeIds);
        Assert.Null(next.ActiveReward);
        Assert.Null(next.ActiveBattle);
        Assert.Null(next.ActiveMerchant);
        Assert.Null(next.ActiveEvent);
        Assert.False(next.ActiveRestPending);
    }

    [Fact]
    public void AdvanceAct_PreservesDeckRelicsGold()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            Gold = 500,
            Relics = new[] { "coin_purse" },
            Deck = ImmutableArray.Create(new CardInstance("strike", true)),
        };
        var next = ActTransition.AdvanceAct(s, FakeMap(1), cat, new SystemRng(1));
        Assert.Equal(500, next.Gold);
        Assert.Contains("coin_purse", next.Relics);
        Assert.Single(next.Deck);
    }

    [Fact]
    public void FinishRun_SetsProgress_AndUpdatesSavedAtUtc()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var before = s.SavedAtUtc;
        System.Threading.Thread.Sleep(10);
        var next = ActTransition.FinishRun(s, RunProgress.Cleared);
        Assert.Equal(RunProgress.Cleared, next.Progress);
        Assert.True(next.SavedAtUtc > before);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~ActTransitionTests`
Expected: FAIL

- [ ] **Step 3: Implement ActTransition**

```csharp
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Run;

public static class ActTransition
{
    public static RunState AdvanceAct(
        RunState state, DungeonMap newMap, DataCatalog catalog, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(newMap);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);

        int nextAct = state.CurrentAct + 1;
        var queueWeak = EncounterQueue.Initialize(new EnemyPool(nextAct, EnemyTier.Weak), catalog, rng);
        var queueStrong = EncounterQueue.Initialize(new EnemyPool(nextAct, EnemyTier.Strong), catalog, rng);
        var queueElite = EncounterQueue.Initialize(new EnemyPool(nextAct, EnemyTier.Elite), catalog, rng);
        var queueBoss = EncounterQueue.Initialize(new EnemyPool(nextAct, EnemyTier.Boss), catalog, rng);

        return state with
        {
            CurrentAct = nextAct,
            CurrentHp = state.MaxHp,
            CurrentNodeId = newMap.StartNodeId,
            VisitedNodeIds = ImmutableArray<int>.Empty,
            UnknownResolutions = ImmutableDictionary<int, TileKind>.Empty,
            ActiveBattle = null,
            ActiveReward = null,
            ActiveMerchant = null,
            ActiveEvent = null,
            ActiveRestPending = false,
            ActiveRestCompleted = false,
            ActiveActStartRelicChoice = null,
            EncounterQueueWeak = queueWeak,
            EncounterQueueStrong = queueStrong,
            EncounterQueueElite = queueElite,
            EncounterQueueBoss = queueBoss,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public static RunState FinishRun(RunState state, RunProgress outcome)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (outcome == RunProgress.InProgress)
            throw new ArgumentException("FinishRun cannot be called with InProgress", nameof(outcome));
        return state with
        {
            Progress = outcome,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
```

**注意:** `UnknownResolutions = ImmutableDictionary.Empty` で型が合わない場合、`ImmutableDictionary.Create<int, TileKind>()` を使う。

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~ActTransitionTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Run/ActTransition.cs tests/Core.Tests/Run/ActTransitionTests.cs
git commit -m "feat(core): add ActTransition.AdvanceAct and FinishRun"
```

---

## Task 9: ActStartActions.GenerateChoices / ChooseRelic

**Files:**
- Create: `src/Core/Run/ActStartActions.cs`
- Test: `tests/Core.Tests/Run/ActStartActionsTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActStartActionsTests
{
    [Fact]
    public void GenerateChoices_Returns3DistinctRelicsFromActPool()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var choice = ActStartActions.GenerateChoices(s, act: 1, cat, new SystemRng(42));
        Assert.Equal(3, choice.RelicIds.Length);
        Assert.Equal(3, choice.RelicIds.Distinct().Count());
        var pool = cat.ActStartRelicPools![1];
        foreach (var id in choice.RelicIds) Assert.Contains(id, pool);
    }

    [Fact]
    public void GenerateChoices_ExcludesOwnedRelics()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var pool = cat.ActStartRelicPools![1];
        var s = TestRunStates.FreshDefault(cat) with
        {
            Relics = new[] { pool[0], pool[1] },
        };
        var choice = ActStartActions.GenerateChoices(s, act: 1, cat, new SystemRng(1));
        Assert.DoesNotContain(pool[0], choice.RelicIds);
        Assert.DoesNotContain(pool[1], choice.RelicIds);
    }

    [Fact]
    public void ChooseRelic_AddsRelic_ClearsChoice()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var pool = cat.ActStartRelicPools![1];
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create(pool[0], pool[1], pool[2])),
        };
        var next = ActStartActions.ChooseRelic(s, pool[0], cat);
        Assert.Contains(pool[0], next.Relics);
        Assert.Null(next.ActiveActStartRelicChoice);
    }

    [Fact]
    public void ChooseRelic_InvalidId_Throws()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var pool = cat.ActStartRelicPools![1];
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create(pool[0], pool[1], pool[2])),
        };
        Assert.Throws<ArgumentException>(() =>
            ActStartActions.ChooseRelic(s, "not_in_choice", cat));
    }

    [Fact]
    public void ChooseRelic_OnPickup_AppliesEffects()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        // act1_start_01 は gainMaxHp +8
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create("act1_start_01", "act1_start_02", "act1_start_03")),
        };
        var next = ActStartActions.ChooseRelic(s, "act1_start_01", cat);
        Assert.Equal(s.MaxHp + 8, next.MaxHp);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~ActStartActionsTests`
Expected: FAIL

- [ ] **Step 3: Implement ActStartActions**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Run;

public static class ActStartActions
{
    public static ActStartRelicChoice GenerateChoices(
        RunState state, int act, DataCatalog catalog, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);
        if (catalog.ActStartRelicPools is null || !catalog.ActStartRelicPools.TryGetValue(act, out var pool))
            throw new InvalidOperationException($"act-start relic pool for act {act} not found");

        var owned = new HashSet<string>(state.Relics, StringComparer.Ordinal);
        var available = pool.Where(id => !owned.Contains(id)).ToList();
        if (available.Count < 3)
            throw new InvalidOperationException(
                $"act {act} pool does not have 3 unowned relics (available={available.Count})");

        // Fisher-Yates 部分抽選で 3 つ
        var picked = new List<string>(3);
        for (int i = 0; i < 3; i++)
        {
            int idx = rng.NextInt(available.Count);
            picked.Add(available[idx]);
            available.RemoveAt(idx);
        }
        return new ActStartRelicChoice(picked.ToImmutableArray());
    }

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
        return NonBattleRelicEffects.ApplyOnPickup(next, relicId, catalog);
    }
}
```

**注意:** `IRng.NextInt(int max)` が存在することを確認。もし違う API 名（`Next(int)` など）であれば既存コードに合わせる。

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~ActStartActionsTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Run/ActStartActions.cs tests/Core.Tests/Run/ActStartActionsTests.cs
git commit -m "feat(core): add ActStartActions (generate 3 choices, apply pickup)"
```

---

## Task 10: BossRewardFlow

**Files:**
- Create: `src/Core/Run/BossRewardFlow.cs`
- Test: `tests/Core.Tests/Run/BossRewardFlowTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class BossRewardFlowTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void GenerateBossReward_NonFinalAct_ReturnsRewardWithIsBossRewardTrue(int act)
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentAct = act };
        var r = BossRewardFlow.GenerateBossReward(s, cat, new SystemRng(1));
        Assert.NotNull(r);
        Assert.True(r!.IsBossReward);
    }

    [Fact]
    public void GenerateBossReward_FinalAct_ReturnsNull()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentAct = RunConstants.MaxAct };
        var r = BossRewardFlow.GenerateBossReward(s, cat, new SystemRng(1));
        Assert.Null(r);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~BossRewardFlowTests`
Expected: FAIL

- [ ] **Step 3: Implement BossRewardFlow**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

public static class BossRewardFlow
{
    public static RewardState? GenerateBossReward(
        RunState state, DataCatalog catalog, IRng rng)
    {
        if (state.CurrentAct >= RunConstants.MaxAct) return null;

        var tableId = $"act{state.CurrentAct}";
        if (!catalog.RewardTables.TryGetValue(tableId, out var table))
            table = catalog.RewardTables["act1"];

        var (reward, _) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(new EnemyPool(state.CurrentAct, EnemyTier.Boss)),
            state.RewardRngState,
            ImmutableArray.CreateRange(state.Relics),
            table, catalog, rng);
        return reward with { IsBossReward = true };
    }
}
```

**注意:** `RewardGenerator.Generate` の正確なシグネチャを一度読むこと（既存 `RunsController.PostBattleWin` で `ImmutableArray.Create("strike", "defend")` を渡しているが、これは `alreadyOwnedCardIds` ではなく「ベース除外カード」の可能性あり）。BossRewardFlow は `state.Relics` を渡すべき。既存の `RewardContext.FromEnemy` コンストラクタパターンに合わせる。

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~BossRewardFlowTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Run/BossRewardFlow.cs tests/Core.Tests/Run/BossRewardFlowTests.cs
git commit -m "feat(core): add BossRewardFlow (null for final act, IsBossReward=true otherwise)"
```

---

## Task 11: DebugActions.ApplyDamage

**Files:**
- Create: `src/Core/Run/DebugActions.cs`
- Test: `tests/Core.Tests/Run/DebugActionsTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class DebugActionsTests
{
    [Fact]
    public void ApplyDamage_SubtractsHp()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentHp = 50 };
        var next = DebugActions.ApplyDamage(s, 10);
        Assert.Equal(40, next.CurrentHp);
    }

    [Fact]
    public void ApplyDamage_ClampsAtZero()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentHp = 5 };
        var next = DebugActions.ApplyDamage(s, 100);
        Assert.Equal(0, next.CurrentHp);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~DebugActionsTests`
Expected: FAIL

- [ ] **Step 3: Implement DebugActions**

```csharp
using System;

namespace RoguelikeCardGame.Core.Run;

public static class DebugActions
{
    public static RunState ApplyDamage(RunState state, int amount)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int next = Math.Max(0, state.CurrentHp - amount);
        return state with { CurrentHp = next };
    }
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~DebugActionsTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Run/DebugActions.cs tests/Core.Tests/Run/DebugActionsTests.cs
git commit -m "feat(core): add DebugActions.ApplyDamage"
```

---

## Task 12: RunHistoryRecord + RunHistoryBuilder

**Files:**
- Create: `src/Core/History/RunHistoryRecord.cs`
- Create: `src/Core/History/RunHistoryBuilder.cs`
- Test: `tests/Core.Tests/History/RunHistoryBuilderTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.History;

public class RunHistoryBuilderTests
{
    [Fact]
    public void From_CopiesBasicFields()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            CurrentAct = 2,
            CurrentHp = 30,
            MaxHp = 80,
            Gold = 123,
            PlaySeconds = 456,
        };
        var rec = RunHistoryBuilder.From("acc_abc", s, nodesVisited: 7, RunProgress.GameOver);
        Assert.Equal(1, rec.SchemaVersion);
        Assert.Equal("acc_abc", rec.AccountId);
        Assert.Equal(s.RunId, rec.RunId);
        Assert.Equal(RunProgress.GameOver, rec.Outcome);
        Assert.Equal(2, rec.ActReached);
        Assert.Equal(30, rec.FinalHp);
        Assert.Equal(80, rec.FinalMaxHp);
        Assert.Equal(123, rec.FinalGold);
        Assert.Equal(456, rec.PlaySeconds);
        Assert.Equal(7, rec.NodesVisited);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~RunHistoryBuilderTests`
Expected: FAIL

- [ ] **Step 3: Implement History types**

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
    DateTimeOffset EndedAtUtc)
{
    public const int CurrentSchemaVersion = 1;
}
```

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
            EndedAtUtc: DateTimeOffset.UtcNow);
    }
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~RunHistoryBuilderTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/History/ tests/Core.Tests/History/
git commit -m "feat(core): add RunHistoryRecord and RunHistoryBuilder"
```

---

## Task 13: NodeEffectResolver — Start tile triggers ActStartRelicChoice

**Files:**
- Modify: `src/Core/Run/NodeEffectResolver.cs`
- Test: `tests/Core.Tests/Run/NodeEffectResolverTests.cs`（既存に追記）

- [ ] **Step 1: Write failing test**

Append to `tests/Core.Tests/Run/NodeEffectResolverTests.cs`:

```csharp
[Fact]
public void Resolve_Start_GeneratesActStartRelicChoice()
{
    var cat = EmbeddedDataLoader.LoadCatalog();
    var s = TestRunStates.FreshDefault(cat);
    var next = NodeEffectResolver.Resolve(s, TileKind.Start, currentRow: 0, cat, new SystemRng(1));
    Assert.NotNull(next.ActiveActStartRelicChoice);
    Assert.Equal(3, next.ActiveActStartRelicChoice!.RelicIds.Length);
}

[Fact]
public void Resolve_Start_UsesPoolForCurrentAct()
{
    var cat = EmbeddedDataLoader.LoadCatalog();
    var s = TestRunStates.FreshDefault(cat) with { CurrentAct = 2 };
    var next = NodeEffectResolver.Resolve(s, TileKind.Start, currentRow: 0, cat, new SystemRng(1));
    var pool = cat.ActStartRelicPools![2];
    foreach (var id in next.ActiveActStartRelicChoice!.RelicIds)
        Assert.Contains(id, pool);
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~NodeEffectResolverTests.Resolve_Start_GeneratesActStartRelicChoice`
Expected: FAIL (既存 `TileKind.Start => state` 無変更のため)

- [ ] **Step 3: Update NodeEffectResolver**

Edit `src/Core/Run/NodeEffectResolver.cs`:

```csharp
public static RunState Resolve(
    RunState state, TileKind kind, int currentRow, DataCatalog data, IRng rng)
{
    state = state with
    {
        ActiveMerchant = null,
        ActiveEvent = null,
        ActiveRestPending = false,
        ActiveRestCompleted = false,
        ActiveActStartRelicChoice = null,  // 通常遷移時はクリア
    };

    var tableId = $"act{state.CurrentAct}";
    if (!data.RewardTables.TryGetValue(tableId, out var table))
        table = data.RewardTables["act1"];

    return kind switch
    {
        TileKind.Start => state with {
            ActiveActStartRelicChoice = ActStartActions.GenerateChoices(state, state.CurrentAct, data, rng),
        },
        TileKind.Enemy => BattlePlaceholder.Start(state,
            RouteEnemyPool(table, state.CurrentAct, currentRow), data, rng),
        TileKind.Elite => BattlePlaceholder.Start(state,
            new EnemyPool(state.CurrentAct, EnemyTier.Elite), data, rng),
        TileKind.Boss => BattlePlaceholder.Start(state,
            new EnemyPool(state.CurrentAct, EnemyTier.Boss), data, rng),
        TileKind.Rest => state with { ActiveRestPending = true },
        TileKind.Merchant => StartMerchant(state, data, rng),
        TileKind.Treasure => StartTreasure(state, table, data, rng),
        TileKind.Event => StartEvent(state, data, rng),
        TileKind.Unknown => throw new ArgumentException("Unknown tile should be pre-resolved"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~NodeEffectResolverTests`
Expected: All pass

- [ ] **Step 5: Check existing "Resolve_ClearsActiveRestCompleted" and similar tests still pass**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj`
Expected: PASS (ActiveActStartRelicChoice は前のマスから引き継がないので、既存テストは影響なし)

- [ ] **Step 6: Commit**

```bash
git add src/Core/Run/NodeEffectResolver.cs tests/Core.Tests/Run/NodeEffectResolverTests.cs
git commit -m "feat(core): NodeEffectResolver generates act-start relic choice on Start tile"
```

---

## Task 14: Server — IHistoryRepository / FileHistoryRepository

**Files:**
- Create: `src/Server/Abstractions/IHistoryRepository.cs`
- Create: `src/Server/Services/FileBacked/FileHistoryRepository.cs`
- Test: `tests/Server.Tests/Services/FileHistoryRepositoryTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class FileHistoryRepositoryTests
{
    [Fact]
    public async Task AppendAndList_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hist_" + System.Guid.NewGuid().ToString("N"));
        var opts = Options.Create(new DataStorageOptions { RootDirectory = dir });
        IHistoryRepository repo = new FileHistoryRepository(opts);

        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = RunState.NewSoloRun(cat, 1UL, 10,
            System.Collections.Immutable.ImmutableDictionary<int, Core.Map.TileKind>.Empty,
            System.Collections.Immutable.ImmutableArray<string>.Empty,
            System.Collections.Immutable.ImmutableArray<string>.Empty,
            System.Collections.Immutable.ImmutableArray<string>.Empty,
            System.Collections.Immutable.ImmutableArray<string>.Empty,
            System.DateTimeOffset.UtcNow);
        var rec = RunHistoryBuilder.From("acc1", s, 3, RunProgress.Cleared);

        await repo.AppendAsync("acc1", rec, CancellationToken.None);
        var list = await repo.ListAsync("acc1", CancellationToken.None);
        Assert.Single(list);
        Assert.Equal(rec.RunId, list[0].RunId);

        Directory.Delete(dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~FileHistoryRepositoryTests`
Expected: FAIL (型が存在しない)

- [ ] **Step 3: Create interface**

`src/Server/Abstractions/IHistoryRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.History;

namespace RoguelikeCardGame.Server.Abstractions;

public interface IHistoryRepository
{
    Task AppendAsync(string accountId, RunHistoryRecord record, CancellationToken ct);
    Task<IReadOnlyList<RunHistoryRecord>> ListAsync(string accountId, CancellationToken ct);
}
```

- [ ] **Step 4: Create FileHistoryRepository**

`src/Server/Services/FileBacked/FileHistoryRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
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
            var rec = JsonSerializer.Deserialize<RunHistoryRecord>(json, JsonOptions.Default);
            if (rec is not null) list.Add(rec);
        }
        list.Sort((a, b) => b.EndedAtUtc.CompareTo(a.EndedAtUtc));
        return list;
    }
}
```

- [ ] **Step 5: Register in Program.cs**

Edit `src/Server/Program.cs` (find existing `AddSingleton<ISaveRepository, FileSaveRepository>()`):

```csharp
builder.Services.AddSingleton<IHistoryRepository, FileHistoryRepository>();
```

- [ ] **Step 6: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~FileHistoryRepositoryTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Server/Abstractions/IHistoryRepository.cs src/Server/Services/FileBacked/FileHistoryRepository.cs src/Server/Program.cs tests/Server.Tests/Services/FileHistoryRepositoryTests.cs
git commit -m "feat(server): add IHistoryRepository with file-backed implementation"
```

---

## Task 15: Server DTOs — ActStartRelicChoiceDto, RunResultDto, isBossReward, activeActStartRelicChoice

**Files:**
- Create: `src/Server/Dtos/ActStartRelicChoiceDto.cs`
- Create: `src/Server/Dtos/RunResultDto.cs`
- Create: `src/Server/Dtos/ActStartChooseRequestDto.cs`
- Create: `src/Server/Dtos/DebugDamageRequestDto.cs`
- Modify: `src/Server/Dtos/RewardStateDto.cs`
- Modify: `src/Server/Dtos/RunSnapshotDto.cs` (add field + mapper)
- Test: `tests/Server.Tests/Dtos/RunSnapshotDtoTests.cs`（既存があれば追記、なければ新規）

- [ ] **Step 1: Write failing test**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Dtos;

public class RunSnapshotDtoMapperPhase7Tests
{
    [Fact]
    public void Maps_ActiveActStartRelicChoice_WhenPresent()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create("a", "b", "c")),
        };
        var map = FakeMap.Tiny(s.CurrentNodeId);  // 既存の helper 名に合わせる
        var dto = RunSnapshotDtoMapper.From(s, map, cat);
        Assert.NotNull(dto.Run.ActiveActStartRelicChoice);
        Assert.Equal(new[] { "a", "b", "c" }, dto.Run.ActiveActStartRelicChoice!.RelicIds);
    }
}
```

**注意:** `FakeMap.Tiny` が既存に無ければ、test 内でインライン作成:

```csharp
var map = new RoguelikeCardGame.Core.Map.DungeonMap(
    StartNodeId: s.CurrentNodeId,
    BossNodeId: s.CurrentNodeId + 100,
    Nodes: ImmutableArray.Create(new RoguelikeCardGame.Core.Map.MapNode(
        Id: s.CurrentNodeId, Row: 0, Column: 0,
        Kind: RoguelikeCardGame.Core.Map.TileKind.Start,
        OutgoingNodeIds: ImmutableArray<int>.Empty)));
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~RunSnapshotDtoMapperPhase7Tests`
Expected: FAIL

- [ ] **Step 3: Create DTO files**

`src/Server/Dtos/ActStartRelicChoiceDto.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record ActStartRelicChoiceDto(IReadOnlyList<string> RelicIds);
```

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
    string EndedAtUtc);
```

`src/Server/Dtos/ActStartChooseRequestDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record ActStartChooseRequestDto(string RelicId);
```

`src/Server/Dtos/DebugDamageRequestDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record DebugDamageRequestDto(int Amount);
```

- [ ] **Step 4: Update RewardStateDto**

Edit `src/Server/Dtos/RewardStateDto.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RewardStateDto(
    int Gold, bool GoldClaimed,
    string? PotionId, bool PotionClaimed,
    IReadOnlyList<string> CardChoices,
    string CardStatus,
    string? RelicId,
    bool RelicClaimed,
    bool IsBossReward);
```

- [ ] **Step 5: Update RunSnapshotDto + mapper**

Edit `src/Server/Dtos/RunSnapshotDto.cs`:

Add field to `RunStateDto`:

```csharp
public sealed record RunStateDto(
    // ... 既存 positional parameters ...
    string SavedAtUtc,
    ActStartRelicChoiceDto? ActiveActStartRelicChoice);   // ← append
```

Update mapper body where `RewardStateDto` constructed:

```csharp
reward = new RewardStateDto(r.Gold, r.GoldClaimed, r.PotionId, r.PotionClaimed,
    r.CardChoices, r.CardStatus.ToString(), r.RelicId, r.RelicClaimed, r.IsBossReward);
```

Update final `new RunStateDto(...)` to include:

```csharp
activeEvent, s.ActiveRestPending, s.ActiveRestCompleted,
s.SavedAtUtc.ToString("O"),
s.ActiveActStartRelicChoice is null ? null : new ActStartRelicChoiceDto(s.ActiveActStartRelicChoice.RelicIds));
```

Also add a static helper inside the mapper class:

```csharp
public static RunResultDto ToResult(string accountId, RunState s, int nodesVisited, RunProgress outcome)
{
    var rec = RoguelikeCardGame.Core.History.RunHistoryBuilder.From(accountId, s, nodesVisited, outcome);
    return ToResultDto(rec);
}

public static RunResultDto ToResultDto(RoguelikeCardGame.Core.History.RunHistoryRecord rec)
{
    var deck = new List<RunResultCardDto>();
    foreach (var c in rec.FinalDeck) deck.Add(new RunResultCardDto(c.Id, c.Upgraded));
    return new RunResultDto(
        rec.SchemaVersion, rec.AccountId, rec.RunId,
        rec.Outcome.ToString(), rec.ActReached, rec.NodesVisited,
        rec.PlaySeconds, rec.CharacterId, rec.FinalHp, rec.FinalMaxHp, rec.FinalGold,
        deck, System.Linq.Enumerable.ToList(rec.FinalRelics),
        rec.EndedAtUtc.ToString("O"));
}
```

- [ ] **Step 6: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~RunSnapshotDtoMapperPhase7Tests`
Expected: PASS

- [ ] **Step 7: Run all Server tests**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj`
Expected: PASS. `RewardStateDto` を直接構築する既存テストがあれば、`isBossReward: false` を追記して修正。

- [ ] **Step 8: Commit**

```bash
git add src/Server/Dtos/ tests/Server.Tests/Dtos/
git commit -m "feat(server): add Phase 7 DTOs (act-start choice, run result, isBossReward)"
```

---

## Task 16: Server — ActStartController

**Files:**
- Create: `src/Server/Controllers/ActStartController.cs`
- Test: `tests/Server.Tests/Controllers/ActStartControllerTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Tests.Infra;  // 既存の WebApplicationFactory helper
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class ActStartControllerTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _fx;
    public ActStartControllerTests(ServerFixture fx) { _fx = fx; }

    [Fact]
    public async Task Choose_NoChoiceActive_Returns409()
    {
        var client = await _fx.NewAuthenticatedClient();
        await _fx.StartNewRun(client);
        // NewRun 直後は未 Start クリックなので choice は null (resolver 未発火)
        var resp = await client.PostAsJsonAsync("/api/v1/act-start/choose",
            new ActStartChooseRequestDto("act1_start_01"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Choose_InvalidRelicId_Returns422()
    {
        var client = await _fx.NewAuthenticatedClient();
        await _fx.StartNewRun(client);
        await _fx.MoveToStart(client);  // Start クリック → choice が生成される
        var resp = await client.PostAsJsonAsync("/api/v1/act-start/choose",
            new ActStartChooseRequestDto("not_a_real_relic"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Choose_Valid_Returns200_AndClearsChoice()
    {
        var client = await _fx.NewAuthenticatedClient();
        await _fx.StartNewRun(client);
        var snap = await _fx.MoveToStart(client);
        var picked = snap.Run.ActiveActStartRelicChoice!.RelicIds[0];
        var resp = await client.PostAsJsonAsync("/api/v1/act-start/choose",
            new ActStartChooseRequestDto(picked));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var next = await resp.Content.ReadFromJsonAsync<RunSnapshotDto>();
        Assert.Null(next!.Run.ActiveActStartRelicChoice);
        Assert.Contains(picked, next.Run.Relics);
    }
}
```

**注意:** `ServerFixture` / `StartNewRun` / `MoveToStart` ヘルパは既存 test infra に無ければ追加する。既存を `grep -rn "WebApplicationFactory" tests/Server.Tests/` で確認。

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~ActStartControllerTests`
Expected: FAIL (controller 未定義)

- [ ] **Step 3: Create controller**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/act-start")]
public sealed class ActStartController : ControllerBase
{
    public const string AccountHeader = RunsController.AccountHeader;

    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly RunStartService _runStart;
    private readonly DataCatalog _data;

    public ActStartController(IAccountRepository a, ISaveRepository s, RunStartService r, DataCatalog d)
    { _accounts = a; _saves = s; _runStart = r; _data = d; }

    [HttpPost("choose")]
    public async Task<IActionResult> Choose([FromBody] ActStartChooseRequestDto body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrEmpty(body.RelicId)) return BadRequest();
        if (!TryAcc(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (s.ActiveActStartRelicChoice is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "act-start relic 選択中ではありません。");

        RunState updated;
        try { updated = ActStartActions.ChooseRelic(s, body.RelicId, _data); }
        catch (ArgumentException ex)
        { return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ex.Message); }

        // Start tile を visited に加える（Phase 7 spec §Start クリック処理）
        if (!updated.VisitedNodeIds.Contains(updated.CurrentNodeId))
            updated = updated with { VisitedNodeIds = updated.VisitedNodeIds.Add(updated.CurrentNodeId) };
        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);

        var map = _runStart.RehydrateMap(updated.RngSeed);
        return Ok(RunSnapshotDtoMapper.From(updated, map, _data));
    }

    private bool TryAcc(out string id, out IActionResult? err)
    {
        id = string.Empty; err = null;
        if (!Request.Headers.TryGetValue(AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        { err = Problem(statusCode: StatusCodes.Status400BadRequest, title: $"ヘッダ {AccountHeader} が必要"); return false; }
        var c = raw.ToString();
        try { AccountIdValidator.Validate(c); }
        catch (ArgumentException ex)
        { err = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message); return false; }
        id = c; return true;
    }
}
```

**注意:** `RehydrateMap` は act 1 しか考慮していない（単一 seed）。現時点では act 遷移で `RngSeed` を書き換える方針で、RehydrateMap は常に「現在の act のマップ」を返す必要がある。→ Task 17 で RunStartService を改修する際にここを合わせて直す。

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~ActStartControllerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Server/Controllers/ActStartController.cs tests/Server.Tests/Controllers/ActStartControllerTests.cs
git commit -m "feat(server): add POST /api/v1/act-start/choose"
```

---

## Task 17: Server — RunStartService で ActMapSeed を採用 + act 遷移対応

**Files:**
- Modify: `src/Server/Services/RunStartService.cs`
- Test: `tests/Server.Tests/Services/RunStartServiceActTransitionTests.cs`

**方針:** `RehydrateMap(rngSeed, act)` オーバーロードを追加し、内部で `ActMapSeed.Derive(rngSeed, act)` を使用。旧シグネチャ `RehydrateMap(ulong)` は `act = 1` 前提に残す（既存 caller を壊さないため）か、すべての caller (`RunsController`, `ActStartController`) を `(rngSeed, currentAct)` 版に差し替える。後者が好ましい。

- [ ] **Step 1: Write failing test**

```csharp
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class RunStartServiceActTransitionTests
{
    [Fact]
    public void RehydrateMap_DifferentAct_YieldsDifferentMaps()
    {
        // 既存 test helper で RunStartService を組み立てる（FakeGenerator + MapConfig）
        // 本テストは ActMapSeed.Derive の結果が変わり、結果として map structure が変わることを検証
        var svc = TestHelpers.BuildRunStartServiceWithDefaults();
        var map1 = svc.RehydrateMap(rngSeed: 42UL, act: 1);
        var map2 = svc.RehydrateMap(rngSeed: 42UL, act: 2);
        Assert.NotEqual(map1.Nodes.Length, map2.Nodes.Length);  // 違う seed → 違う構造
    }
}
```

**注意:** DungeonMap は `record` なので、完全一致が起きる可能性がある。もし `NotEqual` が不安定なら「StartNodeId か Nodes.First().Kind などの何かしら違う値」で比較する。

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~RunStartServiceActTransitionTests`
Expected: FAIL

- [ ] **Step 3: Refactor RunStartService**

Edit `src/Server/Services/RunStartService.cs`:

```csharp
public DungeonMap RehydrateMap(ulong rngSeed, int act = 1)
{
    var derived = ActMapSeed.Derive(rngSeed, act);
    int seed = unchecked((int)(uint)derived);
    return _generator.Generate(new SystemRng(seed), _mapConfig);
}
```

Also update `StartAsync` to pre-apply `ActMapSeed.Derive(seed, 1)` so that subsequent `RehydrateMap(seed, 1)` matches. Easiest: keep `rngSeed` as the "run seed" (raw), and always funnel through `ActMapSeed.Derive` in both `StartAsync` map generation and `RehydrateMap`.

```csharp
for (int attempt = 0; attempt < MaxSeedAttempts; attempt++)
{
    seed = _seedSource();
    var act1Seed = unchecked((int)(uint)ActMapSeed.Derive((ulong)(uint)seed, 1));
    try
    {
        map = _generator.Generate(new SystemRng(act1Seed), _mapConfig);
        break;
    }
    catch (MapGenerationException ex) { last = ex; }
}
```

- [ ] **Step 4: Update callers**

Find all `RehydrateMap(state.RngSeed)` and change to `RehydrateMap(state.RngSeed, state.CurrentAct)`:

Run: `grep -rn "RehydrateMap(" src/Server/`

Update in:
- `RunsController.GetCurrent`
- `RunsController.PostMove`
- `ActStartController.Choose`

- [ ] **Step 5: Delete existing test.json if incompatible**

既存の `test.json` は act 1 のマップを persisted している状態と矛盾する可能性がある（seed と map の関係が変わる）。E2E 確認時に必要なら手動再生成。Server.Tests 側で seed を控えめに（default_seeded）している場合、test を書き換える。

Run: `grep -rn "test.json" tests/Server.Tests/` — 参照箇所があれば影響範囲確認。

- [ ] **Step 6: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~RunStartServiceActTransitionTests`
Expected: PASS

- [ ] **Step 7: Run all tests**

Run: `dotnet test`
Expected: すべて PASS。失敗するものがあれば既存 caller を修正。

- [ ] **Step 8: Commit**

```bash
git add src/Server/Services/RunStartService.cs src/Server/Controllers/RunsController.cs src/Server/Controllers/ActStartController.cs tests/Server.Tests/Services/RunStartServiceActTransitionTests.cs
git commit -m "refactor(server): derive per-act map seed via ActMapSeed"
```

---

## Task 18: Server — battle/win で Boss 分岐 + Cleared / GameOver ハンドリング

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs`
- Test: `tests/Server.Tests/Controllers/BossWinFlowTests.cs`

**動作:**
- 勝利 encounter が Boss でかつ currentAct < MaxAct → `RewardState { IsBossReward: true }` を生成
- 勝利 encounter が Boss でかつ currentAct == MaxAct → `ActTransition.FinishRun(Cleared)` → 履歴保存 → current run 削除 → `RunResultDto` 200 返却
- それ以外: 従来通り

- [ ] **Step 1: Write failing test**

```csharp
using System.Linq;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class BossWinFlowTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _fx;
    public BossWinFlowTests(ServerFixture fx) { _fx = fx; }

    [Fact]
    public async Task Act1Boss_Win_SetsIsBossRewardTrue()
    {
        var (client, acc) = await _fx.SetupRunInBossBattle(act: 1);
        var resp = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win",
            new BattleWinRequestDto(ElapsedSeconds: 0));
        resp.EnsureSuccessStatusCode();
        var snap = await _fx.GetCurrent(client);
        Assert.True(snap!.Run.ActiveReward!.IsBossReward);
    }

    [Fact]
    public async Task Act3Boss_Win_ReturnsRunResult_AndDeletesCurrent()
    {
        var (client, acc) = await _fx.SetupRunInBossBattle(act: RunConstants.MaxAct);
        var resp = await client.PostAsJsonAsync("/api/v1/runs/current/battle/win",
            new BattleWinRequestDto(ElapsedSeconds: 0));
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<RunResultDto>();
        Assert.Equal("Cleared", result!.Outcome);
        var current = await _fx.GetCurrent(client);
        Assert.Null(current);  // current run 削除済み
        var history = await _fx.ListHistory(client);
        Assert.Contains(history, h => h.RunId == result.RunId && h.Outcome == "Cleared");
    }
}
```

**注意:** `SetupRunInBossBattle(act)` は infra helper。`RunState` を直接 save repo に書き込むか、あるいは `BattleStateDto` を持つ既存のテスト用 endpoint があれば使う。無ければ `_fx` に追加する。

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~BossWinFlowTests`
Expected: FAIL

- [ ] **Step 3: Modify PostBattleWin**

Edit `src/Server/Controllers/RunsController.cs` — PostBattleWin:

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
    var enc = _data.Encounters[afterWin.ActiveBattle!.EncounterId];
    bool isBoss = enc.Pool.Tier == RoguelikeCardGame.Core.Enemy.EnemyTier.Boss;

    long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);

    if (isBoss && afterWin.CurrentAct == RunConstants.MaxAct)
    {
        var finished = ActTransition.FinishRun(afterWin with
        {
            ActiveBattle = null,
            PlaySeconds = afterWin.PlaySeconds + elapsed,
        }, RunProgress.Cleared);
        var rec = RunHistoryBuilder.From(accountId, finished, finished.VisitedNodeIds.Length, RunProgress.Cleared);
        await _history.AppendAsync(accountId, rec, ct);
        await _saves.DeleteAsync(accountId, ct);
        return Ok(RunSnapshotDtoMapper.ToResultDto(rec));
    }

    RewardRngState newRng;
    Rewards.RewardState reward;
    var rewardRng = new SystemRng(unchecked((int)s.RngSeed ^ (int)s.PlaySeconds ^ 0x5EED));
    if (isBoss)
    {
        var r = BossRewardFlow.GenerateBossReward(afterWin, _data, rewardRng);
        reward = r!;  // act < MaxAct なので non-null
        newRng = afterWin.RewardRngState;  // BossRewardFlow は RewardRngState を更新しない簡易実装
    }
    else
    {
        var (r, nr) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(enc.Pool),
            afterWin.RewardRngState,
            System.Collections.Immutable.ImmutableArray.CreateRange(s.Relics),
            _data.RewardTables.TryGetValue($"act{s.CurrentAct}", out var tbl) ? tbl : _data.RewardTables["act1"],
            _data, rewardRng);
        reward = r; newRng = nr;
    }

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
```

**注意:** `IHistoryRepository _history` を DI する必要がある。Ctor に `IHistoryRepository history` を追加し、field 保持。

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~BossWinFlowTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/BossWinFlowTests.cs
git commit -m "feat(server): boss win — IsBossReward for non-final act, Cleared+history for act 3"
```

---

## Task 19: Server — reward/proceed で Boss reward ならば AdvanceAct

**Files:**
- Modify: `src/Server/Controllers/RunsController.cs` (PostRewardProceed)
- Test: `tests/Server.Tests/Controllers/RewardProceedActTransitionTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public async Task Proceed_OnBossReward_AdvancesActAndHealsFull()
{
    var (client, acc) = await _fx.SetupRunWithBossReward(act: 1);
    var before = await _fx.GetCurrent(client);
    Assert.Equal(1, before!.Run.CurrentAct);

    await client.PostAsJsonAsync("/api/v1/runs/current/reward/proceed",
        new RewardProceedRequestDto(ElapsedSeconds: 0));

    var after = await _fx.GetCurrent(client);
    Assert.Equal(2, after!.Run.CurrentAct);
    Assert.Equal(after.Run.MaxHp, after.Run.CurrentHp);
    Assert.Null(after.Run.ActiveReward);
    Assert.Empty(after.Run.VisitedNodeIds);
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~RewardProceedActTransitionTests`
Expected: FAIL

- [ ] **Step 3: Modify PostRewardProceed**

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

    long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);

    RunState updated;
    if (s.ActiveReward.IsBossReward && s.CurrentAct < RunConstants.MaxAct)
    {
        // act 遷移: 新マップ生成 → AdvanceAct
        int nextAct = s.CurrentAct + 1;
        var act2Seed = unchecked((int)(uint)ActMapSeed.Derive(s.RngSeed, nextAct));
        var newMap = _runStart.RehydrateMap(s.RngSeed, nextAct);
        var advanceRng = new SystemRng(unchecked(act2Seed ^ 0xAC70));
        updated = ActTransition.AdvanceAct(s, newMap, _data, advanceRng);
        updated = updated with { PlaySeconds = updated.PlaySeconds + elapsed };
    }
    else
    {
        try { updated = RewardApplier.Proceed(s); }
        catch (InvalidOperationException ex)
        { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message); }
        updated = updated with
        {
            PlaySeconds = updated.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    await _saves.SaveAsync(accountId, updated, ct);
    return NoContent();
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~RewardProceedActTransitionTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/RewardProceedActTransitionTests.cs
git commit -m "feat(server): reward/proceed advances act when boss reward claimed"
```

---

## Task 20: Server — DebugController (dev only)

**Files:**
- Create: `src/Server/Controllers/DebugController.cs`
- Modify: `src/Server/Program.cs` — conditional endpoint registration
- Test: `tests/Server.Tests/Controllers/DebugControllerTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public async Task Damage_ReducesHp()
{
    // ServerFixture は Environment=Development で起動
    var (client, _) = await _fx.SetupRunInMap();
    var before = await _fx.GetCurrent(client);
    var resp = await client.PostAsJsonAsync("/api/v1/debug/damage",
        new DebugDamageRequestDto(Amount: 10));
    resp.EnsureSuccessStatusCode();
    var after = await _fx.GetCurrent(client);
    Assert.Equal(before!.Run.CurrentHp - 10, after!.Run.CurrentHp);
}

[Fact]
public async Task Damage_HpReachesZero_ReturnsRunResultAndDeletesCurrent()
{
    var (client, _) = await _fx.SetupRunInMap();
    var resp = await client.PostAsJsonAsync("/api/v1/debug/damage",
        new DebugDamageRequestDto(Amount: 9999));
    resp.EnsureSuccessStatusCode();
    var result = await resp.Content.ReadFromJsonAsync<RunResultDto>();
    Assert.Equal("GameOver", result!.Outcome);
    Assert.Null(await _fx.GetCurrent(client));
}
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~DebugControllerTests`
Expected: FAIL

- [ ] **Step 3: Create controller**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/debug")]
public sealed class DebugController : ControllerBase
{
    private readonly IHostEnvironment _env;
    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly IHistoryRepository _history;
    private readonly RunStartService _runStart;
    private readonly DataCatalog _data;

    public DebugController(
        IHostEnvironment env, IAccountRepository a, ISaveRepository s,
        IHistoryRepository h, RunStartService rs, DataCatalog d)
    { _env = env; _accounts = a; _saves = s; _history = h; _runStart = rs; _data = d; }

    [HttpPost("damage")]
    public async Task<IActionResult> Damage([FromBody] DebugDamageRequestDto body, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (body is null || body.Amount <= 0) return BadRequest();
        if (!TryAcc(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "アカウントなし");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランなし");

        var damaged = DebugActions.ApplyDamage(s, body.Amount);
        if (damaged.CurrentHp <= 0)
        {
            var finished = ActTransition.FinishRun(damaged, RunProgress.GameOver);
            var rec = RunHistoryBuilder.From(accountId, finished, finished.VisitedNodeIds.Length, RunProgress.GameOver);
            await _history.AppendAsync(accountId, rec, ct);
            await _saves.DeleteAsync(accountId, ct);
            return Ok(RunSnapshotDtoMapper.ToResultDto(rec));
        }

        damaged = damaged with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, damaged, ct);
        var map = _runStart.RehydrateMap(damaged.RngSeed, damaged.CurrentAct);
        return Ok(RunSnapshotDtoMapper.From(damaged, map, _data));
    }

    private bool TryAcc(out string id, out IActionResult? err)
    {
        id = string.Empty; err = null;
        if (!Request.Headers.TryGetValue(RunsController.AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        { err = Problem(statusCode: 400, title: "account header missing"); return false; }
        id = raw.ToString();
        try { AccountIdValidator.Validate(id); }
        catch (ArgumentException ex) { err = Problem(statusCode: 400, title: ex.Message); return false; }
        return true;
    }
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~DebugControllerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Server/Controllers/DebugController.cs tests/Server.Tests/Controllers/DebugControllerTests.cs
git commit -m "feat(server): dev-only POST /api/v1/debug/damage — GameOver on 0 HP"
```

---

## Task 21: Server — HistoryController + RunsController.Abandon で履歴保存

**Files:**
- Create: `src/Server/Controllers/HistoryController.cs`
- Modify: `src/Server/Controllers/RunsController.cs` (PostAbandon)
- Test: `tests/Server.Tests/Controllers/HistoryControllerTests.cs`, `tests/Server.Tests/Controllers/AbandonHistoryTests.cs`

- [ ] **Step 1: Write failing test for history listing**

```csharp
[Fact]
public async Task LastResult_ReturnsLatest()
{
    var (client, acc) = await _fx.SetupRunAndTriggerGameOver();
    var resp = await client.GetAsync("/api/v1/history/last-result");
    resp.EnsureSuccessStatusCode();
    var r = await resp.Content.ReadFromJsonAsync<RunResultDto>();
    Assert.Equal("GameOver", r!.Outcome);
}
```

- [ ] **Step 2: Write failing test for abandon saves history**

```csharp
[Fact]
public async Task Abandon_SavesHistory_AndDeletesCurrent()
{
    var (client, acc) = await _fx.SetupRunInMap();
    await client.PostAsJsonAsync("/api/v1/runs/current/abandon",
        new HeartbeatRequestDto(ElapsedSeconds: 0));
    Assert.Null(await _fx.GetCurrent(client));
    var history = await _fx.ListHistory(client);
    Assert.Contains(history, h => h.Outcome == "Abandoned");
}
```

- [ ] **Step 3: Run tests, expect FAIL**

Run: `dotnet test --filter FullyQualifiedName~HistoryControllerTests`
Expected: FAIL

- [ ] **Step 4: Create HistoryController**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/history")]
public sealed class HistoryController : ControllerBase
{
    private readonly IAccountRepository _accounts;
    private readonly IHistoryRepository _history;

    public HistoryController(IAccountRepository a, IHistoryRepository h)
    { _accounts = a; _history = h; }

    [HttpGet("")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!TryAcc(out var acc, out var err)) return err!;
        if (!await _accounts.ExistsAsync(acc, ct))
            return Problem(statusCode: 404, title: "アカウントなし");
        var list = await _history.ListAsync(acc, ct);
        var dtos = new List<RunResultDto>();
        foreach (var rec in list) dtos.Add(RunSnapshotDtoMapper.ToResultDto(rec));
        return Ok(dtos);
    }

    [HttpGet("last-result")]
    public async Task<IActionResult> LastResult(CancellationToken ct)
    {
        if (!TryAcc(out var acc, out var err)) return err!;
        if (!await _accounts.ExistsAsync(acc, ct))
            return Problem(statusCode: 404, title: "アカウントなし");
        var list = await _history.ListAsync(acc, ct);
        if (list.Count == 0) return NoContent();
        return Ok(RunSnapshotDtoMapper.ToResultDto(list[0]));
    }

    private bool TryAcc(out string id, out IActionResult? err)
    {
        id = string.Empty; err = null;
        if (!Request.Headers.TryGetValue(RunsController.AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        { err = Problem(statusCode: 400, title: "account header missing"); return false; }
        id = raw.ToString();
        try { AccountIdValidator.Validate(id); }
        catch (System.ArgumentException ex) { err = Problem(statusCode: 400, title: ex.Message); return false; }
        return true;
    }
}
```

- [ ] **Step 5: Update PostAbandon**

Edit `RunsController.PostAbandon`:

```csharp
[HttpPost("current/abandon")]
public async Task<IActionResult> PostAbandon([FromBody] HeartbeatRequestDto body, CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;
    if (!await _accounts.ExistsAsync(accountId, ct))
        return Problem(statusCode: 404, title: "アカウントなし");

    var state = await _saves.TryLoadAsync(accountId, ct);
    if (state is null || state.Progress != RunProgress.InProgress)
        return Problem(statusCode: 409, title: "進行中のランなし");

    long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
    var finished = ActTransition.FinishRun(state with
    {
        PlaySeconds = state.PlaySeconds + elapsed,
    }, RunProgress.Abandoned);
    var rec = RunHistoryBuilder.From(accountId, finished, finished.VisitedNodeIds.Length, RunProgress.Abandoned);
    await _history.AppendAsync(accountId, rec, ct);
    await _saves.DeleteAsync(accountId, ct);
    return NoContent();
}
```

- [ ] **Step 6: Run tests, expect PASS**

Run: `dotnet test --filter FullyQualifiedName~HistoryControllerTests`
Run: `dotnet test --filter FullyQualifiedName~AbandonHistoryTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Server/Controllers/HistoryController.cs src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/HistoryControllerTests.cs tests/Server.Tests/Controllers/AbandonHistoryTests.cs
git commit -m "feat(server): add history endpoints; abandon now saves history"
```

---

## Task 22: Client — API wrappers + types.ts 更新

**Files:**
- Create: `src/Client/src/api/actStart.ts`
- Create: `src/Client/src/api/debug.ts`
- Create: `src/Client/src/api/history.ts`
- Modify: `src/Client/src/api/types.ts`

- [ ] **Step 1: Update types.ts**

Edit `src/Client/src/api/types.ts`:

```ts
export type ActStartRelicChoiceDto = {
  relicIds: string[]
}

export type RunResultCardDto = {
  id: string
  upgraded: boolean
}

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
}
```

Also update `RewardStateDto`:
```ts
export type RewardStateDto = {
  gold: number
  goldClaimed: boolean
  potionId: string | null
  potionClaimed: boolean
  cardChoices: string[]
  cardStatus: CardRewardStatus
  relicId: string | null
  relicClaimed: boolean
  isBossReward: boolean
}
```

And `RunStateDto` — append:
```ts
  savedAtUtc: string
  activeActStartRelicChoice: ActStartRelicChoiceDto | null
}
```

- [ ] **Step 2: Create api/actStart.ts**

```ts
import { apiRequest } from './client'
import type { RunSnapshotDto } from './types'

export async function chooseActStartRelic(accountId: string, relicId: string): Promise<RunSnapshotDto> {
  return await apiRequest<RunSnapshotDto>('POST', '/act-start/choose', {
    accountId,
    body: { relicId },
  })
}
```

- [ ] **Step 3: Create api/debug.ts**

```ts
import { apiRequest } from './client'
import type { RunSnapshotDto, RunResultDto } from './types'

export async function applyDebugDamage(
  accountId: string,
  amount: number,
): Promise<RunSnapshotDto | RunResultDto> {
  return await apiRequest<RunSnapshotDto | RunResultDto>('POST', '/debug/damage', {
    accountId,
    body: { amount },
  })
}
```

- [ ] **Step 4: Create api/history.ts**

```ts
import { apiRequest } from './client'
import type { RunResultDto } from './types'

export async function getLastResult(accountId: string): Promise<RunResultDto | null> {
  return await apiRequest<RunResultDto | null>('GET', '/history/last-result', { accountId })
}

export async function getHistory(accountId: string): Promise<RunResultDto[]> {
  return await apiRequest<RunResultDto[]>('GET', '/history', { accountId })
}
```

- [ ] **Step 5: Run client type-check**

Run: `cd src/Client && npx tsc --noEmit`
Expected: No type errors.

- [ ] **Step 6: Commit**

```bash
git add src/Client/src/api/types.ts src/Client/src/api/actStart.ts src/Client/src/api/debug.ts src/Client/src/api/history.ts
git commit -m "feat(client): add Phase 7 API wrappers and DTO types"
```

---

## Task 23: Client — ActStartRelicScreen

**Files:**
- Create: `src/Client/src/screens/ActStartRelicScreen.tsx`
- Test: `src/Client/src/screens/ActStartRelicScreen.test.tsx`

- [ ] **Step 1: Write failing test**

```tsx
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ActStartRelicScreen } from './ActStartRelicScreen'

describe('ActStartRelicScreen', () => {
  it('renders 3 relic buttons and calls onChoose', () => {
    const onChoose = vi.fn()
    render(
      <ActStartRelicScreen
        choices={['r1', 'r2', 'r3']}
        relicNames={{ r1: 'Relic 1', r2: 'Relic 2', r3: 'Relic 3' }}
        onChoose={onChoose}
      />,
    )
    const buttons = screen.getAllByRole('button', { name: /Relic \d/i })
    expect(buttons).toHaveLength(3)
    fireEvent.click(buttons[1])
    expect(onChoose).toHaveBeenCalledWith('r2')
  })

  it('has dialog role', () => {
    render(
      <ActStartRelicScreen choices={['r1', 'r2', 'r3']} relicNames={{}} onChoose={vi.fn()} />,
    )
    const dlg = screen.getByRole('dialog')
    expect(dlg.getAttribute('aria-modal')).toBe('true')
  })
})
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `cd src/Client && npm test -- ActStartRelicScreen.test`
Expected: FAIL (コンポーネント未定義)

- [ ] **Step 3: Create component**

```tsx
import { Button } from '../components/Button'

type Props = {
  choices: string[]
  relicNames: Record<string, string>
  onChoose: (relicId: string) => void
}

export function ActStartRelicScreen({ choices, relicNames, onChoose }: Props) {
  return (
    <div className="act-start-relic-screen" role="dialog" aria-modal="true">
      <h2>層開始のレリックを選ぶ</h2>
      <ul>
        {choices.map(id => (
          <li key={id}>
            <Button onClick={() => onChoose(id)} aria-label={relicNames[id] ?? id}>
              {relicNames[id] ?? id}
            </Button>
          </li>
        ))}
      </ul>
    </div>
  )
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `cd src/Client && npm test -- ActStartRelicScreen.test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Client/src/screens/ActStartRelicScreen.tsx src/Client/src/screens/ActStartRelicScreen.test.tsx
git commit -m "feat(client): add ActStartRelicScreen component"
```

---

## Task 24: Client — RunResultScreen

**Files:**
- Create: `src/Client/src/screens/RunResultScreen.tsx`
- Test: `src/Client/src/screens/RunResultScreen.test.tsx`

- [ ] **Step 1: Write failing test**

```tsx
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { RunResultDto } from '../api/types'
import { RunResultScreen } from './RunResultScreen'

const sample: RunResultDto = {
  schemaVersion: 1,
  accountId: 'acc',
  runId: 'run1',
  outcome: 'Cleared',
  actReached: 3,
  nodesVisited: 42,
  playSeconds: 3725,
  characterId: 'default',
  finalHp: 80,
  finalMaxHp: 100,
  finalGold: 500,
  finalDeck: [{ id: 'strike', upgraded: true }],
  finalRelics: ['coin_purse'],
  endedAtUtc: '2026-04-22T00:00:00Z',
}

describe('RunResultScreen', () => {
  it('shows outcome, act reached, nodes, play seconds', () => {
    render(<RunResultScreen result={sample} onReturnToMenu={vi.fn()} />)
    expect(screen.getByText(/Cleared/)).toBeDefined()
    expect(screen.getByText(/Act 3/)).toBeDefined()
    expect(screen.getByText(/42/)).toBeDefined()
    expect(screen.getByText(/01:02:05/)).toBeDefined()  // 3725s = 1h 2m 5s
  })

  it('calls onReturnToMenu', () => {
    const cb = vi.fn()
    render(<RunResultScreen result={sample} onReturnToMenu={cb} />)
    fireEvent.click(screen.getByRole('button', { name: /メニュー/i }))
    expect(cb).toHaveBeenCalled()
  })
})
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `cd src/Client && npm test -- RunResultScreen.test`
Expected: FAIL

- [ ] **Step 3: Create component**

```tsx
import type { RunResultDto } from '../api/types'
import { Button } from '../components/Button'

type Props = {
  result: RunResultDto
  onReturnToMenu: () => void
}

function formatSeconds(total: number): string {
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

export function RunResultScreen({ result, onReturnToMenu }: Props) {
  return (
    <div className="run-result-screen" role="dialog" aria-modal="true">
      <h1>{result.outcome}</h1>
      <dl>
        <dt>到達層</dt><dd>Act {result.actReached}</dd>
        <dt>訪問ノード数</dt><dd>{result.nodesVisited}</dd>
        <dt>プレイ時間</dt><dd>{formatSeconds(result.playSeconds)}</dd>
        <dt>HP</dt><dd>{result.finalHp} / {result.finalMaxHp}</dd>
        <dt>Gold</dt><dd>{result.finalGold}</dd>
      </dl>
      <section>
        <h2>レリック</h2>
        <ul>{result.finalRelics.map(r => <li key={r}>{r}</li>)}</ul>
      </section>
      <section>
        <h2>デッキ ({result.finalDeck.length})</h2>
        <ul>{result.finalDeck.map((c, i) => (
          <li key={i}>{c.id}{c.upgraded ? '+' : ''}</li>
        ))}</ul>
      </section>
      <Button onClick={onReturnToMenu}>メニューへ戻る</Button>
    </div>
  )
}
```

- [ ] **Step 4: Run test, expect PASS**

Run: `cd src/Client && npm test -- RunResultScreen.test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Client/src/screens/RunResultScreen.tsx src/Client/src/screens/RunResultScreen.test.tsx
git commit -m "feat(client): add RunResultScreen"
```

---

## Task 25: Client — RewardPopup で Boss reward ラベル切替

**Files:**
- Modify: `src/Client/src/screens/RewardPopup.tsx`
- Modify: `src/Client/src/screens/RewardPopup.test.tsx`

- [ ] **Step 1: Write failing test**

Append to `RewardPopup.test.tsx`:

```tsx
it('shows "次の層へ" label when reward.isBossReward is true and currentAct < MaxAct', () => {
  const reward = { gold: 0, goldClaimed: true, potionId: null, potionClaimed: true,
    cardChoices: [], cardStatus: 'Skipped' as const, relicId: null, relicClaimed: true,
    isBossReward: true }
  render(<RewardPopup reward={reward} currentAct={1} onProceed={vi.fn()} {/* 他の props */} />)
  expect(screen.getByRole('button', { name: /次の層へ/ })).toBeDefined()
})

it('shows default "次へ" label when reward.isBossReward is false', () => {
  const reward = { gold: 0, goldClaimed: true, potionId: null, potionClaimed: true,
    cardChoices: [], cardStatus: 'Skipped' as const, relicId: null, relicClaimed: true,
    isBossReward: false }
  render(<RewardPopup reward={reward} currentAct={1} onProceed={vi.fn()} {/* 他の props */} />)
  expect(screen.queryByRole('button', { name: /次の層へ/ })).toBeNull()
})
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `cd src/Client && npm test -- RewardPopup.test`
Expected: FAIL

- [ ] **Step 3: Update RewardPopup**

Read the existing file first to find the proceed button. Add `currentAct` to props and use `reward.isBossReward && currentAct < 3` for the label:

```tsx
const proceedLabel = reward.isBossReward ? '次の層へ' : '次へ'
// ...
<Button onClick={onProceed}>{proceedLabel}</Button>
```

Update MapScreen / RewardPopup caller to pass `currentAct={snap.run.currentAct}`.

- [ ] **Step 4: Run test, expect PASS**

Run: `cd src/Client && npm test -- RewardPopup.test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Client/src/screens/RewardPopup.tsx src/Client/src/screens/RewardPopup.test.tsx src/Client/src/screens/MapScreen.tsx
git commit -m "feat(client): RewardPopup shows 次の層へ when boss reward"
```

---

## Task 26: Client — BattleOverlay に DEBUG -10HP ボタン

**Files:**
- Modify: `src/Client/src/screens/BattleOverlay.tsx`
- Modify: `src/Client/src/screens/BattleOverlay.test.tsx`

**注意:** TopBar ファイルは現在の `src/Client/src/screens/` に存在しない。BattleOverlay とは別に、トップレベルで表示される HP 表示コンポーネントが存在するはず。MapScreen で HP 表示がインラインならそこに追加する。探して対応する。

- [ ] **Step 1: Grep for existing HP display**

Run: `grep -rn "currentHp" src/Client/src/screens/ | head`

対象を特定。BattleOverlay と、もし存在すれば map 画面の top 部品 (MapScreen か InGameMenuScreen 等) にボタンを追加する。

- [ ] **Step 2: Write failing test**

Append to `BattleOverlay.test.tsx`:

```tsx
it('shows DEBUG -10HP button when import.meta.env.DEV is true', () => {
  // vitest は dev モードで動くので import.meta.env.DEV = true のはず
  const onDebugDamage = vi.fn()
  render(<BattleOverlay {/* 既存 props */} onDebugDamage={onDebugDamage} />)
  const btn = screen.getByRole('button', { name: /-10HP/i })
  fireEvent.click(btn)
  expect(onDebugDamage).toHaveBeenCalled()
})
```

- [ ] **Step 3: Run test, expect FAIL**

Run: `cd src/Client && npm test -- BattleOverlay.test`
Expected: FAIL

- [ ] **Step 4: Update BattleOverlay**

```tsx
type Props = {
  // 既存 props
  onDebugDamage?: () => void
}

// コンポーネント内、どこかに:
{import.meta.env.DEV && onDebugDamage && (
  <Button onClick={onDebugDamage} aria-label="DEBUG -10HP">DEBUG -10HP</Button>
)}
```

同じ要領で、MapScreen トップ付近（HP 表示の横）にも追加。`onDebugDamage` prop を props から受け取り渡す。

- [ ] **Step 5: Run test, expect PASS**

Run: `cd src/Client && npm test -- BattleOverlay.test`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Client/src/screens/BattleOverlay.tsx src/Client/src/screens/BattleOverlay.test.tsx src/Client/src/screens/MapScreen.tsx
git commit -m "feat(client): dev-only DEBUG -10HP button on battle and map views"
```

---

## Task 27: Client — MapScreen に ActStartRelicScreen mount + debug damage 結線

**Files:**
- Modify: `src/Client/src/screens/MapScreen.tsx`
- Modify: `src/Client/src/screens/MapScreen.test.tsx`

- [ ] **Step 1: Write failing test**

Append to `MapScreen.test.tsx`:

```tsx
it('renders ActStartRelicScreen when activeActStartRelicChoice is set', () => {
  const snap = makeSnap({ activeActStartRelicChoice: { relicIds: ['r1', 'r2', 'r3'] } })
  render(<MapScreen snapshot={snap} /* other props */ />)
  expect(screen.getByRole('dialog')).toBeDefined()
  expect(screen.getByText(/層開始のレリック/)).toBeDefined()
})
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `cd src/Client && npm test -- MapScreen.test`
Expected: FAIL

- [ ] **Step 3: Update MapScreen**

Integration points:
- Import `ActStartRelicScreen` + `chooseActStartRelic` API
- Inside JSX, after existing modals:

```tsx
{snap.run.activeActStartRelicChoice && (
  <ActStartRelicScreen
    choices={snap.run.activeActStartRelicChoice.relicIds}
    relicNames={/* from catalog state if available, else empty */}
    onChoose={async (relicId) => {
      const next = await chooseActStartRelic(accountId, relicId)
      onSnapshotChanged(next)
    }}
  />
)}
```

Also wire `onDebugDamage` handler:

```tsx
const handleDebugDamage = async () => {
  const resp = await applyDebugDamage(accountId, 10)
  if ('outcome' in resp) {
    onRunFinished(resp as RunResultDto)
  } else {
    onSnapshotChanged(resp as RunSnapshotDto)
  }
}
```

`onRunFinished` is a new prop lifted to App root (handled in Task 29).

- [ ] **Step 4: Run test, expect PASS**

Run: `cd src/Client && npm test -- MapScreen.test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Client/src/screens/MapScreen.tsx src/Client/src/screens/MapScreen.test.tsx
git commit -m "feat(client): MapScreen mounts ActStartRelicScreen and wires debug damage"
```

---

## Task 28: Client — MainMenuScreen で「続きから」を current run 有無で出し分け

**Files:**
- Modify: `src/Client/src/screens/MainMenuScreen.tsx`
- Modify: `src/Client/src/screens/MainMenuScreen.test.tsx`

- [ ] **Step 1: Write failing test**

Append to `MainMenuScreen.test.tsx`:

```tsx
it('hides "続きから" when hasCurrentRun is false', () => {
  render(<MainMenuScreen hasCurrentRun={false} /* other props */ />)
  expect(screen.queryByRole('button', { name: /続きから/ })).toBeNull()
  expect(screen.getByRole('button', { name: /新規ラン/ })).toBeDefined()
})

it('shows "続きから" when hasCurrentRun is true', () => {
  render(<MainMenuScreen hasCurrentRun={true} /* other props */ />)
  expect(screen.getByRole('button', { name: /続きから/ })).toBeDefined()
})
```

- [ ] **Step 2: Run test, expect FAIL**

Run: `cd src/Client && npm test -- MainMenuScreen.test`
Expected: FAIL (prop 未対応)

- [ ] **Step 3: Update MainMenuScreen**

Add `hasCurrentRun: boolean` prop, conditionally render:

```tsx
{hasCurrentRun && (
  <Button onClick={onResume}>続きから</Button>
)}
<Button onClick={onNewRun}>新規ラン</Button>
```

App root passes `hasCurrentRun={snapshot !== null}`.

- [ ] **Step 4: Run test, expect PASS**

Run: `cd src/Client && npm test -- MainMenuScreen.test`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Client/src/screens/MainMenuScreen.tsx src/Client/src/screens/MainMenuScreen.test.tsx
git commit -m "feat(client): MainMenuScreen hides 続きから when no current run"
```

---

## Task 29: Client — App root で RunResultScreen 遷移

**Files:**
- Modify: `src/Client/src/App.tsx`（または root screen switcher の実ファイル）

**方針:** `battle/win`, `reward/proceed`, `debug/damage`, `abandon` のいずれかから `RunResultDto` が返ったらそれを state に保持し、`RunResultScreen` を最前面に出す。「メニューへ戻る」で state クリア + snapshot = null にして MainMenuScreen を表示する。

- [ ] **Step 1: Read existing App.tsx structure**

Run: `cat src/Client/src/App.tsx`

- [ ] **Step 2: Add runResult state and RunResultScreen routing**

追加/修正:

```tsx
const [runResult, setRunResult] = useState<RunResultDto | null>(null)

const handleRunFinished = (r: RunResultDto) => {
  setSnapshot(null)
  setRunResult(r)
}

const handleReturnToMenu = () => {
  setRunResult(null)
}

// Render order:
if (runResult) return <RunResultScreen result={runResult} onReturnToMenu={handleReturnToMenu} />
if (!snapshot) return <MainMenuScreen hasCurrentRun={false} ... />
// ...既存の MapScreen / BattleOverlay / etc.
```

渡し先: `MapScreen` props に `onRunFinished={handleRunFinished}` を追加。`BattleOverlay` も win 応答が `RunResultDto` だった場合の handler を呼ぶように接続（battle/win の既存 handler を修正）。

- [ ] **Step 3: Manually verify with npm run dev**

Run: `cd src/Client && npm run dev`
Play through: run start → Start tile → relic 選択 → 進行 → DEBUG -10HP 連打 → HP 0 → RunResultScreen 表示 → メニューへ戻る → 新規ランのみ表示

- [ ] **Step 4: Commit**

```bash
git add src/Client/src/App.tsx
git commit -m "feat(client): route to RunResultScreen when run finishes"
```

---

## Task 30: E2E manual verification + cleanup

- [ ] **Step 1: Run full test suite**

Run: `dotnet test`
Run: `cd src/Client && npm test -- --run`
Expected: ALL PASS.

- [ ] **Step 2: Manual E2E checklist (spec §Testing 戦略 E2E)**

サーバー起動 → クライアント起動:
```bash
dotnet run --project src/Server &
cd src/Client && npm run dev
```

- [ ] act 1 Start 入場 → relic 3 択表示 → 選択 → Start 完了 → row 1 へ進める
- [ ] act 1 Boss 勝利 → 「次の層へ」ボタン → act 2 マップ表示, HP MaxHp まで回復, Start tile で act 2 relic 3 択
- [ ] act 2 Boss 勝利 → act 3
- [ ] act 3 Boss 勝利 → 報酬スキップで直接 RunResultScreen (Cleared)
- [ ] Debug -10HP を MapScreen から実行 → HP 0 で即 RunResultScreen (GameOver)
- [ ] Debug -10HP を BattleOverlay から実行 → 同上
- [ ] RunResultScreen → 「メニューへ戻る」→ 「続きから」非表示, 「新規ラン」のみ
- [ ] Abandon → メニュー戻り, 「続きから」非表示
- [ ] 履歴ファイル `data-local/history/{accountId}/` が生成されている
- [ ] v4 セーブ（既存 test.json）が残っていれば load でき、自然に act-start relic スキップとして扱われる

- [ ] **Step 3: Fix any issues found**

Failing checklist item → identify root cause, write regression test, fix, re-run.

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "chore: finalize Phase 7 manual E2E verification"
```

- [ ] **Step 5: Hand off to finishing-a-development-branch skill**

Run: `dotnet test && cd src/Client && npm test -- --run`
すべて緑ならば **superpowers:finishing-a-development-branch** を使って merge / PR / 保留 / 破棄 を選択する。

---

## Self-Review Checklist

### Spec coverage
- [x] §データ追加 — Task 5, 6
- [x] §RunState v4→v5 — Task 3, 4
- [x] §RewardState.IsBossReward — Task 1
- [x] §ActStartRelicChoice — Task 2, 3, 9
- [x] §ActTransition — Task 8
- [x] §ActMapSeed — Task 7, 17
- [x] §ActStartActions — Task 9
- [x] §BossRewardFlow — Task 10
- [x] §DebugActions — Task 11
- [x] §History module — Task 12, 14
- [x] §NodeEffectResolver Start — Task 13
- [x] §Server new endpoints (act-start, debug, history) — Task 16, 20, 21
- [x] §Server battle/win + reward/proceed 変更 — Task 18, 19
- [x] §Server abandon history 追加 — Task 21
- [x] §DTO 追加 — Task 15, 22
- [x] §Client ActStartRelicScreen — Task 23
- [x] §Client RunResultScreen — Task 24
- [x] §Client RewardPopup label — Task 25
- [x] §Client BattleOverlay / TopBar DEBUG ボタン — Task 26
- [x] §Client MapScreen — Task 27
- [x] §Client MainMenu — Task 28
- [x] §Client App root routing — Task 29
- [x] §Migration v4→v5 — Task 4
- [x] §Testing all categories — Task 1–30

### Consistency check
- RunState positional params (RunId, ActiveActStartRelicChoice) と NewSoloRun の呼び出し側が整合 (Task 3)
- `ActMapSeed.Derive(ulong, int) -> ulong` で統一 (Task 7, 17, 19)
- `IsBossReward` default false で既存テストに影響なし (Task 1, 15)
- `RehydrateMap(ulong, int)` シグネチャを caller 全員使う (Task 17)
- `RunSnapshotDtoMapper.ToResultDto(RunHistoryRecord)` を Controller 共通使用 (Task 15, 18, 20, 21)
- `IHistoryRepository` を DI して各 Controller で使う (Task 14, 18, 20, 21)

### Placeholder scan
No "TBD" / "implement later" / empty code blocks detected.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-22-phase07-boss-act-transition.md`.

Two execution options:

**1. Subagent-Driven (recommended)** — task ごとに fresh subagent を dispatch、spec compliance + code quality の二段レビューで高速イテレーション。

**2. Inline Execution** — このセッションで executing-plans を使って batch 実行、checkpoint でレビュー。

Which approach?
