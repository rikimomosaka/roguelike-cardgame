# Phase 10.5.L2: Potion Editor 設計書

**Phase:** 10.5.L2
**作成日:** 2026-05-10
**前提:** Phase 10.5.L1 (relic editor) 完了済、Phase 10.5.M2-Choose 完了済

---

## 1. Architecture Overview

**目的:** Phase 10.5.L1 (relic editor) の機能をそのまま Potion に再現。dev menu から既存 potion 編集 / 新規 potion 作成 / formatter による効果テキストの自動生成 + 手動 override / version 管理 / 三段ゲート (UI + Route + API) を提供。

### 1.1 3 層の責務

| 層 | 既存 | 新規 / 拡張 |
|---|---|---|
| **Core** | `PotionDefinition(Id, Name, Rarity, Effects)` + `PotionJsonLoader` + `data/Potions/*.json` 7 種 | `PotionDefinition` に `DisplayName?` / `Description?` を追加。version 管理用の JSON schema (`versions[]` + `activeVersion`) を `PotionJsonLoader` が解釈。Formatter は既存 `CardTextFormatter` を流用 (potion effects も card と同じ `CardEffect` 型なので追加実装ほぼ不要) |
| **Server** | (なし) | `DevPotionsController.cs` 新規。`DevRelicsController` の構造を mirror して 7 endpoints を提供。`IsDevelopment()` ガード |
| **Client** | `EffectListEditor` / `EffectEditor` / `FormatterPreview` (再利用) | `DevPotionsScreen.tsx` (一覧 + 編集パネル) + `PotionSpecForm.tsx` (potion 固有 form) を新規作成。`KeywordSelector` は potion で keyword 概念がないため未使用。`RelicVisualPreview` 相当は作らず、既存 `PotionSlot` を埋め込む簡易表示のみ |

### 1.2 データフロー (relic と同等)

1. dev menu → `DevPotionsScreen` 起動 (UI ゲート: `import.meta.env.DEV`)
2. `GET /api/v1/dev/potions` → master 定義 + override 一覧取得
3. ユーザー編集 → `PotionSpecForm` で fields 入力 → live formatter preview (`POST /preview`)
4. 保存 → `POST /api/v1/dev/potions/{id}/versions` → `data-local/dev-overrides/potions/{id}.json` に書込
5. ロード時 (game runtime): `DataCatalog` が override → fallback で master の順で読込 (既存仕組みを流用)

### 1.3 保存方式 = M2 (override + versioning)

L1 と全く同じ。各 JSON に `versions[]` + `activeVersion` 内蔵。アクティブな version 1 つだけが engine から見える。

### 1.4 三段ゲート

- **UI:** `import.meta.env.DEV` でメニュー項目自体を非表示
- **Route:** dev-only route 登録
- **API:** `IsDevelopment()` で 404 を返す

---

## 2. PotionDefinition の拡張 + JSON schema

### 2.1 PotionDefinition の field 追加

```csharp
public sealed record PotionDefinition(
    string Id,
    string Name,
    string? DisplayName,             // 表示名 (省略可、null なら Name を表示)
    CardRarity Rarity,
    IReadOnlyList<CardEffect> Effects,
    string? Description = null);     // 手書き description override (null/空文字 → formatter で自動生成)
```

**追加しない fields (理由付き):**
- `UpgradedXxx` 系: potion は upgrade 概念がない
- `Trigger`: potion は即時使用、trigger 不要
- `VisualKey`: 既存 TopBar の potion-slot asset を流用、専用 visualKey 不要
- `Keywords`: potion で keyword 概念無し
- `Implemented` フラグ: 必要になったら別 phase で追加

### 2.2 JSON schema (versioning 内蔵)

L1 と同じ M2 (override + versioning) パターン:

```json
{
  "id": "fire_potion",
  "activeVersion": 1,
  "versions": [
    {
      "version": 1,
      "name": "ファイアポーション",
      "displayName": null,
      "rarity": 1,
      "effects": [
        { "action": "attack", "scope": "single", "side": "enemy", "amount": 20, "battleOnly": true }
      ],
      "description": null
    }
  ]
}
```

### 2.3 後方互換 (重要)

既存 7 個の `data/Potions/*.json` は flat 形式 (`{id, name, rarity, effects}`)。`PotionJsonLoader` は両形式を受け付ける:
- `versions` キー有り → 新形式、`activeVersion` を解決して該当 version を採用
- `versions` キー無し → 旧 flat 形式、そのまま `version 1` として解釈

これにより既存 7 個の master を移行せずに新 editor が動く。新規作成 / 編集 (override) は新形式で書く。

### 2.4 「戦闘中のみ / どこでも」の仕組み (確認済の既存仕様)

「戦闘中のみ」「どこでも」の判定は **potion 全体ではなく、各 effect (CardEffect) レベル** で `battleOnly: true|false` フラグとして管理:
- `CardEffect.BattleOnly` (default `false`)
- `PotionDefinition.IsUsableOutsideBattle => Effects.Any(e => !e.BattleOnly)` — 1 つでも `battleOnly=false` の effect があれば「どこでも使える」と判定

既存 7 ポーションの分布:
| Potion | effect | battleOnly | 使用タイミング |
|---|---|---|---|
| `fire_potion` | attack 20 | true | 戦闘中のみ |
| `block_potion` | block 12 | true | 戦闘中のみ |
| `strength_potion` | buff strength | true | 戦闘中のみ |
| `swift_potion`, `energy_potion`, `poison_potion` | (各種) | true | 戦闘中のみ |
| `health_potion` | heal 15 | (省略 = false) | どこでも使える |

UI 反映: マップ画面では `IsUsableOutsideBattle=false` の potion は TopBar でグレーアウト、`true` のものは map 上で直接使える。

### 2.5 Override 保存場所

`data-local/dev-overrides/potions/{id}.json` — 初回 override 時にディレクトリを作成 (relic と同じ挙動)。

---

## 3. Server Endpoints

`DevRelicsController` の 7 endpoints を potion 版に mirror。Route base: `[Route("api/v1/dev")]`、`IsDevelopment()` ガード。

| Method | Path | 用途 |
|---|---|---|
| `GET` | `/api/v1/dev/potions` | master + override をマージした一覧 (各 potion の versions[] / activeVersion 含む) |
| `POST` | `/api/v1/dev/potions` | 新規 potion 作成 (id / version 1 を初期化) |
| `POST` | `/api/v1/dev/potions/{id}/versions` | 既存 potion に新 version を追加 |
| `DELETE` | `/api/v1/dev/potions/{id}/versions/{version}` | 特定 version 削除 (`activeVersion` を指している version は削除不可、最後の 1 個も削除不可) |
| `POST` | `/api/v1/dev/potions/preview` | DTO を受け取り `CardTextFormatter` で description を生成して返す (live preview 用) |
| `DELETE` | `/api/v1/dev/potions/{id}` | override 全体を削除して master に戻す (`data-local/dev-overrides/potions/{id}.json` を unlink) |
| `POST` | `/api/v1/dev/potions/{id}/promote` | activeVersion を切替 |

### 3.1 実装方針

- `DevRelicsController.cs` を構造的にコピーし、`Relic` → `Potion` 置換 + potion 固有 fields (`description`, `displayName`) のみ取り扱い
- 既存 `DataCatalog` の potion ロード経路 (`PotionJsonLoader`) を流用、override 優先の merge は relic と同じパターン
- `DataCatalog` のリロード機能を potion でも有効化 (relic 側の hot-reload 仕組みを再利用)

### 3.2 DTO (Server → Client)

```csharp
public sealed record DevPotionListItemDto(
    string Id,
    int ActiveVersion,
    IReadOnlyList<int> AvailableVersions,
    string Source /* "master" | "override" */);

public sealed record DevPotionVersionDto(
    int Version,
    string Name,
    string? DisplayName,
    int Rarity,
    IReadOnlyList<CardEffectDto> Effects,
    string? Description);

public sealed record DevPotionPreviewRequestDto(DevPotionVersionDto Version);
public sealed record DevPotionPreviewResponseDto(string Description);
```

`CardEffectDto` は既存 (relic editor / card editor が使用) なので再利用。

### 3.3 エラー処理

- 不正な id (英数 + `_` 以外) → 400
- master 上書き禁止: 全ての書込み系 endpoint (POST 新規 / POST version 追加 / DELETE version / DELETE override / POST promote) は **必ず `data-local/dev-overrides/potions/{id}.json` のみを読み書きする**。`src/Core/Data/Potions/*.json` 側は read-only で、master JSON ファイルが直接変更されることはない (relic editor と同じ挙動)
- `Production` 環境 → 404 (三段ゲートの API 段)
- 最後の 1 version 削除 → 400
- `activeVersion` を指している version の削除 → 400

---

## 4. Client UI

### 4.1 ファイル構造

| 新規 / 修正 | ファイル | 用途 |
|---|---|---|
| 新規 | `src/Client/src/screens/DevPotionsScreen.tsx` | 画面全体 (左: potion 一覧 + 検索、右: 選択中 potion の編集パネル + version タブ) |
| 新規 | `src/Client/src/screens/DevPotionsScreen.css` | screen スタイル |
| 新規 | `src/Client/src/screens/dev/PotionSpecForm.tsx` | potion 固有フォーム |
| 新規 | `src/Client/src/screens/dev/PotionSpecForm.test.tsx` | フォーム単体テスト |
| 修正 | `src/Client/src/screens/DevHomeScreen.tsx` | ナビ「Potions」ボタンを追加 |
| 修正 | `src/Client/src/screens/dev/EffectEditor.tsx` | `battleOnly` トグルを表示 (always-on 方針) |
| 修正 | `src/Client/src/screens/dev/DevSpecTypes.ts` | `PotionSpec` / `PotionVersionSpec` 型 + `toPotionVersionDto` / `fromPotionVersionDto` 追加 |
| 修正 | `src/Client/src/api/dev.ts` | 7 endpoints の API client 関数 |
| 修正 | `src/Client/src/api/types.ts` | `DevPotionListItemDto` / `DevPotionVersionDto` 等の DTO 型追加 |
| 修正 | `src/Client/src/App.tsx` (or routing) | `/dev/potions` route 登録 (`import.meta.env.DEV` ガード) |

### 4.2 画面レイアウト (`DevPotionsScreen.tsx`)

`DevRelicsScreen` を mirror。左 30% / 右 70% の縦割り:
- 左: 検索 box + potion 一覧 (master / override / id / activeVersion 表示) + 「+ 新規作成」ボタン
- 右: 選択中 potion のパネル
  - ヘッダ: id / source (master/override) / 「master に戻す」ボタン (override 時のみ)
  - version タブ: `[V1] [V2] [+ 新 version]` + active マーク (★) + version の活性化 (promote) ボタン
  - フォーム本体 (`PotionSpecForm`)
  - フッタ: 保存ボタン (override 書込) / formatter live preview

### 4.3 PotionSpecForm.tsx (potion 固有フォーム)

Fields:
- `name` (string, 必須)
- `displayName` (string, 任意)
- `rarity` (Common / Rare / Epic / Legendary 選択)
- `description` (textarea + 「手動 override する」checkbox。off なら formatter 自動生成、on なら手書き)
- Effects (`EffectListEditor` 既存コンポーネント再利用)
  - 各 effect 行に `battleOnly` トグルを必須表示

Live preview パネル:
- 名前
- 説明 (formatter 出力 or override)
- 使用可能タイミング (戦闘中のみ / どこでも) — `IsUsableOutsideBattle` 相当を効果配列から計算して表示

### 4.4 EffectEditor.tsx の battleOnly トグル

- 既に型定義 (`DevSpecTypes.ts:15`) はあるので UI checkbox 行を 1 行追加するだけ
- always-on で表示。Tooltip: 「戦闘中のみ使用可能 (true=戦闘中限定 / false=どこでも使える)」
- 既存 relic / card 編集には混在しても害なし (relic は trigger 経由で発動、battleOnly は engine 側で無視)

### 4.5 API client (`src/Client/src/api/dev.ts` 拡張)

```typescript
export async function listDevPotions(): Promise<DevPotionListItemDto[]>;
export async function createDevPotion(spec: DevPotionVersionDto): Promise<DevPotionListItemDto>;
export async function addPotionVersion(id: string, spec: DevPotionVersionDto): Promise<DevPotionListItemDto>;
export async function deletePotionVersion(id: string, version: number): Promise<DevPotionListItemDto>;
export async function previewPotion(spec: DevPotionVersionDto): Promise<DevPotionPreviewResponseDto>;
export async function deletePotionOverride(id: string): Promise<void>;
export async function promotePotionVersion(id: string, version: number): Promise<DevPotionListItemDto>;
```

### 4.6 Live preview の動き

フォーム入力で onChange → debounce 300ms → `previewPotion` 呼出 → 結果を `FormatterPreview` (既存コンポーネント) に渡す。Description override が有効ならそれを優先表示、無効なら formatter 結果を表示。

### 4.7 Visual preview は省略

Relic の `RelicVisualPreview` は relic の icon assets を表示する固有 UI だが、potion icon は既存 TopBar の `PotionSlot` コンポーネントの asset を再利用するだけなので、画面右上に小さく既存 `PotionSlot` を埋め込めば十分。専用 `PotionVisualPreview.tsx` は作らない。

---

## 5. テスト戦略

### 5.1 Core.Tests (新規)

- `tests/Core.Tests/Potions/PotionJsonLoaderVersioningTests.cs` (新規) — 後方互換 + 新 schema:
  - 既存 flat 形式 (`{id, name, rarity, effects}`) が `version 1` として読み込めること
  - 新 schema (`{id, activeVersion, versions[]}`) が正しく解釈されること
  - `activeVersion` が指す version が選ばれること
  - `versions[]` 内の `description` / `displayName` が `PotionDefinition` に反映されること
  - 不正形式 (versions 空 / activeVersion 不一致 / 必須 fields 欠落) → `PotionJsonException`
- `tests/Core.Tests/Potions/PotionDefinitionTests.cs` (拡張または新規):
  - `Description` null/空 → `IsUsableOutsideBattle` 判定への影響なし
  - 既存 `IsUsableOutsideBattle` テスト維持
- 既存 7 ポーション JSON は **書き換えず**、後方互換テストでカバー

### 5.2 Server.Tests (新規)

- `tests/Server.Tests/Controllers/DevPotionsControllerTests.cs` (新規) — 7 endpoints の happy path:
  1. `GET /api/v1/dev/potions` → master 7 件 + (override 数) を返す
  2. `POST /api/v1/dev/potions` (新規作成) → 200 + override ファイル生成
  3. `POST /api/v1/dev/potions/{id}/versions` → 200 + versions[] に追加
  4. `DELETE /api/v1/dev/potions/{id}/versions/{version}` → 200 + versions[] から削除
  5. `POST /api/v1/dev/potions/preview` → 200 + formatter 出力
  6. `DELETE /api/v1/dev/potions/{id}` → 200 + override ファイル削除
  7. `POST /api/v1/dev/potions/{id}/promote` → 200 + activeVersion 切替

- `tests/Server.Tests/Controllers/DevPotionsControllerErrorTests.cs` (新規) — エラーパス:
  - `Production` 環境 → 404 (三段ゲートの API 段)
  - 不正 id (英数 + `_` 以外) → 400
  - 存在しない id への version 追加 → 404
  - 最後の 1 version 削除 → 400
  - `activeVersion` を指している version の削除 → 400
  - master 上書き直接書込の試み → 400 or 内部で override 側に書き換え

- `tests/Server.Tests/Controllers/DevPotionsControllerMutationTests.cs` (新規) — preview の formatter 検証:
  - description override 有り → そのまま返す
  - description null → formatter 自動生成結果を返す
  - effect 変更時の preview text 妥当性

期待: 既存 Server.Tests **289 + 新 ~15-20 = 約 305 PASS**。

### 5.3 Client.Tests (新規)

- `src/Client/src/screens/dev/PotionSpecForm.test.tsx` (新規) — フォーム単体:
  - 必須 fields (name / rarity) 未入力で保存ボタン disabled
  - effects 追加 / 削除 が state に反映
  - `battleOnly` トグルが state に反映
  - description override checkbox の挙動
  - live preview が API call をトリガ (debounced) — vi.useFakeTimers で時間操作
- `src/Client/src/screens/DevPotionsScreen.test.tsx` (新規、軽量) — screen 統合:
  - 一覧表示 + 選択 + フォーム表示の基本フロー
  - 新規作成ボタン → form 初期化
  - 「master に戻す」ボタン → confirm 後に DELETE /dev/potions/{id} 呼出
- 既存 `EffectEditor.test.tsx` 拡張 — `battleOnly` トグル UI を追加した分の検証

期待: 既存 Client **183 + 新 ~10-15 = 約 195-200 PASS**。

### 5.4 統合動作確認 (manual)

- `dev.bat` 起動 → `/dev/potions` へ遷移
- 既存 `fire_potion` を編集 → 保存 → battle で実際に火力が変わるか確認
- 新規 potion 作成 → reward / merchant に出現するか確認 (要 catalog reload)
- master 戻す → override 削除 + 動作確認
- production build (`npm run build`) 実行 → dev 画面が含まれない / `import.meta.env.DEV` で hidden

### 5.5 Pre-existing flake への対応

T7/T8 で観測された RewardPopup test の parallel-mode flake は本フェーズ作業範囲外。`npm run test:run -- --no-file-parallelism` で実行する運用継続。

---

## 6. Out of Scope (本フェーズで実装しないもの)

明示的に除外する future work — 後続フェーズで必要になったときに別 spec で扱う:

1. **Visual preview の高度版** — 専用 `PotionVisualPreview.tsx` は作らない。potion icon は既存 `PotionSlot` の埋め込みのみ
2. **Trigger / Keywords / VisualKey field の追加** — `PotionDefinition` には追加しない (即時使用 + keyword 概念無し + 単一 asset)
3. **Upgrade メカニズム** — Potion は upgrade 概念がないため `upgradedXxx` 系 fields を追加しない
4. **Generic 抽象化 (`DevSpecForm<T>`) の導入** — Card / Relic / Potion / 将来の Enemy / Unit を共通化する generic フォームは本フェーズで導入しない。L3/L4 着手時に共通化の必要性が出てきたら別フェーズで refactor
5. **既存 7 ポーション JSON の新 schema 移行** — 既存 master JSON は flat 形式のまま放置。`PotionJsonLoader` の後方互換で吸収
6. **Catalog hot-reload の改善** — 新規作成 / 編集後の即時反映は relic editor の既存 hot-reload 機構を再利用。Reload 速度 / 信頼性の改善は対象外
7. **Description formatter の potion 専用拡張** — `CardTextFormatter` はそのまま流用。potion 用の専用 marker は対象外。`IsUsableOutsideBattle` の表示は editor の preview パネル側で別表示する形に留める
8. **多言語対応** — JP 固定 (Phase 10.5 全体の方針継続)
9. **Production 環境への dev menu 露出** — 三段ゲートで完全遮断。Production build に dev コードが残らない検証は手動 build 確認のみ、自動 e2e テストは対象外

---

## 7. 完了条件 (Definition of Done)

- 7 endpoints 実装 + Server.Tests 全 green (約 305 PASS)
- Client UI (`DevPotionsScreen` + `PotionSpecForm` + `EffectEditor.battleOnly` 表示) 実装 + Client.Tests 全 green (約 195-200 PASS)
- Core.Tests の新 versioning + 後方互換テスト全 green (1263 + 新 ~10 = 約 1273 PASS)
- `tsc --noEmit` clean
- `dev.bat` 起動で `/dev/potions` から既存 7 ポーションが編集できる
- `data-local/dev-overrides/potions/` への override 書込 → ゲーム内反映の手動確認
- production build で dev menu 完全遮断
- memory 更新 (`MEMORY.md` + `project_phase_status.md`) で Phase 10.5.L2 完了を記録
