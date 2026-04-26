# Phase 10 — 本格バトルシステム 設計

> 作成日: 2026-04-25
> 対象フェーズ: Phase 10（Phase 5 の `BattlePlaceholder` を本物のバトルロジックに差し替える）
> 関連ロードマップ: [`2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md) Phase 10 節
> 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html` (canonical UI モック)
> カード見た目 canonical: `archives-cards-v12.html`（[memory: project_card_visual_canonical](../../../C%3A/Users/Metaverse/.claude/projects/c--Users-Metaverse-projects-roguelike-cardgame/memory/project_card_visual_canonical.md)）

## ゴール

Phase 5 の暫定実装（敵マスに入ったら即勝利）を、**カードプレイ・召喚・コンボ・状態異常・対象指定** を含む本格バトルに置き換える。バトル外システム（マップ進行・報酬・休憩・商人・図鑑）は既存実装を維持し、戦闘部分だけを差し替える。

UI は `battle-v10.html` を視覚リファレンスとして手動で React 化する（`aidesigner` などのツールは使わない）。Core ロジックは Server / Client / ASP.NET Core / ファイル I/O から完全独立を維持し、Udon# 移植可能性を保つ。

## 用語集

| 用語 | 定義 |
|---|---|
| アタック値 | カードプレイで主人公頭上に蓄積される予約値。単体／ランダム／全体の 3 系統で別カウンタ。ターン終了時に発射 |
| ダメージ値 | ターン終了時の発射処理で算出される最終値（`(アタック値合算 − 対象 Block) × 補正` の結果） |
| 元コスト | カード定義上の本来のコスト（軽減前）。強化済みなら `UpgradedCost ?? Cost` |
| コンボ条件 | 「直前のカードの元コスト + 1 のカードを次に手打ちプレイ」が成立で継続 |
| 対象 | 敵側 1 体 + 味方側 1 体が常に強調表示。クリックで切替可、生存者の最内側に自動切替 |
| caster | 効果の発動主体。カード使用なら主人公、敵 Move なら敵自身、召喚 Move なら召喚自身、レリックなら主人公 |
| effect プリミティブ | カード／敵 Move／召喚 Move／レリック／ポーション すべてで共通の効果単位 |

---

## 1. アーキテクチャ概要

### 1-1. Core（純粋ロジック）

- 配置: `src/Core/Battle/` 配下を新設・刷新
- 既存 `BattlePlaceholder.cs` は Phase 10 完了時に **削除**
- バトルは「`BattleState` + `BattleAction` → `BattleState` + `IReadOnlyList<BattleEvent>`」の純粋関数として実装
- 乱数は `IRng` を注入（テストでは `FakeRng`、本番では `SystemRng`）
- 既存 `RunState` への影響は「戦闘終了時の反映」だけ。**バトル中の RunState 変更は禁止**（ラン側操作系カードは Phase 10 スコープ外）

### 1-2. Server（薄いアダプタ）

- 新設: `src/Server/Hubs/BattleHub.cs`（既存 `RunHub` と分離するか同居させるかは実装時判断）
- Client から「カードプレイ」「ターン終了」「ポーション使用」「対象切替」「パイル内容要求」のメッセージを受け、Core のバトルロジックを呼び、結果（更新後 `BattleStateDto` + `BattleEventDto[]`）を Client に push
- セーブは「**戦闘開始時 / 戦闘終了時のみ**」。戦闘途中の細かい状態は揮発（途中離脱でセーブからは戦闘前に戻る）

### 1-3. Client（表示・入力）

- 新設: `src/Client/src/screens/BattleScreen.tsx`
- `battle-v10.html` の HTML/CSS/JS を React コンポーネントに**手動ポート**
- Server からの `BattleStateDto` を表示するだけ。状態遷移ロジックは持たない
- `archives-cards-v12.html` の `.ar__card` CSS を手札・モーダル両方で流用

### 1-4. 設計原則

- バトルロジックの単体テストは **Core プロジェクトで完結**（Server 不要）
- 戦闘内状態は揮発、戦闘終了で破棄。例外はラン側に反映されると明示された情報のみ
- effect プリミティブ・行動ルーティン・レリックトリガーは将来の拡張ポイント（コスト null 自動詠唱、ラン側操作、Status/Curse の具体動作、マルチモード戦闘不能、割り込み回復ポーション）を残す形で設計

---

## 2. データモデル

### 2-1. 効果プリミティブ `CardEffect`（カード／敵 Move／召喚 Move／レリック／ポーション 共通）

既存の派生 record 構造（`DamageEffect` / `GainBlockEffect` 等）を捨て、**単一 record + Action 文字列フィールド** に統一:

```csharp
public sealed record CardEffect(
    string Action,
    EffectScope Scope,
    EffectSide? Side,
    int Amount,
    string? Name,
    string? UnitId,
    int? ComboMin,
    string? Pile,
    bool BattleOnly = false);

public enum EffectScope { Self, Single, Random, All }
public enum EffectSide  { Enemy, Ally }
```

#### action 一覧（Phase 10 実装範囲）

| Action | 効果 | 備考 |
|---|---|---|
| `attack` | caster の AttackPool に加算（Scope で Single/Random/All に振分） | side は常に Enemy（正規化） |
| `block` | ターゲットの BlockPool に加算 | 敏捷バフで遡及計算 |
| `buff` | ターゲットの Statuses[Name] に Amount 加算（重ね掛け） | 各 buff の挙動は 2-5 参照 |
| `debuff` | ターゲットの Statuses[Name] に Amount 加算 | 同上 |
| `summon` | 空きスロットあれば UnitId のキャラを召喚＋カードを SummonHeld へ | 空きなしなら不発、他 effect は処理続行 |
| `heal` | ターゲットの HP を Amount 回復（最大 HP 上限） | 戦闘内・戦闘外共通 |
| `draw` | カードを Amount 枚ドロー（ハンド上限 10） | レリックで使用可 |
| `discard` | 手札から Amount 枚捨てる | Scope: Random / All（Single 可だが UI 操作要求） |
| `upgrade` | Pile 内のカードを Amount 枚強化 | バトル用デッキ内のみ、戦闘終了で破棄 |
| `exhaustCard` | Pile 内のカードを Amount 枚除外 | Pile = "hand"|"discard"|"draw" |
| `exhaustSelf` | カード自身を除外パイルへ | カード移動先の決定に影響 |
| `retainSelf` | カード自身を保留（ターン終了で捨札に行かず手札に残る） | カード移動先の決定に影響 |
| `gainEnergy` | エナジー +Amount | 上限超過の挙動は実装時判断（Phase 10 では超過可） |

### 2-2. 効果プリミティブの正規化（Effect Normalization）

JSON ロード時に `CardEffect.Normalize()` を適用（safety net）:

- `Scope == Self` のとき `Side = null`（Side が指定されていても破棄）
- `Action == "attack"` のとき `Side = Enemy` を強制（味方側 attack はバグ防止）
- `Action == "attack"` で `Scope == Self` は不正 → ロード時にバリデーションエラー（attack の Self は意味がない）

### 2-3. CardDefinition

```csharp
public sealed record CardDefinition(
    string Id, string Name, string? DisplayName,
    CardRarity Rarity, CardType CardType,
    int? Cost,
    int? UpgradedCost,                            // null/省略 = Cost と同じ
    IReadOnlyList<CardEffect> Effects,
    IReadOnlyList<CardEffect>? UpgradedEffects,   // null/省略 = Effects と同じ
    IReadOnlyList<string>? Keywords)              // 例: ["wild"], ["superwild"]
{
    public bool IsUpgradable => UpgradedCost is not null || UpgradedEffects is not null;
}

public enum CardType { Attack, Skill, Power, Unit, Status, Curse }
```

`CardType.Status` / `CardType.Curse` は CSS classes / プリミティブ拡張ポイントとして追加するが、**具体的データと動作は Phase 10 スコープ外**。

#### カード強化のパターン（4 種すべてが `UpgradedEffects` の完全置換で表現可能）

1. 数値変化: 同じ effect 配列で `amount` だけ違う
2. 効果追加: `UpgradedEffects` に新 effect を加えた配列
3. 効果削除: `UpgradedEffects` から該当 effect を取り除いた配列
4. 複合（変質）: scope 変更や効果差し替え（例: ランダム → 全体）

#### キーワード能力

- **`wild`**: 現在のコンボ条件を満たさなくても継続成立。**自身は軽減されない**（通常条件を満たしている場合は通常通り軽減）
- **`superwild`**: ワイルドの効果 + 次のカード 1 枚もコンボ条件無視で継続

### 2-4. CombatActorDefinition / UnitDefinition / EnemyDefinition

召喚キャラと敵は構造が同じため共通基底:

```csharp
public abstract record CombatActorDefinition(
    string Id, string Name, string ImageId,
    int Hp,                                          // 単一値（Phase 10.1.B 時点）
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);

public sealed record UnitDefinition(...) : CombatActorDefinition(...) {
    public int? LifetimeTurns { get; init; }      // 自動消滅オプション (null = 永続)
}

public sealed record EnemyDefinition(
    string Id, string Name, string ImageId,
    int Hp,
    EnemyPool Pool,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves)
    : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);
```

**既存敵 JSON は Phase 10 で全面書き換え**: 現状の `MoveDefinition` 数値フィールド散在型から、新しい `Effects: List<CardEffect>` 形式へ全変換。Phase 0 の暫定生成 JSON もすべて新形式へ。

### 2-5. MoveDefinition

```csharp
public sealed record MoveDefinition(
    string Id,
    MoveKind Kind,                              // intent UI 表示の判別用
    IReadOnlyList<CardEffect> Effects,
    string NextMoveId);

public enum MoveKind { Attack, Defend, Buff, Debuff, Heal, Multi, Unknown }
```

`Kind` は `battle-v10.html` の `.is-attack/.is-defend/.is-buff/.is-debuff/.is-heal/.is-unknown` へマッピング。実際の処理は `Effects` の中身で行う。

> 注: `battle-v10.html` には現状 `.is-debuff` CSS クラスは存在しない。Phase 10.4（UI 実装）で追加予定。

### 2-6. 状態異常 — Phase 10 基本セット

| Id | 表示名 | 種別 | 持続 | 効果 |
|---|---|---|---|---|
| `strength` | 力 | Buff | 永続 | アタック加算回数 × N を遡及で頭上に上乗せ |
| `dexterity` | 敏捷 | Buff | 永続 | ブロック加算回数 × N を遡及で上乗せ |
| `omnistrike` | 全体化 | Buff | N ターン | 単体／ランダム攻撃を全体扱いに切替（内部値は変更せず、表示と発射時のロジックだけ切替） |
| `vulnerable` | 脆弱 | Debuff | N ターン | 受けるダメージ ×1.5（発射時補正） |
| `weak` | 脱力 | Debuff | N ターン | 与えるアタック値 ×0.75 切捨（発射時補正） |
| `poison` | 毒 | Debuff | N | ターン開始時に Block 無視 N ダメージ、その後 N −1 |

実装は `src/Core/Battle/StatusDefinition.cs` に C# の static リスト（Phase 10 では JSON 化なし）。
**重ね掛けで amount 加算**（同名状態異常を再付与すると `existing.amount + new.amount`）。

### 2-7. RelicDefinition の拡張

```csharp
public sealed record RelicDefinition(
    string Id, string Name, string? DisplayName,
    RelicRarity Rarity,
    string Description,
    RelicTrigger Trigger,
    IReadOnlyList<CardEffect> Effects,
    bool Implemented = true);

public enum RelicTrigger {
    OnPickup           = 0,
    Passive            = 1,
    OnBattleStart      = 2,
    OnBattleEnd        = 3,                       // 既存（burning_blood 等で使用）
    OnMapTileResolved  = 4,                       // 既存（traveler_boots 等で使用）
    OnTurnStart        = 5,                       // 新規
    OnTurnEnd          = 6,                       // 新規
    OnCardPlay         = 7,                       // 新規
    OnEnemyDeath       = 8,                       // 新規
}
```

#### Phase 10 レリック設計指針

- effect の **scope は `Self / Random / All` のみ**（`Single` は禁止、プレイヤー選択を要するため）
- すべての effect は完全自動発動可能（プレイヤー操作を要求しない）
- 効果実装が複雑で Phase 10 で対応しないレリックは `Implemented: false` にし、description プレフィックスに `[未実装] ` を付ける（半角スペース込み）
- `Implemented: false` のレリックは取得・所持は通常通り可能、図鑑にも掲載されるが効果は発動しない
- **`NonBattleRelicEffects.cs` 内でも早期 return される**（OnPickup / OnMapTileResolved / Passive のいずれの戦闘外発火タイミングでも no-op）

### 2-8. PotionDefinition の更新

```csharp
public sealed record PotionDefinition(
    string Id, string Name, string? DisplayName,
    PotionRarity Rarity,
    IReadOnlyList<CardEffect> Effects)
{
    public bool IsUsableOutsideBattle => Effects.Any(e => !e.BattleOnly);
}
```

既存 Phase 0 のポーション JSON は新形式に書き換える。`BattleOnly: true` を効果単位で持ち、戦闘外発動時にスキップ。

---

## 3. バトル状態モデル

### 3-1. BattleState

```csharp
public sealed record BattleState(
    int Turn,
    BattlePhase Phase,
    BattleOutcome Outcome,

    // 両陣営（内側→外側）
    ImmutableArray<CombatActor> Allies,           // 主人公 + 召喚 (最大4)
    ImmutableArray<CombatActor> Enemies,          // 敵 (最大4)
    int? TargetAllyIndex,
    int? TargetEnemyIndex,

    // プレイヤーリソース
    int Energy,
    int EnergyMax,

    // パイル
    ImmutableArray<CardInstance> DrawPile,
    ImmutableArray<CardInstance> Hand,
    ImmutableArray<CardInstance> DiscardPile,
    ImmutableArray<CardInstance> ExhaustPile,
    ImmutableArray<CardInstance> SummonHeld,      // 召喚中の Summon カード（捨札待ち）
    ImmutableArray<CardInstance> PowerCards,      // Power カードの常駐エリア

    // コンボ
    int ComboCount,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,                   // SuperWild 由来

    // メタ
    string EncounterId);

public enum BattlePhase { PlayerInput, PlayerAttacking, EnemyAttacking, Resolved }
public enum BattleOutcome { Pending, Victory, Defeat }
```

> **Phase 10.2.A 補記**: 旧 `Core.Battle.BattleState`（placeholder, EncounterId+Enemies+Outcome）は
> `BattlePlaceholderState` にリネームし、新 `Core.Battle.State.BattleState` (本章定義) との型衝突を回避する。
> Phase 10.5 cleanup で `BattlePlaceholder` 一式を削除し、`RunState.ActiveBattle` を新 BattleState? に切替する
> （save schema v8 マイグレーション同時導入）。

> **Phase 10.2.C 補記**: 10.2.C で `ComboCount: int` / `LastPlayedOrigCost: int?` /
> `NextCardComboFreePass: bool` を追加した。配置は `EncounterId` の直前。
> `SummonHeld` / `PowerCards` は 10.2.D で `ExhaustPile` の後に挿入される予定。
> 初期値は `Start` および `TurnEndProcessor.Process` 後で `0 / null / false`。

### 3-2. CombatActor

```csharp
public sealed record CombatActor(
    string InstanceId,
    string DefinitionId,
    ActorSide Side,
    int SlotIndex,                                // 0..3 (内側→外側)

    int CurrentHp, int MaxHp,
    BlockPool Block,

    AttackPool AttackSingle,
    AttackPool AttackRandom,
    AttackPool AttackAll,

    ImmutableDictionary<string, int> Statuses,   // id → amount

    // AI 行動状態（敵・召喚）
    string? CurrentMoveId,
    int? RemainingLifetimeTurns,
    int? AssociatedSummonHeldIndex);

public enum ActorSide { Ally, Enemy }
```

> **Phase 10.2.B 補記**: `Statuses: ImmutableDictionary<string,int>` を 10.2.B で追加。
> `GetStatus(id)` 便宜プロパティで `Statuses.GetValueOrDefault(id, 0)` 相当を提供。
> 0 になった key は dict から削除する方針（不変条件: dict 内の値は常に > 0）。

### 3-3. AttackPool / BlockPool（遡及計算）

```csharp
public readonly record struct AttackPool(int Sum, int AddCount) {
    public static AttackPool Empty => new(0, 0);
    public AttackPool Add(int amount) => new(Sum + amount, AddCount + 1);
    /// <summary>力バフを遡及反映、脱力で 0.75 倍切捨</summary>
    public int Display(int strength, int weak)
        => weak > 0 ? (int)(((long)(Sum + AddCount * strength)) * 3 / 4)
                    : Sum + AddCount * strength;
}

public readonly record struct BlockPool(int Sum, int AddCount) {
    public static BlockPool Empty => new(0, 0);
    public BlockPool Add(int amount) => new(Sum + amount, AddCount + 1);
    public int Display(int dexterity) => Sum + AddCount * dexterity;

    /// <summary>ダメージを受けて Block を消費。残量を新 Sum、AddCount=0 にリセット</summary>
    public BlockPool Consume(int damage, int dexterity) {
        var current = Display(dexterity);
        var remaining = Math.Max(0, current - damage);
        return new(remaining, 0);
    }
}
```

> **Phase 10.2.A 補記**: 10.2.A では `RawTotal` プロパティのみ提供（遡及計算なし）。
> 10.2.B で `Display(strength, weak)` / `Display(dexterity)` / `Consume(incomingAttack, dexterity)` を追加し、
> `RawTotal` は internal な debug プロパティとして残す（API 変更を最小化）。

> **Phase 10.2.B 補記**: 10.2.B で `AttackPool.Display(strength, weak)` / `AttackPool.operator +` /
> `BlockPool.Display(dexterity)` / `BlockPool.Consume(incomingAttack, dexterity)` を追加。
> 10.2.A の `RawTotal` は internal 化（テスト・debug 用に温存）。
> 旧 `BlockPool.Consume(int)` は削除し、新シグネチャ `Consume(int, int)` のみとした。

### 3-4. CardInstance

```csharp
public sealed record CardInstance(
    string InstanceId,
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride);                          // 戦闘内一時上書き
```

> **Phase 10.2.A 補記**: 親 spec の `CardInstance` 型は実装上 `BattleCardInstance`（`src/Core/Battle/State/`）として
> 新設する。`RunState.Deck` 用の `Cards.CardInstance` (Id+Upgraded のみ) とは別 record。`StartBattle` 時に変換される。

### 3-5. 不変条件（テストで検証）

- `Allies.Length` ≤ 4、`Enemies.Length` ≤ 4
- `Allies[0].DefinitionId == "hero"`（主人公はスロット 0、最内側固定）
- `TargetAllyIndex` / `TargetEnemyIndex` は生存者を指す（または null）
- `Phase == Resolved` ⇔ `Outcome != Pending`
- `Energy ≤ EnergyMax`
- すべてのパイルカード合計 = 戦闘開始時のラン側デッキ枚数

---

## 4. ターン進行ロジック

### 4-1. フェーズ遷移

```
[BattleStart] → [PlayerInput] ─[EndTurn]→ [PlayerAttacking] → [EnemyAttacking] → [PlayerInput Turn+1]
                                                                                  ↑           ↓
                                                                                  └─ループ ───┘

任意のフェーズで:
  全敵死亡       → Outcome=Victory, Phase=Resolved
  主人公 HP ≤ 0  → Outcome=Defeat,  Phase=Resolved (ソロモード)
```

### 4-2. プレイヤーターン開始時 (PlayerInput 突入時)

実行順（ユーザー仕様確定）:

1. **ターン番号 +1**
2. **ターン開始時に発動するバフ/デバフの効果処理（毒含む）**
   - 全キャラに `Statuses["poison"] > 0` なら Block 無視で HP 減算
   - 毒ダメージで全敵死亡 / 主人公死亡が起こりうる → 即時 Outcome 判定
3. **ターン開始時に消費されるバフ/デバフのカウントダウン**
   - `vulnerable / weak / omnistrike / poison` を −1（0 で消滅）
   - `strength / dexterity` は永続なので tick なし
4. **召喚キャラの Lifetime カウント処理**
   - `RemainingLifetimeTurns -= 1`、0 になった召喚は消滅 → 関連 SummonHeld カードを捨札へ
5. **エナジー回復**: `Energy = EnergyMax`
6. **ドロー**: 5 枚（山札不足ならシャッフルで補充、ハンド上限 10 まで）
7. **OnTurnStart レリック発動**

> **Phase 10.2.B 補記**: 10.2.B で 毒 tick / status countdown / 毒死で Outcome 確定（Victory / Defeat）を実装。
> 順序は spec §4-2 通り（Turn+1 → 毒ダメージ → 死亡判定 → countdown → Energy → Draw → TurnStart event）。
> tick 後の死亡判定は `TargetingAutoSwitch.Apply` を流用。
> countdown では `ApplyStatus` event を発火しない（negative delta は意味論が違う）。RemoveStatus（0 になった瞬間）のみ発火。
> `OnTurnStart` レリック発動（step 7）/ 召喚 Lifetime tick（step 4）は後続 phase。

### 4-3. プレイヤーカードプレイフェーズ (PlayerInput)

入力アクション（純粋関数 `Apply(BattleState, BattleAction, IRng) → (BattleState, IReadOnlyList<BattleEvent>)`）:

- `PlayCard(handIndex, targetAllyIndex?, targetEnemyIndex?)`
- `UsePotion(potionIndex, targetAllyIndex?, targetEnemyIndex?)`
- `SetTarget(side, slotIndex)`
- `EndTurn()` → PlayerAttacking フェーズへ移行

カードプレイ時の細部（コンボ判定アルゴリズム）は 6 章参照。

### 4-4. 味方攻撃フェーズ (PlayerAttacking)

擬似コード（**内側 → 外側の順**で発射、SlotIndex 0→3）:

```
foreach ally in Allies.OrderBy(SlotIndex):
  if (!ally.IsAlive) continue;

  // omnistrike バフチェック
  bool omni = ally.Statuses.GetValueOrDefault("omnistrike", 0) > 0;

  if (omni):
    // 全 AttackPool 合算 → 全敵への 1 段階発射
    var combined = ally.AttackSingle + ally.AttackRandom + ally.AttackAll  // (Sum + Sum, AddCount + AddCount + AddCount)
    foreach enemy in Enemies:
      DealDamage(ally, enemy, combined, scopeForUI: All)
  else:
    // 1. 単体攻撃 → 対象敵に発射
    if (TargetEnemyIndex != null && ally.AttackSingle.Sum > 0):
      DealDamage(ally, Enemies[TargetEnemyIndex], ally.AttackSingle, Single)

    // 2. ランダム攻撃 → 発射時に乱数で 1 体選択（生存・死亡問わず）
    if (ally.AttackRandom.Sum > 0):
      randomTarget = rng.Choose(Enemies)        // 死亡敵含む
      DealDamage(ally, randomTarget, ally.AttackRandom, Random)

    // 3. 全体攻撃 → 全敵に発射
    if (ally.AttackAll.Sum > 0):
      foreach enemy in Enemies:
        DealDamage(ally, enemy, ally.AttackAll, All)
```

`DealDamage(attacker, target, pool, scopeForUI)` の擬似コード:

```
// 1. 自陣補正（攻撃側）
totalAttack = pool.Sum + attacker.GetStatus("strength") * pool.AddCount    // 力遡及
if (attacker.GetStatus("weak") > 0):
  totalAttack = floor(totalAttack * 0.75)                                  // 脱力

// 2. Block 消費とダメージ通り計算（同時に算出）
dex = target.GetStatus("dexterity")
preBlock = target.Block.Display(dex)
absorbed = min(totalAttack, preBlock)
damage = totalAttack - absorbed
target.Block = new BlockPool(preBlock - absorbed, AddCount: 0)             // 消費後は遡及性を失う

// 3. 受け側補正（脆弱）
if (target.GetStatus("vulnerable") > 0):
  damage = floor(damage * 1.5)

// 4. HP 減算とイベント発火
target.Hp -= damage
emit BattleEvent(scopeForUI, attacker, target, damage)
```

味方全攻撃終了後:
- HP ≤ 0 の敵を死亡扱い
- 全敵死亡なら Outcome=Victory, Resolved
- TargetEnemyIndex が死亡敵を指していたら、生存敵の最も内側に自動切替（7-4 参照）

> **Phase 10.2.B 補記**: 10.2.B で `DealDamageHelper.Apply(attacker, target, baseSum, addCount, scopeNote, orderBase)` シグネチャに更新。
> 攻撃側 strength × addCount / weak（×0.75 切捨）と受け側 vulnerable（×1.5 切捨）/ dexterity（Block 表示・消費）の補正を helper 内に統合。
> `AttackFire.Amount` は攻撃側補正後・Block 適用前、`DealDamage.Amount` は最終 HP 減算量。
> 切り捨ては全て integer 演算（`* 3 / 4`、`* 3 / 2`）で誤差なし。

### 4-5. 敵攻撃フェーズ (EnemyAttacking)

擬似コード:

```
foreach enemy in Enemies.Where(IsAlive):
  move = enemyDef.Moves[enemy.CurrentMoveId]
  foreach effect in move.Effects:
    ApplyEffect(enemy, effect)              // attack なら scope=all 強制（正規化レイヤー）
  enemy.CurrentMoveId = move.NextMoveId
```

敵 attack effect の `scope` は **JSON 段階で `"all"` を直書きする運用**（Phase 10.1.B 移行で全敵 JSON 適用済み）。ロード時の自動書換は行わない。

味方への発射ロジックは `DealDamage` の対称版（attacker / target を入れ替え）。
全味方死亡 / 主人公死亡で Outcome=Defeat, Resolved。
TargetAllyIndex が死亡味方を指していたら生存味方の最内側に切替。

### 4-6. ターン終了処理（攻撃フェーズ後 → 次の PlayerInput へ）

実行順:

1. **両陣営 Block リセット**（全キャラの `Block = BlockPool.Empty`）
2. **アタック値リセット**: 全キャラの `AttackSingle / Random / All = AttackPool.Empty`
3. **OnTurnEnd レリック発動**
4. **コンボリセット**: `ComboCount = 0, LastPlayedOrigCost = null`
   - `NextCardComboFreePass` は SuperWild の効果なのでターン跨ぎでリセット
5. **手札整理**: `retainSelf` 効果を持っていないカードを全て捨札へ（Phase 10 では retainSelf カードのみ手札に残る）
6. **次の PlayerInput 開始処理（4-2）へ**

> **状態異常カウントダウンはターン終了ではなくターン開始（4-2 step 3）で統合**。これによりターン中にバフを付けたキャラが、そのターンの効果終了時に 0 にならず、次のターンで作用する。

> **Phase 10.2.C 補記**: 10.2.C で `TurnEndProcessor.Process` がコンボ 3 フィールド
> （`ComboCount = 0` / `LastPlayedOrigCost = null` / `NextCardComboFreePass = false`）
> のリセットを実行。`OnTurnEnd` レリック発動（step 3）/ `retainSelf` 対応の手札整理（step 5）は
> 後続 sub-phase（10.2.E / 10.2.D）。

### 4-7. 戦闘終了

- 全敵死亡 → Outcome=Victory, Phase=Resolved
- 主人公 HP ≤ 0（ソロモード）→ Outcome=Defeat, Phase=Resolved
- 終了処理は 10 章「戦闘終了と RunState 反映」で詳述

> **Phase 10.2.A 補記**: 旧 `BattleOutcome { Pending, Victory }` は Phase 10.2.A で `Defeat = 2` を追加し、
> `Pending = 0, Victory = 1, Defeat = 2` の 3 値とする。Phase 5 placeholder では Defeat 経路がなかったが、
> ソロモード戦闘敗北を `RunProgress.GameOver` へ橋渡しするために必要。

---

## 5. 効果プリミティブの適用

### 5-1. ApplyEffect 関数

```csharp
(BattleState, IReadOnlyList<BattleEvent>) ApplyEffect(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng);
```

処理フロー:

1. **ComboMin チェック**: `effect.ComboMin != null && state.ComboCount < effect.ComboMin` ならスキップ
2. **BattleOnly チェック**（戦闘外発動時のみ）: `effect.BattleOnly == true` ならスキップ
3. **ターゲット解決**:
   - `Self` → `caster` 1 体
   - `Single, Side=対立側` → `Target{Enemy|Ally}Index` で指定中
   - `Single, Side=自陣側` → 同上（味方の Single ターゲット）
   - `Random, Side=...` → `rng.Choose(該当 side の生存キャラ)`
   - `All, Side=...` → 該当 side の全生存キャラ
4. **Action ごとの処理**（各ターゲットに対して）:
   - `attack`: caster 自身の AttackPool に加算（**ターゲットへの直接ダメージではない**、Scope で振分）
   - `block`: ターゲットの BlockPool に加算
   - `buff` / `debuff`: ターゲットの Statuses[Name] に Amount 加算
   - `summon`: 空きスロットあり → CombatActor 生成 + SummonHeld 登録、空きなし → 不発
   - `heal`: ターゲットの HP を Amount 回復（最大 HP 上限）
   - `draw`: 主人公がカードを Amount 枚ドロー
   - `discard`: ターゲットの手札（=主人公の手札）から Amount 枚捨てる
   - `upgrade`: Pile 内のカードを Amount 枚強化
   - `exhaustCard`: Pile 内のカードを Amount 枚除外
   - `exhaustSelf` / `retainSelf`: カード移動先の決定にフラグを立てる（5-7 参照）
   - `gainEnergy`: `state.Energy += Amount`

> **Phase 10.2.A 補記**: `EffectApplier.Apply` は incremental 実装方針を採用。
> 10.2.A は `attack` / `block` のみ対応、その他 action は no-op + イベントなし。
> 10.2.B〜E で対応 action を段階的に増やす（buff/debuff → heal/draw/discard/upgrade/exhaust*/retainSelf/gainEnergy → summon → relic/potion トリガー）。
> 各 phase で「未実装 action は no-op」の方針を維持し、データ層と実装層の段階的拡張を許容する。

> **Phase 10.2.B 補記**: 10.2.B で `buff` / `debuff` action に対応（Self / Single / Random / All の 4 scope 全対応）。
> `ReplaceActor` は memory feedback の InstanceId 検索ルールに準拠（10.2.A の `IndexOf` ベース latent bug を根治）。
> `Self` 以外の scope で `effect.Side == null` のときは ApplyEffect 内で例外を投げる。
> `effect.Name` が空の buff/debuff も例外（status id 必須）。
> 重ね掛けは existing.amount + new.amount。

> **Phase 10.2.C 補記**: 10.2.C で `effect.ComboMin` per-effect filter は
> **`BattleEngine.PlayCard` 側**で評価（`EffectApplier.Apply` のシグネチャは不変）。
> カードプレイ経路以外（敵 Move / レリック / ポーション）では comboMin が常に「null と同等」に振る舞う。

### 5-2. Attack effect の蓄積仕様

`Action == "attack"` は**プレイ瞬間に発射されない**。caster 自身の AttackPool に加算されるだけ:

```
effect.Scope == Single → caster.AttackSingle = caster.AttackSingle.Add(effect.Amount)
effect.Scope == Random → caster.AttackRandom = caster.AttackRandom.Add(effect.Amount)
effect.Scope == All    → caster.AttackAll    = caster.AttackAll.Add(effect.Amount)
effect.Scope == Self   → 不正（attack の self は意味がない）。正規化レイヤーで弾く
```

ターン中に力バフが付いた瞬間 → 頭上の表示値が `Sum + AddCount × strength` に切り替わる（遡及反映）。バフが消滅すれば自動的に元の Sum 表示に戻る。

### 5-2-1. 敵 attack と プレイヤー attack の発射タイミング非対称

- **プレイヤー attack**: AttackPool に蓄積され、ターン終了時 (`PlayerAttacking` フェーズ) に
  「Single → Random → All」の順で 1 回ずつ発射される（同 scope 内は 1 まとめ）。
- **敵 attack**: `EnemyAttacking` フェーズで move の各 attack effect が **per-effect 即時発射**。
  `Effects: [{attack 11}, {attack 11}]`（hits=2 相当）は 2 回別々のダメージ判定として処理される。
  AttackPool の利用は内部実装の詳細だが、ターン終了で都度ドレインされる扱い。

この非対称により、プレイヤーは「複数 attack カードをコンボで蓄積→単発の大きな一撃」、
敵は「1 ターン中に複数の独立した連撃」というゲーム体験になる。

### 5-3. Block effect の遡及計算

- `Action == "block"`: ターゲットの BlockPool に加算
- 表示値: `BlockPool.Sum + BlockPool.AddCount × dexterity`
- ダメージ消費: `Consume(damage, dex)` で残量を新 Sum、AddCount=0 にリセット（消費後は遡及性を失う）

### 5-4. Summon カードの捨札遅延

通常カードはプレイ後すぐ捨札へ。Summon カードのみ:
- 召喚成功 → カードを `SummonHeld` に移動、`AssociatedSummonHeldIndex` で召喚キャラと紐付け
- 召喚キャラが消滅（HP=0 / Lifetime=0 / 戦闘終了）→ 結びついた Summon カードを SummonHeld から **捨札へ**移動
- 空きスロットなしで召喚不発 → 通常カード扱いで即捨札

### 5-5. ComboMin による effect 単位フィルタ

`effect.ComboMin` はカード全体ではなく **effect 単位**で評価。1 枚のカードで「素の効果 + コンボ条件付き効果」を組み合わせ可能:

```json
"effects": [
  {"action":"attack","scope":"single","side":"enemy","amount":5},
  {"action":"attack","scope":"single","side":"enemy","amount":5,"comboMin":2}
]
```
- Combo 1 → 5 のみ
- Combo 2 以上 → 5+5 = 10

### 5-6. BattleOnly の戦闘外発動時挙動

ポーションが戦闘外で使われる場合:
- `effect.BattleOnly == true` → スキップ
- `effect.BattleOnly == false` → 戦闘外用の解決ロジックで適用（caster=主人公、対象スロットなし、scope は self または ally で主人公を解決）

### 5-7. カードのプレイ後移動先（優先順位）

カードプレイ完了後、カードの行き先を以下の優先順位で決定:

1. effect に `exhaustSelf` を含む → 除外パイルへ
2. CardType が `Power` → `PowerCards` 配列に常駐（手札・捨札・山札・除外いずれにも入らない）
3. CardType が `Unit`（Summon）かつ召喚成功 → SummonHeld へ
4. effect に `retainSelf` を含む → 手札に残る
5. それ以外 → 捨札へ

---

## 6. コンボ機構

### 6-1. 状態フィールド

```csharp
int  ComboCount;                  // 現在のコンボ数 (0..N)
int? LastPlayedOrigCost;          // 直前に手打ちプレイしたカードの元コスト
bool NextCardComboFreePass;       // SuperWild 由来：次のカード 1 枚はコンボ条件 bypass
```

「元コスト」 = 強化済みなら `UpgradedCost ?? Cost`、無強化なら `Cost`。コスト軽減**前**の値。

### 6-2. ターン開始時のリセット

- `ComboCount = 0`
- `LastPlayedOrigCost = null`
- `NextCardComboFreePass = false`

### 6-3. カードプレイ時のコンボ判定アルゴリズム

```
PlayCard(card):
    actualCost = card.OriginalCost  // 強化込み

    // 1. コンボ継続判定
    matchesNormalCondition = (LastPlayedOrigCost != null
                              && actualCost == LastPlayedOrigCost + 1)

    isComboContinuing = false
    if (NextCardComboFreePass):
        isComboContinuing = true
        NextCardComboFreePass = false
    elif (matchesNormalCondition):
        isComboContinuing = true
    elif (card.Keywords.Contains("wild")):
        isComboContinuing = true   // Wild：継続成立、軽減は別判定

    // 2. コスト軽減判定（軽減は通常条件成立時のみ）
    isReduced = matchesNormalCondition
    payCost = max(0, actualCost - (isReduced ? 1 : 0))

    // 3. エナジーチェック
    if (Energy < payCost): return PlayResult.NotEnoughEnergy

    Energy -= payCost

    // 4. ComboCount 更新
    ComboCount = isComboContinuing ? ComboCount + 1 : 1

    // 5. LastPlayedOrigCost 更新（手打ちプレイのみ）
    LastPlayedOrigCost = actualCost

    // 6. SuperWild の自動詠唱フラグ予約
    if (card.Keywords.Contains("superwild")):
        NextCardComboFreePass = true

    // 7. effects 適用（comboMin チェック付き）
    foreach effect in card.Effects:
        if (effect.ComboMin != null && ComboCount < effect.ComboMin):
            continue
        ApplyEffect(state, caster, effect, rng)

    // 8. カード移動（5-7 の優先順位）
    MoveCardAfterPlay(card)
```

### 6-4. 動作例

| 例 | 状況 | 結果 |
|---|---|---|
| 通常コンボ階段 | 直前 LastOrigCost=1, Combo=1 → 元コスト 2 のカード | 通常条件成立、軽減あり、payCost=1, Combo=2, LastOrigCost=2 |
| Wild（条件不一致） | 直前 LastOrigCost=1, Combo=1 → Wild（元コスト 5） | 通常条件不一致だが Wild で継続、軽減なし、payCost=5, Combo=2, LastOrigCost=5 |
| Wild（条件一致もする） | 直前 LastOrigCost=1, Combo=1 → Wild（元コスト 2） | 通常条件成立、軽減あり（Wild 関係なく）、payCost=1, Combo=2, LastOrigCost=2 |
| SuperWild + 次のカード | 直前 LastOrigCost=1, Combo=1 → SuperWild（元コスト 7） → 元コスト 3 のカード | SuperWild: payCost=7, Combo=2, LastOrigCost=7, FreePass=true。次: 無条件継続、軽減なし、payCost=3, Combo=3, LastOrigCost=3, FreePass=false |
| Wild がリセット後 1 枚目 | LastOrigCost=null, Combo=0 → Wild（元コスト 5） | LastOrigCost が null なので継続相手なし、新規スタート扱い、Combo=1, LastOrigCost=5 |
| SuperWild → 0 コスト | LastOrigCost=4, Combo=2 → SuperWild（元コスト 6） → 元コスト 0 | SuperWild: payCost=6, Combo=3, LastOrigCost=6, FreePass=true。次: payCost=0, Combo=4, **LastOrigCost=0**, FreePass=false。次のコンボ継続要求は元コスト 1 |

### 6-5. effect の comboMin との相互作用

`comboMin` は **ComboCount 更新後**の値で判定。これによりカード自身のプレイがコンボ N に到達した瞬間に「`comboMin: N` の効果」が発動する。

### 6-6. 自動詠唱カード／コスト null カードのコンボ扱い

- コスト null カード: プレイ不可。コンボ判定対象外
- 何らかの効果で自動詠唱されるカード（Phase 10 ではスコープ外）: 発動してもコンボ更新しない

「手打ちしない限りはコンボが途切れない」性質が保証される。Phase 10 では自動詠唱の仕組み自体が未実装。

### 6-7. Phase 10.2.C 実装ノート

- `BattleEngine.PlayCard` 内に実装。`EffectApplier.Apply` のシグネチャは変更しない
- `actualCost` 算定では `BattleCardInstance.CostOverride` を **無視**（コスト軽減前の元コストで階段判定）。
  `payCost` 算定では CostOverride を反映、最後にコンボ軽減 -1 と下限 0 で clamp
- SuperWild の `NextCardComboFreePass` 規則は `newFreePass = isSuperWild` の 1 行で表現:
  - 自身が SuperWild → 次カード向け予約 true
  - 自身が SuperWild 以外 → FreePass を消費して false（または false のまま）
- Energy 不足の例外チェックはコンボ判定後（軽減で `payCost = 0` になった場合 Energy 0 でもプレイ可能）
- `Wild` と `SuperWild` を同時に持つカードは想定外だが、両方立っていた場合は SuperWild の挙動が支配的
  （FreePass フラグも立つ）

---

## 7. 対象指定システム

### 7-1. 対象状態

```csharp
int? TargetAllyIndex;             // 対象指定中の味方スロット index（生存者）
int? TargetEnemyIndex;            // 対象指定中の敵スロット index（生存者）
```

### 7-2. 戦闘開幕の初期対象

- `TargetAllyIndex = 0`（主人公 = `Allies[0]`、画面最内側）
- `TargetEnemyIndex = 0`（敵の最内側、`Enemies[0]`）

`SlotIndex = 0` は常に画面最内側、外側へ 1, 2, 3。

### 7-3. 対象切替アクション

```csharp
SetTarget(ActorSide side, int slotIndex)
```

- バリデーション: 指定スロットが生存者であること
- クライアントは `battle-v10.html` の `bs__slot[data-side][data-index]` クリックで発火

> **Phase 10.2.C 補記**: 10.2.C で `BattleEngine.SetTarget(state, side, slotIndex) → BattleState`
> を**第 5 の public static API** として追加。Phase=PlayerInput 限定（他 Phase で `InvalidOperationException`）、
> 範囲外 / 死亡スロット指定で例外。戻り値は `BattleState` 単体、`BattleEvent` 発火なし。
> `PlayCard` 引数経由の暗黙対象切替（10.2.A 既存）も維持。両者の生存・範囲チェック整合は
> 10.2.D 以降で `UsePotion` 追加時に再考。

### 7-4. 対象死亡時の自動切替

死亡判定後（味方攻撃終了後・敵攻撃終了後・毒ダメージ後）に評価:

```
if (TargetEnemyIndex != null && Enemies[TargetEnemyIndex].IsDead):
    TargetEnemyIndex = Enemies の生存者の SlotIndex 最小（最内側）
                       生存者なし → null （= Outcome=Victory 確定）

if (TargetAllyIndex != null && Allies[TargetAllyIndex].IsDead):
    TargetAllyIndex = Allies の生存者の SlotIndex 最小（=主人公）
                       生存者なし → null （= Outcome=Defeat 確定 in ソロモード）
```

これで「対象不在状態」は戦闘終了直前を除いて発生しない。

### 7-5. 対象指定が不要な scope

- `Self`: caster 自身、対象 index 不要
- `Random`: 発射時に乱数選択
- `All`: 生存者全員

`Single` のみ `Target{Enemy|Ally}Index` を参照。

### 7-6. クライアント UI の挙動

- 対象は `is-selected` クラスで強調表示（味方は黄色発光、敵は赤発光）
- 切替はスロットクリック（intent / status / hp 子要素クリックは対象切替を発火しない）
- カード発動はドラッグ&ドロップ。ドラッグ中は対象切替無効化（プレイ対象は「ドラッグ開始時点」で固定）
- カード発動完了後、対象は維持される

---

## 8. ポーション・レリックの戦闘内発動

### 8-1. ポーション

- `PotionDefinition.Effects` を新形式（`CardEffect` 配列）に統一
- 戦闘内: `UsePotion(potionIndex, targetAllyIndex?, targetEnemyIndex?)` で発動、全 effect 適用
- 戦闘外: マップ画面に**新規 UI 実装**、`BattleOnly: false` の effect のみ適用、全 effect が `BattleOnly: true` のポーションは戦闘外で使用不可（UI でグレーアウト）
- 効果適用は `ApplyEffect`（カードと共通）。caster=主人公。コスト・コンボ更新・捨札移動は発生しない
- 攻撃 effect を含むポーションも「主人公の AttackPool に加算」される（ターン終了時に発射）

### 8-2. レリック

#### 8-2-1. Trigger ごとの発動位置

- `OnPickup`: 取得時（既存）
- `Passive`: 常時参照（既存）
- `OnBattleStart`: 戦闘開幕の初期化処理の最後（手札ドロー後、対象初期化後）
- `OnTurnStart`: ターン開始処理の **step 7**（毒・状態異常 tick → 召喚 Lifetime → エナジー → ドロー → **ここ**）
- `OnTurnEnd`: ターン終了処理の **step 3**（Block リセット → アタック値リセット → **ここ** → コンボリセット → 手札整理）
- `OnCardPlay`: PlayCard の effect 適用**後**（カード移動の前）
- `OnEnemyDeath`: 死亡判定で `IsAlive: true → false` に変わった瞬間、その敵 1 体ごとに発動

#### 8-2-2. 発動主体

- caster は主人公 (`Allies[0]`) 固定
- 召喚キャラがレリックを発動することはない

#### 8-2-3. 複数レリックの発動順

- 同じ Trigger に複数レリックがある場合、**所持順（取得順）で発動**
- `RunState.Relics` の配列順を尊重

#### 8-2-4. 設計指針（Phase 10）

- effect の scope は `Self / Random / All` のみ（`Single` 禁止）
- すべての effect は完全自動発動可能
- 効果実装が複雑で対応しないレリックは `Implemented: false` で切り分け

---

## 9. クライアント DTO

### 9-1. ルート

```csharp
public sealed record BattleStateDto(
    int Turn, string Phase, string? Outcome,
    HudDto Hud, StageDto Stage,
    IReadOnlyList<BattleEventDto>? PendingEvents);
```

### 9-2. HUD

```csharp
public sealed record HudDto(
    int Hp, int MaxHp, int Gold,
    IReadOnlyList<RelicDto> Relics,
    IReadOnlyList<PotionDto?> Potions,
    int DeckCount, string ActLabel, int FloorNumber, int FloorMax);

public sealed record RelicDto(
    string DefinitionId, string Name, string? DisplayName,
    string RarityCode, string Icon, string Description, bool Implemented);

public sealed record PotionDto(
    string DefinitionId, string Name,
    string RarityCode, string Icon, string Description,
    bool IsUsableInBattle, bool IsUsableOutsideBattle);
```

### 9-3. Stage

```csharp
public sealed record StageDto(
    IReadOnlyList<CombatActorDto> Allies,
    IReadOnlyList<CombatActorDto> Enemies,
    int? TargetAllyIndex, int? TargetEnemyIndex,
    int Energy, int EnergyMax,
    int ComboCount, int? ComboNextRequiredOrigCost, bool NextCardComboFreePass,
    int DrawPileCount, int DiscardPileCount, int ExhaustPileCount,
    IReadOnlyList<HandCardDto> Hand);
```

### 9-4. CombatActor

```csharp
public sealed record CombatActorDto(
    int SlotIndex, string DefinitionId,
    string Name, string Description,
    string ImageId, string SpriteCategory,
    int Hp, int MaxHp, int HpLevelClass,
    int Block,
    AttackDisplayDto AttackSingle, AttackDisplayDto AttackRandom, AttackDisplayDto AttackAll,
    bool OmnistrikeActive,
    IntentDto? Intent,
    IReadOnlyList<StatusDto> Statuses,
    bool IsAlive);

public sealed record AttackDisplayDto(int DisplayValue, int RawSum, int AddCount);
public sealed record IntentDto(string Kind, string Icon, int? Num, string Name, string Description);
public sealed record StatusDto(string Id, string DisplayName, string Icon, int Amount, string CssClass, string Description);
```

### 9-5. 手札

```csharp
public sealed record HandCardDto(
    int HandIndex, string CardDefinitionId,
    string Name, string Type, string RarityCode,
    int OrigCost, int DisplayCost, bool IsReduced, bool IsPlayable, bool IsUpgraded,
    string Description, string Icon,
    IReadOnlyList<string>? Keywords);
```

### 9-6. 表示計算ロジック（Server 側）

| 表示値 | 計算式 |
|---|---|
| `AttackDisplayDto.DisplayValue` | `RawSum + AddCount × Strength`、その後 weak で `×0.75` 切捨 |
| `CombatActorDto.Block` | `BlockPool.Sum + BlockPool.AddCount × Dexterity` |
| `OmnistrikeActive` | `Statuses["omnistrike"] > 0` |
| `StageDto.ComboNextRequiredOrigCost` | `LastPlayedOrigCost + 1`、リセット中は null |
| `HandCardDto.DisplayCost` | コンボ条件成立で軽減 −1（下限 0） |
| `HandCardDto.IsPlayable` | エナジー足りる + ターゲット要件満たす |

### 9-7. BattleEvent

```csharp
public sealed record BattleEventDto(
    string Kind,                    // "PlayCard" | "AttackFire" | "DealDamage" | "GainBlock" | "ApplyStatus"
                                    // | "RemoveStatus" | "Summon" | "ActorDeath" | "Draw" | "Discard"
                                    // | "Exhaust" | "Upgrade" | "RelicTrigger" | ...
    int Order,
    string? CasterInstanceId, string? TargetInstanceId,
    int? Amount,
    string? CardId, string? StatusId,
    string? Note);
```

`PlayerAttacking` フェーズで複数の BattleEvent が発火される。クライアントはキューに積み、順次再生して最終 `BattleStateDto` を反映。

> **Phase 10.2.A 補記**: 親 spec §9-7 は `BattleEventDto` のみ定義していたが、Core 側に
> `BattleEvent` record + `BattleEventKind` enum を `src/Core/Battle/Events/` に新設する。
> Phase 10.3 で `BattleEvent` → `BattleEventDto` への変換層が追加される。
> 10.2.A の `BattleEventKind` は 9 種（BattleStart / TurnStart / PlayCard / AttackFire / DealDamage /
> GainBlock / ActorDeath / EndTurn / BattleEnd）、後続 phase で追加していく。

> **Phase 10.2.B 補記**: 10.2.B で `ApplyStatus = 9` / `RemoveStatus = 10` / `PoisonTick = 11` を追加（計 12 値）。
> ペイロード慣例:
> - `ApplyStatus`: Caster=付与主体, Target=対象, Amount=delta, Note=status_id
> - `RemoveStatus`: Caster=null, Target=対象, Amount=null, Note=status_id
> - `PoisonTick`: Caster=null, Target=対象, Amount=ダメージ量, Note="poison"

### 9-8. 通信プロトコル

| Server → Client | 内容 |
|---|---|
| `BattleStateUpdated(BattleStateDto)` | 状態変更時に push |
| `BattleEventsRaised(IReadOnlyList<BattleEventDto>)` | アニメーションキュー push |

| Client → Server | 内容 |
|---|---|
| `PlayCard(handIndex, targetAllyIndex?, targetEnemyIndex?)` | |
| `UsePotion(potionIndex, targetAllyIndex?, targetEnemyIndex?)` | |
| `SetTarget(side, slotIndex)` | |
| `EndTurn()` | |
| `RequestPileContents(pile)` | DECK / 山札 / 捨札 / 除外 のモーダル表示時 |

---

## 10. 戦闘終了と RunState 反映

### 10-1. 戦闘開始時の準備

`StartBattle(RunState run, EncounterId enc, IRng rng) → BattleState`:

1. 主人公の `CombatActor` を生成（HP / MaxHp は run から、SlotIndex=0、DefinitionId="hero"）
2. 敵を `EncounterDefinition.EnemyIds` から生成、HP は `EnemyDefinition.Hp` をそのまま採用（乱数化は将来拡張ポイント）
3. **ラン側 `Deck` を全コピー**して山札にセット、`rng.Shuffle(山札)`
4. 手札 / 捨札 / 除外 / SummonHeld / PowerCards を空配列で初期化
5. `EnergyMax = 3, Energy = 3`
6. コンボ系を初期値
7. ターン 1 開始処理を実行（毒なし、状態異常 tick は無害発火、エナジー、5 ドロー、OnTurnStart レリック）
8. `OnBattleStart` レリック発動
9. 対象を初期化（`TargetAllyIndex=0, TargetEnemyIndex=0`）

### 10-2. 戦闘終了時の RunState 反映

`FinalizeBattle(BattleState bs, RunState before) → (RunState after, BattleSummary summary)`:

ラン側に**戻すもの**:
- 主人公の最終 HP（`Allies[0].Hp`）
- 戦闘中に消費したポーション
- 報酬画面引き金: `Outcome == Victory` なら `RewardGenerator` を呼ぶ。`Defeat` なら `RunProgress.GameOver`

ラン側に**戻さないもの**:
- バトル用デッキ全体（山札・手札・捨札・除外・SummonHeld・PowerCards）→ ラン側 `Deck` は戦闘前のまま不変
- 戦闘内ステータス（Block / Strength / Vulnerable / 力遡及 / etc.）→ 全破棄
- 召喚キャラ → 全消滅
- コンボ状態・エナジー → 戦闘終了で意味を失う

### 10-3. 既存 Phase 5 ロジックとの統合

- `BattlePlaceholder.cs` は Phase 10 完了時に **削除**
- 既存 `BattleState.cs` / `EncounterQueue.cs` は新ロジックで全面刷新
- `Bestiary.BestiaryTracker.NoteEnemiesEncountered` は新ロジックでも呼ばれる位置を維持
- `RewardGenerator` / `RewardApplier` は変更不要

### 10-4. ソロモードのゲームオーバー

- 主人公 HP ≤ 0 → 即 Outcome=Defeat, Resolved
- `RunState.Hp = 0`、`RunState.Progress = RunProgress.GameOver`
- `RunResultScreen` へ遷移（既存 Phase 7 を流用）

### 10-5. マルチモード対応の余地（Phase 10 では未実装、設計余地のみ）

Phase 9 マルチプレイ実装時に追加する設計余地:
- 主人公 HP=0 で戦闘不能フラグ、ターン開始時に HP 1 に復帰
- 敗北条件は「全味方戦闘不能」のような複合条件
- 「割り込み回復ポーション/レリック」の発動タイミングフック
- 実装時は `OnHpZero` フックを `BattleState` に追加し、Defeat 確定前に効果を割り込ませる構造

### 10-6. ラン側操作系の拡張ポイント（Phase 11+ 用）

Phase 10 ではスコープ外だが、設計として余地を残す:
- カード効果でラン側 Gold/HP/MaxHp を直接変更
- カード強化を戦闘外（ラン側）デッキに対して行う
- 敵の泥棒能力（山札・Gold を奪い、逃げられたらラン側にも反映）
- `BattleSummary.RunSideOperations` というキューを持たせ、`FinalizeBattle` 時に適用できる構造（Phase 10 では空配列）

---

## 11. テスト戦略

### 11-1. Core テスト（xUnit）

`tests/Core.Tests/Battle/` 配下にカテゴリ別に配置:

| カテゴリ | 主なテストケース |
|---|---|
| EffectNormalization | `Self` の Side 破棄／`attack` の Side enemy 強制／JSON ロード時呼出 |
| AttackPool | 加算・遡及計算・脱力 0.75 倍切捨・加算 0 回時の表示 0 |
| BlockPool | 敏捷遡及計算・ダメージ消費と AddCount リセット・ターン終了で 0 |
| PhaseTransition | PlayerInput → PlayerAttacking → EnemyAttacking → 次の PlayerInput／途中での Resolved 遷移 |
| TurnStartProcessing | 毒ダメージと即時 Outcome 判定／状態異常 tick／召喚 Lifetime tick／エナジー回復／ドロー／OnTurnStart レリック |
| PlayerAttacking | 内側→外側順／Block 順次削れ／対象死亡しても残り攻撃が飛ぶ／ランダム発射時乱数／脆弱・脱力・力の発射時補正／omnistrike |
| EnemyAttacking | 敵 attack の scope=all 強制／生存全敵が順次行動／味方 Block で受ける／主人公死亡で敗北 |
| Combo | 通常階段／Wild／SuperWild／リセット直後の Wild は新規スタート／effect の comboMin は ComboCount 更新後で判定 |
| CardPlayAndMovement | プレイ後の移動先優先順位／コスト軽減と支払い／ハンド上限超過のドロー無視 |
| Summon | 空きスロットあり召喚成功／空きなし不発／HP 0 で SummonHeld → 捨札／LifetimeTurns カウントダウン |
| TargetingAutoSwitch | 対象死亡 → 生存者の最内側に切替／全敵死亡 → null と Resolved Victory |
| StatusEffects | 力・敏捷の永続性／脆弱・脱力・omnistrike の N ターン減衰／毒の Block 無視ダメージと N 減衰／重ね掛けで amount 加算 |
| Relic | 各 Trigger の発動位置／所持順発動／`Implemented: false` は何もしない |
| Potion | 戦闘内全 effect 適用／戦闘外 BattleOnly スキップ／全 BattleOnly なら戦闘外で使用不可 |
| EffectPrimitives | 各 action × scope × side の組み合わせ |
| Determinism | 同じ seed + 同じ入力列で同じ結果（IRng 注入） |
| FinalizeBattle | 戻されるのは「HP / 消費ポーション / 報酬引き金」のみ／バトル用デッキは破棄／戦闘内ステータスは全破棄 |

### 11-2. Server テスト

- `BattleHubTests`: Client メッセージの受信 → Core 呼出 → push 順序
- `BattleSaveTests`: セーブが「戦闘開始時 / 戦闘終了時」のみ発火

### 11-3. Client テスト（vitest）

- `BattleScreen.test.tsx`: DTO 描画／ドラッグ&ドロップ／対象切替／ターン終了／パイルモーダル／Wild 表示

### 11-4. 既存テストとの整合

- 既存 `BattlePlaceholderTests` は Phase 10 で削除
- `RunStateSerializerTests` は不変
- 既存敵 / ポーション / レリック JSON ローダーテストは新形式対応で更新

### 11-5. テストデータ

- `tests/Core.Tests/Battle/TestData/` に最小限のカード／敵／召喚／レリック JSON を用意
- 本番データ JSON はテストでは使わない（脆弱性回避）

### 11-6. 粒度方針

- 核ロジック（AttackPool・コンボ判定・effect 適用）は単体メソッドレベル
- フェーズ統合（PlayerAttacking 全体・ターン開始処理全体・戦闘終了統合）は中粒度
- エンドツーエンド（1 戦闘を最初から最後まで）は数本（Victory / Defeat / 召喚使用込み / コンボ繋げ込み）

---

## Phase 10 スコープ・非スコープ

### スコープ内

- ✅ バトル基盤（カード・アタック値・対象・コンボ・状態異常・召喚・ポーション・レリック）
- ✅ ポーションの戦闘内・戦闘外発動 UI（マップ画面に新規実装）
- ✅ レリックの戦闘内 Trigger 発動
- ✅ 既存敵 / ポーション / レリック JSON の新形式書き換え
- ✅ 強化システム（+ 方式、`UpgradedEffects` 完全置換）
- ✅ 状態異常 5 種（力・敏捷・脆弱・脱力・毒）+ 全体化（omnistrike）
- ✅ Wild / SuperWild キーワード
- ✅ Phase 5 `BattlePlaceholder` の削除

### スコープ外（拡張ポイントだけ残す）

- ❌ コスト null カードの自動詠唱
- ❌ ラン側操作系カード（Gold/HP/MaxHp 直接変更、戦闘外デッキ強化）
- ❌ 敵泥棒能力（山札・Gold 奪取、逃げられたらラン側に反映）
- ❌ 具体的な Status / Curse カードのデータと動作（CardType と CSS classes だけ準備）
- ❌ Power カードの「常駐」エリアの詳細仕様（フィールドだけ用意、効果は Phase 11+）
- ❌ マルチモードの戦闘不能・割り込み回復（フックポイントだけ）
- ❌ ドロー +1 のような複雑なレリック効果のうち、`Implemented: false` で切り分けたもの
- ❌ retainSelf 以外の保留キーワード詳細仕様
- ❌ オーバーヒール表現（最大 HP 超過の HP）

---

## 移行作業（既存データ書き換え）

Phase 10 の作業項目に含める:

- `src/Core/Data/Cards/*.json` (現在 32 ファイル) を新形式に書き換え（effects プリミティブ、UpgradedCost / UpgradedEffects 整理、Keywords 追加）
- `src/Core/Data/Enemies/*.json` (現在 34 ファイル) を新 `MoveDefinition` 形式に書き換え（`Effects: List<CardEffect>`）
- `src/Core/Data/Potions/*.json` を新 `Effects` 形式に書き換え（`BattleOnly` フラグ追加）
- `src/Core/Data/Relics/*.json` を新形式に書き換え（`Trigger` 拡張、`Effects`、`Implemented` フラグ）
- `src/Core/Data/Units/*.json` を新規追加（召喚キャラ定義）
- 各ローダーテストを新形式対応

---

## 既存 Phase との整合

- **Phase 0 (JSON データ)**: CardEffect / EnemyDefinition / PotionDefinition / RelicDefinition の構造変更。ローダーも新形式対応
- **Phase 1 (RunState)**: 不変。戦闘内ステータスを RunState に持たない原則を維持
- **Phase 2 (メニュー)**: 影響なし
- **Phase 3 (マップ)**: 影響なし
- **Phase 4 (マップ進行)**: 影響なし
- **Phase 5 (暫定バトル)**: `BattlePlaceholder` 削除、本格バトルへ置換。`RewardGenerator` / `RewardApplier` は変更なし
- **Phase 6 (マス別イベント)**: Rest 強化ロジックは ラン側 Deck を直接変更する仕組みで継続。`upgrade` action は戦闘内専用なので別系統
- **Phase 7 (ボス・層移動)**: 影響なし
- **Phase 8 (図鑑・履歴)**: 状態異常リスト・召喚キャラ・Power/Status/Curse カードの追加で図鑑データに追加項目あり
- **Phase 9 (マルチプレイ)**: Phase 10 完了後に着手。HP=0 戦闘不能などの設計余地をこの spec に明記済み

---

## 主要拡張ポイント（将来 Phase 用）

| 拡張 | 触る場所 |
|---|---|
| コスト null カードの自動詠唱 | `CardEffect` に `AutoCastTrigger` 追加、`PlayCard` に自動詠唱経路追加 |
| ラン側操作系カード | `CardEffect` に `RunSideEffect: bool` 追加、`BattleSummary.RunSideOperations` キュー実装 |
| 敵泥棒能力 | `MoveDefinition` の `Effects` に「ラン側 Gold/カード奪取」action 追加 |
| Status / Curse カード具体動作 | カードデータ追加 + `CardType.Status / Curse` 専用ロジック |
| マルチモード戦闘不能 | `CombatActor.IsKnockedOut` 追加、`OnHpZero` フック実装 |
| 割り込み回復ポーション/レリック | `OnHpZero` フックで発動 |
| ドロー +1 のような複雑レリック | `draw` action は既に追加済、レリック JSON で `Implemented: true` にして effect を埋める |
| 強化システムのレベル化／効果スロット | カード強化システム改革（独立 Phase で実施） |

---

## 参照

- ロードマップ: [`2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md) Phase 10 節
- Phase 5 placeholder spec: [`2026-04-22-phase05-battle-placeholder-rewards-design.md`](2026-04-22-phase05-battle-placeholder-rewards-design.md)（差し替え対象）
- 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html`
- カード見た目 canonical: `archives-cards-v12.html`
