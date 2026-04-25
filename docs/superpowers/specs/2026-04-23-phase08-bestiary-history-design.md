# Phase 8 — 図鑑・プレイ履歴 設計書

**Goal:** メイン画面「実績」からアカウント単位の図鑑（カード / レリック / ポーション / モンスター）と過去ランの履歴一覧を閲覧できるようにする。

**Scope:** 図鑑の集計データ構造、発見タイミングの組み込み、アカウント単位 Bestiary の永続化、履歴一覧 UI、Achievements 画面。UI はデバッグスタイルで完成させる。

**Non-goals:**
- 履歴削除機能（YAGNI、必要なら Phase 9 以降）
- 統計情報（撃破数 / 入手回数）
- 実績バッジ・トロフィー
- 初発見日時
- マルチプレイでの Bestiary 共有（Phase 9+）

---

## Architecture

```
Client                              Server                         Core
──────                              ──────                         ────
AchievementsScreen (5 tabs)
  ├─ BestiaryListView  ──GET /bestiary──>  BestiaryController ──>  BestiaryState (record)
  │                                           │
  │                                           └── FileBestiaryRepository
  │                                               (bestiary/{accountId}.json)
  └─ HistoryListView   ──GET /history───>  HistoryController  ──>  RunHistoryRecord (v2)
                                              │
                                              └── FileHistoryRepository

Run end (Cleared / Abandon / GameOver)
  RunsController
    ├─ history.AppendAsync(record)
    └─ bestiary.MergeAsync(accountId, record)
           └─ BestiaryUpdater.Merge(current, record) = new BestiaryState
```

**原則:**
- Core は I/O を持たず純関数。Bestiary 更新は `BestiaryUpdater.Merge(state, record) → state` で表現。
- Server は `FileBestiaryRepository` で `{root}/bestiary/{accountId}.json` に永続化。
- 履歴 (`RunHistoryRecord`) は「ラン中に遭遇した 4 セット」を保持し、これが Bestiary 反映の情報源（単方向: 履歴 → Bestiary）。
- RunState にも同じ 4 セットを保持し、セーブ毎に保存される（ラン途中の再開で保持される）。ラン終了時に `RunHistoryBuilder` が RunState → RunHistoryRecord にコピー、`BestiaryUpdater` で Bestiary に反映。

---

## Data Models

### 1. `BestiaryState` (Core, new)

`src/Core/Bestiary/BestiaryState.cs`

```csharp
public sealed record BestiaryState(
    int SchemaVersion,
    ImmutableHashSet<string> DiscoveredCardBaseIds,
    ImmutableHashSet<string> DiscoveredRelicIds,
    ImmutableHashSet<string> DiscoveredPotionIds,
    ImmutableHashSet<string> EncounteredEnemyIds)
{
    public const int CurrentSchemaVersion = 1;
    public static BestiaryState Empty { get; } = new(
        CurrentSchemaVersion,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty);
}
```

**決定事項:**
- カードは base_id（強化版 `strike+` は `strike` に正規化）で保存する。強化版を別スロットにはしない（質問 5 の案 A）。
- 発見済みセットは順序性なし。シリアライズ時は ID 昇順に並べて決定性を確保。
- `Empty` は新規アカウントや初回取得時のデフォルト。

### 2. `RunState` 拡張（v5 → v6）

`src/Core/Run/RunState.cs` に以下 4 フィールドを追加:

```csharp
ImmutableArray<string> SeenCardBaseIds,      // 初期デッキ+報酬3択+商人在庫+強制イベント配布で見たすべて
ImmutableArray<string> AcquiredRelicIds,      // 入手したレリック
ImmutableArray<string> AcquiredPotionIds,     // 入手したポーション
ImmutableArray<string> EncounteredEnemyIds    // バトル開始時に登場した敵ID
```

**注記:**
- `ImmutableArray<string>` を選んだ理由: 既存 `RunState` 内の他コレクションがすべて `ImmutableArray`。重複チェックは `BestiaryTracker` 経由で追加時に行う（集合としての挙動を保つ）。
- schema v5 → v6 migration: 欠落フィールドは空配列で埋める。既存セーブは破壊しない。
- `RunState.Validate()` では 4 フィールドの `IsDefault` チェックのみ（空 OK）。

### 3. `RunHistoryRecord` 拡張（v1 → v2）

`src/Core/History/RunHistoryRecord.cs` に 4 セットを追加。`RunHistoryBuilder.From(...)` で RunState からコピー。

```csharp
public sealed record RunHistoryRecord(
    int SchemaVersion,
    string AccountId,
    string RunId,
    RunProgress Outcome,
    int ActReached,
    int NodesVisited,
    long PlaySeconds,
    string CharacterId,
    int FinalHp,
    int FinalMaxHp,
    int FinalGold,
    ImmutableArray<CardInstance> FinalDeck,
    ImmutableArray<string> FinalRelics,
    DateTimeOffset EndedAtUtc,
    // v2 で追加:
    ImmutableArray<string> SeenCardBaseIds,
    ImmutableArray<string> AcquiredRelicIds,
    ImmutableArray<string> AcquiredPotionIds,
    ImmutableArray<string> EncounteredEnemyIds)
{
    public const int CurrentSchemaVersion = 2;
}
```

v1 ファイル読み込み時は 4 セットを空配列として扱う（既存履歴は「このランで何に遭遇したか」不明として空で入る、Bestiary には反映されない）。

---

## Detection Points

質問 2 の「C: カテゴリ別に自然な基準」に従い、以下のイベントで `BestiaryTracker` を呼ぶ。

| カテゴリ | 発生源 | 呼び出し箇所 | 追加するもの |
|---|---|---|---|
| カード | 初期デッキ付与 | `Core/Run/RunState.cs` の `RunState.Create`（`return new RunState(...)` の直前で Tracker を適用、または生成後にラップ） | `character.Deck` の base_id 群 |
| カード | 報酬 3 択生成 | `Core/Rewards/RewardGenerator.cs` の `Generate` 呼び出し側（`RunsController` で `ActiveReward` を設定する直前に Tracker 適用） | `reward.CardChoices.Select(c => c.BaseId)` |
| カード | 商人在庫生成 | `Core/Merchant/MerchantInventoryGenerator.cs` 呼び出し側（`MerchantController` で `ActiveMerchant` を設定する直前に Tracker 適用） | `inv.Cards.Select(c => c.CardId)` の base_id |
| カード | `?` イベント強制配布 | `Core/Events/EventResolver.cs` でカードが付与される分岐 | 配布カードの base_id |
| レリック | 報酬で入手 | `Core/Rewards/RewardApplier.cs:84-87`（`Relics = newRelics`） | `r.RelicId` |
| レリック | 商人で入手 | `Core/Merchant/MerchantActions.cs:40-41`（`Relics = ...Append(relicId)`） | `relicId` |
| レリック | イベントで入手 | `Core/Events/EventResolver.cs:67-68`（`Relics = newRelics`） | `chosen.Id` |
| レリック | 層開始で入手 | `Core/Run/ActStartActions.cs:48-51`（`Relics = newRelics`） | `relicId` |
| ポーション | 報酬で入手 | `Core/Rewards/RewardApplier.cs:33-36`（`Potions = s.Potions.SetItem(idx, r.PotionId)`） | `r.PotionId` |
| ポーション | 商人で入手 | `Core/Merchant/MerchantActions.cs:60-61` | `potionId` |
| モンスター | バトル開始 | `Core/Battle/BattlePlaceholder.cs` の `StartBattle` 呼び出し側（`RunsController` の `battle/enter` で `ActiveBattle` を設定する直前に Tracker 適用） | エンカウンターの `EnemyIds` 全件 |

### `BestiaryTracker` (Core, new)

`src/Core/Bestiary/BestiaryTracker.cs`

```csharp
public static class BestiaryTracker
{
    public static RunState NoteCardsSeen(RunState s, IEnumerable<string> baseIds);
    public static RunState NoteRelicsAcquired(RunState s, IEnumerable<string> ids);
    public static RunState NotePotionsAcquired(RunState s, IEnumerable<string> ids);
    public static RunState NoteEnemiesEncountered(RunState s, IEnumerable<string> ids);
}
```

- 4 関数すべて純関数。RunState の該当フィールドに ID を和集合で追加（重複除外）。
- 冪等: 既に含まれる ID を渡しても状態は変わらない。
- 入力 null / 空: そのまま状態を返す（no-op）。

### `BestiaryUpdater` (Core, new)

`src/Core/Bestiary/BestiaryUpdater.cs`

```csharp
public static class BestiaryUpdater
{
    public static BestiaryState Merge(BestiaryState current, RunHistoryRecord record);
}
```

- `record` の 4 セットを `current` の対応 HashSet に和集合で追加。
- カテゴリ対応:
  - `record.SeenCardBaseIds` → `DiscoveredCardBaseIds`
  - `record.AcquiredRelicIds` → `DiscoveredRelicIds`
  - `record.AcquiredPotionIds` → `DiscoveredPotionIds`
  - `record.EncounteredEnemyIds` → `EncounteredEnemyIds`
- 冪等: 既に含まれる ID が record にあっても結果は同じ。
- SchemaVersion は `BestiaryState.CurrentSchemaVersion` に固定。

### `BestiaryStateSerializer` (Core, new)

`src/Core/Bestiary/BestiaryStateSerializer.cs`
- `Serialize(BestiaryState) → string` / `Deserialize(string) → BestiaryState`
- JSON 形式（`JsonOptions.Default` と同じ `System.Text.Json` 設定）
- 決定的出力のため、ID 配列は昇順ソート済みで書き出す。

---

## Server

### 1. `IBestiaryRepository`

`src/Server/Abstractions/IBestiaryRepository.cs`

```csharp
public interface IBestiaryRepository
{
    Task<BestiaryState> LoadAsync(string accountId, CancellationToken ct);
    Task SaveAsync(string accountId, BestiaryState state, CancellationToken ct);
    Task MergeAsync(string accountId, RunHistoryRecord record, CancellationToken ct);
}
```

- `LoadAsync`: ファイルが無ければ `BestiaryState.Empty` を返す。存在すれば `BestiaryStateSerializer.Deserialize`。
- `SaveAsync`: JSON を書き込む（上書き）。
- `MergeAsync`: `LoadAsync → BestiaryUpdater.Merge → SaveAsync` の合成。

### 2. `FileBestiaryRepository`

`src/Server/Services/FileBacked/FileBestiaryRepository.cs`
- `{root}/bestiary/{accountId}.json` に保存。
- UTF-8、改行は環境依存で OK（JSON 整形は `JsonOptions.Default` に従う）。
- `DataStorageOptions.RootDirectory` が未設定なら例外（他 Repository と同じ挙動）。

### 3. `BestiaryController`

`src/Server/Controllers/BestiaryController.cs` — `[Route("api/v1/bestiary")]`

```
GET /api/v1/bestiary
  ヘッダ: X-Account-Id
  レスポンス: BestiaryDto
  - アカウントが無い: 404
  - Bestiary ファイル無し: Empty 相当の DTO を返す (200)
```

### 4. `BestiaryDto`

`src/Server/Dtos/BestiaryDto.cs`

```csharp
public sealed record BestiaryDto(
    int SchemaVersion,
    IReadOnlyList<string> DiscoveredCardBaseIds,
    IReadOnlyList<string> DiscoveredRelicIds,
    IReadOnlyList<string> DiscoveredPotionIds,
    IReadOnlyList<string> EncounteredEnemyIds,
    // Catalog 由来の全 ID（質問 6 C 案 + 質問 1 枠内：未発見の ID をデバッグ表示用に含める）
    IReadOnlyList<string> AllKnownCardBaseIds,
    IReadOnlyList<string> AllKnownRelicIds,
    IReadOnlyList<string> AllKnownPotionIds,
    IReadOnlyList<string> AllKnownEnemyIds);
```

- `AllKnown*` は `DataCatalog` から抽出（Controller 内で算出）。
  - `AllKnownCardBaseIds`: `CardDefinitions.Keys` から base_id ユニーク化（`strike+` → `strike`）。
  - `AllKnownRelicIds`: `RelicDefinitions.Keys`。
  - `AllKnownPotionIds`: `PotionDefinitions.Keys`。
  - `AllKnownEnemyIds`: act1/act2/act3 の `EnemyDefinitions.Keys` をユニーク化。
- 配列は昇順ソート済みで返す（Client 側の安定表示と testability 確保のため）。

### 5. DI 登録

`src/Server/Program.cs` に `FileBestiaryRepository` の DI 登録を追加。

### 6. `RunResultDto` 拡張

`src/Server/Dtos/RunResultDto.cs` に 4 セットを追加:

```csharp
IReadOnlyList<string> SeenCardBaseIds,
IReadOnlyList<string> AcquiredRelicIds,
IReadOnlyList<string> AcquiredPotionIds,
IReadOnlyList<string> EncounteredEnemyIds
```

`RunSnapshotDtoMapper.ToResultDto(record)` で `RunHistoryRecord` の同名フィールドをマッピング。

### 7. ラン終了時のマージ呼び出し

`RunsController` で既に `history.AppendAsync(record)` を呼んでいる箇所すべて（Cleared / Abandon / GameOver）の直後に:

```csharp
await _bestiary.MergeAsync(accountId, record, ct);
```

を追加する。`_bestiary` は `IBestiaryRepository` を DI 注入。

---

## Client

### 1. API 層

**新規 `src/Client/src/api/bestiary.ts`**
```typescript
export async function fetchBestiary(accountId: string): Promise<BestiaryDto> {
  return apiRequest<BestiaryDto>('GET', '/bestiary', { accountId })
}
```

**型追加 `src/Client/src/api/types.ts`**
- `BestiaryDto` 型定義
- `RunResultDto` に 4 セットフィールド追加

### 2. `AchievementsScreen`

`src/Client/src/screens/AchievementsScreen.tsx`

- Props: `{ accountId: string; onBack: () => void }`
- State: 現在タブ（'cards' | 'relics' | 'potions' | 'enemies' | 'history'）
- Mount 時: `fetchBestiary(accountId)` と `listHistory(accountId)` を並列 fetch（Promise.all）
- ローディング中: 「読み込み中...」
- エラー時: シンプルな「読み込み失敗」メッセージ + 「戻る」ボタン

**タブ 1〜4（図鑑）:**
- 見出し: `3 / 12 発見`
- リスト要素:
  - 発見済み: `✓ {display_name} ({id})`
  - 未発見（カード/レリック/ポーション）: `??? (acid_splash)` — ID を小さく括弧で表示
  - 未発見（モンスター）: `??? (act2_brute)`
- 並び順: `AllKnown*` の昇順（DTO から既ソート）
- display_name: Phase 8 時点ではカタログを別途 fetch しない。ID をそのまま表示 or カタログの name をクライアントがどこかで持っていれば使う。**簡単のため ID そのまま表示**（後で local catalog 経由で name 解決に置換する余地を残す）

**タブ 5（履歴）:**
- 1 行 = 1 ラン、サマリー行クリックで展開（アコーディオン）
- サマリー: `[{Outcome}] Act{ActReached} / {mm:ss} / {EndedAtUtc}`
- 展開部:
  - 最終 HP: `{FinalHp}/{FinalMaxHp}`
  - 最終 Gold: `{FinalGold}`
  - 最終デッキ: カード ID リスト（`+` で強化版表示）
  - 最終レリック: ID リスト
  - ── 以下 v2 追加情報（v1 履歴は空）──
  - 見たカード: base_id リスト
  - 入手レリック: ID リスト
  - 入手ポーション: ID リスト
  - 遭遇敵: ID リスト
- リストが空なら "（なし）"
- 履歴 0 件: 「履歴なし」と表示

**「戻る」ボタン:** 画面上部 or 下部に配置、`onBack` 発火。

### 3. `MainMenuScreen` 改修

- 既存の「実績」ボタン（未実装 or 仮実装なら追加）を活性化
- クリックで `onAchievements` プロパティを発火
- `MainMenuScreen` の Props に `onAchievements: () => void` 追加

### 4. `App.tsx` 画面遷移

- screen state に `'achievements'` を追加
- `MainMenuScreen` の `onAchievements` → `setScreen('achievements')`
- `AchievementsScreen` の `onBack` → `setScreen('menu')`

---

## Migration

### RunState v5 → v6
`RunStateSerializer` のマイグレーション関数に v5→v6 ケースを追加:
- 既存フィールドはそのまま
- `SeenCardBaseIds`, `AcquiredRelicIds`, `AcquiredPotionIds`, `EncounteredEnemyIds` を `ImmutableArray<string>.Empty` で埋める

テスト: `RunStateSerializerMigrationTests.V5_To_V6_FillsEmptyBestiarySets`

### RunHistoryRecord v1 → v2
`FileHistoryRepository` デシリアライズ時に schemaVersion を確認。
- v1 の場合、4 セットを空配列として読み込む（System.Text.Json が欠落フィールドを空として扱えない場合は手動で migrate）
- 実装: record を直接デシリアライズせず、JsonNode 経由でバージョンチェック → 必要なら空配列を注入 → 再シリアライズ → record にデシリアライズ

テスト: `FileHistoryRepositoryTests.Load_V1_File_FillsEmptyBestiarySets`

### Bestiary schema
- v1 のみ。マイグレーション不要。

---

## Testing

### Core
- `BestiaryState`: Empty 定数、等価性
- `BestiaryTracker`: 4 関数それぞれで追加・重複冪等・null/空 no-op
- `BestiaryUpdater.Merge`: 全カテゴリの和集合、冪等、Empty と record のマージ
- `BestiaryStateSerializer`: ラウンドトリップ、ID 昇順出力の決定性
- `RunStateSerializer`: v5 → v6 migration
- `RunHistoryBuilder.From`: 4 セットが RunState からコピーされる

### Server
- `FileBestiaryRepository`: Save → Load ラウンドトリップ、存在しないアカウント → Empty、Merge 呼び出しでファイルが更新される
- `BestiaryController`:
  - account header 無し → 400
  - 未知 account → 404
  - 既知 account, Bestiary 無し → 200 + Empty 相当
  - 既知 account, 既存 Bestiary → 200 + 内容、`AllKnown*` が昇順
- `RunsController` ラン終了時の自動マージ:
  - battle/win Cleared 時に `bestiary.MergeAsync` が呼ばれる
  - abandon 時に呼ばれる
  - debug/damage で GameOver になった時に呼ばれる（既存テストを拡張）
- `RunResultDto` マッピング: 4 セットが往復する
- `FileHistoryRepository`: v1 ファイル読み込みで 4 セット空配列
- 各 tracker の呼び出し点統合テスト:
  - 新規ラン開始 → 初期デッキが SeenCardBaseIds に入る
  - 戦闘入場 → 敵が EncounteredEnemyIds に入る
  - 報酬 3 択 → 3 択 base_id が SeenCardBaseIds に入る
  - 商人マス進入 → 在庫 base_id が SeenCardBaseIds に入る
  - レリック入手 → AcquiredRelicIds に入る
  - ポーション入手 → AcquiredPotionIds に入る

### Client
- `api/bestiary.test.ts`: fetchBestiary のリクエスト URL とヘッダ
- `AchievementsScreen.test.tsx`:
  - Bestiary と履歴が並列 fetch される
  - タブ切替で表示カテゴリが変わる
  - 発見済み / 未発見の表示差分
  - 履歴 0 件: 「履歴なし」
  - 履歴行クリックでアコーディオン展開
- `MainMenuScreen.test.tsx`: 「実績」ボタンで onAchievements 発火
- `App.test.tsx`（あれば）: screen 'achievements' の遷移

---

## Done 判定

1. dotnet build / dotnet test / npm test すべて緑。
2. シングルランを 1 回完走、結果画面 → メイン画面 → 「実績」 → 履歴タブに自分のランが現れる。展開で最終デッキ・最終レリック・遭遇 4 セットが見える。
3. 同じ「実績」のカード/レリック/ポーション/モンスタータブで、完走中に遭遇したものが ✓、残りは `???` 表示。
4. 複数ラン跨いでアカウント単位で発見済みが蓄積される（再ログイン後も維持）。
5. v5 RunState セーブ / v1 History ファイルがあるアカウントを起動しても migration でクラッシュせず、旧履歴は Bestiary には反映されないが新履歴からは反映される。
