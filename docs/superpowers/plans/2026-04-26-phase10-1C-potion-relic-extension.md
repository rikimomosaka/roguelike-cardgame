# Phase 10.1.C — Potion / Relic 拡張 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `RelicDefinition` に `Implemented` フラグを追加し、`RelicTrigger` を 9 値に拡張し、`PotionDefinition` から冗長な bool フラグを削除して per-effect の `BattleOnly` から派生プロパティ化する。レリック JSON 36 件 + ポーション JSON 7 件を新形式に移行（旧 action 名 3 件正規化込み）。バトル本体の発火ロジックは Phase 10.2 担当でこのフェーズではコード追加なし。

**Architecture:** ビルド赤の中間状態を最小限にする「漸進的型変更」方針。Tasks 1〜5 は既存コードと共存しながら段階的に追加（Implemented 既定値あり / RelicTrigger は値追加のみ）。Task 6 で `PotionDefinition` の破壊的シェイプ変更を全消費者と同時にコミット。Tasks 7〜8 は JSON データ移行。Tasks 9〜11 でテスト整備・spec 補記・タグ。

**Tech Stack:** C# .NET 10 / xUnit / `System.Text.Json` / TypeScript（Client 側 1 ファイル）

**前提:**
- Phase 10.1.B が master にマージ済みであること（`phase10-1B-complete` タグ）。本プランの worktree は master の最新 HEAD から切る。
- 開始時点で `dotnet build` 0 警告 0 エラー、`dotnet test` 全件緑（Core 507、Server 168）。

**完了判定:**
- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（10.1.B 時点 + 本プラン追加分）
- 旧 `PotionDefinition` フィールド `UsableInBattle` / `UsableOutOfBattle` が production / tests に grep で 0 件
- 旧ポーション action `applyPoison` / `gainStrength` / `drawCards` が `src/Core/Data/Potions/*.json` に grep で 0 件
- 旧 JSON フィールド `usableInBattle` / `usableOutOfBattle` が `src/Core/Data/Potions/*.json` に grep で 0 件
- 全 36 relic JSON が `implemented` フィールドを持つ
- 全 7 potion JSON が新形式
- 親 Phase 10 spec の第 2-7 章補記済み
- ブランチに `phase10-1C-complete` タグを切り origin に push

---

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `src/Core/Relics/RelicTrigger.cs` | enum 9 値（5 既存 + 4 新規）に拡張、整数値明示 | **修正** |
| `src/Core/Relics/RelicDefinition.cs` | `bool Implemented = true` 追加 | **修正** |
| `src/Core/Relics/RelicJsonLoader.cs` | trigger switch に 4 値追加、`implemented` フィールド読み込み | **修正** |
| `src/Core/Relics/NonBattleRelicEffects.cs` | `Implemented:false` で早期 return ガード | **修正** |
| `src/Core/Potions/PotionDefinition.cs` | `UsableInBattle` / `UsableOutOfBattle` 削除、`IsUsableOutsideBattle` 派生プロパティ追加 | **修正（破壊的）** |
| `src/Core/Potions/PotionJsonLoader.cs` | 旧 bool フィールド読込削除 | **修正** |
| `src/Server/Controllers/CatalogController.cs` | `PotionCatalogEntryDto` から旧 2 bool 削除、`UsableOutsideBattle` 単独に置換 | **修正** |
| `src/Client/src/api/catalog.ts` | TypeScript インターフェイス対応（旧 2 bool → `usableOutsideBattle`） | **修正** |
| `src/Core/Data/Relics/*.json` (36 件) | `implemented` 追加、未実装は description プレフィックス `[未実装] ` | **一括書換** |
| `src/Core/Data/Potions/*.json` (7 件) | 旧 bool 削除、per-effect `battleOnly` 追加、3 件 action 正規化 | **一括書換** |
| `tests/Core.Tests/Relics/RelicTriggerTests.cs` | 新規（enum 9 値の整数値テスト） | **新規** |
| `tests/Core.Tests/Relics/RelicDefinitionTests.cs` | `Implemented` フィールド追加 | 修正 |
| `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs` | 新 4 trigger / `implemented` フィールド | 修正 |
| `tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs` | `Implemented:false` skip 検証追加 | 修正 |
| `tests/Core.Tests/Potions/PotionDefinitionTests.cs` | 新規（`IsUsableOutsideBattle` 派生） | **新規** |
| `tests/Core.Tests/Potions/PotionJsonLoaderTests.cs` | 旧 bool 削除、per-effect battleOnly 検証 | 修正 |
| `tests/Core.Tests/Fixtures/JsonFixtures.cs` | ポーション fixture を新形式に更新 | 修正 |
| `tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs` | 36 relic + 7 potion 全件ロード成功、health_potion.IsUsableOutsideBattle 等 | 修正 |
| `tests/Core.Tests/Relics/RelicJsonMigrationTests.cs` | grep ベース migration completeness | **新規** |
| `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` | 第 2-7 章補記 | 修正 |

---

## Task 1: RelicTrigger enum を 9 値に拡張

**Files:**
- Modify: `src/Core/Relics/RelicTrigger.cs`
- Test (new): `tests/Core.Tests/Relics/RelicTriggerTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Relics/RelicTriggerTests.cs` を新規作成:

```csharp
using RoguelikeCardGame.Core.Relics;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicTriggerTests
{
    [Fact]
    public void OnPickup_value_is_zero() => Assert.Equal(0, (int)RelicTrigger.OnPickup);

    [Fact]
    public void Passive_value_is_one() => Assert.Equal(1, (int)RelicTrigger.Passive);

    [Fact]
    public void OnBattleStart_value_is_two() => Assert.Equal(2, (int)RelicTrigger.OnBattleStart);

    [Fact]
    public void OnBattleEnd_value_is_three() => Assert.Equal(3, (int)RelicTrigger.OnBattleEnd);

    [Fact]
    public void OnMapTileResolved_value_is_four() => Assert.Equal(4, (int)RelicTrigger.OnMapTileResolved);

    [Fact]
    public void OnTurnStart_value_is_five() => Assert.Equal(5, (int)RelicTrigger.OnTurnStart);

    [Fact]
    public void OnTurnEnd_value_is_six() => Assert.Equal(6, (int)RelicTrigger.OnTurnEnd);

    [Fact]
    public void OnCardPlay_value_is_seven() => Assert.Equal(7, (int)RelicTrigger.OnCardPlay);

    [Fact]
    public void OnEnemyDeath_value_is_eight() => Assert.Equal(8, (int)RelicTrigger.OnEnemyDeath);
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicTriggerTests" --nologo`
Expected: 5 値（既存）はパスするが、4 新規値（OnTurnStart 等）がコンパイルエラー「メンバが見つかりません」

- [ ] **Step 3: RelicTrigger.cs を 9 値 + 整数値明示に更新**

`src/Core/Relics/RelicTrigger.cs` を以下に置換:

```csharp
namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックの効果発動タイミング。</summary>
public enum RelicTrigger
{
    /// <summary>入手した瞬間に 1 度だけ発動する。</summary>
    OnPickup           = 0,
    /// <summary>所持している間、常に効果を発揮する（runtime 計算は呼び出し側）。</summary>
    Passive            = 1,
    /// <summary>戦闘開始時に発動する（Phase 10.2 で発火）。</summary>
    OnBattleStart      = 2,
    /// <summary>戦闘終了時に発動する（Phase 10.2 で発火）。</summary>
    OnBattleEnd        = 3,
    /// <summary>マスのイベント解決後に発動する（NonBattleRelicEffects で発火）。</summary>
    OnMapTileResolved  = 4,
    /// <summary>各ターン開始時に発動する（Phase 10.2 で発火）。</summary>
    OnTurnStart        = 5,
    /// <summary>各ターン終了時に発動する（Phase 10.2 で発火）。</summary>
    OnTurnEnd          = 6,
    /// <summary>カードプレイ時に発動する（Phase 10.2 で発火、条件絞りは将来拡張）。</summary>
    OnCardPlay         = 7,
    /// <summary>敵撃破時に発動する（Phase 10.2 で発火）。</summary>
    OnEnemyDeath       = 8,
}
```

- [ ] **Step 4: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicTriggerTests" --nologo`
Expected: 9 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Relics/RelicTrigger.cs tests/Core.Tests/Relics/RelicTriggerTests.cs
git commit -m "feat(relics): extend RelicTrigger enum to 9 values with explicit integer values"
```

---

## Task 2: RelicJsonLoader の trigger switch を 9 値対応に拡張

**Files:**
- Modify: `src/Core/Relics/RelicJsonLoader.cs`
- Test: `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs`

- [ ] **Step 1: 失敗テストを 4 件追加**

`tests/Core.Tests/Relics/RelicJsonLoaderTests.cs` の末尾（最後のテストメソッド `}` の後・class `}` の前）に 4 件追加:

```csharp
    [Fact]
    public void ParseRelicWithOnTurnStartTrigger()
    {
        var json = """
        {
          "id":"r1","name":"r1","rarity":1,"trigger":"OnTurnStart","effects":[]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.OnTurnStart, def.Trigger);
    }

    [Fact]
    public void ParseRelicWithOnTurnEndTrigger()
    {
        var json = """
        {
          "id":"r2","name":"r2","rarity":1,"trigger":"OnTurnEnd","effects":[]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.OnTurnEnd, def.Trigger);
    }

    [Fact]
    public void ParseRelicWithOnCardPlayTrigger()
    {
        var json = """
        {
          "id":"r3","name":"r3","rarity":1,"trigger":"OnCardPlay","effects":[]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.OnCardPlay, def.Trigger);
    }

    [Fact]
    public void ParseRelicWithOnEnemyDeathTrigger()
    {
        var json = """
        {
          "id":"r4","name":"r4","rarity":1,"trigger":"OnEnemyDeath","effects":[]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.OnEnemyDeath, def.Trigger);
    }
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicJsonLoaderTests" --nologo`
Expected: 新 4 件は `RelicJsonException("trigger の値 \"OnTurnStart\" は無効です ...")` で失敗

- [ ] **Step 3: ParseTrigger switch に 4 値追加**

`src/Core/Relics/RelicJsonLoader.cs` の `ParseTrigger` メソッドを以下に置換:

```csharp
    private static RelicTrigger ParseTrigger(string s, string? id) => s switch
    {
        "OnPickup" => RelicTrigger.OnPickup,
        "Passive" => RelicTrigger.Passive,
        "OnBattleStart" => RelicTrigger.OnBattleStart,
        "OnBattleEnd" => RelicTrigger.OnBattleEnd,
        "OnMapTileResolved" => RelicTrigger.OnMapTileResolved,
        "OnTurnStart" => RelicTrigger.OnTurnStart,
        "OnTurnEnd" => RelicTrigger.OnTurnEnd,
        "OnCardPlay" => RelicTrigger.OnCardPlay,
        "OnEnemyDeath" => RelicTrigger.OnEnemyDeath,
        _ => throw new RelicJsonException($"trigger の値 \"{s}\" は無効です (relic id={id})。"),
    };
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicJsonLoaderTests" --nologo`
Expected: 全件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Relics/RelicJsonLoader.cs tests/Core.Tests/Relics/RelicJsonLoaderTests.cs
git commit -m "feat(relics): support 4 new trigger values in RelicJsonLoader"
```

---

## Task 3: RelicDefinition に Implemented フィールドを追加

**Files:**
- Modify: `src/Core/Relics/RelicDefinition.cs`
- Test: `tests/Core.Tests/Relics/RelicDefinitionTests.cs`

- [ ] **Step 1: 失敗テストを 3 件追加**

`tests/Core.Tests/Relics/RelicDefinitionTests.cs` のクラスの末尾（既存 2 メソッドの後）に追加:

```csharp
    [Fact]
    public void Implemented_defaults_to_true()
    {
        var def = new RelicDefinition(
            Id: "r",
            Name: "name",
            Rarity: CardRarity.Common,
            Trigger: RelicTrigger.OnPickup,
            Effects: new List<CardEffect>());
        Assert.True(def.Implemented);
    }

    [Fact]
    public void Implemented_can_be_set_false()
    {
        var def = new RelicDefinition(
            Id: "r",
            Name: "name",
            Rarity: CardRarity.Common,
            Trigger: RelicTrigger.OnPickup,
            Effects: new List<CardEffect>(),
            Description: "",
            Implemented: false);
        Assert.False(def.Implemented);
    }

    [Fact]
    public void Records_with_different_Implemented_are_not_equal()
    {
        var a = new RelicDefinition("r", "n", CardRarity.Common, RelicTrigger.OnPickup,
                                    new List<CardEffect>(), "", true);
        var b = new RelicDefinition("r", "n", CardRarity.Common, RelicTrigger.OnPickup,
                                    new List<CardEffect>(), "", false);
        Assert.NotEqual(a, b);
    }
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicDefinitionTests" --nologo`
Expected: 新 3 件は「`Implemented` が見つかりません」または「7 番目の引数が見つかりません」でコンパイルエラー

- [ ] **Step 3: RelicDefinition.cs に Implemented フィールド追加**

`src/Core/Relics/RelicDefinition.cs` を以下に置換:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックのマスター定義。</summary>
/// <param name="Implemented">
/// false の場合、エンジンは effects を一切処理しない（戦闘外・戦闘内とも no-op）。
/// プレイヤー所持・図鑑掲載は通常通り。description には [未実装] プレフィックスを付ける。
/// Phase 10 設計書（10.1.C）第 3-1 章参照。
/// </param>
public sealed record RelicDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    RelicTrigger Trigger,
    IReadOnlyList<CardEffect> Effects,
    string Description = "",
    bool Implemented = true);
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicDefinitionTests" --nologo`
Expected: 5 件全 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Relics/RelicDefinition.cs tests/Core.Tests/Relics/RelicDefinitionTests.cs
git commit -m "feat(relics): add Implemented flag to RelicDefinition (default true)"
```

---

## Task 4: RelicJsonLoader で `implemented` フィールドを読み込む

**Files:**
- Modify: `src/Core/Relics/RelicJsonLoader.cs`
- Test: `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs`

- [ ] **Step 1: 失敗テストを 3 件追加**

`tests/Core.Tests/Relics/RelicJsonLoaderTests.cs` のクラスの末尾に追加:

```csharp
    [Fact]
    public void Implemented_defaults_to_true_when_field_missing()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"trigger":"OnPickup","effects":[]}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.True(def.Implemented);
    }

    [Fact]
    public void Implemented_explicit_false_is_loaded()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"trigger":"OnPickup","effects":[],"implemented":false}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.False(def.Implemented);
    }

    [Fact]
    public void Implemented_explicit_true_is_loaded()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"trigger":"OnPickup","effects":[],"implemented":true}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.True(def.Implemented);
    }
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicJsonLoaderTests" --nologo`
Expected:
- `Implemented_defaults_to_true_when_field_missing` → 既定値 true なので PASS（**注意: ここはすでに緑**）
- `Implemented_explicit_false_is_loaded` → `implemented:false` を読まずに既定 true を返すので FAIL
- `Implemented_explicit_true_is_loaded` → 同上、PASS（既定 true と一致）

実質的に失敗するのは false ケース 1 件。

- [ ] **Step 3: Parse メソッドに implemented 読み込みを追加**

`src/Core/Relics/RelicJsonLoader.cs` の `Parse` メソッド内、`description` の解決の後、`return` の前に以下を追加し、`return` 文も置換:

```csharp
                // description は任意フィールド (図鑑 / ツールチップ向けのフレーバーテキスト)
                var description = root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
                    ? descEl.GetString() ?? string.Empty
                    : string.Empty;

                // implemented は任意フィールド (省略時 true)
                bool implemented = true;
                if (root.TryGetProperty("implemented", out var implEl))
                {
                    if (implEl.ValueKind == JsonValueKind.True) implemented = true;
                    else if (implEl.ValueKind == JsonValueKind.False) implemented = false;
                    else throw new RelicJsonException(
                        $"implemented は boolean である必要があります (relic id={id})。");
                }

                return new RelicDefinition(id, name, rarity, trigger, effects, description, implemented);
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicJsonLoaderTests" --nologo`
Expected: 全件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Relics/RelicJsonLoader.cs tests/Core.Tests/Relics/RelicJsonLoaderTests.cs
git commit -m "feat(relics): read implemented field from relic JSON (default true)"
```

---

## Task 5: NonBattleRelicEffects に Implemented:false ガードを追加

**Files:**
- Modify: `src/Core/Relics/NonBattleRelicEffects.cs`
- Test: `tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs`

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs` のクラスの末尾に追加:

```csharp
    [Fact]
    public void ApplyOnPickup_NotImplementedRelic_NoOp()
    {
        // burning_blood は Implemented: false の予定（Task 7 の JSON 移行後）
        // ここでは loader を直叩きせず、catalog 経由でテスト用のレリックを構築
        var s0 = Sample(gold: 99);
        var fakeCatalog = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_pickup",
            trigger: RelicTrigger.OnPickup,
            effects: new[] { new RoguelikeCardGame.Core.Cards.CardEffect(
                "gainGold", RoguelikeCardGame.Core.Cards.EffectScope.Self, null, 50) },
            implemented: false);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "fake_unimpl_pickup", fakeCatalog);
        Assert.Equal(99, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_NotImplementedRelic_NoOp()
    {
        var s0 = Sample(gold: 10) with { Relics = new List<string> { "fake_unimpl_tile" } };
        var fakeCatalog = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_tile",
            trigger: RelicTrigger.OnMapTileResolved,
            effects: new[] { new RoguelikeCardGame.Core.Cards.CardEffect(
                "gainGold", RoguelikeCardGame.Core.Cards.EffectScope.Self, null, 1) },
            implemented: false);
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, fakeCatalog);
        Assert.Equal(10, s1.Gold);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_NotImplementedRelic_NoOp()
    {
        var s0 = Sample() with { Relics = new List<string> { "fake_unimpl_passive" } };
        var fakeCatalog = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_passive",
            trigger: RelicTrigger.Passive,
            effects: new[] { new RoguelikeCardGame.Core.Cards.CardEffect(
                "restHealBonus", RoguelikeCardGame.Core.Cards.EffectScope.Self, null, 10) },
            implemented: false);
        int bonus = NonBattleRelicEffects.ApplyPassiveRestHealBonus(0, s0, fakeCatalog);
        Assert.Equal(0, bonus);
    }

    private static DataCatalog BuildCatalogWithFakeRelic(
        string id, RelicTrigger trigger,
        IReadOnlyList<RoguelikeCardGame.Core.Cards.CardEffect> effects,
        bool implemented)
    {
        var fake = new RelicDefinition(
            id, name: $"fake_{id}",
            Rarity: RoguelikeCardGame.Core.Cards.CardRarity.Common,
            Trigger: trigger,
            Effects: effects,
            Description: "",
            Implemented: implemented);

        // 既存 Catalog を複製して fake relic を追加
        var orig = Catalog;
        var relics = orig.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return orig with { Relics = relics };
    }
```

> **注意**: `DataCatalog` の `with` 式 + `Relics` プロパティ書換が可能か事前確認。`DataCatalog` が `record` で `Relics` が public init なら問題なし。そうでない場合はテスト helper を以下のいずれかに変更:
> - public ctor を直接呼ぶ（`new DataCatalog(cards: orig.Cards, relics: relics, ...)`）
> - `DataCatalog` に `WithRelics` ヘルパーがあれば利用

実装側で対応していなければこのテストの helper を `with` 式以外に書き換えてビルドを通す。**Step 2 のビルド失敗内容で判断**。

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~NonBattleRelicEffectsTests" --nologo`
Expected: 新 3 件が**機能的に**失敗（gainGold の effect が走って Gold が増える / restHealBonus が走って bonus が 10 になる）。または `BuildCatalogWithFakeRelic` がコンパイルエラーなら helper を実態に合わせて書き換えてから再実行。

- [ ] **Step 3: NonBattleRelicEffects.cs にガード追加**

`src/Core/Relics/NonBattleRelicEffects.cs` の各メソッドに Implemented チェックを追加:

```csharp
public static RunState ApplyOnPickup(RunState s, string relicId, DataCatalog catalog)
{
    if (!catalog.TryGetRelic(relicId, out var def)) return s;
    if (!def.Implemented) return s;
    if (def.Trigger != RelicTrigger.OnPickup) return s;
    return ApplyEffects(s, def);
}

public static RunState ApplyOnMapTileResolved(RunState s, DataCatalog catalog)
{
    foreach (var id in s.Relics)
    {
        if (!catalog.TryGetRelic(id, out var def)) continue;
        if (!def.Implemented) continue;
        if (def.Trigger != RelicTrigger.OnMapTileResolved) continue;
        s = ApplyEffects(s, def);
    }
    return s;
}

public static int ApplyPassiveRestHealBonus(int baseBonus, RunState s, DataCatalog catalog)
{
    int bonus = baseBonus;
    foreach (var id in s.Relics)
    {
        if (!catalog.TryGetRelic(id, out var def)) continue;
        if (!def.Implemented) continue;
        if (def.Trigger != RelicTrigger.Passive) continue;
        foreach (var eff in def.Effects)
            if (eff.Action == "restHealBonus") bonus += eff.Amount;
    }
    return bonus;
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~NonBattleRelicEffectsTests" --nologo`
Expected: 新 3 件含めて全件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Relics/NonBattleRelicEffects.cs tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs
git commit -m "feat(relics): skip Implemented:false relics in NonBattleRelicEffects"
```

---

## Task 6: PotionDefinition のシェイプ変更（破壊的、複数ファイル原子的に修正）

**Files:**
- Modify: `src/Core/Potions/PotionDefinition.cs`
- Modify: `src/Core/Potions/PotionJsonLoader.cs`
- Modify: `src/Server/Controllers/CatalogController.cs`
- Modify: `src/Client/src/api/catalog.ts`
- Modify: `tests/Core.Tests/Fixtures/JsonFixtures.cs`（ポーション fixture 更新）
- Test (new): `tests/Core.Tests/Potions/PotionDefinitionTests.cs`
- Modify: `tests/Core.Tests/Potions/PotionJsonLoaderTests.cs`

> **重要**: このタスクは `PotionDefinition` のコンストラクタ引数を 6 → 4 に減らす破壊的変更。中間コミットでビルドが赤になるため、すべての変更を 1 コミットに収める。

- [ ] **Step 1: 新規テストファイル `PotionDefinitionTests.cs` を作成**

`tests/Core.Tests/Potions/PotionDefinitionTests.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Potions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Potions;

public class PotionDefinitionTests
{
    [Fact]
    public void IsUsableOutsideBattle_true_when_any_effect_is_not_battleOnly()
    {
        var def = new PotionDefinition(
            "p", "n", CardRarity.Common,
            new List<CardEffect>
            {
                new("heal", EffectScope.Self, null, 10, BattleOnly: false),
            });
        Assert.True(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void IsUsableOutsideBattle_false_when_all_effects_are_battleOnly()
    {
        var def = new PotionDefinition(
            "p", "n", CardRarity.Common,
            new List<CardEffect>
            {
                new("block", EffectScope.Self, null, 12, BattleOnly: true),
            });
        Assert.False(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void IsUsableOutsideBattle_false_when_effects_empty()
    {
        var def = new PotionDefinition(
            "p", "n", CardRarity.Common,
            new List<CardEffect>());
        Assert.False(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void IsUsableOutsideBattle_true_when_mixed_with_at_least_one_non_battleOnly()
    {
        var def = new PotionDefinition(
            "p", "n", CardRarity.Common,
            new List<CardEffect>
            {
                new("block", EffectScope.Self, null, 12, BattleOnly: true),
                new("heal", EffectScope.Self, null, 10, BattleOnly: false),
            });
        Assert.True(def.IsUsableOutsideBattle);
    }
}
```

- [ ] **Step 2: PotionDefinition.cs を新シェイプに置換**

`src/Core/Potions/PotionDefinition.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Potions;

/// <summary>ポーションのマスター定義。</summary>
public sealed record PotionDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    IReadOnlyList<CardEffect> Effects)
{
    /// <summary>
    /// 戦闘外で使用可能か。effects のいずれかが BattleOnly=false なら true。
    /// 全 effect が BattleOnly=true なら false（マップ画面でグレーアウト）。
    /// Phase 10 設計書（10.1.C）第 3-3 章参照。
    /// </summary>
    public bool IsUsableOutsideBattle => Effects.Any(e => !e.BattleOnly);
}
```

- [ ] **Step 3: PotionJsonLoader.cs を新シェイプ対応に修正**

`src/Core/Potions/PotionJsonLoader.cs` の `Parse` メソッド内、`usableInBattle` / `usableOutOfBattle` を読む 2 行と `GetRequiredBool` ヘルパーを削除し、コンストラクタ呼出を修正:

```csharp
    public static PotionDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new PotionJsonException("ポーション JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);

                // rarity: 範囲チェック
                var rawRarity = GetRequiredInt(root, "rarity", id);
                if (!Enum.IsDefined(typeof(CardRarity), rawRarity))
                    throw new PotionJsonException($"rarity の値 {rawRarity} は無効です (potion id={id})。");
                var rarity = (CardRarity)rawRarity;

                var effects = ParseEffects(root, "effects", id);

                return new PotionDefinition(id, name, rarity, effects);
            }
            catch (PotionJsonException)
            {
                throw; // already contextual
            }
            catch (Exception ex) when (ex is not PotionJsonException)
            {
                var where = id is null ? "(potion id unknown)" : $"(potion id={id})";
                throw new PotionJsonException($"ポーション JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }
```

`GetRequiredBool` ヘルパーメソッド全体を削除（使われなくなる）。

- [ ] **Step 4: JsonFixtures.cs のポーション fixture を新形式に更新**

`tests/Core.Tests/Fixtures/JsonFixtures.cs` の `BlockPotionJson` / `FirePotionJson` / `PotionMissingUsableFlagsJson` 定数を以下に置換:

```csharp
    public const string BlockPotionJson = """
    {
      "id": "block_potion",
      "name": "ブロックポーション",
      "rarity": 1,
      "effects": [ { "action": "block", "scope": "self", "amount": 12, "battleOnly": true } ]
    }
    """;

    public const string FirePotionJson = """
    {
      "id": "fire_potion",
      "name": "ファイアポーション",
      "rarity": 1,
      "effects": [ { "action": "attack", "scope": "single", "side": "enemy", "amount": 20, "battleOnly": true } ]
    }
    """;

    // 旧形式の usable* フラグ欠落テスト用 fixture は削除（新形式では存在しない）
    // 以前 PotionMissingUsableFlagsJson だった定数を参照しているテストは Step 5 で削除
```

`PotionMissingUsableFlagsJson` 定数を完全に削除（その定数を使うテストが Step 5 で削除される）。

- [ ] **Step 5: PotionJsonLoaderTests.cs を新形式対応に書換**

`tests/Core.Tests/Potions/PotionJsonLoaderTests.cs` 全体を以下に置換:

```csharp
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Potions;

public class PotionJsonLoaderTests
{
    [Fact]
    public void ParseBlockPotion()
    {
        var def = PotionJsonLoader.Parse(JsonFixtures.BlockPotionJson);
        Assert.Equal("block_potion", def.Id);
        var eff = def.Effects.Single();
        Assert.Equal("block", eff.Action);
        Assert.Equal(12, eff.Amount);
        Assert.True(eff.BattleOnly);
        Assert.False(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void ParseFirePotion()
    {
        var def = PotionJsonLoader.Parse(JsonFixtures.FirePotionJson);
        var eff = def.Effects.Single();
        Assert.Equal("attack", eff.Action);
        Assert.Equal(20, eff.Amount);
        Assert.True(eff.BattleOnly);
        Assert.False(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void ParsePotionWithoutBattleOnly_IsUsableOutsideBattle()
    {
        var json = """
        {
          "id": "health_test",
          "name": "テスト",
          "rarity": 1,
          "effects": [ { "action": "heal", "scope": "self", "amount": 15 } ]
        }
        """;
        var def = PotionJsonLoader.Parse(json);
        var eff = def.Effects.Single();
        Assert.False(eff.BattleOnly);
        Assert.True(def.IsUsableOutsideBattle);
    }
}
```

- [ ] **Step 6: CatalogController.cs を新シェイプ対応に修正**

`src/Server/Controllers/CatalogController.cs`:

`PotionCatalogEntryDto` レコード定義を以下に置換（旧 2 bool を `UsableOutsideBattle` 単独に）:

```csharp
    public sealed record PotionCatalogEntryDto(
        string Id,
        string Name,
        int Rarity,
        bool UsableOutsideBattle,
        string Description);
```

`GetPotions()` メソッドの構築呼出を以下に置換:

```csharp
    [HttpGet("potions")]
    public IActionResult GetPotions()
    {
        var result = new Dictionary<string, PotionCatalogEntryDto>(_data.Potions.Count);
        foreach (var (id, def) in _data.Potions)
        {
            result[id] = new PotionCatalogEntryDto(
                def.Id,
                def.Name,
                (int)def.Rarity,
                def.IsUsableOutsideBattle,
                DescribePotionEffects(def));
        }
        return Ok(result);
    }
```

`DescribePotionEffects` ヘルパーを以下に置換:

```csharp
    private static string DescribePotionEffects(Core.Potions.PotionDefinition def)
    {
        var prefix = def.IsUsableOutsideBattle ? "" : "[戦闘中] ";
        return prefix + DescribeEffects(def.Effects);
    }
```

- [ ] **Step 7: Client TS の型定義を更新**

`src/Client/src/api/catalog.ts` の Potion 型定義部分を確認し、`usableInBattle` / `usableOutOfBattle` を `usableOutsideBattle` 1 つに置換:

```typescript
// 旧 19-20 行目周辺
//   usableInBattle: boolean
//   usableOutOfBattle: boolean
// を以下に置換:
  usableOutsideBattle: boolean
```

正確な行番号と context は実装時に Read で確認。

- [ ] **Step 8: ビルド確認**

Run: `dotnet build --nologo`
Expected: 0 警告 0 エラー（Core / Server / 全テストプロジェクト）

(Client TS は `dotnet build` には含まれない。型エラー検出は Step 9 の `npm run build` で。)

- [ ] **Step 9: dotnet test 実行**

Run: `dotnet test --nologo --no-build`
Expected: 全件 PASS（Core 既存 + Server 既存 + 新規 PotionDefinitionTests 4 件 + 修正された PotionJsonLoaderTests 3 件）

> **失敗時の典型**: `EmbeddedDataLoaderTests` 等で「7 ポーション JSON が新形式でない」と落ちる場合がある。それは Task 8 の前なので**正常**。Task 6 は production / fixture テストのみが緑になればよい（embedded JSON テストは Task 8 後で確認）。

正確には: `dotnet test` 中で `EmbeddedDataLoaderTests` が落ちる場合は、ポーション 7 ファイルの旧 `usableInBattle` / `usableOutOfBattle` が `PotionJsonLoader` で読まれなくなったから（今回はそもそも使わないので問題なし、JSON 側に旧フィールドが残っていても無視される）。**ロード自体は今は壊れない**（旧フィールドは ignore される）。

念のため確認:
```bash
dotnet test tests/Core.Tests --filter "FullyQualifiedName~EmbeddedDataLoaderTests" --nologo --no-build
```
これも PASS のはず。

- [ ] **Step 10: Client TS ビルド確認（オプション）**

Run: `cd src/Client && npm run build` （※ TypeScript エラーチェック）
Expected: エラー無し。型 `Potion` の使用箇所がエラーになっていれば修正。

- [ ] **Step 11: コミット**

```bash
git add src/Core/Potions/ src/Server/Controllers/CatalogController.cs src/Client/src/api/catalog.ts \
        tests/Core.Tests/Potions/ tests/Core.Tests/Fixtures/JsonFixtures.cs
git commit -m "refactor(potions): drop UsableInBattle/UsableOutOfBattle in favor of derived IsUsableOutsideBattle"
```

---

## Task 7: 36 レリック JSON を新形式に一括移行

**Files:**
- Modify: `src/Core/Data/Relics/*.json`（全 36 ファイル）

このタスクで、各レリック JSON に `implemented` フィールドを追加し、`Implemented: false` のものは description の先頭に `[未実装] ` を付加する。

**Implemented 値の割当（spec 第 4-2 章より）:**

| Implemented: true | Implemented: false |
|---|---|
| `act1_start_01.json` ～ `act1_start_05.json`（5） | `bell_earring.json` |
| `act2_start_01.json` ～ `act2_start_05.json`（5） | `big_bag.json` |
| `act3_start_01.json` ～ `act3_start_05.json`（5） | `bone_earring.json` |
| `coin_purse.json` | `burning_blood.json` |
| `extra_max_hp.json` | `claw_earring.json` |
| `traveler_boots.json` | `gamble_dice.json` |
| `warm_blanket.json` | `gauntlet.json` |
| | `honeycomb_stone.json` |
| | `lantern.json` |
| | `magic_pouch.json` |
| | `mana_tarot.json` |
| | `nice_acorn.json` |
| | `nyango_bell.json` |
| | `ritual_chalice.json` |
| | `skull_fish.json` |
| | `skull_mushroom.json` |
| | `thorn_collar.json` |
| **合計 19 ファイル** | **合計 17 ファイル** |

- [ ] **Step 1: Implemented:true レリック 19 ファイルに `"implemented": true` を追加**

各ファイルの末尾（最後のフィールドの後）に `"implemented": true` を追加し、JSON 構造を保つ。

例（`coin_purse.json`）の前後:

```json
{
  "id": "coin_purse",
  "name": "コインポーチ",
  "rarity": 1,
  "trigger": "OnPickup",
  "description": "...",
  "effects": [...],
  "implemented": true
}
```

対象 19 ファイル: `act1_start_01.json` ～ `act3_start_05.json`（15）+ `coin_purse.json`、`extra_max_hp.json`、`traveler_boots.json`、`warm_blanket.json`（4）。

- [ ] **Step 2: Implemented:false レリック 17 ファイルを更新**

各ファイル:
- `description` フィールドの先頭に `[未実装] ` を付加
- `"implemented": false` を追加

例（`burning_blood.json`）:

```json
{
  "id": "burning_blood",
  "name": "血のサンゴ",
  "rarity": 1,
  "trigger": "OnBattleEnd",
  "description": "[未実装] 深紅に脈打つ珊瑚。戦いを終えるたび、血肉に低く熱を返してくる。",
  "implemented": false,
  "effects": [{ "action": "healPercent", "scope": "self", "amount": 6 }]
}
```

例（`bell_earring.json` — 空 effects）:

```json
{
  "id": "bell_earring",
  "name": "鈴のイヤリング",
  "rarity": 1,
  "trigger": "Passive",
  "description": "[未実装] ちりんと鳴る、小さな鈴のイヤリング。...",
  "implemented": false,
  "effects": []
}
```

対象 17 ファイル: `bell_earring.json`, `big_bag.json`, `bone_earring.json`, `burning_blood.json`, `claw_earring.json`, `gamble_dice.json`, `gauntlet.json`, `honeycomb_stone.json`, `lantern.json`, `magic_pouch.json`, `mana_tarot.json`, `nice_acorn.json`, `nyango_bell.json`, `ritual_chalice.json`, `skull_fish.json`, `skull_mushroom.json`, `thorn_collar.json`。

> **注意**: `effects` 配列はそのまま保持する（空でも、`healPercent` 等の未実装 action でも、設計意図保持のため触らない）。

- [ ] **Step 3: 全 36 ファイルが `implemented` を持つことを確認**

Run:
```bash
grep -L '"implemented"' src/Core/Data/Relics/*.json
```
Expected: 出力なし（全ファイルが `"implemented"` を含む）

- [ ] **Step 4: Implemented:false の 17 ファイルが `[未実装]` を持つことを確認**

Run:
```bash
grep -lE '"implemented"\s*:\s*false' src/Core/Data/Relics/*.json | xargs grep -L '\[未実装\]'
```
Expected: 出力なし（false のファイルは全て `[未実装]` を含む）

- [ ] **Step 5: 全テスト実行（embedded loader テストで全件ロード確認）**

Run: `dotnet test --nologo`
Expected: 全件 PASS（embedded loader が新 JSON で全件正常にロードできる）

- [ ] **Step 6: コミット**

```bash
git add src/Core/Data/Relics/
git commit -m "data(relics): migrate 36 relic JSONs to new format with Implemented flag"
```

---

## Task 8: 7 ポーション JSON を新形式に一括移行

**Files:**
- Modify: `src/Core/Data/Potions/*.json`（全 7 ファイル）

各ポーション:
- 旧 `usableInBattle` / `usableOutOfBattle` フィールドを削除
- 各 effect に必要なら `battleOnly: true` を追加（health_potion 以外）
- 3 件は action 名を標準語彙に正規化

- [ ] **Step 1: `block_potion.json`**

```json
{
  "id": "block_potion",
  "name": "ブロックポーション",
  "rarity": 1,
  "effects": [{ "action": "block", "scope": "self", "amount": 12, "battleOnly": true }]
}
```

- [ ] **Step 2: `energy_potion.json`**

```json
{
  "id": "energy_potion",
  "name": "エナジーポーション",
  "rarity": 1,
  "effects": [{ "action": "gainEnergy", "scope": "self", "amount": 2, "battleOnly": true }]
}
```

- [ ] **Step 3: `fire_potion.json`**

```json
{
  "id": "fire_potion",
  "name": "ファイアポーション",
  "rarity": 1,
  "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 20, "battleOnly": true }]
}
```

- [ ] **Step 4: `health_potion.json`**

```json
{
  "id": "health_potion",
  "name": "ヘルスポーション",
  "rarity": 1,
  "effects": [{ "action": "heal", "scope": "self", "amount": 15 }]
}
```

(`battleOnly` 省略 = 既定 false → `IsUsableOutsideBattle` が true になる)

- [ ] **Step 5: `poison_potion.json`**（action 正規化: `applyPoison` → `debuff` + name "poison"）

```json
{
  "id": "poison_potion",
  "name": "ポイズンポーション",
  "rarity": 2,
  "effects": [{ "action": "debuff", "scope": "self", "name": "poison", "amount": 6, "battleOnly": true }]
}
```

- [ ] **Step 6: `strength_potion.json`**（action 正規化: `gainStrength` → `buff` + name "strength"）

```json
{
  "id": "strength_potion",
  "name": "ストレングスポーション",
  "rarity": 2,
  "effects": [{ "action": "buff", "scope": "self", "name": "strength", "amount": 2, "battleOnly": true }]
}
```

- [ ] **Step 7: `swift_potion.json`**（action 正規化: `drawCards` → `draw`）

```json
{
  "id": "swift_potion",
  "name": "スウィフトポーション",
  "rarity": 1,
  "effects": [{ "action": "draw", "scope": "self", "amount": 3, "battleOnly": true }]
}
```

- [ ] **Step 8: 全 7 ファイルから旧フィールドが消えたことを確認**

Run:
```bash
grep -nE '"usableInBattle"|"usableOutOfBattle"|"applyPoison"|"gainStrength"|"drawCards"' src/Core/Data/Potions/*.json
```
Expected: 出力なし

- [ ] **Step 9: EmbeddedDataLoader の挙動確認テストを `EmbeddedDataLoaderTests.cs` に追加**

`tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs` のクラス内に以下を追加:

```csharp
    [Fact]
    public void All_potion_JSONs_load_with_new_format()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Equal(7, catalog.Potions.Count);
        foreach (var (id, def) in catalog.Potions)
        {
            Assert.NotNull(def);
            Assert.NotEmpty(def.Effects);
        }
    }

    [Fact]
    public void HealthPotion_IsUsableOutsideBattle()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.True(catalog.Potions["health_potion"].IsUsableOutsideBattle);
    }

    [Fact]
    public void NonHealthPotions_AreNotUsableOutsideBattle()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var nonHealth = new[] { "block_potion", "energy_potion", "fire_potion",
                                "poison_potion", "strength_potion", "swift_potion" };
        foreach (var id in nonHealth)
            Assert.False(catalog.Potions[id].IsUsableOutsideBattle, $"{id} should not be usable outside battle");
    }

    [Fact]
    public void All_relic_JSONs_load_with_implemented_field()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Equal(36, catalog.Relics.Count);
        // 19 ファイルが Implemented: true、17 ファイルが Implemented: false の想定
        var trueCount = catalog.Relics.Values.Count(r => r.Implemented);
        var falseCount = catalog.Relics.Values.Count(r => !r.Implemented);
        Assert.Equal(19, trueCount);
        Assert.Equal(17, falseCount);
    }
```

- [ ] **Step 10: 全テスト実行で確認**

Run: `dotnet test --nologo`
Expected: 全件 PASS（新規 4 件 + 既存全件）

- [ ] **Step 11: コミット**

```bash
git add src/Core/Data/Potions/ tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs
git commit -m "data(potions): migrate 7 potion JSONs to new format (per-effect battleOnly + 3 action renames)"
```

---

## Task 9: Migration completeness grep テストを追加

**Files:**
- Test (new): `tests/Core.Tests/Relics/RelicJsonMigrationTests.cs`

旧フィールド・旧 action 名が embedded JSON に残らないことを CI で保証する static text-search テスト（Phase 10.1.B Task 15 の同種パターン）。

- [ ] **Step 1: テストファイルを作成**

`tests/Core.Tests/Relics/RelicJsonMigrationTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicJsonMigrationTests
{
    private static string RelicDir =>
        Path.Combine(FindRepoRoot(), "src", "Core", "Data", "Relics");

    private static string PotionDir =>
        Path.Combine(FindRepoRoot(), "src", "Core", "Data", "Potions");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !File.Exists(Path.Combine(dir.FullName, "RoguelikeCardGame.sln")) &&
               !File.Exists(Path.Combine(dir.FullName, "RoguelikeCardGame.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repo root not found");
        return dir.FullName;
    }

    public static IEnumerable<object[]> RelicFiles()
        => Directory.EnumerateFiles(RelicDir, "*.json").Select(f => new object[] { f });

    public static IEnumerable<object[]> PotionFiles()
        => Directory.EnumerateFiles(PotionDir, "*.json").Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(RelicFiles))]
    public void All_relics_have_implemented_field(string path)
    {
        var content = File.ReadAllText(path);
        Assert.Contains("\"implemented\"", content);
    }

    [Theory]
    [MemberData(nameof(PotionFiles))]
    public void Potion_JSON_no_legacy_usable_flags(string path)
    {
        var content = File.ReadAllText(path);
        var legacy = new[] { "\"usableInBattle\"", "\"usableOutOfBattle\"" };
        foreach (var key in legacy)
            Assert.False(content.Contains(key),
                $"{Path.GetFileName(path)} contains legacy field {key}");
    }

    [Theory]
    [MemberData(nameof(PotionFiles))]
    public void Potion_JSON_no_legacy_action_names(string path)
    {
        var content = File.ReadAllText(path);
        var legacyActions = new[] { "\"applyPoison\"", "\"gainStrength\"", "\"drawCards\"" };
        foreach (var name in legacyActions)
            Assert.False(content.Contains(name),
                $"{Path.GetFileName(path)} contains legacy action {name}");
    }
}
```

- [ ] **Step 2: テスト実行**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~RelicJsonMigrationTests" --nologo`
Expected: 36 + 7 + 7 = 50 件 PASS（relic 36 implemented チェック × 1 + potion 7 × 2 セット）

- [ ] **Step 3: コミット**

```bash
git add tests/Core.Tests/Relics/RelicJsonMigrationTests.cs
git commit -m "test(relics): add migration completeness grep test for relic/potion JSON"
```

---

## Task 10: 親 Phase 10 spec の補記

**Files:**
- Modify: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`

第 2-7 章を以下のように更新:

- [ ] **Step 1: RelicTrigger 列挙の修正**

第 2-7 章内の以下のコードブロック:
```csharp
public enum RelicTrigger {
    OnPickup, Passive, OnBattleStart,
    OnTurnStart,                                // 新規
    OnTurnEnd,                                  // 新規
    OnCardPlay,                                 // 新規（全カードプレイで発動、条件絞りは将来拡張）
    OnEnemyDeath                                // 新規
}
```

を以下に置換:

```csharp
public enum RelicTrigger {
    OnPickup           = 0,
    Passive            = 1,
    OnBattleStart      = 2,
    OnBattleEnd        = 3,                       // 既存（burning_blood 等で使用）
    OnMapTileResolved  = 4,                       // 既存（traveler_boots 等で使用）
    OnTurnStart        = 5,                       // 新規
    OnTurnEnd          = 6,                       // 新規
    OnCardPlay         = 7,                       // 新規
    OnEnemyDeath       = 8,                       // 新規
}
```

- [ ] **Step 2: Implemented セマンティクスの補強**

第 2-7 章内、Implemented フラグについての記述を見つけ、以下のような補記を追加（既存の説明文の後に挿入）:

```markdown
- `Implemented: false` のレリックは取得・所持は通常通り可能、図鑑にも掲載されるが効果は発動しない。
- **`NonBattleRelicEffects.cs` 内でも早期 return される**（OnPickup / OnMapTileResolved / Passive のいずれの戦闘外発火タイミングでも no-op）。
- description 先頭に `[未実装] ` プレフィックス（半角スペース込み）を付ける運用とする。
```

- [ ] **Step 3: コミット**

```bash
git add docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md
git commit -m "docs(spec): amend Phase 10 spec for 10.1.C decisions (9 RelicTrigger values, NonBattle Implemented guard)"
```

---

## Task 11: 全テスト緑確認 + Phase 5 placeholder 動作確認 + タグ付け

**Files:** なし（最終確認のみ）

- [ ] **Step 1: 全プロジェクトビルド**

Run: `dotnet build --nologo`
Expected: 0 警告 0 エラー

- [ ] **Step 2: 全テスト実行**

Run: `dotnet test --nologo --no-build`
Expected: 全テスト緑（10.1.B 時点 507 + 168 + 本プラン追加分）

- [ ] **Step 3: 旧フィールド・旧 action grep 確認**

```bash
grep -rn "UsableInBattle\|UsableOutOfBattle" src tests --include="*.cs"
```
Expected: マッチなし

```bash
grep -nE '"usableInBattle"|"usableOutOfBattle"|"applyPoison"|"gainStrength"|"drawCards"' src/Core/Data/Potions/*.json
```
Expected: マッチなし

- [ ] **Step 4: Implemented フィールド確認**

```bash
grep -L '"implemented"' src/Core/Data/Relics/*.json
```
Expected: 出力なし（36 ファイル全件含む）

- [ ] **Step 5: Phase 5 placeholder 動作確認（手動）**

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
4. 戦闘画面が出て即勝利（Phase 5 placeholder）
5. 報酬画面でカード選択
6. マップに戻る

Expected: Phase 10.1.C 着手前と同じ挙動。レリック取得時に `Implemented: true` のレリックの効果は通常通り適用、`Implemented: false` のレリックは取得しても効果なし。

- [ ] **Step 6: タグ付け + push**

```bash
git tag phase10-1C-complete
git push -u origin feature/phase10-1C-potion-relic-extension
git push origin phase10-1C-complete
```

完了。次は Phase 10.2（Core バトル本体）。

---

## 完了判定チェックリスト

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全テスト緑
- [ ] `UsableInBattle` / `UsableOutOfBattle` が production / tests に grep で 0 件
- [ ] 旧 JSON フィールド (`usableInBattle` / `usableOutOfBattle`) と旧 action (`applyPoison` / `gainStrength` / `drawCards`) が embedded Potion JSON に grep で 0 件
- [ ] 全 36 relic JSON が `implemented` フィールドを持つ
- [ ] `Implemented:false` の 17 relic に `[未実装] ` プレフィックスあり
- [ ] 全 7 potion JSON が新形式
- [ ] `RelicTrigger` enum が 9 値（整数値明示）
- [ ] `NonBattleRelicEffects` で `Implemented:false` skip ガードあり
- [ ] 親 Phase 10 spec が新方針に合わせて補記済み
- [ ] Phase 5 placeholder バトルが手動で動作確認済み
- [ ] `phase10-1C-complete` タグが切られて origin に push 済み

---

## 補足: Phase 10.1.C で**変更しない**もの

- 新 4 トリガー（OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）の戦闘内発火コード（Phase 10.2）
- 戦闘内 action（healPercent / extraEnergyOnFirstTurn 等）の実装（Phase 10.2）
- `Implemented:false` レリックを `true` に flip するためのゲームデザイン（個別レリック単位、10.2 以降）
- 空 effects レリック 15 件の effect 設計（10.2 以降のデザイン裁量）
- `BattleHub` / `BattleStateDto`（Phase 10.3）
- `BattleScreen.tsx` 配線（Phase 10.4）
- マップ画面のポーションスロット UI（Phase 10.5）

---

## 参照

- 設計書（10.1.C）: [`../specs/2026-04-26-phase10-1C-potion-relic-extension-design.md`](../specs/2026-04-26-phase10-1C-potion-relic-extension-design.md)
- 親 spec（Phase 10）: [`../specs/2026-04-25-phase10-battle-system-design.md`](../specs/2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン plan: [`2026-04-26-phase10-1B-move-unification.md`](2026-04-26-phase10-1B-move-unification.md)
- ロードマップ: [`2026-04-20-roadmap.md`](2026-04-20-roadmap.md)
