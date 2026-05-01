# Phase 10.5.K — 新規カード作成 (テンプレートクローン) 実装

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** dev menu でゼロから新カードを作成できる機能を追加する。新カードは override 層 (`data-local/dev-overrides/cards/{id}.json`) に作成され、既存カードと同じ versioning / Promote の仕組みで管理される。テンプレートとして既存カード ID を指定すればその active spec を v1 にコピー、未指定なら空 spec で作成。

**Architecture:** Server `POST /api/dev/cards` endpoint (DEV ガード)。body は `{ id, name, displayName?, templateCardId? }`。id の uniqueness を base+override 両方で validation。テンプレ指定時は当該カードの active version の spec をコピー、未指定時は最小限の default spec (rarity=Common, cardType=Skill, cost=1, effects=[])。override に versioned 形式で write、rebuild、新カード DTO を返す。Client は DevCardsScreen に "New Card" ボタンと簡易モーダル、作成後は自動選択。

**Tech Stack:** ASP.NET Core 10、xUnit、React 19 + TypeScript、vitest。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-2 (10.5.K)、§5 (Dev menu)

**スコープ外:**
- relic / potion / enemy / unit の新規作成 → 10.5.L
- ID auto-suggestion (例: name から id 生成) → 当面手動入力
- Newカード後すぐの batch import (複数同時作成) → 当面 1 個ずつ

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Server/Controllers/DevCardsController.cs` | Modify | `POST /api/dev/cards` endpoint 追加 |
| `tests/Server.Tests/Controllers/DevCardsControllerNewCardTests.cs` | Create | 4 件: 正常作成 / template クローン / id 衝突 400 / 本番 404 |
| `src/Client/src/api/dev.ts` | Modify | `createNewCard(...)` 関数追加 |
| `src/Client/src/screens/DevCardsScreen.tsx` | Modify | "New Card" ボタン + モーダル |
| `src/Client/src/screens/DevCardsScreen.css` | Modify | モーダルスタイル |
| `src/Client/src/screens/DevCardsScreen.test.tsx` | Modify | smoke test 1 件 (新規作成 → 一覧反映) |

---

## Conventions

- **TDD strictly.**
- **DEV ガード厳守.** POST も in-controller `IsDevelopment()` で 404。
- **id validation.** 半角英数 + アンダースコアのみ許可 (regex `^[a-z][a-z0-9_]*$`)、空文字や全角不可。base + override に同 id が存在したら 409 Conflict。
- **template 経路.** `templateCardId` 指定時、catalog から def を引いて、その active spec を JSON シリアライズして v1 spec として埋め込む。templateCardId が catalog に存在しない場合は 400 Bad Request。
- **default spec.** template 未指定時は以下のミニマム spec で v1 作成:
  ```json
  {
    "rarity": 1,
    "cardType": "Skill",
    "cost": 1,
    "effects": []
  }
  ```
- **後続 promote 必須.** 新カードは override にしかいないため、本番に組み込むには 10.5.J で実装済の `POST /api/dev/cards/{id}/promote` で base に転記する必要あり (このフェーズでは触れない、user 操作)。

---

## Task 1: Server `POST /api/dev/cards` endpoint (TDD)

**Files:**
- Modify: `src/Server/Controllers/DevCardsController.cs`
- Create: `tests/Server.Tests/Controllers/DevCardsControllerNewCardTests.cs`

### Step 1.1: テスト

```csharp
[Fact]
public async Task NewCard_creates_card_with_default_spec()
{
    var body = new { id = "new_skill_x", name = "新規スキル", displayName = (string?)null, templateCardId = (string?)null };
    var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

    // GET で確認: new_skill_x が一覧にいる
    var list = await GetAllCards();
    var newCard = list.FirstOrDefault(c => c.Id == "new_skill_x");
    Assert.NotNull(newCard);
    Assert.Equal("v1", newCard!.ActiveVersion);
    Assert.Single(newCard.Versions);
}

[Fact]
public async Task NewCard_clones_template_spec()
{
    var body = new { id = "strike_clone", name = "ストライククローン", templateCardId = "strike" };
    var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

    var list = await GetAllCards();
    var newCard = list.First(c => c.Id == "strike_clone");
    using var doc = JsonDocument.Parse(newCard.Versions[0].Spec);
    var spec = doc.RootElement;
    // strike の amount 6 が反映されている
    Assert.Equal(6, spec.GetProperty("effects")[0].GetProperty("amount").GetInt32());
}

[Fact]
public async Task NewCard_id_collision_returns_409()
{
    var body = new { id = "strike", name = "重複", templateCardId = (string?)null };  // strike は base に存在
    var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
    Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
}

[Fact]
public async Task NewCard_invalid_id_returns_400()
{
    var body = new { id = "Invalid-ID!", name = "x", templateCardId = (string?)null };
    var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
}

[Fact]
public async Task NewCard_unknown_template_returns_400()
{
    var body = new { id = "x", name = "x", templateCardId = "nonexistent_card" };
    var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
    Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
}

// Production tests
[Fact]
public async Task NewCard_returns_404_in_production()
{
    var body = new { id = "x", name = "x" };
    var resp = await _prodClient.PostAsJsonAsync("/api/dev/cards", body);
    Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
}
```

### Step 1.2: 実装

- [ ] DevCardsController に追加:

```csharp
public sealed record NewCardRequest(string Id, string Name, string? DisplayName, string? TemplateCardId);

[HttpPost("cards")]
public IActionResult NewCard([FromBody] NewCardRequest body)
{
    if (!_env.IsDevelopment()) return NotFound();

    // id validation
    if (string.IsNullOrEmpty(body.Id) ||
        !System.Text.RegularExpressions.Regex.IsMatch(body.Id, @"^[a-z][a-z0-9_]*$"))
        return BadRequest(new { error = "Invalid id: must match ^[a-z][a-z0-9_]*$" });

    // 既存 base / override に同 id が無いか
    var existingBase = _writer.ReadBase(body.Id);
    var existingOverride = _writer.ReadOverride(body.Id);
    if (existingBase is not null || existingOverride is not null)
        return Conflict(new { error = $"card '{body.Id}' already exists" });

    // template clone
    JsonElement specElement;
    if (!string.IsNullOrEmpty(body.TemplateCardId))
    {
        var templateBase = _writer.ReadBase(body.TemplateCardId);
        var templateOverride = _writer.ReadOverride(body.TemplateCardId);
        if (templateBase is null && templateOverride is null)
            return BadRequest(new { error = $"template card '{body.TemplateCardId}' not found" });

        // template の merged JSON を取得 → activeVersion の spec を抽出
        var mergedJson = templateBase ?? "";
        if (templateBase is not null && templateOverride is not null)
            mergedJson = CardOverrideMerger.Merge(templateBase, templateOverride);
        else if (templateOverride is not null)
            mergedJson = templateOverride;

        using var tmplDoc = JsonDocument.Parse(mergedJson);
        var tmplRoot = tmplDoc.RootElement;
        var activeVer = tmplRoot.GetProperty("activeVersion").GetString();
        var versions = tmplRoot.GetProperty("versions");
        JsonElement? matchedSpec = null;
        foreach (var v in versions.EnumerateArray())
        {
            if (v.GetProperty("version").GetString() == activeVer)
            {
                matchedSpec = v.GetProperty("spec");
                break;
            }
        }
        if (matchedSpec is null)
            return BadRequest(new { error = "template active spec not found" });
        specElement = matchedSpec.Value;
    }
    else
    {
        // default minimal spec
        var defaultSpec = """
            { "rarity": 1, "cardType": "Skill", "cost": 1, "effects": [] }
        """;
        using var defDoc = JsonDocument.Parse(defaultSpec);
        specElement = defDoc.RootElement.Clone();
    }

    // 新規 versioned JSON 構築
    var newCardObj = new JsonObject
    {
        ["id"] = body.Id,
        ["name"] = body.Name,
        ["displayName"] = body.DisplayName,
        ["activeVersion"] = "v1",
        ["versions"] = new JsonArray
        {
            new JsonObject
            {
                ["version"] = "v1",
                ["createdAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["label"] = body.TemplateCardId is not null ? $"clone of {body.TemplateCardId}" : "new",
                ["spec"] = JsonNode.Parse(specElement.GetRawText()),
            }
        }
    };
    _writer.WriteOverride(body.Id, newCardObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    _provider.Rebuild();

    return Ok(new { id = body.Id });
}
```

(`JsonObject` / `JsonArray` の using ディレクティブを controller 側に追加)

- [ ] `dotnet test` で全件緑、新テスト 6 件追加で全部 pass。

---

## Task 2: Client api/dev.ts に createNewCard 追加

**Files:**
- Modify: `src/Client/src/api/dev.ts`

```typescript
export async function createNewCard(
  id: string,
  name: string,
  displayName: string | null,
  templateCardId: string | null,
): Promise<{ id: string }> {
  const resp = await fetch('/api/dev/cards', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id, name, displayName, templateCardId }),
  })
  if (!resp.ok) {
    const text = await resp.text()
    throw new Error(`createNewCard failed: ${resp.status} ${text}`)
  }
  return await resp.json()
}
```

---

## Task 3: DevCardsScreen に "New Card" UI 追加 (TDD smoke)

**Files:**
- Modify: `src/Client/src/screens/DevCardsScreen.tsx`
- Modify: `src/Client/src/screens/DevCardsScreen.css`
- Modify: `src/Client/src/screens/DevCardsScreen.test.tsx`

### Step 3.1: UI 設計

`DevCardsScreen` の左側上部に「+ New Card」ボタンを追加、押下でモーダル表示:

```tsx
function NewCardModal({ existingIds, onClose, onCreated }: {
  existingIds: string[]
  onClose: () => void
  onCreated: (id: string) => void
}) {
  const [id, setId] = useState('')
  const [name, setName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [templateId, setTemplateId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const submit = async () => {
    setError(null)
    if (!/^[a-z][a-z0-9_]*$/.test(id)) {
      setError('id must match ^[a-z][a-z0-9_]*$')
      return
    }
    if (!name) {
      setError('name is required')
      return
    }
    if (existingIds.includes(id)) {
      setError(`id '${id}' already exists`)
      return
    }
    setSubmitting(true)
    try {
      await createNewCard(id, name, displayName || null, templateId || null)
      onCreated(id)
    } catch (e) {
      setError(String(e))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="dev-modal-backdrop" onClick={onClose}>
      <div className="dev-modal" onClick={e => e.stopPropagation()}>
        <h3>New Card</h3>
        <label>ID <input value={id} onChange={e => setId(e.target.value)} placeholder="lowercase_id" /></label>
        <label>Name <input value={name} onChange={e => setName(e.target.value)} placeholder="表示名" /></label>
        <label>Display Name <input value={displayName} onChange={e => setDisplayName(e.target.value)} placeholder="(optional)" /></label>
        <label>Template Card ID <input value={templateId} onChange={e => setTemplateId(e.target.value)} placeholder="(optional, e.g., strike)" /></label>
        {error && <div className="dev-error">{error}</div>}
        <div className="dev-modal__actions">
          <button onClick={onClose} disabled={submitting}>Cancel</button>
          <button onClick={submit} disabled={submitting}>Create</button>
        </div>
      </div>
    </div>
  )
}
```

`DevCardsScreen` 本体に:
```tsx
const [newCardOpen, setNewCardOpen] = useState(false)

// 一覧の上部に
<button className="dev-new-card-btn" onClick={() => setNewCardOpen(true)}>+ New Card</button>

// modal 表示
{newCardOpen && (
  <NewCardModal
    existingIds={cards.map(c => c.id)}
    onClose={() => setNewCardOpen(false)}
    onCreated={(id) => {
      setNewCardOpen(false)
      // refresh list and select new card
      refreshAndSelect(id)
    }}
  />
)}
```

`refreshAndSelect`: cards を再 fetch し、新 id を selectedId にセット。

### Step 3.2: CSS

```css
.dev-new-card-btn {
  width: 100%;
  padding: 8px;
  background: #2a4a2a;
  color: #c8ffb3;
  border: 1px solid #4a7a4a;
  cursor: pointer;
  margin-bottom: 8px;
  font-size: 13px;
}
.dev-new-card-btn:hover { background: #3a5a3a; }

.dev-modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 100;
}
.dev-modal {
  background: #1a1a1a;
  border: 1px solid #444;
  padding: 24px;
  width: 400px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.dev-modal h3 { margin: 0 0 8px 0; color: #ffd23f; }
.dev-modal label { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: #aaa; }
.dev-modal input { padding: 6px; background: #0e0e0e; color: #fff; border: 1px solid #555; }
.dev-modal__actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 12px; }
.dev-modal__actions button { padding: 6px 14px; background: #2a2a2a; color: #ddd; border: 1px solid #555; cursor: pointer; }
```

### Step 3.3: smoke test

```tsx
it('creates new card via modal and selects it', async () => {
  vi.spyOn(global, 'fetch')
    .mockResolvedValueOnce({ ok: true, json: async () => [/* 既存 cards */] } as Response)
    .mockResolvedValueOnce({ ok: true, json: async () => ({ id: 'new_test' }) } as Response)
    .mockResolvedValueOnce({ ok: true, json: async () => [/* 既存 + new_test */] } as Response)

  render(<DevCardsScreen />)
  // ...modal open → input fill → submit → 確認
})
```

(設定が複雑なので最低限 modal が開く・閉じるテストでも OK)

---

## Task 4: Self-review + 1 commit + push

### 1. Spec coverage

- [ ] POST /api/dev/cards で新カード作成、id validation、template クローン、id 衝突 409、本番 404 ✓
- [ ] Client に "+ New Card" ボタン + modal ✓
- [ ] 作成後 list 自動更新 + 新カード選択 ✓

### 2. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑
- [ ] `npm run build` (Client) エラーなし

### 3. Commit + push

- [ ] 1 commit (`feat(server/client): new card creation via dev menu (Phase 10.5.K)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] Server `POST /api/dev/cards` が新カードを override に作成、id validation + template クローン対応、DEV ガード
- [ ] Client "New Card" モーダルから作成 → list 自動更新 + 自動選択
- [ ] 既存テスト全件緑、新テスト全件緑
- [ ] commit + push 済み

## 今回スコープ外

- relic / potion / enemy / unit の新規作成 → 10.5.L
- ID auto-suggestion → 当面手動
- 一括 import / batch 作成 → 必要時に別途

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5J-card-editor.md`](2026-05-01-phase10-5J-card-editor.md)
