# Phase 10.5.A — CardTextFormatter (Core 自動文章化) 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** カード `effects` 配列から description を生成する純関数 `CardTextFormatter` を Core に追加。CardDefinition に optional な `Description` / `UpgradedDescription` (override) を持たせ、override が空なら formatter を使う仕組みを Server DTO 経由でゲーム本体・図鑑・報酬・休憩プレビューに反映する。

**Architecture:** `Core/Cards/CardTextFormatter` (純関数) → `CardDefinition` に override フィールド追加 → `CardJsonLoader` で optional パース → Server `CatalogController.GetCards` が override > formatter で description を確定 → 既存 DTO を経由して Client は変更なしに新文言が表示される。

**Tech Stack:** C# .NET 10 + System.Text.Json (Core / Server), xUnit (テスト).

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md`（特に §2 CardTextFormatter）

**Sub-phase scope:** カード description のみ。Potion / Relic / Enemy move / Unit の自動文章化は **本フェーズ対象外**（Phase 10.5.F に移譲）。本フェーズでは **versioned JSON / dev tool / override 層**にも触れない（10.5.B 以降）。

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Cards/CardTextFormatter.cs` | Create | effects + upgraded → description 文字列を返す純関数 |
| `src/Core/Cards/CardDefinition.cs` | Modify | `Description` / `UpgradedDescription` を末尾 optional に追加 |
| `src/Core/Cards/CardJsonLoader.cs` | Modify | `description` / `upgradedDescription` キーを optional パース |
| `tests/Core.Tests/Cards/CardTextFormatterTests.cs` | Create | 各 effect プリミティブ × scope の単体テスト + 連結テスト |
| `tests/Core.Tests/Cards/CardJsonLoaderTests.cs` | Modify | description override パースのテストケース追加 |
| `src/Server/Controllers/CatalogController.cs` | Modify | `DescribeEffects` を削除し、Core formatter + override フォールバックに置換 |
| `tests/Server.Tests/CatalogControllerTests.cs` | Modify | 既存カードの description 期待値を formatter 出力で更新 + override テスト追加 |

---

## Conventions

- **TDD strictly:** テストを書く → fail を確認 → 実装 → green → 次タスク。
- **Build clean:** 各タスク完了時 `dotnet build` 警告 0 / エラー 0。
- **Existing tests stay green:** `dotnet test` 既存 1007 件は常に緑。formatter 切替で description が変わるテスト (`CatalogControllerTests`) は **期待値を更新** することで対応 (削除はしない)。
- **JP literals only:** formatter は当面 JP 固定。テンプレート文字列は `CardTextFormatter` 内 private const として定義し、将来 i18n 切替の準備として独立した内部関数を残す。
- **Pure function:** `CardTextFormatter` は static class。state なし、I/O なし、ロケール引数なし。

---

## Task 1: CardDefinition に override フィールドを追加

**Files:**
- Modify: `src/Core/Cards/CardDefinition.cs`

**目的:** JSON `description` / `upgradedDescription` が指定されたとき、formatter よりそれを優先する仕組みの土台。

### Step 1.1: record 末尾に optional フィールド追加

- [ ] `src/Core/Cards/CardDefinition.cs` の record 引数末尾に追加:

```csharp
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
    IReadOnlyList<string>? Keywords,
    IReadOnlyList<string>? UpgradedKeywords = null,
    string? Description = null,
    string? UpgradedDescription = null)
```

XML doc コメントに以下を追加:
```csharp
/// <param name="Description">手書き description override (任意)。null/空文字なら formatter で自動生成。</param>
/// <param name="UpgradedDescription">強化版 description override (任意)。null/空文字なら formatter で自動生成。</param>
```

### Step 1.2: ビルド確認

- [ ] `dotnet build` がエラー 0 / 警告 0 で通る (既存呼出全て後方互換)。

---

## Task 2: CardJsonLoader を description override パース対応にする

**Files:**
- Modify: `src/Core/Cards/CardJsonLoader.cs`
- Modify: `tests/Core.Tests/Cards/CardJsonLoaderTests.cs`

**TDD:** 失敗テスト → 実装 → green の順で。

### Step 2.1: テストを先に追加

- [ ] `CardJsonLoaderTests.cs` に以下のテストケースを追加:

```csharp
[Fact]
public void Loads_card_with_description_override()
{
    var json = """
    {
      "id": "test",
      "name": "テスト",
      "rarity": 0,
      "cardType": "Attack",
      "cost": 1,
      "effects": [{"action":"attack","scope":"single","side":"enemy","amount":6}],
      "description": "手書きの説明文。",
      "upgradedDescription": "強化版の説明文。"
    }
    """;
    var def = CardJsonLoader.LoadFromJson(json);
    Assert.Equal("手書きの説明文。", def.Description);
    Assert.Equal("強化版の説明文。", def.UpgradedDescription);
}

[Fact]
public void Loads_card_without_description_keys_yields_null_descriptions()
{
    var json = """
    {
      "id": "test",
      "name": "テスト",
      "rarity": 0,
      "cardType": "Attack",
      "cost": 1,
      "effects": [{"action":"attack","scope":"single","side":"enemy","amount":6}]
    }
    """;
    var def = CardJsonLoader.LoadFromJson(json);
    Assert.Null(def.Description);
    Assert.Null(def.UpgradedDescription);
}
```

- [ ] `dotnet test --filter FullyQualifiedName~CardJsonLoaderTests` で **新テストが fail** することを確認。

### Step 2.2: ローダー実装

- [ ] `src/Core/Cards/CardJsonLoader.cs` で以下を追加:
  - `description` / `upgradedDescription` キーを optional に読む（既存 `displayName` と同じパターン）
  - 値が空文字なら `null` に正規化

```csharp
string? description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
    ? (string.IsNullOrEmpty(d.GetString()) ? null : d.GetString())
    : null;
string? upgradedDescription = root.TryGetProperty("upgradedDescription", out var ud) && ud.ValueKind == JsonValueKind.String
    ? (string.IsNullOrEmpty(ud.GetString()) ? null : ud.GetString())
    : null;
```

CardDefinition 生成時にこれらを末尾引数として渡す。

- [ ] `dotnet test --filter FullyQualifiedName~CardJsonLoaderTests` で全テスト緑。

---

## Task 3: CardTextFormatter を実装する

**Files:**
- Create: `src/Core/Cards/CardTextFormatter.cs`
- Create: `tests/Core.Tests/Cards/CardTextFormatterTests.cs`

**TDD:** 各 effect プリミティブごとにテスト → 実装 を反復。

### Step 3.1: テストを先に書く（attack 系）

- [ ] `CardTextFormatterTests.cs` を新規作成、以下のテストを追加:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardTextFormatterTests
{
    private static CardEffect E(string action, EffectScope scope, EffectSide? side, int amount, string? name = null)
        => new(action, scope, side, amount, name);

    [Fact]
    public void Attack_single_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) });
        Assert.Equal("敵 1 体に 6 ダメージ。", s);
    }

    [Fact]
    public void Attack_random_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.Random, EffectSide.Enemy, 5) });
        Assert.Equal("敵ランダム 1 体に 5 ダメージ。", s);
    }

    [Fact]
    public void Attack_all_enemies()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.All, EffectSide.Enemy, 8) });
        Assert.Equal("敵全体に 8 ダメージ。", s);
    }

    [Fact]
    public void Attack_repeated_collapses_to_x_n()
    {
        var s = CardTextFormatter.FormatEffects(new[] {
            E("attack", EffectScope.Single, EffectSide.Enemy, 5),
            E("attack", EffectScope.Single, EffectSide.Enemy, 5),
            E("attack", EffectScope.Single, EffectSide.Enemy, 5),
        });
        Assert.Equal("敵 1 体に 5 ダメージ × 3 回。", s);
    }
}
```

- [ ] `dotnet test --filter FullyQualifiedName~CardTextFormatterTests` で **コンパイルエラー** (CardTextFormatter 未存在)。

### Step 3.2: スケルトン実装

- [ ] `src/Core/Cards/CardTextFormatter.cs` を以下で作成:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// CardEffect 配列から日本語 description を生成する純関数。
/// JSON の description override が空のときに利用される。
/// 関連 spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §2.
/// </summary>
public static class CardTextFormatter
{
    /// <summary>
    /// CardDefinition と upgraded フラグから表示テキストを生成する。
    /// override (Description/UpgradedDescription) が非空ならそれを返し、
    /// 空ならその時点の effects 配列から自動生成する。
    /// </summary>
    public static string Format(CardDefinition def, bool upgraded)
    {
        string? manual = upgraded ? def.UpgradedDescription : def.Description;
        if (!string.IsNullOrWhiteSpace(manual)) return manual!;

        var effects = upgraded && def.UpgradedEffects is not null
            ? def.UpgradedEffects
            : def.Effects;
        return FormatEffects(effects);
    }

    /// <summary>
    /// effects 配列単体から description 文字列を組み立てる。テスト・dev tool プレビュー用。
    /// </summary>
    public static string FormatEffects(IReadOnlyList<CardEffect> effects)
    {
        if (effects.Count == 0) return string.Empty;

        // 連続する同一 (action, scope, side, amount, name) を 1 個にまとめて " × N 回" 表記。
        var grouped = GroupConsecutive(effects);
        var sentences = grouped.Select(g => DescribeGroup(g.Effect, g.Count));
        return string.Join("\n", sentences);
    }

    private record EffectGroup(CardEffect Effect, int Count);

    private static IEnumerable<EffectGroup> GroupConsecutive(IReadOnlyList<CardEffect> effects)
    {
        // 連続して同 spec が並ぶ部分のみ畳む（discontiguous は別グループ）
        var result = new List<EffectGroup>();
        foreach (var e in effects)
        {
            if (result.Count > 0 && IsSameSpec(result[^1].Effect, e))
            {
                result[^1] = new EffectGroup(result[^1].Effect, result[^1].Count + 1);
            }
            else
            {
                result.Add(new EffectGroup(e, 1));
            }
        }
        return result;
    }

    private static bool IsSameSpec(CardEffect a, CardEffect b)
        => a.Action == b.Action
        && a.Scope == b.Scope
        && a.Side == b.Side
        && a.Amount == b.Amount
        && a.Name == b.Name
        && a.UnitId == b.UnitId;

    private static string DescribeGroup(CardEffect e, int count)
    {
        var head = DescribeOne(e);
        if (count <= 1) return head + "。";
        // 末尾「。」の前に " × N 回" を挿入: "X ダメージ" → "X ダメージ × 3 回。"
        return head + " × " + count + " 回。";
    }

    private static string DescribeOne(CardEffect e) => e.Action switch
    {
        "attack" => DescribeAttack(e),
        "block" => DescribeBlock(e),
        "draw" => $"カードを {e.Amount} 枚引く",
        "discard" => $"手札 {e.Amount} 枚を捨てる",
        "buff" => DescribeStatusChange(e, isDebuff: false),
        "debuff" => DescribeStatusChange(e, isDebuff: true),
        "heal" => DescribeHeal(e),
        "summon" => $"{e.UnitId ?? "ユニット"} を召喚",
        "exhaustCard" => $"手札 {e.Amount} 枚を除外",
        "exhaustSelf" => "このカードを除外",
        "retainSelf" => "このカードを次ターンに持ち越す",
        "gainEnergy" => $"エナジー +{e.Amount}",
        "upgrade" => $"カード {e.Amount} 枚を強化",
        _ => $"(未対応 action: {e.Action})",
    };

    private static string DescribeAttack(CardEffect e) => e.Scope switch
    {
        EffectScope.Single => $"敵 1 体に {e.Amount} ダメージ",
        EffectScope.Random => $"敵ランダム 1 体に {e.Amount} ダメージ",
        EffectScope.All => $"敵全体に {e.Amount} ダメージ",
        _ => $"敵に {e.Amount} ダメージ",
    };

    private static string DescribeBlock(CardEffect e) => (e.Scope, e.Side) switch
    {
        (EffectScope.Self, _) => $"ブロック {e.Amount} を得る",
        (EffectScope.Single, EffectSide.Ally) => $"味方 1 体にブロック {e.Amount}",
        (EffectScope.All, EffectSide.Ally) => $"味方全体にブロック {e.Amount}",
        _ => $"ブロック {e.Amount}",
    };

    private static string DescribeHeal(CardEffect e) => (e.Scope, e.Side) switch
    {
        (EffectScope.Self, _) => $"HP を {e.Amount} 回復",
        (EffectScope.Single, EffectSide.Ally) => $"味方 1 体の HP を {e.Amount} 回復",
        (EffectScope.All, EffectSide.Ally) => $"味方全体の HP を {e.Amount} 回復",
        _ => $"HP を {e.Amount} 回復",
    };

    private static string DescribeStatusChange(CardEffect e, bool isDebuff)
    {
        var jpName = JpStatusName(e.Name);
        var target = (e.Scope, e.Side, isDebuff) switch
        {
            (EffectScope.Self, _, _) => "自身",
            (EffectScope.Single, EffectSide.Enemy, _) => "敵 1 体",
            (EffectScope.Single, EffectSide.Ally, _) => "味方 1 体",
            (EffectScope.All, EffectSide.Enemy, _) => "敵全体",
            (EffectScope.All, EffectSide.Ally, _) => "味方全体",
            (EffectScope.Random, EffectSide.Enemy, _) => "敵ランダム 1 体",
            (EffectScope.Random, EffectSide.Ally, _) => "味方ランダム 1 体",
            _ => "対象",
        };
        return $"{target}に {jpName} {e.Amount}";
    }

    private static string JpStatusName(string? id) => id switch
    {
        "weak" => "脱力",
        "vulnerable" => "脆弱",
        "strength" => "筋力",
        "dexterity" => "敏捷",
        "poison" => "毒",
        "omnistrike" => "全体攻撃",
        null or "" => "ステータス",
        _ => id,
    };
}
```

- [ ] `dotnet test --filter FullyQualifiedName~CardTextFormatterTests` で attack 系 4 テストが緑。

### Step 3.3: 残りのプリミティブをテスト追加して実装

block / draw / discard / buff / debuff / heal / summon / exhaustCard / exhaustSelf / retainSelf / gainEnergy / upgrade それぞれ:

- [ ] テストを 1 つ書く (representative case)
- [ ] スケルトン実装で通れば次へ、通らなければ実装を補正

例 (block):
```csharp
[Fact]
public void Block_self()
{
    var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.Self, null, 5) });
    Assert.Equal("ブロック 5 を得る。", s);
}

[Fact]
public void Block_ally_all()
{
    var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.All, EffectSide.Ally, 4) });
    Assert.Equal("味方全体にブロック 4。", s);
}
```

例 (debuff with status name):
```csharp
[Fact]
public void Debuff_weak_single_enemy()
{
    var s = CardTextFormatter.FormatEffects(new[] { E("debuff", EffectScope.Single, EffectSide.Enemy, 1, "weak") });
    Assert.Equal("敵 1 体に 脱力 1。", s);
}
```

例 (buff self):
```csharp
[Fact]
public void Buff_self_strength()
{
    var s = CardTextFormatter.FormatEffects(new[] { E("buff", EffectScope.Self, null, 1, "strength") });
    Assert.Equal("自身に 筋力 1。", s);
}
```

各テストを書いた後 `dotnet test` で都度確認。

### Step 3.4: 連結ルールのテスト

- [ ] 複数異なる effects が `\n` で連結されることを検証:

```csharp
[Fact]
public void Multiple_distinct_effects_join_with_newline()
{
    var s = CardTextFormatter.FormatEffects(new[] {
        E("attack", EffectScope.Single, EffectSide.Enemy, 6),
        E("block", EffectScope.Self, null, 3),
    });
    Assert.Equal("敵 1 体に 6 ダメージ。\nブロック 3 を得る。", s);
}
```

### Step 3.5: Format(def, upgraded) override 優先のテスト

- [ ] override が指定されていれば formatter は呼ばれず override を返す:

```csharp
[Fact]
public void Format_uses_override_when_description_set()
{
    var def = new CardDefinition(
        Id: "x", Name: "x", DisplayName: null,
        Rarity: CardRarity.Common, CardType: CardType.Attack,
        Cost: 1, UpgradedCost: null,
        Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) },
        UpgradedEffects: null, Keywords: null, UpgradedKeywords: null,
        Description: "手書き", UpgradedDescription: null);
    Assert.Equal("手書き", CardTextFormatter.Format(def, upgraded: false));
}

[Fact]
public void Format_falls_back_to_effects_when_override_null()
{
    var def = new CardDefinition(
        Id: "x", Name: "x", DisplayName: null,
        Rarity: CardRarity.Common, CardType: CardType.Attack,
        Cost: 1, UpgradedCost: null,
        Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) },
        UpgradedEffects: null, Keywords: null, UpgradedKeywords: null,
        Description: null, UpgradedDescription: null);
    Assert.Equal("敵 1 体に 6 ダメージ。", CardTextFormatter.Format(def, upgraded: false));
}

[Fact]
public void Format_upgraded_uses_upgraded_effects()
{
    var def = new CardDefinition(
        Id: "x", Name: "x", DisplayName: null,
        Rarity: CardRarity.Common, CardType: CardType.Attack,
        Cost: 1, UpgradedCost: null,
        Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) },
        UpgradedEffects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 9) },
        Keywords: null, UpgradedKeywords: null,
        Description: null, UpgradedDescription: null);
    Assert.Equal("敵 1 体に 9 ダメージ。", CardTextFormatter.Format(def, upgraded: true));
}
```

- [ ] `dotnet test --filter FullyQualifiedName~CardTextFormatterTests` 全緑。

---

## Task 4: CatalogController.GetCards を formatter 経由に切替

**Files:**
- Modify: `src/Server/Controllers/CatalogController.cs`
- Modify: `tests/Server.Tests/CatalogControllerTests.cs`

### Step 4.1: 既存テストの期待値を formatter 出力に更新

- [ ] `tests/Server.Tests/CatalogControllerTests.cs` を読み、`/catalog/cards` の description 期待値を含むテストを特定。
- [ ] 該当テストの期待文字列を新 formatter 出力に書き換え（例: `"6 ダメージ"` → `"敵 1 体に 6 ダメージ。"`）。
- [ ] `dotnet test --filter FullyQualifiedName~CatalogControllerTests` で **fail** を確認 (まだ Server 側は古い DescribeEffects)。

### Step 4.2: GetCards を formatter 呼出に置換

- [ ] `CatalogController.cs` の `GetCards`:
  - 旧: `DescribeEffects(def.Effects)` / `DescribeEffects(def.UpgradedEffects)`
  - 新: `CardTextFormatter.Format(def, upgraded: false)` / `def.IsUpgradable ? CardTextFormatter.Format(def, upgraded: true) : null`

```csharp
result[id] = new CardCatalogEntryDto(
    def.Id,
    def.Name,
    def.DisplayName,
    (int)def.Rarity,
    def.CardType.ToString(),
    def.Cost,
    def.UpgradedCost,
    def.IsUpgradable,
    CardTextFormatter.Format(def, upgraded: false),
    def.IsUpgradable ? CardTextFormatter.Format(def, upgraded: true) : null);
```

### Step 4.3: 旧 DescribeEffects / CardEffectLabel を削除 or 再利用判定

- [ ] potions (`DescribePotionEffects`) はまだ古い DescribeEffects に依存。**スコープ判断**:
  - **A 案 (推奨):** Potion 描写も formatter 経由にしてしまう (potion はカードと同じ `CardEffect` プリミティブを使うため formatter 互換)
    - `DescribePotionEffects` を「prefix + `CardTextFormatter.FormatEffects(def.Effects)`」に書換
    - 既存 potion description テスト期待値も更新が必要
  - **B 案:** 旧 DescribeEffects を残して potion はそのまま。formatter は cards のみ。
    - 技術的負債だが本フェーズスコープを最小化
- [ ] **デフォルトは A 案。** Potion は effect プリミティブが card と同じなので 1 関数で扱うのが筋。Potion テストの期待値更新は同 commit に含める。
- [ ] `CardEffectLabel` は formatter に置換完了したので削除。
- [ ] `DescribeEffects` (private) は formatter 移行で不要になるので削除。

### Step 4.4: ビルド + 全 Server テスト

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` で全テスト緑

---

## Task 5: 全カード手動スモーク確認 (任意、高優先度)

**Files:**
- Modify (only if needed): `src/Core/Data/Cards/*.json` (description override 追加)

### Step 5.1: 一覧出力スクリプトでざっと眺める

- [ ] 開発者が手元で `dotnet run --project src/Server` → ブラウザで `/api/v1/catalog/cards` を叩き、全 35 カードの description を目視確認。
- [ ] 「明らかに違和感」「formatter テンプレで対応すべき細かい違い」「カード固有のフレーバー文を残したい」を分類。
- [ ] フレーバー残したい例外カードのみ JSON に `description` / `upgradedDescription` を追加 (override)。

### Step 5.2: テンプレートが汎用過ぎる場合の調整

- [ ] 違和感が複数カードにわたる場合は `CardTextFormatter` のテンプレート修正を検討。
- [ ] テンプレート修正は新規テストを追加して再 TDD ループを回す。

**注意:** 本タスクは **任意**。本フェーズ完了判定には含めない（spec §8-2 の「差異の数だけ判断ポイント」を 10.5.B 開始前に再評価する）。

---

## Task 6: Self-review + push

### 1. Spec coverage チェック

- [ ] §2.1 API: `Format(def, upgraded)` / `FormatEffects(effects)` の 2 個 export ✓
- [ ] §2.2 テンプレート: 主要 action (attack/block/draw/discard/buff/debuff/heal/summon/exhaustCard/exhaustSelf/retainSelf/gainEnergy/upgrade) を実装 ✓
- [ ] §2.3 連結: 同 spec 連続 → " × N 回"、異 spec → "\n" ✓
- [ ] §2.4 テスト: per-primitive unit test + Format/FormatEffects integration test ✓
- [ ] §2.5 Server DTO 連携: GetCards が formatter 経由 ✓
- [ ] §1.3 override 関係: `Description` 非空なら formatter スキップ ✓

### 2. Placeholder スキャン

- [ ] `(未実装` / `TODO` / `FIXME` を formatter / テストファイルから grep して残存ゼロ確認

### 3. Type consistency チェック

- [ ] `CardDefinition` 末尾追加した optional 引数の呼出側互換 (既存テスト fixture が壊れないこと)
- [ ] `CardJsonLoader` が optional パースでフォールバックする

### 4. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Core ~1015 + Server ~170、新規 + 期待値更新含む)
- [ ] Client 側は変更なし、`npm run build` も sanity 確認

### 5. Commit + push

- [ ] 1 commit (`feat(core): CardTextFormatter for auto card description (Phase 10.5.A)`)
- [ ] `origin master` に push

### 6. tag (本フェーズ後の節目には付けない、Phase 10.5 完了時にまとめて)

- [ ] tag は付けない (10.5.A は Phase 10.5 全体の途中段階)

---

## 完了条件

- [ ] `CardTextFormatter` が Core にあり、各 effect プリミティブを description 文字列に変換する
- [ ] `CardDefinition` に optional な `Description` / `UpgradedDescription` フィールドがあり、JSON ローダーが optional パースする
- [ ] `Server CatalogController.GetCards` が formatter + override で description を確定する
- [ ] Potion 描写も formatter 経由 (A 案採用時)
- [ ] 既存テスト全件緑、新テスト全件緑
- [ ] commit `feat(core): CardTextFormatter for auto card description (Phase 10.5.A)` が origin master に push 済み

## 今回スコープ外（既知の trade-off）

- **versioned JSON schema**: 10.5.B
- **dev menu / editor**: 10.5.C–E
- **relic / enemy / unit の自動文章化**: 10.5.F (potion は今回の formatter 互換で取り込む)
- **i18n 対応 (英語)**: テンプレート JP 固定、内部関数化のみ
- **構造化フォームエディタ**: 10.5.D 検討事項
- **テンプレート微調整による既存表記合わせ**: Task 5 で個別判断、本フェーズ必須ではない

## ロールバック手順

万一 description 表示に致命的不具合があった場合:

1. `Server CatalogController.GetCards` の formatter 呼出を `string.Empty` に置換 (description 空表示)
2. 修正コミット
3. UI 側はカード description が空でも表示崩れしない (Card.tsx は `description?: string` を許容)
4. 後日 formatter 修正後に再有効化

完全 revert する場合: `git revert <commit>` で formatter / DescribeEffects 旧版が戻る。

## 関連ドキュメント

- 本フェーズ spec: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- ロードマップ: [`2026-04-20-roadmap.md`](2026-04-20-roadmap.md) Phase 10.5 セクション
- 親 spec (Phase 10): [`2026-04-25-phase10-battle-system-design.md`](../specs/2026-04-25-phase10-battle-system-design.md)
