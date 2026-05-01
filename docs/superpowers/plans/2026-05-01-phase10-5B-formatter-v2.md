# Phase 10.5.B — CardTextFormatter v2 (キーワード / 新 action spec / 色マーカー) 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 10.5.A で作った `CardTextFormatter` を spec 全体に拡張する。キーワード行表示、状態異常「を付与」表記、色マーカー (`[N:5]` 等)、新 effect action のテキスト対応、Variable / Trigger フィールドの reserved hook を導入。Client は rich-text レンダラで `[N:...]` 等を span に変換、CSS で数字を黄色表示する。**engine 側の新 action / Variable / Trigger 実装は本フェーズ対象外** (10.5.D-F)。

**Architecture:** `CardEffect` record に reserved fields (`CardRefId` / `Select` / `AmountSource` / `Trigger`) を末尾 optional 追加 → `CardJsonLoader` でパース → `CardTextFormatter` を v2 ロジックに書換 (キーワード prefix / "を付与" / 新 action 文言 / マーカー syntax) → Server DTO は変更なし (formatter 出力文字列がマーカー入りで返る) → Client `Card.tsx` で rich-text パース → CSS で `.card-desc-num` 等を彩色。

**Tech Stack:** C# .NET 10 + System.Text.Json (Core / Server)、React 19 + TypeScript (Client)、xUnit (Core テスト)、vitest (Client テスト)。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` (§1-2 / §1-3 / §2)

**Sub-phase scope (本フェーズで実装):**
- ✅ formatter v2 (キーワード行、"を付与"、色マーカー、新 action 文言)
- ✅ `CardEffect` reserved fields (engine は無視、JSON ロードのみ)
- ✅ Client rich-text レンダラ + CSS 黄色化
- ✅ キーワード ID → 表示名 / 説明 マッピング (ワイルド・スーパーワイルドのみ)

**スコープ外 (別 sub-phase):**
- ❌ engine の新 action 実行 (10.5.F)
- ❌ AmountSource 評価 (10.5.D)
- ❌ Trigger 発火 (10.5.E)
- ❌ battle 中の数字 赤/青 比較 (10.5.C)
- ❌ Token rarity (10.5.G)

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Cards/CardEffect.cs` | Modify | reserved fields (`CardRefId` / `Select` / `AmountSource` / `Trigger`) 追加 |
| `src/Core/Cards/CardEffectParser.cs` | Modify | 新フィールドの optional パース |
| `src/Core/Cards/CardTextFormatter.cs` | Modify | v2 ロジック (キーワード行 / "を付与" / 新 action / マーカー) |
| `src/Core/Cards/CardKeywords.cs` | Create | キーワード ID → 表示名 + 説明 (ワイルド / スーパーワイルド) の static 辞書 |
| `tests/Core.Tests/Cards/CardTextFormatterTests.cs` | Modify | 既存テストの期待値マーカー化 + 新テスト群 (キーワード / 新 action / "を付与") |
| `tests/Core.Tests/Cards/CardEffectParserTests.cs` | Modify | 新フィールドのパーステスト追加 |
| `src/Server/Controllers/CatalogController.cs` | (no change) | formatter 文字列出力で完結、DTO 構造変更なし |
| `src/Server/Dtos/...` | (no change) | description は string のまま (マーカー埋込み) |
| `tests/Server.Tests/CatalogControllerTests.cs` | Modify | description 期待値をマーカー入り文字列に更新 |
| `src/Client/src/components/CardDesc.tsx` | Create | rich-text パーサ + span レンダラ (再利用部品) |
| `src/Client/src/components/CardDesc.css` | Create | `.card-desc-num`, `.card-desc-keyword` 等のスタイル |
| `src/Client/src/components/Card.tsx` | Modify | `description` を文字列ではなく `<CardDesc>` で描画 |
| `src/Client/src/components/Tooltip.tsx` | Modify | tip body にも CardDesc 適用 (description の rich-text 化) |
| `src/Client/src/components/CardDesc.test.tsx` | Create | パース + レンダリングの vitest |

---

## Conventions

- **TDD strictly:** テスト → fail → 実装 → green → 次タスク。
- **Build clean:** 各タスク完了時 `dotnet build` 警告 0、`npx tsc --noEmit` パス、`vitest` 全件緑。
- **Server / Core テスト全件緑:** 既存 1038 + 195 + 新規 / 期待値更新を含めて緑を維持。
- **Client テスト全件緑:** 既存 141 + 新規。
- **JP literal:** formatter 内のテンプレ文字列は private const で集約。Client の rich-text パーサは marker syntax のみ知る (JP テキスト本体は知らない)。
- **No engine changes:** `BattleEngine` / `EffectApplier` には触らない。新 fields は JSON ロードで読まれ、reserved として保持されるのみ。
- **Marker syntax:** `[N:5]` (number), `[K:wild]` (keyword), `[T:OnTurnStart]` (trigger), `[V:X|手札の数]` (variable, reserved use), `[C:strike]` (card ref)。括弧は半角、識別子は半角英数のみ。Client パーサは regex で抽出。
- **既存 `[` を含む description override** がある場合に備え、formatter は escape をしない (override 文字列は手書き責任で markup を含めて良い)。

---

## Task 1: CardEffect に reserved fields を追加 (TDD)

**Files:**
- Modify: `src/Core/Cards/CardEffect.cs`
- Modify: `src/Core/Cards/CardEffectParser.cs`
- Modify: `tests/Core.Tests/Cards/CardEffectParserTests.cs`

**目的:** 10.5.D-F で engine が読む新フィールドの **JSON ロード** だけ先行整備。engine の動作は変えない。

### Step 1.1: パーステストを先に書く

- [ ] `tests/Core.Tests/Cards/CardEffectParserTests.cs` に以下を追加:

```csharp
[Fact]
public void Parses_optional_card_ref_id()
{
    var json = """{"action":"addCard","scope":"self","amount":1,"cardRefId":"strike"}""";
    var doc = JsonDocument.Parse(json);
    var eff = CardEffectParser.Parse(doc.RootElement);
    Assert.Equal("strike", eff.CardRefId);
}

[Fact]
public void Parses_optional_select()
{
    var json = """{"action":"discard","scope":"self","amount":1,"select":"choose"}""";
    var doc = JsonDocument.Parse(json);
    var eff = CardEffectParser.Parse(doc.RootElement);
    Assert.Equal("choose", eff.Select);
}

[Fact]
public void Parses_optional_amount_source()
{
    var json = """{"action":"attack","scope":"single","side":"enemy","amount":0,"amountSource":"handCount"}""";
    var doc = JsonDocument.Parse(json);
    var eff = CardEffectParser.Parse(doc.RootElement);
    Assert.Equal("handCount", eff.AmountSource);
}

[Fact]
public void Parses_optional_trigger()
{
    var json = """{"action":"draw","scope":"self","amount":1,"trigger":"OnTurnStart"}""";
    var doc = JsonDocument.Parse(json);
    var eff = CardEffectParser.Parse(doc.RootElement);
    Assert.Equal("OnTurnStart", eff.Trigger);
}

[Fact]
public void Missing_optional_fields_default_null()
{
    var json = """{"action":"attack","scope":"single","side":"enemy","amount":6}""";
    var doc = JsonDocument.Parse(json);
    var eff = CardEffectParser.Parse(doc.RootElement);
    Assert.Null(eff.CardRefId);
    Assert.Null(eff.Select);
    Assert.Null(eff.AmountSource);
    Assert.Null(eff.Trigger);
}
```

- [ ] `dotnet test --filter FullyQualifiedName~CardEffectParserTests` で fail を確認。

### Step 1.2: CardEffect record にフィールド追加

- [ ] `src/Core/Cards/CardEffect.cs` で record 引数末尾に追加:

```csharp
public sealed record CardEffect(
    string Action,
    EffectScope Scope,
    EffectSide? Side,
    int Amount,
    string? Name = null,
    string? UnitId = null,
    int? ComboMin = null,
    string? Pile = null,
    bool BattleOnly = false,
    /// <summary>
    /// 10.5.B reserved: addCard 等で参照する既存カード id。engine 動作は 10.5.F で実装。
    /// </summary>
    string? CardRefId = null,
    /// <summary>
    /// 10.5.B reserved: discard 等の選択方式。"random" | "choose" | "all"。engine 動作は 10.5.F で実装。
    /// </summary>
    string? Select = null,
    /// <summary>
    /// 10.5.B reserved: Variable X 等の amount ソース。"handCount" | "drawPileCount" | 等。engine 動作は 10.5.D で実装。
    /// </summary>
    string? AmountSource = null,
    /// <summary>
    /// 10.5.B reserved: power カードの発火タイミング。"OnTurnStart" | "OnPlayCard" | 等。engine 動作は 10.5.E で実装。
    /// </summary>
    string? Trigger = null)
```

`Normalize` メソッドは現状のまま (新フィールドは正規化対象外)。

### Step 1.3: CardEffectParser を更新

- [ ] `src/Core/Cards/CardEffectParser.cs` で optional パースを追加:

```csharp
string? cardRefId = TryGetString(elem, "cardRefId");
string? select = TryGetString(elem, "select");
string? amountSource = TryGetString(elem, "amountSource");
string? trigger = TryGetString(elem, "trigger");

return new CardEffect(action, scope, side, amount, name, unitId, comboMin, pile, battleOnly,
    cardRefId, select, amountSource, trigger).Normalize();
```

`TryGetString` ヘルパは既存 or 新規追加 (string? を返す optional reader)。

- [ ] `dotnet test --filter FullyQualifiedName~CardEffectParserTests` で全件緑。
- [ ] 既存パーステスト全件緑も確認。

### Step 1.4: ビルド確認

- [ ] `dotnet build` 警告 0 / エラー 0 (既存呼出は後方互換)。

---

## Task 2: CardKeywords 辞書を作成 (TDD)

**Files:**
- Create: `src/Core/Cards/CardKeywords.cs`
- Create: `tests/Core.Tests/Cards/CardKeywordsTests.cs`

**目的:** keyword ID → 表示名 / 説明 のマッピングを Core に集約。formatter / Client tooltip が共有する。

### Step 2.1: テストを先に書く

- [ ] `tests/Core.Tests/Cards/CardKeywordsTests.cs` を新規作成:

```csharp
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardKeywordsTests
{
    [Fact]
    public void Wild_keyword_has_jp_name_and_desc()
    {
        var meta = CardKeywords.Get("wild");
        Assert.NotNull(meta);
        Assert.Equal("ワイルド", meta!.Name);
        Assert.Contains("敵", meta.Description);  // 「敵」を含むかどうかで簡易チェック
    }

    [Fact]
    public void Superwild_keyword_has_jp_name()
    {
        var meta = CardKeywords.Get("superwild");
        Assert.NotNull(meta);
        Assert.Equal("スーパーワイルド", meta!.Name);
    }

    [Fact]
    public void Unknown_keyword_returns_null()
    {
        Assert.Null(CardKeywords.Get("nonexistent"));
    }
}
```

- [ ] `dotnet test --filter FullyQualifiedName~CardKeywordsTests` で fail。

### Step 2.2: 実装

- [ ] `src/Core/Cards/CardKeywords.cs` を新規作成:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// カードキーワード ID → 表示名 / 説明文 のマッピング。
/// formatter のキーワード行表示と Client tooltip popup が共有する。
/// 将来は JSON catalog 化を検討 (現状は ワイルド / スーパーワイルド のみ)。
/// </summary>
public static class CardKeywords
{
    public sealed record KeywordMeta(string Id, string Name, string Description);

    private static readonly Dictionary<string, KeywordMeta> _map = new()
    {
        ["wild"] = new("wild", "ワイルド",
            "敵単体を対象とする攻撃が、ランダムな敵を対象に変わる。"),
        ["superwild"] = new("superwild", "スーパーワイルド",
            "敵単体を対象とする攻撃が、敵全体を対象に変わる。"),
    };

    public static KeywordMeta? Get(string id) =>
        _map.TryGetValue(id, out var meta) ? meta : null;

    public static IReadOnlyDictionary<string, KeywordMeta> All => _map;
}
```

- [ ] `dotnet test --filter FullyQualifiedName~CardKeywordsTests` で全件緑。

---

## Task 3: CardTextFormatter v2 (TDD で段階的拡張)

**Files:**
- Modify: `src/Core/Cards/CardTextFormatter.cs`
- Modify: `tests/Core.Tests/Cards/CardTextFormatterTests.cs`

**目的:** marker syntax + キーワード行 + 新 action + "を付与" の対応。各機能ごと TDD。

### Step 3.1: 既存テストの期待値をマーカー入りに更新

10.5.A の formatter は plain "5" を出力。10.5.B は `[N:5]` を出すよう変更。既存テストの期待値を一括書き換える。

- [ ] `CardTextFormatterTests.cs` の既存テスト 25 件を確認 (`Attack_single_enemy` 等)、期待文字列の数字部分を `[N:N]` で囲む:
  - 旧: `"敵 1 体に 6 ダメージ。"`
  - 新: `"敵 1 体に [N:6] ダメージ。"`
- [ ] 期待値だけ更新 → `dotnet test` で fail を確認。

### Step 3.2: formatter の数字出力を `[N:N]` に変更

- [ ] `CardTextFormatter.cs` の各 `Describe*` メソッドで `e.Amount` を直接埋め込んでいる箇所を `$"[N:{e.Amount}]"` に書換:

```csharp
private static string DescribeAttack(CardEffect e) => e.Scope switch
{
    EffectScope.Single => $"敵 1 体に [N:{e.Amount}] ダメージ",
    EffectScope.Random => $"敵ランダム 1 体に [N:{e.Amount}] ダメージ",
    EffectScope.All => $"敵全体に [N:{e.Amount}] ダメージ",
    _ => $"敵に [N:{e.Amount}] ダメージ",
};
```

block / draw / discard / heal / status 等も同様に `[N:{e.Amount}]` に置換。"× N 回" は `× [N:{count}] 回` に。

- [ ] `dotnet test --filter FullyQualifiedName~CardTextFormatterTests` で全件緑。

### Step 3.3: ステータス変化に「を付与」を追加 (Q8)

- [ ] テスト追加:

```csharp
[Fact]
public void Debuff_weak_with_wo_fuyo_suffix()
{
    var s = CardTextFormatter.FormatEffects(new[] { E("debuff", EffectScope.Single, EffectSide.Enemy, 1, "weak") });
    Assert.Equal("敵 1 体に 脱力 [N:1] を付与。", s);
}
```

- [ ] `DescribeStatusChange` の戻り値に「を付与」を追加:

```csharp
return $"{target}に {jpName} [N:{e.Amount}] を付与";
```

- [ ] 既存 debuff / buff テスト期待値を「を付与」付きに更新 (3.1 のついで更新でも可)。
- [ ] テスト緑。

### Step 3.4: キーワード行 prefix (Q7)

- [ ] テスト追加:

```csharp
[Fact]
public void Keywords_render_as_separate_lines_at_top()
{
    var def = MakeAttackDef(amount: 5, keywords: new[] { "wild" });
    var s = CardTextFormatter.Format(def, upgraded: false);
    Assert.Equal("[K:wild]\n敵 1 体に [N:5] ダメージ。", s);
}

[Fact]
public void Multiple_keywords_each_on_own_line()
{
    var def = MakeAttackDef(amount: 5, keywords: new[] { "wild", "superwild" });
    var s = CardTextFormatter.Format(def, upgraded: false);
    Assert.Equal("[K:wild]\n[K:superwild]\n敵 1 体に [N:5] ダメージ。", s);
}

[Fact]
public void Upgraded_keywords_used_when_upgraded()
{
    var def = MakeAttackDef(amount: 5,
        keywords: new[] { "wild" },
        upgradedKeywords: new[] { "superwild" });
    var s = CardTextFormatter.Format(def, upgraded: true);
    Assert.StartsWith("[K:superwild]\n", s);
}
```

`MakeAttackDef` ヘルパは TestFixture or プライベートメソッドで作成。

- [ ] `Format` メソッドを更新:

```csharp
public static string Format(CardDefinition def, bool upgraded)
{
    string? manual = upgraded ? def.UpgradedDescription : def.Description;
    if (!string.IsNullOrWhiteSpace(manual)) return manual!;

    var keywords = def.EffectiveKeywords(upgraded);
    var keywordLines = keywords?.Select(k => $"[K:{k}]") ?? Enumerable.Empty<string>();

    var effects = upgraded && def.UpgradedEffects is not null
        ? def.UpgradedEffects
        : def.Effects;
    var effectText = FormatEffects(effects);

    var allLines = keywordLines.Concat(effectText.Split('\n'));
    return string.Join("\n", allLines.Where(l => !string.IsNullOrEmpty(l)));
}
```

- [ ] テスト緑。

### Step 3.5: 新 action 文言の追加

各 action ごとにテスト → 実装。本フェーズでは **formatter のみ**で engine は触らない。

#### selfDamage

- [ ] テスト:
```csharp
[Fact]
public void SelfDamage_emits_jp_text()
{
    var s = CardTextFormatter.FormatEffects(new[] { E("selfDamage", EffectScope.Self, null, 3) });
    Assert.Equal("自身のHPを-[N:3]。", s);
}
```
- [ ] `DescribeOne` switch に `"selfDamage" => $"自身のHPを-[N:{e.Amount}]"` を追加。

#### addCard

- [ ] テスト:
```csharp
[Fact]
public void AddCard_to_hand()
{
    var e = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "hand", CardRefId: "strike");
    var s = CardTextFormatter.FormatEffects(new[] { e });
    Assert.Equal("[C:strike] を手札に [N:1] 枚加える。", s);
}

[Fact]
public void AddCard_to_drawpile()
{
    var e = new CardEffect("addCard", EffectScope.Self, null, 2, Pile: "draw", CardRefId: "burn");
    var s = CardTextFormatter.FormatEffects(new[] { e });
    Assert.Equal("[C:burn] を山札に [N:2] 枚加える。", s);
}
```
- [ ] `DescribeOne` で `"addCard"` 分岐:
```csharp
"addCard" => $"[C:{e.CardRefId ?? ""}] を{ZoneJp(e.Pile)}に [N:{e.Amount}] 枚加える",
```
- [ ] `ZoneJp` ヘルパ (`"hand"→"手札"`, `"draw"→"山札"`, `"discard"→"捨札"`, `"exhaust"→"除外"`) を新設。

#### recoverFromDiscard

- [ ] テスト:
```csharp
[Fact]
public void RecoverFromDiscard_random_to_hand()
{
    var e = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 2, Pile: "hand", Select: "random");
    var s = CardTextFormatter.FormatEffects(new[] { e });
    Assert.Equal("捨札からランダムに [N:2] 枚、手札に戻す。", s);
}

[Fact]
public void RecoverFromDiscard_choose_to_exhaust()
{
    var e = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 1, Pile: "exhaust", Select: "choose");
    var s = CardTextFormatter.FormatEffects(new[] { e });
    Assert.Equal("捨札から選んで [N:1] 枚、除外する。", s);
}
```
- [ ] `DescribeOne` で `"recoverFromDiscard"` 分岐 + `SelectJp` ヘルパ (`"random"→"ランダムに"`, `"choose"→"選んで"`, `"all"→"全て"`)。

#### gainMaxEnergy

- [ ] テスト:
```csharp
[Fact]
public void GainMaxEnergy()
{
    var s = CardTextFormatter.FormatEffects(new[] { E("gainMaxEnergy", EffectScope.Self, null, 1) });
    Assert.Equal("エナジー上限を+[N:1]する。", s);
}
```
- [ ] `DescribeOne` で `"gainMaxEnergy" => $"エナジー上限を+[N:{e.Amount}]する"`。

#### discard with Select

- [ ] テスト:
```csharp
[Fact]
public void Discard_with_select_choose()
{
    var e = new CardEffect("discard", EffectScope.Self, null, 1, Select: "choose");
    var s = CardTextFormatter.FormatEffects(new[] { e });
    Assert.Equal("手札を選んで [N:1] 枚捨てる。", s);
}

[Fact]
public void Discard_default_random()
{
    var e = new CardEffect("discard", EffectScope.Self, null, 1);
    var s = CardTextFormatter.FormatEffects(new[] { e });
    Assert.Equal("手札 [N:1] 枚を捨てる。", s);  // Select なしは旧仕様維持 = "手札 N 枚を捨てる"
}
```

- [ ] `DescribeOne` で `"discard"` 分岐を `Select` 有無で出し分け。

### Step 3.6: Power trigger marker (Q1, Q4)

- [ ] テスト:
```csharp
[Fact]
public void Power_trigger_emits_marker_prefix()
{
    var e = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart");
    var s = CardTextFormatter.FormatEffects(new[] { e });
    Assert.Equal("[T:OnTurnStart]の度にカードを [N:1] 枚引く。", s);
}
```

- [ ] `DescribeGroup` を更新:
```csharp
private static string DescribeGroup(CardEffect e, int count)
{
    var head = DescribeOne(e);
    var triggerPrefix = !string.IsNullOrEmpty(e.Trigger) ? $"[T:{e.Trigger}]の度に" : "";
    if (count <= 1) return $"{triggerPrefix}{head}。";
    return $"{triggerPrefix}{head} × [N:{count}] 回。";
}
```

### Step 3.7: AmountSource (Variable X) marker (Q2 reserve)

- [ ] テスト:
```csharp
[Fact]
public void AmountSource_handCount_emits_X_marker()
{
    var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0, AmountSource: "handCount");
    var s = CardTextFormatter.FormatEffects(new[] { e });
    Assert.Equal("敵 1 体に [V:X|手札の数] ダメージ。", s);
}
```

- [ ] `[N:{amount}]` 出力箇所を **AmountSource があれば優先**して `[V:X|<jp_label>]` を出すように:

```csharp
private static string AmountToken(CardEffect e)
{
    if (string.IsNullOrEmpty(e.AmountSource)) return $"[N:{e.Amount}]";
    var label = AmountSourceJp(e.AmountSource);
    // 1 個目変数は X、2 個目以降は呼び出し側で Y/Z 採番 (後で対応)
    return $"[V:X|{label}]";
}

private static string AmountSourceJp(string src) => src switch
{
    "handCount" => "手札の数",
    "drawPileCount" => "山札の数",
    "discardPileCount" => "捨札の数",
    "exhaustPileCount" => "除外の数",
    "selfHp" => "自身のHP",
    "comboCount" => "現在のコンボ",
    _ => src,
};
```

各 `Describe*` で `e.Amount` を直接書いている箇所を `AmountToken(e)` に置換 (リテラル `[N:{e.Amount}]` を全部書換)。

### Step 3.8: 連結ルールの整理

10.5.A の「連続同 spec → × N 回」は 10.5.B でも維持。ただし Trigger / AmountSource が違う場合は同 spec とみなさない:

- [ ] `IsSameSpec` を更新:
```csharp
private static bool IsSameSpec(CardEffect a, CardEffect b)
    => a.Action == b.Action
    && a.Scope == b.Scope
    && a.Side == b.Side
    && a.Amount == b.Amount
    && a.Name == b.Name
    && a.UnitId == b.UnitId
    && a.CardRefId == b.CardRefId
    && a.Select == b.Select
    && a.AmountSource == b.AmountSource
    && a.Trigger == b.Trigger
    && a.Pile == b.Pile;
```

- [ ] テストで「同 action だが Trigger 違い → 別グループ」が分離されることを検証。

### Step 3.9: ビルド + 全テスト

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑

---

## Task 4: Server Catalog テストの期待値更新

**Files:**
- Modify: `tests/Server.Tests/CatalogControllerTests.cs`

### Step 4.1: 既存 description アサート箇所をマーカー入り文字列に更新

- [ ] 10.5.A で追加した `strike` / `defend` の description 期待値を `[N:N]` 含む形式に更新:
  - 旧: `"敵 1 体に 6 ダメージ。"`
  - 新: `"敵 1 体に [N:6] ダメージ。"`
- [ ] `dotnet test --filter FullyQualifiedName~CatalogControllerTests` で全件緑。

---

## Task 5: Client - rich-text レンダラ `<CardDesc>` (TDD)

**Files:**
- Create: `src/Client/src/components/CardDesc.tsx`
- Create: `src/Client/src/components/CardDesc.css`
- Create: `src/Client/src/components/CardDesc.test.tsx`

**目的:** マーカー入り文字列を React span に変換する純コンポーネント。Card / Tooltip / その他カード文言表示で再利用。

### Step 5.1: テストを先に書く

- [ ] `src/Client/src/components/CardDesc.test.tsx` を新規作成:

```tsx
import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { CardDesc } from './CardDesc'

describe('CardDesc', () => {
  it('renders plain text without markers as-is', () => {
    render(<CardDesc text="敵に攻撃する。" />)
    expect(screen.getByText('敵に攻撃する。')).toBeInTheDocument()
  })

  it('wraps [N:5] in a yellow num span', () => {
    const { container } = render(<CardDesc text="敵 1 体に [N:5] ダメージ。" />)
    const num = container.querySelector('.card-desc-num')
    expect(num).toBeInTheDocument()
    expect(num?.textContent).toBe('5')
  })

  it('wraps [K:wild] in keyword span with display name', () => {
    const { container } = render(<CardDesc text="[K:wild]\n敵 1 体に [N:5] ダメージ。" />)
    const kw = container.querySelector('.card-desc-keyword')
    expect(kw?.textContent).toBe('ワイルド')
  })

  it('renders [T:OnTurnStart] as JP label', () => {
    const { container } = render(<CardDesc text="[T:OnTurnStart]の度にカードを [N:1] 枚引く。" />)
    expect(container.textContent).toContain('ターン開始時')
  })

  it('renders [V:X|手札の数] as X(Xは手札の数)', () => {
    const { container } = render(<CardDesc text="敵 1 体に [V:X|手札の数] ダメージ。" />)
    expect(container.textContent).toContain('X(Xは手札の数)')
  })

  it('renders [C:strike] as card name reference', () => {
    const { container } = render(<CardDesc text="[C:strike] を手札に [N:1] 枚加える。" cardNames={{ strike: 'ストライク' }} />)
    expect(container.textContent).toContain('ストライク')
  })

  it('handles multiline newlines', () => {
    const { container } = render(<CardDesc text="行 1。\n行 2。" />)
    expect(container.querySelectorAll('.card-desc-line').length).toBe(2)
  })
})
```

- [ ] `npx vitest run CardDesc` で fail (CardDesc 未存在)。

### Step 5.2: 実装

- [ ] `src/Client/src/components/CardDesc.tsx`:

```tsx
import './CardDesc.css'

const KEYWORD_JP: Record<string, string> = {
  wild: 'ワイルド',
  superwild: 'スーパーワイルド',
}

const TRIGGER_JP: Record<string, string> = {
  OnTurnStart: 'ターン開始時',
  OnPlayCard: 'カードプレイ時',
  OnDamageReceived: 'ダメージ受け時',
  OnCombo: 'コンボ達成時',
}

type Props = {
  text: string
  /** [C:cardId] のカード id → 表示名マップ。catalog から渡す。 */
  cardNames?: Record<string, string>
}

const MARKER_RE = /\[(N|K|T|V|C):([^\]|]+)(?:\|([^\]]+))?\]/g

export function CardDesc({ text, cardNames = {} }: Props) {
  // \n リテラル (テスト経由) と実改行両方を扱う
  const normalized = text.replace(/\\n/g, '\n')
  const lines = normalized.split('\n')
  return (
    <span className="card-desc">
      {lines.map((line, lineIdx) => (
        <span key={lineIdx} className="card-desc-line">
          {renderLine(line, cardNames)}
          {lineIdx < lines.length - 1 ? <br /> : null}
        </span>
      ))}
    </span>
  )
}

function renderLine(line: string, cardNames: Record<string, string>) {
  const parts: React.ReactNode[] = []
  let lastIndex = 0
  let key = 0
  for (const m of line.matchAll(MARKER_RE)) {
    const idx = m.index ?? 0
    if (idx > lastIndex) parts.push(line.slice(lastIndex, idx))
    const [, kind, value, extra] = m
    parts.push(renderMarker(kind, value, extra, cardNames, key++))
    lastIndex = idx + m[0].length
  }
  if (lastIndex < line.length) parts.push(line.slice(lastIndex))
  return parts
}

function renderMarker(
  kind: string,
  value: string,
  extra: string | undefined,
  cardNames: Record<string, string>,
  key: number,
): React.ReactNode {
  switch (kind) {
    case 'N':
      return <span key={key} className="card-desc-num">{value}</span>
    case 'K': {
      const jp = KEYWORD_JP[value] ?? value
      return <span key={key} className="card-desc-keyword" data-keyword={value}>{jp}</span>
    }
    case 'T': {
      const jp = TRIGGER_JP[value] ?? value
      return <span key={key} className="card-desc-trigger">{jp}</span>
    }
    case 'V': {
      const label = extra ?? '?'
      return <span key={key} className="card-desc-var">{value}(Xは{label})</span>
    }
    case 'C': {
      const name = cardNames[value] ?? value
      return <span key={key} className="card-desc-cardref">{name}</span>
    }
    default:
      return <span key={key}>{value}</span>
  }
}
```

- [ ] `src/Client/src/components/CardDesc.css`:

```css
/* Why: カード description の数字・キーワード・トリガを色分けする。
   battle 中の赤/青比較は 10.5.C で別 modifier class を追加予定。 */
.card-desc {
  display: inline;
  white-space: pre-wrap;
}
.card-desc-line {
  display: inline;
}
.card-desc-num {
  color: #ffd23f;       /* 黄 (デフォルト数字) */
  font-weight: bold;
}
.card-desc-keyword {
  color: #c5d4ff;       /* キーワードは薄青系 (ワイルド等の特殊能力ヒント) */
  font-weight: bold;
  cursor: help;
}
.card-desc-trigger {
  color: #ffd9a0;       /* パワーのトリガ部分 */
  font-weight: bold;
}
.card-desc-var {
  color: #c8ffb3;       /* 変数 X (緑系) */
  font-weight: bold;
}
.card-desc-cardref {
  color: #d8b4ff;       /* カード参照 (紫系) */
}
```

- [ ] `npx vitest run CardDesc` で全件緑。

---

## Task 6: Card.tsx で description を CardDesc 経由に切替

**Files:**
- Modify: `src/Client/src/components/Card.tsx`
- Modify: `src/Client/src/components/Tooltip.tsx`

### Step 6.1: Card.tsx の description 表示を CardDesc で

Card.tsx 自体は description を表示していない (ツールチップ内のみ)。useTooltipTarget で生成する `tooltipContent.desc` を文字列のまま渡しているが、Tooltip の描画側で CardDesc を使う方が再利用しやすい。

- [ ] `Card.tsx`:
  - `tooltipContent` の `desc` フィールドはそのまま文字列で渡す (TooltipContent 型変更しない)
  - 別途、card 内で description を JSX 表示する箇所はないので Card.tsx 自体への直接的影響は最小
- [ ] `Tooltip.tsx`:
  - 描画箇所 `<div className="tip__desc">{content.desc}</div>` を:
    ```tsx
    <div className="tip__desc">
      {typeof content.desc === 'string'
        ? <CardDesc text={content.desc} cardNames={...} />
        : content.desc}
    </div>
    ```
  - `cardNames` は context もしくは別 prop で渡す (catalog から取得)。とりあえず `useCardCatalog` を呼んで `names` を渡す形でスタート。

### Step 6.2: 既存 vitest が壊れないこと

- [ ] `npx vitest run` 全件緑を確認。
- [ ] tooltip 内文字列を直接 toEqual したテストがあれば、CardDesc 経由でも textContent 一致していれば緑になるはずだが、もし壊れたら期待値を `getByText` ベースに調整。

---

## Task 7: Self-review + 1 commit + push

### 1. Spec coverage チェック

- [ ] §1-3 Q1: Trigger marker `[T:OnTurnStart]` 出力 ✓
- [ ] §1-3 Q2: AmountSource reserved + `[V:X|...]` marker ✓
- [ ] §1-3 Q3: 黄色のみ (赤/青は 10.5.C) ✓
- [ ] §1-3 Q4: Trigger reserved + marker (engine 未実装) ✓
- [ ] §1-3 Q5: 新 action (selfDamage / addCard / recoverFromDiscard / gainMaxEnergy / discard.Select) の文言 ✓
- [ ] §1-3 Q6: addCard で `[C:strike]` 形式 (既存カード id 参照) ✓
- [ ] §1-3 Q7: キーワード行 + `[K:wild]` marker + Client 側 `card-desc-keyword` 色変え ✓
- [ ] §1-3 Q8: 「を付与」suffix ✓
- [ ] §1-3 Q9: marker syntax + Client 解析 ✓

### 2. Engine 非汚染チェック

- [ ] `BattleEngine` / `EffectApplier` には touch していない (新フィールドは reserved として CardEffect に乗っているだけ)
- [ ] 既存 engine テスト全件緑

### 3. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Core 1038 + 期待値更新含む新規 ~30 / Server 195 + 期待値更新)
- [ ] `cd src/Client && npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑 (既存 141 + 新規 ~7 ≒ 148)
- [ ] `npm run build` (Client) もエラーなし

### 4. Commit + push

- [ ] 1 commit (`feat(core/client): formatter v2 with markers, keywords, new action specs (Phase 10.5.B)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] `CardEffect` に reserved fields (CardRefId / Select / AmountSource / Trigger) が追加され、JSON ロードでパースされる
- [ ] `CardKeywords` 辞書 (wild / superwild) が Core にある
- [ ] `CardTextFormatter` がキーワード行 / "を付与" / 新 action / marker syntax をすべて出力できる
- [ ] Client `<CardDesc>` コンポーネントが marker を span に変換、CSS で色分け表示
- [ ] Tooltip の desc 描画が CardDesc 経由で rich-text 化
- [ ] 既存テスト全件緑、新テスト全件緑
- [ ] commit `feat(core/client): formatter v2 ... (Phase 10.5.B)` が origin master に push 済み

## 今回スコープ外（既知の trade-off）

- **engine 動作**:
  - `selfDamage` / `addCard` / `recoverFromDiscard` / `gainMaxEnergy` / `discard.Select` の実行は **10.5.F**
  - `AmountSource` 評価は **10.5.D**
  - `Trigger` 発火は **10.5.E**
- **battle 中の赤/青比較**: 10.5.C
- **Token rarity フィルタ**: 10.5.G
- **キーワード詳細 popup の hover 動作**: 10.5.B では keyword 表示と CSS 色分けまで。hover で詳細 popup を出す UI は別途 (本フェーズ範囲なら + 1 task で追加可能、追加するなら既存 useTooltipTarget をネスト or 専用 KeywordTooltip 作成)

> **追加検討**: キーワード hover popup を 10.5.B に含めるかは進行時に判断。重ければ 10.5.B-2 として切る。

## ロールバック手順

万一 Client の rich-text 描画でレイアウト破綻があった場合:
1. `Tooltip.tsx` の `<CardDesc>` を `{content.desc}` plain 表示に一時 revert
2. ユーザは marker 入り文字列を生で見ることになるが、機能は保たれる
3. CardDesc 修正後に再有効化

完全 revert: `git revert <commit>` で formatter / CardEffect / CardDesc 全て元 (10.5.A 完了状態) に戻る。

## 関連ドキュメント

- 本フェーズ spec: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md) §1-2 / §1-3 / §2
- ロードマップ Phase 10.5: [`2026-04-20-roadmap.md`](2026-04-20-roadmap.md)
- 前 sub-phase 完了 plan: [`2026-05-01-phase10-5A-formatter.md`](2026-05-01-phase10-5A-formatter.md)
