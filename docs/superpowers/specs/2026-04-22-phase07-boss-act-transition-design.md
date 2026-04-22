# Phase 7 — ボス・層移動・HP 全回復 設計書

**作成日**: 2026-04-22
**対象 Phase**: Phase 7 (roadmap `docs/superpowers/plans/2026-04-20-roadmap.md` 参照)

## 目的

各層（act）のゴールに配置されたボスマスを撃破すると次の層へ遷移し、遷移時に HP 全回復する一連のフローを実装する。加えて、act 開始時の特別レリック 3 択選択、HP 0 到達での GameOver、act 3 ボス撃破での Cleared、ラン終了時の履歴保存を実装する。

## 全体アーキテクチャ

採用アプローチ: **Approach A — 最小拡張（reward state 再利用）**

- 新 state は `ActStartRelicChoice`（act 開始レリック 3 択）のみ
- Boss 撃破時の報酬は既存 `RewardState` を使い回し、`IsBossReward` フラグで UI ラベル切替
- act 3 Boss 勝利時は `RewardState` を生成せず `ActTransition.FinishRun(Cleared)` へ直行
- Boss 専用レリックドロップは実装しない（既存の `Boss: never drop relic` を維持）
- 代わりに各 act 開始時に act 固有の特別レリックプールから 3 択で必ず 1 つ取得

## データ追加

### 新規ディレクトリ
```
src/Core/Data/RelicsActStart/
  act1.json  # { "relicIds": [...] } 5 種
  act2.json  # 5 種
  act3.json  # 5 種
```

### 新規 relic 定義（計 15 種）
`src/Core/Data/Relics/` に追加。命名は `act{N}_start_{NN}` 形式。

- **act 1 用 5 種**: 序盤向け汎用効果（例: Rest 回復量 +, Unknown マスでの gold 増加確率, 等）
- **act 2 用 5 種**: 中盤向け（例: Merchant 価格 -, Elite 戦後ボーナス, 等）
- **act 3 用 5 種**: 終盤向け強効果（例: MaxHp +20, 戦闘勝利時 HP 回復, 等）

Phase 7 段階では effect 実装は OnPickup トリガで完結する範囲のみ。Phase 10（本格バトル）で必要な効果は OnPickup 空実装のまま定義だけ用意する。

### act 2, 3 用 encounter / enemy（最小実装 + TODO 方針）
- act 2: enemy 3〜5 種、encounter Weak 2 / Strong 2 / Elite 1 / Boss 1
- act 3: enemy 3〜5 種、encounter Weak 2 / Strong 2 / Elite 1 / Boss 1

既存 act 1 データと同形式。Phase 10 到来時に拡充する前提で、Phase 7 では「3 act 通せる最小データ」を用意する。

## Core スキーマ拡張

### `RunState` v4 → v5

追加フィールド:
- `string RunId` — 新規 run 作成時に `Guid.NewGuid().ToString()`。履歴との突合キー。
- `ActStartRelicChoice? ActiveActStartRelicChoice = null`

既存フィールドに migration 不要（default 値で後方互換）。

### `RewardState` 拡張（schema version 変更なし）
- `bool IsBossReward = false` — UI ラベル切替用タグ

### `ActStartRelicChoice` 新規 record
```csharp
public sealed record ActStartRelicChoice(
    ImmutableArray<string> RelicIds);  // 長さ 3 固定
```

### `RunConstants`
```csharp
public static class RunConstants
{
    public const int MaxAct = 3;
}
```

## Core モジュール

### `src/Core/Run/ActTransition.cs`
```csharp
public static class ActTransition
{
    // act+1, HP 全回復, visitedNodeIds/unknownResolutions/active* 系をクリア,
    // encounter queue 再生成, 新マップの StartNodeId を currentNodeId に設定
    // newMap は呼び出し側（Server）が DungeonMapGenerator で生成して渡す
    public static RunState AdvanceAct(
        RunState state, DungeonMap newMap, DataCatalog catalog, IRng rng);

    // progress を Cleared / GameOver / Abandoned に変更, savedAtUtc 更新
    public static RunState FinishRun(RunState state, RunProgress outcome);
}
```

### マップ seed の act 間派生
現状 `RunState.RngSeed` は run 単位の単一 seed で、`RunStartService.RehydrateMap` が `rngSeed` からマップを再生成する構造。act 遷移ごとに別マップが必要なので、**act ごとに決定論的な seed を派生**する:

- `ulong ActMapSeed(ulong runSeed, int act) => unchecked(runSeed * 2654435761UL + (ulong)act)` のようなハッシュ派生
- `RehydrateMap` / act 遷移時の新マップ生成とも、この派生式を使う
- RunState 自体には追加フィールドを足さず、`RngSeed + CurrentAct` の組み合わせでマップを一意に特定

### `src/Core/Run/ActStartActions.cs`
```csharp
public static class ActStartActions
{
    // 所持していない act-start relic pool から 3 つ抽選 (act 固有プール)
    public static ActStartRelicChoice GenerateChoices(
        RunState state, int act, DataCatalog catalog, IRng rng);

    // 指定 relic を所持に追加、OnPickup 発火、ActiveActStartRelicChoice をクリア
    public static RunState ChooseRelic(
        RunState state, string relicId, DataCatalog catalog);
}
```

### `src/Core/Run/BossRewardFlow.cs`
```csharp
public static class BossRewardFlow
{
    // Boss 戦勝利時の分岐判定:
    // - act < MaxAct: RewardState { IsBossReward: true } を返す
    // - act == MaxAct: null（呼び出し側が FinishRun(Cleared) を呼ぶ）
    public static RewardState? GenerateBossReward(
        RunState state, DataCatalog catalog, IRng rng);
}
```

### 既存 `NodeEffectResolver.cs` の変更
- Start tile（`TileKind.Start`）入場時（＝プレイヤーが Start マスをクリック → move API 経由で呼び出し）: `ActStartActions.GenerateChoices` を呼び `ActiveActStartRelicChoice` をセット
- 既存の Merchant / Event / Rest と同じパターン

### Start tile の扱いと不変条件の調整
現状の `RunState` には不変条件 `VisitedNodeIds.Contains(CurrentNodeId)` があり、`NewSoloRun` では `VisitedNodeIds = [startNodeId]` として Start を即 visited 扱いしている。これを以下のように変更する:

- **`NewSoloRun` / `AdvanceAct` 直後**: `VisitedNodeIds = []`（Start 未訪問）、`CurrentNodeId = StartNodeId`、`ActiveActStartRelicChoice = null`
- **不変条件の緩和**: `ActiveActStartRelicChoice != null` または `VisitedNodeIds.Contains(CurrentNodeId)` のどちらかを満たせば OK（Start クリック前の「current だが未訪問」状態を許容）
- **Start クリック処理**: `moveToNode(startNodeId)` 相当の API 呼び出しで NodeEffectResolver が Start tile を処理し、`ActStartActions.GenerateChoices` 発火 + `VisitedNodeIds.Add(startNodeId)`
- **`isSelectable` 判定（Client）**: currentNodeId が Start でかつ visitedNodeIds に含まれていない状態では、Start tile 自身が selectable（クリック可）。outgoing は modal 閉じるまで不可。

### `src/Core/Run/DebugActions.cs`（dev 用途、Core に含めて OK）
```csharp
public static class DebugActions
{
    // 単純に CurrentHp を減らすだけ（0 クランプ）
    // HP 判定 / FinishRun 呼び出しは Server 側の責務
    public static RunState ApplyDamage(RunState state, int amount);
}
```

### History モジュール
```csharp
// src/Core/History/RunHistoryRecord.cs
public sealed record RunHistoryRecord(
    int SchemaVersion,           // 1 (History 独自スキーマ)
    string AccountId,
    string RunId,
    RunProgress Outcome,
    int ActReached,
    int NodesVisited,
    int PlaySeconds,
    string CharacterId,
    int FinalHp,
    int FinalMaxHp,
    int FinalGold,
    ImmutableArray<CardInstance> FinalDeck,
    ImmutableArray<string> FinalRelics,
    DateTimeOffset EndedAtUtc);

// src/Core/History/RunHistoryBuilder.cs
public static class RunHistoryBuilder
{
    public static RunHistoryRecord From(
        string accountId, RunState state, int nodesVisited, RunProgress outcome);
}
```

## Server 拡張

### 新規エンドポイント

#### `POST /api/v1/act-start/choose`
- body: `{ relicId: string }`
- 422 if `ActiveActStartRelicChoice == null` or relicId が 3 択に含まれない
- 200 → 更新後 `RunSnapshotDto`

#### `POST /api/v1/debug/damage` (Development 環境限定)
- body: `{ amount: int }`
- `DebugActions.ApplyDamage` 実行後、Controller 側で HP ≤ 0 を判定
  - HP ≤ 0 なら: `ActTransition.FinishRun(GameOver)` → 履歴保存 → current run 削除 → `RunResultDto` 返却
  - HP > 0 なら: `RunSnapshotDto` 返却
- `IHostEnvironment.IsDevelopment()` で endpoint 登録分岐。本番ビルドで 404

#### `GET /api/v1/history`
- 全履歴を新しい順に返却: `RunHistoryRecordDto[]`
- ページング未対応（Phase 8 で必要になれば追加）

#### `GET /api/v1/history/last-result`（Phase 7 用）
- 直近 1 件の履歴を返却: `RunHistoryRecordDto`
- Phase 7 では `RunResultDto` と同形式

### 既存エンドポイント挙動変更

#### `POST /api/v1/rewards/proceed`
- `ActiveReward.IsBossReward && currentAct < MaxAct`:
  - 従来の proceed ではなく `ActTransition.AdvanceAct` 実行（新マップ生成含む）
- `IsBossReward && currentAct == MaxAct` はこの分岐に到達しない（Boss 勝利時に reward 生成しないため）

#### `POST /api/v1/battle/win`
- 勝利 encounter が act N の Boss:
  - N < MaxAct: `RewardState { IsBossReward: true }` を生成
  - N == MaxAct: reward 生成せず `ActTransition.FinishRun(Cleared)` → 履歴保存 → current run 削除 → `RunResultDto` 返却
- 上記以外: 従来通り `RewardState { IsBossReward: false }`

#### `POST /api/v1/runs/abandon`
- 履歴保存処理を追加: `Outcome=Abandoned` で `RunHistoryRecord` を保存してから current run 削除

#### `POST /api/v1/runs/move`
- 既存の move 処理内で NodeEffectResolver が Start tile 入場を判定し `ActiveActStartRelicChoice` を生成。新規エンドポイントは追加しない（責務集中）。
- Start tile 入場時は currentNodeId を Start に設定、outgoing 選択は relic 選択完了まで不可（modal ブロック）

### History 保存パス
```
{Server working directory}/data-local/history/{accountId}/{ISO8601 timestamp}_{runId}.json
```

### DTO 追加
- `ActStartRelicChoiceDto`
- `RunStateDto.activeActStartRelicChoice: ActStartRelicChoiceDto | null`
- `RewardStateDto.isBossReward: boolean`
- `RunHistoryRecordDto` / `RunResultDto`（両者同形式）

## Client 拡張

### 新規画面
- `src/Client/src/screens/ActStartRelicScreen.tsx`
  - props: `{ choices: string[]; onChoose: (relicId: string) => void }`
  - 3 つの relic を並べて表示（名前＋説明）。スキップボタンなし。`role="dialog" aria-modal="true"`
  - Merchant / Event と同じデバッグ UI スタイル
- `src/Client/src/screens/RunResultScreen.tsx`
  - props: `{ result: RunResultDto; onReturnToMenu: () => void }`
  - 表示: Cleared/GameOver ラベル、playSeconds (hh:mm:ss)、actReached、nodesVisited、finalHp/MaxHp、finalGold、finalRelics 一覧、finalDeck 一覧
  - 「メニューへ戻る」ボタンのみ

### 既存画面変更

#### `MapScreen.tsx`
- `activeActStartRelicChoice` を snapshot から読み、non-null なら `ActStartRelicScreen` を modal 表示
- スキップ不可なので `isCurrentReopenable` への登録不要（閉じれない modal）
- Start tile クリック時は既存 `moveToNode` を呼ぶ。サーバー側の NodeEffectResolver が自動的に relic choice state を生成

#### `RewardPopup.tsx`
- `reward.isBossReward && currentAct < MaxAct` の場合、proceed ボタンラベルを「次の層へ」に切替

#### `TopBar.tsx`
- `import.meta.env.DEV === true` のとき「DEBUG -10HP」ボタンを表示
- `onDebugDamage?: () => void` prop 追加

#### `BattleOverlay.tsx`
- 同様に `import.meta.env.DEV` ガードで「DEBUG -10HP」ボタン追加
- `onDebugDamage?: () => void` prop 追加

#### `MainMenuScreen`（既存）
- `getCurrentRun()` が null を返す場合は「続きから」ボタンを非表示にし、「新規ラン開始」のみ表示
- 現状は両ボタン常時表示なので、current run の有無で分岐する

#### App root (screen switcher)
- `snap.run.progress === 'Cleared' || 'GameOver' || 'Abandoned'` の snapshot を受け取った場合は `RunResultScreen` へ遷移
- ただし通常フローでは FinishRun 時にサーバーから `RunResultDto` を直接受け取る（current run は既に削除済み）ので、battle/win や debug/damage のレスポンスで `RunResultDto` が返ったらそのまま `RunResultScreen` 表示

### API client ラッパ
- `src/Client/src/api/actStart.ts` — `chooseActStartRelic(accountId, relicId)`
- `src/Client/src/api/debug.ts` — `applyDebugDamage(accountId, amount)`（dev only）
- `src/Client/src/api/history.ts` — `getLastResult(accountId)`, `getHistory(accountId)`

## 状態遷移フロー

### 新規 run 開始 〜 act 1 ボス撃破
1. `POST /api/v1/runs` で新規 run 作成 → `RunId = Guid.NewGuid()`, `currentAct = 1`, `VisitedNodeIds = []`, `CurrentNodeId = StartNodeId`, `ActiveActStartRelicChoice = null`
2. Client は map 画面表示、Start tile は current かつ未訪問（selectable）
3. プレイヤーが Start tile クリック → `moveToNode(startNodeId)` → サーバーが `NodeEffectResolver` で Start tile を処理 → `ActiveActStartRelicChoice` 生成（act 1 プールから 3 択）＋ `VisitedNodeIds.Add(startNodeId)`
4. Client が `ActStartRelicScreen` 表示 → プレイヤーが 1 つ選択 → `POST /api/v1/act-start/choose`
5. 選択された relic を所持追加、OnPickup 発火、`ActiveActStartRelicChoice = null`
6. Start tile 完了扱い、outgoing マスが selectable に
7. 通常プレイ: enemy / merchant / event / rest / elite を経由
8. Boss tile 入場 → bat ttle → 勝利 → `POST /api/v1/battle/win`
9. サーバー: act 1 Boss (< MaxAct) → `RewardState { IsBossReward: true }` 生成
10. Client: `RewardPopup` 表示、proceed ボタンラベルは「次の層へ」
11. Player が「次の層へ」クリック → `POST /api/v1/rewards/proceed`
12. サーバー: `ActTransition.AdvanceAct` 実行（act=2, HP=MaxHp, 新マップ生成, 新 encounter queue, active* クリア, visitedNodeIds=[new start], currentNodeId=new start）
13. Client: 新しい map snapshot を受信、Start tile クリックで act 2 のレリック選択（3 へループ）

### act 3 Boss 撃破 → Cleared
1. act 3 Boss 勝利 → `POST /api/v1/battle/win`
2. サーバー: act == MaxAct → `ActTransition.FinishRun(Cleared)` → `RunHistoryBuilder.From(...)` → 履歴 JSON 保存 → current run 削除
3. レスポンスで `RunResultDto` 返却
4. Client: `RunResultScreen` 表示
5. 「メニューへ戻る」→ メニュー画面（current run なし → 「新規ラン開始」のみ）

### HP 0 到達 → GameOver
1. 任意のアクション（現状は `POST /api/v1/debug/damage` のみ）で `CurrentHp = 0`
2. Controller が HP ≤ 0 を検知 → `ActTransition.FinishRun(GameOver)` → 履歴保存 → current run 削除
3. レスポンスで `RunResultDto` 返却
4. Client: `RunResultScreen` 表示（GameOver ラベル）
5. Phase 10 本格バトル実装時: `BattleActions` 内でプレイヤー HP 更新時に同じ HP ≤ 0 判定を呼ぶ

### ラン中断（Abandon）
1. InGame menu から「放棄」選択 → `POST /api/v1/runs/abandon`
2. サーバー: 履歴保存（Outcome=Abandoned） → current run 削除
3. Client: メニュー画面へ戻る

## Migration / 後方互換

### v4 セーブの扱い
- `schemaVersion: 4` のセーブをロード時:
  - 新フィールド `RunId` が null/空 → `Guid.NewGuid().ToString()` で補う
  - `ActiveActStartRelicChoice` → `null` で補う（既存 run は act-start relic スキップ済みとみなす。次の act 遷移から機能開始）
  - `RewardState.IsBossReward` → `false`（ある場合）
  - `VisitedNodeIds` は既に Start を含んでいるため Start クリックフローは発動しない（自然と「今回の run は act-start relic スキップ」として扱われる）
- 次セーブ時に schemaVersion: 5 で書き戻し

### History schema
- 独立管理（RunState とは別）。SchemaVersion 1 で新規開始。
- 将来拡張時は History 側で独自に version 上げ。

## Testing 戦略

### Core.Tests 新規
- `ActTransitionTests`
  - AdvanceAct: act+1, HP=MaxHp, visitedNodeIds リセット, 新 currentNodeId = new Start
  - AdvanceAct: encounter queue 再生成
  - AdvanceAct: deck / relics / potions / gold / maxHp / playSeconds / DiscardUsesSoFar 持ち越し
  - FinishRun(Cleared/GameOver/Abandoned): progress 更新, savedAtUtc 更新
- `ActStartActionsTests`
  - GenerateChoices: act プールから 3 つ抽選、重複なし
  - GenerateChoices: 所持済み relic を除外（act プール内で所持済みがあった場合）
  - ChooseRelic: 所持追加, OnPickup 発火, ActiveActStartRelicChoice=null
  - ChooseRelic: 3 択に含まれない id で ArgumentException
- `BossRewardFlowTests`
  - act 1/2 Boss → IsBossReward: true の reward
  - act 3 Boss → null 返却
- `NodeEffectResolverTests`（既存に追加）
  - Start tile 入場で ActiveActStartRelicChoice 生成
- `RunStateJsonLoaderTests`（既存に追加）
  - v4 → v5 migration: RunId 自動発行, 新フィールド default
- `RunHistoryBuilderTests`
  - RunState から RunHistoryRecord が正しく構築される
- `DebugActionsTests`
  - ApplyDamage: HP 減算、0 クランプ

### Server.Tests 新規
- `ActStartControllerTests` — choose endpoint の正常/異常系
- `DebugControllerTests` — dev only ガード、damage の HP 減算 + GameOver 遷移
- `HistoryControllerTests` — list / last-result
- 既存 `RewardsControllerTests` に `proceed` のボス分岐テスト追加
- 既存 `BattleControllerTests` に act 3 Boss 勝利 → RunResultDto 返却テスト追加
- 既存 `RunsControllerTests` の abandon に履歴保存検証追加

### Client.Tests 新規
- `ActStartRelicScreen.test.tsx` — 3 択表示、onChoose 呼び出し
- `RunResultScreen.test.tsx` — 各フィールド表示、onReturnToMenu
- 既存 `MapScreen.test.tsx` に ActStartRelicScreen 表示テスト追加
- 既存 `RewardPopup.test.tsx` に IsBossReward ラベル分岐テスト追加
- `TopBar.test.tsx` / `BattleOverlay.test.tsx` に debug ボタン表示テスト（`import.meta.env.DEV` モック）

### E2E 手動チェックリスト
- [ ] act 1 Start 入場 → relic 3 択表示 → 選択 → Start 完了 → row 1 へ進める
- [ ] act 1 Boss 勝利 → 「次の層へ」ボタン → act 2 マップ表示, HP MaxHp まで回復, Start tile で act 2 relic 3 択
- [ ] act 2 Boss 勝利 → 同上 → act 3
- [ ] act 3 Boss 勝利 → 報酬スキップで直接 RunResultScreen (Cleared)
- [ ] Debug -10HP を TopBar から実行 → HP 0 で即 RunResultScreen (GameOver)
- [ ] Debug -10HP を BattleOverlay から実行 → 同上
- [ ] RunResultScreen → 「メニューへ戻る」→ 「続きから」非表示, 「新規ラン」のみ
- [ ] Abandon → メニュー戻り, 「新規ラン」のみ
- [ ] 履歴ファイル `data-local/history/{accountId}/` が生成されている
- [ ] v4 セーブ（既存 test.json）がロードできる

## Done 判定（roadmap 基準）

- シングルランを通しで 1 回完走でき、結果画面に到達する ✓
- クリア時／ゲームオーバー時に正しく保存され、次回ログイン時は新規開始になる（前回セーブはプレイ履歴へ送られる）✓

## 実装順序（概略、writing-plans で詳細化）

1. Core: RunState v5 + RunId + migration
2. Core: ActStartRelicChoice, RelicsActStart loader
3. Core: ActTransition.AdvanceAct / FinishRun
4. Core: ActStartActions
5. Core: BossRewardFlow
6. Core: DebugActions
7. Core: History module (RunHistoryRecord, RunHistoryBuilder)
8. Core: NodeEffectResolver の Start 対応
9. Core: encounter / enemy / relic JSON データ追加 (act 2, 3 + relic 15 種)
10. Server: DTO 更新, history ストレージ層
11. Server: act-start / debug / history エンドポイント
12. Server: battle/win, rewards/proceed, runs/abandon の分岐追加
13. Client: api ラッパ, 型定義
14. Client: ActStartRelicScreen, RunResultScreen
15. Client: RewardPopup / TopBar / BattleOverlay の調整
16. Client: MapScreen / App root の遷移ロジック
17. Client: Main menu の「続きから」条件分岐
18. E2E 手動検証
