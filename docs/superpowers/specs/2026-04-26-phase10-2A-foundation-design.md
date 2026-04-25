# Phase 10.2.A — Core バトル基盤スケルトン 設計

> 作成日: 2026-04-26
> 対象フェーズ: Phase 10.2.A（Phase 10.2 サブマイルストーン 1 番目 / 全 5 段階）
> 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
> 直前マイルストーン: Phase 10.1.C — Potion / Relic 拡張（`phase10-1C-complete` タグ）
> 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html`

## ゴール

Phase 10.2 の最小単位として、**バトルロジックの walking-skeleton** を Core に建てる。`BattleState` を全面刷新し、`BattleEngine` の 4 公開 API（`Start` / `PlayCard` / `EndTurn` / `Finalize`）と `BattleEvent` 発火基盤を導入する。effect は `attack` / `block` の 2 種のみ実装し、xUnit で「カードを撃つ → ターン終了で発射 → 敵 HP が削れる → 全敵死亡で Victory」の往復が完走することを完了判定とする。

状態異常・コンボ・対象指定の自動切替（基本以外）・残り effect・召喚・レリック・ポーション戦闘内発動はすべて後続 sub-phase に持ち越し、本フェーズは「殴り合うだけのバトル」を pure Core で動かすことに集中する。

`BattlePlaceholder.cs` および `NodeEffectResolver` の wire-up は本フェーズでは触らず、`BattleEngine` は既存ゲームフローと並走する pure Core API として独立する（実プレイへの反映は Phase 10.3 で `BattleHub` 追加時にまとめて行う）。

## 完了判定

- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（既存 + 本フェーズ追加分）
- `BattleEngine.Start` / `PlayCard` / `EndTurn` / `Finalize` の 4 公開 API が動作
- `attack` / `block` の 2 effect で Victory（全敵死亡）/ Defeat（hero HP 0）両方の戦闘が xUnit で完走
- 既存の `BattlePlaceholder` 経由のフロー（敵タイル進入 → 即勝利ボタン → 報酬画面）は無傷で動作（手動プレイ確認）
- 旧 `BattleState` (`EncounterId + Enemies + Outcome=Pending|Victory`) は `BattlePlaceholderState` にリネームし、`BattlePlaceholder.cs` 専用に隔離
- 新 `BattleState` が `src/Core/Battle/State/BattleState.cs` に追加され、`BattleEngine` から使われる
- `RunState.ActiveBattle` の型は `BattlePlaceholderState?` に変更（10.5 で新 `BattleState?` に切替）
- save JSON shape は変更なし（旧 `BattleState` と `BattlePlaceholderState` のフィールド構造は同一）→ schema 移行不要
- 親 spec の該当章に Phase 10.2.A 内で発生した設計判断を補記済み（旧 BattleState→BattlePlaceholderState リネーム、`BattleCardInstance` 命名分離、`AttackPool`/`BlockPool` の 10.2.A 段階の暫定 API、Phase 10.2.A での Outcome=Defeat 追加）
- `phase10-2A-complete` タグが切られ origin に push 済み

---

## 1. アーキテクチャ概要

### 1-1. Phase 10.2 全体の中での位置付け

Phase 10.2 は親 spec §3-§10 の Core バトル本体を 5 段階で建てる。

| サブ phase | 範囲 |
|---|---|
| **10.2.A**（本 spec） | 基盤データモデル + `BattleEngine` 4 公開 API + `attack`/`block` の 2 effect + Phase 進行 + Victory/Defeat |
| 10.2.B | 状態異常 6 種 + 遡及計算 + buff/debuff effect + ターン開始 tick + omnistrike 合算発射 |
| 10.2.C | コンボ（Wild/SuperWild/コスト軽減/comboMin） + 対象指定の SetTarget アクション |
| 10.2.D | 残り effect 8 種（heal/draw/discard/upgrade/exhaustCard/exhaustSelf/retainSelf/gainEnergy）+ 召喚 system + カード移動 5 段優先順位 + PowerCards |
| 10.2.E | レリック 4 新 Trigger 発火 + 所持順発動 + Implemented スキップ + UsePotion 戦闘内 + BattleOnly 戦闘外スキップ |

10.2.A は 10.2.B〜E すべての土台で、**データ構造と公開 API シグネチャを安定させる**ことが最重要目標。後続 phase は 10.2.A の API シグネチャを維持しながら内部処理と effect 種類を拡張していく。

### 1-2. 共存戦略（NodeEffectResolver / BattlePlaceholder）

Phase 10.2.A〜E の間、新 `BattleEngine` は **pure Core API として独立**し、`NodeEffectResolver` は引き続き `BattlePlaceholder.Start` を呼ぶ（既存ゲームフローは無傷）。`BattleEngine` は xUnit でしかテストされない。

`BattleHub` 追加と `BattlePlaceholder` 退役は Phase 10.3 / 10.5 で行う。本フェーズでは触らない。

### 1-3. ファイル構成（10.2.A 完了時）

```
src/Core/Battle/
├── State/                                [新フォルダ]
│   ├── BattleState.cs                    [新] 親 spec §3-1 の本格 BattleState（旧 placeholder と別物）
│   ├── BattlePhase.cs                    [新] enum 4 値
│   ├── BattleOutcome.cs                  [既存修正] Pending/Victory + Defeat 追加
│   ├── ActorSide.cs                      [新] enum Ally/Enemy
│   ├── CombatActor.cs                    [新] バトル中の actor 状態
│   ├── AttackPool.cs                     [新] readonly record struct
│   ├── BlockPool.cs                      [新] readonly record struct
│   └── BattleCardInstance.cs             [新] バトル用カード instance（Cards/CardInstance.cs と別物）
├── Events/                               [新フォルダ]
│   ├── BattleEvent.cs                    [新] record
│   └── BattleEventKind.cs                [新] enum 9 値
├── Engine/                               [新フォルダ]
│   ├── BattleEngine.cs                   [新] public 静的ファサード（Start/PlayCard/EndTurn/Finalize）
│   ├── BattleSummary.cs                  [新] Finalize 戻り値 record
│   ├── EffectApplier.cs                  [新] internal static、effect → state + events
│   ├── PlayerAttackingResolver.cs        [新] internal static、PlayerAttacking フェーズ実行
│   ├── EnemyAttackingResolver.cs        [新] internal static、EnemyAttacking フェーズ実行
│   ├── TurnStartProcessor.cs             [新] internal static、ターン開始処理
│   └── TurnEndProcessor.cs               [新] internal static、ターン終了処理
├── Statuses/                             [新フォルダ・10.2.A では空、10.2.B で StatusDefinition 追加]
├── Definitions/                          [既存・無変更]
├── EncounterQueue.cs                     [既存・無変更]
├── BattlePlaceholder.cs                  [既存・無変更, 10.5 で削除]
└── BattlePlaceholderState.cs             [既存 BattleState.cs をリネーム + 型名変更, 10.5 で削除]

src/Core/Cards/
└── CardInstance.cs                       [既存・無変更] RunState.Deck 用、`(Id, Upgraded)` 2 フィールド

tests/Core.Tests/Battle/
├── State/
│   ├── BattleStateInvariantTests.cs
│   ├── AttackPoolTests.cs
│   ├── BlockPoolTests.cs
│   ├── CombatActorTests.cs
│   └── BattleCardInstanceTests.cs
├── Events/
│   └── BattleEventEmissionTests.cs
├── Engine/
│   ├── BattleEngineStartTests.cs
│   ├── BattleEnginePlayCardTests.cs
│   ├── BattleEngineEndTurnTests.cs
│   ├── BattleEngineFinalizeTests.cs
│   ├── PlayerAttackingResolverTests.cs
│   ├── EnemyAttackingResolverTests.cs
│   ├── TurnStartProcessorTests.cs
│   ├── TurnEndProcessorTests.cs
│   ├── TargetingAutoSwitchTests.cs
│   └── BattleDeterminismTests.cs
└── Fixtures/
    └── BattleFixtures.cs
```

### 1-4. namespace

| パス | namespace |
|---|---|
| `src/Core/Battle/State/*.cs` | `RoguelikeCardGame.Core.Battle.State` |
| `src/Core/Battle/Events/*.cs` | `RoguelikeCardGame.Core.Battle.Events` |
| `src/Core/Battle/Engine/*.cs` | `RoguelikeCardGame.Core.Battle.Engine` |
| `src/Core/Battle/Definitions/*.cs` | `RoguelikeCardGame.Core.Battle.Definitions`（既存） |
| `src/Core/Battle/{BattlePlaceholder,EncounterQueue}.cs` | `RoguelikeCardGame.Core.Battle`（既存） |

**型衝突回避とリネーム戦略**:

- 旧 `Core.Battle.BattleState`（`EncounterId + Enemies + Outcome` の placeholder）は **`Core.Battle.BattlePlaceholderState` にリネーム**
- 旧 `Core.Battle.EnemyInstance` は **`Core.Battle.PlaceholderEnemyInstance` にリネーム**
- ファイルは `src/Core/Battle/BattleState.cs` から `src/Core/Battle/BattlePlaceholderState.cs` へ rename
- `RunState.ActiveBattle: BattleState?` → `RunState.ActiveBattle: BattlePlaceholderState?` に型変更
- `BattlePlaceholder.cs` の中身（`Start` / `Win`）はリネームに合わせて型参照を更新
- 新 `BattleState` は `Core.Battle.State.BattleState` として完全に独立（同名衝突なし）
- 10.5 cleanup で `BattlePlaceholder.cs` / `BattlePlaceholderState.cs` / `PlaceholderEnemyInstance` をすべて削除し、`RunState.ActiveBattle` を新 `Core.Battle.State.BattleState?` に切替（同時に save schema v8 マイグレーション追加）

**save JSON 互換性**:

旧 `BattleState` と `BattlePlaceholderState` は record 名のみ変更でフィールド構造は完全に同一。System.Text.Json の出力 JSON shape (`{ "encounterId": ..., "enemies": [...], "outcome": ... }`) は変わらないため、save schema migration（v7 → v8 等）は **10.2.A では不要**。10.5 で本格 `BattleState` に切替える際に migration を追加する。

### 1-5. 設計意図

- **State / Events / Engine / Statuses のライフサイクル分割**: 10.1.B spec §2-4 で予告した「Definitions / State / Actions / Events のライフサイクル分割」を本フェーズで実体化。10.2.A は Statuses フォルダを空のまま用意し、10.2.B で StatusDefinition を追加する受け皿とする。`Actions/` は API パターン Q3 で「静的メソッド分割」を選択したため作成しない（リプレイ機能が必要になった時点で導入を検討）。
- **`BattleCardInstance` 命名分離**: 既存 `src/Core/Cards/CardInstance.cs` は `RunState.Deck` 用の `(Id, Upgraded)` 2 フィールドで、Phase 10 全体で構造を変えない。バトル中のパイルカードは `InstanceId`（重複カード識別用）と `CostOverride`（戦闘内一時上書き用）が必要なため、別 record `BattleCardInstance` として `Battle/State/` に置く。`StartBattle` 時に `RunState.Deck` の各 `CardInstance` から `BattleCardInstance` を生成する。
- **`BattleEngine` を静的ファサード + internal static helper**: Q3 で確定の API パターン。`PlayerAttackingResolver` 等は `internal static` メソッド集とし、`BattleEngine` の各公開メソッドが orchestration を担う。
- **イベント emission を 10.2.A から導入**: Q7 で確定。`BattleEvent` record を 10.2.A から導入し、`BattleEngine` の各公開メソッドは `(BattleState, IReadOnlyList<BattleEvent>)` を返す。Kind は本フェーズで 9 種、後続 phase で必要に応じて追加。
- **Outcome に Defeat 追加**: 旧 `BattleOutcome { Pending, Victory }` に `Defeat` を追加。Phase 5 placeholder では Defeat 経路がなかったが、Phase 10.2 ではソロモードで主人公 HP=0 を Defeat として表現する必要がある。`BattlePlaceholder` 側は Defeat 経路を使わないので影響なし。

---

## 2. データモデル

### 2-1. `BattlePhase`

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>バトルの大局フェーズ。親 spec §3-1 / §4-1 参照。</summary>
public enum BattlePhase
{
    PlayerInput      = 0,    // プレイヤー操作受付中
    PlayerAttacking  = 1,    // 味方攻撃発射処理中
    EnemyAttacking   = 2,    // 敵行動処理中
    Resolved         = 3,    // 戦闘終了（Outcome != Pending）
}
```

### 2-2. `BattleOutcome`

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>バトル結果。Defeat はソロモードでのみ発生（Phase 10.2.A 時点）。</summary>
public enum BattleOutcome
{
    Pending  = 0,
    Victory  = 1,
    Defeat   = 2,    // 新規（10.2.A）
}
```

### 2-3. `ActorSide`

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

public enum ActorSide
{
    Ally   = 0,
    Enemy  = 1,
}
```

### 2-4. `AttackPool`

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// 攻撃値の蓄積プール。Phase 10.2.A は素値のみ（遡及計算なし）。
/// 力バフ / 脱力での Display 計算は 10.2.B で追加する。
/// </summary>
public readonly record struct AttackPool(int Sum, int AddCount)
{
    public static AttackPool Empty => new(0, 0);

    public AttackPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>10.2.A の暫定。10.2.B で `Display(strength, weak)` 拡張。</summary>
    public int RawTotal => Sum;
}
```

### 2-5. `BlockPool`

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// ブロック値の蓄積プール。Phase 10.2.A は素値のみ（敏捷遡及計算なし）。
/// 敏捷バフでの Display 計算は 10.2.B で追加する。
/// </summary>
public readonly record struct BlockPool(int Sum, int AddCount)
{
    public static BlockPool Empty => new(0, 0);

    public BlockPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>10.2.A の暫定。10.2.B で `Display(dexterity)` 拡張。</summary>
    public int RawTotal => Sum;

    /// <summary>
    /// 攻撃の総量を受けて Block を消費。引数 `incomingAttack` は「ブロック適用前の攻撃値」を渡す（ダメージ通り後ではない）。
    /// 残量を新 Sum、AddCount=0 にリセット（消費後は遡及性を失う）。
    /// 10.2.B で `Consume(incomingAttack, dexterity)` 拡張予定。
    /// </summary>
    public BlockPool Consume(int incomingAttack)
    {
        var remaining = Math.Max(0, Sum - incomingAttack);
        return new(remaining, 0);
    }
}
```

### 2-6. `CombatActor`

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中の戦闘者状態。主人公 / 召喚 / 敵すべて共通。
/// 親 spec §3-2 参照。Statuses / RemainingLifetimeTurns / AssociatedSummonHeldIndex は
/// 10.2.A スコープ外。10.2.B (Statuses) / 10.2.D (Lifetime / Summon) で追加。
/// </summary>
public sealed record CombatActor(
    string InstanceId,
    string DefinitionId,
    ActorSide Side,
    int SlotIndex,                                // 0..3 (内側→外側)
    int CurrentHp,
    int MaxHp,
    BlockPool Block,
    AttackPool AttackSingle,
    AttackPool AttackRandom,
    AttackPool AttackAll,
    string? CurrentMoveId)
{
    public bool IsAlive => CurrentHp > 0;
}
```

### 2-7. `BattleCardInstance`

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中のパイルカード instance。
/// `Cards.CardInstance`（RunState.Deck 用、Id+Upgraded のみ）とは別物。
/// バトル開始時に `Cards.CardInstance` から生成され、戦闘終了で破棄される。
/// 親 spec §3-4 参照。
/// </summary>
public sealed record BattleCardInstance(
    string InstanceId,                            // バトル中の一意 ID（重複カード識別用）
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride);                           // 戦闘内一時上書き（10.2.A では未使用、後続 phase で利用）
```

### 2-8. `BattleState`

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

using System.Collections.Immutable;

/// <summary>
/// バトル全体の不変状態。Phase 10.2.A 版。
/// 10.2.B〜E で Statuses / コンボ / SummonHeld / PowerCards 等のフィールドが追加される。
/// 親 spec §3-1 参照。
/// </summary>
public sealed record BattleState(
    int Turn,
    BattlePhase Phase,
    BattleOutcome Outcome,

    // 両陣営（最内側→外側）
    ImmutableArray<CombatActor> Allies,           // 主人公のみ（10.2.A）、召喚は 10.2.D
    ImmutableArray<CombatActor> Enemies,          // 最大 4
    int? TargetAllyIndex,
    int? TargetEnemyIndex,

    // プレイヤーリソース
    int Energy,
    int EnergyMax,

    // パイル
    ImmutableArray<BattleCardInstance> DrawPile,
    ImmutableArray<BattleCardInstance> Hand,
    ImmutableArray<BattleCardInstance> DiscardPile,
    ImmutableArray<BattleCardInstance> ExhaustPile,

    // メタ
    string EncounterId);
```

### 2-9. 不変条件（`BattleStateInvariantTests` で検証）

- `Allies.Length` ≥ 1（主人公必須）かつ ≤ 4
- `Enemies.Length` ≥ 1 かつ ≤ 4（戦闘開始時、Resolved 直前は 0 もあり得る）
- `Allies[0].DefinitionId == "hero"`（主人公はスロット 0、最内側固定）
- `TargetAllyIndex` / `TargetEnemyIndex` は null か、生存者のスロット index を指す
- `Phase == Resolved` ⇔ `Outcome != Pending`
- `Energy` ≥ 0 かつ `Energy ≤ EnergyMax`
- `Turn` ≥ 1
- 全パイル + Hand のカード合計 = 戦闘開始時の山札枚数（10.2.A はカード消失なしのため恒等成立）

### 2-10. `BattleEventKind`

```csharp
namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中に発火されるイベント種別。Phase 10.2.A の最小セット 9 種。
/// 後続 phase で ApplyStatus / RemoveStatus / Summon / Exhaust / Upgrade /
/// RelicTrigger / UsePotion 等を追加していく。
/// </summary>
public enum BattleEventKind
{
    BattleStart   = 0,    // StartBattle 完了時 1 回
    TurnStart     = 1,    // 各 PlayerInput フェーズ突入時
    PlayCard      = 2,    // PlayCard 開始時
    AttackFire    = 3,    // 1 つの AttackPool が発射開始
    DealDamage    = 4,    // 1 体の actor にダメージ着弾
    GainBlock     = 5,    // 1 体の actor が Block 加算
    ActorDeath    = 6,    // CombatActor.IsAlive が true → false に変わった瞬間
    EndTurn       = 7,    // EndTurn アクション受領時
    BattleEnd     = 8,    // Outcome != Pending に変わった瞬間
}
```

### 2-11. `BattleEvent`

```csharp
namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中の 1 イベント。`BattleEngine` の各公開メソッドが
/// `IReadOnlyList<BattleEvent>` として時系列順に返す。
/// Phase 10.3 で `BattleEventDto` に変換され Client に push される。
/// 親 spec §9-7 参照。
/// </summary>
/// <param name="Kind">イベント種別</param>
/// <param name="Order">同一トリガー内での発火順 (0 始まり)</param>
/// <param name="CasterInstanceId">発動主体 actor の InstanceId（該当しない時 null）</param>
/// <param name="TargetInstanceId">対象 actor の InstanceId（該当しない時 null）</param>
/// <param name="Amount">数値量（ダメージ / Block / etc.、該当しない時 null）</param>
/// <param name="CardId">関連カードの DefinitionId（PlayCard 等、該当しない時 null）</param>
/// <param name="Note">補足文字列（デバッグ・将来拡張用、該当しない時 null）</param>
public sealed record BattleEvent(
    BattleEventKind Kind,
    int Order,
    string? CasterInstanceId = null,
    string? TargetInstanceId = null,
    int? Amount = null,
    string? CardId = null,
    string? Note = null);
```

---

## 3. `BattleEngine` 公開 API

### 3-1. `Start`

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// バトルを開始する。RunState の HP / Deck から BattleState を組み立てる。
    /// 戻り値は <see cref="BattleState"/> のみ（イベントは BattleStart の単発でしかなく、
    /// 呼び出し側で必要なら別途生成）。
    /// </summary>
    public static BattleState Start(
        RunState run,
        string encounterId,
        IRng rng,
        DataCatalog catalog);
}
```

**処理フロー**:

1. `catalog.Encounters[encounterId]` から `EncounterDefinition` を取得
2. 主人公 `CombatActor` を生成（`SlotIndex=0`, `DefinitionId="hero"`, `CurrentHp=run.CurrentHp`, `MaxHp=run.MaxHp`, `CurrentMoveId=null`）
3. 各敵 `EnemyDefinition` から `CombatActor` を生成（`SlotIndex=0..N-1`, `Side=Enemy`, `CurrentMoveId=def.InitialMoveId`）
4. `run.Deck` の各 `Cards.CardInstance` を `BattleCardInstance` に変換し、`rng.Shuffle` で並べ替えて山札に
5. `Hand` / `DiscardPile` / `ExhaustPile` を空配列で初期化
6. `Energy = EnergyMax = 3`
7. `TurnStartProcessor.Process(state, rng)` を呼んでターン 1 開始処理（5 ドローのみ。10.2.B 以降で毒・状態異常 tick / OnTurnStart レリック等が追加される）
8. `TargetAllyIndex = 0`, `TargetEnemyIndex = 0`
9. `Phase = PlayerInput`, `Outcome = Pending`, `Turn = 1`
10. 完成した `BattleState` を返す

> 注: `OnBattleStart` レリック発動は 10.2.E。本フェーズでは何もしない。

### 3-2. `PlayCard`

```csharp
public static partial class BattleEngine
{
    /// <summary>
    /// 手札のカード 1 枚をプレイする。エナジー不足ならエラー（呼び出し側でガード推奨）。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) PlayCard(
        BattleState state,
        int handIndex,
        int? targetEnemyIndex,
        int? targetAllyIndex,
        IRng rng,
        DataCatalog catalog);
}
```

**処理フロー**（10.2.A 簡略版、コンボ・カード移動 5 段優先順位なし）:

1. `state.Phase == PlayerInput` でなければ `InvalidOperationException`
2. `handIndex` の範囲チェック → `BattleCardInstance` を取得
3. `catalog.Cards[card.CardDefinitionId]` で `CardDefinition` を引く
4. cost を判定（10.2.A では `IsUpgraded ? def.UpgradedCost ?? def.Cost : def.Cost`、コンボ軽減は 10.2.C）
5. cost が null なら `InvalidOperationException`（プレイ不可カード）
6. `state.Energy < cost` なら `InvalidOperationException`
7. `Energy -= cost`
8. effect 配列を `EffectApplier.Apply` で全件適用（10.2.A は `attack` / `block` のみ対応、それ以外の action は no-op + warn）
9. カードを `Hand` から取り除き `DiscardPile` 末尾へ
10. `PlayCard` イベントを発火（先頭）+ 各 effect の発火 event を時系列順に追加
11. `(BattleState, events)` を返す

> 注: `OnCardPlay` レリック発火は 10.2.E。本フェーズではスキップ。

### 3-3. `EndTurn`

```csharp
public static partial class BattleEngine
{
    /// <summary>
    /// プレイヤーターンを終了する。PlayerAttacking → 死亡判定 → EnemyAttacking →
    /// 死亡判定 → TurnEnd 処理 → TurnStart 処理 を一気通貫で実行し、
    /// 次の PlayerInput フェーズの状態と全イベントを返す。
    /// 途中で Outcome != Pending になった場合は Resolved 状態で返す。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) EndTurn(
        BattleState state,
        IRng rng,
        DataCatalog catalog);
}
```

**処理フロー**:

1. `state.Phase == PlayerInput` でなければ `InvalidOperationException`
2. `EndTurn` イベント発火
3. Phase = `PlayerAttacking`
4. `PlayerAttackingResolver.Resolve(state, rng)` → 各 ally の Single→Random→All 順で発射、`AttackFire` + `DealDamage` + 必要に応じて `ActorDeath` イベントを追加
5. 死亡判定: 全敵 `IsAlive == false` なら `Outcome = Victory`, `Phase = Resolved`, `BattleEnd` イベント発火 → return
6. 対象敵が死亡していたら最小スロット生存者へ自動切替（§4 参照）
7. Phase = `EnemyAttacking`
8. `EnemyAttackingResolver.Resolve(state, rng, catalog)` → 各生存敵の `MoveDefinition.Effects` を per-effect 即時発射、`NextMoveId` へ遷移
9. 死亡判定: 主人公 `IsAlive == false` なら `Outcome = Defeat`, `Phase = Resolved`, `BattleEnd` イベント発火 → return
10. 対象味方が死亡していたら… 10.2.A では味方は主人公だけなので、主人公死亡 = Defeat 確定で 9 で return される
11. `TurnEndProcessor.Process(state)`: 全 actor の Block / AttackPool リセット、手札全捨て（10.2.A は retainSelf 未対応のため全捨て）
12. `TurnStartProcessor.Process(state, rng, catalog)`: ターン+1, Energy 全回復, 5 ドロー（手札上限 10）
13. Phase = `PlayerInput`, `TurnStart` イベント発火
14. `(BattleState, events)` を返す

### 3-4. `Finalize`

```csharp
public static partial class BattleEngine
{
    /// <summary>
    /// 戦闘終了後に RunState への反映を行う。
    /// HP のみ戻し、バトル用デッキ / 状態 / 召喚は破棄される。
    /// 親 spec §10-2 参照。
    /// </summary>
    public static (RunState, BattleSummary) Finalize(
        BattleState state,
        RunState before);
}
```

**処理フロー**:

1. `state.Phase == Resolved` でなければ `InvalidOperationException`
2. 主人公の最終 HP を取得（`state.Allies[0].CurrentHp`）
3. `RunState` を更新:
   - `CurrentHp = finalHeroHp`
   - `ActiveBattle = null`（呼び出し側で報酬画面 / GameOver 画面に遷移）
   - `Outcome == Victory`: `RunProgress` は `InProgress` 維持、報酬画面遷移は呼び出し側で `RewardGenerator` を起動
   - `Outcome == Defeat`: `Progress = RunProgress.GameOver`
4. `BattleSummary` を返す（呼び出し側が遷移判定に使う）

10.2.A では `ConsumedPotionIds` / `RunSideOperations` は持たない（10.2.E で追加）。

### 3-5. `BattleSummary`

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

public sealed record BattleSummary(
    int FinalHeroHp,
    BattleOutcome Outcome,
    string EncounterId);
// ※ ConsumedPotionIds / RunSideOperations は 10.2.E で追加
```

---

## 4. 内部 helper（`Engine/` 配下）

### 4-1. `EffectApplier`

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class EffectApplier
{
    /// <summary>
    /// 1 つの effect を適用し、新 state とイベント列を返す。
    /// Phase 10.2.A では action が "attack" / "block" 以外なら no-op（warn ログのみ）。
    /// 10.2.B〜E で対応 action が増えていく。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
        BattleState state,
        CombatActor caster,
        CardEffect effect,
        IRng rng);
}
```

**Phase 10.2.A での処理**:

| Action | 処理 |
|---|---|
| `attack` | `effect.Scope` で振分: Single → caster.AttackSingle.Add(amount) / Random → caster.AttackRandom.Add(amount) / All → caster.AttackAll.Add(amount) / Self → 不正（CardEffect.Normalize で弾かれる） |
| `block` | ターゲット解決後、`target.Block.Add(amount)`、`GainBlock` イベント発火 |
| その他 | no-op + `Note: $"action '{effect.Action}' not implemented in Phase 10.2.A"` の `BattleEvent` を返さない（テストノイズ防止）。Debug.Assert か内部カウンタは入れず、後続 phase で実装される前提 |

ターゲット解決:
- `Self` → caster
- `Single, Side=Enemy` → `state.Enemies[state.TargetEnemyIndex.Value]`
- `Single, Side=Ally` → `state.Allies[state.TargetAllyIndex.Value]`
- `Random, Side=...` → 該当 side の生存 actor から `rng` で 1 体（10.2.A では `attack` の蓄積のみで、ApplyEffect 段階では targets は使わない）
- `All, Side=...` → 該当 side の全 actor

10.2.A の `attack` は `caster.AttackPool` への加算のみで、ターゲット解決は不要（発射は `PlayerAttackingResolver` / `EnemyAttackingResolver` で行う）。`block` のみターゲット解決が必要。

### 4-2. `PlayerAttackingResolver`

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class PlayerAttackingResolver
{
    /// <summary>
    /// PlayerAttacking フェーズ実行。各 ally の Single→Random→All の順で発射。
    /// 10.2.A は ally = 主人公 1 体のみ。10.2.D で召喚を inside-out で含める。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState state, IRng rng);
}
```

**処理フロー**（10.2.A、omnistrike なし、力/脱力/脆弱なし）:

```
foreach ally in Allies.OrderBy(SlotIndex):  // 10.2.A は主人公だけ
  if (!ally.IsAlive) continue;

  // 1. Single 攻撃
  if (state.TargetEnemyIndex is { } ti && ally.AttackSingle.Sum > 0)
    DealDamage(ally, state.Enemies[ti], ally.AttackSingle.RawTotal, scopeKind: "single")

  // 2. Random 攻撃
  if (ally.AttackRandom.Sum > 0)
    var randomTarget = rng.Choose(state.Enemies)  // 死亡敵含む（親 spec §4-4 仕様）
    DealDamage(ally, randomTarget, ally.AttackRandom.RawTotal, scopeKind: "random")

  // 3. All 攻撃
  if (ally.AttackAll.Sum > 0)
    foreach enemy in state.Enemies
      DealDamage(ally, enemy, ally.AttackAll.RawTotal, scopeKind: "all")

DealDamage(attacker, target, totalAttack, scopeKind):
  preBlock = target.Block.RawTotal
  damage   = max(0, totalAttack - preBlock)
  target.Block = target.Block.Consume(totalAttack)
  target.CurrentHp -= damage
  emit AttackFire { Caster=attacker, Note=scopeKind }
  emit DealDamage { Caster=attacker, Target=target, Amount=damage }
  if (was alive && now dead) emit ActorDeath { Target=target }
```

> 補正（力 / 脱力 / 脆弱）は 10.2.B で `DealDamage` ヘルパー内に統合する。
> `DealDamage` は `Engine` 内 internal helper として `PlayerAttackingResolver` / `EnemyAttackingResolver` から共有される。

### 4-3. `EnemyAttackingResolver`

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class EnemyAttackingResolver
{
    /// <summary>
    /// EnemyAttacking フェーズ実行。各生存敵の MoveDefinition.Effects を
    /// per-effect 即時発射し、NextMoveId へ遷移する。
    /// 親 spec §5-2-1 参照（敵 attack は per-effect 即時発射）。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState state, IRng rng, DataCatalog catalog);
}
```

**処理フロー**:

```
foreach enemy in state.Enemies.OrderBy(SlotIndex):
  if (!enemy.IsAlive) continue;
  var enemyDef = catalog.Enemies[enemy.DefinitionId];
  var move = enemyDef.Moves.First(m => m.Id == enemy.CurrentMoveId);

  foreach effect in move.Effects:
    // 10.2.A の対応 action
    if (effect.Action == "attack"):
      // 親 spec §5-2-1: 敵 attack は per-effect 即時発射
      // scope は JSON 段階で "all" 直書きされている (Phase 10.1.B で全 JSON 移行済み)
      foreach target in state.Allies.Where(a => a.IsAlive):  // 10.2.A は主人公だけ
        DealDamage(enemy, target, effect.Amount, scopeKind: "enemy_attack")
    elif (effect.Action == "block"):
      // 敵 move の block effect は scope=self を前提とする (10.2.A の制約)。
      // Phase 10.1.B 移行後の全敵 JSON で block effect は scope=self のみ。
      // scope=all/random の block は 10.2.D で EffectApplier 経由に統一する際に対応。
      enemy.Block = enemy.Block.Add(effect.Amount)
      emit GainBlock { Caster=enemy, Target=enemy, Amount=effect.Amount }
    else:
      // その他 action は 10.2.B 以降で対応 (no-op)
      continue

  // 主人公死亡で即 Outcome=Defeat 判定 (resolver の上位 = EndTurn で判定)

  // NextMoveId へ遷移
  enemy.CurrentMoveId = move.NextMoveId
```

`DealDamage` は `PlayerAttackingResolver` と同じヘルパー（`Engine` 内 internal helper として共有）。

### 4-4. `TurnStartProcessor`

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class TurnStartProcessor
{
    /// <summary>
    /// ターン開始処理。10.2.A は最小限（ターン+1, Energy 全回復, 5 ドロー）。
    /// 10.2.B で 毒・状態異常 tick / 召喚 Lifetime tick / OnTurnStart レリックが追加される。
    /// 親 spec §4-2 参照。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(
        BattleState state, IRng rng);
}
```

**処理フロー**（10.2.A）:

1. `state.Turn += 1`
2. `state.Energy = state.EnergyMax`
3. 5 枚ドロー: `Hand.Length < 10` の範囲で、`DrawPile` から 1 枚ずつ取り出し `Hand` 末尾へ。`DrawPile` が空になったら `DiscardPile` を `rng.Shuffle` して `DrawPile` に戻す。`DrawPile` も `DiscardPile` も空なら停止。
4. `TurnStart` イベント発火（`Note: $"turn={state.Turn}"`）

### 4-5. `TurnEndProcessor`

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class TurnEndProcessor
{
    /// <summary>
    /// ターン終了処理。10.2.A は最小限（Block リセット, AttackPool リセット, 手札全捨て）。
    /// 10.2.B で OnTurnEnd レリック / コンボリセット, 10.2.D で retainSelf 対応が追加される。
    /// 親 spec §4-6 参照。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state);
}
```

**処理フロー**（10.2.A）:

1. 全 ally / enemy の `Block = BlockPool.Empty`
2. 全 ally / enemy の `AttackSingle / AttackRandom / AttackAll = AttackPool.Empty`
3. 手札を全て `DiscardPile` 末尾へ（10.2.A は `retainSelf` 未対応のため全捨て）
4. イベントなし（10.2.A）

---

## 5. 自動対象切替（10.2.A 基本版）

死亡判定後（PlayerAttacking 終了時 / EnemyAttacking 終了時）に評価:

```
if (state.TargetEnemyIndex is { } ti && !state.Enemies[ti].IsAlive):
    state.TargetEnemyIndex = state.Enemies
        .Where(e => e.IsAlive)
        .OrderBy(e => e.SlotIndex)
        .Select(e => (int?)e.SlotIndex)
        .FirstOrDefault();   // 生存者なし → null（= Outcome=Victory 確定）

if (state.TargetAllyIndex is { } ai && !state.Allies[ai].IsAlive):
    state.TargetAllyIndex = state.Allies
        .Where(a => a.IsAlive)
        .OrderBy(a => a.SlotIndex)
        .Select(a => (int?)a.SlotIndex)
        .FirstOrDefault();   // 生存者なし → null（= Outcome=Defeat 確定）
```

10.2.A では「主人公以外の ally」が存在しないので、`TargetAllyIndex` の自動切替は実質意味を持たないが、コードは含めて 10.2.D （召喚追加）への布石とする。

`SetTarget` アクション（プレイヤー UI 操作からの対象切替）は 10.2.C で追加。10.2.A は初期値 (0, 0) と死亡時自動切替のみ。

---

## 6. テスト戦略

Phase 10.1.A〜C と同じ TDD 1 サイクル粒度（失敗テスト → 実装 → 緑 → commit）。

### 6-1. テストカテゴリ一覧

| ファイル | カバレッジ |
|---|---|
| `tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs` | §2-9 不変条件 8 項目（Allies/Enemies 範囲, hero=Allies[0], TargetIndex 生存性, Phase=Resolved⇔Outcome≠Pending, Energy 範囲, Turn≥1） |
| `tests/Core.Tests/Battle/State/AttackPoolTests.cs` | Empty / Add 加算 / AddCount 増分 / RawTotal |
| `tests/Core.Tests/Battle/State/BlockPoolTests.cs` | Empty / Add / RawTotal / Consume の残量 / Consume の AddCount リセット / Consume(damage > Sum) で 0 |
| `tests/Core.Tests/Battle/State/CombatActorTests.cs` | record 等価 / IsAlive (HP>0) / IsAlive (HP=0) / IsAlive (HP<0 防御的) |
| `tests/Core.Tests/Battle/State/BattleCardInstanceTests.cs` | record 等価 / CostOverride null と値 / IsUpgraded |
| `tests/Core.Tests/Battle/Events/BattleEventEmissionTests.cs` | Kind / Order 連番 / Caster/Target/Amount/CardId/Note の任意 null |
| `tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs` | hero 生成 (HP/MaxHp/SlotIndex=0) / 敵生成 (各 SlotIndex / CurrentMoveId=InitialMoveId) / Deck コピー & シャッフル / 5 ドロー / 初期対象 (0,0) / Turn=1 / Phase=PlayerInput / Outcome=Pending / Energy=3 |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs` | Energy 支払い / `attack` の AttackPool 加算 (Single/Random/All) / `block` の BlockPool 加算 / Hand→DiscardPile 移動 / cost 不足で例外 / cost null で例外 / PlayCard event 発火 |
| `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs` | フェーズ進行 (PlayerInput→PlayerAttacking→EnemyAttacking→PlayerInput) / 全敵死亡で Outcome=Victory + Phase=Resolved / 主人公死亡で Outcome=Defeat + Phase=Resolved / 次ターンで Energy 全回復 / 手札全捨て + 5 ドロー / Turn+1 |
| `tests/Core.Tests/Battle/Engine/BattleEngineFinalizeTests.cs` | Phase != Resolved で例外 / Victory: HP 戻し + Progress=InProgress + ActiveBattle=null / Defeat: HP 戻し + Progress=GameOver + ActiveBattle=null / バトル用 Deck はラン側に戻らない / BattleSummary フィールド |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverTests.cs` | Single → 対象敵 1 体に着弾 / Random → rng で選んだ敵に着弾 (死亡敵も候補) / All → 全敵に着弾 / Block で吸収 → 残ダメージ HP に / Block の AddCount は Consume でリセット / 0 ダメージなら DealDamage event なし / actor 死亡で ActorDeath 発火 |
| `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverTests.cs` | scope=all attack で 主人公に着弾 / per-effect 即時発射 (effects 配列に attack×2 → DealDamage 2 回) / block effect で 敵自身に Block / NextMoveId 遷移 / 死亡敵はスキップ |
| `tests/Core.Tests/Battle/Engine/TurnStartProcessorTests.cs` | Turn+1 / Energy 全回復 / 5 ドロー / DrawPile 空でシャッフル補充 / 両パイル空で停止 / 手札上限 10 で停止 |
| `tests/Core.Tests/Battle/Engine/TurnEndProcessorTests.cs` | 全 actor の Block リセット / 全 actor の AttackPool リセット / 手札全捨て (DiscardPile 末尾追加) |
| `tests/Core.Tests/Battle/Engine/TargetingAutoSwitchTests.cs` | 対象敵死亡 → 最小スロット生存者へ / 全敵死亡 → null / 主人公死亡 → null (10.2.A では Allies は主人公のみ) |
| `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs` | 同じ seed + 同じ EncounterId + 同じカードプレイ列で State / Events 完全一致 |
| `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` | `Hero(int hp = 70)` / `Goblin(int hp = 20, int attack = 5)` / `Strike(int amount = 6)` / `Defend(int amount = 5)` / `EncounterOf(params CombatActor[] enemies)` / `RunWithDeck(params CardInstance[] deck)` |

合計: 想定 17 テストファイル（含 Fixtures）、~70 テスト。

### 6-2. テスト方針

- **Fixtures はインライン factory のみ**（Q5 確定）。JSON 経由 fixture は 10.2.A では作らない
- **CardDefinition / EnemyDefinition / EncounterDefinition / DataCatalog はテスト内で組み立てる**。`BattleFixtures.cs` に `MinimalCatalog(...)` ヘルパーを置き、テスト用カード/敵だけを含む `DataCatalog` を返す
- **`IRng` は `FakeRng` を使用**（既存）。`Determinism` テストでは同じ seed で 2 回流して State / Events の完全一致を Assert
- **テスト内のカードプレイ・ターン進行は Helper 経由で短く書く**:
  ```csharp
  var (s1, _) = engine.PlayCard(state, handIndex: 0, targetEnemyIndex: 0, targetAllyIndex: 0, rng);
  var (s2, _) = engine.EndTurn(s1, rng);
  ```
- **既存テストとの整合**: `BattlePlaceholderTests` は無傷で残す（10.5 で削除）。`RunStateSerializerTests` は `BattleState` の record 構造変更で fixture を更新する必要あり（パイル 4 種が新規フィールド）

### 6-3. ビルド赤期間

`BattleState` 構造の全面刷新は破壊的変更。`RunState.ActiveBattle: BattleState?` の型は維持されるが、record のフィールドが激変するため、以下の連鎖更新が必要:

- `BattlePlaceholder.Start` / `BattlePlaceholder.Win` の中の `BattleState` 構築
- `BattlePlaceholder` 内の `EnemyInstance` 廃止 → `CombatActor` への置換
- `RunStateSerializer` の `BattleState` 部分の serialize / deserialize
- `RunStateSerializerTests` の fixture
- 既存 `BattlePlaceholderTests` の `BattleState` 検証

これらは 1 commit でまとめて書き換える（部分赤を回避）。テストを書く順序は:

1. 新 `BattleState` / `CombatActor` / `AttackPool` / `BlockPool` / `BattlePhase` / `BattleOutcome` / `ActorSide` / `BattleCardInstance` / `BattleEvent` / `BattleEventKind` の追加（既存コードに影響なし、テスト緑）
2. 旧 `BattleState` / `EnemyInstance` の削除 + `BattlePlaceholder` 書き換え + `RunStateSerializer` 更新 + 既存テスト fixture 更新（**1 commit でまとめて緑復帰**）
3. `BattleEngine.Start` 実装 + テスト
4. `EffectApplier` + `BattleEngine.PlayCard` 実装 + テスト
5. `PlayerAttackingResolver` 実装 + テスト
6. `EnemyAttackingResolver` 実装 + テスト
7. `TurnStartProcessor` / `TurnEndProcessor` 実装 + テスト
8. `BattleEngine.EndTurn` 実装 + テスト（resolver / processor を統合）
9. `TargetingAutoSwitch` 実装 + テスト
10. `BattleEngine.Finalize` 実装 + テスト
11. `BattleDeterminism` テスト

詳細な順序・粒度は plan に記載する。

---

## 7. 影響範囲

### 7-1. 新規ファイル

§1-3 のファイル構成参照。production: 14 ファイル、tests: 16 ファイル。

### 7-2. 既存ファイルの変更

| ファイル | 変更内容 |
|---|---|
| `src/Core/Battle/BattleState.cs` | **`BattlePlaceholderState.cs` にリネーム**。型名 `BattleState` → `BattlePlaceholderState`、`EnemyInstance` → `PlaceholderEnemyInstance`。フィールド構造は不変 |
| `src/Core/Battle/BattlePlaceholder.cs` | リネームに伴う型参照更新（`BattleState` → `BattlePlaceholderState`、`EnemyInstance` → `PlaceholderEnemyInstance`）のみ |
| `src/Core/Run/RunState.cs` | `BattleState? ActiveBattle` → `BattlePlaceholderState? ActiveBattle` に型変更 |
| `src/Core/Run/RunStateSerializer.cs` | **無変更**（`JsonSerializer.Serialize/Deserialize<RunState>` の generic 経路、JSON shape 不変のため） |
| `tests/Core.Tests/Battle/BattlePlaceholderTests.cs` | リネームに伴う型参照更新 |
| `tests/Core.Tests/Run/RunStateSerializerTests.cs` | リネームに伴う型参照更新（実 assert は `Assert.Null(loaded.ActiveBattle)` のみで shape 検証なし） |
| `src/Core/Battle/EncounterQueue.cs` | 無変更（既存 namespace のまま） |
| その他 production / tests で `Core.Battle.BattleState` を import している箇所 | リネームに合わせて `BattlePlaceholderState` に置換（grep で全件特定） |

### 7-3. ドキュメント

`docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に Phase 10.2.A 内で発生した設計判断を補記（§9 「親 spec への補記事項」参照）。

---

## 8. 親 spec への補記事項

Phase 10.2.A の最終タスクで `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に以下を反映:

1. **旧 `BattleState` → `BattlePlaceholderState` リネーム（10.2.A 限定の暫定）**:
   - 旧 `Core.Battle.BattleState` (placeholder) を `Core.Battle.BattlePlaceholderState` にリネーム
   - 新 `Core.Battle.State.BattleState` との型衝突を回避
   - `RunState.ActiveBattle` の型が一時的に `BattlePlaceholderState?` になる
   - 10.5 の最終 cleanup で `BattlePlaceholder.cs` / `BattlePlaceholderState.cs` を削除し、`ActiveBattle` を新 `BattleState?` に切替（同時に save schema v8 マイグレーション追加）

2. **§3-4 `CardInstance` 命名の明確化**:
   - 親 spec の `CardInstance` は実装上 `BattleCardInstance` として `src/Core/Battle/State/` に新設
   - `RunState.Deck` 用の `Cards.CardInstance` (Id+Upgraded のみ) とは別 record
   - `StartBattle` 時に変換される

3. **§3-3 `AttackPool` / `BlockPool` の Phase 10.2.A 暫定 API**:
   - 10.2.A では `RawTotal` プロパティのみ提供（遡及計算なし）
   - 10.2.B で `Display(strength, weak)` / `Display(dexterity)` を追加し、`RawTotal` は internal な debug プロパティとして残す（API 変更を最小化）

4. **§3-1 / §4-7 `BattleOutcome.Defeat` の追加**:
   - 旧 `BattlePlaceholder` の `BattleOutcome { Pending, Victory }` に `Defeat = 2` を追加
   - 親 spec §4-7 / §10-4 はソロモードでの Defeat を前提としていたが、enum への追加は 10.2.A で実施

5. **§5-2 `EffectApplier.Apply` の incremental 実装方針**:
   - 10.2.A は `attack` / `block` のみ対応、その他 action は no-op
   - 10.2.B〜E で対応 action を段階的に増やす
   - 各 phase で「未実装 action は no-op」の方針を維持し、データ層と実装層の段階的拡張を許容する

6. **§9-7 `BattleEvent` の Core 型分離**:
   - 親 spec §9-7 は `BattleEventDto` のみ定義していたが、Core 側に `BattleEvent` record + `BattleEventKind` enum を新設
   - Phase 10.3 で `BattleEvent` → `BattleEventDto` への変換層が追加される

これら 6 項目は Phase 10.2.A 内で発生した設計判断の追記。コードと spec の乖離を残さない。

---

## 9. スコープ外（再確認）

### 9-1. Phase 10.2.A では触らない

- 状態異常 6 種（strength/dexterity/vulnerable/weak/omnistrike/poison）→ Phase 10.2.B
- buff/debuff effect → Phase 10.2.B
- AttackPool / BlockPool の遡及計算（力/敏捷/脱力/脆弱）→ Phase 10.2.B
- omnistrike 合算発射 → Phase 10.2.B
- ターン開始時の毒 tick / 状態異常 tick → Phase 10.2.B
- コンボ機構（ComboCount / Wild / SuperWild / コスト軽減 / comboMin per-effect）→ Phase 10.2.C
- `SetTarget` アクション → Phase 10.2.C
- 残り effect 8 種（heal/draw/discard/upgrade/exhaustCard/exhaustSelf/retainSelf/gainEnergy）→ Phase 10.2.D
- 召喚 system（UnitDefinition runtime / SummonHeld / Lifetime / AssociatedSummonHeldIndex）→ Phase 10.2.D
- カード移動 5 段優先順位 → Phase 10.2.D
- PowerCards 配列 → Phase 10.2.D
- 味方攻撃の inside-out 順序（複数 ally）→ Phase 10.2.D
- レリック 4 新 Trigger 発火（OnBattleStart/OnTurnStart/OnTurnEnd/OnCardPlay/OnEnemyDeath）→ Phase 10.2.E
- レリック所持順発動 / Implemented:false スキップ → Phase 10.2.E
- ポーション戦闘内発動 / 戦闘外 BattleOnly スキップ → Phase 10.2.E
- `BattlePlaceholder.cs` / `BattlePlaceholderState.cs` の削除 + `RunState.ActiveBattle` を新 `BattleState?` に切替 + save schema v8 マイグレーション → Phase 10.5
- `NodeEffectResolver` から新 `BattleEngine.Start` への wire-up → Phase 10.3 (Server)
- `BattleHub` / `BattleStateDto` / `BattleEventDto` → Phase 10.3
- `BattleScreen.tsx` / `battle-v10.html` ポート → Phase 10.4
- 本番 Unit JSON データの追加 → Phase 11+ のカードデザイン拡充

### 9-2. Phase 10.2.A 完了後の状態

- データモデルが新形式で整備済み（`BattleState` / `CombatActor` / `AttackPool` / `BlockPool` / `BattleCardInstance` / `BattlePhase` / `BattleOutcome` (Defeat 追加) / `ActorSide` / `BattleEvent` / `BattleEventKind`）
- `BattleEngine` 4 公開 API（`Start` / `PlayCard` / `EndTurn` / `Finalize`）が `attack` / `block` の 2 effect で動作
- xUnit で Victory / Defeat 両経路の 1 戦闘が完走
- 既存ゲームフロー（`BattlePlaceholder` 経由）は無傷
- 親 spec が新方針に合わせて補記済み
- `phase10-2A-complete` タグ push 済み

---

## 参照

- 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン spec: [`2026-04-26-phase10-1C-potion-relic-extension-design.md`](2026-04-26-phase10-1C-potion-relic-extension-design.md)
- 直前マイルストーン plan: [`../plans/2026-04-26-phase10-1C-potion-relic-extension.md`](../plans/2026-04-26-phase10-1C-potion-relic-extension.md)
- ロードマップ: [`../plans/2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md)
