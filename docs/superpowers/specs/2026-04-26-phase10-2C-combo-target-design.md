# Phase 10.2.C — コンボ + 対象指定 設計

> 作成日: 2026-04-26
> 対象フェーズ: Phase 10.2.C（Phase 10.2 サブマイルストーン 3 番目 / 全 5 段階）
> 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
> 直前マイルストーン spec: [`2026-04-26-phase10-2B-statuses-design.md`](2026-04-26-phase10-2B-statuses-design.md)
> 直前マイルストーン plan: [`../plans/2026-04-26-phase10-2B-statuses.md`](../plans/2026-04-26-phase10-2B-statuses.md)
> 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html`

## ゴール

10.2.B で完成した「状態異常 6 種 + 遡及計算 + buff/debuff effect + ターン開始 tick + omnistrike 合算発射」を持つ `BattleEngine` に、**コンボ機構**と**対象指定アクション**を追加する。

具体的には:

- `BattleState` に 3 フィールド追加: `ComboCount` / `LastPlayedOrigCost` / `NextCardComboFreePass`
- `BattleEngine.PlayCard` のコンボ判定アルゴリズムを実装（親 spec §6-3）
  - 通常コンボ階段（元コスト+1 で継続、コスト軽減 -1）
  - `wild` キーワード（条件不一致でも継続、軽減なし）
  - `superwild` キーワード（Wild の効果 + 次の 1 枚も bypass）
- `effect.ComboMin` per-effect filter を `BattleEngine.PlayCard` のループ内で評価（`EffectApplier` のシグネチャは不変）
- `TurnEndProcessor` でコンボリセット（`ComboCount = 0` / `LastPlayedOrigCost = null` / `NextCardComboFreePass = false`）
- **第 5 の public static API** `BattleEngine.SetTarget(state, side, slotIndex)` を追加（生存者・範囲・Phase バリデーション付き、イベント発火なし）

`BattleEngine` 既存 4 公開 API（`Start` / `PlayCard` / `EndTurn` / `Finalize`）のシグネチャは不変。`PlayCard` の引数経由の対象切替（10.2.A 既存）も維持。10.2.C で増えるのは「`SetTarget` 単独 API」と「`PlayCard` 内部のコンボ判定ロジック」のみ。

残り effect 8 種・召喚・カード移動 5 段優先順位・レリック 4 新 Trigger・ポーション戦闘内発動はすべて後続 sub-phase に持ち越す。

`BattlePlaceholder` 経由の既存ゲームフロー（敵タイル進入 → 即勝利ボタン → 報酬画面）は無傷で動作させ続ける。

## 完了判定

- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（10.2.B 完了時の Core 783 + 10.2.C 追加分）
- `BattleState` に `ComboCount: int` / `LastPlayedOrigCost: int?` / `NextCardComboFreePass: bool` の 3 フィールドが追加済み（initializer 順序は record primary constructor 末尾近く）
- `BattleEngine.Start` 直後の state で 3 フィールドが `0 / null / false` の初期値
- `BattleEngine.PlayCard` のコンボ判定が親 spec §6-3 / §6-4 の 6 例すべてを正しく処理
  - 通常コンボ階段 / Wild（条件不一致）/ Wild（条件一致）/ SuperWild + 次カード bypass / リセット直後の Wild は新規スタート / SuperWild → 0 コスト
- `effect.ComboMin` が `BattleEngine.PlayCard` の effect ループ内で評価され、`ComboCount < ComboMin` の effect はスキップ
- `EffectApplier.Apply` のシグネチャ（`(BattleState, CombatActor, CardEffect, IRng)`）は不変
- `BattleEngine.SetTarget(state, side, slotIndex) → BattleState` が public static として公開済み
- `SetTarget` の Phase バリデーション（`PlayerInput` のみ許可）/ 範囲バリデーション / 死亡バリデーションが動作
- `SetTarget` の戻り値は `BattleState` 単体（`BattleEvent` 発火なし）
- `BattleEventKind` の値数は **不変**（10.2.B 完了時の 12 値のまま、`TargetChanged` 等は追加しない）
- `TurnEndProcessor.Process` が `ComboCount = 0` / `LastPlayedOrigCost = null` / `NextCardComboFreePass = false` にリセット
- 既存 `BattlePlaceholder` 経由のフローは無傷（手動プレイ確認）
- 親 spec §3-1 / §4-6 / §5-1 / §6 / §7-3 に 10.2.C で発生した設計判断を補記済み
- `phase10-2C-complete` タグが切られ origin に push 済み

---

## 1. アーキテクチャ概要

### 1-1. Phase 10.2 全体の中での位置付け

| サブ phase | 範囲 | 状態 |
|---|---|---|
| 10.2.A | 基盤データモデル + `BattleEngine` 4 公開 API + `attack`/`block` の 2 effect + Phase 進行 + Victory/Defeat | ✅ 完了 |
| 10.2.B | 状態異常 6 種 + 遡及計算 + buff/debuff effect + ターン開始 tick + omnistrike 合算発射 | ✅ 完了 |
| **10.2.C**（本 spec） | **コンボ機構 + `SetTarget` API + `comboMin` per-effect filter** | 本フェーズ |
| 10.2.D | 残り effect 8 種 + 召喚 system + カード移動 5 段優先順位 + PowerCards | 後続 |
| 10.2.E | レリック 4 新 Trigger 発火 + 所持順発動 + Implemented スキップ + UsePotion 戦闘内 + BattleOnly 戦闘外スキップ | 後続 |

10.2.C は **5 つ目の public static API（`SetTarget`）を初めて追加する** sub-phase。10.2.D の `UsePotion`、10.2.E の `UsePotion` 戦闘内発動経路もこのパターンを踏襲する想定。

### 1-2. 共存戦略（NodeEffectResolver / BattlePlaceholder）

10.2.A / 10.2.B と同じ。新 `BattleEngine` は **pure Core API として独立**し、`NodeEffectResolver` は引き続き `BattlePlaceholder.Start` を呼ぶ（既存ゲームフローは無傷）。`BattleEngine` は xUnit でしかテストされない。`BattleHub` 統合は 10.3 で実施。

### 1-3. ファイル構成（10.2.C 完了時の差分）

```
src/Core/Battle/
├── State/
│   └── BattleState.cs                    [修正] +ComboCount / LastPlayedOrigCost / NextCardComboFreePass
└── Engine/
    ├── BattleEngine.cs                   [修正] Start で 3 フィールド初期化 (0 / null / false)
    ├── BattleEngine.PlayCard.cs          [修正] コンボ判定アルゴリズム + comboMin per-effect filter
    ├── BattleEngine.SetTarget.cs         [新] 第 5 の public static API（partial class）
    └── TurnEndProcessor.cs               [修正] コンボ 3 フィールドのリセット

tests/Core.Tests/Battle/
├── State/
│   └── BattleStateTests.cs               [修正] 3 フィールド record 等価 / 初期値
├── Engine/
│   ├── BattleEnginePlayCardComboTests.cs            [新] 通常階段 / Wild / SuperWild / FreePass / リセット直後 / SuperWild→0 コスト
│   ├── BattleEnginePlayCardComboMinTests.cs         [新] per-effect filter（達成・未達成・combo 1 のときの comboMin:1）
│   ├── BattleEnginePlayCardCostReductionTests.cs    [新] payCost = max(0, X-1) / CostOverride との合算 / Energy 不足時の例外順序
│   ├── BattleEngineSetTargetTests.cs                [新] Phase 制約 / 範囲外 / 死亡 / Ally / Enemy / 正常切替
│   ├── TurnEndProcessorComboResetTests.cs           [新] EndTurn 跨ぎで 3 フィールドリセット
│   ├── BattleEnginePlayCardTests.cs                 [修正] 既存 fixture の BattleState 初期化追従
│   ├── BattleEngineEndTurnTests.cs                  [修正] コンボリセットの assertion 追加
│   ├── BattleEngineStartTests.cs                    [修正] Start 直後の 3 フィールド初期値検証
│   └── BattleDeterminismTests.cs                    [修正] コンボ含む 1 戦闘で seed 一致
└── Fixtures/
    └── BattleFixtures.cs                            [修正] 全 BattleState 生成箇所に 3 フィールド初期化伝播
```

### 1-4. namespace

10.2.C で新 namespace は追加しない。`BattleEngine.SetTarget.cs` は既存 `RoguelikeCardGame.Core.Battle.Engine` の `BattleEngine` partial class に追加。

### 1-5. memory feedback の遵守（10.2.A / 10.2.B 由来）

`memory/feedback_battle_engine_conventions.md` の 2 ルールは 10.2.C でも維持する:

1. **`BattleOutcome` は常に fully qualified**: 10.2.C では `BattleEngine.PlayCard` / `SetTarget` / `TurnEndProcessor` のいずれも `Outcome` を変更しないため、新規参照箇所は **基本的に増えない**。既存箇所（`BattleEngine.EndTurn` の `ResolveOutcome` 等）は touch しない
2. **`state.Allies` / `state.Enemies` への書き戻しは InstanceId で検索**: 10.2.C では `Allies` / `Enemies` への書き戻しは発生しない（コンボフィールドは `BattleState` ルート、`SetTarget` は `Target{Ally|Enemy}Index` のみ）。ただし `BattleEngine.PlayCard` 内の effect ループで `caster` を再 fetch する 10.2.A/B のパターン（`caster = state.Allies[0]` / `FindActor(state, "hero_inst")`）は **維持**する

10.2.C で新規発生する書き戻しは `BattleState with { ComboCount = ..., LastPlayedOrigCost = ..., NextCardComboFreePass = ..., Energy = ... }` のフラットフィールド更新のみ。

---

## 2. データモデル

### 2-1. `BattleState` の 3 フィールド追加

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

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
    int ComboCount,                       // ← 10.2.C 追加: 現在のコンボ数 (0..N)
    int? LastPlayedOrigCost,              // ← 10.2.C 追加: 直前に手打ちプレイしたカードの元コスト
    bool NextCardComboFreePass,           // ← 10.2.C 追加: SuperWild 由来。次のカード 1 枚はコンボ条件 bypass
    string EncounterId);
```

- 配置: `EncounterId` の直前（既存 record 末尾の手前にまとめる）。既存フィールド順は不変
- 初期値: `Start` 時 `0 / null / false`、`TurnEndProcessor.Process` で同じ値にリセット
- `BattleCardInstance` / `CombatActor` / `BlockPool` / `AttackPool` の各 record は **不変**

> 設計判断: 親 spec §3-1 では `ExhaustPile` の後に `SummonHeld` / `PowerCards`、その後にコンボ系がリストアップされている。10.2.C 時点では `SummonHeld` / `PowerCards` は未追加（10.2.D で追加）。10.2.C のコンボ 3 フィールドは現在の record 末尾（`EncounterId` の前）に追加し、10.2.D で `SummonHeld` / `PowerCards` をその間（`ExhaustPile` の後）に挿入する想定。フィールド順序の最終形は親 spec §3-1 と一致しない期間があるが、10.2.E 完了時に揃える。

### 2-2. `BattleEventKind` は不変

10.2.C で `BattleEventKind` は変更しない（10.2.B 完了時の 12 値のまま）。

理由:
- `SetTarget` はイベント発火なし（§4 参照）
- コンボ階段・FreePass・comboMin filter はすべて `BattleState` の差分で表現可能（クライアントは `ComboCount` / `NextCardComboFreePass` / `LastPlayedOrigCost` の差分から演出を組む）
- 既存 `PlayCard` event の `Amount` フィールドは「払ったエナジー量 = `payCost`」の意味で一貫（10.2.A 既存）。コンボ軽減後の値が入る

### 2-3. `CardEffect.ComboMin` の意味再確認

`CardEffect.cs` の既存定義は不変:

```csharp
public sealed record CardEffect(
    string Action,
    EffectScope Scope,
    EffectSide? Side,
    int Amount,
    string? Name = null,
    string? UnitId = null,
    int? ComboMin = null,         // ← 10.2.C で初めて意味を持つ
    string? Pile = null,
    bool BattleOnly = false);
```

`ComboMin == null` または `state.ComboCount >= ComboMin` のとき effect を適用、それ以外はスキップ。ComboMin は **カードプレイ経路のみ**で評価。敵 Move / レリック / ポーションの effect で ComboMin を持つことは想定していない（JSON で書かれていても `EffectApplier` は ComboMin を見ないので結果として無視される）。

### 2-4. `CardDefinition.Keywords` の意味再確認

`CardDefinition.cs` の既存定義は不変:

```csharp
public sealed record CardDefinition(
    string Id, string Name, string? DisplayName,
    CardRarity Rarity, CardType CardType,
    int? Cost,
    int? UpgradedCost,
    IReadOnlyList<CardEffect> Effects,
    IReadOnlyList<CardEffect>? UpgradedEffects,
    IReadOnlyList<string>? Keywords);     // ← 10.2.C で初めてコンボ判定で使う
```

10.2.C で参照する Keywords:

- `"wild"` — コンボ条件不一致でも継続成立、軽減なし
- `"superwild"` — Wild の効果 + 次のカード 1 枚の FreePass フラグ立て

判定例（実装内で記述）:

```csharp
bool isWild      = def.Keywords?.Contains("wild") == true;
bool isSuperWild = def.Keywords?.Contains("superwild") == true;
```

`"wild"` と `"superwild"` の両方が同時に立つカードは想定外（JSON データ作成時の規約）。両方立っていた場合は `isSuperWild` の挙動が支配的（FreePass フラグも立つ）。

---

## 3. `BattleEngine.PlayCard` のコンボ判定

### 3-1. 全体フロー（疑似コード）

```
PlayCard(state, handIndex, targetEnemy?, targetAlly?, rng, catalog):
    if (state.Phase != PlayerInput) throw          // 10.2.A 既存
    if (handIndex 範囲外) throw                     // 10.2.A 既存
    
    card = state.Hand[handIndex]
    def  = catalog.TryGetCard(card.CardDefinitionId)
    
    // === 10.2.C 追加: actualCost 算定（CostOverride 無視） ===
    int? origCost = card.IsUpgraded ? def.UpgradedCost ?? def.Cost : def.Cost
    if (origCost is null) throw "unplayable"
    int actualCost = origCost.Value
    
    // === 10.2.C 追加: コンボ継続判定 ===
    bool matchesNormal =
        state.LastPlayedOrigCost is { } prev && actualCost == prev + 1
    bool isWild      = def.Keywords?.Contains("wild") == true
    bool isSuperWild = def.Keywords?.Contains("superwild") == true
    
    bool isContinuing =
        state.NextCardComboFreePass ? true            // SuperWild 由来 bypass
      : matchesNormal              ? true             // 通常階段
      : (isWild || isSuperWild)    ? true             // Wild / SuperWild 自身も継続
      : false                                         // 新規スタート
    
    // === 10.2.C 追加: コスト軽減判定（軽減は通常条件のみ） ===
    bool isReduced = matchesNormal
    
    // === 10.2.C 追加: payCost 算定 ===
    // CostOverride は payCost に反映、軽減 -1 も反映（下限 0）
    int basePay = card.CostOverride ?? actualCost
    int payCost = Math.Max(0, basePay - (isReduced ? 1 : 0))
    
    // === エナジーチェック（10.2.A 既存だが順序が変わる: コンボ判定後） ===
    if (state.Energy < payCost) throw "insufficient energy"
    
    // === 10.2.C 追加: ComboCount / LastPlayedOrigCost / NextCardComboFreePass 更新 ===
    int  newCombo    = isContinuing ? state.ComboCount + 1 : 1
    int? newLastCost = actualCost                          // 手打ちプレイのみ更新
    bool newFreePass = isSuperWild                          // SuperWild なら true、それ以外 false（消費）
    
    // === state 更新 ===
    var s = state with {
        Energy                = state.Energy - payCost,
        ComboCount            = newCombo,
        LastPlayedOrigCost    = newLastCost,
        NextCardComboFreePass = newFreePass,
        TargetEnemyIndex      = targetEnemy ?? state.TargetEnemyIndex,
        TargetAllyIndex       = targetAlly  ?? state.TargetAllyIndex,
    }
    
    // === PlayCard event（10.2.A 既存。Amount は payCost） ===
    events.Add(PlayCard event { Caster: hero, CardId: def.Id, Amount: payCost })
    
    // === effects 適用（10.2.C 追加: per-effect comboMin filter） ===
    var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
        ? def.UpgradedEffects : def.Effects
    var caster = s.Allies[0]
    foreach eff in effects:
        if (eff.ComboMin is { } min && newCombo < min)        // ← 10.2.C 追加
            continue
        (s, evs) = EffectApplier.Apply(s, caster, eff, rng)
        events.AddRange(evs)
        caster = s.Allies[0]                                    // 10.2.A/B 既存の再 fetch
    
    // === カード移動（10.2.A 既存: Hand → Discard、優先順位は 10.2.D で拡張） ===
    s = s with { Hand = s.Hand.RemoveAt(handIndex), Discard = s.Discard.Add(card) }
    
    return (s, events)
```

### 3-2. 設計上の判断

#### 3-2-1. `actualCost` から `CostOverride` を除外（Q5 (a)）

親 spec §6-1: 「**元コスト** = 強化済みなら `UpgradedCost ?? Cost`、無強化なら `Cost`。**コスト軽減前**の値」。

`BattleCardInstance.CostOverride` は戦闘内の動的コスト軽減を表現するフィールド（10.2.D 以降のレリック効果で利用見込み）。コンボ判定で使う `actualCost` は **`CostOverride` を無視**して定義値だけで算出する。

理由:
- 「コスト 0 化レリックがあってもコンボ階段は元コスト 1→2→3 の整数段階で判定」という設計
- `LastPlayedOrigCost` の "OrigCost" 命名はこの `actualCost` を指す
- 「コスト 0 化中は階段が無意味化する」を避ける

例: ダブルダウンレリックで全カード -1 されている状態でも、元コスト 1→2→3 のカードを順に打てば通常階段成立。`payCost` は `0 + 0 + 0`（CostOverride で 0 に固定）+ 軽減 -1 の下限 0 で全カード 0 エナジーで打てるが、`ComboCount` は 1→2→3 で正しく階段。

#### 3-2-2. `payCost` には `CostOverride` を反映

`payCost` は実際にエナジーから引かれる量。`CostOverride` を反映し、コンボ軽減 -1 も反映、最後に下限 0 で clamp。

```csharp
int basePay = card.CostOverride ?? actualCost;
int payCost = Math.Max(0, basePay - (isReduced ? 1 : 0));
```

10.2.A の既存実装は `cost = card.CostOverride ?? (card.IsUpgraded ? def.UpgradedCost ?? def.Cost : def.Cost)` で `payCost` 相当を計算していたので、CostOverride 優先は変わらない。差分は「`isReduced ? -1 : 0` の補正と Math.Max(0, ...) の追加」のみ。

#### 3-2-3. Energy 不足の例外順序

10.2.A は `if (state.Energy < cost.Value) throw` を **コンボ判定の前**で評価していた（cost.Value が `payCost` 相当）。10.2.C では:

1. `actualCost` 算定
2. コンボ判定（matchesNormal / isWild / isSuperWild / isContinuing / isReduced）
3. `payCost` 算定（軽減反映、下限 0）
4. **Energy < payCost で例外** ← 10.2.C 移動位置

軽減 -1 で `payCost = 0` になった場合は Energy 0 でもプレイ可能、というのが正しい挙動。テストでカバー（`BattleEnginePlayCardCostReductionTests`）。

#### 3-2-4. SuperWild の FreePass 規則

`NextCardComboFreePass` は **SuperWild プレイで true がセットされ、次のカードプレイで消費されて false に戻る**。

```csharp
bool newFreePass = isSuperWild;
```

これだけで規則を満たす。理由:

- 現在のカードが SuperWild → `newFreePass = true`（次カード向け予約）
- 現在のカードが SuperWild 以外 → `newFreePass = false`（消費して次以降は通常判定）
- 連続 SuperWild → 後者が前者の予約を上書きする（FreePass は次の 1 枚にしか作用しない、親 spec §6-4 例 4）

`state.NextCardComboFreePass` の値を保持するケースはない。「SuperWild → 何かの理由で次のカードがプレイされず EndTurn」 → `TurnEndProcessor` が false にリセット（§5）。

#### 3-2-5. `LastPlayedOrigCost` 更新は **手打ちプレイ全カード**

親 spec §6-6:「自動詠唱（コスト null カードや何らかの効果による）はコンボ更新しない」。Phase 10 では自動詠唱の仕組み自体が未実装なので、10.2.C では手打ちプレイ = `BattleEngine.PlayCard` 呼出の全ケースで `LastPlayedOrigCost = actualCost` 更新。

将来の自動詠唱経路（Phase 11+）が増える際は別関数（`AutoCastEffect` 等）として分岐し、`LastPlayedOrigCost` を触らない設計を維持する。10.2.C スコープ外。

### 3-3. 6 種の動作例（親 spec §6-4 と一致）

実装上の挙動を Pinning するためのテストケース。`BattleEnginePlayCardComboTests` で網羅:

| # | 直前状態 | プレイカード | 期待結果 |
|---|---|---|---|
| 1 通常階段 | LastOrigCost=1, Combo=1, FreePass=false | 元コスト 2、Keywords なし | matchesNormal=true, isContinuing=true, isReduced=true, payCost=1, Combo=2, LastOrigCost=2, FreePass=false |
| 2 Wild（条件不一致） | LastOrigCost=1, Combo=1, FreePass=false | 元コスト 5、`["wild"]` | matchesNormal=false, isWild=true, isContinuing=true, isReduced=false, payCost=5, Combo=2, LastOrigCost=5, FreePass=false |
| 3 Wild（条件一致） | LastOrigCost=1, Combo=1, FreePass=false | 元コスト 2、`["wild"]` | matchesNormal=true, isWild=true, isContinuing=true, isReduced=true, payCost=1, Combo=2, LastOrigCost=2, FreePass=false |
| 4 SuperWild | LastOrigCost=1, Combo=1, FreePass=false | 元コスト 7、`["superwild"]` | matchesNormal=false, isSuperWild=true, isContinuing=true, isReduced=false, payCost=7, Combo=2, LastOrigCost=7, FreePass=true |
| 4-cont 次カード | LastOrigCost=7, Combo=2, FreePass=true | 元コスト 3、Keywords なし | FreePass 由来 isContinuing=true, isReduced=false（matchesNormal は元コスト 8 でないため false）, payCost=3, Combo=3, LastOrigCost=3, FreePass=false |
| 5 リセット直後 Wild | LastOrigCost=null, Combo=0, FreePass=false | 元コスト 5、`["wild"]` | matchesNormal=false（LastOrigCost null）, isWild=true, isContinuing=true, isReduced=false, payCost=5, Combo=1, LastOrigCost=5, FreePass=false |
| 6 SuperWild → 0 コスト | LastOrigCost=4, Combo=2, FreePass=false | 元コスト 6 SuperWild → 元コスト 0 | 1 枚目: Combo=3, LastOrigCost=6, FreePass=true。2 枚目: FreePass 消費で isContinuing=true, isReduced=false（matchesNormal は元コスト 7 でないため false）, payCost=0, Combo=4, LastOrigCost=0, FreePass=false |

例 5 の補足: `state.LastPlayedOrigCost == null` のとき `matchesNormal == false` だが、Wild なら `isContinuing == true` で **新規スタート扱い**ではなく「Combo=0 から +1 で Combo=1」の継続扱いになる。結果 `Combo=1` は新規スタート時と同値だが、内部経路が違う。`isReduced=false` なので軽減なし、payCost=5。これは親 spec §6-4 例 5 の意図と一致。

### 3-4. `comboMin` per-effect filter（Q4 (a)）

`EffectApplier.Apply` のシグネチャは **不変**（`(BattleState, CombatActor, CardEffect, IRng)`）。

`BattleEngine.PlayCard` の effect ループ内で per-effect filter:

```csharp
foreach (var eff in effects)
{
    if (eff.ComboMin is { } min && newCombo < min)
        continue;
    var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng);
    s = afterEffect;
    foreach (var ev in evs) { events.Add(ev with { Order = order }); order++; }
    caster = s.Allies[0];
}
```

判定値は `newCombo`（**ComboCount 更新後**の値、親 spec §6-5）。これにより:

- カードプレイで Combo が 2 になった瞬間 → `comboMin: 2` の effect が発動
- Combo が 1 のまま → `comboMin: 2` の effect はスキップ
- `comboMin: 1` は Combo 1 以上で常に true（最初のカードでも作用）
- `comboMin: 0` は意味的に `null` と同等だが、 JSON 上は `null` 推奨

敵 Move / レリック / ポーションの effect で ComboMin が指定されていても、それぞれの Apply 経路（10.2.D 以降）は ComboMin を見ない。結果として **effects on カードプレイ以外では comboMin が常に「null と同等」に振る舞う**。

### 3-5. `PlayCard` event の Amount

10.2.A 既存の `PlayCard` event の `Amount` は「払ったエナジー量」=`cost.Value`。10.2.C で命名は `payCost` に変わるが意味は同じ。`Amount = payCost` を維持。

クライアントは `payCost` と `actualCost` の差分（軽減 -1 が効いたかどうか）を `BattleStateDto.Hand[handIndex].DisplayCost` から復元する想定（10.3 で `DisplayCost` 計算ロジックを Server に追加、§9-6 親 spec）。10.2.C では Core が `payCost` を知っていれば十分。

---

## 4. `BattleEngine.SetTarget` 新 public API

### 4-1. シグネチャ

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// 対象スロットを切替する。Phase=PlayerInput でのみ呼出可能、
    /// 生存・範囲バリデーション失敗時は InvalidOperationException。
    /// 親 spec §7-3 / Phase 10.2.C spec §4 参照。
    /// </summary>
    public static BattleState SetTarget(BattleState state, ActorSide side, int slotIndex);
}
```

戻り値は `BattleState` 単体。`BattleEvent` 発火なし（Q3 (a)）。

### 4-2. バリデーション仕様

```
SetTarget(state, side, slotIndex):
    if (state.Phase != BattlePhase.PlayerInput)
        throw InvalidOperationException(
            $"SetTarget requires Phase=PlayerInput, got {state.Phase}")
    
    pool = side == ActorSide.Ally ? state.Allies : state.Enemies
    
    if (slotIndex < 0 || slotIndex >= pool.Length)
        throw InvalidOperationException(
            $"slotIndex {slotIndex} out of range [0, {pool.Length}) for side={side}")
    
    if (!pool[slotIndex].IsAlive)
        throw InvalidOperationException(
            $"slot {side}[{slotIndex}] is dead and cannot be targeted")
    
    if (side == ActorSide.Ally)
        return state with { TargetAllyIndex = slotIndex }
    else
        return state with { TargetEnemyIndex = slotIndex }
```

### 4-3. 設計上の判断

#### 4-3-1. Phase 制約 = `PlayerInput` のみ（Q2 (a)）

`Phase != PlayerInput` で例外。理由:
- `PlayerAttacking` / `EnemyAttacking` 中の対象切替はバトルロジック上 race を起こしうる
- `Resolved` 中の切替は意味なし（バトル終了後）
- `PlayCard` / `EndTurn` の既存慣例（`Phase != PlayerInput` で例外）と一致

クライアント UI も同 Phase でのみ切替を許可するため、Core 側の制約は二重防衛として機能する。

#### 4-3-2. 死亡スロット指定で例外

10.2.A の `TargetingAutoSwitch` は「死亡時に自動で生存者最内側に切替」する仕組みを既に持つ。手動 `SetTarget` で死亡スロットを指定するのは UI バグの兆候 → 例外で早期検知。

範囲外 slotIndex も同様。

#### 4-3-3. イベント発火なし

`BattleEventKind.TargetChanged` 等の追加なし。クライアントは `BattleStateDto.Stage.TargetAllyIndex` / `TargetEnemyIndex` の差分を毎 push で確認することで対象切替を検知できる（10.3 で実装）。対象切替は瞬時で演出不要、event キューに混ぜる必要がない。

#### 4-3-4. `PlayCard` 引数経由の対象切替との関係

10.2.A 既存の `BattleEngine.PlayCard(handIndex, targetEnemyIndex?, targetAllyIndex?, ...)` は、引数 non-null のとき state.Target* を上書きしてからカード処理。これは「カードプレイ操作の一部として対象切替が含まれる」経路（クライアント: ドラッグ確定時の暗黙切替）。

10.2.C の `SetTarget` は「カードプレイなしの単独切替」（クライアント: スロットクリック）。**両者は共存**し、バリデーション規則も同じ（Phase=PlayerInput / 範囲 / 生存）が必要。10.2.C で `PlayCard` 引数経由の path にも生存・範囲チェックを追加するかどうかは:

- **追加する**: `SetTarget` を内部呼出する形で統一
- **追加しない**: `PlayCard` 引数経由は「null 上書き = 無変化」の緩い解釈のまま

10.2.A の `PlayCard` は既に `TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex` の代入だけで生存チェックしていない。10.2.C ではこの部分を **触らない**（既存挙動を壊さない、テストも追加しない）。10.2.D 以降で `UsePotion` を追加する際にあわせて整合する余地として残す。

### 4-4. ファイル分割

`BattleEngine.SetTarget.cs` を新設し、`partial class BattleEngine` に追加。10.2.A の `BattleEngine.PlayCard.cs` / `BattleEngine.EndTurn.cs` / `BattleEngine.Finalize.cs` の partial 分割と同じパターン。

---

## 5. `TurnEndProcessor` のコンボリセット

### 5-1. 処理フロー（修正後）

```csharp
internal static class TurnEndProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state)
    {
        var allies  = state.Allies.Select(ResetActor).ToImmutableArray();
        var enemies = state.Enemies.Select(ResetActor).ToImmutableArray();
        var newDiscard = state.DiscardPile.AddRange(state.Hand);
        var next = state with
        {
            Allies                = allies,
            Enemies               = enemies,
            Hand                  = ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile           = newDiscard,
            ComboCount            = 0,           // ← 10.2.C 追加
            LastPlayedOrigCost    = null,        // ← 10.2.C 追加
            NextCardComboFreePass = false,       // ← 10.2.C 追加
        };
        return (next, Array.Empty<BattleEvent>());
    }

    private static CombatActor ResetActor(CombatActor a) => a with
    {
        Block        = BlockPool.Empty,
        AttackSingle = AttackPool.Empty,
        AttackRandom = AttackPool.Empty,
        AttackAll    = AttackPool.Empty,
    };
}
```

### 5-2. 設計上の判断

#### 5-2-1. `NextCardComboFreePass` もリセットする

親 spec §4-6 step 4: 「`NextCardComboFreePass` は SuperWild の効果なのでターン跨ぎでリセット」。SuperWild プレイ後にカードを 1 枚もプレイせず EndTurn した場合、FreePass は次ターンに持ち越されない。

クライアント UI の意図は「SuperWild の bypass は次の **このターン中の** 1 枚」。ターン終了で消える方が直感的。

#### 5-2-2. リセット位置は EndTurn 内の `TurnEndProcessor.Process`

親 spec §4-6 のターン終了処理は EndTurn 内の `TurnEndProcessor` ステップ。順序は:

1. Block リセット
2. AttackPool リセット
3. OnTurnEnd レリック発動（10.2.E）
4. **コンボリセット**（10.2.C で追加）
5. 手札整理（10.2.D で `retainSelf` 対応に拡張）

10.2.C では step 1-2 と step 4 を同時に `TurnEndProcessor.Process` で実行。step 3 / 5 は後続 sub-phase で挿入。

#### 5-2-3. `BattleEngine.Start` 直後の初期値

`BattleState` の record primary constructor 末尾近くに 3 フィールドを追加するため、`BattleEngine.Start` の `new BattleState(...)` 呼出箇所で `ComboCount: 0` / `LastPlayedOrigCost: null` / `NextCardComboFreePass: false` を明示的に渡す。

`BattleEngine.Start` 実行後に `TurnStartProcessor.Process` が呼ばれるが、`TurnStartProcessor` はコンボフィールドに介入しない（10.2.B で実装した毒 tick / countdown / Energy / Draw / TurnStart event のみ）→ Start 直後も 3 フィールドは初期値のまま。

---

## 6. comboMin per-effect filter の詳細仕様

### 6-1. 評価タイミング（親 spec §6-5）

`comboMin` は **`ComboCount` 更新後**（`newCombo` の値）で判定。

```csharp
int newCombo = isContinuing ? state.ComboCount + 1 : 1;
// state は newCombo を反映済み（s = state with { ComboCount = newCombo, ... }）
foreach (var eff in effects)
{
    if (eff.ComboMin is { } min && newCombo < min)
        continue;
    ...
}
```

これにより:

- `comboMin: 1` のカード effect は最初のカード（Combo 0 → 1）でも作用
- `comboMin: 2` のカード effect は **そのカード自身がコンボ階段の 2 段目になった瞬間**作用
- `comboMin: 3` のカード effect は同 3 段目で作用

### 6-2. 1 枚のカードに「素の効果 + comboMin 付き効果」を併記

親 spec §5-5 の例:

```json
"effects": [
  {"action":"attack","scope":"single","side":"enemy","amount":5},
  {"action":"attack","scope":"single","side":"enemy","amount":5,"comboMin":2}
]
```

- Combo 1（newCombo=1）→ 1 つ目だけ適用 → AttackPool +5
- Combo 2（newCombo=2）→ 両方適用 → AttackPool +10

`comboMin` は **per-effect** で評価され、カード全体は常にプレイ可能（payCost が払えるなら）。

### 6-3. `comboMin: 0` / 負値の挙動

- `comboMin: 0`: 常に true（newCombo は最低 1）→ `null` と同等
- `comboMin: 負値`: 常に true（同上）→ JSON データ作成時に避ける推奨
- `comboMin: null`: 既存挙動（フィルタなし）

実装は `if (eff.ComboMin is { } min && newCombo < min) continue;` で十分（`min` の値が 0 や負でも newCombo は 1 以上のため pass する）。

### 6-4. アップグレード版 effects との関係

`card.IsUpgraded && def.UpgradedEffects is not null` の場合は `def.UpgradedEffects` を使用（10.2.A 既存）。`UpgradedEffects` 内の effect も同じ `comboMin` 評価ルールが適用される。

---

## 7. 不変条件（10.2.C 追加分）

10.2.B 完了時の不変条件に加えて、10.2.C で以下を保証:

- `BattleState.ComboCount >= 0`（負値にならない）
- `BattleState.NextCardComboFreePass == true` ⇒ 直前にプレイされたカードが SuperWild
  - ※ 10.2.C 完了時点では「直前カードが SuperWild」を Core が検証する手段はないため、テストでの間接確認に留める
- `state.Phase == BattlePhase.PlayerInput` ⇒ `SetTarget` が成功する条件は「指定スロットが範囲内 + 生存」
- `BattleEngine.SetTarget` 成功後の `state.Target{Ally|Enemy}Index` は **必ず生存スロット**を指す（または null のまま、ただし null は本 API の戻り値では起きない）
- `TurnEndProcessor.Process` 後の state は `ComboCount == 0 && LastPlayedOrigCost == null && NextCardComboFreePass == false` を満たす

`BattleStateInvariantTests` に追加項目として組み込む（ComboCount >= 0 のみ。他は中間状態で違反するため不変条件にしない）。

---

## 8. テスト戦略

### 8-1. テスト粒度

10.2.A / 10.2.B と同じ TDD 1 サイクル（失敗 → 実装 → 緑 → commit）。subagent-driven-development を前提。

### 8-2. 新規テストファイル一覧

| ファイル | カバレッジ |
|---|---|
| `Engine/BattleEnginePlayCardComboTests.cs` | §3-3 の 6 例網羅（通常階段 / Wild 不一致 / Wild 一致 / SuperWild + 次カード bypass / リセット直後 Wild / SuperWild→0 コスト）+ Wild と SuperWild 両方を持つカードの SuperWild 優先 |
| `Engine/BattleEnginePlayCardComboMinTests.cs` | per-effect filter（comboMin null / 1 / 2 / 3 / 0 / 負値）+ 1 枚で素の effect + comboMin effect 混在 + UpgradedEffects 内の comboMin |
| `Engine/BattleEnginePlayCardCostReductionTests.cs` | matchesNormal で payCost = actualCost - 1 / 軽減で payCost = 0（Energy 0 でも可）/ 軽減で下限 0 / CostOverride との合算 / Energy 不足の例外順序（コンボ判定後） |
| `Engine/BattleEngineSetTargetTests.cs` | Phase=PlayerInput 正常切替（Ally / Enemy）/ Phase 違反例外（PlayerAttacking / EnemyAttacking / Resolved）/ 範囲外 slotIndex 例外 / 死亡スロット例外 / 切替後の state diff |
| `Engine/TurnEndProcessorComboResetTests.cs` | EndTurn 跨ぎで ComboCount=0 / LastPlayedOrigCost=null / NextCardComboFreePass=false にリセット / SuperWild プレイ後の FreePass もリセット / 単独 TurnEndProcessor.Process 呼出 |

### 8-3. 既存テスト拡張

| ファイル | 変更 |
|---|---|
| `State/BattleStateTests.cs` | 3 フィールドの record 等価 / 初期値 / `with` 式更新 |
| `Engine/BattleEnginePlayCardTests.cs` | 既存 fixture の `BattleState` 初期化に 3 フィールドを追加（コンパイルエラー解消）+ コンボ無関係（LastOrigCost=null & Keywords=null）の場合の挙動が 10.2.A 既存と一致することを確認 |
| `Engine/BattleEngineEndTurnTests.cs` | コンボリセットの assertion 追加（既存テストフィクスチャ後に SuperWild プレイ → EndTurn → 3 フィールドリセット確認） |
| `Engine/BattleEngineStartTests.cs` | Start 直後の `ComboCount=0 / LastPlayedOrigCost=null / NextCardComboFreePass=false` 検証 |
| `Engine/BattleDeterminismTests.cs` | コンボ・SetTarget を含む 1 戦闘で seed 同一 → state・event 完全一致 |
| `Fixtures/BattleFixtures.cs` | 全 `BattleState` 生成箇所に 3 フィールド初期化（デフォルト 0 / null / false）伝播。既存ヘルパー（`MakeBattleState` 等）の引数オプション追加 |

合計 想定 5 新規テストファイル + 6 既存テストファイル拡張、~40-60 新規テスト。

### 8-4. ビルド赤期間管理

破壊的変更:

1. `BattleState` の primary constructor に 3 フィールド追加 → 全 `new BattleState(...)` 呼出箇所がコンパイルエラー
   - `BattleEngine.cs` (Start)
   - `BattleFixtures.cs`（テスト fixture 全般）
   - 既存テスト内の直接 `new BattleState(...)` 呼出（あれば）
2. `with` 式での更新は影響なし（追加フィールドはデフォルト値で初期化されない、明示的に渡す必要があるが既存 with 式は他フィールドのみ更新するためコンパイルは通る）

これらを 1 commit でまとめると赤期間が長くなるため、依存順を守って小刻みに進める:

1. `BattleState` 3 フィールド追加（Task A）→ ビルド赤
2. `BattleEngine.cs` Start で初期値渡し（Task B）→ Server プロジェクトはまだ赤
3. `Fixtures/BattleFixtures.cs` 更新（Task C）→ ビルド緑（テスト未実装分は赤）
4. 以後 Task ごとに新テスト → 実装 → 緑 → commit

詳細順序は plan に記載。

### 8-5. テスト実行コマンド

- 1 ファイル単位: `dotnet test --filter FullyQualifiedName~<TestClass>`
- 全 Battle: `dotnet test --filter FullyQualifiedName~Battle`
- 全体: `dotnet build && dotnet test`

### 8-6. SetTarget の Determinism

`SetTarget` は `IRng` を取らない決定論的関数。`BattleDeterminismTests` で「SetTarget を挟んだ 1 戦闘も同 seed で完全一致」を確認する。

---

## 9. スコープ外（再確認）

### 9-1. Phase 10.2.C では触らない

- 残り effect 8 種（heal / draw / discard / upgrade / exhaustCard / exhaustSelf / retainSelf / gainEnergy）→ 10.2.D
- 召喚 system / SummonHeld / Lifetime / PowerCards → 10.2.D
- カード移動 5 段優先順位（exhaustSelf / Power / Unit / retainSelf / Discard）→ 10.2.D
- レリック 4 新 Trigger（OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）発火 / Implemented スキップ → 10.2.E
- ポーション戦闘内発動（`UsePotion` API）/ `BattleOnly` 戦闘外スキップ → 10.2.E
- `BattlePlaceholder` 削除 / `RunState.ActiveBattle` 型切替 / save schema v8 → 10.5
- `BattleHub` / `BattleStateDto` / `BattleEventDto` → 10.3
- `BattleScreen.tsx` ポート → 10.4
- `PlayCard` 引数経由の暗黙対象切替への生存・範囲チェック追加（10.2.A 既存挙動を維持）
- `BattleEventKind.TargetChanged` 等のイベント追加（不要と判断）
- 自動詠唱経路（コスト null カード等）のコンボ非更新ロジック → Phase 11+
- ラン側操作系カードのコスト軽減レリック発動 → Phase 11+

### 9-2. Phase 10.2.C 完了後の状態

- `BattleState` に `ComboCount` / `LastPlayedOrigCost` / `NextCardComboFreePass` の 3 フィールドが追加され、コンボ機構が動作
- `BattleEngine.PlayCard` がコンボ判定（通常階段 / Wild / SuperWild / FreePass / リセット直後）を正しく処理し、コスト軽減・comboMin per-effect filter も実装
- `BattleEngine.SetTarget` が第 5 の public static API として公開、Phase=PlayerInput 限定 + 生存・範囲バリデーション付き
- `TurnEndProcessor` がターン終了時にコンボ 3 フィールドをリセット
- `BattleEventKind` は不変（12 値のまま）
- `EffectApplier.Apply` のシグネチャは不変（comboMin filter は `BattleEngine.PlayCard` 側で評価）
- xUnit で「コンボ含む 1 戦闘」が完走（通常階段 / Wild / SuperWild / comboMin filter / SetTarget / EndTurn 跨ぎリセット を含むエンドツーエンド）
- 既存ゲームフロー（`BattlePlaceholder` 経由）は無傷
- 親 spec が 10.2.C の決定事項に合わせて補記済み
- `phase10-2C-complete` タグ push 済み

---

## 10. 親 spec への補記事項

Phase 10.2.C の最終タスクで `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に以下を反映:

1. **§3-1 `BattleState`**:
   - 10.2.C で `ComboCount: int` / `LastPlayedOrigCost: int?` / `NextCardComboFreePass: bool` を追加
   - 配置は `EncounterId` の直前（`SummonHeld` / `PowerCards` は 10.2.D で追加されるまで未追加）
   - 初期値は `0 / null / false`（`BattleEngine.Start` および `TurnEndProcessor.Process` 後）

2. **§4-6 ターン終了処理**:
   - 10.2.C で `TurnEndProcessor.Process` がコンボ 3 フィールドのリセットを実行
   - `NextCardComboFreePass` も SuperWild の効果としてターン跨ぎでリセット（親 spec §4-6 step 4 既述だが、実装が 10.2.C で入った旨補記）
   - `OnTurnEnd` レリック発動（step 3）/ `retainSelf` 対応の手札整理（step 5）は後続 sub-phase

3. **§5-1 `EffectApplier` 補記**:
   - 10.2.C で `effect.ComboMin` per-effect filter は **`BattleEngine.PlayCard` 側**で評価
   - `EffectApplier.Apply` のシグネチャは不変（カードプレイ以外の経路で comboMin を見ないため）

4. **§6 コンボ機構**:
   - 10.2.C で `BattleEngine.PlayCard` 内に実装
   - `actualCost` 算定では `BattleCardInstance.CostOverride` を **無視**（コスト軽減前の元コストで階段判定、`payCost` 算定では CostOverride を反映）
   - SuperWild の `NextCardComboFreePass` 規則: 「自身が SuperWild なら true / それ以外なら消費して false」を 1 行で表現（`newFreePass = isSuperWild`）
   - Energy 不足の例外チェックはコンボ判定後（軽減で payCost=0 になった場合 Energy 0 でもプレイ可能）

5. **§7-3 `SetTarget`**:
   - 10.2.C で `BattleEngine.SetTarget(state, side, slotIndex) → BattleState` を第 5 の public static API として追加
   - Phase=PlayerInput 限定（他 Phase で例外）
   - 範囲外 / 死亡スロット指定で例外
   - 戻り値は `BattleState` 単体、`BattleEvent` 発火なし
   - `PlayCard` 引数経由の暗黙対象切替（10.2.A 既存）も維持。両者の生存・範囲チェック整合は 10.2.D 以降で `UsePotion` 追加時に再考

これら 5 項目は Phase 10.2.C 内で発生した設計判断の追記。コードと spec の乖離を残さない。

---

## 11. memory feedback ルールの遵守チェックリスト

実装中・レビュー時に確認する 2 項目（`memory/feedback_battle_engine_conventions.md`）:

- [ ] `BattleOutcome` 参照は今回新規には基本的に発生しない（コンボ・SetTarget・TurnEndProcessor は Outcome を変更しない）。新規参照箇所が出たら必ず `RoguelikeCardGame.Core.Battle.State.BattleOutcome` の fully qualified
- [ ] `state.Allies` / `state.Enemies` への書き戻しは今回 effect 経路（既存 `EffectApplier`）以外で発生しない。`BattleEngine.PlayCard` の effect ループでの `caster = s.Allies[0]` 再 fetch は 10.2.A/B 既存パターンを維持
- [ ] `BattleEngine.PlayCard` のコンボ更新は `state with { ComboCount = ..., LastPlayedOrigCost = ..., NextCardComboFreePass = ..., Energy = ... }` のフラットフィールド更新で実施。Allies / Enemies 配列を触らない

---

## 参照

- 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン spec: [`2026-04-26-phase10-2B-statuses-design.md`](2026-04-26-phase10-2B-statuses-design.md)
- 直前マイルストーン plan: [`../plans/2026-04-26-phase10-2B-statuses.md`](../plans/2026-04-26-phase10-2B-statuses.md)
- ロードマップ: [`../plans/2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md)
- memory feedback: `memory/feedback_battle_engine_conventions.md`
