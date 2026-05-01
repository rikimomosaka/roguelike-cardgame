# Phase 10.5.J — Cards 編集ツール (Save as v{N+1} / Switch Active / Promote / Delete) 実装

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 10.5.I の read-only viewer に編集機能を載せる。JSON テキストエディタで spec を編集 → 「Save as v{N+1}」で override に新 version 追加、「Switch active」で activeVersion 切替、「Promote」で override → base 転記、「Delete」で override version 削除。Server 側は disk I/O と DataCatalog hot rebuild、Client 側は textarea + ボタン UI + 検証フィードバック。

**Architecture:**
- **Server**: 4 mutation endpoint (POST versions, PATCH active, DELETE versions/{ver}, POST promote)、すべて DEV ガード。disk への書き込みは override (`data-local/dev-overrides/cards/{id}.json`) と base (`src/Core/Data/Cards/{id}.json` の repo パス) に対して行う。promote 前に `data-local/backups/cards/{id}-{timestamp}.json` にバックアップ。書き込み後 DataCatalog を再構築 (in-memory rebuild)
- **Client**: DevCardsScreen の詳細パネルに textarea + 4 ボタン追加、編集中の dirty buffer を保持、保存時は API call → 成功で UI 更新

**Tech Stack:** ASP.NET Core 10、System.Text.Json (Server)、React 19 + TypeScript (Client)、xUnit / vitest。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-2 (10.5.J)、§4 (Override 層)、§5 (Dev menu)

**スコープ外:**
- Ajv による厳密 JSON schema 検証 → ベース実装 (JSON.parse + CardJsonLoader.Parse trial) で MVP、Ajv は polish 別途
- 構造化フォームエディタ (dropdown / number spinner) → 当面 textarea 直書き
- FileSystemWatcher hot reload → mutation endpoint で同期 rebuild するためそもそも不要
- 新規カード作成 → 10.5.K
- relic / potion 等 → 10.5.L
- Validation 失敗 / partial 失敗時の細かいエラー UX → 簡素なメッセージで OK

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Server/Controllers/DevCardsController.cs` | Modify | 4 mutation endpoint 追加 |
| `src/Server/Services/DevCardWriter.cs` | Create | disk I/O ヘルパ (override 読み書き / base 書き込み / backup) |
| `src/Server/Services/DataCatalogProvider.cs` | Create | DataCatalog をミュータブル wrapper で保持、rebuild 可能に |
| `src/Server/Program.cs` | Modify | `DataCatalog` 直登録から `DataCatalogProvider` に変更、Singleton 用法は同じ |
| `tests/Server.Tests/Controllers/DevCardsControllerMutationTests.cs` | Create | 4 endpoint の DEV 環境テスト + 本番 404 |
| `src/Client/src/api/dev.ts` | Modify | save / switch / delete / promote 関数追加 |
| `src/Client/src/screens/DevCardsScreen.tsx` | Modify | editor textarea + ボタン UI + dirty buffer state |
| `src/Client/src/screens/DevCardsScreen.css` | Modify | エディタ部のスタイル |
| `src/Client/src/screens/DevCardsScreen.test.tsx` | Modify | save / promote の smoke test 追加 |

---

## Conventions

- **TDD strictly.** mutation endpoint ごと、Client UI は smoke test。
- **DEV ガード厳守.** mutation endpoint も `IsDevelopment()` で 404 ガード。
- **Disk write atomicity.** 書き込み失敗時は state inconsistent にしない (一時ファイル → rename パターン推奨だが MVP は直接 write で OK、失敗時は 500 + ロールバックなし)。
- **Rebuild 同期.** mutation 成功後、Server 内 `DataCatalogProvider.Rebuild()` で in-memory catalog を再構築。Client は次の API call から新カタログを見る。
- **Path 解決.** Server プロセスは `src/Server/bin/Debug/net10.0/` で動くので、repo root は `Path.Combine(env.ContentRootPath, "..", "..")`。`override` は `<repo>/data-local/dev-overrides/cards/{id}.json`、`base` は `<repo>/src/Core/Data/Cards/{id}.json`、`backup` は `<repo>/data-local/backups/cards/{id}-{yyyyMMddHHmmss}.json`。
- **idempotent save.** 同じ spec を複数回 save すると都度新 version を作る (重複検出はしない、user が判断)。

---

## Task 1: DataCatalogProvider で rebuild 可能にする (TDD)

**Files:**
- Create: `src/Server/Services/DataCatalogProvider.cs`
- Modify: `src/Server/Program.cs`
- Modify: 既存全 controller (`_catalog` field を `_provider.Current` に置換)

**目的:** mutation 後に Server 内 catalog を再構築できるようにする。既存 `AddSingleton<DataCatalog>` は immutable な fixed instance を返すため、rebuild できない。

### Step 1.1: Provider 設計

- [ ] `DataCatalogProvider.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using RoguelikeCardGame.Core.Data;
using System.IO;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// DataCatalog をミュータブルに保持し、override 変更時に rebuild できる Provider。
/// 既存の DataCatalog 直 inject 先には Provider.Current を経由するように差し替える。
/// </summary>
public sealed class DataCatalogProvider
{
    private readonly IWebHostEnvironment _env;
    private DataCatalog _current;

    public DataCatalogProvider(IWebHostEnvironment env)
    {
        _env = env;
        _current = BuildCatalog();
    }

    public DataCatalog Current => _current;

    public void Rebuild()
    {
        _current = BuildCatalog();
    }

    private DataCatalog BuildCatalog()
    {
        if (!_env.IsDevelopment())
            return EmbeddedDataLoader.LoadCatalog();

        var overrideRoot = Path.Combine(_env.ContentRootPath, "..", "..", "data-local", "dev-overrides");
        var overrides = DevOverrideLoader.LoadCards(overrideRoot);
        return overrides.Count == 0
            ? EmbeddedDataLoader.LoadCatalog()
            : EmbeddedDataLoader.LoadCatalogWithOverrides(overrides);
    }
}
```

### Step 1.2: DI 切替

- [ ] `Program.cs`:
```csharp
builder.Services.AddSingleton<DataCatalogProvider>();
builder.Services.AddSingleton<DataCatalog>(sp => sp.GetRequiredService<DataCatalogProvider>().Current);
```

注意: `DataCatalog` を Singleton で再登録すると Rebuild 後も古い instance を返す。代替案:

- (a) `AddTransient<DataCatalog>(sp => sp.GetRequiredService<DataCatalogProvider>().Current)` — 都度 lookup (推奨)
- (b) 全 controller を Provider inject に書き換える (既存 controller 多いので変更量大)

**(a) Transient 採用** で Singleton 用法のまま動作 (Provider 経由で常に最新)。

```csharp
builder.Services.AddSingleton<DataCatalogProvider>();
builder.Services.AddTransient<DataCatalog>(sp => sp.GetRequiredService<DataCatalogProvider>().Current);
```

### Step 1.3: 既存 controller の動作確認

- [ ] 既存 Server.Tests を実行し、catalog 経由のテスト (CatalogControllerTests, BattleControllerTests, etc.) が緑のまま。

---

## Task 2: DevCardWriter (TDD)

**Files:**
- Create: `src/Server/Services/DevCardWriter.cs`
- Create: `tests/Server.Tests/Services/DevCardWriterTests.cs`

**責務:** override / base ファイルへの書き込み + backup。

### Step 2.1: テスト

```csharp
public class DevCardWriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DevCardWriter _writer;

    public DevCardWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "writer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _writer = new DevCardWriter(_tempRoot);  // overrideRoot に直接渡す変則 ctor 想定、実装は調整
    }
    public void Dispose() { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }

    [Fact]
    public void WriteOverride_creates_file()
    {
        _writer.WriteOverride("strike", """{ "id": "strike", "versions": [] }""");
        var path = Path.Combine(_tempRoot, "cards", "strike.json");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void DeleteOverride_removes_file()
    {
        _writer.WriteOverride("strike", "{}");
        _writer.DeleteOverride("strike");
        var path = Path.Combine(_tempRoot, "cards", "strike.json");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteOverride_missing_is_noop()
    {
        // 例外を投げない
        _writer.DeleteOverride("nonexistent");
    }

    // promote / backup などは Server 統合 test で
}
```

### Step 2.2: 実装

- [ ] `DevCardWriter.cs`:

```csharp
using System.IO;

namespace RoguelikeCardGame.Server.Services;

public sealed class DevCardWriter
{
    private readonly string _overrideRoot;  // <repo>/data-local/dev-overrides
    private readonly string? _baseCardsDir; // <repo>/src/Core/Data/Cards (promote 用)
    private readonly string? _backupRoot;   // <repo>/data-local/backups

    public DevCardWriter(string overrideRoot, string? baseCardsDir = null, string? backupRoot = null)
    {
        _overrideRoot = overrideRoot;
        _baseCardsDir = baseCardsDir;
        _backupRoot = backupRoot;
    }

    public void WriteOverride(string cardId, string json)
    {
        var dir = Path.Combine(_overrideRoot, "cards");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{cardId}.json");
        File.WriteAllText(path, json);
    }

    public string? ReadOverride(string cardId)
    {
        var path = Path.Combine(_overrideRoot, "cards", $"{cardId}.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void DeleteOverride(string cardId)
    {
        var path = Path.Combine(_overrideRoot, "cards", $"{cardId}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public void WriteBaseWithBackup(string cardId, string json)
    {
        if (_baseCardsDir is null) throw new System.InvalidOperationException("baseCardsDir not configured");
        var basePath = Path.Combine(_baseCardsDir, $"{cardId}.json");
        // backup 既存 base
        if (_backupRoot is not null && File.Exists(basePath))
        {
            var backupDir = Path.Combine(_backupRoot, "cards");
            Directory.CreateDirectory(backupDir);
            var ts = System.DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(basePath, Path.Combine(backupDir, $"{cardId}-{ts}.json"), overwrite: false);
        }
        File.WriteAllText(basePath, json);
    }

    public string? ReadBase(string cardId)
    {
        if (_baseCardsDir is null) return null;
        var path = Path.Combine(_baseCardsDir, $"{cardId}.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
```

`Program.cs` で DI 登録:
```csharp
builder.Services.AddSingleton<DevCardWriter>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var repoRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));
    var overrideRoot = Path.Combine(repoRoot, "data-local", "dev-overrides");
    var baseCardsDir = Path.Combine(repoRoot, "src", "Core", "Data", "Cards");
    var backupRoot = Path.Combine(repoRoot, "data-local", "backups");
    return new DevCardWriter(overrideRoot, baseCardsDir, backupRoot);
});
```

- [ ] テスト緑。

---

## Task 3: DevCardsController に 4 mutation endpoint 追加 (TDD)

**Files:**
- Modify: `src/Server/Controllers/DevCardsController.cs`
- Modify: `tests/Server.Tests/Controllers/DevCardsControllerMutationTests.cs` (新規)

### Step 3.1: 設計

| Method | Path | Body | 動作 |
|---|---|---|---|
| POST | `/api/dev/cards/{id}/versions` | `{ label: string?, spec: object }` | 新 version を override に追加。version id は `v{max+1}` で自動採番 |
| PATCH | `/api/dev/cards/{id}/active` | `{ version: string }` | override の `activeVersion` を上書き保存 |
| DELETE | `/api/dev/cards/{id}/versions/{version}` | (empty) | override から指定 version を削除 (active と同じならエラー) |
| POST | `/api/dev/cards/{id}/promote` | `{ version: string, makeActiveOnBase?: bool }` | override の version を base JSON に転記、override から削除、optional で base.activeVersion 更新 |

### Step 3.2: テスト

```csharp
[Fact]
public async Task SaveVersion_creates_override_with_new_version()
{
    var body = new { label = "test", spec = new { rarity = 1, cardType = "Attack", cost = 1, effects = new object[] {} } };
    var resp = await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", body);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    // GET /api/dev/cards で確認: strike の versions に v2 が増えている
    var listResp = await _client.GetAsync("/api/dev/cards");
    var list = await listResp.Content.ReadFromJsonAsync<List<DevCardDto>>();
    var strike = list!.First(c => c.Id == "strike");
    Assert.True(strike.Versions.Count >= 2);
}

[Fact]
public async Task SwitchActive_updates_activeVersion()
{
    // 先に v2 を作って、その後 PATCH で active=v2 に切替
    // 確認: GET で activeVersion=v2
}

[Fact]
public async Task DeleteVersion_removes_from_override()
{
    // v2 を作って削除 → GET で v2 が無い
}

[Fact]
public async Task DeleteActive_version_returns_400()
{
    // active な version は削除できない
}

[Fact]
public async Task Promote_writes_to_base_and_removes_from_override()
{
    // v2 promote → base に v2 が含まれる、override から v2 消える
    // (実 file system は temp に向ける fixture が必要)
}

[Fact]
public async Task All_mutation_endpoints_return_404_in_production()
{
    // 4 endpoint × Production environment で 404
}
```

mutation テストは temp dir 経由が望ましい。WebApplicationFactory に `services.AddSingleton<DevCardWriter>(_ => new DevCardWriter(tempRoot, ...))` を上書き injection。

### Step 3.3: 実装

```csharp
[HttpPost("cards/{id}/versions")]
public IActionResult SaveVersion(string id, [FromBody] SaveVersionRequest body)
{
    if (!_env.IsDevelopment()) return NotFound();

    // 1. Read base / current override
    var baseJson = _writer.ReadBase(id);
    if (baseJson is null) return NotFound($"card '{id}' not found in base");
    var existingOverride = _writer.ReadOverride(id);

    // 2. Determine next version id (max v\d+ from base + override + 1)
    int maxN = ScanMaxVersionNumber(baseJson, existingOverride);
    string newVer = $"v{maxN + 1}";

    // 3. Construct new version entry
    var newVersionEntry = new {
        version = newVer,
        createdAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        label = body.Label,
        spec = body.Spec,
    };

    // 4. Build override JSON: existing override (if any) + new version
    var overrideObj = ParseOrCreateOverride(existingOverride, id);
    AppendVersion(overrideObj, newVersionEntry);
    if (string.IsNullOrEmpty(overrideObj.GetProperty("activeVersion")?.GetValue<string>()))
        overrideObj["activeVersion"] = newVer;  // first save → make active
    var overrideJson = overrideObj.ToJsonString(...);

    // 5. Write + rebuild
    _writer.WriteOverride(id, overrideJson);
    _provider.Rebuild();

    return Ok(new { newVersion = newVer });
}

[HttpPatch("cards/{id}/active")]
public IActionResult SwitchActive(string id, [FromBody] SwitchActiveRequest body) { /* similar */ }

[HttpDelete("cards/{id}/versions/{version}")]
public IActionResult DeleteVersion(string id, string version) { /* similar */ }

[HttpPost("cards/{id}/promote")]
public IActionResult Promote(string id, [FromBody] PromoteRequest body) { /* similar */ }

public sealed record SaveVersionRequest(string? Label, JsonElement Spec);
public sealed record SwitchActiveRequest(string Version);
public sealed record PromoteRequest(string Version, bool MakeActiveOnBase = false);
```

(具体的な JSON 操作は `JsonNode` ベース、CardOverrideMerger 同様の pattern。) 実装で苦戦するポイント:
- 自動採番: base + override の versions[] から `v\d+` を全部抽出して max+1
- Promote: override から version を 1 個 base に移す + 残りは override 保持 (空になれば override file 削除)

---

## Task 4: Client api/dev.ts を mutation 対応 (TDD)

**Files:**
- Modify: `src/Client/src/api/dev.ts`

```typescript
export async function saveCardVersion(id: string, label: string | null, spec: unknown): Promise<{ newVersion: string }> {
  const resp = await fetch(`/api/dev/cards/${id}/versions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ label, spec }),
  })
  if (!resp.ok) throw new Error(`save failed: ${resp.status} ${await resp.text()}`)
  return await resp.json()
}

export async function switchActiveVersion(id: string, version: string): Promise<void> {
  const resp = await fetch(`/api/dev/cards/${id}/active`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ version }),
  })
  if (!resp.ok) throw new Error(`switch failed: ${resp.status}`)
}

export async function deleteCardVersion(id: string, version: string): Promise<void> {
  const resp = await fetch(`/api/dev/cards/${id}/versions/${version}`, { method: 'DELETE' })
  if (!resp.ok) throw new Error(`delete failed: ${resp.status}`)
}

export async function promoteCardVersion(id: string, version: string, makeActiveOnBase = false): Promise<void> {
  const resp = await fetch(`/api/dev/cards/${id}/promote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ version, makeActiveOnBase }),
  })
  if (!resp.ok) throw new Error(`promote failed: ${resp.status}`)
}
```

---

## Task 5: DevCardsScreen に editor UI を追加 (TDD: smoke)

**Files:**
- Modify: `src/Client/src/screens/DevCardsScreen.tsx`
- Modify: `src/Client/src/screens/DevCardsScreen.css`
- Modify: `src/Client/src/screens/DevCardsScreen.test.tsx`

### Step 5.1: 詳細パネルに editor 追加

```tsx
function DevCardDetail({ card, ... }) {
  const ver = card.versions.find((v) => v.version === selectedVer)!
  const [draft, setDraft] = useState(ver.spec)
  const [label, setLabel] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // selectedVer 変わったら draft 更新
  useEffect(() => { setDraft(ver.spec); setLabel(''); setError(null) }, [card.id, selectedVer])

  const saveAsNew = async () => {
    setError(null); setSaving(true)
    try {
      let parsed
      try { parsed = JSON.parse(draft) }
      catch (e) { setError(`Invalid JSON: ${e}`); return }
      await saveCardVersion(card.id, label || null, parsed)
      // refresh
      onAfterMutation()
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  const switchActive = async () => {
    setError(null); setSaving(true)
    try {
      await switchActiveVersion(card.id, selectedVer)
      onAfterMutation()
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  const deleteVersion = async () => {
    if (!confirm(`Delete version ${selectedVer}?`)) return
    setError(null); setSaving(true)
    try {
      await deleteCardVersion(card.id, selectedVer)
      onAfterMutation()
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  const promoteVer = async () => {
    if (!confirm(`Promote ${selectedVer} to source (base JSON)?`)) return
    setError(null); setSaving(true)
    try {
      await promoteCardVersion(card.id, selectedVer)
      onAfterMutation()
    } catch (e) { setError(String(e)) }
    finally { setSaving(false) }
  }

  return (
    <div className="dev-card-detail">
      {/* ... 既存表示 ... */}
      <section className="dev-card-detail__editor">
        <h3>Editor (selected: {selectedVer})</h3>
        <input
          type="text"
          placeholder="Label (optional)"
          value={label}
          onChange={e => setLabel(e.target.value)}
        />
        <textarea
          className="dev-card-detail__textarea"
          value={draft}
          onChange={e => setDraft(e.target.value)}
        />
        <div className="dev-card-detail__actions">
          <button onClick={saveAsNew} disabled={saving}>Save as v{N+1}</button>
          <button onClick={switchActive} disabled={saving || selectedVer === card.activeVersion}>
            Set as active
          </button>
          <button onClick={promoteVer} disabled={saving}>Promote to source</button>
          <button onClick={deleteVersion} disabled={saving || selectedVer === card.activeVersion}>
            Delete version
          </button>
        </div>
        {error && <div className="dev-error">{error}</div>}
      </section>
    </div>
  )
}
```

`onAfterMutation` は親 (`DevCardsScreen`) から渡され、card list を fetch し直す callback。

### Step 5.2: smoke test

```tsx
it('saves new version when "Save as v..." button clicked', async () => {
  // mock fetch GET → cards、POST → success、GET 再 → updated
  // textarea を編集 → Save click → 確認
})
```

### Step 5.3: CSS

```css
.dev-card-detail__textarea {
  width: 100%;
  min-height: 240px;
  font-family: monospace;
  font-size: 12px;
  background: #0e0e0e;
  color: #c9d1d9;
  border: 1px solid #444;
  padding: 8px;
}
.dev-card-detail__actions { display: flex; gap: 8px; margin-top: 8px; }
.dev-card-detail__actions button {
  padding: 6px 12px;
  background: #2a2a2a;
  color: #ddd;
  border: 1px solid #555;
  cursor: pointer;
}
.dev-card-detail__actions button:hover:not(:disabled) { background: #3a3a3a; }
.dev-card-detail__actions button:disabled { opacity: 0.4; cursor: not-allowed; }
.dev-error { color: #ff7a7a; padding: 8px; background: #2a1010; margin-top: 8px; }
```

---

## Task 6: Self-review + 1 commit + push

### 1. Spec coverage

- [ ] Save as v{N+1}: override に新 version 追加、自動採番 ✓
- [ ] Switch active: override の activeVersion 上書き ✓
- [ ] Delete version: override から削除 (active は削除不可) ✓
- [ ] Promote: override → base 転記、override から削除、backup 取る ✓
- [ ] mutation 後 DataCatalog rebuild ✓
- [ ] DEV ガード in-controller、本番 404 ✓

### 2. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Server +mutation tests)
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑
- [ ] `npm run build` (Client) エラーなし
- [ ] `dotnet run --project src/Server` 起動 sanity 確認 (override 無し → 動作変化なし)

### 3. Commit + push

- [ ] 1 commit (`feat(server/client): card editor (save/switch/delete/promote) for dev menu (Phase 10.5.J)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] DataCatalogProvider が rebuild 可能、既存 controller が transient 経路で常に最新 catalog を見る
- [ ] DevCardWriter が override / base / backup の disk I/O を担う
- [ ] DevCardsController に 4 mutation endpoint、DEV ガード付き
- [ ] DevCardsScreen に editor UI、4 ボタン + textarea + label
- [ ] xUnit / vitest 新規テスト全件緑、既存全件緑
- [ ] commit + push 済み

## 今回スコープ外

- Ajv schema 検証 → polish
- 構造化フォームエディタ → 将来
- 新規カード作成 → 10.5.K
- relic / potion 等 → 10.5.L
- mutation の rollback / atomic write → 必要なら別途

## ロールバック

問題あれば `Program.cs` で `DataCatalog` を直 inject に戻す + `DevCardsController` の mutation endpoint method をコメントアウトすれば、editor UI は API call 失敗してエラー表示するが catalog は immutable に戻り安全。

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5I-dev-viewer.md`](2026-05-01-phase10-5I-dev-viewer.md)
- override merge: [`2026-05-01-phase10-5H-versioned-json.md`](2026-05-01-phase10-5H-versioned-json.md)
