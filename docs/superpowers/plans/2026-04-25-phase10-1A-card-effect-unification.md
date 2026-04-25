# Phase 10.1.A — CardEffect 統一 + CardDefinition 拡張 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 既存の派生 record（`DamageEffect` / `GainBlockEffect` / `GainMaxHpEffect` / `GainGoldEffect` / `RestHealBonusEffect` / `UnknownEffect`）を捨て、`{Action, Scope, Side, Amount, Name, UnitId, ComboMin, Pile, BattleOnly}` の単一 record に統一する。同時に `CardDefinition` に `UpgradedCost` / `Keywords` を追加、`CardType` に `Status` / `Curse` を追加、関連ローダー・JSON データ・依存コードを新形式に移行する。完了時点で `dotnet build` / `dotnet test` が緑、Phase 5 placeholder バトルが引き続き動作する。

**Architecture:** 旧派生 record と新形式 record は API が完全に異なるため、並列維持はせず**一度に置換**する。各タスクは TDD 1 サイクル（失敗テスト → 実装 → 通過 → commit）で 1 ファイル単位の変更に絞る。`NonBattleRelicEffects` / `CatalogController` 等の依存箇所は新形式の `Action` 文字列マッチに書き換える。カード JSON は 32 ファイル、effect 構造を新形式に変換し、`upgradedCost` / `keywords` の省略デフォルトを利用してデータ最小化。

**Tech Stack:** C# .NET 10 / xUnit / `System.Text.Json`

**完了判定:**
- `dotnet build` 警告 0、エラー 0
- `dotnet test` 全テスト緑（既存 ~355 件 + 本 plan で追加するテスト）
- Phase 5 placeholder バトルが従来通り動作（マップで敵マスに入って即勝利・報酬画面へ遷移できる）
- 旧派生 record（`DamageEffect` 等）が grep で 0 件
- カード JSON 32 ファイルが新形式
- ブランチに `phase10-1A-complete` タグを切る

---

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `src/Core/Cards/EffectScope.cs` | 新 enum: `Self / Single / Random / All` | **新規** |
| `src/Core/Cards/EffectSide.cs` | 新 enum: `Enemy / Ally` | **新規** |
| `src/Core/Cards/CardEffect.cs` | 単一 record + `Normalize()` メソッド | **全面書き換え**（旧派生 record 全削除） |
| `src/Core/Cards/CardEffectParser.cs` | 新形式 effect JSON パーサー | **全面書き換え** |
| `src/Core/Cards/CardType.cs` | `Status` / `Curse` を enum 値に追加 | 修正 |
| `src/Core/Cards/CardDefinition.cs` | `UpgradedCost` / `Keywords` / `IsUpgradable` 追加 | 修正 |
| `src/Core/Cards/CardJsonLoader.cs` | 新フィールド対応、新 effect 形式に対応 | 修正 |
| `src/Core/Cards/CardUpgrade.cs` | `IsUpgradable` 計算プロパティを利用するよう修正 | 修正 |
| `src/Core/Relics/NonBattleRelicEffects.cs` | 派生 record マッチを `Action` 文字列マッチに書き換え | 修正 |
| `src/Server/Controllers/CatalogController.cs` | 派生 record 参照を新形式に書き換え | 修正 |
| `src/Core/Data/Cards/*.json` (32 ファイル) | effect の `type` フィールド → `action`/`scope`/`side`/`amount` 形式に変換 | 一括書き換え |
| `tests/Core.Tests/Cards/CardEffectTests.cs` | 新 record 用テストへ全面書き換え | 修正 |
| `tests/Core.Tests/Cards/CardDefinitionTests.cs` | 新フィールド対応 | 修正 |
| `tests/Core.Tests/Cards/CardJsonLoaderTests.cs` | 新形式 JSON 対応 | 修正 |
| `tests/Core.Tests/Cards/CardEnumTests.cs` | `Status` / `Curse` 追加 | 修正 |
| `tests/Core.Tests/Cards/CardUpgradeTests.cs` | `IsUpgradable` 連携の調整 | 修正 |
| `tests/Core.Tests/Cards/CardInstanceTests.cs` | 影響なし（確認のみ） | (確認) |
| `tests/Core.Tests/Events/EventJsonLoaderTests.cs` | テスト用 effect 構造を新形式に | 修正 |
| `tests/Core.Tests/Fixtures/JsonFixtures.cs` | 同上 | 修正 |
| `tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs` | 同上 | 修正 |
| `tests/Core.Tests/Relics/RelicDefinitionTests.cs` | 同上 | 修正 |
| `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs` | 同上 | 修正 |
| `tests/Core.Tests/Potions/PotionJsonLoaderTests.cs` | 同上 | 修正 |

---

## Task 1: EffectScope enum を追加

**Files:**
- Create: `src/Core/Cards/EffectScope.cs`
- Test: `tests/Core.Tests/Cards/EffectScopeTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/EffectScopeTests.cs` を新規作成:

```csharp
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class EffectScopeTests
{
    [Fact]
    public void Self_value_is_zero() => Assert.Equal(0, (int)EffectScope.Self);

    [Fact]
    public void Single_value_is_one() => Assert.Equal(1, (int)EffectScope.Single);

    [Fact]
    public void Random_value_is_two() => Assert.Equal(2, (int)EffectScope.Random);

    [Fact]
    public void All_value_is_three() => Assert.Equal(3, (int)EffectScope.All);
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EffectScopeTests"`
Expected: コンパイルエラー「型または名前空間 'EffectScope' が見つかりません」

- [ ] **Step 3: 最小実装を書く**

`src/Core/Cards/EffectScope.cs`:
```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// effect の対象スコープ。
/// Self = 発動主体本人、Single = 対象指定中の 1 体、
/// Random = ランダム 1 体、All = 全員。
/// </summary>
public enum EffectScope
{
    Self = 0,
    Single = 1,
    Random = 2,
    All = 3,
}
```

- [ ] **Step 4: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EffectScopeTests"`
Expected: PASS (4 件)

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/EffectScope.cs tests/Core.Tests/Cards/EffectScopeTests.cs
git commit -m "feat(cards): add EffectScope enum (Self/Single/Random/All)"
```

---

## Task 2: EffectSide enum を追加

**Files:**
- Create: `src/Core/Cards/EffectSide.cs`
- Test: `tests/Core.Tests/Cards/EffectSideTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/EffectSideTests.cs`:
```csharp
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class EffectSideTests
{
    [Fact]
    public void Enemy_value_is_zero() => Assert.Equal(0, (int)EffectSide.Enemy);

    [Fact]
    public void Ally_value_is_one() => Assert.Equal(1, (int)EffectSide.Ally);
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EffectSideTests"`
Expected: コンパイルエラー

- [ ] **Step 3: 最小実装**

`src/Core/Cards/EffectSide.cs`:
```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// effect の対象側。行動主体から見た相対視点で:
/// Enemy = 自分の対立側、Ally = 自分側。
/// </summary>
public enum EffectSide
{
    Enemy = 0,
    Ally = 1,
}
```

- [ ] **Step 4: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~EffectSideTests"`
Expected: PASS (2 件)

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/EffectSide.cs tests/Core.Tests/Cards/EffectSideTests.cs
git commit -m "feat(cards): add EffectSide enum (Enemy/Ally)"
```

---

## Task 3: 新 CardEffect record を導入（旧派生 record を全削除）

**Files:**
- Modify: `src/Core/Cards/CardEffect.cs`（旧 abstract record + 派生 6 種を削除し、単一 record に置換）
- Test: `tests/Core.Tests/Cards/CardEffectTests.cs`（既存内容を新形式テストで全面置換）

> **重要**: このタスク 3 完了後、`CardEffectParser` / `CardJsonLoader` / `NonBattleRelicEffects` / `CatalogController` / 各テストはコンパイルエラーになる。タスク 4 以降で順次修正する。

- [ ] **Step 1: 失敗テストを書く**

既存 `tests/Core.Tests/Cards/CardEffectTests.cs` の内容を全面置換:

```csharp
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardEffectTests
{
    [Fact]
    public void Default_BattleOnly_is_false()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
        Assert.False(e.BattleOnly);
    }

    [Fact]
    public void Optional_fields_default_to_null()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
        Assert.Null(e.Name);
        Assert.Null(e.UnitId);
        Assert.Null(e.ComboMin);
        Assert.Null(e.Pile);
    }

    [Fact]
    public void Records_with_same_field_values_are_equal()
    {
        var a = new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength");
        var b = new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Normalize_self_drops_side()
    {
        var e = new CardEffect("block", EffectScope.Self, EffectSide.Ally, 5);
        var n = e.Normalize();
        Assert.Null(n.Side);
    }

    [Fact]
    public void Normalize_attack_forces_side_enemy()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Ally, 5);
        var n = e.Normalize();
        Assert.Equal(EffectSide.Enemy, n.Side);
    }

    [Fact]
    public void Normalize_attack_with_null_side_forces_enemy()
    {
        var e = new CardEffect("attack", EffectScope.All, null, 5);
        var n = e.Normalize();
        Assert.Equal(EffectSide.Enemy, n.Side);
    }

    [Fact]
    public void Normalize_non_attack_with_side_keeps_side()
    {
        var e = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 2, Name: "vulnerable");
        var n = e.Normalize();
        Assert.Equal(EffectSide.Enemy, n.Side);
    }

    [Fact]
    public void Normalize_self_block_drops_side_even_if_specified()
    {
        var e = new CardEffect("block", EffectScope.Self, EffectSide.Ally, 5);
        var n = e.Normalize();
        Assert.Equal(EffectScope.Self, n.Scope);
        Assert.Null(n.Side);
        Assert.Equal(5, n.Amount);
    }

    [Fact]
    public void Normalize_idempotent()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5).Normalize();
        var twice = e.Normalize();
        Assert.Equal(e, twice);
    }
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet build src/Core`
Expected: コンパイルエラー（旧 `DamageEffect` 等が削除されていないため、CardEffect.cs は変更前のまま）

実際にはこのステップ 2 は「CardEffect.cs の旧構造を完全に置換した結果、依存箇所がコンパイルエラーになる」ことを確認するためにある。先に Step 3 の実装に進む。

- [ ] **Step 3: 最小実装**

`src/Core/Cards/CardEffect.cs` を全面置換:

```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// カード／敵 Move／召喚 Move／レリック／ポーション 共通の効果プリミティブ。
/// Phase 10 設計書 (2026-04-25-phase10-battle-system-design.md) 第 2-1 章参照。
/// </summary>
/// <param name="Action">"attack"|"block"|"buff"|"debuff"|"summon"|"heal"|"draw"|"discard"|"upgrade"|"exhaustCard"|"exhaustSelf"|"retainSelf"|"gainEnergy" など</param>
/// <param name="Scope">対象スコープ</param>
/// <param name="Side">対象側（行動主体からの相対視点）。Self では null</param>
/// <param name="Amount">効果量</param>
/// <param name="Name">buff/debuff の種類名 ("strength"|"vulnerable" 等)</param>
/// <param name="UnitId">summon 用：召喚キャラ ID</param>
/// <param name="ComboMin">コンボ N 以上で適用（カードのみ意味あり）</param>
/// <param name="Pile">"hand"|"discard"|"draw" (exhaustCard / upgrade / discard 用)</param>
/// <param name="BattleOnly">true なら戦闘外発動時にスキップ</param>
public sealed record CardEffect(
    string Action,
    EffectScope Scope,
    EffectSide? Side,
    int Amount,
    string? Name = null,
    string? UnitId = null,
    int? ComboMin = null,
    string? Pile = null,
    bool BattleOnly = false)
{
    /// <summary>
    /// JSON ロード時の safety net 正規化。
    /// - Scope=Self なら Side を破棄（null に）
    /// - Action=="attack" なら Side を Enemy に強制
    /// </summary>
    public CardEffect Normalize()
    {
        var side = Side;
        if (Scope == EffectScope.Self) side = null;
        if (Action == "attack") side = EffectSide.Enemy;
        return this with { Side = side };
    }
}
```

- [ ] **Step 4: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardEffectTests"`
Expected: 9 件 PASS

ただし、**プロジェクト全体のビルドはまだエラー**（CardEffectParser 等が旧派生 record を参照しているため）。これは正常で、Task 4 以降で解決する。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/CardEffect.cs tests/Core.Tests/Cards/CardEffectTests.cs
git commit -m "refactor(cards): replace abstract CardEffect with single record + Normalize"
```

> ⚠ このコミットの時点でリポジトリは**ビルド不可**状態。Task 4〜11 で順次修正する。

---

## Task 4: CardEffectParser を新形式対応に書き換え

**Files:**
- Modify: `src/Core/Cards/CardEffectParser.cs`
- Test: `tests/Core.Tests/Cards/CardEffectParserTests.cs` （新規）

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/CardEffectParserTests.cs` を新規作成:

```csharp
using System;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardEffectParserTests
{
    private static CardEffect Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return CardEffectParser.ParseEffect(doc.RootElement, msg => new Exception(msg));
    }

    [Fact]
    public void Parse_attack_single_enemy()
    {
        var e = Parse("""{"action":"attack","scope":"single","side":"enemy","amount":5}""");
        Assert.Equal("attack", e.Action);
        Assert.Equal(EffectScope.Single, e.Scope);
        Assert.Equal(EffectSide.Enemy, e.Side);
        Assert.Equal(5, e.Amount);
    }

    [Fact]
    public void Parse_block_self_drops_side_via_normalize()
    {
        var e = Parse("""{"action":"block","scope":"self","amount":6}""");
        Assert.Equal(EffectScope.Self, e.Scope);
        Assert.Null(e.Side);
        Assert.Equal(6, e.Amount);
    }

    [Fact]
    public void Parse_buff_with_name()
    {
        var e = Parse("""{"action":"buff","scope":"self","name":"strength","amount":2}""");
        Assert.Equal("buff", e.Action);
        Assert.Equal("strength", e.Name);
        Assert.Equal(2, e.Amount);
    }

    [Fact]
    public void Parse_summon_with_unitId()
    {
        var e = Parse("""{"action":"summon","scope":"self","amount":0,"unitId":"wolf"}""");
        Assert.Equal("summon", e.Action);
        Assert.Equal("wolf", e.UnitId);
    }

    [Fact]
    public void Parse_attack_with_comboMin()
    {
        var e = Parse("""{"action":"attack","scope":"single","side":"enemy","amount":5,"comboMin":2}""");
        Assert.Equal(2, e.ComboMin);
    }

    [Fact]
    public void Parse_exhaustCard_with_pile()
    {
        var e = Parse("""{"action":"exhaustCard","scope":"random","pile":"hand","amount":1}""");
        Assert.Equal("exhaustCard", e.Action);
        Assert.Equal("hand", e.Pile);
    }

    [Fact]
    public void Parse_with_battleOnly_true()
    {
        var e = Parse("""{"action":"block","scope":"self","amount":5,"battleOnly":true}""");
        Assert.True(e.BattleOnly);
    }

    [Fact]
    public void Parse_attack_normalizes_side_when_ally_specified()
    {
        var e = Parse("""{"action":"attack","scope":"single","side":"ally","amount":5}""");
        Assert.Equal(EffectSide.Enemy, e.Side);
    }

    [Fact]
    public void Parse_missing_action_throws()
    {
        Assert.Throws<Exception>(() => Parse("""{"scope":"self","amount":5}"""));
    }

    [Fact]
    public void Parse_missing_scope_throws()
    {
        Assert.Throws<Exception>(() => Parse("""{"action":"attack","amount":5}"""));
    }

    [Fact]
    public void Parse_missing_amount_throws()
    {
        Assert.Throws<Exception>(() => Parse("""{"action":"attack","scope":"self"}"""));
    }

    [Fact]
    public void Parse_unknown_scope_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"action":"attack","scope":"weird","amount":5}"""));
    }

    [Fact]
    public void Parse_unknown_side_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"action":"attack","scope":"single","side":"middle","amount":5}"""));
    }
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardEffectParserTests"`
Expected: コンパイルエラー（既存 `CardEffectParser.ParseEffect` のシグネチャは同じだが、戻り値が新型 CardEffect になることを期待しており、本体実装が古いため）

- [ ] **Step 3: 最小実装**

`src/Core/Cards/CardEffectParser.cs` を全面置換:

```csharp
using System;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// カード／レリック／ポーション／敵 Move 共通で使う、新形式 CardEffect の JSON パーサー。
/// Phase 10 設計書 第 2-1, 2-2 章参照。
/// </summary>
public static class CardEffectParser
{
    /// <summary>
    /// 単一 effect オブジェクトを CardEffect に変換し、Normalize() を適用して返す。
    /// 必須フィールド (action / scope / amount) が欠落していれば makeException 経由で送出。
    /// </summary>
    public static CardEffect ParseEffect(JsonElement el, Func<string, Exception> makeException)
    {
        var action = GetRequiredString(el, "action", makeException);
        var scope = ParseScope(GetRequiredString(el, "scope", makeException), makeException);
        var amount = GetRequiredInt(el, "amount", makeException);

        EffectSide? side = null;
        if (el.TryGetProperty("side", out var sideEl) && sideEl.ValueKind == JsonValueKind.String)
            side = ParseSide(sideEl.GetString()!, makeException);

        string? name = GetOptionalString(el, "name");
        string? unitId = GetOptionalString(el, "unitId");
        int? comboMin = GetOptionalInt(el, "comboMin");
        string? pile = GetOptionalString(el, "pile");
        bool battleOnly = el.TryGetProperty("battleOnly", out var boEl)
                          && boEl.ValueKind == JsonValueKind.True;

        var raw = new CardEffect(action, scope, side, amount,
            Name: name, UnitId: unitId, ComboMin: comboMin, Pile: pile, BattleOnly: battleOnly);
        return raw.Normalize();
    }

    private static EffectScope ParseScope(string s, Func<string, Exception> makeException) => s switch
    {
        "self" => EffectScope.Self,
        "single" => EffectScope.Single,
        "random" => EffectScope.Random,
        "all" => EffectScope.All,
        _ => throw makeException($"未知の scope 値: \"{s}\"。'self'/'single'/'random'/'all' のいずれか。"),
    };

    private static EffectSide ParseSide(string s, Func<string, Exception> makeException) => s switch
    {
        "enemy" => EffectSide.Enemy,
        "ally" => EffectSide.Ally,
        _ => throw makeException($"未知の side 値: \"{s}\"。'enemy'/'ally' のいずれか。"),
    };

    private static string GetRequiredString(JsonElement el, string key, Func<string, Exception> mk)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            throw mk($"必須フィールド \"{key}\" (string) がありません。");
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, Func<string, Exception> mk)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
            throw mk($"必須フィールド \"{key}\" (number) がありません。");
        return v.GetInt32();
    }

    private static string? GetOptionalString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetOptionalInt(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}
```

- [ ] **Step 4: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardEffectParserTests"`
Expected: 13 件 PASS

> リポジトリ全体はまだビルド不可（CardJsonLoader / NonBattleRelicEffects 等で旧派生 record 参照中）。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/CardEffectParser.cs tests/Core.Tests/Cards/CardEffectParserTests.cs
git commit -m "refactor(cards): rewrite CardEffectParser for new effect format"
```

---

## Task 5: CardType に Status / Curse を追加

**Files:**
- Modify: `src/Core/Cards/CardType.cs`
- Modify: `tests/Core.Tests/Cards/CardEnumTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/CardEnumTests.cs` を編集（既存テストを残しつつ追加）:

該当ファイルの末尾に以下を追加:
```csharp
    [Fact]
    public void CardType_includes_Status_value() =>
        Assert.True(System.Enum.IsDefined(typeof(CardType), CardType.Status));

    [Fact]
    public void CardType_includes_Curse_value() =>
        Assert.True(System.Enum.IsDefined(typeof(CardType), CardType.Curse));
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardEnumTests"`
Expected: コンパイルエラー（CardType.Status / CardType.Curse 未定義）

- [ ] **Step 3: 最小実装**

`src/Core/Cards/CardType.cs`:
```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードの種別。</summary>
public enum CardType
{
    Unit,
    Attack,
    Skill,
    Power,
    Status,
    Curse,
}
```

- [ ] **Step 4: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardEnumTests"`
Expected: PASS（既存 + 追加 2 件）

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/CardType.cs tests/Core.Tests/Cards/CardEnumTests.cs
git commit -m "feat(cards): add Status and Curse to CardType enum"
```

---

## Task 6: CardDefinition に UpgradedCost / Keywords / IsUpgradable を追加

**Files:**
- Modify: `src/Core/Cards/CardDefinition.cs`
- Modify: `tests/Core.Tests/Cards/CardDefinitionTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/CardDefinitionTests.cs` の末尾に追加:

```csharp
    [Fact]
    public void IsUpgradable_false_when_neither_upgradedCost_nor_upgradedEffects()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        Assert.False(def.IsUpgradable);
    }

    [Fact]
    public void IsUpgradable_true_when_upgradedCost_only()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 2, UpgradedCost: 1,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        Assert.True(def.IsUpgradable);
    }

    [Fact]
    public void IsUpgradable_true_when_upgradedEffects_only()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: System.Array.Empty<CardEffect>(),
            Keywords: null);
        Assert.True(def.IsUpgradable);
    }

    [Fact]
    public void Keywords_default_to_null()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        Assert.Null(def.Keywords);
    }

    [Fact]
    public void Keywords_can_hold_wild()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 5, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: new[] { "wild" });
        Assert.NotNull(def.Keywords);
        Assert.Contains("wild", def.Keywords);
    }
```

> 既存の `CardDefinition` を生成しているテストすべてが**新コンストラクタシグネチャ**でコンパイルエラーになる。Step 3 で本体を修正、Step 4 直前に既存テストの引数を一括修正する。

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardDefinitionTests"`
Expected: コンパイルエラー（UpgradedCost / Keywords / IsUpgradable 未定義）

- [ ] **Step 3: 最小実装**

`src/Core/Cards/CardDefinition.cs` を全面置換:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードのマスター定義。Phase 10 設計書 第 2-3 章参照。</summary>
/// <param name="Id">一意の英数字 ID</param>
/// <param name="Name">カード名</param>
/// <param name="DisplayName">表示名（省略可、null なら Name を表示）</param>
/// <param name="Rarity">レアリティ</param>
/// <param name="CardType">カード種別</param>
/// <param name="Cost">プレイコスト。null はプレイ不可</param>
/// <param name="UpgradedCost">強化後のプレイコスト。null/省略 = Cost と同じ</param>
/// <param name="Effects">効果プリミティブ配列</param>
/// <param name="UpgradedEffects">強化時の効果配列。null/省略 = Effects と同じ</param>
/// <param name="Keywords">キーワード能力（"wild"|"superwild" 等）。null/省略 = なし</param>
public sealed record CardDefinition(
    string Id,
    string Name,
    string? DisplayName,
    CardRarity Rarity,
    CardType CardType,
    int? Cost,
    int? UpgradedCost,
    IReadOnlyList<CardEffect> Effects,
    IReadOnlyList<CardEffect>? UpgradedEffects,
    IReadOnlyList<string>? Keywords)
{
    /// <summary>
    /// UpgradedCost か UpgradedEffects のどちらかが指定されているとき強化可能。
    /// 両方とも null/省略のカードは強化対象外。
    /// </summary>
    public bool IsUpgradable => UpgradedCost is not null || UpgradedEffects is not null;
}
```

- [ ] **Step 4: 既存テストの引数を一括修正**

`tests/Core.Tests/Cards/CardDefinitionTests.cs` 内の既存テストで `new CardDefinition(...)` を書いている箇所をすべて、新しい順序（`UpgradedCost`、`Keywords` 引数追加）に修正。例:

旧:
```csharp
new CardDefinition("x","x",null,CardRarity.Common,CardType.Skill,1,Array.Empty<CardEffect>(),null);
```
新:
```csharp
new CardDefinition("x","x",null,CardRarity.Common,CardType.Skill,
    Cost:1, UpgradedCost:null,
    Effects: Array.Empty<CardEffect>(),
    UpgradedEffects: null,
    Keywords: null);
```

- [ ] **Step 5: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardDefinitionTests"`
Expected: 既存 + 追加 5 件 PASS

> リポジトリ全体はまだビルド不可（CardJsonLoader 等が旧シグネチャで生成中）。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Cards/CardDefinition.cs tests/Core.Tests/Cards/CardDefinitionTests.cs
git commit -m "feat(cards): add UpgradedCost, Keywords, IsUpgradable to CardDefinition"
```

---

## Task 7: CardJsonLoader を新フィールド・新形式 effect に対応

**Files:**
- Modify: `src/Core/Cards/CardJsonLoader.cs`
- Modify: `tests/Core.Tests/Cards/CardJsonLoaderTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/CardJsonLoaderTests.cs` の末尾に追加（既存テストの effect 形式は次タスクで一括修正する想定）:

```csharp
    [Fact]
    public void Parse_with_upgradedCost_only_sets_field()
    {
        var json = """
        {
          "id":"hb","name":"重撃","rarity":1,"cardType":"Attack",
          "cost":2,"upgradedCost":1,
          "effects":[{"action":"attack","scope":"single","side":"enemy","amount":12}]
        }""";
        var def = CardJsonLoader.Parse(json);
        Assert.Equal(2, def.Cost);
        Assert.Equal(1, def.UpgradedCost);
        Assert.Null(def.UpgradedEffects);
        Assert.True(def.IsUpgradable);
    }

    [Fact]
    public void Parse_without_upgradedCost_or_upgradedEffects_yields_non_upgradable()
    {
        var json = """
        {
          "id":"c","name":"呪い","rarity":1,"cardType":"Curse",
          "cost":null,
          "effects":[]
        }""";
        var def = CardJsonLoader.Parse(json);
        Assert.False(def.IsUpgradable);
        Assert.Null(def.UpgradedCost);
        Assert.Null(def.UpgradedEffects);
    }

    [Fact]
    public void Parse_with_keywords_array()
    {
        var json = """
        {
          "id":"w","name":"Wild Strike","rarity":2,"cardType":"Attack",
          "cost":5,
          "keywords":["wild"],
          "effects":[{"action":"attack","scope":"single","side":"enemy","amount":12}]
        }""";
        var def = CardJsonLoader.Parse(json);
        Assert.NotNull(def.Keywords);
        Assert.Contains("wild", def.Keywords);
    }

    [Fact]
    public void Parse_with_status_card_type()
    {
        var json = """
        {
          "id":"s","name":"傷","rarity":1,"cardType":"Status",
          "cost":null,
          "effects":[]
        }""";
        var def = CardJsonLoader.Parse(json);
        Assert.Equal(CardType.Status, def.CardType);
    }

    [Fact]
    public void Parse_with_curse_card_type()
    {
        var json = """
        {
          "id":"c","name":"呪い","rarity":1,"cardType":"Curse",
          "cost":null,
          "effects":[]
        }""";
        var def = CardJsonLoader.Parse(json);
        Assert.Equal(CardType.Curse, def.CardType);
    }
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardJsonLoaderTests"`
Expected: コンパイルエラー（旧 effect 形式や旧 CardDefinition シグネチャを参照中）

- [ ] **Step 3: 最小実装**

`src/Core/Cards/CardJsonLoader.cs` を全面置換:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>カード JSON のパース失敗を表す例外。</summary>
public sealed class CardJsonException : Exception
{
    public CardJsonException(string message) : base(message) { }
    public CardJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>カード JSON 文字列を CardDefinition に変換する純粋関数群。Phase 10 設計書 第 2-3 章参照。</summary>
public static class CardJsonLoader
{
    public static CardDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new CardJsonException("カード JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);
                string? displayName = root.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String
                    ? dn.GetString() : null;

                var rawRarity = GetRequiredInt(root, "rarity", id);
                if (!Enum.IsDefined(typeof(CardRarity), rawRarity))
                    throw new CardJsonException($"rarity の値 {rawRarity} は無効です (card id={id})。");
                var rarity = (CardRarity)rawRarity;

                var cardType = ParseCardType(GetRequiredString(root, "cardType", id), id);

                int? cost = root.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Number
                    ? costEl.GetInt32() : (int?)null;

                int? upgradedCost = root.TryGetProperty("upgradedCost", out var ucEl) && ucEl.ValueKind == JsonValueKind.Number
                    ? ucEl.GetInt32() : (int?)null;

                var effects = ParseEffects(root, "effects", id);

                IReadOnlyList<CardEffect>? upgraded;
                if (root.TryGetProperty("upgradedEffects", out var upgEl))
                {
                    if (upgEl.ValueKind == JsonValueKind.Array)
                        upgraded = ParseEffectsFromElement(upgEl, id);
                    else if (upgEl.ValueKind == JsonValueKind.Null)
                        upgraded = null;
                    else
                        throw new CardJsonException(
                            $"upgradedEffects must be an array or absent/null (card id={id})。");
                }
                else
                {
                    upgraded = null;
                }

                IReadOnlyList<string>? keywords = null;
                if (root.TryGetProperty("keywords", out var kwEl) && kwEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var k in kwEl.EnumerateArray())
                    {
                        if (k.ValueKind != JsonValueKind.String)
                            throw new CardJsonException($"keywords の要素は string でなければなりません (card id={id})。");
                        list.Add(k.GetString()!);
                    }
                    keywords = list;
                }

                return new CardDefinition(id, name, displayName, rarity, cardType,
                    cost, upgradedCost, effects, upgraded, keywords);
            }
            catch (CardJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(card id unknown)" : $"(card id={id})";
                throw new CardJsonException($"カード JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static IReadOnlyList<CardEffect> ParseEffects(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<CardEffect>();
        return ParseEffectsFromElement(arr, id);
    }

    private static IReadOnlyList<CardEffect> ParseEffectsFromElement(JsonElement arr, string? id)
    {
        var list = new List<CardEffect>();
        foreach (var el in arr.EnumerateArray())
            list.Add(ParseEffect(el, id));
        return list;
    }

    private static CardEffect ParseEffect(JsonElement el, string? id)
    {
        var ctx = id is null ? "" : $" (card id={id})";
        return CardEffectParser.ParseEffect(el, msg => new CardJsonException($"{msg}{ctx}"));
    }

    private static CardType ParseCardType(string s, string? id) => s switch
    {
        "Unit" => CardType.Unit,
        "Attack" => CardType.Attack,
        "Skill" => CardType.Skill,
        "Power" => CardType.Power,
        "Status" => CardType.Status,
        "Curse" => CardType.Curse,
        _ => throw new CardJsonException($"未知の cardType: {s} (card id={id})"),
    };

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (card id={id})";
            throw new CardJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (card id={id})";
            throw new CardJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }
}
```

- [ ] **Step 4: 既存 CardJsonLoaderTests の effect 形式を新形式に書き換え**

`tests/Core.Tests/Cards/CardJsonLoaderTests.cs` の旧 effect JSON（`"type":"damage","amount":6`）を、新形式（`"action":"attack","scope":"single","side":"enemy","amount":6` 等）に置き換え。具体的には文字列検索で旧形式を見つけ、対応する新形式に書き換える。
例:
```csharp
"effects":[{"type":"damage","amount":6}]
↓
"effects":[{"action":"attack","scope":"single","side":"enemy","amount":6}]

"effects":[{"type":"gainBlock","amount":5}]
↓
"effects":[{"action":"block","scope":"self","amount":5}]

"effects":[{"type":"gainMaxHp","amount":6}]   (レリック等)
↓
(該当なし、Phase 10 では削除。代替が必要なら "buff","name":"maxHp" 等。Card のテストでは出てこないはず)
```

期待される旧 effect の対応表:
- `damage` → `attack` + `scope:single` + `side:enemy`
- `gainBlock` → `block` + `scope:self`

- [ ] **Step 5: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardJsonLoaderTests"`
Expected: 既存 + 追加 5 件 PASS

> リポジトリ全体はまだビルド不可（NonBattleRelicEffects / CatalogController / その他テストが旧型参照中）。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Cards/CardJsonLoader.cs tests/Core.Tests/Cards/CardJsonLoaderTests.cs
git commit -m "refactor(cards): update CardJsonLoader for new effect format and new fields"
```

---

## Task 8: NonBattleRelicEffects を新形式に書き換え

**Files:**
- Modify: `src/Core/Relics/NonBattleRelicEffects.cs`

旧コードは派生 record（`GainMaxHpEffect`, `GainGoldEffect`, `RestHealBonusEffect`）にパターンマッチしていたが、新形式は `Action` 文字列で識別する。既存の `NonBattleRelicEffectsTests.cs` は外部から見える挙動（MaxHp 増、Gold 増、Rest 回復ボーナス）を検証しており、内部実装が変わっても**期待動作は同じ**。よってこのタスクでは新規テスト追加は不要、**既存テストが新実装で緑のままであることが緑判定**となる。

> **注意**: Phase 0 互換のため `gainMaxHp` / `gainGold` / `restHealBonus` の 3 つの action 文字列を継続採用する。これは Phase 10 設計書の effect プリミティブには含まれないが、Phase 6 までの実装（マップタイル解決時のレリック効果、Rest の回復ボーナス）を壊さないために残す。Phase 10.2 でレリック effect 体系を再整理する際に統合・改名するかは別途判断。

- [ ] **Step 1: 既存テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~NonBattleRelicEffectsTests"`
Expected: コンパイルエラー（`NonBattleRelicEffects.cs` 内の `GainMaxHpEffect` / `GainGoldEffect` / `RestHealBonusEffect` 派生 record パターンマッチが Task 3 で削除された型を参照しているため）

- [ ] **Step 2: 最小実装**

`src/Core/Relics/NonBattleRelicEffects.cs` を全面置換:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>
/// 戦闘外（マップ／休憩／取得時）でのレリック効果を適用する純粋関数群。
/// Phase 10 設計書 第 2-7 章参照。Action 文字列で効果を識別する。
/// </summary>
public static class NonBattleRelicEffects
{
    public static RunState ApplyOnPickup(RunState s, string relicId, DataCatalog catalog)
    {
        if (!catalog.TryGetRelic(relicId, out var def)) return s;
        if (def.Trigger != RelicTrigger.OnPickup) return s;
        return ApplyEffects(s, def);
    }

    public static RunState ApplyOnMapTileResolved(RunState s, DataCatalog catalog)
    {
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
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
            if (def.Trigger != RelicTrigger.Passive) continue;
            foreach (var eff in def.Effects)
                if (eff.Action == "restHealBonus") bonus += eff.Amount;
        }
        return bonus;
    }

    private static RunState ApplyEffects(RunState s, RelicDefinition def)
    {
        foreach (var eff in def.Effects)
        {
            s = eff.Action switch
            {
                "gainMaxHp" => s with { MaxHp = s.MaxHp + eff.Amount, CurrentHp = s.CurrentHp + eff.Amount },
                "gainGold"  => s with { Gold = s.Gold + eff.Amount },
                _           => s,
            };
        }
        return s;
    }
}
```

- [ ] **Step 3: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~NonBattleRelicEffectsTests"`
Expected: 既存 5 件すべて PASS

> ⚠ ただしレリック JSON データはまだ旧形式 (`"type":"gainMaxHp"` 等) で書かれているため、`EmbeddedDataLoader.LoadCatalog()` 段階でエラーになる可能性が高い。レリック JSON / ポーション JSON 等の書き換えは Phase 10.1.C の責務だが、Phase 10.1.A の Task 11 でカード JSON だけ書き換えても、レリック / ポーション JSON が旧形式のままだとカタログロードがコケる。

> **暫定対応**: Phase 10.1.A の範囲では、レリック JSON / ポーション JSON の `"type":"gainMaxHp"` などを `"action":"gainMaxHp","scope":"self"` 形式に最小限書き換える。これは Phase 10.1.C で本格的に整理するが、ビルド・テストを通すための前倒し作業として Phase 10.1.A に含める（Task 10.5 を新設）。

- [ ] **Step 4: コミット**

```bash
git add src/Core/Relics/NonBattleRelicEffects.cs
git commit -m "refactor(relics): identify effects by Action string in NonBattleRelicEffects"
```

---

## Task 9: CatalogController の派生 record 参照を新形式に書き換え

**Files:**
- Modify: `src/Server/Controllers/CatalogController.cs`

- [ ] **Step 1: ファイルを確認**

Run: `grep -n "DamageEffect\|GainBlockEffect\|GainMaxHpEffect\|GainGoldEffect\|RestHealBonusEffect\|UnknownEffect" src/Server/Controllers/CatalogController.cs`

参照箇所を特定し、それらを新形式（`eff.Action == "..."` パターン）に置換する。

- [ ] **Step 2: 該当箇所を新形式に書き換え**

例（実際のコードに合わせて適宜置換）:
旧:
```csharp
return def.Effects.Select(e => e switch
{
    DamageEffect d => new EffectDto("damage", d.Amount),
    GainBlockEffect g => new EffectDto("gainBlock", g.Amount),
    _ => new EffectDto("unknown", 0)
});
```
新:
```csharp
return def.Effects.Select(e => new EffectDto(e.Action, e.Amount));
```

- [ ] **Step 3: ビルド確認**

Run: `dotnet build src/Server`
Expected: 成功

- [ ] **Step 4: 既存 Server.Tests を走らせる**

Run: `dotnet test tests/Server.Tests`
Expected: 既存テスト緑（CatalogController のテストがあれば、新形式で値が一致することを確認）

- [ ] **Step 5: コミット**

```bash
git add src/Server/Controllers/CatalogController.cs
git commit -m "refactor(server): use Action string for CatalogController effect mapping"
```

---

## Task 10: その他テストファイル（Fixtures / EmbeddedDataLoaderTests / EventJsonLoaderTests / RelicJsonLoaderTests / RelicDefinitionTests / PotionJsonLoaderTests）の effect JSON を新形式に書き換え

**Files:**
- Modify: `tests/Core.Tests/Fixtures/JsonFixtures.cs`
- Modify: `tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs`
- Modify: `tests/Core.Tests/Events/EventJsonLoaderTests.cs`
- Modify: `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs`
- Modify: `tests/Core.Tests/Relics/RelicDefinitionTests.cs`
- Modify: `tests/Core.Tests/Potions/PotionJsonLoaderTests.cs`

このタスクはテキスト検索＆置換中心。各ファイルを開き、旧形式 effect JSON を新形式に置き換える。

- [ ] **Step 1: 各ファイルでパターン検索**

Run:
```
grep -n '"type":"damage"\|"type":"gainBlock"\|"type":"gainMaxHp"\|"type":"gainGold"\|"type":"restHealBonus"' tests/Core.Tests
```

- [ ] **Step 2: 旧 effect record（`new DamageEffect(...)` 等）の C# 使用箇所も検索**

Run:
```
grep -n 'new DamageEffect\|new GainBlockEffect\|new GainMaxHpEffect\|new GainGoldEffect\|new RestHealBonusEffect\|new UnknownEffect' tests/Core.Tests
```

- [ ] **Step 3: 置換マッピングに従ってすべて新形式に書き換える**

| 旧 (JSON) | 新 (JSON) |
|---|---|
| `{"type":"damage","amount":N}` | `{"action":"attack","scope":"single","side":"enemy","amount":N}` |
| `{"type":"gainBlock","amount":N}` | `{"action":"block","scope":"self","amount":N}` |
| `{"type":"gainMaxHp","amount":N}` | `{"action":"gainMaxHp","scope":"self","amount":N}` |
| `{"type":"gainGold","amount":N}` | `{"action":"gainGold","scope":"self","amount":N}` |
| `{"type":"restHealBonus","amount":N}` | `{"action":"restHealBonus","scope":"self","amount":N}` |

| 旧 (C#) | 新 (C#) |
|---|---|
| `new DamageEffect(N)` | `new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, N)` |
| `new GainBlockEffect(N)` | `new CardEffect("block", EffectScope.Self, null, N)` |
| `new GainMaxHpEffect(N)` | `new CardEffect("gainMaxHp", EffectScope.Self, null, N)` |
| `new GainGoldEffect(N)` | `new CardEffect("gainGold", EffectScope.Self, null, N)` |
| `new RestHealBonusEffect(N)` | `new CardEffect("restHealBonus", EffectScope.Self, null, N)` |

旧 `CardDefinition` コンストラクタ呼び出しもすべて新シグネチャ（`UpgradedCost: null, Keywords: null` を追加）に書き換える。

- [ ] **Step 4: ビルド確認**

Run: `dotnet build`
Expected: 警告のみ、エラーなし。もし旧型参照が残っていればここで露出する。

- [ ] **Step 5: テスト全実行**

Run: `dotnet test`
Expected: 全テスト緑（カード JSON ファイルはまだ旧形式だが、テストはモック JSON を使うので問題ないはず。`EmbeddedDataLoaderTests` だけは実データを読み込むため失敗する可能性あり → 次タスクで対応）

- [ ] **Step 6: コミット**

```bash
git add tests/
git commit -m "refactor(tests): migrate test fixtures to new CardEffect format"
```

---

## Task 10.5: レリック・ポーション JSON の effect 形式を最小限新形式に書き換え

**Files:**
- Modify: `src/Core/Data/Relics/*.json` (effect の `"type"` フィールドを持つもの全て)
- Modify: `src/Core/Data/Potions/*.json` (effect の `"type"` フィールドを持つもの全て)

`EmbeddedDataLoader.LoadCatalog()` がレリック / ポーション JSON も読み込むため、effect を旧形式 (`"type"` ベース) のまま放置すると、カード以外の JSON でロードに失敗する。Phase 10.1.A の範囲で**最小限**変換する（`scope` などのフィールドは追加するが、レリック・ポーションの構造自体（Trigger/Implemented フラグ等）は Phase 10.1.C で本格的に整理する）。

- [ ] **Step 1: 旧形式の JSON ファイル一覧を確認**

Run:
```
grep -l '"type"' src/Core/Data/Relics/*.json src/Core/Data/Potions/*.json
```
ヒットしたファイル一覧を確認する。

- [ ] **Step 2: 各ファイルの effect 配列を Task 10 の置換マッピングに従って書き換え**

| 旧 | 新 |
|---|---|
| `{"type":"damage","amount":N}` | `{"action":"attack","scope":"single","side":"enemy","amount":N}` |
| `{"type":"gainBlock","amount":N}` | `{"action":"block","scope":"self","amount":N}` |
| `{"type":"gainMaxHp","amount":N}` | `{"action":"gainMaxHp","scope":"self","amount":N}` |
| `{"type":"gainGold","amount":N}` | `{"action":"gainGold","scope":"self","amount":N}` |
| `{"type":"restHealBonus","amount":N}` | `{"action":"restHealBonus","scope":"self","amount":N}` |

> もし上記マッピングに該当しない `"type"` 値があれば、`"action"` フィールドに同じ値を入れて `"scope":"self"` を追加（暫定対応）。Phase 10.1.C で正式整理。

- [ ] **Step 3: ビルド + テスト**

Run: `dotnet build && dotnet test`
Expected: 全テスト緑（Catalog ロードが成功し、レリック・ポーション JSON ローダーも新形式で動く）

- [ ] **Step 4: コミット**

```bash
git add src/Core/Data/Relics/ src/Core/Data/Potions/
git commit -m "data(relics,potions): minimal migration of effect JSON to new format"
```

---

## Task 11: カード JSON 32 ファイルを新形式に一括変換

**Files:**
- Modify: `src/Core/Data/Cards/*.json` (32 ファイル)

旧形式:
```json
{
  "id": "strike",
  "name": "ストライク",
  "rarity": 1,
  "cardType": "Attack",
  "cost": 1,
  "effects": [{ "type": "damage", "amount": 6 }],
  "upgradedEffects": [{ "type": "damage", "amount": 9 }]
}
```

新形式:
```json
{
  "id": "strike",
  "name": "ストライク",
  "rarity": 1,
  "cardType": "Attack",
  "cost": 1,
  "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 6 }],
  "upgradedEffects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 9 }]
}
```

- [ ] **Step 1: 各カードの効果を確認し、新形式の effect 列を決定**

Run:
```
grep -l '"type"' src/Core/Data/Cards/*.json
```
で、変換対象ファイル一覧を確認。

- [ ] **Step 2: 一括変換スクリプトまたは手動編集**

各 JSON ファイルで以下の変換を行う:

| 旧 | 新 |
|---|---|
| `"type":"damage"` → 単体攻撃カード | `"action":"attack","scope":"single","side":"enemy"` |
| `"type":"gainBlock"` → ブロック獲得 | `"action":"block","scope":"self"` |

`amount` フィールドはそのまま維持。

> **注**: Phase 0 で AI 生成された 32 枚のカードはすべて Strike / Defend のバリアント（damage / gainBlock のみ）。effects が複数ある場合や、単体以外の attack（all / random）はこの段階のデータには出現しない。

- [ ] **Step 3: 全ファイルをビルド・テスト確認**

Run: `dotnet build && dotnet test`
Expected: 全テスト緑（特に `EmbeddedDataLoaderTests` で実データ読み込みが成功）

- [ ] **Step 4: コミット**

```bash
git add src/Core/Data/Cards/
git commit -m "data(cards): migrate 32 card JSONs to new CardEffect format"
```

---

## Task 12: CardUpgrade.cs の IsUpgradable 連携を確認・更新

**Files:**
- Modify: `src/Core/Cards/CardUpgrade.cs`
- Modify: `tests/Core.Tests/Cards/CardUpgradeTests.cs`

既存実装は `def.UpgradedEffects is not null` で判定していたが、新たに `IsUpgradable` プロパティが導入されたためそちらに切り替える。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/CardUpgradeTests.cs` の末尾に追加:

```csharp
    [Fact]
    public void CanUpgrade_returns_true_when_only_UpgradedCost_is_set()
    {
        var def = new CardDefinition(
            "x","x",null,CardRarity.Common,CardType.Skill,
            Cost: 2, UpgradedCost: 1,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        var catalog = TestHelpers.DataCatalogBuilder.With(def);  // 既存ヘルパに合わせる
        var inst = new CardInstance("x");
        Assert.True(CardUpgrade.CanUpgrade(inst, catalog));
    }
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardUpgradeTests"`
Expected: FAIL（旧実装は `UpgradedEffects is not null` のみチェック、`UpgradedCost` のみのケースで false を返す）

- [ ] **Step 3: 最小実装**

`src/Core/Cards/CardUpgrade.cs` を以下に修正:

```csharp
using System;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Cards;

public static class CardUpgrade
{
    public static bool CanUpgrade(CardInstance ci, DataCatalog catalog)
    {
        if (ci.Upgraded) return false;
        if (!catalog.TryGetCard(ci.Id, out var def)) return false;
        return def.IsUpgradable;
    }

    public static CardInstance Upgrade(CardInstance ci)
    {
        if (ci.Upgraded) throw new InvalidOperationException($"Card {ci.Id} already upgraded");
        return ci with { Upgraded = true };
    }
}
```

- [ ] **Step 4: テストを走らせて緑確認**

Run: `dotnet test tests/Core.Tests --filter "FullyQualifiedName~CardUpgradeTests"`
Expected: 既存 + 追加 1 件 PASS

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/CardUpgrade.cs tests/Core.Tests/Cards/CardUpgradeTests.cs
git commit -m "refactor(cards): use IsUpgradable property in CardUpgrade.CanUpgrade"
```

---

## Task 13: 全テスト緑確認 + Phase 5 placeholder 動作確認 + タグ付け

**Files:** なし（最終確認のみ）

- [ ] **Step 1: 全プロジェクトビルド**

Run: `dotnet build`
Expected: 警告 0、エラー 0

- [ ] **Step 2: 全テスト実行**

Run: `dotnet test`
Expected: 全テスト緑（Phase 10.1.A 開始前と同数 + 本 plan で追加したテスト分の増加）

- [ ] **Step 3: 旧派生 record の grep 確認**

Run:
```
grep -r "DamageEffect\|GainBlockEffect\|GainMaxHpEffect\|GainGoldEffect\|RestHealBonusEffect\|UnknownEffect" src tests --include="*.cs"
```
Expected: マッチなし（クラス名としての出現が 0 件）

- [ ] **Step 4: Phase 5 placeholder 動作確認（手動）**

```
dotnet run --project src/Server
```
別ターミナルで:
```
cd src/Client && npm run dev
```
ブラウザで:
1. ログイン
2. 新規ラン開始
3. マップで敵マスを選択
4. 戦闘画面が出て即勝利
5. 報酬画面でカード選択
6. マップに戻る

Expected: 上記すべてが Phase 10.1.A 着手前と同じ挙動で動く

- [ ] **Step 5: タグ付け + commit**

```bash
git tag phase10-1A-complete
git push origin master
git push origin phase10-1A-complete
```

完了。次は Phase 10.1.B（MoveDefinition 統一 + CombatActorDefinition）の brainstorming/writing-plans サイクル。

---

## 完了判定チェックリスト

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全テスト緑
- [ ] 旧派生 record の grep が 0 件
- [ ] カード JSON 32 ファイルが新形式
- [ ] Phase 5 placeholder バトルが手動で動作確認済み
- [ ] `phase10-1A-complete` タグが切られて origin に push 済み

---

## 補足: Phase 10.1.A で**変更しない**もの

- `MoveDefinition` の構造（Phase 10.1.B で書き換え）
- `EnemyDefinition` の構造（Phase 10.1.B で `CombatActorDefinition` 派生に）
- `PotionDefinition` / `RelicDefinition` の構造（Phase 10.1.C で拡張）
- 敵 / ポーション / レリック JSON データ（Phase 10.1.B / 10.1.C で書き換え）
- Phase 5 `BattlePlaceholder.cs`（Phase 10 完了時に削除予定）
- Server `BattleHub.cs`（未存在、Phase 10.3 で新設）
- Client `BattleScreen.tsx`（未存在、Phase 10.4 で新設）

---

## 参照

- Phase 10 設計書: [`2026-04-25-phase10-battle-system-design.md`](../specs/2026-04-25-phase10-battle-system-design.md)
- ロードマップ: [`2026-04-20-roadmap.md`](2026-04-20-roadmap.md)
