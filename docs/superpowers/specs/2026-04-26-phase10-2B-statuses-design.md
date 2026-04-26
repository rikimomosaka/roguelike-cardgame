# Phase 10.2.B — 状態異常 + 遡及計算 設計

> 作成日: 2026-04-26
> 対象フェーズ: Phase 10.2.B（Phase 10.2 サブマイルストーン 2 番目 / 全 5 段階）
> 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
> 直前マイルストーン spec: [`2026-04-26-phase10-2A-foundation-design.md`](2026-04-26-phase10-2A-foundation-design.md)
> 直前マイルストーン plan: [`../plans/2026-04-26-phase10-2A-foundation.md`](../plans/2026-04-26-phase10-2A-foundation.md)
> 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html`

## ゴール

10.2.A で建てた walking-skeleton（`attack` / `block` の 2 effect で殴り合うだけのバトル）に、**6 種の状態異常**（strength / dexterity / vulnerable / weak / omnistrike / poison）を導入する。

具体的には:

- `CombatActor` に `Statuses: ImmutableDictionary<string,int>` フィールド追加
- `AttackPool` に `Display(strength, weak)` / `+` operator、`BlockPool` に `Display(dexterity)` / `Consume(incomingAttack, dexterity)` を追加（10.2.A の `RawTotal` は internal 化）
- `EffectApplier` に `buff` / `debuff` action 対応（Self / Single / Random / All の 4 scope 全対応）
- `DealDamageHelper` を `(attacker, target, baseSum, addCount, scopeNote, orderBase)` 形に拡張し、攻撃側 strength/weak と受け側 vulnerable/dexterity の補正を中核ヘルパーに統合
- `TurnStartProcessor` にターン開始 tick（毒ダメージ → countdown）を追加。tick 中の死亡で Outcome=Victory/Defeat を確定
- `PlayerAttackingResolver` に omnistrike 合算発射を追加（Single+Random+All を合算 → 全敵に 1 回発射）
- `BattleEventKind` に `ApplyStatus` / `RemoveStatus` / `PoisonTick` の 3 値追加（計 12 値）
- `StatusDefinition` 静的リスト 6 種を `src/Core/Battle/Statuses/StatusDefinition.cs` に新設（10.2.A で空フォルダ準備済み）

10.2.A の `BattleEngine` 4 公開 API（`Start` / `PlayCard` / `EndTurn` / `Finalize`）のシグネチャは不変。コンボ・対象指定（`SetTarget`）・残り effect 8 種・召喚・レリック・ポーション戦闘内発動はすべて後続 sub-phase に持ち越す。

`BattlePlaceholder` 経由の既存ゲームフローは無傷で動作させ続ける。

## 完了判定

- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（10.2.A 完了時の 693 + 10.2.B 追加分）
- `CombatActor` に `Statuses: ImmutableDictionary<string,int>` フィールド追加済み、`GetStatus(id)` 便宜プロパティ動作
- `StatusDefinition` 静的リスト 6 種が `src/Core/Battle/Statuses/StatusDefinition.cs` に存在
- `AttackPool.Display(strength, weak)` / `AttackPool.operator +` / `BlockPool.Display(dexterity)` / `BlockPool.Consume(incomingAttack, dexterity)` が動作。10.2.A の `RawTotal` は internal 化（`Core.Tests` 経由でのみ参照可）
- `EffectApplier` が `buff` / `debuff` を 4 scope（Self / Single / Random / All）で処理、`ApplyStatus` / `RemoveStatus` event 発火
- `EffectApplier.ReplaceActor` が memory feedback の InstanceId 検索ルールに準拠（10.2.A の latent bug 根治）
- `DealDamageHelper.Apply` シグネチャが `(attacker, target, baseSum, addCount, scopeNote, orderBase)` に変更され、攻撃側 strength × addCount / weak / 受け側 vulnerable / dexterity の補正を統合
- `TurnStartProcessor` がターン開始 tick（毒ダメージ → countdown）を実行し、毒死で `Outcome=Victory/Defeat`, `Phase=Resolved` を確定
- `BattleEngine.EndTurn` が TurnStartProcessor 後の Outcome 確定時に Phase 上書きをスキップ
- `PlayerAttackingResolver` が omnistrike 持ち ally に対して `Single + Random + All` 合算発射、それ以外は従来通り Single → Random → All の順で発射
- `BattleEventKind` に `ApplyStatus` / `RemoveStatus` / `PoisonTick` の 3 値追加（計 12 値）
- 既存の `BattlePlaceholder` 経由のフロー（敵タイル進入 → 即勝利ボタン → 報酬画面）は無傷で動作（手動プレイ確認）
- 親 spec の §3-2 / §3-3 / §4-2 / §4-4 / §5-1 / §9-7 に Phase 10.2.B 内で発生した設計判断を補記済み
- `phase10-2B-complete` タグが切られ origin に push 済み

---

## 1. アーキテクチャ概要

### 1-1. Phase 10.2 全体の中での位置付け

| サブ phase | 範囲 | 状態 |
|---|---|---|
| 10.2.A | 基盤データモデル + `BattleEngine` 4 公開 API + `attack`/`block` の 2 effect + Phase 進行 + Victory/Defeat | ✅ 完了 |
| **10.2.B**（本 spec） | **状態異常 6 種 + 遡及計算 + buff/debuff effect + ターン開始 tick + omnistrike 合算発射** | 本フェーズ |
| 10.2.C | コンボ（Wild/SuperWild/コスト軽減/comboMin） + 対象指定の SetTarget アクション | 後続 |
| 10.2.D | 残り effect 8 種 + 召喚 system + カード移動 5 段優先順位 + PowerCards | 後続 |
| 10.2.E | レリック 4 新 Trigger 発火 + 所持順発動 + Implemented スキップ + UsePotion 戦闘内 + BattleOnly 戦闘外スキップ | 後続 |

10.2.B は 10.2.C〜E の前提となる「状態異常を読み書きする基盤」を完成させる。後続 phase は 10.2.B の `Statuses` フィールド・status 計算ロジック・event 発火基盤を流用する。

### 1-2. 共存戦略（NodeEffectResolver / BattlePlaceholder）

10.2.A と同じ。新 `BattleEngine` は **pure Core API として独立**し、`NodeEffectResolver` は引き続き `BattlePlaceholder.Start` を呼ぶ（既存ゲームフローは無傷）。`BattleEngine` は xUnit でしかテストされない。

### 1-3. ファイル構成（10.2.B 完了時の差分）

```
src/Core/Battle/
├── State/
│   ├── AttackPool.cs                    [修正] Display(str, weak) / + operator / RawTotal は internal 化
│   ├── BlockPool.cs                     [修正] Display(dex) / Consume(in, dex) / RawTotal は internal 化
│   └── CombatActor.cs                   [修正] Statuses: ImmutableDictionary<string,int> 追加
│                                                + GetStatus(id) 便宜プロパティ
├── Statuses/                            [新フォルダ]（10.2.A で空フォルダを用意済み）
│   ├── StatusDefinition.cs              [新] record + static IReadOnlyList<StatusDefinition> All
│   ├── StatusKind.cs                    [新] enum Buff / Debuff
│   └── StatusTickDirection.cs           [新] enum None / Decrement
├── Events/
│   └── BattleEventKind.cs               [修正] +ApplyStatus / RemoveStatus / PoisonTick （計 12 値）
└── Engine/
    ├── DealDamageHelper.cs              [修正] (attacker, target, baseSum, addCount, scopeNote, orderBase) に変更
    ├── EffectApplier.cs                 [修正] buff / debuff action 追加 + ReplaceActor の InstanceId 化
    ├── TurnStartProcessor.cs            [修正] tick 処理（毒 → countdown → 死亡判定 → Outcome 確定）
    ├── PlayerAttackingResolver.cs       [修正] omnistrike 合算発射 + status 補正は DealDamageHelper 経由
    │                                              + InstanceId 検索による書き戻しに統一
    ├── EnemyAttackingResolver.cs        [修正] DealDamageHelper シグネチャ変更に追従
    └── BattleEngine.EndTurn.cs          [修正] TurnStartProcessor が Outcome を立てた場合 Phase 上書きをスキップ

tests/Core.Tests/Battle/
├── State/
│   ├── AttackPoolTests.cs               [修正] Display(str, weak) / + operator のテスト追加
│   ├── BlockPoolTests.cs                [修正] Display(dex) / Consume(in, dex) のテスト追加
│   └── CombatActorTests.cs              [修正] Statuses フィールド + GetStatus のテスト追加
├── Statuses/                            [新フォルダ]
│   └── StatusDefinitionTests.cs         [新] 6 種の存在 / IsPermanent / Kind / TickDirection
├── Events/
│   └── BattleEventKindTests.cs          [修正] 12 値の整数値検証
├── Engine/
│   ├── DealDamageHelperTests.cs                       [新] str/weak/vuln/dex の各補正 / 組み合わせ
│   ├── EffectApplierBuffDebuffTests.cs                [新] 4 scope × 重ね掛け / RemoveStatus 発火条件
│   ├── EffectApplierReplaceActorInstanceIdTests.cs    [新] 複数 effect カードでの latent bug 回帰防止
│   ├── TurnStartProcessorTickTests.cs                 [新] 毒ダメージ / countdown / 0 で削除 / 毒死で Outcome 確定
│   ├── PlayerAttackingResolverOmnistrikeTests.cs      [新] 合算発射 / Pool 全 Empty で発射なし
│   ├── PlayerAttackingResolverStatusTests.cs          [新] str/weak の遡及反映 / vuln 受け
│   ├── EnemyAttackingResolverStatusTests.cs           [新] 敵側 str/weak / 受け側 vuln/dex
│   ├── PlayerAttackingResolverTests.cs                [修正] DealDamageHelper シグネチャ変更追従
│   ├── EnemyAttackingResolverTests.cs                 [修正] 同上
│   ├── EffectApplierTests.cs                          [修正] 既存 attack/block テストの fixture 追従
│   ├── BattleEngineEndTurnTests.cs                    [修正] TurnStart 中の毒死で Outcome 確定パス追加
│   └── BattleDeterminismTests.cs                      [修正] status 含む 1 戦闘で seed 同一 → 一致
└── Fixtures/
    └── BattleFixtures.cs                              [修正] WithStatus / Strength / Vulnerable 等の factory 拡張
```

### 1-4. namespace

| パス | namespace |
|---|---|
| `src/Core/Battle/Statuses/*.cs` | `RoguelikeCardGame.Core.Battle.Statuses`（新規） |
| 既存 namespace は不変 | |

### 1-5. memory feedback の遵守（10.2.A 由来）

10.2.A で繰り返し問題になった 2 ルール（`memory/feedback_battle_engine_conventions.md`）を 10.2.B でも厳守する:

1. **`BattleOutcome` は常に fully qualified**: `RoguelikeCardGame.Core.Battle.State.BattleOutcome` と書く。10.2.B で新規追加するコード（`TurnStartProcessor` 内の Outcome 確定、`BattleEngine.EndTurn` の上書き回避ロジック等）でも徹底
2. **`state.Allies` / `state.Enemies` への書き戻しは InstanceId で検索**: 10.2.B では以下の箇所すべてで適用:
   - `EffectApplier.ReplaceActor`（10.2.A の latent bug 根治）
   - `TurnStartProcessor` の毒 tick / countdown ループ
   - `PlayerAttackingResolver` の omnistrike 合算発射ループ（複数敵への着弾）
   - `EffectApplier` の buff/debuff 4 scope（特に Random / All で複数 actor を更新）

これらを「最初から InstanceId 検索パターンで書く」ことで latent bug を発生させない。

---

## 2. データモデル

### 2-1. `StatusKind` / `StatusTickDirection` enum

```csharp
namespace RoguelikeCardGame.Core.Battle.Statuses;

public enum StatusKind
{
    Buff   = 0,
    Debuff = 1,
}

public enum StatusTickDirection
{
    None      = 0,    // ターン開始 tick で減衰しない（strength / dexterity）
    Decrement = 1,    // ターン開始 tick で −1（vulnerable / weak / omnistrike / poison）
}
```

### 2-2. `StatusDefinition`

```csharp
namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>
/// 状態異常の静的定義。Phase 10 では JSON 化せず C# の static リストで保持。
/// 親 spec §2-6 参照。
/// </summary>
public sealed record StatusDefinition(
    string Id,
    StatusKind Kind,
    bool IsPermanent,
    StatusTickDirection TickDirection)
{
    public static IReadOnlyList<StatusDefinition> All { get; } = new[]
    {
        new StatusDefinition("strength",   StatusKind.Buff,   IsPermanent: true,  StatusTickDirection.None),
        new StatusDefinition("dexterity",  StatusKind.Buff,   IsPermanent: true,  StatusTickDirection.None),
        new StatusDefinition("omnistrike", StatusKind.Buff,   IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("vulnerable", StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("weak",       StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("poison",     StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
    };

    public static StatusDefinition Get(string id) =>
        All.FirstOrDefault(s => s.Id == id)
        ?? throw new InvalidOperationException($"unknown status id '{id}'");
}
```

`IsPermanent == true && TickDirection == None` の組は strength / dexterity 用。冗長だが意図を明確にする（IsPermanent だけで条件分岐すると tick ロジックが直感的でなくなる）。

DisplayName / Description は Phase 10.4（UI 実装）で必要になるため、本フェーズでは保持しない。

### 2-3. `CombatActor` の Statuses フィールド追加

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

public sealed record CombatActor(
    string InstanceId,
    string DefinitionId,
    ActorSide Side,
    int SlotIndex,
    int CurrentHp,
    int MaxHp,
    BlockPool Block,
    AttackPool AttackSingle,
    AttackPool AttackRandom,
    AttackPool AttackAll,
    ImmutableDictionary<string, int> Statuses,   // ← 10.2.B で追加
    string? CurrentMoveId)
{
    public bool IsAlive => CurrentHp > 0;

    /// <summary>未保持なら 0 を返す便宜プロパティ。</summary>
    public int GetStatus(string id) => Statuses.GetValueOrDefault(id, 0);
}
```

- 初期値は `ImmutableDictionary<string, int>.Empty`
- 0 になった key は dict から削除する方針（§5-3 / §5-4 参照）。`GetStatus(id) > 0` で IsActive 判定

### 2-4. `AttackPool` API 拡張

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

public readonly record struct AttackPool(int Sum, int AddCount)
{
    public static AttackPool Empty => new(0, 0);

    public AttackPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>omnistrike 合算用。Sum / AddCount をペアで加算する。</summary>
    public static AttackPool operator +(AttackPool a, AttackPool b) =>
        new(a.Sum + b.Sum, a.AddCount + b.AddCount);

    /// <summary>
    /// 力バフを遡及反映（×AddCount）し、脱力 weak > 0 で 0.75 倍切捨。
    /// 親 spec §3-3 参照。
    /// </summary>
    public int Display(int strength, int weak)
    {
        long boosted = (long)Sum + (long)AddCount * strength;
        return weak > 0 ? (int)(boosted * 3 / 4) : (int)boosted;
    }

    /// <summary>10.2.A の暫定 API、テスト・debug 用に internal 化して温存。</summary>
    internal int RawTotal => Sum;
}
```

- 切り捨ては integer 演算（`* 3 / 4`）。`(int)(x * 0.75)` は浮動小数点誤差リスクがあるため避ける
- `long` キャストで `AddCount * strength` のオーバーフロー防止（実用上 ありえないが防御的）
- `RawTotal` は `Core.csproj` の `InternalsVisibleTo("Core.Tests")` 経由でテストから引き続き参照可能

### 2-5. `BlockPool` API 拡張

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

public readonly record struct BlockPool(int Sum, int AddCount)
{
    public static BlockPool Empty => new(0, 0);

    public BlockPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>敏捷を遡及反映（×AddCount）した表示・吸収用 Block 量。</summary>
    public int Display(int dexterity) => Sum + AddCount * dexterity;

    /// <summary>
    /// 攻撃の総量を受けて Block を消費。引数 incomingAttack は「ブロック適用前の攻撃値」を渡す。
    /// 残量を新 Sum、AddCount=0 にリセット（消費後は遡及性を失う）。
    /// 親 spec §3-3 / §4-4 参照。
    /// </summary>
    public BlockPool Consume(int incomingAttack, int dexterity)
    {
        var current = Display(dexterity);
        var remaining = Math.Max(0, current - incomingAttack);
        return new(remaining, 0);
    }

    /// <summary>10.2.A の暫定 API、テスト・debug 用に internal 化。</summary>
    internal int RawTotal => Sum;
}
```

旧 `Consume(int)` は **削除**。呼び出し側は `DealDamageHelper` の 1 箇所のみで、同 commit で `Consume(in, dex)` に置換する。

### 2-6. `BattleEventKind` の拡張

```csharp
namespace RoguelikeCardGame.Core.Battle.Events;

public enum BattleEventKind
{
    BattleStart   = 0,
    TurnStart     = 1,
    PlayCard      = 2,
    AttackFire    = 3,
    DealDamage    = 4,
    GainBlock     = 5,
    ActorDeath    = 6,
    EndTurn       = 7,
    BattleEnd     = 8,
    ApplyStatus   = 9,    // ← 10.2.B 新規（buff/debuff 付与・重ね掛け）
    RemoveStatus  = 10,   // ← 10.2.B 新規（countdown で 0 → 削除）
    PoisonTick    = 11,   // ← 10.2.B 新規（毒ダメージ）
}
```

イベントペイロードの慣例:

| Kind | Caster | Target | Amount | Note |
|---|---|---|---|---|
| `ApplyStatus` | 付与主体（カード使用なら hero、敵 Move なら敵自身） | 対象 actor | 加算量（delta） | status_id（"strength" 等） |
| `RemoveStatus` | null | 対象 actor | null | status_id |
| `PoisonTick` | null | 対象 actor | ダメージ量（=poison amount） | "poison" |

毒ダメージは「攻撃由来でない HP 減算」なので `DealDamage` と分離（クライアント側の演出設計でも有利）。

---

## 3. `DealDamageHelper` 拡張（攻撃補正の中核）

### 3-1. 新シグネチャ

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class DealDamageHelper
{
    /// <summary>
    /// 1 回の攻撃を target に着弾させる。
    /// 攻撃側 strength × addCount で遡及加算 → weak で 0.75 切捨 →
    /// dexterity 反映の Block で吸収 → 残量に vulnerable で 1.5 倍 → HP 減算。
    /// 親 spec §4-4 参照。
    /// </summary>
    /// <returns>(更新後 target, イベント列, 今この攻撃で死亡したか)</returns>
    public static (CombatActor updatedTarget, IReadOnlyList<BattleEvent> events, bool diedNow) Apply(
        CombatActor attacker,
        CombatActor target,
        int baseSum,
        int addCount,
        string scopeNote,
        int orderBase);
}
```

### 3-2. 処理フロー

```
1. 攻撃側補正
   strength = attacker.GetStatus("strength")
   weak     = attacker.GetStatus("weak")
   long boosted = baseSum + addCount * strength
   totalAttack  = weak > 0 ? (int)(boosted * 3 / 4) : (int)boosted

2. Block 消費（敏捷遡及込み）
   dex      = target.GetStatus("dexterity")
   preBlock = target.Block.Display(dex)
   absorbed = min(totalAttack, preBlock)
   rawDamage = totalAttack - absorbed
   newBlock  = target.Block.Consume(totalAttack, dex)   // AddCount=0 にリセット

3. 受け側補正（脆弱）
   vulnerable = target.GetStatus("vulnerable")
   damage     = vulnerable > 0 ? (rawDamage * 3) / 2 : rawDamage

4. HP 減算
   newHp     = target.CurrentHp - damage
   updated   = target with { Block = newBlock, CurrentHp = newHp }
   diedNow   = wasAlive && !updated.IsAlive

5. イベント発火（順序）
   AttackFire { Caster=attacker, Target=target, Amount=totalAttack, Note=scopeNote }
   DealDamage { Caster=attacker, Target=target, Amount=damage,      Note=scopeNote }
   if (diedNow) ActorDeath { Caster=attacker, Target=target, Note=scopeNote }
```

### 3-3. 設計上の判断

- **`AttackFire.Amount` = 攻撃側補正後・Block 適用前**の値（クライアントの「頭上飛び出し数値」と一致、ターン中の表示値とも一致）
- **`DealDamage.Amount` = 最終 HP 減算量**（vulnerable / Block 反映済み）
- **vulnerable は Block 通り後**に適用（spec §4-4 の擬似コード順）。これは:
  - Block 100 持ちに 10 ダメージ攻撃 + vulnerable → 0 ダメージ（× 1.5 しても 0）
  - Block 5 持ちに 10 ダメージ攻撃 + vulnerable → 5 通り → × 1.5 = 7 ダメージ
- 切り捨ては全て integer 演算
- **`updated` の status / SlotIndex / InstanceId はそのまま**（caller が state.Allies / Enemies への書き戻しを担う）

### 3-4. 呼び出し側の差異

| 呼び出し側 | baseSum | addCount | scopeNote |
|---|---|---|---|
| `PlayerAttackingResolver` Single | `ally.AttackSingle.Sum` | `ally.AttackSingle.AddCount` | "single" |
| `PlayerAttackingResolver` Random | `ally.AttackRandom.Sum` | `ally.AttackRandom.AddCount` | "random" |
| `PlayerAttackingResolver` All | `ally.AttackAll.Sum` | `ally.AttackAll.AddCount` | "all" |
| `PlayerAttackingResolver` omnistrike | `(Single + Random + All).Sum` | `(Single + Random + All).AddCount` | "omnistrike" |
| `EnemyAttackingResolver` per-effect attack | `effect.Amount` | 1 | "enemy_attack" |

敵 attack は `AttackPool` を通さず直接発射するため `addCount = 1`。これは「敵の力バフは 1 effect ごとに 1 回ずつ加算される」挙動と一致する。

---

## 4. `EffectApplier` の buff / debuff 拡張

### 4-1. action ディスパッチ

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    return effect.Action switch
    {
        "attack" => ApplyAttack(state, caster, effect),
        "block"  => ApplyBlock(state, caster, effect),
        "buff"   => ApplyStatusChange(state, caster, effect, rng),
        "debuff" => ApplyStatusChange(state, caster, effect, rng),
        _        => (state, Array.Empty<BattleEvent>()),
    };
}
```

`buff` / `debuff` は内部処理を共有（status を加算するだけで、Kind の解釈は `StatusDefinition` 側で一意）。

### 4-2. ターゲット解決（4 scope）

```
TargetActors(state, caster, effect, rng):
  side = effect.Side  // null は Self では無害、Self 以外で null なら例外
  switch (effect.Scope):
    Self   → [ caster ]
    Single →
      if (side == Ally  && state.TargetAllyIndex  is { } ai) → [ state.Allies[ai] ]
      if (side == Enemy && state.TargetEnemyIndex is { } ei) → [ state.Enemies[ei] ]
      else → [] (対象不在は no-op)
    Random →
      pool = (side == Ally ? state.Allies : state.Enemies).Where(IsAlive).ToList()
      if (pool.Empty) → []
      → [ pool[rng.NextInt(0, pool.Count)] ]
    All    →
      → (side == Ally ? state.Allies : state.Enemies).Where(IsAlive).ToList()
```

> 補足: `effect.Side` は `EffectSide?`。`buff` / `debuff` で `Self` 以外の scope のときに `Side == null` は実装エラー → ApplyEffect で例外。`CardEffect.Normalize` は `attack` の Side 強制のみで buff/debuff には介入しないため、JSON ロード時のバリデーションが必要（10.2.B の範囲では「実装側で例外」のみ、ローダー検証は後続 phase で対応する余地）。

### 4-3. 重ね掛け加算と event 発火

```
foreach target in TargetActors(...):
  string statusId = effect.Name      // "strength" / "vulnerable" / "poison" 等
  int    delta    = effect.Amount

  // 1. 重ね掛け加算
  int currentAmount = target.GetStatus(statusId)
  int newAmount     = currentAmount + delta

  // 2. dict 更新（0 以下は削除）
  ImmutableDictionary<string,int> newStatuses;
  if (newAmount <= 0):
    newStatuses = target.Statuses.Remove(statusId)
  else:
    newStatuses = target.Statuses.SetItem(statusId, newAmount)

  // 3. actor 書き戻し（InstanceId 検索 で）
  var updated = target with { Statuses = newStatuses }
  state = ReplaceActor(state, target.InstanceId, updated)

  // 4. イベント
  if (newAmount > 0 && currentAmount != newAmount):
    emit ApplyStatus { Caster=caster.InstanceId, Target=target.InstanceId, Amount=delta, Note=statusId }
  else if (newAmount <= 0 && currentAmount > 0):
    emit RemoveStatus { Target=target.InstanceId, Note=statusId }
```

- 同名 status の再付与は `existing + delta`（spec §2-6 の「重ね掛けで amount 加算」）
- delta が負（仕様外だが防御的に）で結果が 0 以下になったら dict から削除 + RemoveStatus 発火
- `Caster` は EffectApplier の `caster` 引数をそのまま流す（PlayCard なら hero、敵 Move なら敵）

### 4-4. `EffectApplier.ReplaceActor` の InstanceId 化（latent bug 根治）

10.2.A の `EffectApplier.ReplaceActor` は `state.Allies.IndexOf(before)` を使っていたため、複数 effect 間で `before` が古い snapshot 参照になると `IndexOf == -1` で `SetItem` 例外になる latent bug があった。

10.2.A の `BattleEngine.PlayCard` は effect ごとに `caster = s.Allies[0]` で再取得することで回避していたが、構造的には危うい。10.2.B で `ReplaceActor` を InstanceId 検索に書き換えて根治する:

```csharp
private static BattleState ReplaceActor(BattleState state, string instanceId, CombatActor after)
{
    if (after.Side == ActorSide.Ally)
    {
        for (int i = 0; i < state.Allies.Length; i++)
            if (state.Allies[i].InstanceId == instanceId)
                return state with { Allies = state.Allies.SetItem(i, after) };
    }
    else
    {
        for (int i = 0; i < state.Enemies.Length; i++)
            if (state.Enemies[i].InstanceId == instanceId)
                return state with { Enemies = state.Enemies.SetItem(i, after) };
    }
    return state;
}
```

`EffectApplierReplaceActorInstanceIdTests` で「複数 effect カード（attack + block + buff）が 1 PlayCard 内で連続適用される」回帰テストを追加。

---

## 5. `TurnStartProcessor` の tick 処理

### 5-1. 処理フロー（spec §4-2 step 1〜3 + 5〜6 + 7 のうち 10.2.B 範囲）

```
1. Turn+1
2. 毒ダメージ tick（全 actor、Allies → Enemies、SlotIndex 順）
   InstanceId スナップショットを採り、各 iteration で state から再 fetch:
     poison = actor.GetStatus("poison")
     if (poison <= 0 || !actor.IsAlive) continue
     // Block 無視ダメージ
     newHp   = actor.CurrentHp - poison
     updated = actor with { CurrentHp = newHp }
     state   = ReplaceActor(state, actor.InstanceId, updated)
     emit PoisonTick { Target=actor, Amount=poison, Note="poison" }
     if (wasAlive && !updated.IsAlive):
       emit ActorDeath { Target=actor }
3. tick 後の死亡判定
   state = TargetingAutoSwitch.Apply(state)   // 対象 index 死亡で生存者最内側へ
   if (!state.Enemies.Any(IsAlive)):
     state = state with { Outcome=Victory, Phase=Resolved }
     emit BattleEnd { Note="Victory" }
     return
   if (!state.Allies.Any(IsAlive)):
     state = state with { Outcome=Defeat, Phase=Resolved }
     emit BattleEnd { Note="Defeat" }
     return
4. status countdown（全 actor、Allies → Enemies、SlotIndex 順）
   InstanceId スナップショットを採り、各 iteration で state から再 fetch:
     foreach (id in actor.Statuses.Keys.ToList()):
       def = StatusDefinition.Get(id)
       if (def.TickDirection != Decrement) continue   // strength / dexterity skip
       newAmount = actor.Statuses[id] - 1
       if (newAmount <= 0):
         newStatuses = actor.Statuses.Remove(id)
         emit RemoveStatus { Target=actor, Note=id }
       else:
         newStatuses = actor.Statuses.SetItem(id, newAmount)
         // ApplyStatus event は発火しない（countdown は意味論的に違う）
       state = ReplaceActor(state, actor.InstanceId, actor with { Statuses = newStatuses })
5. Energy = EnergyMax
6. 5 ドロー（既存ロジック流用、ハンド上限 10）
7. TurnStart event 発火
```

### 5-2. 設計上の判断

- **順序: 毒 → countdown** は spec §4-2 で固定。「N ターン毒」を付けたターン開始時 → N ダメージ → N-1 へ。これにより「毒 1 ターン残し」が「最後のダメージを受けてから消える」挙動になる
- **死亡判定の境界**: 毒 tick で actor が死亡 → 全敗北 / 全勝利が確定したら return（countdown / energy / draw はスキップ）。countdown はスキップしても次の戦闘では state がリセットされるので問題なし
- **`!IsAlive` actor の二重適用回避**: 死亡済み actor は step 2 / step 4 でスキップ
- **Allies が先か Enemies が先か**: Allies 先（spec の擬似コード順）。死亡判定は両陣営 tick 完了後に 1 回でやるので順序依存はない
- **`TargetingAutoSwitch.Apply` 流用**: 10.2.A 既存の auto switch をそのまま呼ぶ
- **InstanceId 検索の徹底**: tick は loop 内で actor を更新しながら次 actor へ進むため、memory feedback ルールの典型的な該当箇所。最初に `actor.InstanceId` のスナップショット list を作り、各 iteration で state から再 fetch
- **ApplyStatus event を countdown では発火しない**: countdown は negative delta なので「buff/debuff 付与」とは意味論が違う。RemoveStatus（0 になった瞬間）のみ発火。クライアントは status の amount 表示を毎ターン再描画する想定なので、countdown の中間値変化を event で追う必要はない

### 5-3. `BattleEngine.EndTurn` の Outcome 上書き回避

10.2.A の `BattleEngine.EndTurn` 末尾は `s = afterStart with { Phase = BattlePhase.PlayerInput }` を**無条件**で実行。10.2.B では TurnStartProcessor が Outcome を立てた場合に Phase 上書きをスキップ:

```csharp
var (afterStart, evsStart) = TurnStartProcessor.Process(s, rng);
AddWithOrder(events, evsStart, ref order);
if (afterStart.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
    return (afterStart, events);   // Phase=Resolved を上書きしない
s = afterStart with { Phase = BattlePhase.PlayerInput };
return (s, events);
```

`BattleEngine.Start` も同じ TurnStartProcessor を呼ぶが、戦闘開始直後は status なし → Outcome は Pending のまま → 既存挙動と一致。

---

## 6. `PlayerAttackingResolver` の omnistrike 合算発射

### 6-1. 処理フロー

```
foreach ally in state.Allies.OrderBy(SlotIndex):
  if (!ally.IsAlive) continue;

  bool omni = ally.GetStatus("omnistrike") > 0;

  if (omni):
    // 合算 Pool
    var combined = ally.AttackSingle + ally.AttackRandom + ally.AttackAll;
    if (combined.Sum > 0):
      // 全敵に発射（SlotIndex 順）
      var enemyIds = state.Enemies.Select(e => e.InstanceId).ToList();
      foreach (var eid in enemyIds):
        // InstanceId で再 fetch（per-effect で state 更新済み）
        int idx = -1;
        for (int i = 0; i < state.Enemies.Length; i++)
          if (state.Enemies[i].InstanceId == eid) { idx = i; break; }
        if (idx < 0) continue;
        var current = state.Enemies[idx];
        // 死亡敵にも一応撃つ（spec §4-4 仕様準拠 / DealDamageHelper が 0 ダメージ判定する）
        var (updated, evs, _) = DealDamageHelper.Apply(
          ally, current, combined.Sum, combined.AddCount, "omnistrike", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
        events.AddRange(evs);
        order += evs.Count;

  else:
    // Single → Random → All の順（10.2.A の挙動を維持）
    if (state.TargetEnemyIndex is { } ti && ally.AttackSingle.Sum > 0 && ti < state.Enemies.Length):
      var (updated, evs, _) = DealDamageHelper.Apply(
        ally, state.Enemies[ti], ally.AttackSingle.Sum, ally.AttackSingle.AddCount, "single", order);
      state = state with { Enemies = state.Enemies.SetItem(ti, updated) };
      events.AddRange(evs); order += evs.Count;

    if (ally.AttackRandom.Sum > 0 && state.Enemies.Length > 0):
      int idx = rng.NextInt(0, state.Enemies.Length);   // 死亡敵含む（spec §4-4 仕様）
      var (updated, evs, _) = DealDamageHelper.Apply(
        ally, state.Enemies[idx], ally.AttackRandom.Sum, ally.AttackRandom.AddCount, "random", order);
      state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
      events.AddRange(evs); order += evs.Count;

    if (ally.AttackAll.Sum > 0):
      var enemyIds = state.Enemies.Select(e => e.InstanceId).ToList();
      foreach (var eid in enemyIds):
        // InstanceId 再 fetch（per-effect 更新後の最新を引く）
        int idx = ...;
        ...
```

### 6-2. memory feedback の InstanceId 検索ルール適用

10.2.A の `PlayerAttackingResolver` は Single / Random は単発のため `state.Enemies.SetItem(ti, updated)` を直接やっていた（per-effect 更新後の参照に問題なし）。10.2.B で `omnistrike` と `All` のループ内で複数敵に着弾するため、各 iteration で InstanceId 再 fetch するパターンに揃える。

10.2.A の `EnemyAttackingResolver` で確立した「allyIdsAtStart 採取 → loop 内で再 fetch」パターンを `PlayerAttackingResolver` の omnistrike / All ループにも適用する。

### 6-3. weak / strength 補正の遡及性

omnistrike 合算でも `combined.AddCount` には Single / Random / All の AddCount が累計されているので、`Display` 経由で「攻撃を打った回数 × strength」が正しく加算される。

例: Single で +5 を 2 枚（AddCount=2, Sum=10）、All で +3 を 1 枚（AddCount=1, Sum=3）→ combined `(Sum=13, AddCount=3)`。strength=2 なら `13 + 3*2 = 19` を全敵に発射。

omnistrike が立っているが Pool が全部 Empty の場合は `combined.Sum == 0` なので発射しない。

---

## 7. 不変条件（10.2.B 追加分）

10.2.A の §2-9 不変条件 8 項目に加えて、10.2.B で以下を保証:

- `CombatActor.Statuses` の値はすべて `> 0`（`<= 0` のキーは存在しない）
- `Statuses.ContainsKey(id)` のとき `id` は `StatusDefinition.All` のいずれかに存在する（未知 id は EffectApplier 経由でのみ追加され、`StatusDefinition.Get(id)` で例外になる）
- ターン開始 tick 後、Outcome が確定したら `Phase == Resolved`（5-3 の上書き回避経路）

`BattleStateInvariantTests` に追加項目として組み込む。

---

## 8. テスト戦略

### 8-1. テスト粒度

10.2.A と同じ TDD 1 サイクル粒度（失敗テスト → 実装 → 緑 → commit）。subagent-driven-development で進める前提。

### 8-2. 新規テストファイル一覧

| ファイル | カバレッジ |
|---|---|
| `Statuses/StatusDefinitionTests.cs` | 6 種の存在 / Kind / IsPermanent / TickDirection / `Get(id)` 既知/未知 |
| `Engine/DealDamageHelperTests.cs` | str 加算 / weak 0.75 切捨 / vuln 1.5 切捨 / dex Block / Block 通り後 vuln / 全補正同時 / 死亡時 ActorDeath / `wasAlive=false` で `diedNow=false` |
| `Engine/EffectApplierBuffDebuffTests.cs` | buff Self / Single / Random / All / debuff 同 / 重ね掛け加算 / `Self 以外で Side==null` 例外 / ApplyStatus 発火（caster 込み）/ 0 → Remove で RemoveStatus 発火 |
| `Engine/EffectApplierReplaceActorInstanceIdTests.cs` | 複数 effect カード（attack + block + buff）が 1 PlayCard で連続適用されても全 effect が正しく state に反映される（latent bug 回帰防止） |
| `Engine/TurnStartProcessorTickTests.cs` | 毒ダメージ Block 無視 / 毒死で ActorDeath / 全敵毒死で Victory & Phase=Resolved / 主人公毒死で Defeat & Phase=Resolved / countdown -1 / 0 で削除 + RemoveStatus / strength/dex は countdown skip / 死亡 actor は tick skip |
| `Engine/PlayerAttackingResolverOmnistrikeTests.cs` | omni 持ちで全敵着弾 / Pool 全 Empty で発射なし / combined.AddCount を strength で乗算 / Single+All 混在の合算 / 死亡敵にも形式上発射（0 ダメージ） |
| `Engine/PlayerAttackingResolverStatusTests.cs` | str 遡及（×AddCount）/ weak 0.75 / vuln 1.5（受け側）/ dex Block / 複合（str + vuln + dex 同時） |
| `Engine/EnemyAttackingResolverStatusTests.cs` | 敵側 str（addCount=1）/ 敵側 weak / 受け側 vuln / 受け側 dex / 複合 |

### 8-3. 既存テスト拡張

| ファイル | 変更 |
|---|---|
| `State/AttackPoolTests.cs` | Display(0,0)=Sum / Display(str,0) 遡及 / Display(str,weak>0) 0.75 切捨 / `+` operator (Sum/AddCount 共加算) / RawTotal は internal 化（既存 public テストは internal アクセスに変更） |
| `State/BlockPoolTests.cs` | Display(0)=Sum / Display(dex) 遡及 / Consume(in,dex) 残量 / Consume 後 AddCount=0 / 旧 `Consume(int)` 廃止 |
| `State/CombatActorTests.cs` | Statuses フィールド record 等価 / GetStatus 既知/未知 / Statuses 違いで record !等しい |
| `Events/BattleEventKindTests.cs` | 12 値の整数値検証 |
| `Engine/PlayerAttackingResolverTests.cs` | DealDamageHelper シグネチャ変更追従、status 0 時の互換挙動（既存テストの assertion は不変） |
| `Engine/EnemyAttackingResolverTests.cs` | 同上 |
| `Engine/EffectApplierTests.cs` | 既存 attack/block の fixture を `Statuses: ImmutableDictionary<string,int>.Empty` で初期化 |
| `Engine/BattleEngineEndTurnTests.cs` | TurnStart 中の毒死で Outcome 確定パス（Defeat / Victory）追加 |
| `Engine/BattleDeterminismTests.cs` | status 含む 1 戦闘で seed 同一 → 状態・event 完全一致 |
| `Fixtures/BattleFixtures.cs` | `WithStatus(actor, id, amount)` / `Strength(int)` / `Vulnerable(int)` / `Weak(int)` / `Poison(int)` 等の小さな factory 拡張、`Statuses: ImmutableDictionary<string,int>.Empty` 初期化を全 fixture に伝播 |

合計 想定 8 新規テストファイル + 9 既存テストファイル拡張、~50–70 新規テスト。

### 8-4. ビルド赤期間管理

破壊的変更:

1. `CombatActor` に `Statuses` フィールド追加 → 既存 fixture / Resolver / Engine.Start すべてで初期化が必要
2. `BlockPool.Consume(int)` → `Consume(int, int)` に変更 → DealDamageHelper の呼び出し 1 箇所のみ修正
3. `AttackPool.RawTotal` を public → internal → `Core.Tests` からは引き続き参照可能（既存 `InternalsVisibleTo`）、production code の resolver 側は `Display` に置換
4. `DealDamageHelper.Apply` シグネチャ変更 → 呼び出し側 PlayerAttackingResolver / EnemyAttackingResolver の同時更新

これらを 1 commit でまとめると赤期間が長くなるため、依存順を守って小刻みに進める。詳細順序は plan に記載。

### 8-5. テスト実行コマンド

- 1 ファイル単位: `dotnet test --filter FullyQualifiedName~<TestClass>`
- 全 Battle: `dotnet test --filter FullyQualifiedName~Battle`
- 全体: `dotnet build && dotnet test`

---

## 9. スコープ外（再確認）

### 9-1. Phase 10.2.B では触らない

- コンボ機構（ComboCount / Wild / SuperWild / コスト軽減 / `comboMin` per-effect）→ 10.2.C
- `SetTarget` アクション → 10.2.C
- 残り effect 8 種（heal / draw / discard / upgrade / exhaustCard / exhaustSelf / retainSelf / gainEnergy）→ 10.2.D
- 召喚 system / SummonHeld / Lifetime / PowerCards → 10.2.D
- カード移動 5 段優先順位 → 10.2.D
- 味方攻撃の inside-out 順序（複数 ally の本格対応）→ 10.2.D
- レリック 4 新 Trigger 発火 / Implemented スキップ → 10.2.E
- ポーション戦闘内発動 / `BattleOnly` 戦闘外スキップ → 10.2.E
- `BattlePlaceholder` 削除 / `RunState.ActiveBattle` 型切替 / save schema v8 → 10.5
- `BattleHub` / `BattleStateDto` / `BattleEventDto` → 10.3
- `BattleScreen.tsx` ポート → 10.4
- `StatusDefinition` の DisplayName / Description / Icon / CssClass → 10.4（UI 実装で必要になる）
- `StatusDefinition` の JSON 化 → 後続 phase（Phase 10 では C# static で固定）
- `OnTurnStart` レリック発動（spec §4-2 step 7）→ 10.2.E
- 召喚 Lifetime tick（spec §4-2 step 4）→ 10.2.D

### 9-2. Phase 10.2.B 完了後の状態

- `CombatActor` に `Statuses` フィールドが追加され、6 種の状態異常が動作
- `AttackPool` / `BlockPool` の遡及計算 API が完成
- `DealDamageHelper` が攻撃補正の中核として全 4 補正（str / weak / vuln / dex）を統合
- `EffectApplier` が `attack` / `block` / `buff` / `debuff` の 4 action を 4 scope で処理
- `TurnStartProcessor` がターン開始 tick で毒・countdown を実行し、毒死で Outcome 確定
- `PlayerAttackingResolver` が omnistrike 合算発射を処理
- `BattleEventKind` が 12 値（10.2.A の 9 + 10.2.B の 3）
- xUnit で「状態異常を含む 1 戦闘」が完走（attack / block / buff / debuff / 毒 tick / 力遡及 / 脆弱受け / omnistrike 合算 を含むエンドツーエンド）
- 既存ゲームフロー（`BattlePlaceholder` 経由）は無傷
- 親 spec が 10.2.B の決定事項に合わせて補記済み
- `phase10-2B-complete` タグ push 済み

---

## 10. 親 spec への補記事項

Phase 10.2.B の最終タスクで `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に以下を反映:

1. **§3-2 `CombatActor`**:
   - 10.2.B で `Statuses: ImmutableDictionary<string,int>` フィールドを追加
   - `GetStatus(id)` 便宜プロパティで `Statuses.GetValueOrDefault(id, 0)` を提供
   - 0 になった key は dict から削除する方針

2. **§3-3 `AttackPool` / `BlockPool`**:
   - 10.2.B で `AttackPool.Display(strength, weak)` / `AttackPool.operator +` / `BlockPool.Display(dexterity)` / `BlockPool.Consume(incomingAttack, dexterity)` を追加
   - 10.2.A の `RawTotal` は internal 化（テスト・debug 用に温存）
   - 旧 `BlockPool.Consume(int)` は削除

3. **§4-2 ターン開始処理**:
   - 10.2.B で 毒 tick / status countdown / 毒死で Outcome 確定（Victory / Defeat）を実装
   - tick 後の死亡判定は `TargetingAutoSwitch.Apply` を流用
   - `OnTurnStart` レリック発動（step 7）/ 召喚 Lifetime tick（step 4）は後続 phase
   - countdown では `ApplyStatus` event を発火しない（negative delta は意味論が違う）。RemoveStatus（0 になった瞬間）のみ発火

4. **§4-4 `DealDamage` 擬似コード**:
   - 10.2.B で `DealDamageHelper.Apply(attacker, target, baseSum, addCount, scopeNote, orderBase)` シグネチャに更新
   - 攻撃側 strength × addCount / weak（×0.75 切捨）と受け側 vulnerable（×1.5 切捨）/ dexterity（Block 表示・消費）の補正を helper 内に統合
   - `AttackFire.Amount` は攻撃側補正後・Block 適用前、`DealDamage.Amount` は最終 HP 減算量

5. **§5-1 `EffectApplier`**:
   - 10.2.B で `buff` / `debuff` action 対応（Self / Single / Random / All の 4 scope 全対応）
   - `ReplaceActor` は memory feedback の InstanceId 検索ルールに準拠（10.2.A の latent bug 根治）
   - `Self` 以外の scope で `effect.Side == null` のときは ApplyEffect 内で例外（CardEffect.Normalize は介入しない）

6. **§9-7 `BattleEventKind`**:
   - 10.2.B で `ApplyStatus = 9` / `RemoveStatus = 10` / `PoisonTick = 11` を追加（計 12 値）
   - 各 Kind のペイロード慣例は §2-6 表参照

これら 6 項目は Phase 10.2.B 内で発生した設計判断の追記。コードと spec の乖離を残さない。

---

## 11. memory feedback ルールの遵守チェックリスト

実装中・レビュー時に確認する 2 項目（`memory/feedback_battle_engine_conventions.md`）:

- [ ] `BattleOutcome` への参照はすべて `RoguelikeCardGame.Core.Battle.State.BattleOutcome` の fully qualified 表記
  - `TurnStartProcessor` 内の Outcome 確定
  - `BattleEngine.EndTurn` の上書き回避ロジック
  - 新規 `TurnStartProcessorTickTests` 等のテスト assertions
- [ ] `state.Allies` / `state.Enemies` への書き戻しは InstanceId で検索
  - `EffectApplier.ReplaceActor`（latent bug 根治）
  - `EffectApplier` の buff/debuff Random / All ループ
  - `TurnStartProcessor` の毒 tick ループ
  - `TurnStartProcessor` の status countdown ループ
  - `PlayerAttackingResolver` の omnistrike ループ
  - `PlayerAttackingResolver` の All ループ（単発 Single / Random は per-effect 単発のため既存パターン維持）

---

## 参照

- 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン spec: [`2026-04-26-phase10-2A-foundation-design.md`](2026-04-26-phase10-2A-foundation-design.md)
- 直前マイルストーン plan: [`../plans/2026-04-26-phase10-2A-foundation.md`](../plans/2026-04-26-phase10-2A-foundation.md)
- ロードマップ: [`../plans/2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md)
- memory feedback: `memory/feedback_battle_engine_conventions.md`
