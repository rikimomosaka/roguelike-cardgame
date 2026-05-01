# Phase 10.5.M — Dev Editor UX 全面刷新 (構造化フォーム + ライブプレビュー + カード削除)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** dev menu のカード編集 UX を全面刷新する。
1. **JSON テキストエリアを廃止**して、ボタン / プルダウン / 数字入力の**構造化フォーム** に置き換え
2. **ゲーム本編と同じ formatter** をライブプレビュー表示 (debounce server call)
3. **ゲームと同じ `<Card>` 描画**でカードの見た目をライブプレビュー表示 (normal / upgraded 並列)
4. **カード自体の削除**機能を追加 (override + base 両方からの削除、backup 取得)

**Architecture:**
- **Server**:
  - `POST /api/dev/cards/preview` (spec を受け取って auto-text 返却、formatter は CardTextFormatter)
  - `DELETE /api/dev/cards/{id}` (override + optional base 削除、backup 取得)
  - `GET /api/dev/meta` (formatter / engine が知っている enum 値リストを返す: actions / scopes / sides / piles / triggers / amountSources / keywords / statuses / cardTypes / rarities)
- **Client**:
  - `CardSpecForm` 新コンポーネント (構造化フォームのトップ)
  - `EffectListEditor` / `EffectEditor` (effects 配列を行ごとに編集、action 種別で動的に field 切替)
  - `KeywordSelector` (multi-select)
  - `FormatterPreview` (debounce 200ms で preview API)
  - DevCardsScreen から textarea を撤去、CardSpecForm に差し替え
  - 「Delete Card」ボタン + 確認ダイアログ (override only / base 両方の場合で警告差別化)
- **DEV ガード**: 既存と同じ三段ゲート

**Tech Stack:** ASP.NET Core 10、xUnit、React 19 + TypeScript、vitest。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-3 Q9 (formatter preview の必要性は本フェーズで顕在化)、§5 (Dev menu)

**スコープ外:**
- `select="choose"` の player input ランタイム実装 → 将来 (10.5.M2 仮称)
- フォーム validation の Ajv 厳密化 → 当面 type 指定 + range / enum で代替
- relic / potion / enemy / unit エディタ → 10.5.L
- 立ち絵 / アート選択 UI → 別途

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Server/Controllers/DevCardsController.cs` | Modify | preview / delete endpoint 追加 |
| `src/Server/Controllers/DevMetaController.cs` | Create | enum 値リスト endpoint |
| `tests/Server.Tests/Controllers/DevCardsPreviewTests.cs` | Create | preview / delete のテスト |
| `tests/Server.Tests/Controllers/DevMetaControllerTests.cs` | Create | meta endpoint テスト |
| `src/Client/src/api/dev.ts` | Modify | preview / delete / meta 関数追加 |
| `src/Client/src/screens/dev/CardSpecForm.tsx` | Create | 構造化フォーム本体 |
| `src/Client/src/screens/dev/CardSpecForm.css` | Create | スタイル |
| `src/Client/src/screens/dev/EffectListEditor.tsx` | Create | effects[] 編集 |
| `src/Client/src/screens/dev/EffectEditor.tsx` | Create | 単一 effect 編集 |
| `src/Client/src/screens/dev/KeywordSelector.tsx` | Create | keyword multi-select |
| `src/Client/src/screens/dev/FormatterPreview.tsx` | Create | debounced auto-text preview |
| `src/Client/src/screens/dev/CardVisualPreview.tsx` | Create | ゲームと同じ `<Card>` 描画でカード見た目ライブプレビュー |
| `src/Client/src/screens/dev/DevSpecTypes.ts` | Create | フォーム用の型定義 (CardSpec, Effect, etc.) |
| `src/Client/src/screens/DevCardsScreen.tsx` | Modify | textarea を CardSpecForm に置換、Delete Card ボタン追加 |
| `src/Client/src/screens/DevCardsScreen.css` | Modify | レイアウト調整 |
| `src/Client/src/screens/DevCardsScreen.test.tsx` | Modify | smoke テスト更新 (textarea idiom 廃止) |

---

## Conventions

- **TDD strictly.** Server endpoint を先、UI components は smoke test で十分。
- **Build clean.** 警告 0 / エラー 0、既存 lint パス維持。
- **DEV ガード厳守.** 全 mutation / preview / meta endpoint も in-controller `IsDevelopment()` で 404。
- **Pure form-state.** CardSpecForm は controlled component (上から `spec` props、`onChange` で親に伝播)。spec 表現は client 側の TS 型 `CardSpec` で持ち、保存直前に JSON シリアライズ。
- **Effect の field 表示は action 連動.** action=attack なら scope/side/amount のみ、action=addCard なら cardRefId/pile/amount、等。Action ごとの field map は `EFFECT_ACTION_FIELDS: Record<Action, FieldName[]>` で集約。
- **Live preview は 200ms debounce.** 入力途中の連発を抑制。
- **「保存前は raw JSON 経由しない」.** Form state → spec object → JSON.stringify は save 時のみ。preview は spec object をそのまま POST。

---

## Task 1: Server `POST /api/dev/cards/preview` endpoint (TDD)

**Files:**
- Modify: `src/Server/Controllers/DevCardsController.cs`
- Create: `tests/Server.Tests/Controllers/DevCardsPreviewTests.cs`

### Step 1.1: 設計

```
POST /api/dev/cards/preview
body: { spec: <CardSpec object>, upgraded: bool }
→ { description: string }    // marker 入り、Client 側 CardDesc で renders
```

実装: spec から `CardDefinition` を組み立てて `CardTextFormatter.Format(def, upgraded)` を呼ぶ。

### Step 1.2: テスト

```csharp
[Fact]
public async Task Preview_returns_formatted_description_with_markers()
{
    var body = new {
        spec = new {
            rarity = 1, cardType = "Attack", cost = 1,
            effects = new object[] {
                new { action = "attack", scope = "single", side = "enemy", amount = 6 },
            },
        },
        upgraded = false,
    };
    var resp = await _client.PostAsJsonAsync("/api/dev/cards/preview", body);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    var json = await resp.Content.ReadAsStringAsync();
    Assert.Contains("[N:6]", json);
    Assert.Contains("敵 1 体に", json);
}

[Fact]
public async Task Preview_uses_upgraded_effects_when_upgraded_true()
{
    // upgradedEffects に amount=9 → preview で [N:9] が入る
}

[Fact]
public async Task Preview_returns_404_in_production() { /* Production fixture */ }
```

### Step 1.3: 実装

- [ ] DevCardsController に追加:

```csharp
public sealed record PreviewRequest(JsonElement Spec, bool Upgraded);

[HttpPost("cards/preview")]
public IActionResult Preview([FromBody] PreviewRequest body)
{
    if (!_env.IsDevelopment()) return NotFound();
    try
    {
        // spec を JSON 文字列化して CardJsonLoader.ParseSpec 相当を呼ぶ。
        // 既存 ParseSpec が private ならこのフェーズで internal 公開 or
        // CardJsonLoader.ParseSpecJson(string) のような専用 API を用意。
        var def = BuildPreviewDefinition(body.Spec);
        var desc = CardTextFormatter.Format(def, body.Upgraded);
        return Ok(new { description = desc });
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}

private static CardDefinition BuildPreviewDefinition(JsonElement spec)
{
    // id/name は dummy で。formatter は spec の中身 (rarity/cost/effects/etc) しか見ない
    var dummy = new JsonObject
    {
        ["id"] = "preview",
        ["name"] = "preview",
        ["activeVersion"] = "v1",
        ["versions"] = new JsonArray
        {
            new JsonObject
            {
                ["version"] = "v1",
                ["spec"] = JsonNode.Parse(spec.GetRawText()),
            }
        }
    };
    return CardJsonLoader.Parse(dummy.ToJsonString());
}
```

- [ ] テスト緑。

---

## Task 2: Server `DELETE /api/dev/cards/{id}` endpoint (TDD)

**Files:**
- Modify: `src/Server/Controllers/DevCardsController.cs`
- Create: tests in `DevCardsControllerMutationTests.cs` (or 既存 mutation tests に追加)

### Step 2.1: 設計

```
DELETE /api/dev/cards/{id}
query: ?alsoBase=true (オプション、デフォルト false)
→
  alsoBase=false: override file のみ削除 (base に存在する card は復活)
  alsoBase=true: base file も削除 (data-local/backups/cards/ にバックアップ)
404: card not found in either
```

### Step 2.2: テスト

```csharp
[Fact]
public async Task Delete_override_only_keeps_base()
{
    // 1) override 作成、2) DELETE without alsoBase, 3) GET で base のみ復活
}

[Fact]
public async Task Delete_with_alsoBase_removes_base_file()
{
    // 1) DELETE with ?alsoBase=true, 2) base file がなくなる、3) backup が残る
}

[Fact]
public async Task Delete_unknown_id_returns_404()
{
    var resp = await _client.DeleteAsync("/api/dev/cards/nonexistent_xyz");
    Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
}

[Fact]
public async Task Delete_returns_404_in_production() { /* prod */ }
```

### Step 2.3: 実装

```csharp
[HttpDelete("cards/{id}")]
public IActionResult DeleteCard(string id, [FromQuery] bool alsoBase = false)
{
    if (!_env.IsDevelopment()) return NotFound();

    var hasOverride = _writer.ReadOverride(id) is not null;
    var hasBase = _writer.ReadBase(id) is not null;
    if (!hasOverride && !hasBase) return NotFound();

    _writer.DeleteOverride(id);
    if (alsoBase && hasBase)
    {
        _writer.DeleteBaseWithBackup(id);  // 新メソッド
    }
    _provider.Rebuild();
    return Ok(new { deleted = id, alsoBase });
}
```

- [ ] `DevCardWriter.DeleteBaseWithBackup(string cardId)` を追加 (`File.Copy → File.Delete`)。
- [ ] テスト緑。

---

## Task 3: Server `GET /api/dev/meta` endpoint (TDD)

**Files:**
- Create: `src/Server/Controllers/DevMetaController.cs`
- Create: `tests/Server.Tests/Controllers/DevMetaControllerTests.cs`

### Step 3.1: 設計

Form の dropdown / multi-select の選択肢を Server から動的に取得することで、Core 側の追加 (status / keyword / action) が UI に自動反映される。

```
GET /api/dev/meta
→
{
  cardTypes: ["Attack","Skill","Power","Curse","Status","Unit"],
  rarities: [{ value: 0, label: "Promo" }, { value: 1, label: "Common" }, ...],
  effectActions: ["attack","block","buff","debuff","heal","draw","drawCards","discard",
                  "exhaustSelf","retainSelf","gainEnergy","exhaustCard","upgrade","summon",
                  "selfDamage","addCard","recoverFromDiscard","gainMaxEnergy"],
  effectScopes: ["Self","Single","Random","All"],
  effectSides: ["Enemy","Ally"],
  piles: ["hand","draw","discard","exhaust"],
  selectModes: ["random","choose","all"],
  triggers: ["OnTurnStart","OnPlayCard","OnDamageReceived","OnCombo"],
  amountSources: ["handCount","drawPileCount","discardPileCount","exhaustPileCount",
                   "selfHp","selfHpLost","selfBlock","comboCount","energy","powerCardCount"],
  keywords: [{ id: "wild", name: "ワイルド", description: "..." },
             { id: "superwild", name: "スーパーワイルド", description: "..." }],
  statuses: [{ id: "weak", jp: "脱力" }, { id: "vulnerable", jp: "脆弱" }, ...],
}
```

### Step 3.2: テスト

```csharp
[Fact]
public async Task Meta_returns_lists_in_dev()
{
    var resp = await _client.GetAsync("/api/dev/meta");
    var json = await resp.Content.ReadAsStringAsync();
    Assert.Contains("\"effectActions\"", json);
    Assert.Contains("attack", json);
    Assert.Contains("addCard", json);
    Assert.Contains("\"keywords\"", json);
    Assert.Contains("wild", json);
}

[Fact]
public async Task Meta_returns_404_in_production() { /* prod */ }
```

### Step 3.3: 実装

- [ ] `DevMetaController.cs` で 1 endpoint 実装、Core から hardcoded list を返す:

```csharp
[ApiController]
[Route("api/dev")]
public sealed class DevMetaController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    public DevMetaController(IWebHostEnvironment env) => _env = env;

    [HttpGet("meta")]
    public IActionResult GetMeta()
    {
        if (!_env.IsDevelopment()) return NotFound();
        return Ok(new
        {
            cardTypes = new[] { "Attack","Skill","Power","Curse","Status","Unit" },
            rarities = new object[] {
                new { value = 0, label = "Promo" },
                new { value = 1, label = "Common" },
                new { value = 2, label = "Rare" },
                new { value = 3, label = "Epic" },
                new { value = 4, label = "Legendary" },
                new { value = 5, label = "Token" },
            },
            effectActions = new[] {
                "attack","block","buff","debuff","heal","draw","drawCards","discard",
                "exhaustSelf","retainSelf","gainEnergy","exhaustCard","upgrade","summon",
                "selfDamage","addCard","recoverFromDiscard","gainMaxEnergy",
            },
            effectScopes = new[] { "Self","Single","Random","All" },
            effectSides = new[] { "Enemy","Ally" },
            piles = new[] { "hand","draw","discard","exhaust" },
            selectModes = new[] { "random","choose","all" },
            triggers = new[] { "OnTurnStart","OnPlayCard","OnDamageReceived","OnCombo" },
            amountSources = new[] {
                "handCount","drawPileCount","discardPileCount","exhaustPileCount",
                "selfHp","selfHpLost","selfBlock","comboCount","energy","powerCardCount",
            },
            keywords = CardKeywords.All.Values.Select(k => new { id = k.Id, name = k.Name, description = k.Description }).ToArray(),
            statuses = new object[] {
                new { id = "weak", jp = "脱力" },
                new { id = "vulnerable", jp = "脆弱" },
                new { id = "strength", jp = "筋力" },
                new { id = "dexterity", jp = "敏捷" },
                new { id = "poison", jp = "毒" },
                new { id = "omnistrike", jp = "全体攻撃" },
            },
        });
    }
}
```

- [ ] テスト緑。

---

## Task 4: Client API helpers + 型 (DevSpecTypes.ts)

**Files:**
- Modify: `src/Client/src/api/dev.ts`
- Create: `src/Client/src/screens/dev/DevSpecTypes.ts`

### Step 4.1: api/dev.ts

```typescript
export type DevMeta = {
  cardTypes: string[]
  rarities: { value: number; label: string }[]
  effectActions: string[]
  effectScopes: string[]
  effectSides: string[]
  piles: string[]
  selectModes: string[]
  triggers: string[]
  amountSources: string[]
  keywords: { id: string; name: string; description: string }[]
  statuses: { id: string; jp: string }[]
}

export async function fetchDevMeta(): Promise<DevMeta> {
  const r = await fetch('/api/dev/meta')
  if (!r.ok) throw new Error(`fetchDevMeta failed: ${r.status}`)
  return await r.json()
}

export async function previewDescription(spec: unknown, upgraded: boolean): Promise<string> {
  const r = await fetch('/api/dev/cards/preview', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ spec, upgraded }),
  })
  if (!r.ok) throw new Error(`preview failed: ${r.status} ${await r.text()}`)
  const j = await r.json()
  return j.description as string
}

export async function deleteCard(id: string, alsoBase: boolean): Promise<void> {
  const r = await fetch(`/api/dev/cards/${id}?alsoBase=${alsoBase}`, { method: 'DELETE' })
  if (!r.ok) throw new Error(`deleteCard failed: ${r.status}`)
}
```

### Step 4.2: DevSpecTypes.ts

```typescript
export type CardEffect = {
  action: string
  scope: string
  side: string | null
  amount: number
  name: string | null
  unitId: string | null
  comboMin: number | null
  pile: string | null
  battleOnly: boolean
  cardRefId: string | null
  select: string | null
  amountSource: string | null
  trigger: string | null
}

export type CardSpec = {
  rarity: number
  cardType: string
  cost: number | null
  upgradedCost: number | null
  effects: CardEffect[]
  upgradedEffects: CardEffect[] | null
  description: string | null
  upgradedDescription: string | null
  keywords: string[] | null
  upgradedKeywords: string[] | null
}

export function emptyEffect(): CardEffect { /* default */ }
export function emptySpec(): CardSpec { /* default */ }
export function parseSpec(json: string): CardSpec { /* JSON.parse + 正規化 */ }
export function specToJson(spec: CardSpec): string { /* JSON.stringify、null フィールド省略 */ }
```

---

## Task 5: Client form components (TDD: smoke)

**Files:**
- Create: `src/Client/src/screens/dev/CardSpecForm.tsx`
- Create: `src/Client/src/screens/dev/CardSpecForm.css`
- Create: `src/Client/src/screens/dev/EffectListEditor.tsx`
- Create: `src/Client/src/screens/dev/EffectEditor.tsx`
- Create: `src/Client/src/screens/dev/KeywordSelector.tsx`
- Create: `src/Client/src/screens/dev/FormatterPreview.tsx`

### Step 5.1: EffectEditor (action ごとの動的 field)

action ごとに表示する field を `EFFECT_ACTION_FIELDS` で管理:

```typescript
const EFFECT_ACTION_FIELDS: Record<string, (keyof CardEffect)[]> = {
  attack: ['scope', 'side', 'amount', 'amountSource', 'trigger'],
  block: ['scope', 'side', 'amount', 'amountSource', 'trigger'],
  buff: ['scope', 'side', 'name', 'amount', 'comboMin', 'trigger'],
  debuff: ['scope', 'side', 'name', 'amount', 'comboMin', 'trigger'],
  heal: ['scope', 'side', 'amount', 'trigger'],
  draw: ['amount', 'amountSource', 'trigger'],
  drawCards: ['amount', 'amountSource', 'trigger'],
  discard: ['scope', 'amount', 'select', 'trigger'],
  exhaustSelf: [],
  retainSelf: [],
  gainEnergy: ['amount', 'trigger'],
  gainMaxEnergy: ['amount', 'trigger'],
  exhaustCard: ['amount', 'pile', 'trigger'],
  upgrade: ['amount', 'pile', 'trigger'],
  summon: ['unitId', 'amount', 'trigger'],
  selfDamage: ['amount', 'trigger'],
  addCard: ['cardRefId', 'amount', 'pile', 'trigger'],
  recoverFromDiscard: ['amount', 'pile', 'select', 'trigger'],
}
```

```tsx
function EffectEditor({ effect, meta, onChange, onRemove, allCardIds }) {
  const fields = EFFECT_ACTION_FIELDS[effect.action] ?? []

  return (
    <div className="effect-editor">
      <div className="effect-editor__row">
        <label>Action <select value={effect.action} onChange={e => onChange({ ...effect, action: e.target.value })}>
          {meta.effectActions.map(a => <option key={a} value={a}>{a}</option>)}
        </select></label>
        <button className="effect-editor__remove" onClick={onRemove}>✕</button>
      </div>
      <div className="effect-editor__fields">
        {fields.includes('scope') && (
          <label>Scope <select value={effect.scope} onChange={e => onChange({ ...effect, scope: e.target.value })}>
            {meta.effectScopes.map(s => <option key={s} value={s}>{s}</option>)}
          </select></label>
        )}
        {fields.includes('side') && (
          <label>Side <select value={effect.side ?? ''} onChange={e => onChange({ ...effect, side: e.target.value || null })}>
            <option value="">(none)</option>
            {meta.effectSides.map(s => <option key={s} value={s}>{s}</option>)}
          </select></label>
        )}
        {fields.includes('amount') && (
          <label>Amount <input type="number" value={effect.amount} onChange={e => onChange({ ...effect, amount: parseInt(e.target.value, 10) || 0 })} /></label>
        )}
        {fields.includes('amountSource') && (
          <label>Amount Source <select value={effect.amountSource ?? ''} onChange={e => onChange({ ...effect, amountSource: e.target.value || null })}>
            <option value="">(literal)</option>
            {meta.amountSources.map(s => <option key={s} value={s}>{s}</option>)}
          </select></label>
        )}
        {fields.includes('name') && (
          <label>Status <select value={effect.name ?? ''} onChange={e => onChange({ ...effect, name: e.target.value || null })}>
            <option value="">(none)</option>
            {meta.statuses.map(s => <option key={s.id} value={s.id}>{s.id} ({s.jp})</option>)}
          </select></label>
        )}
        {fields.includes('pile') && (
          <label>Pile <select value={effect.pile ?? ''} onChange={e => onChange({ ...effect, pile: e.target.value || null })}>
            <option value="">(none)</option>
            {meta.piles.map(p => <option key={p} value={p}>{p}</option>)}
          </select></label>
        )}
        {fields.includes('select') && (
          <label>Select <select value={effect.select ?? ''} onChange={e => onChange({ ...effect, select: e.target.value || null })}>
            <option value="">(none)</option>
            {meta.selectModes.map(m => <option key={m} value={m}>{m}</option>)}
          </select></label>
        )}
        {fields.includes('cardRefId') && (
          <label>Card Ref <select value={effect.cardRefId ?? ''} onChange={e => onChange({ ...effect, cardRefId: e.target.value || null })}>
            <option value="">(none)</option>
            {allCardIds.map(id => <option key={id} value={id}>{id}</option>)}
          </select></label>
        )}
        {fields.includes('trigger') && (
          <label>Trigger <select value={effect.trigger ?? ''} onChange={e => onChange({ ...effect, trigger: e.target.value || null })}>
            <option value="">(immediate)</option>
            {meta.triggers.map(t => <option key={t} value={t}>{t}</option>)}
          </select></label>
        )}
        {fields.includes('comboMin') && (
          <label>Combo Min <input type="number" value={effect.comboMin ?? ''} placeholder="(none)" onChange={e => onChange({ ...effect, comboMin: e.target.value ? parseInt(e.target.value, 10) : null })} /></label>
        )}
        {fields.includes('unitId') && (
          <label>Unit ID <input type="text" value={effect.unitId ?? ''} placeholder="ally id" onChange={e => onChange({ ...effect, unitId: e.target.value || null })} /></label>
        )}
      </div>
    </div>
  )
}
```

### Step 5.2: EffectListEditor

`effects[]` を上下に並べ、各行に EffectEditor、末尾に「+ Add Effect」ボタン:

```tsx
function EffectListEditor({ effects, meta, onChange, allCardIds, label }) {
  const updateAt = (i: number, eff: CardEffect) => {
    const next = [...effects]
    next[i] = eff
    onChange(next)
  }
  const removeAt = (i: number) => onChange(effects.filter((_, j) => j !== i))
  const addNew = () => onChange([...effects, emptyEffect()])
  const moveUp = (i: number) => { /* swap */ }
  const moveDown = (i: number) => { /* swap */ }

  return (
    <div className="effect-list">
      <h4>{label} ({effects.length})</h4>
      {effects.map((eff, i) => (
        <div key={i} className="effect-row">
          <div className="effect-row__order">
            <button onClick={() => moveUp(i)} disabled={i === 0}>↑</button>
            <button onClick={() => moveDown(i)} disabled={i === effects.length - 1}>↓</button>
          </div>
          <EffectEditor effect={eff} meta={meta} allCardIds={allCardIds}
            onChange={(e) => updateAt(i, e)}
            onRemove={() => removeAt(i)} />
        </div>
      ))}
      <button onClick={addNew}>+ Add Effect</button>
    </div>
  )
}
```

### Step 5.3: KeywordSelector

`meta.keywords` から checkbox 一覧を出して、選択された id を `string[]` として onChange。

```tsx
function KeywordSelector({ value, meta, onChange, label }) {
  const toggle = (id: string) => {
    const set = new Set(value ?? [])
    if (set.has(id)) set.delete(id); else set.add(id)
    onChange(Array.from(set))
  }
  return (
    <div className="keyword-selector">
      <label>{label}</label>
      <div className="keyword-selector__checks">
        {meta.keywords.map(k => (
          <label key={k.id} title={k.description}>
            <input type="checkbox" checked={(value ?? []).includes(k.id)}
              onChange={() => toggle(k.id)} />
            {k.name}
          </label>
        ))}
      </div>
    </div>
  )
}
```

### Step 5.4a: CardVisualPreview (ゲームと同じ `<Card>` 描画)

card name / displayName / spec から `<Card>` 用 props を組んで、ゲーム本編と完全に同じ見た目で描画する。normal / upgraded 並列で表示し、休憩マスの強化プレビュー (Phase 10.5.A2 で既実装) と同じ「[before] → [after]」レイアウト。

```tsx
import { Card } from '../../components/Card'
import type { CardRarity, CardType } from '../../components/Card'
import type { CardSpec } from './DevSpecTypes'

type Props = {
  cardId: string
  cardName: string
  displayName: string | null
  spec: CardSpec
  /** preview API から得た auto-text (description 用、override が無ければこれが使われる) */
  normalDescription: string
  upgradedDescription: string
}

const RARITY_NUM_TO_CHAR: Record<number, CardRarity> = {
  0: 'c', 1: 'c', 2: 'r', 3: 'e', 4: 'l', 5: 't',
}

const CARDTYPE_TO_LOWER: Record<string, CardType> = {
  Attack: 'attack', Skill: 'skill', Power: 'power',
  Curse: 'curse', Status: 'status', Unit: 'unit',
}

export function CardVisualPreview({
  cardId, cardName, displayName, spec,
  normalDescription, upgradedDescription,
}: Props) {
  const rarity = RARITY_NUM_TO_CHAR[spec.rarity] ?? 'c'
  const type = CARDTYPE_TO_LOWER[spec.cardType] ?? 'attack'
  const isUpgradable = spec.upgradedCost !== null
    || (spec.upgradedEffects !== null && spec.upgradedEffects.length > 0)
    || (spec.upgradedKeywords !== null && spec.upgradedKeywords.length > 0)

  const normalCost = spec.cost ?? 'X'
  const upgradedCost = spec.upgradedCost ?? spec.cost ?? 'X'

  return (
    <div className="dev-card-visual-preview">
      <div className="dev-card-visual-preview__panel">
        <h5>Normal</h5>
        <Card
          name={displayName ?? cardName}
          cost={normalCost}
          type={type}
          rarity={rarity}
          upgraded={false}
          description={spec.description ?? normalDescription}
          upgradedDescription={spec.upgradedDescription ?? upgradedDescription}
          width={128}
        />
      </div>
      {isUpgradable && (
        <>
          <span className="dev-card-visual-preview__arrow" aria-hidden="true">→</span>
          <div className="dev-card-visual-preview__panel">
            <h5>Upgraded</h5>
            <Card
              name={displayName ?? cardName}
              cost={upgradedCost}
              type={type}
              rarity={rarity}
              upgraded={true}
              description={spec.description ?? normalDescription}
              upgradedDescription={spec.upgradedDescription ?? upgradedDescription}
              width={128}
            />
          </div>
        </>
      )}
    </div>
  )
}
```

CSS: `.dev-card-visual-preview` を rest screen の `.rs-confirm-preview` と同様の flex 中央寄せで配置。

### Step 5.4b: FormatterPreview (debounced)

```tsx
function FormatterPreview({ spec, upgraded, cardNames }) {
  const [text, setText] = useState<string>('...')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    const t = window.setTimeout(async () => {
      try {
        const result = await previewDescription(spec, upgraded)
        if (!cancelled) { setText(result); setError(null) }
      } catch (e) {
        if (!cancelled) { setError(String(e)); setText('') }
      }
    }, 200)
    return () => { cancelled = true; window.clearTimeout(t) }
  }, [JSON.stringify(spec), upgraded])

  if (error) return <div className="dev-error">Preview error: {error}</div>
  return <div className="formatter-preview"><CardDesc text={text} cardNames={cardNames} /></div>
}
```

`spec` を deps にすると参照比較で毎 render fire するので、`JSON.stringify(spec)` を deps key にする (簡易、性能十分)。

### Step 5.5: CardSpecForm (本体)

トップ階層: rarity / cardType / cost / upgradedCost / upgradable / description (optional override) / upgradedDescription / keywords / upgradedKeywords / effects / upgradedEffects

```tsx
function CardSpecForm({ spec, meta, allCardIds, cardNames, onChange }) {
  const set = (patch: Partial<CardSpec>) => onChange({ ...spec, ...patch })

  return (
    <div className="card-spec-form">
      <div className="card-spec-form__row">
        <label>Rarity <select value={spec.rarity} onChange={e => set({ rarity: parseInt(e.target.value, 10) })}>
          {meta.rarities.map(r => <option key={r.value} value={r.value}>{r.label}</option>)}
        </select></label>
        <label>Card Type <select value={spec.cardType} onChange={e => set({ cardType: e.target.value })}>
          {meta.cardTypes.map(t => <option key={t} value={t}>{t}</option>)}
        </select></label>
        <label>Cost <input type="number" value={spec.cost ?? ''} placeholder="(unplayable)"
          onChange={e => set({ cost: e.target.value ? parseInt(e.target.value, 10) : null })} /></label>
        <label>Upgraded Cost <input type="number" value={spec.upgradedCost ?? ''} placeholder="(=cost)"
          onChange={e => set({ upgradedCost: e.target.value ? parseInt(e.target.value, 10) : null })} /></label>
      </div>

      <KeywordSelector value={spec.keywords} meta={meta} label="Keywords" onChange={(v) => set({ keywords: v })} />
      <KeywordSelector value={spec.upgradedKeywords} meta={meta} label="Upgraded Keywords" onChange={(v) => set({ upgradedKeywords: v })} />

      <EffectListEditor effects={spec.effects} meta={meta} allCardIds={allCardIds}
        label="Effects" onChange={(e) => set({ effects: e })} />
      <EffectListEditor effects={spec.upgradedEffects ?? []} meta={meta} allCardIds={allCardIds}
        label="Upgraded Effects" onChange={(e) => set({ upgradedEffects: e })} />

      <details>
        <summary>Description Override (optional)</summary>
        <label>Description (override) <textarea value={spec.description ?? ''}
          onChange={e => set({ description: e.target.value || null })} /></label>
        <label>Upgraded Description <textarea value={spec.upgradedDescription ?? ''}
          onChange={e => set({ upgradedDescription: e.target.value || null })} /></label>
      </details>

      <div className="card-spec-form__previews">
        <h4>Card Visual Preview</h4>
        {/* CardVisualPreview に渡す normal/upgraded の auto-text は内部で
            FormatterPreview と同じ /api/dev/cards/preview を 2 回叩く
            (debounce + 結果を state に持って render に渡す) */}
        <CardVisualPreviewWithText
          cardId={cardId}
          cardName={cardName}
          displayName={displayName}
          spec={spec}
        />
        <h4>Auto-Text (raw markers)</h4>
        <FormatterPreview spec={spec} upgraded={false} cardNames={cardNames} />
        {/* upgraded preview は upgradedEffects あれば表示、無ければ省略可 */}
        <FormatterPreview spec={spec} upgraded={true} cardNames={cardNames} />
      </div>
    </div>
  )
}
```

### Step 5.6: CSS

CardSpecForm.css でフォーム全体のスタイル。詳細は実装時。

---

## Task 6: DevCardsScreen 統合 + Delete Card UI

**Files:**
- Modify: `src/Client/src/screens/DevCardsScreen.tsx`
- Modify: `src/Client/src/screens/DevCardsScreen.css`
- Modify: `src/Client/src/screens/DevCardsScreen.test.tsx`

### Step 6.1: textarea 撤去 → CardSpecForm

DevCardDetail 内の textarea ベース editor を CardSpecForm に置換:

```tsx
function DevCardDetail({ card, ..., meta, allCardIds, cardNames }) {
  const ver = card.versions.find(v => v.version === selectedVer)!
  const [draft, setDraft] = useState<CardSpec>(parseSpec(ver.spec))
  const [label, setLabel] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    setDraft(parseSpec(ver.spec))
    setLabel('')
    setError(null)
  }, [card.id, selectedVer])

  const saveAsNew = async () => {
    setError(null); setSaving(true)
    try {
      // CardSpec を JSON-shape の素 object に
      await saveCardVersion(card.id, label || null, draft)
      onAfterMutation()
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  const deleteCard = async () => {
    const hasBaseFile = ver.spec /* assume base 確認は server に依存、UI は単純化 */
    const msg = `Delete card '${card.id}'?\n(also delete from base/source?)`
    if (!confirm(msg)) return
    const alsoBase = confirm('Also delete from base/source (committed)?')
    setError(null); setSaving(true)
    try {
      await deleteCardApi(card.id, alsoBase)
      onAfterMutation()
      onAfterDelete?.()  // 親で list refresh + 別 card 選択
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  return (
    <div className="dev-card-detail">
      <header>
        <h2>{card.name}</h2>
        <code>{card.id}</code>
        <button className="dev-card-detail__delete" onClick={deleteCard}>🗑 Delete Card</button>
      </header>
      {/* version tabs - 既存 */}
      {/* CardSpecForm */}
      <CardSpecForm spec={draft} meta={meta} allCardIds={allCardIds}
        cardNames={cardNames} onChange={setDraft} />

      <section className="dev-card-detail__editor-actions">
        <input type="text" placeholder="Label (optional)" value={label} onChange={e => setLabel(e.target.value)} />
        <button onClick={saveAsNew} disabled={saving}>Save as v{N+1}</button>
        <button onClick={switchActive} disabled={saving || selectedVer === card.activeVersion}>Set as active</button>
        <button onClick={promoteVer} disabled={saving}>Promote to source</button>
        <button onClick={deleteVersion} disabled={saving || selectedVer === card.activeVersion}>Delete this version</button>
      </section>
      {error && <div className="dev-error">{error}</div>}
    </div>
  )
}
```

### Step 6.2: meta + allCardIds の取得

DevCardsScreen トップレベルで `fetchDevMeta()` も呼んで meta を保持、props で down-pass。

### Step 6.3: テスト更新

textarea idiom が消えたので、対応 smoke test を form-based に書き換え or 削除 → 新たに「dropdown で action 切替えると amount field が見える/消える」程度の最低限テストに。

---

## Task 7: Self-review + 1 commit + push

### 1. Spec coverage

- [ ] textarea 廃止 → CardSpecForm で構造化編集 ✓
- [ ] action 連動の動的 field 切替 ✓
- [ ] keyword multi-select、status dropdown、cardRefId dropdown ✓
- [ ] live formatter preview (200ms debounce、CardDesc rendered) ✓
- [ ] **CardVisualPreview**: ゲームと同じ `<Card>` 描画で normal / upgraded 並列ライブプレビュー ✓
- [ ] Delete Card endpoint + UI 確認 dialog ✓
- [ ] Meta endpoint で enum 値供給 ✓
- [ ] DEV ガード全 endpoint で 404 ✓

### 2. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑
- [ ] `npm run build` (Client) エラーなし

### 3. Commit + push

- [ ] 1 commit (`feat(server/client): structured form editor + live preview + card deletion (Phase 10.5.M)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] DevCardsScreen の編集 UI が構造化フォーム + ライブプレビュー
- [ ] 全 effect action が UI で編集可能、status / keyword / pile / trigger / amountSource / select 等 dropdown 化
- [ ] CardKeywords の wild / superwild が KeywordSelector に出る
- [ ] DELETE /api/dev/cards/{id} 動作、UI から削除可能 (override only / alsoBase 区別)
- [ ] Meta endpoint で動的に enum リスト供給
- [ ] commit + push 済み

## 今回スコープ外

- relic / potion / enemy / unit のフォーム化 → 10.5.L
- Ajv 厳密 schema (numeric range / enum 厳密) → 必要時
- フォームのキーボードショートカット / undo → 当面なし
- 立ち絵 / icon 選択 UI → 別途

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5K-new-card.md`](2026-05-01-phase10-5K-new-card.md)
- formatter: [`2026-05-01-phase10-5B-formatter-v2.md`](2026-05-01-phase10-5B-formatter-v2.md)
