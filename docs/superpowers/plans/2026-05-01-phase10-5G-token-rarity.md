# Phase 10.5.G — Token Rarity 実装

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** カードレアリティに `Token` を追加し、トークンカードが報酬抽選 / 商人在庫から自動除外されるようにする。これにより `addCard` effect で battle 中に手札に来るが、デッキ強化フローには出てこないトークンカード (Slay the Spire の Wound / Burn / Slimed 等に相当) を実装可能にする。

**Architecture:** `Core/Cards/CardRarity` に `Token=5` を追加 → `CardJsonLoader` で受理 → `RewardGenerator` / `MerchantInventoryGenerator` が rarity ベースで明示除外 → Server `CardCatalogEntryDto.Rarity` は int=5 がそのまま流れる → Client `cardRarityFromNumber` が `5 → 't'` を返す → CSS で当面 Common と同じ色 (将来差別化)。

**Tech Stack:** C# .NET 10、xUnit、TypeScript、vitest。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-3 Q6

**スコープ外:**
- Token カード専用 JSON データ追加 (本フェーズは仕組みのみ、データは別途)
- Token カード専用 図鑑 (アーカイブ) 画面 — bestiary が card id 単位で discovered tracking する既存機構があれば、Token は単にそこに乗らない (収集対象外) で十分
- Token rarity 専用の見た目 (CSS / icon) の差別化 — 当面 Common 同等表示で OK

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Cards/CardRarity.cs` | Modify | enum に `Token = 5` 追加 |
| `src/Core/Cards/CardJsonLoader.cs` | (確認のみ) | `Enum.IsDefined` で 5 を許容するか確認、現状で OK のはず |
| `src/Core/Rewards/RewardGenerator.cs` | Modify | rarity フィルタに `c.Rarity != CardRarity.Token` を追加 (defensive) |
| `src/Core/Merchant/MerchantInventoryGenerator.cs` | Modify | 同様 (defensive) |
| `tests/Core.Tests/Cards/CardJsonLoaderTests.cs` | Modify | rarity=5 (Token) JSON を読めるテスト追加 |
| `tests/Core.Tests/Rewards/RewardGeneratorTests.cs` | Modify (or 追加) | Token 化したカードが報酬から除外されるテスト |
| `tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs` | Modify (or 追加) | 同様、商人在庫から除外 |
| `src/Client/src/screens/battleScreen/dtoAdapter.ts` | Modify | `cardRarityFromNumber` で `case 5: return 't'` を追加 |
| `src/Client/src/components/Card.tsx` | Modify | `CardRarity` 型 union に `'t'` 追加 |
| `src/Client/src/components/Card.css` | Modify | `.card--rarity-t` を Common と同色 (placeholder) で定義 |
| `src/Client/src/components/Tooltip.css` | Modify | `.tip__rare--t` 同様 |
| `src/Client/src/components/Tooltip.tsx` | Modify | `defaultRarityLabel` に `case 't': return 'TOKEN'` |

---

## Conventions

- **TDD strictly.**
- **Build clean.**
- **Defensive filter.** `reward_` prefix の既存フィルタが Token を除外できる前提でも、明示的に rarity でも除外する (将来 reward_ prefix 規約が変わっても安全)。
- **Display 当面 Common 同等.** 差別化は別フェーズで。
- **JSON での表記**: `"rarity": 5` (int) を期待。string "token" は受理しない (既存の int-based 規約維持)。

---

## Task 1: CardRarity に Token を追加

**Files:**
- Modify: `src/Core/Cards/CardRarity.cs`
- Modify: `tests/Core.Tests/Cards/CardJsonLoaderTests.cs` (TDD)

### Step 1.1: テスト

- [ ] CardJsonLoaderTests に追加:

```csharp
[Fact]
public void Loads_token_rarity()
{
    var json = """
    {
      "id": "wound",
      "name": "傷",
      "rarity": 5,
      "cardType": "Status",
      "cost": null,
      "effects": []
    }
    """;
    var def = CardJsonLoader.LoadFromJson(json);
    Assert.Equal(CardRarity.Token, def.Rarity);
}
```

- [ ] fail を確認 (rarity=5 が `Enum.IsDefined` で reject される前のテスト)。

### Step 1.2: enum 拡張

- [ ] `src/Core/Cards/CardRarity.cs`:

```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードのレアリティ。JSON では整数として保存する。</summary>
public enum CardRarity
{
    Promo = 0,
    Common = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
    /// <summary>
    /// バトル中の addCard effect で手札に加えられる token カード。
    /// 報酬・商人プールには出現しない (RewardGenerator / MerchantInventoryGenerator で除外)。
    /// 図鑑の通常コレクション対象外。
    /// </summary>
    Token = 5,
}
```

- [ ] `dotnet build` パス、Step 1.1 のテスト緑。

---

## Task 2: RewardGenerator から Token を除外

**Files:**
- Modify: `src/Core/Rewards/RewardGenerator.cs`
- Modify: `tests/Core.Tests/Rewards/RewardGeneratorTests.cs`

### Step 2.1: テスト

- [ ] 既存 RewardGeneratorTests に追加:

```csharp
[Fact]
public void Token_rarity_card_excluded_from_rewards()
{
    // Token rarity の reward_ prefix card が catalog にあっても、報酬抽選で選ばれない
    var tokenCard = new CardDefinition(
        Id: "reward_token_test",  // 規約破りの prefix だが defensive 検証
        Name: "テストトークン", DisplayName: null,
        Rarity: CardRarity.Token,
        CardType: CardType.Status,
        Cost: null, UpgradedCost: null,
        Effects: System.Array.Empty<CardEffect>(),
        UpgradedEffects: null, Keywords: null, UpgradedKeywords: null);
    var catalog = TestCatalogs.WithCard(tokenCard /* + 通常カード多数 */);

    // 多数回抽選しても Token カードは picks に出ない
    for (int seed = 0; seed < 50; seed++)
    {
        var picks = RewardGenerator.GenerateCardChoices(/* args... */);
        Assert.DoesNotContain("reward_token_test", picks);
    }
}
```

(`TestCatalogs.WithCard` 等の helper は既存のものを使う or 必要なら追加)

### Step 2.2: 実装

- [ ] `RewardGenerator.cs` のフィルタ行 (line ~140):

```csharp
.Where(c => c.Rarity == rarity && c.Id.StartsWith("reward_") && c.Rarity != CardRarity.Token)
```

(Rarity == rarity の比較で既に Token は除外されるが、コード意図を明示するため重ねて書く。あるいは defensive line として追加)

実は `rarity` は Common/Rare/Epic のいずれかなので Token は元々除外される。それでも将来「Token rarity の card が抽選対象」となるバグを早期検出するため、明示的フィルタを最上層に追加する:

```csharp
var pool = data.Cards.Values.Where(c => c.Rarity != CardRarity.Token).ToList();
// 以降は pool ベース
```

- [ ] テスト緑。

---

## Task 3: MerchantInventoryGenerator から Token を除外

**Files:**
- Modify: `src/Core/Merchant/MerchantInventoryGenerator.cs`
- Modify: `tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs`

### Step 3.1: テスト

- [ ] 同様に Token カードが商人在庫に出ないことを検証。

### Step 3.2: 実装

- [ ] フィルタ行 (line ~37):

```csharp
.Where(c => c.Id.StartsWith("reward_")
    && prices.Cards.ContainsKey(c.Rarity)
    && c.Rarity != CardRarity.Token)
```

または上層で除外:
```csharp
var allCards = data.Cards.Values.Where(c => c.Rarity != CardRarity.Token);
```

`prices.Cards.ContainsKey(c.Rarity)` は MerchantPrices の dictionary に Token が無ければ自動 false なので、これだけでも除外される可能性高。明示的フィルタを足して意図を明確化する。

- [ ] テスト緑。

---

## Task 4: Client 側の Token rarity 対応

**Files:**
- Modify: `src/Client/src/components/Card.tsx` (CardRarity 型)
- Modify: `src/Client/src/screens/battleScreen/dtoAdapter.ts` (`cardRarityFromNumber`)
- Modify: `src/Client/src/components/Card.css` (`.card--rarity-t`)
- Modify: `src/Client/src/components/Tooltip.tsx` (`defaultRarityLabel`)
- Modify: `src/Client/src/components/Tooltip.css` (`.tip__rare--t`、`.tip__name--t`)

### Step 4.1: 型 union 拡張

- [ ] `src/Client/src/components/Card.tsx`:
```typescript
export type CardRarity = 'c' | 'r' | 'e' | 'l' | 't'
```

- [ ] `npx tsc --noEmit` で existing consumers を確認。switch 文に網羅性チェックがあれば自動的にエラー出るので拾って対応。

### Step 4.2: dtoAdapter

- [ ] `cardRarityFromNumber`:
```typescript
function cardRarityFromNumber(n: number): CardRarity {
  switch (n) {
    case 0: return 'c'  // Promo
    case 1: return 'c'  // Common
    case 2: return 'r'  // Rare
    case 3: return 'e'  // Epic
    case 4: return 'l'  // Legendary
    case 5: return 't'  // Token
    default: return 'c'
  }
}
```

(既存マッピングが Promo を扱っていなければ整理する。CardRarity enum と整数の対応を一度確認)

- [ ] `relicRarityFromString` 等他の rarity 経路も Token を扱えるか確認、必要なら同等更新。

### Step 4.3: Tooltip ラベル

- [ ] `Tooltip.tsx` の `defaultRarityLabel`:
```typescript
function defaultRarityLabel(r: CardRarity): string {
  switch (r) {
    case 'c': return 'COMMON'
    case 'r': return 'RARE'
    case 'e': return 'EPIC'
    case 'l': return 'LEGENDARY'
    case 't': return 'TOKEN'
  }
}
```

### Step 4.4: CSS placeholder

- [ ] `Card.css` 末尾に追加 (Common と同色を仮置き):
```css
.card--rarity-t { /* Token: placeholder, 当面 Common 同色 */
  border-color: var(--rarity-c-border, #b8a880);
}
.card--rarity-t .card__rarity {
  color: var(--rarity-c, #b8a880);
}
```

(既存 `.card--rarity-c` 等のスタイル定義を確認して同等パターンで)

- [ ] `Tooltip.css`:
```css
.tip__rare--t { color: var(--rarity-c); border-color: var(--rarity-c-border); background: var(--rarity-c-bg); }
.tip__name--t { color: var(--rarity-c); }
```

(差別化は別フェーズ)

### Step 4.5: ビルド・テスト

- [ ] `npm run build` (Client) エラーなし
- [ ] `npx vitest run` 全件緑

---

## Task 5: Self-review + 1 commit + push

### 1. Spec coverage

- [ ] Token=5 が CardRarity に存在 ✓
- [ ] CardJsonLoader が rarity=5 JSON を受理 ✓
- [ ] RewardGenerator / MerchantInventoryGenerator が Token を除外 ✓
- [ ] Client が Token rarity を 't' で扱える、ラベル "TOKEN" 表示 ✓

### 2. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑
- [ ] `npm run build` (Client) エラーなし

### 3. Commit + push

- [ ] 1 commit (`feat(core/client): Token rarity for battle-only token cards (Phase 10.5.G)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] `CardRarity.Token = 5` が enum に存在し、JSON ローダーが受理
- [ ] RewardGenerator / MerchantInventoryGenerator が Token を明示除外
- [ ] Client `CardRarity` 型に 't' 追加、`cardRarityFromNumber(5)` が 't' を返す、`defaultRarityLabel('t') === 'TOKEN'`
- [ ] CSS で `.card--rarity-t` / `.tip__rare--t` / `.tip__name--t` placeholder
- [ ] 既存テスト全件緑、新テスト追加で全件緑
- [ ] commit + push 済み

## 今回スコープ外

- Token カード専用 JSON データ作成 (`wound.json` 等) — 必要時に追加
- Token カードを取得する図鑑/コレクション画面 — 当面 normal achievements に出ないだけ
- Token rarity 専用 CSS デザイン — Common 同色 placeholder、後日差別化

## ロールバック

問題があれば `CardRarity.Token = 5` を enum から削除すれば、5 を含む JSON は読めずローダーで弾かれる (Token カード作成済みなら問題、データなければ即 revert 可)。

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5D-variable-x.md`](2026-05-01-phase10-5D-variable-x.md)
