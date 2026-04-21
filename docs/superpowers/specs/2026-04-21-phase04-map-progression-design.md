# Phase 4 — マップ進行（マス選択）とセーブ連動 設計書

**日付:** 2026-04-21
**スコープ:** プレイヤーが Phase 3 で生成されたマップ上を移動し、セーブ／再開／放棄できる状態までを完成させる。戦闘や商人画面など「マス踏んだ後の処理」は Phase 5-7 で乗せる前提で、Phase 4 ではマスの「中身（resolved kind）」を返すところまで。
**前提タグ:** `phase3-complete`（map generator 完了）、および Phase 1 のセーブシステム、Phase 2 のメインメニュー／ログイン／設定がマージ済み。

---

## 1. スコープとゴール

**含む**
- `RunState` スキーマの v2 昇格（マップ進行に必要なフィールド追加、Phase 1 のプレースホルダ `CurrentTileIndex` 削除）
- Unknown マスを具体 TileKind に解決する抽選ロジック（`UnknownResolver`）
- ラン開始・現在状態取得・移動・放棄の REST エンドポイント
- クライアント: SVG マップ描画、ノード選択 UI、ダンジョン内メニュー（モーダルオーバーレイ）
- 既存 `MainMenuScreen` の「シングルプレイ」導線を実装（新規 / 続きから確認ダイアログ）
- プレイ秒数の自動加算（クライアント側計測 → セーブ時にサーバ加算）

**含まない（後続フェーズ）**
- 戦闘処理（`Enemy` / `Elite` / `Boss` マスに入っても即勝利扱いすらしない、Phase 4 では `resolved kind` を表示するだけで止まる）
- 商人・宝箱・休憩・イベントの具体画面（Phase 6）
- Act 移動とボス撃破後の HP 回復（Phase 7）
- プレイ履歴（Abandoned を履歴ファイルに残す処理、Phase 8）
- Unknown 抽選先候補への `Event` 追加（Phase 6）

**運用上の到達点**
- 新規ラン → MapScreen → ノード選択 → 移動 → リロードで同じ状態から再開、までが手動で確認できる
- 「あきらめる」→ Main Menu で「保存済みラン有り」バッジが消え、シングル押下でダイアログ無し新規開始ができる
- ボス（Row 16）に到達して選択しても、Phase 4 ではそこで止まる（勝利判定は未実装）

---

## 2. 全体アーキテクチャ

### 層構成

```
Client (React)
  MapScreen + InGameMenuScreen
    ↓ HTTP
Server (ASP.NET Core)
  RunsController
    ↓ 呼び出し
Core
  Run / Map / Random
```

- 通信方式は REST のみ（Phase 2 と一貫）。SignalR は Phase 9 でマルチプレイのため必要になったら追加。
- Core は UI／通信非依存の原則を維持（CLAUDE.md の規約通り）。
- サーバは Core のロジックを呼ぶ薄いレイヤ。マップ生成・Unknown 解決・移動バリデーションはすべて Core 側に閉じる。

### 責務分離

| 層 | 新規成果物 | 責務 |
|---|---|---|
| Core/Run | `RunActions.SelectNextNode` | 現在地から隣接ノードへの移動を `RunState` に反映（純関数） |
| Core/Run | `RunState` v2 スキーマ | 現在地・訪問履歴・Unknown 解決結果を保持 |
| Core/Map | `UnknownResolver` + `UnknownResolutionConfig` | マップ生成直後に全 Unknown を具体 kind に解決（純関数） |
| Server/Services | `RunStartService` | seed 発行 → map 生成 → UnknownResolver 呼び出し → RunState 構築 → 保存 |
| Server/Controllers | `RunsController` 拡張 | `current` / `new` / `move` / `abandon` / `heartbeat` の 5 エンドポイント |
| Client/api | `runs.ts` 拡張 | 上記 5 エンドポイントのクライアント関数 |
| Client/screens | `MapScreen.tsx` | SVG マップ描画、ノード選択、ゲアイコン |
| Client/screens | `InGameMenuScreen.tsx` | モーダルオーバーレイ（あきらめる／メニューに戻る／音量設定） |
| Client/App | ルーティング追加 | `MainMenu` ↔ `MapScreen` の画面遷移 |

---

## 3. RunState v2 スキーマ

### 変更点

削除:
```csharp
int CurrentTileIndex  // Phase 1 のプレースホルダ
```

追加:
```csharp
int CurrentNodeId
IReadOnlyList<int> VisitedNodeIds
IReadOnlyDictionary<int, TileKind> UnknownResolutions
```

`SchemaVersion` は `1` → `2` に引き上げ。

### 新規ラン時の初期値
- `CurrentNodeId = map.StartNodeId`
- `VisitedNodeIds = [map.StartNodeId]`（Start は訪問済みとして扱う）
- `UnknownResolutions` は `UnknownResolver.ResolveAll(map, config, rng)` の結果（ラン開始時に一度だけ決定、以降不変）

### 既存 v1 セーブの扱い
- Core の `RunStateSerializer` は `SchemaVersion != CurrentSchemaVersion (= 2)` を検出したら `InvalidOperationException` を投げる。
- `FileSaveRepository.TryLoadAsync` はそれをキャッチし、`null` を返す（＝「セーブ無し」扱い、呼び出し側には 204 を返す）。
- v1 → v2 の移行ロジックは**書かない**（ユーザがまだ居ない開発段階のため。将来本番運用前に必要になったら別フェーズで追加）。

### 不変条件（`RunState.Validate` で検査、サーバで入り口チェック）
- `VisitedNodeIds` は `CurrentNodeId` を含む
- `VisitedNodeIds` は重複無し
- `UnknownResolutions` のキーは全て有効なノード ID（`0 <= id < map.Nodes.Length`）
- `UnknownResolutions` の値は `Enemy` / `Elite` / `Merchant` / `Rest` / `Treasure` のみ（`Unknown` / `Start` / `Boss` は不可）

---

## 4. Unknown 抽選（`UnknownResolver`）

### 配置
- `src/Core/Map/UnknownResolver.cs`（静的クラス）
- `src/Core/Map/UnknownResolutionConfig.cs`（record、`ImmutableDictionary<TileKind, double> Weights`）

### API

```csharp
public static class UnknownResolver
{
    /// <summary>
    /// Map 内の全 Unknown ノードについて、重み付きランダムで具体 kind を抽選。
    /// 順序は Node.Id 昇順で決定的。
    /// </summary>
    public static ImmutableDictionary<int, TileKind> ResolveAll(
        DungeonMap map, UnknownResolutionConfig config, IRng rng);
}
```

### 設定

`map-act1.json` に新ブロックを追加：

```json
"unknownResolutionWeights": {
  "Enemy": 48,
  "Merchant": 24,
  "Rest": 24,
  "Treasure": 4
}
```

将来 `Event` 追加時は 1 行足すだけで済むよう、`ImmutableDictionary<TileKind, double>` として読み込む。

### 抽選先の制限
- 許可: `Enemy` / `Elite` / `Merchant` / `Rest` / `Treasure`
- 禁止: `Unknown` / `Start` / `Boss`
- 許可されない kind が weights に含まれていたら `MapGenerationConfigException`
- `Elite` は許可されるが、Phase 4 の act1 config では `unknownResolutionWeights` 辞書自体に含めない（＝ 0 重みと等価）

### 決定性
- `map` のノード順序（Id 昇順）で反復、同じ `IRng` 状態から抽選開始すれば同じ結果
- Act 1 マップは Unknown が最大でも 20 個程度、処理は O(ノード数)

---

## 5. `RunActions.SelectNextNode`

### 配置
- `src/Core/Run/RunActions.cs`（静的クラス）

### API

```csharp
public static class RunActions
{
    /// <summary>
    /// 現在地から target ノードへの移動を反映した新しい RunState を返す。
    /// 成功条件: target が 現在ノードの OutgoingNodeIds に含まれる。
    /// 違反時: ArgumentException（サーバ側が 400 にマップ）。
    /// </summary>
    public static RunState SelectNextNode(RunState state, DungeonMap map, int targetNodeId);
}
```

### 更新内容
- `CurrentNodeId` = `targetNodeId`
- `VisitedNodeIds` に `targetNodeId` を append（重複チェックはしない。前提: マップは DAG なので同じノードには戻れない）
- `PlaySeconds` はこの関数では触らない（サーバのセーブ時に加算）
- `SavedAtUtc` はこの関数では触らない（サーバのセーブ時に設定）

### 検査
- `targetNodeId` が `state.CurrentNodeId` の `OutgoingNodeIds` にあるか
- ノード ID が map に存在するか

### 進行の終わり
- ボス（Row 16）に移動したら `CurrentNodeId = bossId`、`VisitedNodeIds` に boss が入るだけ。Phase 4 ではそれ以上何もしない（クライアントは「ここから先は Phase 5 以降」のような表示にしておく）

---

## 6. サーバ API

### 全エンドポイント一覧

| Method | Path | 用途 | Body / Response |
|---|---|---|---|
| GET | `/api/v1/runs/current` | 現在 InProgress のラン読み込み | `{ run, map }` or 204 |
| POST | `/api/v1/runs/new` | 新規ラン開始 | (空) / `{ run, map }` or 409 |
| POST | `/api/v1/runs/new?force=true` | 既存 InProgress があっても上書き | 同上、409 にならず常に作成 |
| POST | `/api/v1/runs/current/move` | ノード選択 | `{ nodeId, elapsedSeconds }` / 204 |
| POST | `/api/v1/runs/current/abandon` | ラン放棄 | `{ elapsedSeconds }` / 204 |
| POST | `/api/v1/runs/current/heartbeat` | 画面離脱時のプレイ秒数加算 | `{ elapsedSeconds }` / 204 |

全エンドポイントで `X-Account-Id` ヘッダ必須（Phase 2 方式と同一）。

### 旧 `/api/v1/runs/latest` の扱い
- Phase 1 で作られたが、Phase 4 では `/api/v1/runs/current` に**改名**。`latest` は残さない（未リリースのため後方互換不要）。
- 新仕様では `Progress == InProgress` のみ 200 + `{ run, map }` を返し、`Abandoned` / `Cleared` / `GameOver` のセーブは 204 扱い（= 「再開できるラン無し」）。

### `/api/v1/runs/new` の挙動
- 既存セーブがあり、かつ `Progress == InProgress` の場合 → 409 Conflict
- `?force=true` 付きなら既存を上書き
- リクエストボディは不要（新規ランは `PlaySeconds = 0` から開始するため、elapsedSeconds は送らない）
- サーバ内処理:
  1. `seed = new Random().NextInt64()`（ランごとに新しい seed、決定性は seed を保存することで確保）
  2. `map = _generator.Generate(new SystemRng(seed), _mapConfig)`
  3. `rngForResolutions = new SystemRng(seed + 1)` で `UnknownResolver.ResolveAll` を呼ぶ（同じ seed を二重消費しないため +1 する）
  4. `RunState.NewSoloRun(...)` で初期化（`CurrentNodeId = map.StartNodeId` 等）
  5. `_saves.SaveAsync(accountId, state)` で永続化
  6. `{ run: state, map: map }` を返す

### `/api/v1/runs/current/move` の挙動
- リクエスト `{ nodeId, elapsedSeconds }`
- 処理:
  1. 現在セーブをロード → `Progress != InProgress` なら 409
  2. Core: `updated = RunActions.SelectNextNode(state, map, nodeId)`
  3. `updated = updated with { PlaySeconds = state.PlaySeconds + elapsedSeconds, SavedAtUtc = now }`
  4. 保存 → 204
- `nodeId` がバリデーション失敗なら 400（`RunActions` の `ArgumentException` をキャッチ）
- map は seed から再生成（サーバがキャッシュしても良いがまずは都度生成、Act 1 マップ生成は ~5ms なので問題なし）

### `/api/v1/runs/current/abandon` の挙動
- 現在ラン `Progress == InProgress` → `Abandoned` にして PlaySeconds 加算のうえ保存、204
- `Progress != InProgress` なら 409
- Phase 4 では履歴ファイル作成はしない。保存ファイル自体は Abandoned 状態のまま残り、次回 `current` で 204 になるだけ

### `/api/v1/runs/current/heartbeat` の挙動
- MapScreen アンマウント時（＝メニュー戻る / タブ閉じる / Phase 4 時点では想定される唯一の離脱経路）に 1 回送信
- `PlaySeconds += elapsedSeconds` して保存、204
- 移動と同時には呼ばない（move がプレイ秒数を吸収する）

---

## 7. プレイ秒数の計測と加算

### クライアント側（MapScreen）
- `useEffect` で `mountedAt = performance.now()` を記録
- `move` / `abandon` / `heartbeat` 送信時に `elapsedSeconds = Math.floor((performance.now() - mountedAt) / 1000)` を計算し、リクエストボディに乗せる
- 送信後は `mountedAt = performance.now()` にリセット（二重加算防止）

### サーバ側
- 受信した `elapsedSeconds` は非負・上限あり（例: 86400 秒 = 1 日）。範囲外は `0` にクランプ（MVP では単純に `Math.Clamp`、エラー扱いはしない）
- `state with { PlaySeconds = state.PlaySeconds + clamped }` してから保存

### Done 判定
- 新規ラン → 10 秒待つ → 移動 → セーブ内 `PlaySeconds >= 10` （手動確認、ユニットテストはクランプ挙動のみ）

---

## 8. Client 画面構成

### MapScreen.tsx

レイアウト（SVG）:
- viewBox: `0 0 500 900` 相当（5 列 × 17 行分のグリッド）
- 列間隔: 100px、行間隔: 50px、ノード円半径: 20px
- Start は最下段（Row 0 相当、画面下）、Boss は最上段（Row 16 相当、画面上）。ユーザ視点で「下から上へ進む」Slay the Spire 風
- スクロール: マップ全体が画面に入るよう自動スケール。ビューポートより大きければ縦スクロールを許可

ノード描画:
- 未訪問 Unknown: `?` マーク（TileKind.Unknown と同じ）
- 未訪問その他: kind に応じたアイコン（Enemy: 剣、Elite: 2 本剣、Merchant: 店、Rest: 焚火、Treasure: 宝箱、Boss: 王冠）
- 訪問済み: 上記アイコンのグレーアウト版 + 中央に ✓
- 現在地: ゴールドの縁取り + pulse アニメーション
- 選択可能（current の OutgoingNodeIds）: 青い縁取り + クリック可能カーソル

エッジ描画:
- 直線（Phase 4 では曲線にしない、後でリファクタ可）
- 訪問済みルートは濃色、未踏ルートは薄色

インタラクション:
- 選択可能ノードクリック → `POST /runs/current/move` → 応答後に state 更新 → 再レンダ
- 右上ギアアイコン → `InGameMenuScreen` を開く
- Esc キー → `InGameMenuScreen` を開く
- ボス到達後: ボスノードが「現在地」だがそれ以上進めない状態。Phase 4 では画面下部に開発メッセージ（例：「ここから先は Phase 5 以降で実装されます」）を表示するだけ。Phase 5 以降で勝利フローに差し替える。

### InGameMenuScreen.tsx（モーダル）

- MapScreen の上に半透明黒背景 + 中央カード
- ボタン: 「続ける」（閉じる）／ 「音量設定」（Phase 2 の `SettingsScreen` を埋め込み）／ 「メニューに戻る」／ 「あきらめる」
- 「メニューに戻る」→ `heartbeat` 送信 → `MainMenu` へ遷移（セーブそのまま）
- 「あきらめる」→ 確認ダイアログ「本当にこのランを放棄しますか？」→ 確定で `abandon` → `MainMenu` へ遷移
- フォーカストラップ: モーダル内の tab 循環、Esc で閉じる
- a11y: `role="dialog"` `aria-modal="true"` `aria-labelledby`

### MainMenuScreen.tsx 更新
- 「シングルプレイ」押下 → 既存 `hasRun` ロジックを Progress 判定に拡張：
  - 取得した RunState が `Progress == InProgress` なら確認ダイアログ表示（「続きから」「新規で上書き」）
  - それ以外なら直接新規ラン作成（`POST /runs/new`）→ MapScreen へ遷移
- 「続きから」→ `GET /runs/current` で取得した `{ run, map }` を MapScreen に渡して遷移
- 「新規で上書き」→ `POST /runs/new?force=true` → MapScreen 遷移

### App.tsx ルーティング
- 既存: `LoginScreen` / `MainMenuScreen` / `SettingsScreen` 切替
- 追加: `MapScreen`（MainMenu から遷移）、`InGameMenuScreen`（MapScreen のモーダルなので独立 route は不要）
- 画面状態管理は既存パターン踏襲（context or useState の判断は実装時）

---

## 9. テスト方針

### Core.Tests
- `UnknownResolverTests`: 決定性（同 seed 同結果）、重み 0 の kind が結果に出ないこと、許可されない kind が weights にあると `MapGenerationConfigException`
- `RunActionsTests.SelectNextNode`: 正常移動、非隣接へ移動で ArgumentException、ノード ID 範囲外で ArgumentException、`VisitedNodeIds` が更新される
- `RunStateSerializerTests` 追加: v2 スキーマの往復シリアライズ、Core のシリアライザは v1 JSON に対して `InvalidOperationException`（Schema version mismatch）を投げる。`FileSaveRepository.TryLoadAsync` はそれをキャッチして `null` を返す（＝「セーブ無し」扱い）

### Server.Tests
- `RunsControllerTests`:
  - `GET /runs/current` で InProgress / 204 / 各種 not-InProgress の分岐
  - `POST /runs/new` で既存 InProgress 有りで 409、`?force=true` で上書き成功、新規時は seed 固定で map/RunState が決定的
  - `POST /runs/current/move` で正常 / 非隣接 400 / InProgress でない 409
  - `POST /runs/current/abandon` で Progress=Abandoned 保存、再取得時 204
  - `POST /runs/current/heartbeat` で PlaySeconds 加算
- 統合テスト: new → move → heartbeat → current → move のフルフロー、PlaySeconds 加算が累積していること

### Client.Tests
- `MapScreen` 単体: 現在地ハイライト、選択可能ノードのクリックで API コール、訪問済みノードの表示
- `InGameMenuScreen` 単体: ボタン押下で期待する API コール、モーダル閉じる、フォーカストラップ
- `MainMenuScreen` 拡張: InProgress セーブあり時の確認ダイアログ、無し時の直接遷移

### 手動確認
- 新規ラン → 数マス進む → ブラウザリロード → 同じ状態で復帰
- 「メニューに戻る」→ Main Menu → 「続きから」→ 同じ状態
- 「あきらめる」→ Main Menu → 「保存済みラン有り」バッジ消失、シングルプレイ即新規
- ボス到達で止まる（クリア判定未実装の確認）

---

## 10. Done 判定（Phase 4 完了基準）

1. 新規ラン → MapScreen にマップが描画される
2. 選択可能ノードをクリックすると移動できる
3. Unknown ノードを訪問すると抽選結果の kind が表示される
4. ブラウザリロードで同じ状態から再開できる
5. 「メニューに戻る」→ Main Menu → 「続きから」で復帰できる
6. 「あきらめる」→ 次回 Main Menu でバッジ消失、シングル押下で即新規開始
7. プレイ秒数が正しく加算されている（セーブファイルを直接確認）
8. Core.Tests / Server.Tests / Client.Tests が全て緑
9. `phase4-complete` タグが `master` に付与される

戦闘処理・商人画面・ボス撃破後の遷移は本フェーズの対象外。

---

## 11. 既知のリスクと将来への引き継ぎ

- **map の毎回再生成コスト**: 1 リクエストあたり 5-20ms 程度で問題なしだが、マルチプレイや頻繁な move 連打でボトルネックになれば `AccountId → DungeonMap` のメモリキャッシュを Phase 9 で検討
- **Unknown の抽選 RNG**: seed から +1 した別 RNG を使う（map 生成と同一 RNG を使い回すと map の再生成時に消費位置が合わなくなるため）。これはテストで明示的に担保する
- **プレイ秒数の誤差**: `performance.now()` ベースでブラウザ背景化時は停止するので、実際のプレイ時間より短めに計測される可能性あり。MVP ではこの挙動を受け入れる
- **v1 セーブの破棄**: 本番リリース前に互換ロジックを入れるか、リリース時 DB を空にするかは Phase 8 以降で判断
- **`Event` kind 追加**: Phase 6 で `TileKind.Event` を追加したら `unknownResolutionWeights` に行を足し、kind ごとのアイコン／画面を足すだけで連携が取れる設計にしてある
