# Phase 1 — ラン状態とセーブシステム Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ソロラン 1 回分の状態を単一の `record` で表現し、JSON シリアライズ／デシリアライズ／ファイル保存・読込・削除ができる状態にする。

**Architecture:** `src/Core/Run/` にラン状態（`RunState` record、`RunProgress` 列挙、`RunClock`、`RunStateSerializer`）を置き、`src/Server/Services/SaveRepository.cs` が Core のシリアライザを呼び出してアカウント ID 単位で単一スロットのファイルを読み書きする。マルチプレイランは Server メモリでホストが駆動するため、このフェーズではセーブされない（ソロのみ永続化）。スキーマには `schemaVersion = 1` を埋め込み、将来の互換性破壊時にバージョン判定で弾く。

**Tech Stack:** C# .NET 10、`System.Text.Json`、xUnit（`Assert.*` のみ、FluentAssertions 禁止）。

---

## 決定済みの設計判断

| 項目 | 決定 |
|------|------|
| セーブスロット | アカウント ID ごとに 1 スロット（ソロランのみ）。オートセーブは上書き、クリア／ゲームオーバーで削除 |
| マルチプレイの保存 | 行わない。ホスト Server のメモリ上でのみ保持（Phase 9 で実装） |
| アカウント ID | 不透明な文字列キー。Phase 2 のログイン実装で実体 ID と紐付く。Phase 1 テストでは固定値 |
| スキーマバージョン | `"schemaVersion": 1` を JSON に埋め込む。`Deserialize` で != 1 なら例外 |
| RNG シード型 | `ulong`（`System.Random` / `Random.Shared.NextInt64` と相性良し） |
| 時刻型 | `DateTimeOffset`（`DateTime` より曖昧さが少ない。UTC を保存） |
| コレクション型 | `string[]`（`System.Text.Json` が確実に復元できる。record 等価比較は依存しない） |
| 開始ステータス | `MaxHp = 80`、`Gold = 99`（定数。キャラクター選択は後続フェーズ） |

---

## File Structure

```
src/Core/Data/
├── DataCatalog.cs              # （修正）TryGetCard/Relic/Potion/Enemy 追加

src/Core/Run/                   # 新規ディレクトリ
├── RunProgress.cs              # enum（InProgress/Cleared/GameOver/Abandoned）
├── RunState.cs                 # 主データ record + NewSoloRun 静的ファクトリ
├── RunStateSerializer.cs       # Serialize/Deserialize + 例外型（schemaVersion 検証）
└── RunClock.cs                 # プレイ秒数トラッカー（Func<DateTimeOffset> 注入可能）

src/Server/Services/            # 新規ディレクトリ
└── SaveRepository.cs           # アカウント ID 単位のファイル I/O（単一スロット上書き）

tests/Core.Tests/Data/
└── DataCatalogLookupTests.cs   # TryGet ヘルパー

tests/Core.Tests/Run/           # 新規ディレクトリ
├── RunStateFactoryTests.cs     # NewSoloRun の初期値検証
├── RunStateSerializerTests.cs  # 往復 + スキーマバージョン + エラー
└── RunClockTests.cs            # 経過秒数の加算・一時停止

tests/Server.Tests/
└── SaveRepositoryTests.cs      # 保存・読込・削除・パストラバーサル防御
```

**依存方向:** `Run/` → `Data/`, `Player/`, `Cards/`（既存）。`Server/Services/` → `Core/Run/`。循環なし。

---

## Task 1 — DataCatalog に TryGet ヘルパーを追加

**Files:**
- Modify: `src/Core/Data/DataCatalog.cs`
- Create: `tests/Core.Tests/Data/DataCatalogLookupTests.cs`

**設計メモ:** Phase 0 最終レビューで「`catalog.Cards["strike"]` の直接アクセスは `KeyNotFoundException` にコンテキストが乗らない」と指摘されたため、各カテゴリ用の `bool TryGetX(id, out def)` を追加する。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Data/DataCatalogLookupTests.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class DataCatalogLookupTests
{
    private static DataCatalog BuildCatalog() =>
        DataCatalog.LoadFromStrings(
            cards: new[] { JsonFixtures.StrikeJson, JsonFixtures.DefendJson },
            relics: new[] { JsonFixtures.BurningBloodJson },
            potions: new[] { JsonFixtures.BlockPotionJson },
            enemies: new[] { JsonFixtures.JawWormJson });

    [Fact]
    public void TryGetCard_Hit_ReturnsTrueAndDefinition()
    {
        var catalog = BuildCatalog();
        Assert.True(catalog.TryGetCard("strike", out var def));
        Assert.NotNull(def);
        Assert.Equal("ストライク", def!.Name);
    }

    [Fact]
    public void TryGetCard_Miss_ReturnsFalseAndNull()
    {
        var catalog = BuildCatalog();
        Assert.False(catalog.TryGetCard("nonexistent", out var def));
        Assert.Null(def);
    }

    [Fact]
    public void TryGetRelic_Potion_Enemy_AllWork()
    {
        var catalog = BuildCatalog();
        Assert.True(catalog.TryGetRelic("burning_blood", out _));
        Assert.True(catalog.TryGetPotion("block_potion", out _));
        Assert.True(catalog.TryGetEnemy("jaw_worm", out _));
        Assert.False(catalog.TryGetRelic("missing", out _));
        Assert.False(catalog.TryGetPotion("missing", out _));
        Assert.False(catalog.TryGetEnemy("missing", out _));
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~DataCatalogLookupTests"`
Expected: コンパイルエラー（`TryGetCard` 等が未定義）。

- [ ] **Step 3: `DataCatalog.cs` に 4 つの `TryGet` メソッドを追加**

`src/Core/Data/DataCatalog.cs` の `record DataCatalog(...)` ブロック内（`LoadFromStrings` の後）に追加:

```csharp
    public bool TryGetCard(string id, out CardDefinition? def) => Cards.TryGetValue(id, out def);
    public bool TryGetRelic(string id, out RelicDefinition? def) => Relics.TryGetValue(id, out def);
    public bool TryGetPotion(string id, out PotionDefinition? def) => Potions.TryGetValue(id, out def);
    public bool TryGetEnemy(string id, out EnemyDefinition? def) => Enemies.TryGetValue(id, out def);
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~DataCatalogLookupTests"`
Expected: 3 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Data/DataCatalog.cs tests/Core.Tests/Data/DataCatalogLookupTests.cs
git commit -m "feat(core): add TryGet lookup helpers to DataCatalog"
```

---

## Task 2 — RunProgress 列挙

**Files:**
- Create: `src/Core/Run/RunProgress.cs`
- Test: Task 3 で `RunState` を作った後に一緒に使うので、このタスク単独のテストは書かない（列挙値のみ）

- [ ] **Step 1: 列挙を作成**

`src/Core/Run/RunProgress.cs`:

```csharp
namespace RoguelikeCardGame.Core.Run;

/// <summary>ラン 1 回分の進行状態。</summary>
public enum RunProgress
{
    /// <summary>進行中（通常）。</summary>
    InProgress = 0,
    /// <summary>ボス撃破でクリア。</summary>
    Cleared = 1,
    /// <summary>HP 0 で死亡。</summary>
    GameOver = 2,
    /// <summary>プレイヤー操作による放棄（タイトルへ戻るなど）。</summary>
    Abandoned = 3,
}
```

- [ ] **Step 2: ビルド確認**

Run: `dotnet build --nologo`
Expected: 成功。

- [ ] **Step 3: コミット**

```bash
git add src/Core/Run/RunProgress.cs
git commit -m "feat(core): add RunProgress enum"
```

---

## Task 3 — RunState record（データ定義のみ）

**Files:**
- Create: `src/Core/Run/RunState.cs`
- Test: Task 4 で `NewSoloRun` ファクトリと一緒に検証

**設計メモ:** record のプライマリコンストラクタパラメータ順は JSON シリアライズで名前ベースになるため重要ではないが、読みやすさ優先で「スキーマ→位置→ステータス→インベントリ→メタ」の順に並べる。

- [ ] **Step 1: record を作成**

`src/Core/Run/RunState.cs`:

```csharp
using System;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ソロ／マルチ共通のラン 1 回分の状態。ソロのみ SaveRepository で永続化される。</summary>
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentTileIndex,
    int CurrentHp,
    int MaxHp,
    int Gold,
    string[] Deck,
    string[] Relics,
    string[] Potions,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress);
```

**フィールドの意味:**
- `SchemaVersion`: 常に 1（Phase 1 時点）。Deserialize で検証。
- `CurrentAct`: 1..3（1 = 第一層、3 = 第三層）。
- `CurrentTileIndex`: 0 = 開始マス、1..15 = 中間マス、16 = ボスマス。
- `CurrentHp` / `MaxHp`: HP。
- `Gold`: 所持金。
- `Deck` / `Relics` / `Potions`: 各 ID の配列。カードはデッキ重複 ID を許容する（例：`strike` 5 枚）。レリック／ポーションはゲーム上重複しない想定だが Phase 1 では検証しない。
- `PlaySeconds`: 累計プレイ秒数。再開時に `RunClock` がここから加算する。
- `RngSeed`: ラン開始時に決定した乱数シード。マップ生成・報酬抽選などで派生 RNG を作る元。
- `SavedAtUtc`: セーブ時刻（UTC）。
- `Progress`: ラン進行状態。

- [ ] **Step 2: ビルド確認**

Run: `dotnet build --nologo`
Expected: 成功。

- [ ] **Step 3: コミット**

```bash
git add src/Core/Run/RunState.cs
git commit -m "feat(core): add RunState record"
```

---

## Task 4 — RunState.NewSoloRun 静的ファクトリ

**Files:**
- Modify: `src/Core/Run/RunState.cs`（`NewSoloRun` と定数を追加）
- Create: `tests/Core.Tests/Run/RunStateFactoryTests.cs`

**設計メモ:** 新規ソロランを作る共通路。`StarterDeck.DefaultCardIds` を `DataCatalog` に対して検証し、不明 ID があれば例外。開始 HP=80、Gold=99 は Slay the Spire の Ironclad 相当。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Run/RunStateFactoryTests.cs`:

```csharp
using System;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateFactoryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewSoloRun_InitialValues()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(catalog, rngSeed: 42UL, nowUtc: FixedNow);

        Assert.Equal(1, state.SchemaVersion);
        Assert.Equal(1, state.CurrentAct);
        Assert.Equal(0, state.CurrentTileIndex);
        Assert.Equal(80, state.CurrentHp);
        Assert.Equal(80, state.MaxHp);
        Assert.Equal(99, state.Gold);
        Assert.Equal(10, state.Deck.Length);
        Assert.Equal(5, Array.FindAll(state.Deck, id => id == "strike").Length);
        Assert.Equal(5, Array.FindAll(state.Deck, id => id == "defend").Length);
        Assert.Empty(state.Relics);
        Assert.Empty(state.Potions);
        Assert.Equal(0L, state.PlaySeconds);
        Assert.Equal(42UL, state.RngSeed);
        Assert.Equal(FixedNow, state.SavedAtUtc);
        Assert.Equal(RunProgress.InProgress, state.Progress);
    }

    [Fact]
    public void NewSoloRun_ThrowsWhenStarterCardMissingFromCatalog()
    {
        // StarterDeck は "strike" と "defend" を要求するので、空カタログだと失敗する
        var emptyCatalog = DataCatalog.LoadFromStrings(
            cards: System.Array.Empty<string>(),
            relics: System.Array.Empty<string>(),
            potions: System.Array.Empty<string>(),
            enemies: System.Array.Empty<string>());

        var ex = Assert.Throws<InvalidOperationException>(
            () => RunState.NewSoloRun(emptyCatalog, rngSeed: 0UL, nowUtc: FixedNow));
        Assert.Contains("strike", ex.Message);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~RunStateFactoryTests"`
Expected: コンパイルエラー（`NewSoloRun` が未定義）。

- [ ] **Step 3: `RunState.cs` に定数とファクトリメソッドを追加**

`src/Core/Run/RunState.cs` に `using` と `RunState` record 本体の中括弧ブロックを追加：

```csharp
using System;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Player;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ソロ／マルチ共通のラン 1 回分の状態。ソロのみ SaveRepository で永続化される。</summary>
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentTileIndex,
    int CurrentHp,
    int MaxHp,
    int Gold,
    string[] Deck,
    string[] Relics,
    string[] Potions,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress)
{
    /// <summary>Phase 1 の JSON スキーマバージョン。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>初期最大 HP。</summary>
    public const int StartingMaxHp = 80;

    /// <summary>初期所持金。</summary>
    public const int StartingGold = 99;

    /// <summary>新規ソロラン状態を作る。StarterDeck が DataCatalog に存在することを検証。</summary>
    public static RunState NewSoloRun(DataCatalog catalog, ulong rngSeed, DateTimeOffset nowUtc)
    {
        foreach (var id in StarterDeck.DefaultCardIds)
        {
            if (!catalog.TryGetCard(id, out _))
                throw new InvalidOperationException(
                    $"StarterDeck が参照するカード ID が DataCatalog に存在しません: {id}");
        }

        var deck = new string[StarterDeck.DefaultCardIds.Count];
        for (var i = 0; i < deck.Length; i++) deck[i] = StarterDeck.DefaultCardIds[i];

        return new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentTileIndex: 0,
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
}
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~RunStateFactoryTests"`
Expected: 2 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Run/RunState.cs tests/Core.Tests/Run/RunStateFactoryTests.cs
git commit -m "feat(core): add RunState.NewSoloRun factory with StarterDeck validation"
```

---

## Task 5 — RunStateSerializer（Serialize + 往復テスト）

**Files:**
- Create: `src/Core/Run/RunStateSerializer.cs`
- Create: `tests/Core.Tests/Run/RunStateSerializerTests.cs`

**設計メモ:** `System.Text.Json` の `JsonSerializer.Serialize/Deserialize<T>` を使う。レコードの primary ctor パラメータは PascalCase なので `JsonNamingPolicy.CamelCase` を適用。`RunProgress` は `JsonStringEnumConverter` で文字列化（数値ドリフトを避ける）。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Run/RunStateSerializerTests.cs`:

```csharp
using System;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateSerializerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    private static RunState FreshRun()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        return RunState.NewSoloRun(catalog, rngSeed: 42UL, nowUtc: FixedNow);
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTripsAllFields()
    {
        var original = FreshRun();

        var json = RunStateSerializer.Serialize(original);
        var restored = RunStateSerializer.Deserialize(json);

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.CurrentAct, restored.CurrentAct);
        Assert.Equal(original.CurrentTileIndex, restored.CurrentTileIndex);
        Assert.Equal(original.CurrentHp, restored.CurrentHp);
        Assert.Equal(original.MaxHp, restored.MaxHp);
        Assert.Equal(original.Gold, restored.Gold);
        Assert.Equal(original.Deck, restored.Deck);
        Assert.Equal(original.Relics, restored.Relics);
        Assert.Equal(original.Potions, restored.Potions);
        Assert.Equal(original.PlaySeconds, restored.PlaySeconds);
        Assert.Equal(original.RngSeed, restored.RngSeed);
        Assert.Equal(original.SavedAtUtc, restored.SavedAtUtc);
        Assert.Equal(original.Progress, restored.Progress);
    }

    [Fact]
    public void Serialize_UsesCamelCaseAndStringEnum()
    {
        var json = RunStateSerializer.Serialize(FreshRun());
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"progress\":\"InProgress\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json); // PascalCase は出ない
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~RunStateSerializerTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: シリアライザを実装**

`src/Core/Run/RunStateSerializer.cs`:

```csharp
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoguelikeCardGame.Core.Run;

/// <summary>RunState JSON のパース失敗を表す例外。</summary>
public sealed class RunStateSerializerException : Exception
{
    public RunStateSerializerException(string message) : base(message) { }
    public RunStateSerializerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>RunState ⇔ JSON 文字列の変換。ファイル I/O は Server 側の SaveRepository が担当。</summary>
public static class RunStateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(RunState state)
    {
        return JsonSerializer.Serialize(state, Options);
    }

    public static RunState Deserialize(string json)
    {
        RunState? state;
        try
        {
            state = JsonSerializer.Deserialize<RunState>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex);
        }

        if (state is null)
            throw new RunStateSerializerException("RunState JSON が null として解釈されました。");

        if (state.SchemaVersion != RunState.CurrentSchemaVersion)
            throw new RunStateSerializerException(
                $"未対応の schemaVersion: {state.SchemaVersion} (対応: {RunState.CurrentSchemaVersion})");

        return state;
    }
}
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~RunStateSerializerTests"`
Expected: 2 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Run/RunStateSerializer.cs tests/Core.Tests/Run/RunStateSerializerTests.cs
git commit -m "feat(core): add RunStateSerializer with schemaVersion + round-trip"
```

---

## Task 6 — RunStateSerializer エラーケース

**Files:**
- Modify: `tests/Core.Tests/Run/RunStateSerializerTests.cs`（テスト追加のみ、本体は変更不要）

**設計メモ:** `Deserialize` の 3 系統の失敗（不正 JSON／null／未対応 schemaVersion）をテストで固定する。本体の `Deserialize` はすでにこれらを投げるので実装変更は無い。

- [ ] **Step 1: 失敗テストを追記**

`tests/Core.Tests/Run/RunStateSerializerTests.cs` の `RunStateSerializerTests` クラスに以下を追記：

```csharp
    [Fact]
    public void Deserialize_BrokenJson_Throws()
    {
        var ex = Assert.Throws<RunStateSerializerException>(
            () => RunStateSerializer.Deserialize("{ not valid"));
        Assert.Contains("パース", ex.Message);
    }

    [Fact]
    public void Deserialize_NullLiteral_Throws()
    {
        var ex = Assert.Throws<RunStateSerializerException>(
            () => RunStateSerializer.Deserialize("null"));
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void Deserialize_WrongSchemaVersion_Throws()
    {
        // 手書きで schemaVersion=99 の RunState を作る
        var json = """
        {
          "schemaVersion": 99,
          "currentAct": 1,
          "currentTileIndex": 0,
          "currentHp": 80,
          "maxHp": 80,
          "gold": 99,
          "deck": [],
          "relics": [],
          "potions": [],
          "playSeconds": 0,
          "rngSeed": 0,
          "savedAtUtc": "2026-04-20T12:00:00+00:00",
          "progress": "InProgress"
        }
        """;

        var ex = Assert.Throws<RunStateSerializerException>(
            () => RunStateSerializer.Deserialize(json));
        Assert.Contains("schemaVersion", ex.Message);
        Assert.Contains("99", ex.Message);
    }
```

- [ ] **Step 2: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~RunStateSerializerTests"`
Expected: 5 件成功（前 2 件 + 新規 3 件）。

- [ ] **Step 3: コミット**

```bash
git add tests/Core.Tests/Run/RunStateSerializerTests.cs
git commit -m "test(core): cover RunStateSerializer error paths"
```

---

## Task 7 — RunClock（プレイ秒数トラッカー）

**Files:**
- Create: `src/Core/Run/RunClock.cs`
- Create: `tests/Core.Tests/Run/RunClockTests.cs`

**設計メモ:** 「再開時に前回の続きから加算される」ことを保証するため、`RunClock` は `baseSeconds`（保存済みの累計秒数）と `resumedAt`（現在セッション開始時刻）を持ち、`TotalSeconds` で合算を返す。`Func<DateTimeOffset>` を注入することでテスト時に時刻を差し替え可能。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Run/RunClockTests.cs`:

```csharp
using System;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunClockTests
{
    // 可変なフェイク時刻
    private DateTimeOffset _now = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset Now() => _now;

    [Fact]
    public void NotResumed_TotalSecondsEqualsBase()
    {
        var clock = new RunClock(Now, baseSeconds: 100);
        Assert.Equal(100L, clock.TotalSeconds);
    }

    [Fact]
    public void Resume_ThenAdvance_AddsElapsedSeconds()
    {
        var clock = new RunClock(Now, baseSeconds: 100);
        clock.Resume();
        _now = _now.AddSeconds(45);
        Assert.Equal(145L, clock.TotalSeconds);
    }

    [Fact]
    public void Pause_FreezesTotalAndSurvivesClockAdvance()
    {
        var clock = new RunClock(Now, baseSeconds: 100);
        clock.Resume();
        _now = _now.AddSeconds(45);
        clock.Pause();
        _now = _now.AddSeconds(1000); // Pause 後は進まない
        Assert.Equal(145L, clock.TotalSeconds);
    }

    [Fact]
    public void ResumeAfterPause_ContinuesFromPaused()
    {
        var clock = new RunClock(Now, baseSeconds: 100);
        clock.Resume();
        _now = _now.AddSeconds(45);
        clock.Pause();
        _now = _now.AddSeconds(1000);
        clock.Resume();
        _now = _now.AddSeconds(10);
        Assert.Equal(155L, clock.TotalSeconds);
    }

    [Fact]
    public void DoubleResume_IsIdempotent()
    {
        var clock = new RunClock(Now, baseSeconds: 0);
        clock.Resume();
        _now = _now.AddSeconds(30);
        clock.Resume(); // 2 回目は無視されるべき
        _now = _now.AddSeconds(20);
        Assert.Equal(50L, clock.TotalSeconds);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~RunClockTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: 実装**

`src/Core/Run/RunClock.cs`:

```csharp
using System;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ラン中の経過秒数を追跡する。Pause/Resume で進行を切り替え、TotalSeconds で合算値を返す。</summary>
public sealed class RunClock
{
    private readonly Func<DateTimeOffset> _now;
    private long _baseSeconds;
    private DateTimeOffset? _resumedAt;

    public RunClock(Func<DateTimeOffset> nowProvider, long baseSeconds = 0)
    {
        _now = nowProvider ?? throw new ArgumentNullException(nameof(nowProvider));
        _baseSeconds = baseSeconds;
    }

    /// <summary>計測を再開する。すでに再開中なら何もしない（冪等）。</summary>
    public void Resume()
    {
        if (_resumedAt is null) _resumedAt = _now();
    }

    /// <summary>計測を一時停止し、現在の累計を内部ベースに畳み込む。</summary>
    public void Pause()
    {
        if (_resumedAt is not null)
        {
            _baseSeconds = TotalSeconds;
            _resumedAt = null;
        }
    }

    /// <summary>現在の累計秒数（ベース + 再開中なら現在セッション経過分）。</summary>
    public long TotalSeconds =>
        _resumedAt is null
            ? _baseSeconds
            : _baseSeconds + (long)(_now() - _resumedAt.Value).TotalSeconds;
}
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~RunClockTests"`
Expected: 5 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Run/RunClock.cs tests/Core.Tests/Run/RunClockTests.cs
git commit -m "feat(core): add RunClock for pausable play-seconds tracking"
```

---

## Task 8 — SaveRepository（Server ファイル I/O）：保存と読込

**Files:**
- Create: `src/Server/Services/SaveRepository.cs`
- Create: `tests/Server.Tests/SaveRepositoryTests.cs`

**設計メモ:** `Core` は I/O を持たない原則を守るため、ファイル読み書きは Server 側で行う。`SaveRepository(rootDir)` はコンストラクタでディレクトリを作成、`Save(accountId, state)` で `{rootDir}/{accountId}.json` を UTF-8 で書き出し、`TryLoad` で読み出す。`accountId` は `Path.GetInvalidFileNameChars` で検証してパストラバーサルを防ぐ。

- [ ] **Step 1: 失敗テストを書く**

`tests/Server.Tests/SaveRepositoryTests.cs`:

```csharp
using System;
using System.IO;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests;

public class SaveRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DataCatalog _catalog = EmbeddedDataLoader.LoadCatalog();
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    public SaveRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rcg-save-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private RunState FreshRun(ulong seed = 42UL) =>
        RunState.NewSoloRun(_catalog, rngSeed: seed, nowUtc: FixedNow);

    [Fact]
    public void Save_CreatesFileForAccountId()
    {
        var repo = new SaveRepository(_tempRoot);
        repo.Save("player-001", FreshRun());

        var expectedPath = Path.Combine(_tempRoot, "player-001.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public void TryLoad_AfterSave_ReturnsEquivalentState()
    {
        var repo = new SaveRepository(_tempRoot);
        var original = FreshRun(seed: 777UL);
        repo.Save("player-002", original);

        Assert.True(repo.TryLoad("player-002", out var restored));
        Assert.NotNull(restored);
        Assert.Equal(original.RngSeed, restored!.RngSeed);
        Assert.Equal(original.MaxHp, restored.MaxHp);
        Assert.Equal(original.Deck, restored.Deck);
    }

    [Fact]
    public void TryLoad_MissingAccount_ReturnsFalseAndNull()
    {
        var repo = new SaveRepository(_tempRoot);
        Assert.False(repo.TryLoad("never-saved", out var state));
        Assert.Null(state);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var repo = new SaveRepository(_tempRoot);
        repo.Save("p", FreshRun(seed: 1UL));
        repo.Save("p", FreshRun(seed: 2UL));

        Assert.True(repo.TryLoad("p", out var state));
        Assert.Equal(2UL, state!.RngSeed);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~SaveRepositoryTests"`
Expected: コンパイルエラー（`SaveRepository` が未定義）。

- [ ] **Step 3: 実装**

`src/Server/Services/SaveRepository.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Server.Services;

/// <summary>アカウント ID ごとに単一スロットのラン状態をファイルに保存／読込する。</summary>
public sealed class SaveRepository
{
    private readonly string _rootDir;

    public SaveRepository(string rootDir)
    {
        if (string.IsNullOrWhiteSpace(rootDir))
            throw new ArgumentException("rootDir は空にできません。", nameof(rootDir));
        _rootDir = rootDir;
        Directory.CreateDirectory(_rootDir);
    }

    public void Save(string accountId, RunState state)
    {
        ValidateAccountId(accountId);
        var json = RunStateSerializer.Serialize(state);
        File.WriteAllText(PathFor(accountId), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public bool TryLoad(string accountId, out RunState? state)
    {
        ValidateAccountId(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path))
        {
            state = null;
            return false;
        }
        var json = File.ReadAllText(path, Encoding.UTF8);
        state = RunStateSerializer.Deserialize(json);
        return true;
    }

    private string PathFor(string accountId) =>
        Path.Combine(_rootDir, accountId + ".json");

    private static void ValidateAccountId(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("accountId は空にできません。", nameof(accountId));
        if (accountId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(
                $"accountId にファイル名として使えない文字が含まれています: {accountId}", nameof(accountId));
    }
}
```

**`Server.Tests.csproj` と namespace について:** `tests/Server.Tests/Server.Tests.csproj` が `RoguelikeCardGame.Core.dll` を参照している前提で進める。現状 `tests/Server.Tests/UnitTest1.cs` の namespace は `Server.Tests` だが、これは Phase 0 の残置テンプレート。**本タスクで新規作成するテストファイルの namespace は `RoguelikeCardGame.Server.Tests` とする**。既存 `UnitTest1.cs` の namespace 不整合は Task 10 の regression 前に合わせて修正する。

- [ ] **Step 4: `tests/Server.Tests/Server.Tests.csproj` の ProjectReference を確認**

Run: `cat tests/Server.Tests/Server.Tests.csproj`
Expected: `<ProjectReference Include="..\..\src\Core\Core.csproj" />` が存在する。存在しない場合は追加：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Core\Core.csproj" />
  <ProjectReference Include="..\..\src\Server\Server.csproj" />
</ItemGroup>
```

（`Server.csproj` への参照も必要。現状のテンプレートに無ければ追加する。）

- [ ] **Step 5: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~SaveRepositoryTests"`
Expected: 4 件成功。

- [ ] **Step 6: コミット**

```bash
git add src/Server/Services/SaveRepository.cs tests/Server.Tests/SaveRepositoryTests.cs tests/Server.Tests/Server.Tests.csproj
git commit -m "feat(server): add SaveRepository for per-account solo run persistence"
```

---

## Task 9 — SaveRepository：削除と accountId 検証

**Files:**
- Modify: `src/Server/Services/SaveRepository.cs`（`Delete` 追加）
- Modify: `tests/Server.Tests/SaveRepositoryTests.cs`（テスト追記）

**設計メモ:** クリア／ゲームオーバー時に呼ぶ `Delete` を追加。存在しないファイルの削除は no-op（べき等）。accountId 検証は既存の Save/TryLoad と同じく共有ヘルパーを使う。パストラバーサル防御のテストもここで固定する。

- [ ] **Step 1: 失敗テストを追記**

`tests/Server.Tests/SaveRepositoryTests.cs` の `SaveRepositoryTests` クラスに追記：

```csharp
    [Fact]
    public void Delete_ExistingAccount_RemovesFile()
    {
        var repo = new SaveRepository(_tempRoot);
        repo.Save("to-delete", FreshRun());
        var path = Path.Combine(_tempRoot, "to-delete.json");
        Assert.True(File.Exists(path));

        repo.Delete("to-delete");

        Assert.False(File.Exists(path));
        Assert.False(repo.TryLoad("to-delete", out _));
    }

    [Fact]
    public void Delete_MissingAccount_IsNoOp()
    {
        var repo = new SaveRepository(_tempRoot);
        // 例外を投げず何もしないことを確認
        repo.Delete("never-existed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escape")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    public void InvalidAccountId_ThrowsArgumentException(string bad)
    {
        var repo = new SaveRepository(_tempRoot);
        Assert.Throws<ArgumentException>(() => repo.Save(bad, FreshRun()));
        Assert.Throws<ArgumentException>(() => repo.TryLoad(bad, out _));
        Assert.Throws<ArgumentException>(() => repo.Delete(bad));
    }
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~SaveRepositoryTests"`
Expected: `Delete` と `InvalidAccountId` がコンパイルエラーまたは失敗。

- [ ] **Step 3: `Delete` メソッドを追加**

`src/Server/Services/SaveRepository.cs` の `TryLoad` の後に追加：

```csharp
    public void Delete(string accountId)
    {
        ValidateAccountId(accountId);
        var path = PathFor(accountId);
        if (File.Exists(path)) File.Delete(path);
    }
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~SaveRepositoryTests"`
Expected: 全 9 件成功（Task 8 の 4 件 + Delete 2 件 + InvalidAccountId 5 ケース × 3 呼び出し、実際は Theory で 5 件扱い）。

**補足:** `[Theory]` は InlineData ごとに 1 ケースとカウントされる。5 ケース。合計 4 + 2 + 5 = 11 件になる場合もある（xUnit のバージョン依存）。件数よりも全成功を確認する。

- [ ] **Step 5: コミット**

```bash
git add src/Server/Services/SaveRepository.cs tests/Server.Tests/SaveRepositoryTests.cs
git commit -m "feat(server): add SaveRepository.Delete + accountId validation"
```

---

## Task 10 — Phase 1 リグレッションと完了タグ

**Files:**
- Modify: `tests/Server.Tests/UnitTest1.cs`（Phase 0 の残置テンプレートの namespace を修正）

**設計メモ:** Phase 0 の最終レビューで「`tests/Server.Tests/UnitTest1.cs` の namespace が `Server.Tests` で `RoguelikeCardGame.Server.Tests` になっていない」との指摘があった（M6）。Phase 1 で `SaveRepositoryTests` を追加するタイミングで揃える。

- [ ] **Step 1: 既存 `UnitTest1.cs` の namespace を修正**

`tests/Server.Tests/UnitTest1.cs` を開き、最上部の `namespace Server.Tests;` を `namespace RoguelikeCardGame.Server.Tests;` に変更する。

`Server.Tests.csproj` に `<RootNamespace>RoguelikeCardGame.Server.Tests</RootNamespace>` が無ければ追記：

```xml
<PropertyGroup>
  ...
  <RootNamespace>RoguelikeCardGame.Server.Tests</RootNamespace>
</PropertyGroup>
```

- [ ] **Step 2: 全テスト通過を確認**

Run: `dotnet test --nologo`
Expected: 全成功。件数目安は Core.Tests 44（Phase 0 完了時点）+ 新規 Core テスト（Lookup 3 + Factory 2 + Serializer 5 + Clock 5 = 15）= 59 件、Server.Tests 元 1 + SaveRepository 11 = 12 件前後。

- [ ] **Step 3: 警告ゼロでビルド**

Run: `dotnet build -warnaserror --nologo`
Expected: `0 個の警告、0 エラー`。

- [ ] **Step 4: Phase 1 完了タグを打つ**

```bash
git add tests/Server.Tests/UnitTest1.cs tests/Server.Tests/Server.Tests.csproj
git commit -m "chore(server.tests): align root namespace with project convention"
git tag phase1-complete
git log --oneline phase0-complete..HEAD
```

Expected: Phase 1 の 11 コミット（Task 1〜10 分 + namespace 修正）が並ぶ。

---

## Self-Review（仕様カバレッジ）

| ロードマップ Phase 1 項目 | 対応タスク |
|--------------------------|----------|
| `src/Core/Run/RunState.cs` | Task 3, 4 |
| `src/Core/Run/RunProgress.cs` | Task 2 |
| `src/Core/Run/RunStateSerializer.cs` | Task 5, 6 |
| `src/Server/Services/SaveRepository.cs` | Task 8, 9 |
| `tests/Core.Tests/Run/RunStateSerializerTests.cs` | Task 5, 6 |
| `tests/Server.Tests/SaveRepositoryTests.cs` | Task 8, 9 |
| RunState 全フィールドの往復テスト | Task 5 |
| プレイ秒数が再開時に加算される検証 | Task 7 (RunClockTests) |
| schemaVersion = 1 の埋め込みと検証 | Task 5, 6 |
| アカウント ID ごとの単一スロット | Task 8 |
| クリア／ゲームオーバー時の削除 | Task 9 |
| パストラバーサル防御 | Task 9 |
| Phase 0 最終レビューで指摘された TryGet ヘルパー | Task 1 |
| Phase 0 最終レビューの Server.Tests namespace 不整合修正 | Task 10 |

**Phase 1 に含めない（後続フェーズの責任範囲）:**
- `Cost == null`（呪いカード）の挙動契約 → Phase 5（バトル）
- `UnknownEffect` の解決ポリシー → Phase 5
- マルチプレイのラン同期 → Phase 9
- ランを超えた永続データ（図鑑、プレイ履歴） → Phase 8
- マップ座標を構造化する `MapPosition` 型 → Phase 3（マップ生成）

## Done 判定

1. `dotnet build -warnaserror` クリーン
2. `dotnet test` 全件成功（Core 59 件前後、Server 12 件前後）
3. `phase1-complete` タグが打たれている
4. `RunState` を `Serialize` → ファイル保存 → 読込 → `Deserialize` の一連のサイクルが成立
5. `RunClock` が Pause/Resume サイクルを通じて正しい累計秒数を返す
