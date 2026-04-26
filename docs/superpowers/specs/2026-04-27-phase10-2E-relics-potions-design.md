# Phase 10.2.E — レリック 4 新 Trigger 発火 + UsePotion 戦闘内 設計

> 作成日: 2026-04-27
> 対象フェーズ: Phase 10.2.E（Phase 10.2 サブマイルストーン 5 番目 / 全 5 段階・最終）
> 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
> 直前マイルストーン spec: [`2026-04-26-phase10-2D-effects-summon-design.md`](2026-04-26-phase10-2D-effects-summon-design.md)
> 直前マイルストーン plan: [`../plans/2026-04-26-phase10-2D-effects-summon.md`](../plans/2026-04-26-phase10-2D-effects-summon.md)
> 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html`

## ゴール

10.2.A〜D で完成した「`BattleEngine` 5 公開 API + コンボ + 状態異常 + 対象指定 + 9 effect actions + 召喚 system + カード移動 5 段優先順位」基盤に、以下 3 領域を追加する:

1. **レリック 4 新 Trigger 発火**: `OnBattleStart` / `OnTurnStart` / `OnTurnEnd` / `OnCardPlay` / `OnEnemyDeath` を全 6 サイトで発火
2. **`BattleEngine.UsePotion` 第 6 公開 API**: 戦闘内ポーション使用、Phase=PlayerInput 限定、cost なし、コンボ更新なし、捨札移動なし
3. **`BattleSummary.ConsumedPotionIds` + `RunState.Potions` 反映**: Finalize で消費スロットを `RunState` に反映

10.2.E 完了で **Phase 10.2 全体（Core バトルロジック完成）**。10.3 で Server/SignalR、10.4 で Client、10.5 で `BattlePlaceholder` 退役 + 戦闘外 UsePotion UI。

10.2.A〜D の 5 公開 API（`Start` / `PlayCard` / `EndTurn` / `SetTarget` / `Finalize`）のうち、`Start` のシグネチャが `(BattleState, IReadOnlyList<BattleEvent>)` 戻り値に変更（破壊的変更）。`PlayCard` / `EndTurn` / `Finalize` のシグネチャは不変。`UsePotion` が第 6 公開 API として追加される。

`BattlePlaceholder` 経由の既存ゲームフロー（敵タイル進入 → 即勝利ボタン → 報酬画面）は無傷で動作させ続ける。

## 完了判定

- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（10.2.D 完了時の Core テスト + 10.2.E 追加分）
- `BattleState` に `OwnedRelicIds: ImmutableArray<string>` / `Potions: ImmutableArray<string>` の 2 フィールド追加
- `BattleSummary` に `ConsumedPotionIds: ImmutableArray<string>` の 1 フィールド追加
- `BattleEventKind` に `UsePotion = 19` 追加（計 20 値）
- `BattleEngine` 公開 API が 6 つ（`Start` / `PlayCard` / `EndTurn` / `SetTarget` / `UsePotion` / `Finalize`）
- `BattleEngine.Start` が `(BattleState, IReadOnlyList<BattleEvent>)` を返す（破壊的変更）
- `RelicTriggerProcessor` 新設、4 Trigger 統一発火 + 所持順 (RunState.Relics 配列順) + `Implemented:false` skip + caster=hero 検索
- 全 6 発火サイト統合済み:
  - `BattleEngine.Start` 末尾 → OnBattleStart
  - `BattleEngine.PlayCard` effect 適用後・カード移動前 → OnCardPlay
  - `TurnStartProcessor.Process` step 8 (Draw 後 / TurnStart event 前) → OnTurnStart
  - `TurnEndProcessor.Process` step 3 (AttackPool reset 後 / コンボリセット前) → OnTurnEnd
  - `PlayerAttackingResolver.Resolve` 1 攻撃発射ごと → OnEnemyDeath
  - `TurnStartProcessor.ApplyPoisonTick` 敵毒死時 → OnEnemyDeath
- `BattleEngine.UsePotion(state, potionIndex, targetEnemyIndex?, targetAllyIndex?, rng, catalog)` 実装
- `BattleEngine.Finalize` で `state.Potions` を `RunState.Potions` に丸ごとコピー、`BattleSummary.ConsumedPotionIds` は diff 派生
- `DrawHelper` 共通化（W5 修正）/ `summon` InstanceId 決定的 RNG ベース（W4 修正）
- 既存 `BattlePlaceholder` 経由のフロー無傷（手動プレイ確認）
- 親 spec §3-1 / §3-3 / §3-5 / §4-1 / §4-2 / §4-6 / §5-1 / §5-6 / §7-3 / §8-1 / §8-2-1 / §8-2-2 / §9-7 に 10.2.E で発生した設計判断を補記済み
- `phase10-2E-complete` タグが切られ origin に push 済み

---

## 1. アーキテクチャ概要

### 1-1. Phase 10.2 全体の中での位置付け

| サブ phase | 範囲 | 状態 |
|---|---|---|
| 10.2.A | 基盤 + `attack`/`block` + Phase 進行 + Victory/Defeat | ✅ 完了 |
| 10.2.B | 状態異常 6 種 + 遡及計算 + buff/debuff + tick + omnistrike | ✅ 完了 |
| 10.2.C | コンボ + `SetTarget` + comboMin filter | ✅ 完了 |
| 10.2.D | 9 effect actions + 召喚 system + カード移動 5 段優先順位 | ✅ 完了 |
| **10.2.E**（本 spec） | **レリック 4 新 Trigger 発火 + Implemented スキップ + UsePotion 戦闘内 + ConsumedPotion 反映** | 本フェーズ |

10.2.E 完了で **Phase 10.2 全体（Core バトルロジック完成）**。後続 10.3〜10.5 は Server / Client / cleanup の統合フェーズ。

### 1-2. 共存戦略

10.2.A〜D と同じ。新 `BattleEngine` は **pure Core API として独立**し、`NodeEffectResolver` は引き続き `BattlePlaceholder.Start` を呼ぶ（既存ゲームフローは無傷）。`BattleEngine` は xUnit でしかテストされない。

### 1-3. ファイル構成（10.2.E 完了時の差分）

```
src/Core/Battle/
├── State/
│   └── BattleState.cs              [修正] +OwnedRelicIds / Potions snapshot
├── Engine/
│   ├── BattleEngine.cs             [修正] Start で OwnedRelicIds/Potions snapshot, OnBattleStart 発火, Start シグネチャ変更
│   ├── BattleEngine.PlayCard.cs    [修正] effect ループ後・カード移動前に OnCardPlay 発火
│   ├── BattleEngine.EndTurn.cs     [修正] TurnEndProcessor / TurnStartProcessor の events 統合
│   ├── BattleEngine.UsePotion.cs   [新] 第 6 公開 API
│   ├── BattleEngine.Finalize.cs    [修正] state.Potions 全置換 + ConsumedPotionIds diff
│   ├── BattleSummary.cs            [修正] +ConsumedPotionIds: ImmutableArray<string>
│   ├── RelicTriggerProcessor.cs    [新] 統一ヘルパー（4 Trigger + 所持順 + Implemented スキップ + caster=hero）
│   ├── DrawHelper.cs               [新, W5 修正] Hand 増分の共通ヘルパー
│   ├── EffectApplier.cs            [修正, W4] summon の InstanceId を rng ベースに → 衝突回避
│   ├── TurnStartProcessor.cs       [修正] catalog 引数追加, OnTurnStart 発火 (step 8), DrawHelper 使用, ApplyPoisonTick で OnEnemyDeath
│   ├── TurnEndProcessor.cs         [修正] rng 引数追加, OnTurnEnd 発火 (step 3)
│   └── PlayerAttackingResolver.cs  [修正] catalog 引数追加, 1 攻撃発射ごとに新規死亡敵を slot 順 OnEnemyDeath 発火

tests/Core.Tests/Battle/
├── State/
│   └── BattleStateInvariantTests.cs            [修正] +OwnedRelicIds / Potions 不変条件
├── Engine/
│   ├── RelicTriggerProcessorTests.cs           [新] 4 Trigger 単体 + 所持順 + Implemented スキップ + caster
│   ├── BattleEngineUsePotionTests.cs           [新] 第 6 API 単体テスト
│   ├── BattleEngineStartRelicTests.cs          [新] OnBattleStart / OnTurnStart 発火 from Start
│   ├── BattleEnginePlayCardOnCardPlayTests.cs  [新] OnCardPlay 発火 + 順序
│   ├── TurnStartProcessorOnTurnStartTests.cs   [新] OnTurnStart 発火位置・順序
│   ├── TurnEndProcessorOnTurnEndTests.cs       [新] OnTurnEnd 発火位置・順序
│   ├── PlayerAttackingResolverOnEnemyDeathTests.cs  [新] 1 攻撃で複数死亡時の slot 順発火
│   ├── PoisonTickOnEnemyDeathTests.cs          [新] 毒死時の OnEnemyDeath 発火
│   ├── BattleEngineFinalizeConsumedPotionTests.cs   [新] Potions 反映 + ConsumedPotionIds diff
│   ├── DrawHelperTests.cs                      [新] W5 共通化テスト
│   ├── EffectApplierSummonInstanceIdTests.cs   [新] W4 RNG ベース ID 衝突回避
│   ├── BattleEngineStartTests.cs               [修正] 戻り値 tuple 追従, OwnedRelicIds/Potions snapshot 検証
│   ├── BattleEnginePlayCardTests.cs            [修正] fixture 追従
│   ├── BattleEngineEndTurnTests.cs             [修正] fixture + sig 変更追従
│   ├── BattleEngineFinalizeTests.cs            [修正] BattleSummary.ConsumedPotionIds 追従
│   ├── BattleEngineSetTargetTests.cs           [修正] fixture 追従
│   ├── PlayerAttackingResolverTests.cs         [修正] catalog 引数追加追従
│   ├── EnemyAttackingResolverTests.cs          [修正] 既存挙動維持
│   ├── TurnStartProcessorTests.cs              [修正] catalog 引数追加, step 8 確認
│   ├── TurnStartProcessorTickTests.cs          [修正] catalog 引数追加追従
│   ├── TurnStartProcessorLifetimeTests.cs      [修正] catalog 引数追加追従
│   ├── TurnEndProcessorTests.cs                [修正] rng/catalog 引数追加, step 3 確認
│   ├── TurnEndProcessorRetainSelfTests.cs      [修正] rng/catalog 引数追加追従
│   ├── TurnEndProcessorComboResetTests.cs      [修正] rng/catalog 引数追加追従
│   ├── EffectApplierSummonTests.cs             [修正] InstanceId アサーションを prefix のみに緩和
│   ├── BattleDeterminismTests.cs               [修正] レリック + UsePotion 含む 1 戦闘 seed 一致テスト追加
│   └── (既存 fixture で `new BattleState(...)` 呼出全箇所 → +OwnedRelicIds/Potions 追従)
└── Fixtures/
    └── BattleFixtures.cs                       [修正] RelicDefinition factory + PotionDefinition factory + MinimalCatalog 拡張
```

### 1-4. namespace

10.2.E で新 namespace は追加しない。`RelicTriggerProcessor.cs` / `DrawHelper.cs` / `BattleEngine.UsePotion.cs` はすべて `RoguelikeCardGame.Core.Battle.Engine` 配下。

### 1-5. memory feedback の遵守（Phase 10.2 系列で再徹底）

`memory/feedback_battle_engine_conventions.md` の 2 ルール:

1. **`BattleOutcome` は常に fully qualified**: 10.2.E の発火パスで Outcome 確定箇所は無い（Q6 確定、レリック effect は Pool 加算 / heal / status / draw / etc. で直接死を引き起こさない）が、念のため新規 Outcome 参照箇所はすべて `RoguelikeCardGame.Core.Battle.State.BattleOutcome.X` で書く
2. **`state.Allies` / `state.Enemies` への書き戻しは InstanceId で検索**: 10.2.E で新規発生する loop:
   - `RelicTriggerProcessor.Fire` 内で `caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero")` で索引（DefinitionId 検索 = InstanceId 検索の精神に準拠）
   - 各 effect 適用は `EffectApplier.Apply` 委譲で、内部の InstanceId 検索パターン (10.2.D 既存) に従う
   - `PlayerAttackingResolver` の `enemyIdsBefore` snapshot は `InstanceId` HashSet で取得、`DetectNewlyDead` も InstanceId ベース
   - `BattleEngine.UsePotion` 内で `caster = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero")` で索引
   - `BattleEngine.Finalize` で `hero = state.Allies.FirstOrDefault(...)` で索引

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
    ImmutableArray<BattleCardInstance> SummonHeld,
    ImmutableArray<BattleCardInstance> PowerCards,
    int ComboCount,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    ImmutableArray<string> OwnedRelicIds,         // ← 10.2.E 追加: Start で snapshot、戦闘中不変
    ImmutableArray<string> Potions,               // ← 10.2.E 追加: Start で snapshot、UsePotion で slot[i]=""
    string EncounterId);
```

配置: `NextCardComboFreePass` の直後、`EncounterId` の前。これで親 spec §3-1 の最終形に近づく。

### 2-2. `BattleSummary` の 1 フィールド追加

```csharp
public sealed record BattleSummary(
    int FinalHeroHp,
    BattleOutcome Outcome,
    string EncounterId,
    ImmutableArray<string> ConsumedPotionIds);    // ← 10.2.E 追加: before vs state の diff
```

### 2-3. `BattleEventKind` の 1 値追加

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
    Heal          = 12,
    Draw          = 13,
    Discard       = 14,
    Upgrade       = 15,
    Exhaust       = 16,
    GainEnergy    = 17,
    Summon        = 18,
    UsePotion     = 19,    // ← 10.2.E 追加
}
```

#### Event ペイロード慣例

| Kind | Caster | Target | Amount | Note | CardId |
|---|---|---|---|---|---|
| `UsePotion` | hero | null | `potionIndex` (slot) | null（内訳の effect 適用は既存 effect event で表現） | `PotionDefinition.Id` |

**レリック発動の event 表現**: 専用 `RelicTriggered` event は **新設しない**。レリック effect が `EffectApplier.Apply` 経由で適用された結果の events（`Heal` / `GainBlock` / `AttackFire` 等の既存 Kind）を `Note: "relic:<relicId>"` で関連付ける。理由:

- 既存 Kind で十分表現できる（レリックの効果は通常 effect と同じ）
- `RelicTriggered` を独立 Kind にすると Client 側で 2 重ハンドリングが必要
- `Note` フィールドで起源を識別可能（既存 `"poison"` / `"lifetime"` 等と同パターン）

ただし「レリックが発動したが effect が空」のケース（`Implemented:false` や effect 配列が空）の検出のため、`RelicTriggerProcessor` 内では event を発火しない（呼出側が Note で「`relic:<id>` 由来 event」を後付けで識別）。

### 2-4. `CombatActor` 不変

10.2.E で `CombatActor` は変更しない。

### 2-5. 不変条件（10.2.E 追加分）

10.2.A〜D 完了時の不変条件に加えて:

- `BattleState.OwnedRelicIds` は戦闘中不変（Start 後は append/remove なし）
- `BattleState.Potions.Length == before.PotionSlotCount`（Start 時に snapshot）
- `BattleState.Potions[i] == ""` または `BattleState.Potions[i]` は catalog に存在する potion ID
- 各 `BattleState.OwnedRelicIds[i]` は catalog に存在する relic ID
- `Finalize` 時、`BattleSummary.ConsumedPotionIds` は `before.Potions[i] != "" && state.Potions[i] == ""` を満たす i に対する `before.Potions[i]` を i 順に列挙

`BattleStateInvariantTests` にこれら不変条件を追加。

---

## 3. `RelicTriggerProcessor` のロジック

### 3-1. ファサード API

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 戦闘内 4 Trigger（OnBattleStart / OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）の
/// レリック発動を統一的に処理する internal static helper。
/// 所持順発動 (RunState.Relics 配列順) + Implemented:false スキップ + caster=Allies hero を集約。
/// 親 spec §8-2 / 10.2.E spec §3 参照。
/// </summary>
internal static class RelicTriggerProcessor
{
    /// <summary>
    /// OnBattleStart / OnTurnStart / OnTurnEnd / OnCardPlay 共通の Trigger 発動。
    /// state.OwnedRelicIds を所持順に走査し、catalog で trigger 一致のレリックを見つけたら
    /// effect 配列を EffectApplier.Apply で順次適用。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) Fire(
        BattleState state,
        RelicTrigger trigger,
        DataCatalog catalog,
        IRng rng,
        int orderStart);

    /// <summary>
    /// OnEnemyDeath 専用。死亡敵の InstanceId を渡し、
    /// caster は変わらず hero。
    /// 注: deadEnemyInstanceId は現状 Note に乗せるだけで、
    /// 10.2.E 範囲のレリック effect は死亡敵を直接参照しない（attack/block/heal/etc. への加算のみ）。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnEnemyDeath(
        BattleState state,
        string deadEnemyInstanceId,
        DataCatalog catalog,
        IRng rng,
        int orderStart);
}
```

### 3-2. 処理フロー（共通）

```
Fire(state, trigger, catalog, rng, orderStart):
  s = state
  events = new List<BattleEvent>()
  order = orderStart

  caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero")
  // hero が存在しなければ no-op (理論上死亡時のみ、その場合は Defeat 確定 → 発火サイトに到達しない)
  if (caster is null || !caster.IsAlive) return (s, events)

  foreach relicId in s.OwnedRelicIds:           // 所持順 (RunState.Relics 配列順)
    if (!catalog.TryGetRelic(relicId, out var def)) continue   // catalog 未登録は silent skip
    if (!def.Implemented) continue                              // Implemented: false は no-op
    if (def.Trigger != trigger) continue

    foreach eff in def.Effects:
      // BattleOnly チェックは戦闘内では skip (戦闘内では全 effect 発動、Q4 確定)
      var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog)
      s = afterEff
      foreach ev in evs:
        // Note prefix で「<前置> + 'relic:<relicId>'」を付与
        var newNote = (ev.Note is null or "")
                      ? $"relic:{relicId}"
                      : $"{ev.Note};relic:{relicId}"
        events.Add(ev with { Order = order, Note = newNote })
        order++
      // 各 effect 適用後、hero を再 fetch（status 変化等が caster に影響する可能性）
      caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero")
      if (caster is null || !caster.IsAlive) break  // hero 死亡 (起こり得ないが防御)

  return (s, events)
```

### 3-3. `FireOnEnemyDeath` の差分

- 上記 `Fire` の `trigger == OnEnemyDeath` 版
- Note に `$"relic:{relicId};deadEnemy:{deadEnemyInstanceId}"` を付与（dead enemy ID も identifier に乗せる）
- effect 適用ロジックは共通

### 3-4. 設計判断

#### 3-4-1. caster = `Allies.FirstOrDefault(a => a.DefinitionId == "hero")` で索引

`s.Allies[0]` 直接参照ではなく `DefinitionId == "hero"` で検索する理由:
- 親 spec §8-2-2 で「caster は hero」と明記
- 10.2.D code review W3 で「`Allies[0]` 直接参照は hero 死亡時の脆弱性」が指摘済み
- hero 不在時の防御 (`null` チェックで no-op) を明示
- memory feedback ルール「InstanceId 検索」の精神に沿う（DefinitionId 検索 ≒ 安全な索引）

#### 3-4-2. effect 適用ごとの caster 再 fetch

`EffectApplier.Apply` 委譲中に hero の status / HP / Block が変わる可能性があるため、各 effect 後に再 fetch。10.2.D の `BattleEngine.PlayCard` 既存パターンに準拠。

#### 3-4-3. catalog 未登録 / Implemented:false / trigger 不一致は silent skip

例外を投げない。理由:
- catalog 未登録: 既存 `NonBattleRelicEffects.ApplyOnMapTileResolved` と同じ defensive 挙動
- Implemented:false: 既存 `NonBattleRelicEffects` 全メソッドと同じ挙動
- trigger 不一致: 同レリックが複数 trigger で誤呼出されないための安全網

#### 3-4-4. Note prefix の構造

Event の `Note` フィールドに `relic:<relicId>` を付与することで、Client / テストが「どのレリック由来の effect か」を識別可能。複数の Note 要素は `;` で区切る (例: `"poison;relic:burning_blood"`)。

#### 3-4-5. `RelicTriggered` 専用 event は新設しない

§2-3 で記述済み。理由は同節参照。

### 3-5. メモリ feedback ルールの遵守

- `BattleOutcome` 参照は §3-2 / §3-3 内に無い（Outcome 確定経路を持たない、Q6 確定）
- `state.Allies` への書き戻し: `EffectApplier.Apply` 内既存パターン（InstanceId 検索）に委譲、`RelicTriggerProcessor` 自身は Allies をいじらない
- `state.Enemies` への書き戻し: 同上

---

## 4. `UsePotion` 公開 API

### 4-1. 公開 API シグネチャ

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// 戦闘内でポーションを使用する。第 6 公開 API。
    /// Phase=PlayerInput 限定、cost なし、コンボ更新なし、捨札移動なし。
    /// effects は EffectApplier.Apply で順次適用、消費スロットは空文字に置換。
    /// </summary>
    /// <param name="state">現在のバトル状態</param>
    /// <param name="potionIndex">使用するポーションのスロット index (0 ~ Potions.Length-1)</param>
    /// <param name="targetEnemyIndex">対象敵スロット index (省略時は state.TargetEnemyIndex を使用)</param>
    /// <param name="targetAllyIndex">対象味方スロット index (省略時は state.TargetAllyIndex を使用)</param>
    /// <param name="rng">乱数 (random scope effect / discard random 等で使用)</param>
    /// <param name="catalog">DataCatalog (potion / card / unit 定義参照)</param>
    /// <returns>更新後 BattleState + 発火 events</returns>
    public static (BattleState, IReadOnlyList<BattleEvent>) UsePotion(
        BattleState state,
        int potionIndex,
        int? targetEnemyIndex,
        int? targetAllyIndex,
        IRng rng,
        DataCatalog catalog);
}
```

### 4-2. 処理フロー

```
1. Phase != PlayerInput なら InvalidOperationException
   (PlayerAttacking / EnemyAttacking / Resolved 中は使用不可)

2. potionIndex < 0 || potionIndex >= state.Potions.Length なら InvalidOperationException

3. var potionId = state.Potions[potionIndex]
   if (potionId == "") throw InvalidOperationException("empty potion slot")

4. catalog.TryGetPotion(potionId, out var def) でなければ InvalidOperationException

5. caster = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero")
   if (caster is null || !caster.IsAlive) throw InvalidOperationException
   (理論上は Phase=PlayerInput 中に hero 死亡はない)

6. // ターゲット index を一時更新（PlayCard と同じパターン）
   var s = state with {
     TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
     TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
   }

7. // UsePotion event 発火
   var events = new List<BattleEvent> {
     new(BattleEventKind.UsePotion, Order: 0,
         CasterInstanceId: caster.InstanceId,
         CardId: def.Id,
         Amount: potionIndex)
   }
   int order = 1

8. // effects を順次適用（戦闘内では BattleOnly チェックなし、全 effect 適用）
   foreach eff in def.Effects:
     var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog)
     s = afterEff
     foreach ev in evs:
       events.Add(ev with { Order = order })
       order++
     caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero")
     if (caster is null || !caster.IsAlive) break

9. // ポーション消費: スロットを空文字に
   s = s with { Potions = s.Potions.SetItem(potionIndex, "") }

10. return (s, events)
```

### 4-3. 設計判断

#### 4-3-1. cost / コンボ更新 / 捨札移動なし

- **cost**: ポーションは Energy を消費しない
- **コンボ更新**: `LastPlayedOrigCost` / `ComboCount` / `NextCardComboFreePass` を変更しない（カードプレイではない）
- **捨札移動**: ポーションは「カード」ではないので手札・捨札・除外・PowerCards 等に移動しない。スロット消費のみ
- **5 段優先順位なし**: カード移動 logic を通らない（exhaustSelf / retainSelf 等のマーカーは持たない前提、ポーションでこれらを書くべきではない仕様）

#### 4-3-2. ターゲット index の一時更新

`PlayCard` 既存パターンに準拠。`targetEnemyIndex` / `targetAllyIndex` 引数が non-null なら state を更新、null なら既存の `state.TargetEnemyIndex` / `TargetAllyIndex` を保持。

#### 4-3-3. 戦闘内では `BattleOnly` チェック skip

戦闘内 UsePotion は全 effect を適用（`BattleOnly: true` でも実行）。`BattleOnly` は戦闘外 UI (10.5) のみで意味を持つ。

#### 4-3-4. summon 等のポーション effect も自然に動作

ポーションが `summon` action を含む場合、`EffectApplier.ApplySummon` 経由で `state.Allies` に追加。`AssociatedSummonHeldInstanceId` は `null` のまま（カードではないので紐付け先カードがない、`SummonCleanup` で Hold カード移動も発生しない）。Lifetime tick / 通常死亡時の挙動は通常召喚と同じ（actor の HP=0 化、event 発火）。

#### 4-3-5. UsePotion の発動が他のレリックを誘発しない

10.2.E スコープで「OnPotionUse」レリック Trigger は **存在しない**（`RelicTrigger` enum に該当値なし）。将来追加する場合は別 phase。

#### 4-3-6. 連続使用

同 turn 内に複数のポーションを使うことは可能（spec 上の制限なし）。各使用は独立に events を返す。

### 4-4. 例外仕様まとめ

| 状況 | 例外 |
|---|---|
| Phase != PlayerInput | `InvalidOperationException`: "UsePotion requires Phase=PlayerInput, got X" |
| potionIndex 範囲外 | `InvalidOperationException`: "potionIndex N out of range [0, M)" |
| 該当 slot が空 ("") | `InvalidOperationException`: "potion slot N is empty" |
| catalog に potion 未登録 | `InvalidOperationException`: "potion 'id' not in catalog" |
| hero 不在 / 死亡 | `InvalidOperationException`: "hero not available" (理論上発生しない) |

---

## 5. 発火サイト統合

各サイトでの `RelicTriggerProcessor.Fire` / `FireOnEnemyDeath` 呼出を仕様化。

### 5-1. `BattleEngine.Start` 末尾 — `OnBattleStart`

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Start(
    RunState run, string encounterId, IRng rng, DataCatalog catalog)
{
    // ...既存初期化処理 (hero/enemies 生成, deck shuffle, 初期 BattleState 構築)...

    initial = initial with {
        OwnedRelicIds = run.Relics.ToImmutableArray(),
        Potions = run.Potions,
    }

    var events = new List<BattleEvent>();
    int order = 0;

    // BattleStart event 発火
    events.Add(new BattleEvent(BattleEventKind.BattleStart, Order: order++,
        Note: encounterId));

    // ターン 1 開始処理（TurnStart 内で OnTurnStart レリック発動）
    var (afterTurnStart, evsTurnStart) = TurnStartProcessor.Process(initial, rng, catalog);
    foreach (var ev in evsTurnStart) { events.Add(ev with { Order = order++ }); }

    // OnBattleStart レリック発動（TurnStart 後、親 spec §4-1 の "戦闘開幕の初期化処理の最後"）
    var (afterBattleStart, evsBattleStart) =
        RelicTriggerProcessor.Fire(afterTurnStart, RelicTrigger.OnBattleStart, catalog, rng, orderStart: order);
    foreach (var ev in evsBattleStart) { events.Add(ev with { Order = order++ }); }

    return (afterBattleStart, events);
}
```

**`Start` シグネチャ変更**: `BattleState Start(...)` → `(BattleState, IReadOnlyList<BattleEvent>) Start(...)`。10.2.A の単純化からの破壊的変更。10.3 で BattleHub が events を Client に push するため、最初から events を返す設計に統一。

**`TurnStartProcessor.Process` シグネチャ変更**: 引数に `DataCatalog catalog` を追加（既存は `(state, rng)`）。理由: §5-2 で OnTurnStart レリック発動には catalog が必要。

### 5-2. `TurnStartProcessor.Process` 末尾 — `OnTurnStart`

step 順序（親 spec §4-2 / 10.2.D 完了時の順序）に OnTurnStart を追加:

```
TurnStartProcessor.Process flow (10.2.E):
1. Turn+1
2. Poison tick (+ SummonCleanup, ApplyPoisonTick 内で OnEnemyDeath 発火 → §5-6)
3. Death detection / TargetingAutoSwitch / Outcome 確定
4. Status countdown
5. Lifetime tick (+ SummonCleanup)
6. Energy = EnergyMax
7. Draw（DrawHelper 経由 → §5-7 / W5）
8. OnTurnStart レリック発動 ← 10.2.E 追加
9. TurnStart event 発火
```

**OnTurnStart の挿入位置 = step 8 (Draw 後 / TurnStart event 前)**。理由:
- 親 spec §4-2 step 7 を「Draw 後」と解釈（10.2.D code review §4 で曖昧性指摘あり、ここで明確化）
- レリックが `attack` を Pool 加算する場合、当ターンの PlayerAttacking で発射される（自然な期待）
- レリックが `gainEnergy` を発動する場合、Draw 後の Energy をさらに増やす

**シグネチャ変更**: `(BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng, DataCatalog catalog)`

```csharp
// step 8 (10.2.E 追加)
var (afterRelic, evsRelic) =
    RelicTriggerProcessor.Fire(s, RelicTrigger.OnTurnStart, catalog, rng, orderStart: order);
s = afterRelic;
foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

// step 9
events.Add(new BattleEvent(BattleEventKind.TurnStart, Order: order++, Note: $"turn={s.Turn}"));
```

### 5-3. `TurnEndProcessor.Process` 内 — `OnTurnEnd`

step 順序（親 spec §4-6）:

```
TurnEndProcessor.Process flow (10.2.E):
1. 両陣営 Block リセット
2. アタック値リセット
3. OnTurnEnd レリック発動 ← 10.2.E 追加
4. コンボリセット (ComboCount=0, LastPlayedOrigCost=null, NextCardComboFreePass=false)
5. 手札整理（retainSelf-aware, 10.2.D 既存）
```

**OnTurnEnd の挿入位置 = step 3 (AttackPool reset 後 / コンボリセット前)**。理由:
- 親 spec §4-6 step 3 と一致
- AttackPool reset 後なので、レリックが `attack` を Pool 加算すれば次ターン PlayerAttacking で発射される
- コンボリセット前なので、レリックが `attack` を発動した瞬間の `ComboCount` はターン中の最終値を反映

**シグネチャ変更**: `(BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng, DataCatalog catalog)`
- `IRng rng` を追加（OnTurnEnd レリックが random scope effect を持つ可能性）

### 5-4. `BattleEngine.PlayCard` の effect 適用後 — `OnCardPlay`

```csharp
// 既存: foreach effects loop (10.2.D)
// ...

// 10.2.E 追加: OnCardPlay レリック発動（effect 適用後・カード移動前）
var (afterRelic, evsRelic) =
    RelicTriggerProcessor.Fire(s, RelicTrigger.OnCardPlay, catalog, rng, orderStart: order);
s = afterRelic;
foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

// 10.2.D 既存: 5 段優先順位カード移動
bool hasExhaustSelf = effects.Any(e => e.Action == "exhaustSelf");
// ...
```

**注**: OnCardPlay レリック自身も `summon` action を持ちうるが、これは `BattleEngine.PlayCard` のカード自身の `summonSucceeded` フラグには影響しない（フラグはカード effect ループ内でのみセット）。レリック由来の召喚は `state.Allies` に追加されるが、カード自身の `Hand → SummonHeld` 移動判定には関与しない（カードが Unit type かつ自身の effect で summon が成功した場合のみ SummonHeld へ）。

### 5-5. `PlayerAttackingResolver` — 1 攻撃発射ごとに `OnEnemyDeath`

Q3-B 確定通り、1 攻撃発射の最後に新規死亡敵を slot 順 fire。

```
PlayerAttackingResolver.Resolve flow (10.2.E):
foreach ally in state.Allies.OrderBy(SlotIndex):
  if (!ally.IsAlive) continue;

  // Single 攻撃
  if (ally.AttackSingle.Sum > 0):
    var enemyIdsBefore = SnapshotEnemyAliveIds(s)        // InstanceId set
    var (afterFire, evs) = DealDamageHelper.Apply(s, ally, AttackSingle, ...)
    s = afterFire
    AddEvents(events, evs, ref order)

    // 新規死亡検出 + slot 順発火
    var newlyDeadIds = DetectNewlyDead(s, enemyIdsBefore)  // slot 順ソート
    foreach deadId in newlyDeadIds:
      var (afterRelic, evsRelic) = RelicTriggerProcessor.FireOnEnemyDeath(s, deadId, catalog, rng, order)
      s = afterRelic
      AddEvents(events, evsRelic, ref order)

  // Random 攻撃 (同様)
  // ...

  // All 攻撃 (同様、複数死亡時に slot 順)
  // ...

// 10.2.D 既存: SummonCleanup
s = SummonCleanup.Apply(s, events, ref order)
```

**ヘルパー追加**:
- `SnapshotEnemyAliveIds(state)`: `state.Enemies.Where(e => e.IsAlive).Select(e => e.InstanceId).ToHashSet()`
- `DetectNewlyDead(state, beforeAliveIds)`: `state.Enemies.Where(e => beforeAliveIds.Contains(e.InstanceId) && !e.IsAlive).OrderBy(e => e.SlotIndex).Select(e => e.InstanceId).ToList()`

これらは `PlayerAttackingResolver` 内 private static helper として実装。

**シグネチャ変更**: `Resolve(BattleState state, IRng rng, DataCatalog catalog)` に catalog 引数追加。

### 5-6. `TurnStartProcessor.ApplyPoisonTick` — 毒死時の `OnEnemyDeath`

```
ApplyPoisonTick flow (10.2.E):
foreach actorId in actorIds:
  CombatActor? actor = FindActor(s, actorId)
  if (actor is null || !actor.IsAlive) continue
  int poison = actor.GetStatus("poison")
  if (poison <= 0) continue

  bool wasAlive = actor.IsAlive
  bool wasEnemy = actor.Side == ActorSide.Enemy
  var updated = actor with { CurrentHp = actor.CurrentHp - poison }
  s = ReplaceActor(s, actorId, updated)

  events.Add(PoisonTick { TargetInstanceId=actorId, Amount=poison, Note="poison" })

  if (wasAlive && !updated.IsAlive):
    events.Add(ActorDeath { TargetInstanceId=actorId, Note="poison" })

    // 10.2.E 追加: 敵の毒死で OnEnemyDeath
    if (wasEnemy):
      var (afterRelic, evsRelic) = RelicTriggerProcessor.FireOnEnemyDeath(s, actorId, catalog, rng, order)
      s = afterRelic
      AddEvents(events, evsRelic, ref order)
```

**シグネチャ変更**: `ApplyPoisonTick(state, events, ref order, catalog, rng)` に `catalog` / `rng` 追加。`TurnStartProcessor.Process` 経由で渡される。

### 5-7. `DrawHelper` 共通化（W5 修正）

`TurnStartProcessor.DrawCards` と `EffectApplier.ApplyDraw` のシャッフルロジック重複を解消:

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class DrawHelper
{
    public const int HandCap = 10;

    /// <summary>
    /// state.Hand に最大 count 枚追加。山札不足時は捨札をシャッフルして補充。
    /// HandCap で打ち切り。実際にドローした枚数を out で返す。
    /// </summary>
    public static BattleState Draw(BattleState state, int count, IRng rng, out int actuallyDrawn);
}
```

呼出側:
- `TurnStartProcessor.DrawCards` (private) 削除、`Process` 内で `s = DrawHelper.Draw(s, DrawPerTurn, rng, out _)`
- `EffectApplier.ApplyDraw` (10.2.D 既存): 既存 inline ロジックを `DrawHelper.Draw(s, eff.Amount, rng, out var drawn)` に置換、`drawn > 0` で `Draw` event 発火

**HandCap 定数の集約**: `TurnStartProcessor.HandCap` / `EffectApplier` のマジックナンバー 10 → `DrawHelper.HandCap` 一元化。

### 5-8. `EffectApplier.ApplySummon` の決定的 ID 生成（W4 修正）

```csharp
// 旧 (10.2.D)
string newInstanceId = $"summon_inst_{state.Turn}_{state.Allies.Length}";

// 新 (10.2.E)
string newInstanceId = $"summon_inst_{state.Turn}_{rng.NextInt(0, 1 << 30):x}";
```

理由:
- レリック由来の召喚（OnTurnStart で `summon` を含むレリック等）が同ターンに発生すると、`Turn + Allies.Length` の組み合わせで衝突可能性
- RNG ベースに切り替え、Determinism Tests は `IRng` 注入で seed 固定 → ID も決定的

**Determinism 影響**:
- `IRng` 注入で seed 固定 → ID も決定的
- 既存 `BattleDeterminismTests.Combat_with_summon_and_heal_is_deterministic` は seed 一致で同 ID 生成 → 影響なし
- ただし、StateJson serialize で具体的な ID 文字列が変わる → "snapshot" 期待値を hardcoded で持つテストがあれば update が必要

**Grep で影響範囲確認**: `summon_inst_` を含む期待値を持つテストの抽出は plan の Task 1 で実施。

**`PlayCard` の `Allies[Allies.Length - 1]` 暗黙前提**: W4 修正で ID 衝突は解消するが、「最後尾 = 直前召喚」の前提（`BattleEngine.PlayCard.cs:129`）は残る。これは 10.2.E スコープ外（複数 summon を 1 カードに持たせる、レリック発動と同時に summon カード play する等のシナリオ）。§9 で Phase 11+ 持ち越しと明記。

### 5-9. シグネチャ変更まとめ（破壊的変更）

| API | 変更前 | 変更後 |
|---|---|---|
| `BattleEngine.Start` | `BattleState Start(...)` | `(BattleState, IReadOnlyList<BattleEvent>) Start(...)` |
| `TurnStartProcessor.Process` | `(BattleState, ...) Process(state, rng)` | `Process(state, rng, catalog)` |
| `TurnEndProcessor.Process` | `Process(state, catalog)` | `Process(state, rng, catalog)` |
| `PlayerAttackingResolver.Resolve` | `Resolve(state, rng)` | `Resolve(state, rng, catalog)` |
| `BattleEngine.UsePotion` | (新設) | `(BattleState, IReadOnlyList<BattleEvent>) UsePotion(...)` |

これらの変更は破壊的だが、テスト fixture / 内部 caller (`BattleEngine.EndTurn` 等) を一括更新する形で吸収。§8 で test 戦略を詳述。

---

## 6. `BattleSummary` / `Finalize` 更新

### 6-1. `BattleSummary` 拡張

```csharp
namespace RoguelikeCardGame.Core.Battle.Engine;

public sealed record BattleSummary(
    int FinalHeroHp,
    BattleOutcome Outcome,
    string EncounterId,
    ImmutableArray<string> ConsumedPotionIds);    // ← 10.2.E 追加
```

### 6-2. `Finalize` 処理フロー

```csharp
public static (RunState, BattleSummary) Finalize(BattleState state, RunState before)
{
    if (state.Phase != BattlePhase.Resolved)
        throw new InvalidOperationException($"Finalize requires Phase=Resolved, got {state.Phase}");

    // 1. hero の最終 HP を取得
    var hero = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero")
               ?? throw new InvalidOperationException("hero not found in Allies");
    int finalHeroHp = Math.Max(0, hero.CurrentHp);

    // 2. ConsumedPotionIds を before vs state.Potions の diff として算出
    var consumed = ImmutableArray.CreateBuilder<string>();
    int slotCount = Math.Min(before.Potions.Length, state.Potions.Length);
    for (int i = 0; i < slotCount; i++)
    {
        if (before.Potions[i] != "" && state.Potions[i] == "")
            consumed.Add(before.Potions[i]);
    }
    var consumedIds = consumed.ToImmutable();

    // 3. RunState を更新（10.2.E 追加: state.Potions を全コピー）
    var nextRun = before with
    {
        CurrentHp = finalHeroHp,
        Potions = state.Potions,                              // 10.2.E 追加: 消費反映
        ActiveBattle = null,                                  // 既存
        Progress = state.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat
                   ? RunProgress.GameOver
                   : before.Progress,
    };

    // 4. BattleSummary 構築
    var summary = new BattleSummary(
        FinalHeroHp: finalHeroHp,
        Outcome: state.Outcome,
        EncounterId: state.EncounterId,
        ConsumedPotionIds: consumedIds);

    return (nextRun, summary);
}
```

### 6-3. 設計判断

#### 6-3-1. `state.Potions` を `RunState.Potions` に丸ごとコピー

`state.Potions` が真実の source。Finalize で全置換することで、戦闘中の `UsePotion` による消費が確実に反映される。

#### 6-3-2. `ConsumedPotionIds` の算出は diff 計算

`state` 単体には「消費した」という履歴は持たず、`before.Potions` との比較で派生計算。利点:
- BattleState のフィールドを増やさない（`state.Potions` だけで完結）
- 「消費 ID」の concept は Finalize 時にだけ意味を持つ → 派生値で十分

順序: スロット index 順 (i=0, 1, 2, ...)。同 ID を 2 スロット消費した場合は 2 回現れる。

#### 6-3-3. `OwnedRelicIds` は反映しない

`state.OwnedRelicIds` は戦闘中不変（§2-5 不変条件）なので、`RunState.Relics` に反映する必要なし。Finalize は無視する。

#### 6-3-4. `AcquiredPotionIds` (Bestiary) との関係

`RunState.AcquiredPotionIds` (Phase 8) は「初めて見たポーション ID」を記録する Bestiary 用フィールド。10.2.E では触らない（取得時に既に追加されており、消費とは独立）。

#### 6-3-5. hero 不在時の挙動

`Allies.FirstOrDefault(a => a.DefinitionId == "hero")` が null を返す状況は仕様上ありえない（hero 死亡 = `IsAlive: false` だが Allies 配列からは除去されない、hero=Allies[0] / SlotIndex=0 を保証）。null チェックは defensive コードとして残し、`InvalidOperationException` を投げる。

`finalHeroHp` は `Math.Max(0, hero.CurrentHp)` で負値ガード（毒 / 大ダメージで HP が負になっている可能性）。

#### 6-3-6. `ActiveBattle = null` セット

10.2.A 既存挙動を維持。`BattlePlaceholderState? ActiveBattle` は 10.5 で新 `BattleState?` に切替予定。10.2.E では既存パスをそのまま動かす（`BattleEngine.Finalize` は新 `BattleState` を入力に取るが、`RunState.ActiveBattle` は依然として `BattlePlaceholderState` 型なので、null セットだけが意味を持つ）。

### 6-4. 例外仕様

| 状況 | 例外 |
|---|---|
| `state.Phase != Resolved` | `InvalidOperationException`: "Finalize requires Phase=Resolved, got X" |
| hero が `state.Allies` に存在しない | `InvalidOperationException`: "hero not found in Allies" |

---

## 7. 10.2.D residue: W4 / W5 preparation tasks

10.2.D code-reviewer レビュー (BASE=af1353d / HEAD=306f977) で挙がった 4 件の Warnings/Nits のうち、**W4 と W5 のみ** を 10.2.E 着手前の preparation として組み込む。W1 / N1 は別 cleanup task で対応（10.2.E 後）。

### 7-1. W5: `DrawHelper` 共通化

§5-7 で詳述。新規ファイル `DrawHelper.cs` で `Draw(state, count, rng, out actuallyDrawn) → BattleState` を提供し、`TurnStartProcessor.DrawCards` / `EffectApplier.ApplyDraw` の重複を解消。`HandCap = 10` も `DrawHelper.HandCap` に一元化。

**テスト**: `DrawHelperTests.cs` 新設
- 山札充足時の通常ドロー
- 山札不足時のシャッフル補充
- Hand cap 10 で打ち切り
- count > DrawPile + DiscardPile で actuallyDrawn = 利用可能枚数
- count = 0 で no-op

### 7-2. W4: `summon` 決定的 ID 生成

§5-8 で詳述。`EffectApplier.ApplySummon` の `newInstanceId` 生成を `Turn + Allies.Length` 方式から RNG ベース (`Turn + rng.NextInt(...).x`) に切替え、レリック由来 summon との衝突を回避。

**Determinism Tests への影響**: `IRng` 注入で seed 固定 → ID も決定的、既存 `BattleDeterminismTests.Combat_with_summon_and_heal_is_deterministic` は影響なし。`summon_inst_` を含む期待値を hardcoded で持つテストの抽出 + 更新が必要（plan の Task 1 で grep 確認）。

### 7-3. preparation task としての位置付け

これら 2 つは 10.2.E plan の **Task 0 / Task 1** として独立 commit にし、10.2.E 本体（レリック発動 + UsePotion）の前提を整える。両タスクとも spec の挙動変更なし（リファクタ）。緑維持の確認のみ。

### 7-4. W1 / N1 の処遇

| 項目 | 処遇 | 理由 |
|---|---|---|
| W1: `IsUpgradable` 未使用 | 別 cleanup task で対応（10.2.E 後） | 既存挙動と等価、10.2.E 経路に影響なし |
| N1: `SummonCleanup.Apply` の events 引数未使用 | 当面シグネチャ維持 | 10.2.E でも未使用、将来 OnSummonDeath 等で活用見込み |

これらは 10.2.E plan に含めない。別途 issue / cleanup PR で対応。

---

## 8. テスト戦略

10.2.A〜D と同じ TDD 1 サイクル粒度（失敗テスト → 実装 → 緑 → commit）。subagent-driven-development。

### 8-1. 新規テストファイル（11 ファイル想定）

| ファイル | カバレッジ |
|---|---|
| `Engine/RelicTriggerProcessorTests.cs` | 4 Trigger 単体発火 / catalog 未登録 silent skip / `Implemented:false` skip / Trigger 不一致 skip / 所持順 (RunState.Relics 配列順) / caster=hero 検索 / hero 不在で no-op / Note prefix `relic:<id>` 付与 / 各 effect 後の caster 再 fetch |
| `Engine/BattleEngineUsePotionTests.cs` | Phase != PlayerInput で例外 / potionIndex 範囲外で例外 / 空 slot で例外 / catalog 未登録で例外 / heal effect 適用 / attack effect で Pool 加算 / 全 effect 適用後 slot=`""` / `UsePotion` event 発火 / target 引数非 null で state 更新 / target 引数 null で 既存 target 維持 / cost 消費なし / コンボ更新なし / 同 turn 連続使用 |
| `Engine/BattleEngineStartRelicTests.cs` | Start 戻り値が `(BattleState, IReadOnlyList<BattleEvent>)` / events 列に BattleStart + TurnStart + OnBattleStart relic events 順 / `OwnedRelicIds` snapshot / `Potions` snapshot / 該当 relic なしで空 events / Implemented:false relic はスキップ |
| `Engine/BattleEnginePlayCardOnCardPlayTests.cs` | OnCardPlay 発火位置（effect 適用後・カード移動前）/ Note=`relic:<id>` / 該当 relic なしでも既存 events 不変 / レリック由来 summon でカード自身の `summonSucceeded` フラグに影響しない / レリック由来の `attack` 加算が当 turn の PlayerAttacking で発射 |
| `Engine/TurnStartProcessorOnTurnStartTests.cs` | OnTurnStart 発火位置（Draw 後 / TurnStart event 前）/ 該当 relic なしで挙動不変 / レリック由来 attack が当 turn PlayerAttacking で発射 / レリック由来 gainEnergy で Energy が EnergyMax を超える / Implemented:false skip / 所持順 |
| `Engine/TurnEndProcessorOnTurnEndTests.cs` | OnTurnEnd 発火位置（AttackPool reset 後 / コンボリセット前）/ 該当 relic なしで既存挙動 / レリック由来 attack が次 turn PlayerAttacking で発射（reset 後なので保持される）/ Process シグネチャに `IRng rng` 追加 / `DataCatalog catalog` 既存 |
| `Engine/PlayerAttackingResolverOnEnemyDeathTests.cs` | Single 攻撃で 1 体死亡 → 1 fire / Random で 1 体死亡 → 1 fire / All で 3 体死亡 → slot 内側→外側順 3 fire / 攻撃で死亡 0 体なら fire なし / Single→Random→All のシーケンス内で各発射ごとに fire / dead 敵に再 fire しない（`enemyIdsBefore` snapshot で防止）/ 該当 relic なしで挙動不変 |
| `Engine/PoisonTickOnEnemyDeathTests.cs` | 敵が毒死 → OnEnemyDeath 1 fire / hero/summon の毒死は OnEnemyDeath fire しない（敵限定）/ 複数敵が同 tick で毒死 → InstanceId snapshot 順に fire / 該当 relic なしで既存 PoisonTick 挙動不変 |
| `Engine/BattleEngineFinalizeConsumedPotionTests.cs` | UsePotion 0 回 → ConsumedPotionIds 空 / UsePotion 1 回 → 該当 ID 1 件 / 同 ID を 2 スロット消費 → ID 2 件 / state.Potions が RunState.Potions に丸ごとコピー / hero HP 反映 / Defeat で Progress=GameOver / Victory で Progress 維持 / hero 不在で例外 |
| `Engine/DrawHelperTests.cs` (W5) | 通常ドロー / 山札不足でシャッフル補充 / Hand cap 10 で打ち切り / count 過大で actuallyDrawn = 利用可能枚数 / count=0 で no-op / DrawPile も DiscardPile も空で actuallyDrawn=0 / 同 seed で決定的 |
| `Engine/EffectApplierSummonInstanceIdTests.cs` (W4) | RNG ベース ID 生成 / 同ターン 2 連続 summon で ID 衝突なし / 異なる seed で異なる ID / 同 seed で決定的 |

### 8-2. 既存テストファイルの拡張

| ファイル | 変更 |
|---|---|
| `State/BattleStateInvariantTests.cs` | +`OwnedRelicIds` 不変 / +`Potions.Length == PotionSlotCount` |
| `Engine/BattleEngineStartTests.cs` | 戻り値 `(BattleState, events)` 追従 / 既存 assertion を `var (state, _) = Start(...)` 形式に / OwnedRelicIds / Potions snapshot 検証追加 |
| `Engine/BattleEnginePlayCardTests.cs` | 既存 fixture の `new BattleState(...)` に OwnedRelicIds / Potions 追加 |
| `Engine/BattleEngineEndTurnTests.cs` | 既存 fixture 追従 / TurnEnd / TurnStart シグネチャ変更追従 |
| `Engine/BattleEngineFinalizeTests.cs` | BattleSummary に ConsumedPotionIds フィールド追加追従 / state.Potions コピー検証 |
| `Engine/BattleEngineSetTargetTests.cs` | 既存 fixture 追従（OwnedRelicIds/Potions 追加） |
| `Engine/PlayerAttackingResolverTests.cs` | catalog 引数追加追従 / 既存挙動維持 |
| `Engine/EnemyAttackingResolverTests.cs` | 既存挙動維持 |
| `Engine/TurnStartProcessorTests.cs` | catalog 引数追加追従 / 既存 step 順序検証 + step 8 (OnTurnStart) 挿入確認 |
| `Engine/TurnStartProcessorTickTests.cs` | catalog 引数追加追従 |
| `Engine/TurnStartProcessorLifetimeTests.cs` | catalog 引数追加追従 |
| `Engine/TurnEndProcessorTests.cs` | rng / catalog 引数追加追従 / step 3 (OnTurnEnd) 挿入確認 |
| `Engine/TurnEndProcessorRetainSelfTests.cs` | rng / catalog 引数追加追従 |
| `Engine/TurnEndProcessorComboResetTests.cs` | rng / catalog 引数追加追従 |
| `Engine/EffectApplier*Tests.cs` (10 ファイル) | catalog 引数は既に渡している（10.2.D 既存）、変更なし |
| `Engine/EffectApplierSummonTests.cs` | summon InstanceId アサーションを RNG ベース prefix (`summon_inst_<turn>_*`) に緩和 |
| `Engine/BattleDeterminismTests.cs` | レリック + UsePotion を含む 1 戦闘の seed 一致テスト追加 / 既存テストの InstanceId 期待値を prefix only に緩和 |
| `Engine/SummonCleanupTests.cs` | 既存挙動維持、シグネチャ不変 |
| `Fixtures/BattleFixtures.cs` | RelicDefinition factory 追加 / PotionDefinition factory 追加 / MinimalCatalog の Relics / Potions 拡張 / 全 BattleState 生成箇所に OwnedRelicIds / Potions パラメータ伝播 |

合計 想定 11 新規テストファイル + 18 既存テストファイル拡張、~80-100 新規テスト。

### 8-3. ビルド赤期間管理

破壊的変更が複数同時:

1. `BattleState` に `OwnedRelicIds` / `Potions` 2 フィールド追加 → 全 `new BattleState(...)` 呼出（10.2.D で 17 + 新規 = 多数）がコンパイルエラー
2. `BattleSummary` に `ConsumedPotionIds` フィールド追加 → `BattleEngineFinalizeTests` がコンパイルエラー
3. `BattleEngine.Start` シグネチャ変更（戻り値が tuple に）→ 全呼出側がコンパイルエラー
4. `TurnStartProcessor.Process` に `catalog` 引数追加 → `BattleEngine.Start` / `BattleEngine.EndTurn` / 全 TurnStart テスト
5. `TurnEndProcessor.Process` に `rng` 引数追加 → `BattleEngine.EndTurn` / 全 TurnEnd テスト
6. `PlayerAttackingResolver.Resolve` に `catalog` 引数追加 → `BattleEngine.EndTurn` / 全 PlayerAttacking テスト

これらを 1 commit でまとめると赤期間が長すぎ、依存順を守って小刻みに進める:

| Task | 内容 | 緑復帰時点 |
|---|---|---|
| 0 | DrawHelper 共通化 (W5) | 既存テスト緑維持 |
| 1 | summon InstanceId RNG ベース (W4) | 既存テスト緑維持 + InstanceIdTests 追加 |
| 2 | BattleState に OwnedRelicIds / Potions 追加 + 全 fixture 追従 + Start で snapshot | ビルド緑、既存テスト緑 |
| 3 | BattleEventKind に UsePotion=19 追加 + Start シグネチャ変更 (events 戻り値) | ビルド緑、既存テスト緑 |
| 4 | RelicTriggerProcessor 新設 (Fire / FireOnEnemyDeath) + 単体テスト | RelicTriggerProcessorTests 緑 |
| 5 | TurnStartProcessor.Process シグネチャ変更 (catalog 追加) + OnTurnStart 発火 (step 8) | TurnStart 系全テスト緑 + 新規 OnTurnStart テスト |
| 6 | TurnEndProcessor.Process シグネチャ変更 (rng 追加) + OnTurnEnd 発火 (step 3) | TurnEnd 系全テスト緑 + 新規 OnTurnEnd テスト |
| 7 | BattleEngine.PlayCard で OnCardPlay 発火 (effect 適用後・カード移動前) | PlayCard 系全テスト緑 + 新規 OnCardPlay テスト |
| 8 | PlayerAttackingResolver.Resolve シグネチャ変更 (catalog 追加) + OnEnemyDeath 発火 (1 攻撃ごと) | PlayerAttacking 系全テスト緑 + 新規 OnEnemyDeath テスト |
| 9 | TurnStartProcessor.ApplyPoisonTick で OnEnemyDeath 発火 | PoisonTick テスト緑 + 新規 PoisonTickOnEnemyDeath テスト |
| 10 | BattleEngine.Start で OnBattleStart 発火 (TurnStart 後) | Start 系全テスト緑 + 新規 StartRelic テスト |
| 11 | BattleEngine.UsePotion 新設 (第 6 公開 API) | UsePotion 単体テスト緑 |
| 12 | BattleSummary に ConsumedPotionIds 追加 + Finalize で state.Potions コピー + diff 計算 | Finalize 系全テスト緑 + 新規 ConsumedPotion テスト |
| 13 | Determinism Test 拡張 (レリック + UsePotion 含む 1 戦闘) | 全テスト緑 |
| 14 | 親 spec への補記事項反映 (§9-3) | spec ファイル更新 |
| 15 | `phase10-2E-complete` タグ + push | git tag |

詳細順序・粒度は plan に記載。

### 8-4. テスト方針

- **Fixtures は引き続きインライン factory のみ**。10.2.A〜D と同じ
- **`BattleFixtures.cs` の MinimalCatalog 拡張**: RelicDefinition / PotionDefinition factory を追加
  ```csharp
  public static RelicDefinition Relic(
      string id = "test_relic",
      RelicTrigger trigger = RelicTrigger.OnTurnStart,
      params CardEffect[] effects)
  public static PotionDefinition Potion(
      string id = "test_potion",
      params CardEffect[] effects)
  ```
- **`BattleState` factory**: `BattleFixtures.MinimalState(...)` ヘルパーに `OwnedRelicIds = ImmutableArray<string>.Empty` / `Potions = ImmutableArray.Create("", "", "")` をデフォルト付き引数で追加
- **`IRng` は `FakeRng` を使用**（既存）
- **既存テストとの整合**: `BattlePlaceholderTests` / `RunStateSerializerTests` は無傷（10.2.E は新 `BattleState` のみ変更、placeholder 系列は触らない）

### 8-5. テスト実行コマンド

- 1 ファイル: `dotnet test --filter FullyQualifiedName~<TestClass>`
- 全 Battle: `dotnet test --filter FullyQualifiedName~Battle`
- 全体: `dotnet build && dotnet test`

---

## 9. スコープ外 + 親 spec 補記事項

### 9-1. Phase 10.2.E では触らない

- **戦闘外 UsePotion（マップ画面のポーション UI）**: `BattleOnly: false` effect のみ適用するヘルパー (`OutOfBattleEffectApplier` など) → Phase 10.5
- **`OnPotionUse` レリック Trigger**: `RelicTrigger` enum に該当値なし → Phase 11+
- **複数 summon を 1 カード / 1 effect で連発**: `BattleEngine.PlayCard.cs:129` の `Allies[Allies.Length - 1]` 暗黙前提（10.2.D spec §5-3-3）の解消 → Phase 11+
- **召喚 actor 自身がカードを発動 / move 駆動 attack**: `CombatActor.CurrentMoveId` の活用 → Phase 11+
- **死亡 summon の slot 再利用**: 現状 Allies 配列 append-only → Phase 11+
- **レリック発動による更なる death の即時連鎖**: AOE 攻撃ループ内で Pool 加算が即時着弾しない (Q6-A 確定) → Phase 11+ で必要なら設計
- **W1: `IsUpgradable` 未使用** / **N1: `SummonCleanup.Apply` の events 引数未使用**: 別 cleanup task → 10.2.E 後
- **`BattlePlaceholder.cs` / `BattlePlaceholderState.cs` の削除 + `RunState.ActiveBattle` 型切替 + save schema v8 マイグレーション**: Phase 10.5
- **`BattleHub` / `BattleStateDto` / `BattleEventDto`**: Phase 10.3
- **`BattleScreen.tsx` / `battle-v10.html` ポート**: Phase 10.4

### 9-2. Phase 10.2.E 完了後の状態

- `BattleState` に `OwnedRelicIds` / `Potions` 2 フィールド snapshot
- `BattleSummary` に `ConsumedPotionIds` 1 フィールド追加
- `BattleEventKind` に `UsePotion = 19` 追加（計 20 値）
- `BattleEngine` 公開 API が **6 つ** (`Start` / `PlayCard` / `EndTurn` / `SetTarget` / `UsePotion` / `Finalize`)
- `BattleEngine.Start` が `(BattleState, IReadOnlyList<BattleEvent>)` を返す（10.2.A 単純化からの破壊的変更）
- `RelicTriggerProcessor` 新設、4 Trigger 統一発火 + 所持順 + Implemented:false skip + caster=hero 検索
- 4 Trigger の発火サイト全 6 箇所統合済み
- `BattleEngine.UsePotion` 新規実装
- `BattleEngine.Finalize` で `state.Potions` を `RunState.Potions` に丸ごとコピー、`BattleSummary.ConsumedPotionIds` は diff 派生
- `DrawHelper` 共通化 (W5 修正)、`summon` InstanceId 決定的 RNG ベース (W4 修正)
- xUnit で「レリック 4 Trigger 発火 + UsePotion + ConsumedPotion 反映」を含む 1 戦闘が完走
- 既存ゲームフロー (`BattlePlaceholder` 経由) は無傷
- 親 spec が新方針に合わせて補記済み
- `phase10-2E-complete` タグ push 済み

### 9-3. 親 spec への補記事項

`docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に以下を反映:

1. **§3-1 `BattleState`**: 10.2.E で `OwnedRelicIds: ImmutableArray<string>` / `Potions: ImmutableArray<string>` を追加。Start で snapshot、`OwnedRelicIds` は不変、`Potions` は UsePotion で消費
2. **§3-3 `BattleSummary`**: 10.2.E で `ConsumedPotionIds: ImmutableArray<string>` を追加。Finalize で `before.Potions` vs `state.Potions` の diff として算出
3. **§3-5 `BattleEngine` 公開 API**: 10.2.E で `UsePotion` 第 6 API 追加。`Start` シグネチャを `(BattleState, IReadOnlyList<BattleEvent>)` に変更（破壊的変更）
4. **§4-1 戦闘開幕の初期化**: 10.2.E で `Start` 末尾に OnBattleStart レリック発動を追加。順序: ターン 1 開始処理 (TurnStart) → OnBattleStart レリック → return
5. **§4-2 ターン開始処理 step 7**: 10.2.E で OnTurnStart レリック発動を実装。Draw 後 / TurnStart event 前に挿入（step 8 として位置付け、親 spec の "step 7" 表現を「Draw 後」に明確化）
6. **§4-6 ターン終了処理 step 3**: 10.2.E で OnTurnEnd レリック発動を実装。AttackPool reset 後 / コンボリセット前に挿入。`TurnEndProcessor.Process` シグネチャに `IRng rng` 引数追加
7. **§5-1 `EffectApplier.Apply`**: 10.2.E で sig 変更なし、`BattleOnly` フラグは戦闘内では無視（戦闘外は 10.5 で `OutOfBattleEffectApplier` 経由）
8. **§5-6 BattleOnly の戦闘外発動時挙動**: 10.5 で実装と明記（10.2.E 範囲外）
9. **§7-3 `UsePotion` の追加**: 10.2.E で第 6 API として実装。Phase=PlayerInput 限定、cost なし、コンボ更新なし、捨札移動なし、消費 slot を `""` に。両者（PlayCard 経路の暗黙対象切替 + UsePotion 経由の対象切替）の整合は両方とも `target ?? state.Target` パターン
10. **§8-1 ポーション**: 10.2.E で戦闘内 UsePotion 実装、戦闘外 UI は 10.5
11. **§8-2-1 レリック 4 新 Trigger 発火位置**: 10.2.E で全 6 サイト実装。OnTurnStart は step 8 (Draw 後)、OnTurnEnd は step 3 (AttackPool reset 後)、OnCardPlay は effect 適用後・カード移動前、OnEnemyDeath は 1 攻撃発射ごと + 敵毒死時
12. **§8-2-2 発動主体**: 10.2.E で caster は `Allies.FirstOrDefault(a => a.DefinitionId == "hero")` で索引（`Allies[0]` 直接参照ではなく defensive 検索）
13. **§9-7 `BattleEventKind`**: 10.2.E で `UsePotion = 19` 追加（計 20 値）。レリック発動 events は専用 Kind を持たず、既存 Kind の Note 末尾に `relic:<id>` を付与する慣例

これら 13 項目は Phase 10.2.E 内で発生した設計判断の追記。コードと spec の乖離を残さない。

### 9-4. memory feedback ルールの遵守チェックリスト

実装中・レビュー時に確認する 2 項目（`memory/feedback_battle_engine_conventions.md`）:

- [ ] `BattleOutcome` 参照は今回新規発生しうる箇所:
  - `RelicTriggerProcessor` 内では Outcome 確定経路を持たない（Q6-A 確定、レリック effect は Pool 加算 / heal / status / draw 等で直接死を引き起こさない）
  - `BattleEngine.UsePotion` 内も同様（ポーションは hero に対する補助効果のみ）
  - `BattleEngine.Finalize` の Defeat 判定は 10.2.A 既存パターン (`state.Outcome == Defeat` を直接参照)、すべて `RoguelikeCardGame.Core.Battle.State.BattleOutcome.X` の fully qualified
- [ ] `state.Allies` / `state.Enemies` への書き戻しは InstanceId で検索:
  - `RelicTriggerProcessor.Fire` 内で `caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero")` で索引（DefinitionId 検索 = InstanceId 検索の精神に準拠）
  - 各 effect 適用は `EffectApplier.Apply` 委譲で、内部の InstanceId 検索パターン (10.2.D 既存) に従う
  - `PlayerAttackingResolver` の `enemyIdsBefore` snapshot は `InstanceId` HashSet で取得、`DetectNewlyDead` も InstanceId ベース
  - `BattleEngine.UsePotion` 内で `caster = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero")` で索引
  - `BattleEngine.Finalize` で `hero = state.Allies.FirstOrDefault(...)` で索引

---

## 参照

- 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン spec: [`2026-04-26-phase10-2D-effects-summon-design.md`](2026-04-26-phase10-2D-effects-summon-design.md)
- 直前マイルストーン plan: [`../plans/2026-04-26-phase10-2D-effects-summon.md`](../plans/2026-04-26-phase10-2D-effects-summon.md)
- ロードマップ: [`../plans/2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md)
- memory feedback: `memory/feedback_battle_engine_conventions.md`
- 10.2.D code review (BASE=af1353d / HEAD=306f977): W4 / W5 修正を §7 で組み込み
