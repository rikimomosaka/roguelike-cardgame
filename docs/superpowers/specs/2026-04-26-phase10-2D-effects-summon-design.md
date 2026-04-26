# Phase 10.2.D — 残り effect + 召喚 + カード移動優先順位 設計

> 作成日: 2026-04-26
> 対象フェーズ: Phase 10.2.D（Phase 10.2 サブマイルストーン 4 番目 / 全 5 段階）
> 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
> 直前マイルストーン spec: [`2026-04-26-phase10-2C-combo-target-design.md`](2026-04-26-phase10-2C-combo-target-design.md)
> 直前マイルストーン plan: [`../plans/2026-04-26-phase10-2C-combo-target.md`](../plans/2026-04-26-phase10-2C-combo-target.md)
> 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html`

## ゴール

10.2.A〜C で完成した「`BattleEngine` 5 公開 API + コンボ + 状態異常 + 対象指定」基盤に、以下 3 領域を追加する:

1. **残り effect 8 種**: `heal` / `draw` / `discard` / `upgrade` / `exhaustCard` / `exhaustSelf` / `retainSelf` / `gainEnergy`
2. **召喚 system**: `summon` action + `SummonHeld` pile + `RemainingLifetimeTurns` + `AssociatedSummonHeldInstanceId` + Lifetime tick
3. **カード移動 5 段優先順位**: `exhaustSelf` / Power / Unit / `retainSelf` / Discard + `PowerCards` pile

10.2.D 完了で「`BattleEngine` の Core ロジック完成」。10.2.E ではレリック・ポーションの戦闘内発動だけが残る。

10.2.A〜C の 5 公開 API（`Start` / `PlayCard` / `EndTurn` / `Finalize` / `SetTarget`）のシグネチャは不変。`BattleEngine.PlayCard` の card-move 末尾ロジックと `EffectApplier.Apply` の switch 文と `TurnStartProcessor.Process` の tick 順序、そして `TurnEndProcessor.Process` のシグネチャ（`DataCatalog` 引数追加）が変わる。

`BattlePlaceholder` 経由の既存ゲームフロー（敵タイル進入 → 即勝利ボタン → 報酬画面）は無傷で動作させ続ける。

## 完了判定

- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（10.2.C 完了時の Core 829 + 10.2.D 追加分）
- `BattleState` に `SummonHeld: ImmutableArray<BattleCardInstance>` / `PowerCards: ImmutableArray<BattleCardInstance>` の 2 フィールド追加
- `CombatActor` に `RemainingLifetimeTurns: int?` / `AssociatedSummonHeldInstanceId: string?` の 2 フィールド追加
- `EffectApplier.Apply` が `heal` / `draw` / `discard` / `upgrade` / `exhaustCard` / `exhaustSelf` / `retainSelf` / `gainEnergy` / `summon` の 9 action（`exhaustSelf` / `retainSelf` は event 発火だけの「マーカー effect」、`summon` も含む）を処理
- `discard` の `Scope == Single` で `InvalidOperationException`
- `upgrade` / `exhaustCard` がランダム選択（`IRng` 経由）し、Pile 不足時は存在分だけ処理
- `BattleEngine.PlayCard` 末尾のカード移動が 5 段優先順位（exhaustSelf / Power / Unit+summonSucceeded / retainSelf / Discard）
- `TurnStartProcessor.Process` が Lifetime tick を実行（countdown 後、Energy 前）
- 召喚死亡時に `SummonHeld` 内の紐付きカードが `DiscardPile` へ移動
- `TurnEndProcessor.Process` が retainSelf-aware 手札整理（`DataCatalog` 引数追加）
- `BattleEventKind` に 7 値追加（`Heal` / `Draw` / `Discard` / `Upgrade` / `Exhaust` / `GainEnergy` / `Summon`、計 19 値）
- 既存 `BattlePlaceholder` 経由のフロー無傷（手動プレイ確認）
- 親 spec §2-4 / §3-1 / §3-2 / §4-2 / §4-6 / §5-1 / §5-4 / §5-7 / §9-7 に 10.2.D で発生した設計判断を補記済み
- `phase10-2D-complete` タグが切られ origin に push 済み

---

## 1. アーキテクチャ概要

### 1-1. Phase 10.2 全体の中での位置付け

| サブ phase | 範囲 | 状態 |
|---|---|---|
| 10.2.A | 基盤 + `attack`/`block` + Phase 進行 + Victory/Defeat | ✅ 完了 |
| 10.2.B | 状態異常 6 種 + 遡及計算 + buff/debuff + tick + omnistrike | ✅ 完了 |
| 10.2.C | コンボ + `SetTarget` + comboMin filter | ✅ 完了 |
| **10.2.D**（本 spec） | **8 effect actions + 召喚 system + カード移動 5 段優先順位** | 本フェーズ |
| 10.2.E | レリック 4 新 Trigger + Implemented スキップ + UsePotion + BattleOnly | 後続 |

10.2.D は **Core ロジックの完成**。10.2.E は外部統合（レリック・ポーションのトリガー連携）のみ。

### 1-2. 共存戦略

10.2.A〜C と同じ。新 `BattleEngine` は **pure Core API として独立**し、`NodeEffectResolver` は引き続き `BattlePlaceholder.Start` を呼ぶ（既存ゲームフローは無傷）。`BattleEngine` は xUnit でしかテストされない。

### 1-3. ファイル構成（10.2.D 完了時の差分）

```
src/Core/Battle/
├── State/
│   ├── BattleState.cs              [修正] +SummonHeld / PowerCards
│   └── CombatActor.cs              [修正] +RemainingLifetimeTurns / AssociatedSummonHeldInstanceId
├── Engine/
│   ├── BattleEngine.cs             [修正] Start で SummonHeld/PowerCards 初期化、CombatActor 2 新フィールド初期化
│   ├── BattleEngine.PlayCard.cs    [修正] card-move 5 段優先順位 + summonSucceeded フラグ追跡
│   ├── BattleEngine.EndTurn.cs     [修正] TurnEndProcessor.Process に catalog 渡す
│   ├── EffectApplier.cs            [修正] +heal/draw/discard/upgrade/exhaustCard/exhaustSelf/retainSelf/gainEnergy/summon
│   ├── TurnStartProcessor.cs       [修正] +Lifetime tick（countdown 後、Energy 前）
│   ├── TurnEndProcessor.cs         [修正] retainSelf-aware 手札整理（catalog 引数追加）
│   ├── PlayerAttackingResolver.cs  [修正] 召喚クリーンアップ呼出
│   ├── EnemyAttackingResolver.cs   [修正] 同上
│   └── SummonCleanup.cs            [新] 死亡 summon の SummonHeld → Discard helper
└── Events/
    └── BattleEventKind.cs          [修正] +Heal / Draw / Discard / Upgrade / Exhaust / GainEnergy / Summon

tests/Core.Tests/Battle/
├── State/
│   ├── BattleStateInvariantTests.cs       [修正] +SummonHeld / PowerCards 不変条件
│   └── CombatActorTests.cs                [修正] +Lifetime / AssociatedSummonHeldInstanceId
├── Engine/
│   ├── EffectApplierHealTests.cs                       [新]
│   ├── EffectApplierDrawTests.cs                       [新]
│   ├── EffectApplierDiscardTests.cs                    [新]
│   ├── EffectApplierUpgradeTests.cs                    [新]
│   ├── EffectApplierExhaustCardTests.cs                [新]
│   ├── EffectApplierExhaustSelfRetainSelfTests.cs      [新]
│   ├── EffectApplierGainEnergyTests.cs                 [新]
│   ├── EffectApplierSummonTests.cs                     [新]
│   ├── BattleEnginePlayCardCardMovementTests.cs        [新] 5 段優先順位
│   ├── TurnStartProcessorLifetimeTests.cs              [新] Lifetime tick + 死亡 → SummonHeld → Discard
│   ├── TurnEndProcessorRetainSelfTests.cs              [新] retainSelf-aware 手札整理
│   ├── SummonCleanupTests.cs                           [新] 共通 helper
│   ├── BattleEngineEndTurnTests.cs                     [修正] catalog 引数追加追従
│   ├── PlayerAttackingResolverTests.cs                 [修正] 召喚死亡 SummonHeld → Discard
│   ├── EnemyAttackingResolverTests.cs                  [修正] 同上
│   ├── BattleDeterminismTests.cs                       [修正] 召喚 + heal/draw 含む 1 戦闘 seed 一致
│   └── (既存 fixture / test の new BattleState() 呼出全箇所 → +SummonHeld/PowerCards 追従)
└── Fixtures/
    └── BattleFixtures.cs                               [修正] UnitDefinition factory + summon 用 fixture
```

### 1-4. namespace

10.2.D で新 namespace は追加しない。`SummonCleanup.cs` は既存 `RoguelikeCardGame.Core.Battle.Engine` の internal static helper。

### 1-5. memory feedback の遵守（Phase 10.2 系列で再徹底）

`memory/feedback_battle_engine_conventions.md` の 2 ルール:

1. **`BattleOutcome` は常に fully qualified**: 召喚死亡で全敵 / 全味方死亡が起こる経路（Lifetime tick）→ Outcome 確定箇所すべてで `RoguelikeCardGame.Core.Battle.State.BattleOutcome.X`
2. **`state.Allies` / `state.Enemies` への書き戻しは InstanceId 検索**: 10.2.D で新規発生する loop:
   - `EffectApplier` の `heal` All / Random scope（複数 actor の HP 更新）
   - `EffectApplier` の `summon` action（Allies に append、その後の effect で hero を再 fetch）
   - `TurnStartProcessor` の Lifetime tick loop
   - `SummonCleanup.Apply` のクリーンアップ loop
   - 既存 `caster = s.Allies[0]` 再 fetch パターン（PlayCard 内）は維持

---

## 2. データモデル

### 2-1. `BattleState` の 2 フィールド追加

```csharp
public sealed record BattleState(
    int Turn,
    BattlePhase Phase,
    BattleOutcome Outcome,
    ImmutableArray<CombatActor> Allies,
    ImmutableArray<CombatActor> Enemies,
    int? TargetAllyIndex,
    int? TargetEnemyIndex,
    int Energy,
    int EnergyMax,
    ImmutableArray<BattleCardInstance> DrawPile,
    ImmutableArray<BattleCardInstance> Hand,
    ImmutableArray<BattleCardInstance> DiscardPile,
    ImmutableArray<BattleCardInstance> ExhaustPile,
    ImmutableArray<BattleCardInstance> SummonHeld,    // ← 10.2.D 追加: 召喚カード待機所
    ImmutableArray<BattleCardInstance> PowerCards,    // ← 10.2.D 追加: Power 常駐エリア（field のみ、効果 Phase 11+）
    int ComboCount,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    string EncounterId);
```

配置: `ExhaustPile` の直後、`ComboCount` の前。これで親 spec §3-1 の最終形フィールド順に揃う。

### 2-2. `CombatActor` の 2 フィールド追加

```csharp
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
    ImmutableDictionary<string, int> Statuses,
    string? CurrentMoveId,
    int? RemainingLifetimeTurns,                      // ← 10.2.D 追加
    string? AssociatedSummonHeldInstanceId)           // ← 10.2.D 追加
{
    public bool IsAlive => CurrentHp > 0;
    public int GetStatus(string id) => Statuses.TryGetValue(id, out var v) ? v : 0;
}
```

#### `RemainingLifetimeTurns: int?`

- `null` = 永続（hero、永続召喚）
- `N > 0` = N ターン後に消滅（`UnitDefinition.LifetimeTurns` から複製）
- `0` = `TurnStartProcessor` の Lifetime tick で 0 になった瞬間に「死亡」扱い（HP=0 化 + ActorDeath event）

#### `AssociatedSummonHeldInstanceId: string?` — Q2 (b) 確定

親 spec §3-2 では `int? AssociatedSummonHeldIndex` だが、`SummonHeld` 配列の要素削除で index がずれる latent bug を避けるため、**`BattleCardInstance.InstanceId` で紐付ける**。memory feedback ルール「InstanceId 検索」を spec レベルで踏襲する形。親 spec 補記で訂正。

- `null` = hero / 紐付くカードなし（Power/exhaust/retain 経路で召喚成功した稀ケース等）
- `string` = `SummonHeld` 内のカードの `BattleCardInstance.InstanceId`

### 2-3. `BattleEventKind` の 7 値追加

```csharp
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
    ApplyStatus   = 9,
    RemoveStatus  = 10,
    PoisonTick    = 11,
    Heal          = 12,    // ← 10.2.D
    Draw          = 13,    // ← 10.2.D
    Discard       = 14,    // ← 10.2.D
    Upgrade       = 15,    // ← 10.2.D
    Exhaust       = 16,    // ← 10.2.D（exhaustCard / exhaustSelf 共通）
    GainEnergy    = 17,    // ← 10.2.D
    Summon        = 18,    // ← 10.2.D
}
```

#### Event ペイロード慣例

| Kind | Caster | Target | Amount | Note |
|---|---|---|---|---|
| `Heal` | 効果発動主体 | 回復対象 actor | 回復量（実際に増えた HP、最大値超過分は除外） | null |
| `Draw` | 主人公 | null | ドロー枚数（実際に引けた枚数） | null |
| `Discard` | 主人公 | null | 捨て枚数（実際に捨てた枚数） | "random" / "all" |
| `Upgrade` | 主人公 | null | 強化された枚数（pile 不足や全部強化済みなら 0） | pile 名 ("hand" / "discard" / "draw") |
| `Exhaust` | 主人公 | null | exhaust された枚数 | "self" / pile 名 |
| `GainEnergy` | 主人公 | null | 増えた量 | null |
| `Summon` | 主人公 | 新召喚 actor の InstanceId | null | UnitId（"shield_minion" 等） |

`retainSelf` は event 発火しない（カードが「動かない」だけ、ゲーム上の演出不要）。

### 2-4. `CardEffect` の意味再確認

10.2.D で `CardEffect` 既存型は **不変**。新 action の意味:

| Action | Scope | Side | Amount | Name | UnitId | Pile |
|---|---|---|---|---|---|---|
| `heal` | Self / Single / All / Random | Ally （Single/All/Random） | 回復量 | — | — | — |
| `draw` | Self（caster=hero） | null | ドロー枚数 | — | — | — |
| `discard` | Random / All（Single 不可） | null | 捨て枚数 | — | — | — |
| `upgrade` | — | — | 強化枚数 | — | — | "hand" / "discard" / "draw" 必須 |
| `exhaustCard` | — | — | exhaust 枚数 | — | — | "hand" / "discard" / "draw" 必須 |
| `exhaustSelf` | — | — | — | — | — | — |
| `retainSelf` | — | — | — | — | — | — |
| `gainEnergy` | Self | null | エナジー増分 | — | — | — |
| `summon` | Self | null | — | — | "shield_minion" 等 必須 | — |

`exhaustSelf` / `retainSelf` は **マーカー effect** として `EffectApplier` 内では event 発火（`Exhaust` for exhaustSelf）または no-op（retainSelf）。実際のカード移動先決定は `BattleEngine.PlayCard` 末尾の card-move logic が effects 配列を走査して判定。

---

## 3. `EffectApplier` の 9 action 追加

### 3-1. action ディスパッチ拡張

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng,
    DataCatalog catalog)
{
    return effect.Action switch
    {
        "attack"      => ApplyAttack(state, caster, effect),                     // 既存
        "block"       => ApplyBlock(state, caster, effect),                       // 既存
        "buff"        => ApplyStatusChange(state, caster, effect, rng),          // 既存
        "debuff"      => ApplyStatusChange(state, caster, effect, rng),          // 既存
        "heal"        => ApplyHeal(state, caster, effect, rng),                  // 10.2.D
        "draw"        => ApplyDraw(state, caster, effect, rng),                  // 10.2.D
        "discard"     => ApplyDiscard(state, caster, effect, rng),               // 10.2.D
        "upgrade"     => ApplyUpgrade(state, caster, effect, rng, catalog),      // 10.2.D
        "exhaustCard" => ApplyExhaustCard(state, caster, effect, rng),           // 10.2.D
        "exhaustSelf" => ApplyExhaustSelf(state, caster, effect),                // 10.2.D マーカー
        "retainSelf"  => (state, Array.Empty<BattleEvent>()),                    // 10.2.D no-op
        "gainEnergy"  => ApplyGainEnergy(state, caster, effect),                 // 10.2.D
        "summon"      => ApplySummon(state, caster, effect, catalog),            // 10.2.D
        _             => (state, Array.Empty<BattleEvent>()),
    };
}
```

`Apply` のシグネチャに **`DataCatalog catalog` を追加**（`upgrade` / `summon` で必要）。既存呼出側（`BattleEngine.PlayCard` / `EnemyAttackingResolver` / `TurnStartProcessor` 等）に catalog を渡す経路を追加する。

### 3-2. `heal` 詳細

- `Self`: caster の HP +Amount（cap MaxHp）
- `Single Ally`: `state.TargetAllyIndex` の actor、生存中のみ。死亡 actor は no-op
- `Random Ally`: 生存 ally から `IRng` でランダム 1 体、なければ no-op
- `All Ally`: 生存 ally 全員
- `Side != Ally && Scope != Self` で `InvalidOperationException`（heal はプレイヤー側のみ）

実 HP 増分（`min(Amount, MaxHp - CurrentHp)`）が 0 のとき event 発火しない（既に MaxHp）。

### 3-3. `draw` 詳細

- `Scope == Self` のみ（他は `InvalidOperationException`）
- 主人公の Hand に `DrawPile` から最大 `Amount` 枚追加
- 山札不足時は `DiscardPile` をシャッフルして `DrawPile` に投入（既存 `TurnStartProcessor.DrawCards` ロジックを流用 / 共通化）
- ハンド上限 10 で打ち切り
- 実ドロー枚数を `Draw` event の `Amount` に設定（0 なら event 発火しない）

`TurnStartProcessor.DrawCards` を `internal static` にして共通化、または `EffectApplier` 内で複製。実装時判断（spec §「実装上の判断」参照）。

### 3-4. `discard` 詳細 — Q3 (a)

- `Scope == Single` で **`InvalidOperationException`**（UI 連携待ち）
- `Scope == Random`: 主人公の Hand から `IRng` でランダム `Amount` 枚を `DiscardPile` へ
- `Scope == All`: Hand 全捨て
- Hand 不足時は存在分だけ
- 実捨て枚数を `Discard` event の `Amount` に、`Note` に "random" / "all" を設定

### 3-5. `upgrade` 詳細 — Q4 (a)

- `effect.Pile` で対象 pile を判定: `"hand"` / `"discard"` / `"draw"`、それ以外は `InvalidOperationException`
- 対象 pile から `IRng` でランダム選択
- 強化済み (`IsUpgraded == true`) は **スキップして次候補抽選**
- 「強化可能な候補」(IsUpgraded=false かつ `def.UpgradedCost` または `def.UpgradedEffects` が non-null) のみが対象 — `def.IsUpgradable` プロパティ参照
- Pile 内の強化候補が `Amount` 未満なら、存在分だけ強化
- 選ばれたカードを `IsUpgraded = true` に置換
- 実強化枚数を `Upgrade` event の `Amount` に、`Note` に pile 名を設定

### 3-6. `exhaustCard` 詳細

- `effect.Pile` で対象 pile を判定（`upgrade` と同じ検証）
- ランダム `Amount` 枚を `ExhaustPile` へ
- Pile 不足時は存在分だけ
- 実 exhaust 枚数を `Exhaust` event の `Amount` に、`Note` に pile 名を設定

### 3-7. `exhaustSelf` / `retainSelf` 詳細

- `exhaustSelf`: `Exhaust` event を発火（`Note: "self"`、`Amount: 1`）するだけ。実際の移動は `BattleEngine.PlayCard` 末尾の card-move logic が判定
- `retainSelf`: event 発火しない、no-op（プレイヤーへの演出は不要、カードが手札に残ることは UI 側で明らか）

### 3-8. `gainEnergy` 詳細

- `Scope == Self` のみ
- `state.Energy += Amount`（**上限なし**、Phase 10 では超過可）
- `GainEnergy` event 発火

### 3-9. `summon` 詳細

```
ApplySummon(state, caster, effect, catalog):
  unitId = effect.UnitId
  if (unitId is null) throw InvalidOperationException
  if (!catalog.TryGetUnit(unitId, out var unitDef)) throw InvalidOperationException
  
  // 空き slot 検索（hero=0 を除く 1〜3）
  occupiedSlots = state.Allies.Select(a => a.SlotIndex).ToHashSet()
  emptySlot = Enumerable.Range(1, 3).FirstOrDefault(i => !occupiedSlots.Contains(i), -1)
  if (emptySlot == -1) {
    // 不発（silent skip、event なし、後続 effect は処理続行）
    return (state, Array.Empty<BattleEvent>())
  }
  
  // 新 CombatActor 生成（InstanceId は state.Turn と既存 actor 数からユニーク文字列）
  newInstanceId = $"summon_inst_{state.Turn}_{state.Allies.Length}"
  newActor = new CombatActor(
    InstanceId: newInstanceId,
    DefinitionId: unitId,
    Side: ActorSide.Ally,
    SlotIndex: emptySlot,
    CurrentHp: unitDef.Hp,
    MaxHp: unitDef.Hp,
    Block: BlockPool.Empty,
    AttackSingle: AttackPool.Empty, AttackRandom: AttackPool.Empty, AttackAll: AttackPool.Empty,
    Statuses: ImmutableDictionary<string, int>.Empty,
    CurrentMoveId: unitDef.InitialMoveId,
    RemainingLifetimeTurns: unitDef.LifetimeTurns,
    AssociatedSummonHeldInstanceId: null  // PlayCard の card-move logic で設定
  )
  
  newAllies = state.Allies.Add(newActor)
  newState = state with { Allies = newAllies }
  
  // Summon event 発火
  evs = [Summon { CasterInstanceId=caster.InstanceId, TargetInstanceId=newInstanceId, Note=unitId }]
  
  return (newState, evs)
```

`PlayCard` 経路では、後続 `caster = s.Allies[0]` の再 fetch（10.2.A/B/C 既存）で hero 自身が caster として継続されるため、`summon` 後の `attack` 等の effect は **hero の AttackPool に加算**される（spec §5-2 通り）。

#### 「summon 成功」フラグの追跡

`BattleEngine.PlayCard` の effect ループで、`summon` action が成功したかどうかを bool で追跡する:

```csharp
bool summonSucceeded = false;
foreach (var eff in effects)
{
    if (eff.ComboMin is { } min && newCombo < min) continue;
    
    var beforeAlliesLength = s.Allies.Length;
    var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
    s = afterEffect;
    foreach (var ev in evs) { events.Add(ev with { Order = order }); order++; }
    caster = s.Allies[0];
    
    if (eff.Action == "summon" && s.Allies.Length > beforeAlliesLength)
        summonSucceeded = true;
}
```

`summonSucceeded == true` のとき、card-move logic は `def.CardType == Unit` のときカードを `SummonHeld` へ移動 + 召喚 actor の `AssociatedSummonHeldInstanceId` を設定（§5）。

---

## 4. 召喚 system 詳細

### 4-1. `UnitDefinition` 既存 record（不変）

```csharp
public sealed record UnitDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves,
    int? LifetimeTurns = null)
    : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);
```

10.2.D で `UnitDefinition` 自体は変更しない。`DataCatalog.TryGetUnit(id)` 経由で参照。

### 4-2. 召喚キャラの行動（10.2.D 範囲）

10.2.D では召喚キャラは「**壁役**」として登場する:

- 召喚キャラの `AttackSingle` / `Random` / `All` は常に Empty（誰も AttackPool に加算しない）
- `PlayerAttackingResolver` で SlotIndex 順に Allies を loop するが、召喚キャラは Pool が空 → 何も発射しない
- 状態異常を受ける（poison / vulnerable / weak 等）、Block を持つ、HP がある
- 攻撃を受ける（敵の `Single` 攻撃の対象になりうる、`Random` 攻撃でランダム対象になりうる、`All` 攻撃で全員に着弾）

「召喚自身が `attack` effect を持つカードのように発射する」は **Phase 11+** に持ち越し。10.2.D では `CurrentMoveId` を保持するだけで使わない。

### 4-3. Lifetime tick（`TurnStartProcessor` step 4）

`countdown` step（既存）の後、`Energy` step（既存）の前に Lifetime tick を挿入:

```
TurnStartProcessor.Process flow:
1. Turn+1                                       （既存）
2. Poison tick                                  （既存 10.2.B）
3. Death detection / TargetingAutoSwitch / Outcome 確定  （既存 10.2.B）
4. Status countdown                             （既存 10.2.B）
5. Lifetime tick + Lifetime 死後の SummonCleanup  ← 10.2.D 追加（5-1〜5-3 の sub-step）
6. Energy = EnergyMax                           （既存）
7. Draw                                         （既存）
8. TurnStart event                              （既存）
```

§4-2 spec の step 番号と若干ずれるが、本 sub-spec 内の処理流れの説明として表記。

#### Lifetime tick 詳細

```
ApplyLifetimeTick(state):
  s = state
  lifetimeAllyIds = s.Allies
    .Where(a => a.Side == ActorSide.Ally && a.RemainingLifetimeTurns is not null && a.IsAlive)
    .OrderBy(a => a.SlotIndex)
    .Select(a => a.InstanceId)
    .ToList()
  
  foreach allyId in lifetimeAllyIds:
    actor = FindActor(s, allyId)
    if (actor is null || !actor.IsAlive) continue
    if (actor.RemainingLifetimeTurns is null) continue
    
    newRemaining = actor.RemainingLifetimeTurns.Value - 1
    
    if (newRemaining <= 0):
      // 死亡
      diedActor = actor with { RemainingLifetimeTurns = newRemaining, CurrentHp = 0 }
      s = ReplaceActor(s, allyId, diedActor)
      events.Add(ActorDeath { TargetInstanceId=allyId, Note="lifetime" })
      // SummonHeld → Discard 移動は SummonCleanup.Apply で
    else:
      s = ReplaceActor(s, allyId, actor with { RemainingLifetimeTurns = newRemaining })
  
  // SummonCleanup の呼出
  s = SummonCleanup.Apply(s, events, ref order)
  
  // Outcome 確定（全味方死亡で Defeat、ただし hero は Lifetime null なので tick されない → Defeat はあり得ない）
  // 全敵死亡もここでは確定しない（Lifetime tick は ally のみ）
  return s
```

InstanceId snapshot pattern を厳守（memory feedback ルール 2）。

### 4-4. `SummonCleanup.Apply` 共通 helper

死亡経路（PlayerAttacking 着弾 / EnemyAttacking 着弾 / poison tick / Lifetime tick）の各処理後に呼ばれる:

```csharp
internal static class SummonCleanup
{
    /// <summary>
    /// state.Allies 内の死亡した summon actor の AssociatedSummonHeldInstanceId を辿り、
    /// SummonHeld 内の対応カードを DiscardPile に移動する。
    /// </summary>
    public static BattleState Apply(
        BattleState state, List<BattleEvent> events, ref int order)
    {
        var s = state;
        // 死亡した summon を抽出（hero=null associated は除外）
        var deadSummonIds = s.Allies
            .Where(a => a.Side == ActorSide.Ally
                     && a.DefinitionId != "hero"
                     && !a.IsAlive
                     && a.AssociatedSummonHeldInstanceId is not null)
            .Select(a => (a.InstanceId, a.AssociatedSummonHeldInstanceId!))
            .ToList();
        
        foreach (var (allyId, cardInstId) in deadSummonIds)
        {
            // SummonHeld から該当カードを抽出
            int idx = -1;
            for (int i = 0; i < s.SummonHeld.Length; i++)
                if (s.SummonHeld[i].InstanceId == cardInstId) { idx = i; break; }
            if (idx < 0) continue; // 既にクリーンアップ済み
            
            var card = s.SummonHeld[idx];
            s = s with
            {
                SummonHeld = s.SummonHeld.RemoveAt(idx),
                DiscardPile = s.DiscardPile.Add(card),
            };
            
            // ally の AssociatedSummonHeldInstanceId を null に（再処理防止）
            int allyIdx = -1;
            for (int i = 0; i < s.Allies.Length; i++)
                if (s.Allies[i].InstanceId == allyId) { allyIdx = i; break; }
            if (allyIdx >= 0)
            {
                var actor = s.Allies[allyIdx];
                s = s with { Allies = s.Allies.SetItem(
                    allyIdx, actor with { AssociatedSummonHeldInstanceId = null }) };
            }
            // event は発火しない（カード移動だけ、UI は state diff から認識）
        }
        return s;
    }
}
```

呼出箇所:
- `PlayerAttackingResolver.Resolve` の戻り直前
- `EnemyAttackingResolver.Resolve` の戻り直前
- `TurnStartProcessor.Process` の poison tick 後 / Lifetime tick 後

### 4-5. 死亡 summon の Allies 配列残留

仕様: **死亡した summon は Allies 配列に dead 状態で残る**（HP=0、IsAlive=false）。

理由:
- Allies 配列の index 安定性（既存 InstanceId 検索と整合）
- 死亡した summon の SlotIndex は **新召喚で再利用しない**（Phase 10.2.D 範囲ではこの単純化を許容）
- Phase 11+ で「死亡 summon の slot 再利用」「複数戦闘間の summon 永続化」等が必要になれば別途設計

これにより `Allies.Length` は **「これまで存在した summon の総数 + 1（hero）」** になりうる。Phase 10.2.D の召喚カードは少数 + Lifetime ありが想定なので問題ない。

---

## 5. カード移動 5 段優先順位（`BattleEngine.PlayCard` 末尾の更新）

### 5-1. 現在の logic（10.2.C）

```csharp
var newHand = s.Hand.RemoveAt(handIndex);
var newDiscard = s.DiscardPile.Add(card);
s = s with { Hand = newHand, DiscardPile = newDiscard };
```

### 5-2. 10.2.D の 5 段優先順位

```csharp
// effects 解決後、card-move logic
var effectsList = (card.IsUpgraded && def.UpgradedEffects is not null)
    ? def.UpgradedEffects : def.Effects;
bool hasExhaustSelf = effectsList.Any(e => e.Action == "exhaustSelf");
bool hasRetainSelf = effectsList.Any(e => e.Action == "retainSelf");
bool isPower = def.CardType == CardType.Power;
bool isUnit = def.CardType == CardType.Unit;

s = s with { Hand = s.Hand.RemoveAt(handIndex) };

if (hasExhaustSelf)
{
    s = s with { ExhaustPile = s.ExhaustPile.Add(card) };
}
else if (isPower)
{
    s = s with { PowerCards = s.PowerCards.Add(card) };
}
else if (isUnit && summonSucceeded)
{
    s = s with { SummonHeld = s.SummonHeld.Add(card) };
    // 直前に追加された召喚 actor の AssociatedSummonHeldInstanceId を card.InstanceId に設定
    var lastSummonIdx = s.Allies.Length - 1;
    if (lastSummonIdx >= 0
        && s.Allies[lastSummonIdx].DefinitionId != "hero"
        && s.Allies[lastSummonIdx].AssociatedSummonHeldInstanceId is null)
    {
        var summonActor = s.Allies[lastSummonIdx];
        s = s with { Allies = s.Allies.SetItem(
            lastSummonIdx,
            summonActor with { AssociatedSummonHeldInstanceId = card.InstanceId }) };
    }
}
else if (hasRetainSelf)
{
    s = s with { Hand = s.Hand.Insert(handIndex, card) };
}
else
{
    s = s with { DiscardPile = s.DiscardPile.Add(card) };
}
```

### 5-3. 設計上の判断

#### 5-3-1. 優先順位の根拠

spec §5-7 通り。`exhaustSelf` 最優先（除外は最強制力）→ Power（CardType 専用エリア）→ Unit + summon 成功（召喚紐付け）→ retainSelf（次ターン以降のため hand）→ Discard（既定）。

#### 5-3-2. `isUnit && summonSucceeded` の判定

`summonSucceeded` フラグは effect ループ中に追跡（§3-9）。`Unit` カードでも `summon` 失敗（slot 満杯）の場合、step 3 をスキップして step 4/5 へ進む。例:
- Unit カード（retainSelf なし）が summon 失敗 → DiscardPile（step 5）
- Unit カードに retainSelf 付き → summon 失敗時は Hand に残る（step 4）

#### 5-3-3. 召喚 actor の `AssociatedSummonHeldInstanceId` 設定タイミング

`§3-9 ApplySummon` では `null` で actor を生成（card.InstanceId は EffectApplier が知らない）。`BattleEngine.PlayCard` の card-move logic で「直前に追加された召喚 actor」を `s.Allies[s.Allies.Length - 1]` で参照し、`AssociatedSummonHeldInstanceId = card.InstanceId` に更新する。

ただし「直前」の判定は脆弱（複数 summon を 1 カードで連続発動した場合、最後の 1 体しか紐付けされない）。Phase 10.2.D 範囲では「**1 カード = 1 summon effect = 1 召喚 actor**」を前提とする（複数 summon の連発カードは Phase 10 範囲外）。

#### 5-3-4. retainSelf カードの Hand 内位置

カードを `Hand.RemoveAt(handIndex)` した後、step 4 で `Hand.Insert(handIndex, card)` で同位置に戻す。新規ドローが他カードを Hand 末尾に追加しても、retainSelf カードの位置は変わらない。

---

## 6. `TurnEndProcessor` の retainSelf-aware 手札整理

### 6-1. シグネチャ変更

```csharp
// 既存（10.2.C）
public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state)

// 10.2.D
public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, DataCatalog catalog)
```

呼出側 `BattleEngine.EndTurn` で `catalog` を渡す（既存引数として `DataCatalog` を受け取っているので問題なし）。

### 6-2. 処理フロー

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, DataCatalog catalog)
{
    // 既存: Block / AttackPool リセット
    var allies = state.Allies.Select(ResetActor).ToImmutableArray();
    var enemies = state.Enemies.Select(ResetActor).ToImmutableArray();
    
    // 10.2.D: retainSelf-aware 手札整理
    var keepInHand = ImmutableArray.CreateBuilder<BattleCardInstance>();
    var newDiscardBuilder = state.DiscardPile.ToBuilder();
    
    foreach (var card in state.Hand)
    {
        if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
        {
            // 未知カード（実装上ありえない）→ 安全側で Discard
            newDiscardBuilder.Add(card);
            continue;
        }
        var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
            ? def.UpgradedEffects : def.Effects;
        
        if (effects.Any(e => e.Action == "retainSelf"))
            keepInHand.Add(card);
        else
            newDiscardBuilder.Add(card);
    }
    
    var next = state with
    {
        Allies = allies,
        Enemies = enemies,
        Hand = keepInHand.ToImmutable(),                                         // ← retainSelf カードのみ残る
        DiscardPile = newDiscardBuilder.ToImmutable(),
        ComboCount = 0,                       // 10.2.C
        LastPlayedOrigCost = null,            // 10.2.C
        NextCardComboFreePass = false,        // 10.2.C
    };
    return (next, Array.Empty<BattleEvent>());
}
```

### 6-3. retainSelf カード on PlayCard との関係

`BattleEngine.PlayCard` の card-move logic（§5-2）でも retainSelf カードは Hand に残る。`TurnEndProcessor` の処理は「次ターンへの引き継ぎ」を保証する。

両者の整合性: PlayCard 後 hand 内に retainSelf カードがあり、ターン終了時もそのカードは Hand に残る → ユーザーは次ターンに同じカードを再プレイできる（但し Cost 払えれば）。

---

## 7. 不変条件（10.2.D 追加分）

10.2.A〜C 完了時の不変条件に加えて:

- `BattleState.Allies` の各 actor で:
  - `DefinitionId == "hero"` ⇒ `RemainingLifetimeTurns is null && AssociatedSummonHeldInstanceId is null && SlotIndex == 0`
  - `DefinitionId != "hero" && AssociatedSummonHeldInstanceId is { } cid` ⇒ `cid == ` 何らかの存在する `BattleCardInstance.InstanceId`（SummonHeld 内 or DiscardPile 内、cleanup 後は DiscardPile）
- `Allies` の `SlotIndex` 値は重複なし（hero=0、summon は 1-3 の重複なしユニーク）
- `Allies.Length <= 4`
- `SummonHeld` の各カードに対して、`Allies` 内に `AssociatedSummonHeldInstanceId == card.InstanceId` の actor が **0 または 1 体** 存在（cleanup 完了状態では「対応 ally がまだ生存中」のみ）
- `PowerCards` への入退記録: PlayCard 経路でのみ追加、`TurnEndProcessor` / `TurnStartProcessor` は触らない
- カード総数不変条件: `DrawPile.Length + Hand.Length + DiscardPile.Length + ExhaustPile.Length + SummonHeld.Length + PowerCards.Length == 戦闘開始時のラン側 Deck 枚数`（召喚 actor はカードでないので別枠）

`BattleStateInvariantTests` に追加項目として組み込む（実用的な範囲、§ ` `Allies.Length <= 4`の確認 / hero の Lifetime null など）。

---

## 8. テスト戦略

### 8-1. テスト粒度

10.2.A〜C と同じ TDD 1 サイクル（失敗 → 実装 → 緑 → commit）。subagent-driven-development。

### 8-2. 新規テストファイル（13 ファイル想定）

| ファイル | カバレッジ |
|---|---|
| `Engine/EffectApplierHealTests.cs` | Self / Single / All / Random / dead skip / cap MaxHp / event 発火条件 |
| `Engine/EffectApplierDrawTests.cs` | Self only / 山札不足→シャッフル / ハンド上限打切 / 完全空ならドロー停止 |
| `Engine/EffectApplierDiscardTests.cs` | Random / All / Single throws / Hand 不足は存在分 / 0 枚で event なし |
| `Engine/EffectApplierUpgradeTests.cs` | hand/discard/draw pile 別 / IsUpgraded skip / IsUpgradable=false skip / 不足は存在分 / 不正 Pile throws |
| `Engine/EffectApplierExhaustCardTests.cs` | hand/discard/draw / 不足は存在分 / 不正 Pile throws |
| `Engine/EffectApplierExhaustSelfRetainSelfTests.cs` | Exhaust event 発火 / retainSelf no-op / event 順序 |
| `Engine/EffectApplierGainEnergyTests.cs` | Self / Energy +Amount / 上限なし（EnergyMax 超過 OK）|
| `Engine/EffectApplierSummonTests.cs` | 空き slot ありで成功 / 満杯で不発 / Allies に追加 / Summon event / UnitId null throws |
| `Engine/BattleEnginePlayCardCardMovementTests.cs` | 5 段優先順位（exhaustSelf / Power / Unit成功 / retainSelf / Discard） + Unit 失敗時の retainSelf 経路 / Unit + exhaustSelf の優先順位 |
| `Engine/TurnStartProcessorLifetimeTests.cs` | LifetimeTurns null skip / N -1 → 0 で死亡 / SummonHeld → Discard / 死亡で ActorDeath event / 順序（countdown→Lifetime→Energy） |
| `Engine/TurnEndProcessorRetainSelfTests.cs` | retainSelf カードのみ Hand 残 / それ以外 Discard / catalog 引数 / コンボリセットも維持 |
| `Engine/SummonCleanupTests.cs` | 死亡 ally 検出 / SummonHeld → Discard / AssociatedSummonHeldInstanceId null 化 / 既クリーン済みは no-op |
| `Engine/BattleEnginePlayCardSummonIntegrationTests.cs` | summon カードプレイ → Allies 増 / SummonHeld 追加 / AssociatedSummonHeldInstanceId 設定 / 連続プレイで slot 進行 |

### 8-3. 既存テスト拡張

| ファイル | 変更 |
|---|---|
| `State/BattleStateInvariantTests.cs` | +SummonHeld / PowerCards 不変条件 / Allies.Length<=4 |
| `State/CombatActorTests.cs` | +Lifetime / AssociatedSummonHeldInstanceId フィールド record 等価 |
| `Events/BattleEventKindTests.cs` | 19 値の整数値検証 |
| `Engine/BattleEnginePlayCardTests.cs` | 既存 fixture の `new BattleState(...)` に SummonHeld/PowerCards 追加 |
| `Engine/BattleEngineEndTurnTests.cs` | catalog 引数追加 / TurnEnd 後の retainSelf カード維持 assertion |
| `Engine/PlayerAttackingResolverTests.cs` | 召喚死亡 → SummonHeld → Discard / hero attack のみ既存挙動 |
| `Engine/EnemyAttackingResolverTests.cs` | 同上 |
| `Engine/TurnStartProcessorTickTests.cs` | tick 順序確認（poison → countdown → Lifetime → Energy → Draw）|
| `Engine/TurnEndProcessorTests.cs` | catalog 引数追加 / 既存挙動維持 |
| `Engine/TurnEndProcessorComboResetTests.cs` | catalog 引数追加 |
| `Engine/EffectApplierTests.cs` / `EffectApplier*Tests.cs` 全部 | catalog 引数追加 |
| `Engine/BattleEngineStartTests.cs` | SummonHeld / PowerCards 初期空 / hero の Lifetime null 確認 |
| `Engine/BattleDeterminismTests.cs` | 召喚 + heal/draw を含む 1 戦闘で seed 一致 |
| `Fixtures/BattleFixtures.cs` | UnitDefinition factory + summon 用 catalog 拡張 + 全 BattleState 生成箇所に 2 新フィールド伝播 |

合計 想定 13 新規テストファイル + 14 既存テストファイル拡張、~80-120 新規テスト。

### 8-4. ビルド赤期間管理

破壊的変更が複数同時:

1. `BattleState` に 2 フィールド追加 → 全 `new BattleState(...)` 呼出（10.2.C で 17 + 新規 = 多数）がコンパイルエラー
2. `CombatActor` に 2 フィールド追加 → 全 `new CombatActor(...)` 呼出 + `BattleFixtures.Hero()` / `Goblin()` がコンパイルエラー
3. `EffectApplier.Apply` に `DataCatalog catalog` 引数追加 → 全呼出側（既存 10 箇所程度）がコンパイルエラー
4. `TurnEndProcessor.Process` に `DataCatalog catalog` 引数追加 → 呼出側（`BattleEngine.EndTurn` 1 箇所 + テスト多数）がコンパイルエラー

これらを 1 commit でまとめると赤期間が長すぎ、依存順を守って小刻みに進める:

1. Task A: `BattleState` 2 フィールド追加 + `BattleEngine.Start` / `BattleEngine.PlayCard` の card-move logic 暫定（5 段優先順位は後）+ 全 fixture 追従 → 1 commit
2. Task B: `CombatActor` 2 フィールド追加 + Hero/Goblin/全 actor 生成箇所追従 → 1 commit
3. Task C: `BattleEventKind` 7 値追加 → 1 commit
4. Task D: `EffectApplier.Apply` シグネチャ変更（`DataCatalog catalog` 追加）+ 既存 caller 全更新（catalog を渡すだけ、既存 4 action 動作は不変）→ 1 commit
5. Task E〜L: 各 effect action の TDD（heal / draw / discard / upgrade / exhaustCard / exhaustSelf / retainSelf / gainEnergy / summon）→ 9 commits
6. Task M: `BattleEngine.PlayCard` 5 段優先順位実装 + summonSucceeded フラグ追跡 → 1 commit
7. Task N: `TurnStartProcessor` Lifetime tick 追加 → 1 commit
8. Task O: `SummonCleanup` 共通 helper 実装 + 各 caller (PlayerAttacking / EnemyAttacking / TurnStart) で呼出 → 1 commit
9. Task P: `TurnEndProcessor` retainSelf-aware + catalog 引数 → 1 commit
10. Task Q-R: 統合 / Determinism / 親 spec 補記 / タグ → 数 commits

詳細順序は plan に記載。

### 8-5. テスト実行コマンド

- 1 ファイル: `dotnet test --filter FullyQualifiedName~<TestClass>`
- 全 Battle: `dotnet test --filter FullyQualifiedName~Battle`
- 全体: `dotnet build && dotnet test`

---

## 9. スコープ外（10.2.E / 11+）

### 9-1. Phase 10.2.D では触らない

- レリック 4 新 Trigger（OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）発火 / `Implemented` スキップ → 10.2.E
- `UsePotion` 戦闘内発動 / `BattleOnly` 戦闘外スキップ → 10.2.E
- Power カードの「常駐効果」（毎ターン発火 / 永続バフ等）→ Phase 11+
- `discard` の `Scope == Single` UI 連携 → Phase 11+
- 召喚キャラの `CurrentMoveId` 駆動の attack 発射（移動先パターン）→ Phase 11+
- 死亡 summon の slot 再利用 → Phase 11+
- 1 カードで複数 summon 連発（spec §5-3-3 の「直前 1 体」前提を超える）→ Phase 11+
- `BattlePlaceholder` 削除 / `RunState.ActiveBattle` 型切替 / save schema v8 → 10.5
- `BattleHub` / `BattleStateDto` / `BattleEventDto` → 10.3
- `BattleScreen.tsx` ポート → 10.4

### 9-2. Phase 10.2.D 完了後の状態

- `BattleState` に `SummonHeld` / `PowerCards` 2 フィールド追加、初期空
- `CombatActor` に `RemainingLifetimeTurns` / `AssociatedSummonHeldInstanceId` 追加
- `EffectApplier.Apply` が 13 action（既存 4 + 新 9）を処理し、`DataCatalog` 引数を受け取る
- `BattleEngine.PlayCard` 末尾のカード移動が 5 段優先順位で動作
- `TurnStartProcessor` が Lifetime tick を含む 6 step（Turn+1 / poison / death+autoswitch / countdown / lifetime / energy / draw）
- `TurnEndProcessor.Process` が `DataCatalog` 引数を受け取り、retainSelf-aware 手札整理
- `SummonCleanup.Apply` が 4 箇所（PlayerAttacking / EnemyAttacking / TurnStart の poison tick 後 / TurnStart の Lifetime tick 後）から呼ばれる
- `BattleEventKind` が 19 値
- xUnit で「召喚 + heal / draw / 状態異常 / コンボ含む 1 戦闘」が完走
- 既存ゲームフロー（`BattlePlaceholder` 経由）は無傷
- 親 spec が 10.2.D の決定事項に合わせて補記済み
- `phase10-2D-complete` タグ push 済み

---

## 10. 親 spec への補記事項

Phase 10.2.D の最終タスクで `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に以下を反映:

1. **§2-4 `EnemyDefinition` / `UnitDefinition`**: `UnitDefinition` 構造は不変だが、10.2.D で動作実装。「召喚キャラの行動: 10.2.D 範囲では Pool 機能なし、Phase 11+ で move 駆動 attack 実装予定」を補記

2. **§3-1 `BattleState`**:
   - 10.2.D で `SummonHeld: ImmutableArray<BattleCardInstance>` / `PowerCards: ImmutableArray<BattleCardInstance>` を追加。配置は `ExhaustPile` 直後、`ComboCount` 前。これで親 spec §3-1 の最終形フィールド順に揃う

3. **§3-2 `CombatActor`**:
   - 10.2.D で `RemainingLifetimeTurns: int?` / `AssociatedSummonHeldInstanceId: string?` を追加
   - **`AssociatedSummonHeldIndex: int?` を `AssociatedSummonHeldInstanceId: string?` に訂正**（memory feedback ルール「InstanceId 検索」準拠、index ずれ問題回避、Q2 (b) 確定事項）

4. **§4-2 ターン開始処理 step 4**:
   - 10.2.D で Lifetime tick 実装。`countdown` 後、`Energy` 前に挿入
   - Lifetime 0 で `CurrentHp = 0` / `ActorDeath` event / `SummonCleanup` 経由 SummonHeld → Discard
   - hero は `RemainingLifetimeTurns is null` で tick されない

5. **§4-6 ターン終了処理 step 5**:
   - 10.2.D で `TurnEndProcessor.Process` のシグネチャに `DataCatalog catalog` 引数追加
   - retainSelf-aware 手札整理: `effects.Any(e => e.Action == "retainSelf")` のカードのみ Hand に残す

6. **§5-1 `EffectApplier.Apply`**:
   - 10.2.D で `DataCatalog catalog` 引数追加
   - 9 新 action（heal / draw / discard / upgrade / exhaustCard / exhaustSelf / retainSelf / gainEnergy / summon）対応
   - `discard Scope==Single` で `InvalidOperationException`（UI 連携待ち）
   - `upgrade` / `exhaustCard` は `IRng` でランダム選択、Pile 不足は存在分だけ
   - `upgrade` は `IsUpgraded == true` を skip、強化候補不足ならその分は無効
   - `gainEnergy` は上限なし（Phase 10 では超過可）
   - `exhaustSelf` / `retainSelf` はマーカー effect（`exhaustSelf` だけ `Exhaust` event 発火、`retainSelf` no-op）

7. **§5-4 召喚カードの捨札遅延**:
   - 10.2.D で `summon` action 実装
   - `effect.UnitId` で `UnitDefinition` を catalog 検索
   - 空き slot (1-3) なし → 不発（silent skip、後続 effect は処理続行）
   - 召喚成功時、新 `CombatActor` 生成 + `Allies` に append + `Summon` event
   - `AssociatedSummonHeldInstanceId` は `BattleEngine.PlayCard` の card-move logic で設定（card.InstanceId）
   - 1 カード = 1 summon effect = 1 召喚 actor を前提（複数 summon 連発は Phase 11+）

8. **§5-7 カード移動 5 段優先順位**:
   - 10.2.D で `BattleEngine.PlayCard` 末尾に実装
   - 優先順位: exhaustSelf → Power → Unit+summonSucceeded → retainSelf → Discard
   - `summonSucceeded` フラグは effect ループ中に追跡
   - `Power` カードはプレイ時の effects 発動後、`PowerCards` 配列に inert 配置（常駐効果は Phase 11+）

9. **§9-7 `BattleEventKind`**:
   - 10.2.D で 7 値追加（`Heal=12` / `Draw=13` / `Discard=14` / `Upgrade=15` / `Exhaust=16` / `GainEnergy=17` / `Summon=18`、計 19 値）
   - 各 Kind のペイロード慣例は §2-3 表参照

これら 9 項目は Phase 10.2.D 内で発生した設計判断の追記。コードと spec の乖離を残さない。

---

## 11. memory feedback ルールの遵守チェックリスト

実装中・レビュー時に確認する 2 項目（`memory/feedback_battle_engine_conventions.md`）:

- [ ] `BattleOutcome` 参照は今回新規発生しうる箇所:
  - Lifetime tick の死亡判定で「全敵 / 全味方死亡」のチェック（hero は永続なので Defeat はあり得ないが、Allies 全死亡パスは厳密に検証）
  - すべて `RoguelikeCardGame.Core.Battle.State.BattleOutcome.X` の fully qualified
- [ ] `state.Allies` / `state.Enemies` への書き戻しは InstanceId で検索:
  - `EffectApplier` の `heal` All / Random scope（複数 actor 更新）
  - `EffectApplier` の `summon` action（Allies に append、後続 effect で hero 再 fetch）
  - `TurnStartProcessor` の Lifetime tick loop
  - `SummonCleanup.Apply` のクリーンアップ loop
  - 既存 `caster = s.Allies[0]` 再 fetch パターンを維持
- [ ] `Allies.Add(newSummon)` は `ImmutableArray.Add` を使用、新 actor の InstanceId は同じターン内で重複しないことを確認

---

## 参照

- 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン spec: [`2026-04-26-phase10-2C-combo-target-design.md`](2026-04-26-phase10-2C-combo-target-design.md)
- 直前マイルストーン plan: [`../plans/2026-04-26-phase10-2C-combo-target.md`](../plans/2026-04-26-phase10-2C-combo-target.md)
- ロードマップ: [`../plans/2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md)
- memory feedback: `memory/feedback_battle_engine_conventions.md`
