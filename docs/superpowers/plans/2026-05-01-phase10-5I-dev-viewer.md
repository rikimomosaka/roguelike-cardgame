# Phase 10.5.I — Dev Menu + Cards Read-Only Viewer 実装

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 開発者専用の `/dev` 画面を新設し、cards 一覧と詳細 (全 version、active 強調、auto-text preview) を read-only で閲覧できるようにする。**3 段ゲート (UI / Route / API)** で本番ビルドからは物理的に消えるよう設計。

**Architecture:**
- **API**: `GET /api/dev/cards` — DEV 環境のみ enabled、cards の versioned 生 JSON 配列を返す
- **Client**: React Route `/dev/cards`、`import.meta.env.DEV` でのみ render
- **UI**: 2 カラム (左: card 一覧、右: 詳細ビュー)。各 version のスペックを Card コンポーネントで描画して比較可能
- **No editing**: 本フェーズは閲覧のみ。10.5.J で編集 / version 切替 / save 追加

**Tech Stack:** ASP.NET Core 10 + System.Text.Json (Server)、React 19 + TypeScript + Vite (Client)、xUnit / vitest。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-2 (10.5.I)、§5 (Dev menu)

**スコープ外:**
- 編集 / 新 version 作成 / Promote → 10.5.J / K
- relic / potion / enemy / unit viewer → 10.5.L
- Hot reload (FileSystemWatcher) → 必要なら 10.5.J で同時実装
- ログイン認証 (DEV 環境前提なので不要)

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Server/Controllers/DevCardsController.cs` | Create | `GET /api/dev/cards` endpoint、DEV ガード |
| `src/Server/Dtos/DevCardDto.cs` | Create | versioned JSON 構造を反映した DTO (id, name, displayName, activeVersion, versions[]) |
| `src/Server/Program.cs` | Modify | DEV 環境のみ DevCardsController を有効化する経路 |
| `tests/Server.Tests/Controllers/DevCardsControllerTests.cs` | Create | DEV 環境で 200 / 本番環境で 404 |
| `src/Client/src/screens/DevHomeScreen.tsx` | Create | `/dev` トップ (cards 等のメニュー一覧) |
| `src/Client/src/screens/DevCardsScreen.tsx` | Create | `/dev/cards` 一覧 + 詳細 |
| `src/Client/src/screens/DevCardsScreen.css` | Create | 2 カラムレイアウト |
| `src/Client/src/screens/DevCardsScreen.test.tsx` | Create | smoke test (一覧表示 + 詳細クリック) |
| `src/Client/src/api/dev.ts` | Create | `fetchDevCards()` |
| `src/Client/src/App.tsx` | Modify | DEV 時のみ `/dev/*` route mount |

---

## Conventions

- **TDD strictly.** Server endpoint 単体 → Client コンポーネント → wire-up の順。
- **Build clean.**
- **3 段ゲート**:
  1. UI: `import.meta.env.DEV` でのみ DevHomeScreen / DevCardsScreen import & 描画
  2. Route: DEV 時のみ React Router で `/dev/*` を mount。本番ビルドでは route 自体が無い
  3. API: DevCardsController を DEV 時のみ DI 登録 or `[Conditional]` 等で endpoint 自体を無効化、本番では 404
- **Read-only.** Server endpoint は GET のみ。POST / PUT / DELETE は **本フェーズ未実装** (10.5.J)
- **No editing in UI.** 詳細パネルでは「保存」ボタンを出さない。version 切替も「プレビュー」ラベルだけで実 active は変えない (実 active 切替は 10.5.J)
- **Display 経路は既存活用.** カード描画は既存 `<Card>` コンポーネント、description は `<CardDesc>` で marker 解釈

---

## Task 1: Server API GET /api/dev/cards (TDD)

**Files:**
- Create: `src/Server/Dtos/DevCardDto.cs`
- Create: `src/Server/Controllers/DevCardsController.cs`
- Modify: `src/Server/Program.cs`
- Create: `tests/Server.Tests/Controllers/DevCardsControllerTests.cs`

### Step 1.1: DTO 設計

- [ ] `DevCardDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record DevCardDto(
    string Id,
    string Name,
    string? DisplayName,
    string ActiveVersion,
    System.Collections.Generic.IReadOnlyList<DevCardVersionDto> Versions);

public sealed record DevCardVersionDto(
    string Version,
    string? CreatedAt,
    string? Label,
    string Spec);  // spec 部分は JSON 文字列のまま (UI 側で適宜パース or 表示)
```

(Spec を string で渡すのは、JSON 構造を保ったまま UI で表示するため。完全に DTO 化すると CardDefinition と同等の表現が必要になり scope creep)

### Step 1.2: テスト

```csharp
public class DevCardsControllerTests : IClassFixture<DevWebApplicationFactory>
{
    private readonly HttpClient _client;
    public DevCardsControllerTests(DevWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task GetCards_returns_200_in_dev_with_card_list()
    {
        var resp = await _client.GetAsync("/api/dev/cards");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        Assert.Contains("strike", json);
        Assert.Contains("activeVersion", json);
        Assert.Contains("versions", json);
    }
}

// 本番環境 (Production) で 404 になるテスト
public class DevCardsControllerProdTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly HttpClient _client;
    public DevCardsControllerProdTests(ProductionWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task GetCards_returns_404_in_production()
    {
        var resp = await _client.GetAsync("/api/dev/cards");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, resp.StatusCode);
    }
}
```

`DevWebApplicationFactory` / `ProductionWebApplicationFactory` は既存の `WebApplicationFactory<Program>` を `UseEnvironment("Development")` / `UseEnvironment("Production")` で派生。既存テスト fixture を流用 or 新設。

### Step 1.3: 実装

- [ ] `DevCardsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Dtos;
using System.Reflection;
using System.Text.Json;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/dev")]
public sealed class DevCardsController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly DataCatalog _catalog;

    public DevCardsController(IWebHostEnvironment env, DataCatalog catalog)
    {
        _env = env;
        _catalog = catalog;
    }

    [HttpGet("cards")]
    public IActionResult GetCards()
    {
        if (!_env.IsDevelopment()) return NotFound();

        // 埋め込みリソースの versioned JSON を読んで一覧化 + override 反映後の active を確認
        var asm = typeof(DataCatalog).Assembly;
        const string prefix = "RoguelikeCardGame.Core.Data.Cards.";
        var result = new System.Collections.Generic.List<DevCardDto>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix) || !name.EndsWith(".json")) continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new System.IO.StreamReader(stream);
            var json = reader.ReadToEnd();
            try
            {
                var dto = ParseDevCardJson(json);
                if (dto is not null) result.Add(dto);
            }
            catch { /* skip malformed */ }
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return Ok(result);
    }

    private static DevCardDto? ParseDevCardJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("id", out var idEl)) return null;
        if (!root.TryGetProperty("name", out var nameEl)) return null;
        if (!root.TryGetProperty("activeVersion", out var avEl)) return null;
        if (!root.TryGetProperty("versions", out var vsEl) || vsEl.ValueKind != JsonValueKind.Array) return null;

        var versions = new System.Collections.Generic.List<DevCardVersionDto>();
        foreach (var v in vsEl.EnumerateArray())
        {
            string ver = v.TryGetProperty("version", out var vEl) ? vEl.GetString() ?? "" : "";
            string? createdAt = v.TryGetProperty("createdAt", out var cEl) ? cEl.GetString() : null;
            string? label = v.TryGetProperty("label", out var lEl) ? lEl.GetString() : null;
            string spec = v.TryGetProperty("spec", out var sEl) ? sEl.GetRawText() : "{}";
            versions.Add(new DevCardVersionDto(ver, createdAt, label, spec));
        }

        string? displayName = root.TryGetProperty("displayName", out var dnEl) && dnEl.ValueKind == JsonValueKind.String
            ? dnEl.GetString() : null;

        return new DevCardDto(
            idEl.GetString() ?? "",
            nameEl.GetString() ?? "",
            displayName,
            avEl.GetString() ?? "",
            versions);
    }
}
```

注意: 本番環境で `[ApiController]` controller 自体は登録されるが、メソッド内で `IsDevelopment` ガード → 本番では 404。**より厳格にしたい場合**は `Program.cs` で `app.MapWhen(env.IsDevelopment(), ...)` で controller 自体を本番 mount しない設計も可。今は in-controller ガードでシンプルに。

- [ ] `dotnet build` パス、テスト緑。

---

## Task 2: Client API helper + DEV ルーティング (TDD は smoke 程度)

**Files:**
- Create: `src/Client/src/api/dev.ts`
- Modify: `src/Client/src/App.tsx`

### Step 2.1: API helper

- [ ] `src/Client/src/api/dev.ts`:

```typescript
export type DevCardVersionDto = {
  version: string
  createdAt: string | null
  label: string | null
  spec: string  // JSON string
}

export type DevCardDto = {
  id: string
  name: string
  displayName: string | null
  activeVersion: string
  versions: DevCardVersionDto[]
}

export async function fetchDevCards(): Promise<DevCardDto[]> {
  const resp = await fetch('/api/dev/cards')
  if (!resp.ok) throw new Error(`fetchDevCards failed: ${resp.status}`)
  return await resp.json()
}
```

### Step 2.2: Route mount (DEV ガード)

- [ ] `App.tsx` で DEV 時のみ `/dev` route を mount。既存 routing pattern を確認 (React Router 使用なら `<Route path="/dev/*" element={...} />`、ない場合は state ベース screen 切替で `?dev=1` クエリ等で起動)

```tsx
{/* App.tsx 既存 routing 内 */}
{import.meta.env.DEV && (
  <>
    <Route path="/dev" element={<DevHomeScreen />} />
    <Route path="/dev/cards" element={<DevCardsScreen />} />
  </>
)}
```

`React Router` が既存使われているか確認。**使われていない場合**は state ベースで切替 (`window.location.search` から `?dev=cards` を読む等の簡素な実装で OK)。

---

## Task 3: DevHomeScreen + DevCardsScreen (TDD: smoke + snapshot)

**Files:**
- Create: `src/Client/src/screens/DevHomeScreen.tsx`
- Create: `src/Client/src/screens/DevCardsScreen.tsx`
- Create: `src/Client/src/screens/DevCardsScreen.css`
- Create: `src/Client/src/screens/DevCardsScreen.test.tsx`

### Step 3.1: DevHomeScreen (シンプルなメニュー)

- [ ] `DevHomeScreen.tsx`:

```tsx
export function DevHomeScreen() {
  return (
    <div className="dev-home">
      <h1>開発者メニュー (DEV ONLY)</h1>
      <ul>
        <li><a href="/dev/cards">Cards Viewer</a></li>
        {/* 後続: relic / potion / enemy / unit へのリンク */}
      </ul>
    </div>
  )
}
```

### Step 3.2: DevCardsScreen (一覧 + 詳細 2 カラム)

- [ ] レイアウト:
  - 左: card 一覧 (scrollable)、各エントリは「id (active=v1)」形式
  - 右: 選択中カードの詳細
    - 上: id / name / displayName
    - 中: versions タブ (v1 active / v2 / v3 等)、選択した version の spec (JSON 表示) と Card プレビュー
    - 下: auto-text を CardDesc で描画

```tsx
import { useEffect, useState } from 'react'
import { fetchDevCards } from '../api/dev'
import type { DevCardDto } from '../api/dev'
import { Card } from '../components/Card'
import { CardDesc } from '../components/CardDesc'
import './DevCardsScreen.css'

export function DevCardsScreen() {
  const [cards, setCards] = useState<DevCardDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selectedVer, setSelectedVer] = useState<string | null>(null)

  useEffect(() => {
    fetchDevCards()
      .then((list) => {
        setCards(list)
        if (list.length > 0) {
          setSelectedId(list[0].id)
          setSelectedVer(list[0].activeVersion)
        }
      })
      .catch((e) => setError(String(e)))
  }, [])

  if (error) return <div className="dev-error">Error: {error}</div>
  if (!cards) return <div className="dev-loading">Loading...</div>

  const selected = cards.find((c) => c.id === selectedId)
  const ver = selected?.versions.find((v) => v.version === selectedVer)

  return (
    <div className="dev-cards">
      <aside className="dev-cards__list">
        <h2>Cards ({cards.length})</h2>
        <ul>
          {cards.map((c) => (
            <li
              key={c.id}
              className={c.id === selectedId ? 'is-active' : ''}
              onClick={() => {
                setSelectedId(c.id)
                setSelectedVer(c.activeVersion)
              }}
            >
              {c.id} <span className="dev-cards__active-tag">({c.activeVersion})</span>
            </li>
          ))}
        </ul>
      </aside>
      <main className="dev-cards__detail">
        {selected ? (
          <DevCardDetail
            card={selected}
            versionId={selectedVer ?? selected.activeVersion}
            onSelectVersion={setSelectedVer}
          />
        ) : (
          <p>Select a card</p>
        )}
      </main>
    </div>
  )
}

function DevCardDetail({
  card,
  versionId,
  onSelectVersion,
}: {
  card: DevCardDto
  versionId: string
  onSelectVersion: (v: string) => void
}) {
  const ver = card.versions.find((v) => v.version === versionId)
  if (!ver) return <p>Version not found</p>

  // spec から Card 表示用フィールドを抽出 (簡易パース、Catalog 経由の方が網羅的だが
  // ここは raw JSON ベースで OK)
  let spec: any = {}
  try { spec = JSON.parse(ver.spec) } catch { /* fallthrough */ }

  return (
    <div className="dev-card-detail">
      <header>
        <h2>{card.name}</h2>
        <code>{card.id}</code>
      </header>
      <section className="dev-card-detail__versions">
        <h3>Versions</h3>
        <div className="dev-card-detail__version-tabs">
          {card.versions.map((v) => (
            <button
              key={v.version}
              type="button"
              className={[
                'dev-card-detail__ver-btn',
                v.version === versionId ? 'is-selected' : '',
                v.version === card.activeVersion ? 'is-active' : '',
              ].filter(Boolean).join(' ')}
              onClick={() => onSelectVersion(v.version)}
            >
              {v.version}
              {v.version === card.activeVersion ? ' ✓' : ''}
              {v.label ? ` (${v.label})` : ''}
            </button>
          ))}
        </div>
      </section>
      <section className="dev-card-detail__preview">
        <h3>Card Preview ({versionId})</h3>
        {/* Card コンポーネントは catalog DTO 経由を想定しているので、ここは簡易表示 */}
        <div className="dev-card-detail__card-wrap">
          {/* spec.description / spec.effects から自動生成テキストを CardDesc で表示 */}
          {/* 簡易: spec.description あればそれを、なければ spec.effects を JSON で */}
          <CardDesc text={spec.description ?? '(auto-generated text would appear here once integrated)'} />
        </div>
      </section>
      <section className="dev-card-detail__spec">
        <h3>Spec (JSON)</h3>
        <pre><code>{JSON.stringify(spec, null, 2)}</code></pre>
      </section>
    </div>
  )
}
```

### Step 3.3: CSS

- [ ] `DevCardsScreen.css`:

```css
.dev-cards {
  display: flex;
  height: 100vh;
  background: #1a1a1a;
  color: #ddd;
  font-family: var(--font-body);
}
.dev-cards__list {
  width: 240px;
  border-right: 1px solid #444;
  overflow-y: auto;
  padding: 12px;
}
.dev-cards__list h2 {
  font-size: 14px;
  letter-spacing: 1px;
  margin-bottom: 8px;
  color: #999;
}
.dev-cards__list ul {
  list-style: none;
  padding: 0;
  margin: 0;
}
.dev-cards__list li {
  padding: 6px 8px;
  cursor: pointer;
  font-size: 13px;
  border-radius: 4px;
}
.dev-cards__list li:hover { background: #2a2a2a; }
.dev-cards__list li.is-active { background: #3a3a3a; color: #ffd23f; }
.dev-cards__active-tag {
  color: #888;
  font-size: 11px;
  margin-left: 4px;
}
.dev-cards__detail {
  flex: 1;
  overflow-y: auto;
  padding: 24px;
}
.dev-card-detail header h2 { margin: 0; font-size: 22px; color: #ffd23f; }
.dev-card-detail header code { font-size: 13px; color: #888; }
.dev-card-detail__version-tabs {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  margin: 8px 0;
}
.dev-card-detail__ver-btn {
  padding: 4px 10px;
  background: #2a2a2a;
  border: 1px solid #444;
  color: #bbb;
  cursor: pointer;
  font-size: 12px;
}
.dev-card-detail__ver-btn.is-selected { background: #3a3a3a; color: #fff; }
.dev-card-detail__ver-btn.is-active { border-color: #ffd23f; }
.dev-card-detail__spec pre {
  background: #0e0e0e;
  border: 1px solid #333;
  padding: 12px;
  font-size: 12px;
  font-family: monospace;
  color: #c9d1d9;
  overflow-x: auto;
}
```

### Step 3.4: smoke test

- [ ] `DevCardsScreen.test.tsx`:

```tsx
import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { DevCardsScreen } from './DevCardsScreen'

describe('DevCardsScreen', () => {
  it('shows loading then card list', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => [
        { id: 'strike', name: 'ストライク', displayName: null, activeVersion: 'v1',
          versions: [{ version: 'v1', createdAt: null, label: 'original', spec: '{}' }] },
      ],
    } as Response)

    render(<DevCardsScreen />)
    expect(screen.getByText(/Loading/i)).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('strike')).toBeInTheDocument())
  })

  it('renders error state on fetch failure', async () => {
    vi.spyOn(global, 'fetch').mockRejectedValueOnce(new Error('boom'))
    render(<DevCardsScreen />)
    await waitFor(() => expect(screen.getByText(/Error/)).toBeInTheDocument())
  })
})
```

---

## Task 4: Self-review + 1 commit + push

### 1. Spec coverage

- [ ] `/api/dev/cards` endpoint が DEV 環境のみ機能、本番は 404 ✓
- [ ] `/dev` および `/dev/cards` ルートが DEV 時のみ mount ✓
- [ ] cards 一覧 + 詳細 (versions タブ + spec JSON 表示 + 自動テキスト preview) ✓
- [ ] read-only (編集 UI なし) ✓

### 2. 3 段ゲート確認

- [ ] UI: `import.meta.env.DEV` 分岐で本番 build から DevCardsScreen が tree-shake される (もしくは少なくとも mount されない)
- [ ] Route: `/dev/*` が本番 build に存在しない (App.tsx の条件付き mount で物理的に消える)
- [ ] API: 本番環境で `/api/dev/cards` が 404

### 3. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (既存 + 新 ~3)
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑 (既存 + 新 ~2)
- [ ] `npm run build` (Client) エラーなし
- [ ] `dotnet run --project src/Server` 起動 sanity 確認

### 4. Commit + push

- [ ] 1 commit (`feat(server/client): dev menu + read-only cards viewer (Phase 10.5.I)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] Server `GET /api/dev/cards` が DEV 環境で cards 配列を返す、本番で 404
- [ ] Client `/dev/cards` route が DEV 時のみ mount され、cards 一覧 + 詳細表示
- [ ] 本フェーズは read-only、編集 UI 一切なし
- [ ] Tests + commit + push 済み

## 今回スコープ外

- Editor (新 version 作成 / spec 編集 / save / Promote) → 10.5.J
- relic / potion / enemy / unit viewer → 10.5.L
- React Router 未導入なら state ベース切替で OK (後で React Router 化検討)
- Hot reload (FileSystemWatcher) → 10.5.J or 別途
- 認証 (DEV 環境前提)

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5H-versioned-json.md`](2026-05-01-phase10-5H-versioned-json.md)
