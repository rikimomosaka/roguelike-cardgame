# Phase 10.5.C — Battle 色比較 (赤/青) 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** バトル中の手札・各パイル表示で、カード description の数字が **現在値 > base なら赤、< なら青、= なら黄 (既定)** で表示されるようにする。Strength / Weak / Dexterity 等のバフ・デバフによる調整が「上振れ / 下振れ」として一目で分かる。

**Architecture:** Core 側で `CardTextFormatter` に context 付き formatter API を追加 → Server `BattleStateDtoMapper` が hero (caster) の statuses を context に渡して各 `BattleCardInstanceDto` に `adjustedDescription` / `adjustedUpgradedDescription` を populate → Client `CardDesc` が `[N:5|up]` / `[N:5|down]` の修飾子を読んで CSS class を切替 → 既存 catalog 表示は無修飾の `[N:5]` のまま (catalog では context 不明なので比較なし)。

**Tech Stack:** C# .NET 10 (Core / Server)、xUnit、React 19 + TypeScript (Client)、vitest。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-3 Q3、§1-2 (10.5.C)

**スコープ外 (別 sub-phase):**
- Variable X (`[V:X|...]`) の up/down 比較 (10.5.D)
- Power trigger 発火後の amount 変化 (10.5.E)

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Cards/CardTextFormatter.cs` | Modify | `Format(def, upgraded, context)` overload + context-aware amount で `[N:N\|up]` `[N:N\|down]` emit |
| `src/Core/Cards/CardActorContext.cs` | Create | `record CardActorContext(int Strength, int Weak, int Dexterity)` value object (formatter context) |
| `tests/Core.Tests/Cards/CardTextFormatterTests.cs` | Modify | context overload のテスト追加 (strength で up、weak で down 等) |
| `src/Server/Dtos/BattleCardInstanceDto.cs` | Modify | `AdjustedDescription` / `AdjustedUpgradedDescription` (string?) を追加 |
| `src/Server/Services/BattleStateDtoMapper.cs` | Modify | hand / draw / discard / exhaust / summonHeld / power 全 pile の card に対して hero の `CardActorContext` で adjusted description を計算 |
| `tests/Server.Tests/Services/BattleStateDtoMapperTests.cs` | Modify (or Create) | adjusted description が hero context で計算されることを確認 |
| `src/Client/src/api/types.ts` | Modify | `BattleCardInstanceDto` に adjustedDescription 2 フィールド追加 |
| `src/Client/src/components/CardDesc.tsx` | Modify | `[N:5\|up]` `[N:5\|down]` を `.card-desc-num--up` / `--down` クラスで描画 |
| `src/Client/src/components/CardDesc.css` | Modify | `.card-desc-num--up` (赤) / `--down` (青) の色定義 |
| `src/Client/src/components/CardDesc.test.tsx` | Modify | up/down 描画のテスト追加 |
| `src/Client/src/screens/battleScreen/dtoAdapter.ts` | Modify | `toHandCardDemo` 等が adjustedDescription を優先して `description` フィールドに渡す |
| `src/Client/src/screens/BattleScreen.tsx` | (no change) | dtoAdapter を介して透過的に新 description が流れる |

---

## Conventions

- **TDD strictly.** テスト → fail → 実装 → green → 次タスク。
- **Build clean.** `dotnet build` 警告 0、`npx tsc --noEmit` パス、`vitest` 全件緑。
- **Core は純粋ロジック維持.** `CardActorContext` は単純な record、ASP.NET Core 系 using 一切使わない。
- **既存 catalog は変更なし.** 10.5.B の `[N:5]` 出力はそのまま、context あり経路だけ `|up` / `|down` を追加する。
- **比較対象は effect の amount.** upgraded vs base 比較はしない (10.5.B の Format(def, upgraded) で既に upgraded 用 effects から `[N:9]` が出る、強化した結果が新 base 扱い)。
- **比較ロジック (Server).** 既存 `BattleStateDtoMapper.AdjustAttackAmount` と整合させる:
  - attack: `withStr = base + strength`、`weak > 0` なら `(int)(withStr * 0.75)` (floor)
  - block: 当面 `base + dexterity` (engine 側の dexterity 計算と同じ式があれば合わせる、無ければ単純加算)
  - 他 (draw / discard / heal / status / etc.) は **比較対象外**、無修飾 `[N:5]` のまま
- **Caster は hero 固定.** 10.5.C スコープでは hero (player) actor の statuses を使う。enemy / summon の手札表示は無いので不要。

---

## Task 1: CardActorContext record を新設 (TDD)

**Files:**
- Create: `src/Core/Cards/CardActorContext.cs`
- Create or Modify: `tests/Core.Tests/Cards/CardActorContextTests.cs` (任意、テストは Task 2 とまとめても可)

### Step 1.1: record 作成

- [ ] `src/Core/Cards/CardActorContext.cs`:

```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// CardTextFormatter が context-aware に [N:N|up/down] マーカーを出すために
/// 受け取る、actor のスタタス snapshot。Phase 10.5.C で導入。
/// </summary>
/// <param name="Strength">attack に加算される筋力</param>
/// <param name="Weak">>0 なら attack に 0.75 倍 (floor)</param>
/// <param name="Dexterity">block に加算される敏捷</param>
public sealed record CardActorContext(
    int Strength,
    int Weak,
    int Dexterity)
{
    public static readonly CardActorContext Empty = new(0, 0, 0);
}
```

- [ ] `dotnet build` 通る。

---

## Task 2: CardTextFormatter に context overload を追加 (TDD)

**Files:**
- Modify: `src/Core/Cards/CardTextFormatter.cs`
- Modify: `tests/Core.Tests/Cards/CardTextFormatterTests.cs`

### Step 2.1: テストを先に書く

- [ ] 以下のテストを追加:

```csharp
[Fact]
public void Attack_with_strength_emits_up_marker()
{
    var def = MakeAttackDef(amount: 5);
    var ctx = new CardActorContext(Strength: 2, Weak: 0, Dexterity: 0);
    var s = CardTextFormatter.Format(def, upgraded: false, ctx);
    Assert.Equal("敵 1 体に [N:7|up] ダメージ。", s);
}

[Fact]
public void Attack_with_weak_emits_down_marker()
{
    var def = MakeAttackDef(amount: 5);
    var ctx = new CardActorContext(Strength: 0, Weak: 1, Dexterity: 0);
    // 5 * 0.75 = 3.75 → floor 3
    var s = CardTextFormatter.Format(def, upgraded: false, ctx);
    Assert.Equal("敵 1 体に [N:3|down] ダメージ。", s);
}

[Fact]
public void Attack_unchanged_emits_no_modifier()
{
    var def = MakeAttackDef(amount: 5);
    var s = CardTextFormatter.Format(def, upgraded: false, CardActorContext.Empty);
    Assert.Equal("敵 1 体に [N:5] ダメージ。", s);
}

[Fact]
public void Block_with_dexterity_emits_up()
{
    var def = MakeSkillDefBlock(amount: 5);
    var ctx = new CardActorContext(Strength: 0, Weak: 0, Dexterity: 3);
    var s = CardTextFormatter.Format(def, upgraded: false, ctx);
    Assert.Equal("自身にブロック [N:8|up] を得る。", s);
}

[Fact]
public void Strength_after_weak_uses_floor()
{
    // (5 + 2) * 0.75 = 5.25 → 5。base 5 と等しいので無修飾。
    var def = MakeAttackDef(amount: 5);
    var ctx = new CardActorContext(Strength: 2, Weak: 1, Dexterity: 0);
    var s = CardTextFormatter.Format(def, upgraded: false, ctx);
    Assert.Equal("敵 1 体に [N:5] ダメージ。", s);
}

[Fact]
public void Format_overload_without_context_keeps_existing_behavior()
{
    // 既存 Format(def, upgraded) は無 context として動く (CardActorContext.Empty 経由)。
    var def = MakeAttackDef(amount: 5);
    var s = CardTextFormatter.Format(def, upgraded: false);
    Assert.Equal("敵 1 体に [N:5] ダメージ。", s);
}
```

- [ ] テスト fail を確認。

### Step 2.2: 実装

- [ ] `CardTextFormatter` に新 overload:

```csharp
public static string Format(CardDefinition def, bool upgraded, CardActorContext context)
{
    string? manual = upgraded ? def.UpgradedDescription : def.Description;
    if (!string.IsNullOrWhiteSpace(manual)) return manual!;

    var keywords = def.EffectiveKeywords(upgraded);
    var keywordLines = keywords?.Select(k => $"[K:{k}]") ?? Enumerable.Empty<string>();

    var effects = upgraded && def.UpgradedEffects is not null
        ? def.UpgradedEffects
        : def.Effects;
    var effectText = FormatEffects(effects, context);

    var allLines = keywordLines.Concat(effectText.Split('\n'));
    return string.Join("\n", allLines.Where(l => !string.IsNullOrEmpty(l)));
}

// 既存 Format(def, upgraded) は新 overload を Empty で呼ぶ薄いラッパに
public static string Format(CardDefinition def, bool upgraded)
    => Format(def, upgraded, CardActorContext.Empty);

// FormatEffects も context overload を追加
public static string FormatEffects(IReadOnlyList<CardEffect> effects, CardActorContext context)
{
    // ...既存ロジック流用、AmountToken に context を渡す
}

public static string FormatEffects(IReadOnlyList<CardEffect> effects)
    => FormatEffects(effects, CardActorContext.Empty);
```

- [ ] `AmountToken` (内部 helper) を context 受け取りに変更:

```csharp
private static string AmountToken(CardEffect e, CardActorContext context)
{
    if (!string.IsNullOrEmpty(e.AmountSource))
    {
        var label = AmountSourceJp(e.AmountSource);
        return $"[V:X|{label}]";
    }

    int adjusted = AdjustAmount(e, context);
    if (adjusted == e.Amount) return $"[N:{e.Amount}]";
    var modifier = adjusted > e.Amount ? "up" : "down";
    return $"[N:{adjusted}|{modifier}]";
}

private static int AdjustAmount(CardEffect e, CardActorContext ctx)
{
    if (e.Action == "attack")
    {
        int withStr = e.Amount + ctx.Strength;
        return ctx.Weak > 0 ? (int)(withStr * 0.75) : withStr;
    }
    if (e.Action == "block")
    {
        return e.Amount + ctx.Dexterity;
    }
    return e.Amount;  // 他 action は当面比較対象外
}
```

- [ ] テスト緑。

### Step 2.3: 既存テスト互換性チェック

- [ ] 10.5.B で追加した既存 27 件のテスト (no context、CardActorContext.Empty で動く) が緑のまま。
- [ ] `dotnet test --filter FullyQualifiedName~CardTextFormatterTests` 全件緑。

---

## Task 3: BattleCardInstanceDto に adjustedDescription を追加

**Files:**
- Modify: `src/Server/Dtos/BattleCardInstanceDto.cs`
- Modify: `src/Server/Services/BattleStateDtoMapper.cs`
- Modify (or Create): `tests/Server.Tests/Services/BattleStateDtoMapperTests.cs`

### Step 3.1: DTO 拡張

- [ ] `BattleCardInstanceDto` record に optional フィールド追加:

```csharp
public sealed record BattleCardInstanceDto(
    string InstanceId,
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride,
    string? AdjustedDescription = null,
    string? AdjustedUpgradedDescription = null);
```

末尾 default null で既存呼出互換維持。

### Step 3.2: BattleStateDtoMapper で hero context 計算 + 各 card に description 注入

- [ ] hero (caster) の statuses を `CardActorContext` に変換するヘルパ:

```csharp
private static CardActorContext BuildHeroContext(BattleState state)
{
    var hero = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
    if (hero is null) return CardActorContext.Empty;
    return new CardActorContext(
        Strength: hero.GetStatus("strength"),
        Weak: hero.GetStatus("weak"),
        Dexterity: hero.GetStatus("dexterity"));
}
```

- [ ] hand / drawPile / discardPile / exhaustPile / summonHeld / powerCards 各 instance を BattleCardInstanceDto に変換する箇所で、catalog から `CardDefinition` を引いて context-aware formatter を呼ぶ:

```csharp
private static BattleCardInstanceDto MapCard(
    BattleCardInstance c, DataCatalog data, CardActorContext ctx)
{
    string? adjusted = null;
    string? adjustedUp = null;
    if (data.Cards.TryGetValue(c.CardDefinitionId, out var def))
    {
        adjusted = CardTextFormatter.Format(def, upgraded: false, ctx);
        if (def.IsUpgradable)
            adjustedUp = CardTextFormatter.Format(def, upgraded: true, ctx);
    }
    return new BattleCardInstanceDto(
        c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride,
        adjusted, adjustedUp);
}
```

そして `MapState` 内で `var ctx = BuildHeroContext(state);` を一度だけ計算し、各 pile の map で再利用。

### Step 3.3: テスト追加

- [ ] strength=2 の hero がいる state で、hand 内のカードの adjustedDescription に `[N:N|up]` が含まれることを assert。
- [ ] weak=1 の hero で `[N:N|down]` が含まれること。
- [ ] no buff の hero で `|up` `|down` が含まれないこと。

```csharp
[Fact]
public void Map_emits_up_marker_when_hero_has_strength()
{
    var hero = HeroWith(strength: 2);
    var card = new BattleCardInstance("inst1", "strike", IsUpgraded: false, CostOverride: null);
    var state = MakeStateWithHandHeroAndCard(hero, card);

    var dto = BattleStateDtoMapper.Map(state, _catalog);

    var handCard = dto.Hand.First(c => c.InstanceId == "inst1");
    Assert.NotNull(handCard.AdjustedDescription);
    Assert.Contains("|up]", handCard.AdjustedDescription!);
}
```

(既存テスト fixture / helper があれば活用)

- [ ] 全 Server テスト緑。

---

## Task 4: Client types に adjustedDescription 追加

**Files:**
- Modify: `src/Client/src/api/types.ts`

- [ ] `BattleCardInstanceDto` 型に 2 フィールド追加:

```typescript
export type BattleCardInstanceDto = {
  instanceId: string
  cardDefinitionId: string
  isUpgraded: boolean
  costOverride: number | null
  adjustedDescription: string | null
  adjustedUpgradedDescription: string | null
}
```

`npx tsc --noEmit` でビルド確認。既存 vitest が緑のまま (no consumer 増えない)。

---

## Task 5: Client CardDesc を up/down 描画対応 (TDD)

**Files:**
- Modify: `src/Client/src/components/CardDesc.tsx`
- Modify: `src/Client/src/components/CardDesc.css`
- Modify: `src/Client/src/components/CardDesc.test.tsx`

### Step 5.1: テスト追加

```tsx
it('renders [N:7|up] with up class', () => {
  const { container } = render(<CardDesc text="敵に [N:7|up] ダメージ。" />)
  const num = container.querySelector('.card-desc-num')
  expect(num).toBeInTheDocument()
  expect(num?.classList.contains('card-desc-num--up')).toBe(true)
  expect(num?.textContent).toBe('7')
})

it('renders [N:3|down] with down class', () => {
  const { container } = render(<CardDesc text="敵に [N:3|down] ダメージ。" />)
  const num = container.querySelector('.card-desc-num')
  expect(num?.classList.contains('card-desc-num--down')).toBe(true)
  expect(num?.textContent).toBe('3')
})

it('renders [N:5] (no modifier) with default class only', () => {
  const { container } = render(<CardDesc text="敵に [N:5] ダメージ。" />)
  const num = container.querySelector('.card-desc-num')
  expect(num?.classList.contains('card-desc-num--up')).toBe(false)
  expect(num?.classList.contains('card-desc-num--down')).toBe(false)
})
```

- [ ] vitest fail 確認。

### Step 5.2: パーサ更新

`MARKER_RE` は既に `[type:value(|extra)?]` 構造を解析できる。`renderMarker` の N 分岐で extra を読んで class を切替:

```tsx
case 'N': {
  const cls = ['card-desc-num']
  if (extra === 'up') cls.push('card-desc-num--up')
  else if (extra === 'down') cls.push('card-desc-num--down')
  return <span key={key} className={cls.join(' ')}>{value}</span>
}
```

### Step 5.3: CSS

`src/Client/src/components/CardDesc.css`:

```css
/* battle 中 actor の statuses で base より上振れ → 赤 (= 強くなった)。 */
.card-desc-num--up {
  color: #ff7a7a;
}
/* base より下振れ → 青 (= 弱くなった)。 */
.card-desc-num--down {
  color: #7aa8ff;
}
```

(既存 `.card-desc-num` の黄色は `--up` / `--down` で上書き可能、`color` プロパティの specificity 等しいので **後勝ち**を保つために `--up` / `--down` を黄色定義より下に書く。)

- [ ] vitest 全件緑。

---

## Task 6: Client dtoAdapter で adjustedDescription を優先

**Files:**
- Modify: `src/Client/src/screens/battleScreen/dtoAdapter.ts`
- Modify: `src/Client/src/screens/battleScreen/dtoAdapter.test.ts` (もし affected)

### Step 6.1: toHandCardDemo に adjustedDescription を流し込む

`toHandCardDemo` は現在 catalog の `def.description` / `def.upgradedDescription` を `description` / `upgradedDescription` フィールドに渡している。これを `card.adjustedDescription` / `card.adjustedUpgradedDescription` が non-null ならそちらを優先するように変更:

```typescript
return {
  // ...既存フィールド
  description: card.adjustedDescription ?? def?.description ?? '',
  upgradedDescription: card.adjustedUpgradedDescription ?? def?.upgradedDescription ?? null,
}
```

### Step 6.2: pile 内カード表示 (PileModal 等) も同様

PileModal で表示する Card に `description` / `upgradedDescription` を渡す箇所も、handCardの場合と同じパターン (battle 中の cardInstance なら adjusted を優先)。BattleScreen 内 PileModal 呼出 + Card props 経路を確認、必要なら同等修正。

- [ ] vitest 緑、`npx tsc --noEmit` パス。

---

## Task 7: Self-review + 1 commit + push

### 1. Spec coverage

- [ ] §1-3 Q3: battle 中のみ赤/青比較、catalog は無修飾 ✓
- [ ] adjustedDescription を BattleCardInstanceDto に流し、Client が優先表示 ✓
- [ ] up/down CSS class が `.card-desc-num--up` / `--down` で展開 ✓

### 2. Engine 非汚染

- [ ] BattleEngine / EffectApplier に touch していない ✓

### 3. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Core 1066 + 新 ~6 / Server 195 + 新 ~3)
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑 (152 + 新 ~3)

### 4. Commit + push

- [ ] 1 commit (`feat(core/server/client): battle-context up/down color compare on numbers (Phase 10.5.C)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] `CardActorContext` record が Core にあり、formatter が context-aware に `[N:N|up]` / `[N:N|down]` を emit
- [ ] `BattleCardInstanceDto.AdjustedDescription` / `AdjustedUpgradedDescription` が Server で計算され Client に届く
- [ ] Client `CardDesc` が `|up` `|down` 修飾子に応じて赤 / 青クラスを付与
- [ ] dtoAdapter で adjusted があれば優先採用
- [ ] 既存テスト全件緑、新テスト全件緑
- [ ] commit + push 済み

## 今回スコープ外

- Variable X (`[V:X|...]`) の up/down: 10.5.D で AmountSource engine 評価実装後
- Power trigger 効果中の amount 変化: 10.5.E
- relic / potion description の context 比較: 当面 catalog (静的) のみ、必要なら別途

## ロールバック

問題があれば BattleStateDtoMapper の `MapCard` を以前の (adjusted=null) 形に revert すれば Client は dtoAdapter のフォールバックで catalog description を使う形に自動で戻る。

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5B-formatter-v2.md`](2026-05-01-phase10-5B-formatter-v2.md)
