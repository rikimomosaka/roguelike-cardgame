# Phase 10.5.L1 — Relic Editor (versioned JSON + dev menu)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Card で実装済の versioned JSON / override / dev menu / editor / new / delete 機構を **relic** に展開する。完了時、開発者は `/dev/relics` で 36 件の relic を編集・新規作成・delete・promote できる。

**Architecture:** Card の構成をそのままミラーする:
- `RelicJsonLoader` を versioned 対応に拡張 (旧 flat 形式も後方互換)
- `RelicOverrideMerger` (純関数) を新設 — `CardOverrideMerger` の mirror
- 一括移行 PowerShell スクリプトで 36 relic JSON を versioned に変換
- Server `DevRelicsController` に GET / POST / PATCH / DELETE / promote / preview / new endpoint
- `DevCardWriter` を `DevAssetWriter` 風に拡張 or 新 `DevRelicWriter` を追加
- `DataCatalogProvider` / `EmbeddedDataLoader.LoadCatalogWithOverrides` に relic override パスを追加
- Client `api/dev.ts` に relic 系関数追加
- 新 `DevRelicsScreen.tsx` + `RelicSpecForm.tsx` + `RelicVisualPreview.tsx`
- `DevHomeScreen` に `/dev/relics` ボタン
- `App.tsx` の `?dev=relics` route 分岐

**Tech Stack:** .NET 10 + System.Text.Json (Core / Server)、xUnit、React 19 + TypeScript、vitest。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-2 (10.5.L)、§3 (versioned JSON)、§4 (override)。

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Relics/RelicJsonLoader.cs` | Modify | versioned/flat 両対応に拡張 (mirror CardJsonLoader) |
| `src/Core/Relics/RelicOverrideMerger.cs` | Create | 純関数 merge (mirror CardOverrideMerger) |
| `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs` | Modify | versioned パースのテスト追加 |
| `tests/Core.Tests/Relics/RelicOverrideMergerTests.cs` | Create | merge ルール単体テスト |
| `tools/migrate-relics-to-versioned.ps1` | Create | 36 relic JSON 一括移行スクリプト (mirror migrate-cards) |
| `src/Core/Data/Relics/*.json` (36 ファイル) | Modify | 移行スクリプトで versioned 化 |
| `src/Core/Data/EmbeddedDataLoader.cs` | Modify | `LoadCatalogWithOverrides` シグネチャに relic override 引数追加 |
| `src/Server/Services/DevOverrideLoader.cs` | Modify | `LoadRelics(overrideRoot)` 関数追加 |
| `src/Server/Services/DataCatalogProvider.cs` | Modify | rebuild 時に card / relic 両方の override を読む |
| `src/Server/Services/DevCardWriter.cs` | Modify (or 拡張) | base/override/backup の path を asset type ごとに切替可能化 |
| `src/Server/Controllers/DevRelicsController.cs` | Create | GET / POST / PATCH / DELETE / promote / preview / new (mirror DevCardsController) |
| `src/Server/Dtos/DevRelicDto.cs` | Create | id/name/displayName/activeVersion/versions[] (mirror DevCardDto) |
| `src/Server/Controllers/DevMetaController.cs` | Modify | `relicTriggers` を返す (OnPickup/Passive/OnBattleStart/...) |
| `tests/Server.Tests/Controllers/DevRelicsControllerTests.cs` | Create | GET / 4 mutation / preview / new / delete + production 404 (mirror DevCardsControllerTests) |
| `src/Client/src/api/dev.ts` | Modify | `fetchDevRelics`, `saveRelicVersion`, `switchActiveRelicVersion`, `deleteRelicVersion`, `promoteRelicVersion`, `createNewRelic`, `previewRelicDescription`, `deleteRelic` |
| `src/Client/src/screens/DevRelicsScreen.tsx` | Create | 一覧 + 詳細 + editor (mirror DevCardsScreen) |
| `src/Client/src/screens/DevRelicsScreen.css` | Create (or 共通化) | mirror DevCardsScreen.css 流用可 |
| `src/Client/src/screens/dev/RelicSpecForm.tsx` | Create | rarity / trigger dropdown / effects (CardEffect 配列) / description override |
| `src/Client/src/screens/dev/RelicVisualPreview.tsx` | Create | 既存 RelicIcon component で見た目プレビュー |
| `src/Client/src/screens/dev/DevSpecTypes.ts` | Modify | `RelicSpec` 型 + `parseRelicSpec`/`relicSpecToJsonObject`/`emptyRelicSpec` |
| `src/Client/src/screens/DevHomeScreen.tsx` | Modify | 「Relic 編集」ボタン追加 |
| `src/Client/src/App.tsx` | Modify | `?dev=relics` route mount |

---

## Versioned JSON Schema (Relic)

旧 flat:
```json
{
  "id": "act1_start_01",
  "name": "アンカー",
  "rarity": 1,
  "trigger": "OnPickup",
  "description": "迷っても、心を留めるための小さな錨。",
  "effects": [{ "action": "gainMaxHp", "scope": "self", "amount": 8 }],
  "implemented": true
}
```

新 versioned:
```json
{
  "id": "act1_start_01",
  "name": "アンカー",
  "displayName": null,
  "activeVersion": "v1",
  "versions": [
    {
      "version": "v1",
      "createdAt": "2026-05-02T...",
      "label": "original",
      "spec": {
        "rarity": 1,
        "trigger": "OnPickup",
        "description": "迷っても、心を留めるための小さな錨。",
        "effects": [...],
        "implemented": true
      }
    }
  ]
}
```

`spec` の中身は flat の root から `id` / `name` / `displayName` を抜いた残り。`displayName` は relic に元々無いので null 固定 (将来用)。

---

## Conventions

- **TDD strictly.** Server endpoint → Client component → wire-up の順。
- **Build clean.**
- **Mirror card 実装.** Card 側の同じパターンに従い、コピペ + relic 用 substitution で実装。リファクタで共通化したい欲求は本フェーズでは抑える (10.5.L 全 4 種別完了後にまとめて検討)。
- **DevCardWriter の拡張**: `DevCardWriter` のパス組み立てロジック (`overrideRoot/cards/*`、`baseCardsDir`) を `assetType` パラメータ化して relic でも使えるようにする。または新 `DevRelicWriter` を追加してもよい (同じだけのコード)。subagent の判断に任せる。
- **EmbeddedDataLoader.LoadCatalogWithOverrides** シグネチャ拡張:
  ```csharp
  public static DataCatalog LoadCatalogWithOverrides(
      IReadOnlyDictionary<string, string> cardOverrides,
      IReadOnlyDictionary<string, string>? relicOverrides = null)
  ```
- **dev menu ガード 3 段** は card 同様: `import.meta.env.DEV` (Client) + DEV-only route + in-controller `IsDevelopment()` (Server)。

---

## Task 1: RelicJsonLoader を versioned/flat 両対応に拡張 (TDD)

**Files:**
- Modify: `src/Core/Relics/RelicJsonLoader.cs`
- Modify: `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs`

CardJsonLoader と同じパターン。`Parse` 入口で `versions` プロパティ有無を見て分岐、`ParseSpec` private helper に共通ロジックを切り出す。

新 spec 構造:
- root: `id`, `name`, `displayName?`, `activeVersion`, `versions[]`
- 各 version: `version`, `createdAt?`, `label?`, `spec`
- spec: `rarity`, `trigger`, `description`, `effects`, `implemented`

### テスト

- versioned 形式読込 (multi-version + activeVersion 解決)
- unknown activeVersion → throw
- flat 形式の後方互換維持

---

## Task 2: RelicOverrideMerger 新設 (TDD)

**Files:**
- Create: `src/Core/Relics/RelicOverrideMerger.cs`
- Create: `tests/Core.Tests/Relics/RelicOverrideMergerTests.cs`

`CardOverrideMerger.Merge(baseJson, overrideJson)` の完全 mirror。同じ規則:
- versions 配列 union (override 優先で同 version id を上書き)
- override の activeVersion が指定されていれば base の activeVersion を上書き
- id mismatch なら throw

---

## Task 3: 一括移行スクリプト

**Files:**
- Create: `tools/migrate-relics-to-versioned.ps1`

`tools/migrate-cards-to-versioned.ps1` を base に複製、対象を `src\Core\Data\Relics\` に変更。spec として root から抜く field は `id`, `name`, `displayName` のみ (relic は `displayName` 無いがファイルにあれば抜く)。

実行 → `git diff src/Core/Data/Relics/` で 36 ファイルが versioned 化されたことを確認 → 既存 `dotnet test` 全件緑 (loader 後方互換のため)。

---

## Task 4: Server Override loader / catalog provider 拡張

**Files:**
- Modify: `src/Core/Data/EmbeddedDataLoader.cs`
- Modify: `src/Server/Services/DevOverrideLoader.cs`
- Modify: `src/Server/Services/DataCatalogProvider.cs`

`EmbeddedDataLoader.LoadCatalogWithOverrides` 拡張で relic override も merge できるように。`DevOverrideLoader` に `LoadRelics(overrideRoot)` 追加。`DataCatalogProvider` の rebuild 経路で card + relic 両方の override を読む。

---

## Task 5: DevCardWriter を asset-type 対応に拡張 (or DevRelicWriter 新設)

**Files:**
- Modify: `src/Server/Services/DevCardWriter.cs` (推奨)
  - もしくは Create: `src/Server/Services/DevRelicWriter.cs` (同等)

card と relic で同じロジック (override read/write/delete + base read/write+backup) なので、subdir 名を引数化する形で1 クラスに集約するのが合理的。`new DevCardWriter(overrideRoot, baseCardsDir, backupRoot, subDir: "cards")` のような感じ。

DI 登録:
```csharp
builder.Services.AddSingleton<DevCardWriter>(...) // subDir: "cards"
builder.Services.AddSingleton<DevRelicWriter>(...) // 別 instance、subDir: "relics"
```

Subagent の好みで wrapper 型名は `DevCardWriter` を generic 化するか、別名 type を作るか選択。テストは新 instance で行う。

---

## Task 6: DevRelicsController + DevRelicDto + DevMetaController 拡張 (TDD)

**Files:**
- Create: `src/Server/Dtos/DevRelicDto.cs`
- Create: `src/Server/Controllers/DevRelicsController.cs`
- Modify: `src/Server/Controllers/DevMetaController.cs`
- Create: `tests/Server.Tests/Controllers/DevRelicsControllerTests.cs`

### Endpoints (mirror DevCardsController)

| Method | Path | 動作 |
|---|---|---|
| GET | `/api/dev/relics` | 一覧 (versioned 形式で返す、override merged) |
| POST | `/api/dev/relics/{id}/versions` | 新 version 作成 (override に保存、自動採番 v{N+1}) |
| PATCH | `/api/dev/relics/{id}/active` | activeVersion 切替 |
| DELETE | `/api/dev/relics/{id}/versions/{ver}` | version 削除 (active は削除不可) |
| POST | `/api/dev/relics/{id}/promote` | override 版を base に転記 + backup |
| POST | `/api/dev/relics/preview` | spec → formatter 出力 (description 自動生成、或いは override 文字列) |
| POST | `/api/dev/relics` | 新規 relic 作成 (template 指定可) |
| DELETE | `/api/dev/relics/{id}` | relic 全削除 (override or alsoBase) |

DEV ガード in-controller `IsDevelopment()` で本番 404。

### DevMetaController 拡張

```csharp
relicTriggers = new[] {
    "OnPickup", "Passive", "OnBattleStart", "OnBattleEnd",
    "OnMapTileResolved", "OnTurnStart", "OnTurnEnd",
    "OnCardPlay", "OnEnemyDeath",
},
```

### preview endpoint について

Relic は description override が一般的なので、formatter は CardTextFormatter ほど高機能でなくて OK。当面は spec.description (override 文字列) をそのまま返す形でよい。effects 自動文章化は将来検討 (Card formatter は使い回せるが trigger 表現等が異なる)。

```csharp
[HttpPost("relics/preview")]
public IActionResult Preview([FromBody] PreviewRequest body)
{
    if (!_env.IsDevelopment()) return NotFound();
    // spec.description が non-empty ならそれを返す。
    // empty なら effects から CardTextFormatter.FormatEffects を呼んでも OK。
    ...
}
```

### テスト

- 各 endpoint の DEV 200 / production 404
- mutation (save / switch / delete / promote / new / delete relic) の整合性
- override file 配置 + provider rebuild 経路

---

## Task 7: Client api/dev.ts に relic 系関数

**Files:**
- Modify: `src/Client/src/api/dev.ts`

card 系の各関数を mirror した relic 版を追加 (8 関数):
- `fetchDevRelics()`
- `saveRelicVersion(id, label, spec)`
- `switchActiveRelicVersion(id, version)`
- `deleteRelicVersion(id, version)`
- `promoteRelicVersion(id, version, makeActiveOnBase?)`
- `createNewRelic(id, name, displayName, templateRelicId)`
- `previewRelicDescription(spec)`
- `deleteRelic(id, alsoBase)`

DTO 型: `DevRelicDto` / `DevRelicVersionDto`。

---

## Task 8: DevSpecTypes に RelicSpec 追加

**Files:**
- Modify: `src/Client/src/screens/dev/DevSpecTypes.ts`

```typescript
export type RelicSpec = {
  rarity: number
  trigger: string  // "OnPickup" | "Passive" | ...
  description: string  // override 必須 (relic は description 手書きが基本)
  effects: CardEffect[]  // 既存 CardEffect 型を流用
  implemented: boolean
}

export function emptyRelicSpec(): RelicSpec
export function parseRelicSpec(json: string): RelicSpec
export function relicSpecToJsonObject(spec: RelicSpec): Record<string, unknown>
```

---

## Task 9: DevRelicsScreen + RelicSpecForm + RelicVisualPreview

**Files:**
- Create: `src/Client/src/screens/DevRelicsScreen.tsx`
- Create: `src/Client/src/screens/dev/RelicSpecForm.tsx`
- Create: `src/Client/src/screens/dev/RelicVisualPreview.tsx`
- Modify: `src/Client/src/screens/DevHomeScreen.tsx`
- Modify: `src/Client/src/App.tsx`

### DevRelicsScreen

`DevCardsScreen` の完全コピー + relic 用に置換:
- 左ペイン: relic 一覧 (id 順)、`+ 新規レリック` ボタン
- 右ペイン: 詳細 (versions タブ + RelicSpecForm + 各種 mutation ボタン)

### RelicSpecForm

入力 field:
- rarity (dropdown、meta.rarities)
- trigger (dropdown、meta.relicTriggers — 新規)
- description (textarea、relic は手書き必須)
- implemented (checkbox)
- effects (既存 EffectListEditor 流用)

下部に RelicVisualPreview。

### RelicVisualPreview

既存 `RelicIcon` component を使って icon + name を表示。description は `<CardDesc>` で marker 解釈 (effect 内に [N:..] 等が出る可能性あり)。

### DevHomeScreen

「Relic 編集」リンク追加 (`?dev=relics` route)。

### App.tsx

```tsx
if (import.meta.env.DEV && screen.kind === 'dev-relics') {
  return <DevRelicsScreen onBack={() => setScreen({ kind: 'dev-home' })} />
}
```

---

## Task 10: Self-review + 1 commit + push

### Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Core +relic loader/merger テスト、Server +DevRelicsController テスト)
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑 (新規 RelicSpecForm smoke test)
- [ ] `npm run build` (Client) エラーなし
- [ ] 36 relic JSON が versioned 化されたことを git diff で確認
- [ ] `dotnet run --project src/Server` 起動 sanity 確認

### Commit + push

- [ ] 1 commit (`feat(server/client): relic editor with versioned JSON + dev menu (Phase 10.5.L1)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] RelicJsonLoader が versioned/flat 両対応
- [ ] RelicOverrideMerger 純関数 + xUnit テスト
- [ ] 36 relic JSON 全部 versioned 化
- [ ] DevRelicsController で 8 endpoint (GET / 5 mutation / preview / new / delete) 全て DEV ガード付き
- [ ] DataCatalogProvider が relic override を読んで rebuild できる
- [ ] DevRelicsScreen / RelicSpecForm / RelicVisualPreview で UI 完成
- [ ] DevHomeScreen に link、App.tsx で route mount
- [ ] 既存テスト全件緑 + 新規テスト全件緑
- [ ] commit + push 済み

## スコープ外

- Potion / Enemy / Unit (それぞれ 10.5.L2 / L3 / L4 で別途)
- Card-Relic 共通の dev tool 抽象化 (4 種別完了後に検討)
- Relic effects 専用 formatter テンプレート (将来、effects 自動文章化が relic にも欲しくなったら)

## 関連ドキュメント

- 全体設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase (M4): Card editor 完成 + auto-fit / 文言改訂
- 移行サンプル: [`2026-05-01-phase10-5H-versioned-json.md`](2026-05-01-phase10-5H-versioned-json.md)
- card 編集 plan (mirror 元): [`2026-05-01-phase10-5J-card-editor.md`](2026-05-01-phase10-5J-card-editor.md)
- card 新規 plan: [`2026-05-01-phase10-5K-new-card.md`](2026-05-01-phase10-5K-new-card.md)
- card UX plan: [`2026-05-01-phase10-5M-form-editor.md`](2026-05-01-phase10-5M-form-editor.md)
