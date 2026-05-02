# Phase 10.3-MVP — REST + in-memory による最小プレイアブル戦闘 設計

> 作成日: 2026-04-27
> 対象フェーズ: Phase 10.3-MVP（Phase 10.3 本格の前段、最小プレイアブル先行）
> 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
> 直前マイルストーン spec: [`2026-04-27-phase10-2E-relics-potions-design.md`](2026-04-27-phase10-2E-relics-potions-design.md)
> 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html` (`BattleScreen.tsx` 既ポート済)

## ゴール

Phase 10.2 で完成した `BattleEngine`（Core ロジック完成、6 公開 API、xUnit 975 tests pass）を **「ランを通したテストプレイで実際に動かせる」** 最小構成にする。

**完成イメージ**: マップで敵タイル進入 → battle-v10.html ベースの `BattleScreen` がフルスクリーン表示 → カードプレイ・ターン終了で戦闘進行 → Victory で既存 Reward フロー / Defeat で GameOver。

このフェーズを **「Phase 10.3-MVP」** と呼び、本格 Phase 10.3（SignalR `BattleHub` + 永続化）/ 10.4（Client polish）/ 10.5（`BattlePlaceholder` 退役）とは別線で進める。完了時点で `BattleEngine` が実プレイで動作し、後続フェーズは段階的な改善 + cleanup となる。

## 完了判定

- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（Core 975 + Server 168 既存 + 新規 ~20 ≒ 1163 tests）
- Client `npm run build` がビルドエラーなし
- 7 endpoint (`POST /battle/{start, play-card, end-turn, use-potion, set-target, finalize}` + `GET /battle`) が REST で動作
- 手動プレイで 1 戦闘完走 + Victory → Reward → 次マップ進行が確認できる
- 手動プレイで Defeat 時の GameOver 遷移が確認できる
- ブラウザリロード時の戦闘 reset → POST start 再開が動作
- `BattleScreen.tsx` が production で動作、battle-v10.html 由来の polished UI で実プレイ可能
- `BattleOverlay.tsx` / `BattleOverlay.test.tsx` 削除、`?demo=battle` URL 廃止
- 親 spec に「Phase 10.3-MVP 補記」追加
- `phase10-3-mvp-complete` タグが切られ origin に push 済み

---

## 1. アーキテクチャ概要

### 1-1. Phase 10.3 / 10.4 / 10.5 との関係

10.3-MVP は本格 Phase 10.3 の前段。spec 上の正規ルートは `10.3 (SignalR) → 10.4 (Client polish) → 10.5 (placeholder 退役)` だが、`BattleEngine` の実プレイ検証を最速で得るため REST + in-memory の MVP を先行する。

| | Phase 10.3-MVP（本フェーズ） | Phase 10.3 本格 | Phase 10.4 | Phase 10.5 |
|---|---|---|---|---|
| Server API | REST | SignalR Hub | - | - |
| Battle state 保存 | in-memory | DB / save 永続化 | - | save schema v8 |
| Client UI | `BattleScreen.tsx` 接続 | (10.3-MVP 流用) | event polish / animation | placeholder 削除 |
| Placeholder | 共存 | 共存 | 共存 | 退役 |
| 戦闘外 UsePotion | 未対応 | 未対応 | 未対応 | 実装 |

10.3-MVP の REST endpoint ロジックは丸ごと Phase 10.3 本格の `BattleHub` メソッドに移植可能な設計とする。

### 1-2. 共存戦略

- `BattlePlaceholder.Start` の **既存呼出元** (`NodeEffectResolver`) は **継続して呼ばれる**（`RunState.ActiveBattle` に `BattlePlaceholderState` がセットされる、戦闘中マーカー兼用）
- 新規 `POST /api/v1/runs/current/battle/start` で `BattleEngine.Start` を呼び、サーバインメモリの `BattleSessionStore` に `BattleState` を保存
- 戦闘終了時 (`POST /battle/finalize`) はサーバインメモリ削除 + `RunState.ActiveBattle = null`
- リロード時: `RunState.ActiveBattle != null` だが in-memory にセッションなし → Client が `POST /battle/start` で同 encounter を再開始（戦闘進行は最初から、戦闘自体は維持）
- 既存 `POST /api/v1/runs/current/battle/win` (placeholder 用) は **並走維持**（Client は使わない、Phase 10.5 で削除）

### 1-3. ファイル構成（10.3-MVP 完了時の差分）

```
src/Core/                                    [無変更]

src/Server/
├── Services/
│   ├── BattleSessionStore.cs               [新] in-memory store: ConcurrentDictionary<accountId, BattleState>
│   └── BattleStateDtoMapper.cs             [新] BattleState → BattleStateDto 変換 + Display 値計算
├── Controllers/
│   ├── BattleController.cs                 [新] 7 endpoints (start / get / play-card / end-turn / use-potion / set-target / finalize)
│   ├── CatalogController.cs                [修正] +units / +enemies / +potions endpoint
│   └── RunsController.cs                   [修正] /battle/win 既存維持、/abandon で session cleanup
└── Dtos/
    ├── BattleStateDto.cs                   [全面書き換え] フル DTO (新)
    ├── BattlePlaceholderStateDto.cs        [新ファイル] 旧 BattleStateDto をリネーム移設
    ├── BattleEventDto.cs                   [新]
    ├── BattleEventStepDto.cs               [新]
    ├── BattleActionResponseDto.cs          [新]
    ├── CombatActorDto.cs                   [新]
    ├── BattleCardInstanceDto.cs            [新]
    ├── PlayCardRequestDto.cs               [新]
    ├── EndTurnRequestDto.cs                [新]
    ├── UsePotionRequestDto.cs              [新]
    ├── SetTargetRequestDto.cs              [新]
    └── BattleStartRequestDto.cs            [新]

src/Client/src/
├── api/
│   ├── battle.ts                           [修正] 既存 winBattle 維持 + 7 関数追加 (start/get/play-card/end-turn/use-potion/set-target/finalize)
│   ├── catalog.ts                          [修正] +fetchUnits / +fetchEnemies / +fetchPotions
│   └── types.ts                            [修正] 新 DTO 型追加 (BattleStateDto = フル新型に置換、BattlePlaceholderStateDto は別名)
├── hooks/
│   ├── useUnitCatalog.ts                   [新]
│   ├── useEnemyCatalog.ts                  [新]
│   └── usePotionCatalog.ts                 [新]
├── screens/
│   ├── BattleScreen.tsx                    [大改修] hardcoded → API 接続、events animation queue、onClick wiring
│   ├── BattleScreen.css                    [流用、無変更]
│   ├── battleScreen/dtoAdapter.ts          [新] DTO + catalog → 既存中間型 (RelicDemo / CharacterDemo / HandCardDemo) 変換
│   ├── BattleOverlay.tsx                   [削除]
│   ├── BattleOverlay.test.tsx              [削除]
│   └── MapScreen.tsx                       [修正] snapshot.battle ありで BattleScreen にスイッチ
└── App.tsx                                 [修正] ?demo=battle 削除

tests/Server.Tests/
├── Controllers/
│   └── BattleControllerTests.cs            [新] 7 endpoint の単体テスト ~20
└── Fixtures/
    └── BattleControllerFixtures.cs         [新] テスト共通 helper
```

### 1-4. namespace

新規追加なし。`RoguelikeCardGame.Server.Controllers` / `RoguelikeCardGame.Server.Dtos` / `RoguelikeCardGame.Server.Services` 既存配下。

### 1-5. memory feedback の遵守

`memory/feedback_battle_engine_conventions.md` の 2 ルール:

1. **`BattleOutcome` は常に fully qualified**: `BattleController.Finalize` の Victory/Defeat 分岐で `summary.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory` を維持
2. **`state.Allies` / `state.Enemies` への書き戻しは InstanceId で検索**: `BattleController` は `BattleEngine.*` への委譲のみで Allies/Enemies を直接書き戻さない、`BattleStateDtoMapper` は read-only 変換 → 該当なし

---

## 2. サーバ API 設計

### 2-1. エンドポイント一覧

すべて `[Route("api/v1/runs/current/battle")]` 配下、`X-Account-Id` ヘッダ必須。

| Method | Path | Body | Response |
|---|---|---|---|
| `POST` | `/start` | `BattleStartRequestDto` ({}) | `BattleActionResponseDto` |
| `GET` | `` (root) | - | `BattleStateDto` or 404 |
| `POST` | `/play-card` | `PlayCardRequestDto` | `BattleActionResponseDto` |
| `POST` | `/end-turn` | `EndTurnRequestDto` ({}) | `BattleActionResponseDto` |
| `POST` | `/use-potion` | `UsePotionRequestDto` | `BattleActionResponseDto` |
| `POST` | `/set-target` | `SetTargetRequestDto` | `BattleStateDto` (events 無し) |
| `POST` | `/finalize` | (空) | `RunSnapshotDto` (Victory→Reward) or `RunResultDto` (Boss-final-act / Defeat) |

### 2-2. `POST /battle/start`

戦闘開始 / リロード後の再開両用。

**処理フロー**:
1. `RunState` を save から取得
2. `state.Progress != InProgress` または `state.ActiveBattle is null` → 409
3. `BattleSessionStore.TryGet` で既存セッションを確認 → あればそのまま返却（リロード時の冪等性、events は空配列）
4. なければ `BattleEngine.Start(run, run.ActiveBattle.EncounterId, rng, catalog)` で新規開始
5. `BattleSessionStore.Set(accountId, state)` で保存
6. `BattleActionResponseDto { state, steps }` 返却

**冪等性**: 同 encounter で 2 回呼んでも同じセッションを返す。リロード後の Client 起動シーケンスで安全に呼べる。

**rng**: `new SystemRng(unchecked((int)run.RngSeed ^ (int)run.PlaySeconds ^ 0xBA77LE))` で seed 生成。同 run 内でリプロデューシブル。

### 2-3. `GET /battle`

リロード時の State 復元用。

**処理フロー**:
1. `BattleSessionStore.TryGet(accountId)` で in-memory state を取得
2. 無ければ 404 (Client は `POST /start` で再開始)
3. あれば `BattleStateDto` 返却（events 無し）

### 2-4. `POST /battle/play-card`

```csharp
public sealed record PlayCardRequestDto(
    int HandIndex,
    int? TargetEnemyIndex,
    int? TargetAllyIndex);
```

**処理フロー**:
1. セッション取得、無ければ 409
2. `BattleEngine.PlayCard(state, handIndex, targetEnemyIndex, targetAllyIndex, rng, catalog)` 呼出
3. `InvalidOperationException` (cost 不足 / Phase 不正 / handIndex 範囲外) → 400 + `{ title }` で返す
4. 成功時、新 state を store に保存
5. `BattleActionResponseDto` 返却

### 2-5. `POST /battle/end-turn`

**処理フロー**:
1. セッション取得
2. `BattleEngine.EndTurn(state, rng, catalog)` 呼出
3. 例外時 400
4. 新 state 保存（Outcome 確定でも、Client が finalize endpoint を別に呼ぶまで store に保持）
5. `BattleActionResponseDto` 返却

### 2-6. `POST /battle/use-potion`

```csharp
public sealed record UsePotionRequestDto(
    int PotionIndex,
    int? TargetEnemyIndex,
    int? TargetAllyIndex);
```

`BattleEngine.UsePotion` 呼出。それ以外は play-card と同じ。

### 2-7. `POST /battle/set-target`

```csharp
public sealed record SetTargetRequestDto(
    string Side,    // "Ally" | "Enemy"
    int SlotIndex);
```

**処理フロー**:
1. セッション取得
2. `BattleEngine.SetTarget(state, side, slotIndex)` 呼出（10.2.C 既存公開 API）
3. 新 state 保存
4. **events 無し**なので `BattleStateDto` のみ返却（特例）

### 2-8. `POST /battle/finalize`

戦闘終了確定後の reward / GameOver 遷移。

**処理フロー**:
1. セッション取得
2. `state.Phase != Resolved` なら 409 ("battle not yet resolved")
3. `RunState` を save から取得
4. `BattleEngine.Finalize(state, run)` → `(nextRun, summary)` 取得
5. `nextRun = nextRun with { ActiveBattle = null }` で placeholder マーカークリア
6. `BattleSessionStore.Remove(accountId)` で in-memory セッション破棄
7. `summary.Outcome` 別分岐:
   - **Victory**: 既存 `RunsController.PostBattleWin` のロジックを流用（`isBoss && lastAct` で `ActTransition.FinishRun` 経由 `RunResultDto` 返却 / 通常時は `RewardGenerator.Generate` 経由 `ActiveReward` セット → `RunSnapshotDto`）
   - **Defeat**: `RunHistoryBuilder.From(GameOver)` + history 追加 + bestiary merge + save 削除 → `RunResultDto`
8. レスポンス: Victory の reward フローは `RunSnapshotDto`、ラン終了は `RunResultDto`

### 2-9. リロード時の Client 起動シーケンス

1. App boot → `GET /api/v1/runs/current` で `RunSnapshotDto` 取得
2. `snapshot.battle != null` → `MapScreen` 経由で `BattleScreen` にスイッチ
3. `BattleScreen` mount → `GET /battle` を呼ぶ
4. 200 → state ありとして表示
5. 404 → `POST /battle/start` でリロード後の再開始（戦闘進行はリセット）
6. それ以降は通常の play-card / end-turn フロー

### 2-10. Error 応答

既存 `Problem(statusCode: ..., title: ...)` パターン踏襲:

| 状況 | Status |
|---|---|
| アカウント未存在 | 404 |
| 進行中ラン無し / 戦闘 placeholder 無し / 戦闘セッション無し / Phase 不正 | 409 |
| `BattleEngine` からの `InvalidOperationException` (cost 不足等) | 400 |

---

## 3. DTO 型定義

すべて `RoguelikeCardGame.Server.Dtos` 名前空間。

### 3-1. `BattleStateDto` (フル DTO に置き換え)

既存 placeholder 用 DTO は `BattlePlaceholderStateDto` にリネーム（後方互換のため当面残す）。新 `BattleStateDto`:

```csharp
public sealed record BattleStateDto(
    int Turn,
    string Phase,                                          // "PlayerInput" | "PlayerAttacking" | "EnemyAttacking" | "Resolved"
    string Outcome,                                        // "Pending" | "Victory" | "Defeat"
    IReadOnlyList<CombatActorDto> Allies,
    IReadOnlyList<CombatActorDto> Enemies,
    int? TargetAllyIndex,
    int? TargetEnemyIndex,
    int Energy,
    int EnergyMax,
    IReadOnlyList<BattleCardInstanceDto> DrawPile,
    IReadOnlyList<BattleCardInstanceDto> Hand,
    IReadOnlyList<BattleCardInstanceDto> DiscardPile,
    IReadOnlyList<BattleCardInstanceDto> ExhaustPile,
    IReadOnlyList<BattleCardInstanceDto> SummonHeld,
    IReadOnlyList<BattleCardInstanceDto> PowerCards,
    int ComboCount,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    IReadOnlyList<string> OwnedRelicIds,
    IReadOnlyList<string> Potions,
    string EncounterId);
```

enum 系（`Phase` / `Outcome`）は string serialize（既存 `BattlePlaceholderStateDto.Outcome` のパターン継承、JSON の可読性 + Client TypeScript の literal union 型と相性良）。

### 3-2. `CombatActorDto`

```csharp
public sealed record CombatActorDto(
    string InstanceId,
    string DefinitionId,
    string Side,                                           // "Ally" | "Enemy"
    int SlotIndex,
    int CurrentHp,
    int MaxHp,
    int BlockDisplay,                                      // BlockPool.Display(dexterity) 計算済み
    int AttackSingleDisplay,                               // AttackPool.Display(strength, weak) 計算済み
    int AttackRandomDisplay,
    int AttackAllDisplay,
    IReadOnlyDictionary<string, int> Statuses,             // "strength":2, "vulnerable":3 etc.
    string? CurrentMoveId,
    int? RemainingLifetimeTurns,
    string? AssociatedSummonHeldInstanceId);
```

**Display 計算は `BattleStateDtoMapper` で実施**:
- `BlockDisplay` = `actor.Block.Display(actor.GetStatus("dexterity"))` (10.2.B 実装済み)
- `AttackXxxDisplay` = `actor.AttackXxx.Display(actor.GetStatus("strength"), actor.GetStatus("weak"))` (10.2.B 実装済み)
- これにより Client 側で再計算不要

`RawTotal` / `AddCount` は送らない（Client は Display しか使わない、演出に不要）。

### 3-3. `BattleCardInstanceDto`

```csharp
public sealed record BattleCardInstanceDto(
    string InstanceId,
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride);
```

カードの表示情報（Name / Cost / Description / Type / Rarity）は Client が `useCardCatalog` 経由で `CardDefinitionId` から引く。

### 3-4. `BattleEventDto`

```csharp
public sealed record BattleEventDto(
    string Kind,                                           // "BattleStart", "PlayCard", ... "UsePotion"
    int Order,
    string? CasterInstanceId,
    string? TargetInstanceId,
    int? Amount,
    string? CardId,
    string? Note);
```

`BattleEventKind` enum を string serialize。

### 3-5. `BattleActionResponseDto` / `BattleEventStepDto`

```csharp
public sealed record BattleActionResponseDto(
    BattleStateDto State,                                  // 最終状態
    IReadOnlyList<BattleEventStepDto> Steps);

public sealed record BattleEventStepDto(
    BattleEventDto Event,
    BattleStateDto SnapshotAfter);                         // この event 適用直後の中間 state
```

**`SnapshotAfter` は常に最終 state と同一**（実装最小、CSS transition で滑らかに補間）。Client は events を 200ms ステップで再生 + HP/Block の数値を CSS `transition` で滑らかに補間。これで「ダメージ 14 が表示 → HP バーが 0.3s で減る」のような演出が無料で得られる。

将来 (Phase 10.4) で本物の中間 state に進化させる場合、DTO 形式は維持して中身だけ差し替え可能。

### 3-6. リクエスト DTO

```csharp
public sealed record BattleStartRequestDto();
public sealed record EndTurnRequestDto();
public sealed record PlayCardRequestDto(int HandIndex, int? TargetEnemyIndex, int? TargetAllyIndex);
public sealed record UsePotionRequestDto(int PotionIndex, int? TargetEnemyIndex, int? TargetAllyIndex);
public sealed record SetTargetRequestDto(string Side, int SlotIndex);
```

### 3-7. 既存 `BattleStateDto` の扱い

- 既存 `src/Server/Dtos/BattleStateDto.cs` (placeholder 用、`EncounterId + Enemies + Outcome`) を **`BattlePlaceholderStateDto` にリネーム**
- 既存 `EnemyInstanceDto` は **`PlaceholderEnemyInstanceDto` にリネーム**
- 既存利用箇所 (`RunSnapshotDtoMapper` / `BattleOverlay.tsx` / Client `types.ts`) のリネーム追従
- Phase 10.5 の `BattlePlaceholder` 削除時に同時削除予定

### 3-8. Client 側 TypeScript 型 (`src/Client/src/api/types.ts`)

```typescript
export type BattlePhase = 'PlayerInput' | 'PlayerAttacking' | 'EnemyAttacking' | 'Resolved'
export type BattleOutcomeKind = 'Pending' | 'Victory' | 'Defeat'
export type ActorSide = 'Ally' | 'Enemy'
export type BattleEventKind =
  | 'BattleStart' | 'TurnStart' | 'PlayCard'
  | 'AttackFire' | 'DealDamage' | 'GainBlock'
  | 'ActorDeath' | 'EndTurn' | 'BattleEnd'
  | 'ApplyStatus' | 'RemoveStatus' | 'PoisonTick'
  | 'Heal' | 'Draw' | 'Discard' | 'Upgrade' | 'Exhaust'
  | 'GainEnergy' | 'Summon' | 'UsePotion'

export type CombatActorDto = { /* ... */ }
export type BattleCardInstanceDto = { /* ... */ }
export type BattleEventDto = { /* ... */ }
export type BattleStateDto = { /* ... */ }   // ← 既存と同名で置換、placeholder 用は別名
export type BattlePlaceholderStateDto = { /* 既存リネーム後 */ }
export type BattleEventStepDto = { event: BattleEventDto; snapshotAfter: BattleStateDto }
export type BattleActionResponseDto = { state: BattleStateDto; steps: BattleEventStepDto[] }
```

### 3-9. JSON 命名規約

ASP.NET Core デフォルト (PascalCase → camelCase) を維持。System.Text.Json の `JsonOptions` は既存設定使用。

---

## 4. Server 内部実装

### 4-1. `BattleSessionStore`

`src/Server/Services/BattleSessionStore.cs`:

```csharp
using System.Collections.Concurrent;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// 戦闘中の BattleState を accountId 単位でメモリ保持する。
/// Phase 10.3-MVP 暫定: save に乗らない (リロードで戦闘進行リセット)。
/// Phase 10.5 で本格保存への移行を検討。
/// </summary>
public sealed class BattleSessionStore
{
    private readonly ConcurrentDictionary<string, BattleState> _sessions = new();

    public bool TryGet(string accountId, out BattleState state)
        => _sessions.TryGetValue(accountId, out state!);

    public void Set(string accountId, BattleState state)
        => _sessions[accountId] = state;

    public void Remove(string accountId)
        => _sessions.TryRemove(accountId, out _);
}
```

**DI 登録**: `Program.cs` で `services.AddSingleton<BattleSessionStore>()`。プロセス全体で 1 インスタンス、accountId キーで分離。

**メモリリーク防止**: 戦闘終了時 (`finalize`) に明示的に `Remove`、ラン放棄 (`/abandon`) でも `Remove` 必須（§6-2 暗黙要件参照）。

### 4-2. `BattleStateDtoMapper`

`src/Server/Services/BattleStateDtoMapper.cs`:

主要メソッド:
- `ToDto(BattleState) → BattleStateDto`: 全フィールド変換 + Display 値計算
- `ToActorDto(CombatActor) → CombatActorDto`: AttackPool / BlockPool の Display 計算
- `ToCardDto(BattleCardInstance) → BattleCardInstanceDto`: 4 フィールド単純コピー
- `ToEventDto(BattleEvent) → BattleEventDto`: enum string 化
- `ToActionResponse(BattleState finalState, IReadOnlyList<BattleEvent> events) → BattleActionResponseDto`: 各 event に最終 state の snapshot を付与（§3-5 参照）

**Display 計算の注意**:
- `BlockPool.Display(int dexterity)` / `AttackPool.Display(int strength, int weak)` は 10.2.B 実装済
- `dexterity` / `strength` / `weak` は `actor.GetStatus(...)` で取得
- 敵側の AttackPool 表示は通常 0（敵 attack は per-effect 即時発射、Pool 蓄積なし）

### 4-3. `BattleController`

`src/Server/Controllers/BattleController.cs`:

```csharp
[ApiController]
[Route("api/v1/runs/current/battle")]
public sealed class BattleController : ControllerBase
{
    public const string AccountHeader = "X-Account-Id";

    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly DataCatalog _data;
    private readonly BattleSessionStore _sessions;
    private readonly RunStartService _runStart;
    private readonly IHistoryRepository _history;
    private readonly IBestiaryRepository _bestiary;

    public BattleController(/* 7 services 注入 */) { ... }

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken ct) { ... }

    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken ct) { ... }

    [HttpPost("play-card")]
    public async Task<IActionResult> PlayCard([FromBody] PlayCardRequestDto body, CancellationToken ct) { ... }

    [HttpPost("end-turn")]
    public async Task<IActionResult> EndTurn(CancellationToken ct) { ... }

    [HttpPost("use-potion")]
    public async Task<IActionResult> UsePotion([FromBody] UsePotionRequestDto body, CancellationToken ct) { ... }

    [HttpPost("set-target")]
    public async Task<IActionResult> SetTarget([FromBody] SetTargetRequestDto body, CancellationToken ct) { ... }

    [HttpPost("finalize")]
    public async Task<IActionResult> Finalize(CancellationToken ct) { ... }

    private bool TryGetAccountId(out string accountId, out IActionResult? err) { ... }

    private IRng MakeBattleRng(RunState run) =>
        new SystemRng(unchecked((int)run.RngSeed ^ (int)run.PlaySeconds ^ 0xBA77LE));
}
```

### 4-4. `Start` endpoint 実装の骨格

```csharp
[HttpPost("start")]
public async Task<IActionResult> Start(CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;

    var run = await _saves.TryLoadAsync(accountId, ct);
    if (run is null || run.Progress != RunProgress.InProgress)
        return Problem(statusCode: 409, title: "進行中のランがありません。");
    if (run.ActiveBattle is null)
        return Problem(statusCode: 409, title: "進行中の戦闘がありません。");

    // 冪等性: 既存セッションがあれば返す
    if (_sessions.TryGet(accountId, out var existing))
        return Ok(BattleStateDtoMapper.ToActionResponse(existing, Array.Empty<BattleEvent>()));

    var rng = MakeBattleRng(run);
    var (state, events) = BattleEngine.Start(run, run.ActiveBattle.EncounterId, rng, _data);
    _sessions.Set(accountId, state);

    return Ok(BattleStateDtoMapper.ToActionResponse(state, events));
}
```

### 4-5. `PlayCard` endpoint 実装の骨格

```csharp
[HttpPost("play-card")]
public async Task<IActionResult> PlayCard([FromBody] PlayCardRequestDto body, CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;
    if (body is null) return BadRequest();

    if (!_sessions.TryGet(accountId, out var state))
        return Problem(statusCode: 409, title: "戦闘セッションが存在しません。");

    var run = await _saves.TryLoadAsync(accountId, ct);
    if (run is null) return Problem(statusCode: 409, title: "進行中のランがありません。");

    var rng = MakeBattleRng(run);
    try
    {
        var (newState, events) = BattleEngine.PlayCard(
            state, body.HandIndex, body.TargetEnemyIndex, body.TargetAllyIndex, rng, _data);
        _sessions.Set(accountId, newState);
        return Ok(BattleStateDtoMapper.ToActionResponse(newState, events));
    }
    catch (InvalidOperationException ex)
    {
        return Problem(statusCode: 400, title: ex.Message);
    }
}
```

`EndTurn` / `UsePotion` も同パターン。

### 4-6. `Finalize` endpoint 実装の骨格

```csharp
[HttpPost("finalize")]
public async Task<IActionResult> Finalize(CancellationToken ct)
{
    if (!TryGetAccountId(out var accountId, out var err)) return err!;

    if (!_sessions.TryGet(accountId, out var battleState))
        return Problem(statusCode: 409, title: "戦闘セッションが存在しません。");

    if (battleState.Phase != BattlePhase.Resolved)
        return Problem(statusCode: 409, title: "戦闘がまだ終了していません。");

    var run = await _saves.TryLoadAsync(accountId, ct);
    if (run is null) return Problem(statusCode: 409, title: "進行中のランがありません。");

    var (afterFinalize, summary) = BattleEngine.Finalize(battleState, run);
    afterFinalize = afterFinalize with { ActiveBattle = null };
    _sessions.Remove(accountId);

    if (summary.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory)
        return await HandleVictoryAsync(accountId, afterFinalize, run, ct);
    else
        return await HandleDefeatAsync(accountId, afterFinalize, ct);
}
```

`HandleVictoryAsync` / `HandleDefeatAsync` は既存 `RunsController.PostBattleWin` の Victory / Defeat 経路ロジックを **コピー流用**（DRY より MVP スコープ優先、placeholder 用 endpoint も並走するため）:

- **Victory + Boss + last act**: `ActTransition.FinishRun(Cleared)` → `RunHistoryBuilder.From` → history 追加 → bestiary merge → save 削除 → `RunResultDto` 返却
- **Victory 通常 / Boss 非 last**: `RewardGenerator.Generate` or `BossRewardFlow.GenerateBossReward` → `ActiveReward` セット → `BestiaryTracker.NoteCardsSeen` → save → `RunSnapshotDto` 返却
- **Defeat**: `RunHistoryBuilder.From(GameOver)` + history 追加 + bestiary merge + save 削除 → `RunResultDto`

### 4-7. DI 登録 (`Program.cs`)

```csharp
services.AddSingleton<BattleSessionStore>();
```

既存の他 service 登録の隣に追加。`BattleController` は `[ApiController]` の attribute scan で自動登録。

### 4-8. `RunSnapshotDtoMapper` の `ActiveBattle` 扱い

既存 `RunSnapshotDtoMapper.From` で `RunState.ActiveBattle: BattlePlaceholderState?` を `BattlePlaceholderStateDto` に変換している部分はリネーム追従のみ。**新 `BattleStateDto` は `RunSnapshotDto` に乗らない**（戦闘 state は別 endpoint で取得、Q1-A の方針）。

### 4-9. `RunsController.Abandon` / `RunsController.PostBattleWin` の修正

- `Abandon` (既存): `await _saves.DeleteAsync` の前に `_sessions.Remove(accountId)` を呼ぶ（暗黙要件 F2、§6-2）
- `PostBattleWin` (既存、placeholder 用): **無変更で並走維持**（Phase 10.5 で削除予定）

### 4-10. `CatalogController` の拡張

`src/Server/Controllers/CatalogController.cs` に以下 endpoint 追加:

| Method | Path | Response |
|---|---|---|
| `GET` | `/api/v1/catalog/units` | `UnitDefinition[]` |
| `GET` | `/api/v1/catalog/enemies` | `EnemyDefinition[]` |
| `GET` | `/api/v1/catalog/potions` | `PotionDefinition[]` |

既存 `cards` / `relics` / `events` endpoint と同パターン。Client の `useUnitCatalog` / `useEnemyCatalog` / `usePotionCatalog` hook が必要とする（暗黙要件 F3）。

---

## 5. Client 接続

### 5-1. `src/Client/src/api/battle.ts` 拡張

既存 `winBattle` (placeholder 用) は維持しつつ、新 7 関数追加: `startBattle`, `getBattle`, `playCard`, `endTurn`, `usePotion`, `setTarget`, `finalizeBattle`。

`getBattle` は 404 を null 返却に変換（リロード時の再開フロー用）:
```typescript
export async function getBattle(accountId: string): Promise<BattleStateDto | null> {
  try {
    return await apiRequest<BattleStateDto>('GET', '/runs/current/battle', { accountId })
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null
    throw e
  }
}
```

### 5-2. `BattleScreen.tsx` 大改修

#### 5-2-1. Props と状態

```typescript
type Props = {
  accountId: string
  onBattleResolved: (result: RunSnapshotDto | RunResultDto) => void
}

export function BattleScreen({ accountId, onBattleResolved }: Props) {
  const [state, setState] = useState<BattleStateDto | null>(null)
  const [animating, setAnimating] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const cardCatalog = useCardCatalog()
  const relicCatalog = useRelicCatalog()
  const potionCatalog = usePotionCatalog()
  const enemyCatalog = useEnemyCatalog()
  const unitCatalog = useUnitCatalog()

  // mount: GET /battle → なければ POST /start
  useEffect(() => {
    let cancelled = false
    async function init() {
      const existing = await getBattle(accountId)
      if (cancelled) return
      if (existing) {
        setState(existing)
      } else {
        const resp = await startBattle(accountId)
        if (cancelled) return
        await playSteps(resp)
      }
    }
    void init()
    return () => { cancelled = true }
  }, [accountId])

  async function playSteps(resp: BattleActionResponseDto) {
    setAnimating(true)
    for (const step of resp.steps) {
      setState(step.snapshotAfter)
      await sleep(220)  // 200-300ms ステップ
    }
    setState(resp.state)
    setAnimating(false)
    if (resp.state.outcome === 'Victory' || resp.state.outcome === 'Defeat') {
      await handleFinalize()
    }
  }

  async function handlePlayCard(handIndex: number) {
    if (animating || busy) return
    setBusy(true)
    try {
      const resp = await playCard(accountId, { handIndex })
      await playSteps(resp)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleEndTurn() { /* 同パターン */ }
  async function handleUsePotion(potionIndex: number) { /* 同パターン */ }
  async function handleSetTarget(side: ActorSide, slotIndex: number) {
    const newState = await setTarget(accountId, { side, slotIndex })
    setState(newState)  // events なし
  }

  async function handleFinalize() {
    const result = await finalizeBattle(accountId)
    onBattleResolved(result)
  }

  // ... レンダリング (既存 layout 流用、データソース変更)
}
```

#### 5-2-2. データソース変換層

既存 `BattleScreen.tsx` の中間型 (`RelicDemo` / `CharacterDemo` / `HandCardDemo`) を **DTO + catalog から構築するヘルパー** に置換。新ファイル `src/Client/src/screens/battleScreen/dtoAdapter.ts` に:

- `toRelicDemo(relicId, catalog) → RelicDemo`
- `toCharacterDemo(actor, catalog) → CharacterDemo` (Allies / Enemies + heroCharacter / enemyDef / unitDef を見分け)
- `toHandCardDemo(card, catalog, energy) → HandCardDemo` (cost 計算 + playable 判定)
- `toBuffs(actor) → BuffDemo[]` (Statuses dict + BlockDisplay → BuffDemo 配列)
- `toIntent(actor, def, catalog) → IntentDemo | undefined` (currentMoveId → MoveDefinition から intent 推定)

#### 5-2-3. インタラクション wiring

- `HandCard` に `onClick` prop 追加 → `handlePlayCard(handIndex)`、`playable` false で disabled
- `Slot` に `onClick` prop 追加 → `handleSetTarget(side, slotIndex)`、targeted 時に CSS class `is-targeted`
- `<button className="end-turn">` に `onClick={handleEndTurn}` 追加
- `Potion` に `onClick` prop 追加 → `handleUsePotion(potionIndex)`、空 slot で disabled

### 5-3. `App.tsx` 修正

既存の `?demo=battle` ガード削除。通常フローで `MapScreen` に snapshot を渡す。

### 5-4. `MapScreen.tsx` 修正

`snapshot.battle != null` の時、既存の `<BattleOverlay>` を `<BattleScreen>` に置換。フルスクリーンで MapScreen を覆う:

```typescript
if (snapshot.battle) {
  return (
    <BattleScreen
      accountId={accountId}
      onBattleResolved={(result) => {
        if ('progress' in result) {
          // RunResultDto (ラン終了) → onRunFinished
          onRunFinished(result, ...)
        } else {
          // RunSnapshotDto (Reward 画面など) → setSnapshot
          setSnapshot(result)
        }
      }}
    />
  )
}
```

`BattleOverlay` の import を削除。

### 5-5. CSS

`src/Client/src/screens/BattleScreen.css` は既存（battle-v10.html ポート）をそのまま使用。HP / Block / Energy の数値変化に `transition` が既に入っているなら活用、なければ追加（実装時に確認）。

### 5-6. 削除するファイル

- `src/Client/src/screens/BattleOverlay.tsx`
- `src/Client/src/screens/BattleOverlay.test.tsx`

### 5-7. Catalog hooks の追加

既存 `useCardCatalog` / `useRelicCatalog` / `useEventCatalog` あり。新規追加:
- `useUnitCatalog`: 召喚 actor 表示用
- `useEnemyCatalog`: 敵 actor 表示用
- `usePotionCatalog`: ポーション slot 表示用

既存 `useRelicCatalog` のコピーで catalog endpoint 別 fetch（暗黙要件 F3）。

### 5-8. Hero の表示

`actor.definitionId === "hero"` の場合は `CharacterDefinition` (既存 `default` キャラ等) から取得。既存 `CharacterCatalog` または `useRunSnapshot` 経由で character info を持っている想定。なければ最小ハードコード (`name: "主人公"`, `sprite: "☗"`) で OK。

---

## 6. テスト戦略 + 暗黙要件

Q8-A 確定通り、**Server xUnit のみ**、Client は手動プレイ確認。

### 6-1. 新規テストファイル

`tests/Server.Tests/Controllers/BattleControllerTests.cs`:

| テストケース | 確認内容 |
|---|---|
| `Start_when_no_active_run_returns_409` | run 無しで 409 |
| `Start_when_no_active_battle_returns_409` | RunState.ActiveBattle が null で 409 |
| `Start_creates_session_and_returns_BattleStart_TurnStart_events` | 新規 start で session 作成 + events 列に BattleStart/TurnStart |
| `Start_is_idempotent_returns_same_session` | 連続呼出で session 維持（リロード対応） |
| `Get_when_session_exists_returns_BattleStateDto` | GET で current state 取得 |
| `Get_when_no_session_returns_404` | session 無しで 404 |
| `PlayCard_with_valid_index_advances_state_and_returns_events` | play-card 成功 + events |
| `PlayCard_with_invalid_handIndex_returns_400` | range out で 400 |
| `PlayCard_with_insufficient_energy_returns_400` | cost 不足で 400 |
| `EndTurn_resolves_phase_transitions_and_returns_events` | EndTurn 成功 + 攻撃発射 events |
| `EndTurn_when_player_dies_sets_Outcome_to_Defeat` | hero 死亡で state.Outcome=Defeat |
| `UsePotion_with_valid_slot_consumes_potion` | slot[i] が "" になる |
| `UsePotion_with_empty_slot_returns_400` | 空 slot で 400 |
| `SetTarget_updates_target_indices_without_events` | events なし、state のみ返却 |
| `Finalize_Victory_creates_reward_and_clears_session` | RunState.ActiveReward セット + session 削除 |
| `Finalize_Defeat_sets_Progress_GameOver_and_returns_RunResultDto` | GameOver フロー、save 削除 |
| `Finalize_Victory_when_boss_last_act_returns_RunResultDto_Cleared` | Boss + last act → ラン終了 |
| `Finalize_when_battle_not_resolved_returns_409` | Phase != Resolved で 409 |
| `Finalize_consumes_potions_in_RunState_Potions` | UsePotion 後 finalize で `RunState.Potions[i] == ""` |
| `BattleStateDto_serializes_AttackPool_Display_correctly` | 力 / 脱力バフ込みの Display 値が正しく DTO に乗る |

合計 想定 20 テスト。既存 `RunsController` テストパターンを真似て `WebApplicationFactory<Program>` ベース。

### 6-2. MVP の暗黙要件（必ず含める）

| # | 項目 | 理由 / 実装場所 |
|---|---|---|
| F1 | `BestiaryTracker.NoteEnemiesEncountered` 呼出 | 既存 `BattlePlaceholder.Start` で実施済、新 `BattleEngine.Start` 経路でも維持必要（図鑑の敵記録）。`BattleController.Start` の冪等チェック後、`BattleEngine.Start` 呼出と並列で `BestiaryTracker.NoteEnemiesEncountered(run, encounter.EnemyIds)` を実行し save に書き戻す |
| F2 | 戦闘中ラン放棄時の session 削除 | `RunsController.Abandon` (既存) の `await _saves.DeleteAsync` 前に `_sessions.Remove(accountId)` を呼出。`BattleSessionStore` のメモリリーク防止 |
| F3 | `CatalogController` への units/enemies/potions endpoint 追加 | UI 表示に必須（`useUnitCatalog` 等の hook 実装に依存）|
| F4 | `run.Relics` の `BattleEngine.Start` 渡し | 既に Phase 10.2.E Task 2 で実装済、確認のみ |

これらは MVP の動作要件であり、必ず実装する。

### 6-3. テストフィクスチャ

新規 fixture を `tests/Server.Tests/Fixtures/BattleControllerFixtures.cs` に集約:

- `CreateClient(/* test server setup */)`: WebApplicationFactory ベースの test client
- `SetupRunWithActiveBattle(client, accountId, encounterId)`: 戦闘進入直前の RunState を save に書き込む helper
- `AssertNoSession(services, accountId)`: `BattleSessionStore` が空であることを確認

`DataCatalog` のテスト用は既存 Server.Tests のパターン（minimal JSON catalog）を流用。

### 6-4. 既存テストへの影響

- `BattleOverlay.test.tsx`: 削除（Q2-A 確定でファイル退役）
- `RunsController` の `PostBattleWin` テスト: **無変更** (placeholder 用 endpoint は共存維持)
- `BattleStateDto` リネーム → `BattlePlaceholderStateDto` に伴うテスト更新: 既存テストの type 参照を機械的に更新

### 6-5. 手動プレイ確認チェックリスト

実装完了時に以下を手動で確認:

- [ ] 新規ラン開始 → マップで敵タイル進入 → BattleScreen 表示
- [ ] 手札カードクリック → カード効果適用 + Energy 消費 + Discard へ移動
- [ ] エネミーをクリック → ターゲット切替（視覚ハイライト）
- [ ] End Turn → 攻撃発射 events 演出 → 敵行動 → 次ターン開始
- [ ] ポーションクリック → 効果発動 + slot 空に
- [ ] Victory: 全敵撃破 → finalize → Reward 画面に遷移
- [ ] Defeat: hero HP 0 → finalize → RunResult 画面 (GameOver)
- [ ] Boss + last act 撃破 → ラン Cleared
- [ ] レリック発動: OnTurnStart relic 持ちでターン開始時に効果発火（events 演出に `relic:<id>` Note）
- [ ] ブラウザリロード: 戦闘中に F5 → 戦闘リセットして同 encounter で再開
- [ ] 既存 placeholder 経路 (`POST /battle/win`) が無傷で動作（テスト経路として残存）
- [ ] 図鑑に新敵が記録されている（F1）
- [ ] 戦闘中にラン放棄 → session が削除されている（F2、再開できないことで確認）

### 6-6. ビルド赤期間管理

破壊的変更:
1. `BattleStateDto` (placeholder 用) → `BattlePlaceholderStateDto` リネーム → 既存 `RunSnapshotDtoMapper` / Client `types.ts` / `BattleOverlay.tsx` への type 参照更新
2. 新 `BattleStateDto` (フル DTO) と `BattleEventDto` 等の追加 → 既存コードに影響なし
3. `BattleSessionStore` / `BattleController` 追加 → 既存に影響なし
4. `BattleOverlay.tsx` 削除 → `MapScreen.tsx` の置換と同 commit

実装順序の概略（plan で詳細化）:
1. Server: DTO リネーム + 全参照追従
2. Server: 新 DTO 群追加 + `BattleSessionStore` + `BattleStateDtoMapper`
3. Server: `BattleController` 各 endpoint TDD
4. Server: `Finalize` の Victory/Defeat 分岐
5. Server: `CatalogController` 拡張
6. Server: `RunsController.Abandon` で session cleanup (F2)
7. Client: 新 type + battle.ts API + catalog hook 追加
8. Client: `BattleScreen.tsx` 改修 (dtoAdapter + onClick wiring + animation queue)
9. Client: `BattleOverlay.tsx` 削除 + `MapScreen.tsx` 置換
10. 手動プレイ確認 + 残調整

### 6-7. テスト実行コマンド

- 1 ファイル: `dotnet test --filter FullyQualifiedName~BattleControllerTests`
- 全 Server: `dotnet test --filter FullyQualifiedName~Server`
- 全体: `dotnet build && dotnet test`

---

## 7. スコープ外 + 後続フェーズへの移行ロードマップ

### 7-1. Phase 10.3-MVP では触らない（核心スコープ外）

- **SignalR `BattleHub`**: REST のみで実装。Phase 10.3 本格で SignalR 化（endpoint ロジックは Hub メソッドに移植可能）
- **戦闘中 save 対応**: in-memory のみ、リロードで戦闘進行リセット。Phase 10.5 で本格保存
- **`RunState.ActiveBattle: BattlePlaceholderState?` の型変更**: placeholder と新 BattleEngine が共存。Phase 10.5 で削除 + save schema v8 マイグレーション
- **`BattlePlaceholder.cs` / `BattlePlaceholderState.cs` の削除**: Phase 10.5
- **`POST /api/v1/runs/current/battle/win` 既存 endpoint の削除**: Phase 10.5
- **戦闘外 UsePotion (マップ画面ポーション UI)**: Phase 10.5
- **`OnPotionUse` レリック Trigger**: Phase 11+
- **Event 演出のリッチ化**: ダメージ数値 flash / カード再生アニメ / 敵 attack のテレグラフ等。10.3-MVP は最小（CSS transition のみ）、polish は Phase 10.4
- **複数戦闘並走**: 1 アカウント 1 戦闘前提
- **戦闘中の disconnect 検出 / タイムアウト処理**: 単純な「リロード時 GET 404 → POST start で再開」のみ
- **Phase 10.4 React BattleScreen polish**: animation queue の高度化、event Kind 別演出、敵 intent のリッチ表示等

### 7-2. 後続フェーズへの移行ロードマップ

#### A. MVP 完了後も残る不完全な部分（実プレイ可能だが未洗練）

| # | 項目 | MVP の状態 | 本番との差 |
|---|---|---|---|
| A1 | 戦闘中の永続化 | in-memory のみ。リロードで戦闘進行ロスト → 同 encounter で最初から再開 | 戦闘中も毎アクション後に save、リロードしても進行維持 |
| A2 | Event 演出 | events を 200ms ステップで順次再生、HP/Block は CSS transition の数値補間のみ | 各 event Kind 別演出（ダメージ数値 popup、attack orb 飛行、ActorDeath dissolve、card flying to discard 等）|
| A3 | 敵 intent 表示 | `currentMoveId` から MoveDefinition を引く程度の単純表示 | Slay the Spire 風の精密 intent（"14 ダメージ × 2 ヒット" / "15 防御" 等、effects を解析）|
| A4 | エラーハンドリング | 400/409 は文字列で setError、日本語化最小 | ユーザフレンドリ日本語メッセージ、リトライ UI、state desync 時の再 sync |
| A5 | AttackPool 消費の演出 | ターン終了 → 全 events 一気に reveal、Pool 消費の途中表示なし | 攻撃発射中に hero の AttackPool 値が `Sum × 1, 2, 3` と減っていく演出 |
| A6 | 戦闘中ラン放棄時の session cleanup | 暗黙要件 F2 で対応するがテスト範囲限定 | abandon endpoint で session 削除 + テスト網羅 |
| A7 | 複数戦闘並走 / disconnect 処理 | 1 アカウント 1 戦闘前提、disconnect timeout なし | session lifecycle 管理（タイムアウト + 再接続トークン）|

#### B. Phase 10.3 本格で実装する部分（SignalR 化 + 永続化）

| # | 項目 | MVP | 本格 10.3 |
|---|---|---|---|
| B1 | 通信プロトコル | REST、各アクションで `(state, events)` 一括返却 | SignalR `BattleHub`、event を逐次 server → client push |
| B2 | 戦闘 state の永続化 | in-memory `BattleSessionStore` | DB or save に乗せる（リロード対応 + マルチ協力時の合意状態）|
| B3 | DTO 設計 | `BattleEventStepDto.SnapshotAfter` は常に最終 state と同一（近似）| 各 event の本物の中間 state（または client が局所差分計算）|

10.3-MVP の REST endpoint ロジックは丸ごと SignalR Hub メソッドに移植可能。

#### C. Phase 10.4 で実装する部分（Client polish）

| # | 項目 | MVP | 10.4 |
|---|---|---|---|
| C1 | カード移動アニメーション | CSS fade out 程度 | カードが捨札 / 除外パイルに飛んでいく動的アニメ |
| C2 | Event 演出のリッチ化 | A2 と同 | 各 event Kind に専用演出 |
| C3 | Client 自動テスト | 手動プレイのみ | vitest 結合テスト + E2E (Playwright) |
| C4 | UI 微調整 | battle-v10.html canonical の素のポート | 実プレイで気付いた違和感の調整 |

#### D. Phase 10.5 で実装する部分（cleanup + 戦闘外）

| # | 項目 | MVP | 10.5 |
|---|---|---|---|
| D1 | `BattlePlaceholder` 退役 | 共存維持 (戦闘中マーカーとして使用) | `BattlePlaceholder.cs` / `BattlePlaceholderState.cs` 削除 |
| D2 | `POST /battle/win` 退役 | 並走維持（Client は使わない）| endpoint 削除 |
| D3 | `RunState.ActiveBattle` 型切替 | `BattlePlaceholderState?` のまま | `BattleState?` に切替 + save schema v8 マイグレーション |
| D4 | 戦闘外 UsePotion | 戦闘内のみ、マップ画面のポーション UI から発動不可 | `POST /runs/current/use-potion` + `OutOfBattleEffectApplier` ヘルパー |
| D5 | placeholder 系 DTO 削除 | `BattlePlaceholderStateDto` 残存 | 削除 |

#### E. Phase 9 / Phase 11+（さらに先）

| # | 項目 | 対象 phase |
|---|---|---|
| E1 | マルチプレイ協力戦闘 (2-4 人) | Phase 9（後回し方針）|
| E2 | `OnPotionUse` レリック Trigger | Phase 11+ |
| E3 | 複数 summon を 1 カードで連発 | Phase 11+ |
| E4 | 召喚 actor 自身がカードを発動 / move 駆動 attack | Phase 11+ |
| E5 | 死亡 summon の slot 再利用 | Phase 11+ |
| E6 | アンドゥ / replay 機能 | 別 phase で検討 |

### 7-3. Phase 10.3-MVP 完了後の状態

- **公開 API**: `POST /api/v1/runs/current/battle/{start, play-card, end-turn, use-potion, set-target, finalize}` + `GET /api/v1/runs/current/battle` の 7 endpoint
- **既存 `POST /battle/win`**: 並走維持（Phase 10.5 で削除予定）
- **戦闘 state**: `BattleSessionStore` (in-memory) で accountId 単位保持
- **`BattleStateDto` (新)**: フル DTO、AttackPool / BlockPool は Display 値計算済み
- **`BattleEventDto` + `BattleEventStepDto`**: event 順次再生サポート
- **既存 `BattleStateDto`**: `BattlePlaceholderStateDto` にリネーム
- **Client**:
  - `BattleScreen.tsx` が production で動作、battle-v10.html ベースの polished UI
  - `BattleOverlay.tsx` / `BattleOverlay.test.tsx` 削除
  - `?demo=battle` URL 廃止
  - 7 個の battle API + catalog hooks (units/enemies/potions) 追加
- **テスト**: `BattleControllerTests` 約 20 テスト追加、Server.Tests 全緑
- **手動プレイ確認**: §6-5 チェックリスト全項目完了
- **既存ゲームフロー**: マップ進行 / リワード / マーチャント / イベント / 休憩 / 図鑑 / アクト遷移 / ラン終了 すべて無傷
- **タグ**: `phase10-3-mvp-complete` を切って push

### 7-4. 親 spec 補記事項

`docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に補記:

1. **§10-3 (新設 or 既存補強)**: Phase 10.3 を「10.3-MVP (REST + in-memory)」と「10.3 本格 (SignalR + 永続化)」に分割する旨。10.3-MVP の範囲・API contract・Client 結線方針を補記
2. **§3-3 `BattleStateDto`**: Server 側 DTO 設計 (フル DTO + AttackPool Display 値計算済み + Catalog 引きを Client 責任とする方針) を追記
3. **§9-7 `BattleEventDto`**: 10.3-MVP で `BattleEventStepDto { event, snapshotAfter }` を導入。`SnapshotAfter` は最終 state と同一 (近似演出)、Client は CSS transition で補間

これら 3 項目は Phase 10.3-MVP 内で発生した設計判断の追記。コードと spec の乖離を残さない。

### 7-5. memory feedback ルールの遵守チェックリスト

実装中・レビュー時に確認する 2 項目（`memory/feedback_battle_engine_conventions.md`）:

- [ ] `BattleOutcome` 参照は今回新規発生しうる箇所:
  - `BattleController.Finalize` の Victory/Defeat 分岐: `summary.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory` の fully qualified
  - `BattleStateDtoMapper.ToDto` で `state.Outcome.ToString()` (string 化なので fully qualified 不要、enum 自体への参照なし)
- [ ] `state.Allies` / `state.Enemies` への書き戻しは InstanceId で検索:
  - `BattleController` は `BattleEngine.*` に委譲のみで Allies/Enemies を直接書き戻さない → 該当なし
  - `BattleStateDtoMapper` は read-only 変換 → 該当なし
- [ ] `BattleSessionStore` の thread-safety: `ConcurrentDictionary` 使用 + ASP.NET Core パイプラインで同 accountId 並列なし

---

## 参照

- 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン spec: [`2026-04-27-phase10-2E-relics-potions-design.md`](2026-04-27-phase10-2E-relics-potions-design.md)
- ロードマップ: [`../plans/2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md)
- memory feedback: `memory/feedback_battle_engine_conventions.md`
- 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html` (`src/Client/src/screens/BattleScreen.tsx` に既ポート済)
