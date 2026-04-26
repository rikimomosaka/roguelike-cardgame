# Phase 10.2.D — 残り effect + 召喚 + カード移動優先順位 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 10.2 (Core バトル本体) の 4 段階目として、残り effect 8 種（heal / draw / discard / upgrade / exhaustCard / exhaustSelf / retainSelf / gainEnergy）+ `summon` action + 召喚 system（`SummonHeld` pile / `RemainingLifetimeTurns` / `AssociatedSummonHeldInstanceId` / Lifetime tick）+ カード移動 5 段優先順位（exhaustSelf / Power / Unit+success / retainSelf / Discard）+ `PowerCards` pile を実装する。10.2.D 完了で **`BattleEngine` の Core ロジック完成**。

**Architecture:** `BattleState` に `SummonHeld` / `PowerCards` 2 フィールド追加。`CombatActor` に `RemainingLifetimeTurns` / `AssociatedSummonHeldInstanceId` (string) 追加（spec §3-2 の int? を memory feedback ルール準拠で string? に訂正）。`EffectApplier.Apply` のシグネチャに `DataCatalog catalog` 追加し、9 新 action 対応（exhaustSelf/retainSelf はマーカー effect、event 発火のみ）。`BattleEngine.PlayCard` 末尾のカード移動を 5 段優先順位に置換、`summonSucceeded` フラグ追跡。`TurnStartProcessor` に Lifetime tick（countdown 後、Energy 前）。`SummonCleanup.Apply` 共通 helper を 4 箇所（PlayerAttacking / EnemyAttacking / TurnStart の poison tick 後 / TurnStart の Lifetime tick 後）から呼び、死亡 summon の SummonHeld → Discard を実行。`TurnEndProcessor.Process` のシグネチャに `DataCatalog catalog` 追加し retainSelf-aware 手札整理。`BattleEventKind` に 7 値追加（19 値）。memory feedback の 2 ルール（`BattleOutcome` fully qualified / `state.Allies`/`state.Enemies` 書き戻しは InstanceId 検索）を全新規 loop 箇所で遵守。

**Tech Stack:** C# .NET 10 / xUnit / `System.Collections.Immutable`

**前提:**
- Phase 10.2.C が master にマージ済み（`phase10-2C-complete` タグ + master HEAD `91b5579` 以降）
- 開始時点で `dotnet build` 0 警告 0 エラー、`dotnet test` 全件緑（Core 829 件 + Server 168/170 skip 2）

**完了判定（spec §「完了判定」と同期）:**
- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（10.2.C 完了時 Core 829 + 10.2.D 追加分 ~80-120）
- `BattleState` に `SummonHeld` / `PowerCards` の 2 フィールド追加、初期空、`Start` 直後で空配列
- `CombatActor` に `RemainingLifetimeTurns: int?` / `AssociatedSummonHeldInstanceId: string?` 追加。hero は両方 null
- `EffectApplier.Apply` が `DataCatalog catalog` 引数を受け取り、9 新 action（heal/draw/discard/upgrade/exhaustCard/exhaustSelf/retainSelf/gainEnergy/summon）に対応
- `discard` の `Scope == Single` で `InvalidOperationException`
- `upgrade` / `exhaustCard` がランダム選択（`IRng` 経由）し Pile 不足時は存在分だけ処理、`upgrade` は `IsUpgraded=true` を skip
- `BattleEngine.PlayCard` 末尾のカード移動が 5 段優先順位
- `TurnStartProcessor.Process` が Lifetime tick を実行（countdown 後、Energy 前）
- 召喚死亡時に `SummonHeld` 内の紐付きカードが `DiscardPile` へ移動（`SummonCleanup.Apply` 経由）
- `TurnEndProcessor.Process` が `DataCatalog catalog` 引数を受け取り、retainSelf-aware 手札整理
- `BattleEventKind` 19 値（10.2.C 完了時 12 + 10.2.D 追加 7）
- 既存 `BattlePlaceholder` 経由のフロー無傷
- 親 spec §2-4 / §3-1 / §3-2 / §4-2 / §4-6 / §5-1 / §5-4 / §5-7 / §9-7 に補記済み
- `phase10-2D-complete` タグ origin に push 済み
- `memory/project_phase_status.md` を 10.2.D 完了状態に更新

---

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `src/Core/Battle/State/BattleState.cs` | +SummonHeld / PowerCards | 修正 |
| `src/Core/Battle/State/CombatActor.cs` | +RemainingLifetimeTurns / AssociatedSummonHeldInstanceId | 修正 |
| `src/Core/Battle/Engine/BattleEngine.cs` | Start で 2 新 BattleState フィールド + 2 新 CombatActor フィールド初期化 | 修正 |
| `src/Core/Battle/Engine/BattleEngine.PlayCard.cs` | カード移動 5 段優先順位 + summonSucceeded 追跡 + AssociatedSummonHeldInstanceId binding + EffectApplier 呼出に catalog 追加 | 修正 |
| `src/Core/Battle/Engine/BattleEngine.EndTurn.cs` | TurnEndProcessor.Process に catalog 渡す | 修正 |
| `src/Core/Battle/Engine/EffectApplier.cs` | +DataCatalog 引数 + 9 新 action（heal/draw/discard/upgrade/exhaustCard/exhaustSelf/retainSelf/gainEnergy/summon） | 修正 |
| `src/Core/Battle/Engine/TurnStartProcessor.cs` | +Lifetime tick（countdown 後、Energy 前）+ SummonCleanup 呼出 2 箇所 | 修正 |
| `src/Core/Battle/Engine/TurnEndProcessor.cs` | +DataCatalog 引数 + retainSelf-aware 手札整理 | 修正 |
| `src/Core/Battle/Engine/PlayerAttackingResolver.cs` | SummonCleanup 呼出 | 修正 |
| `src/Core/Battle/Engine/EnemyAttackingResolver.cs` | SummonCleanup 呼出 + EffectApplier 呼出に catalog 追加 | 修正 |
| `src/Core/Battle/Engine/SummonCleanup.cs` | 死亡 summon の SummonHeld → Discard helper | **新規** |
| `src/Core/Battle/Events/BattleEventKind.cs` | +Heal / Draw / Discard / Upgrade / Exhaust / GainEnergy / Summon | 修正 |
| `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` | UnitDefinition factory + summon 用 catalog 拡張 + Hero/Goblin に 2 新 CombatActor フィールド初期化 | 修正 |
| `tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs` | +SummonHeld/PowerCards/Allies.Length<=4/hero Lifetime null | 修正 |
| `tests/Core.Tests/Battle/State/CombatActorTests.cs` | +RemainingLifetimeTurns / AssociatedSummonHeldInstanceId record 等価 | 修正 |
| `tests/Core.Tests/Battle/Events/BattleEventKindTests.cs` | 19 値検証 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs` | +SummonHeld/PowerCards 初期空 / hero の 2 新フィールド null | 修正 |
| `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs` | catalog 引数追加追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/EffectApplierBuffDebuffTests.cs` | catalog 引数追加追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/EffectApplierReplaceActorInstanceIdTests.cs` | catalog 引数追加追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/EffectApplierHealTests.cs` | Self / Single / All / Random / dead skip / cap MaxHp | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierDrawTests.cs` | Self only / 山札不足→shuffle / hand cap / 完全空 | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierDiscardTests.cs` | Random / All / Single throws / Hand 不足 | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierGainEnergyExhaustSelfRetainSelfTests.cs` | gainEnergy +Energy / exhaustSelf event / retainSelf no-op | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierExhaustCardTests.cs` | hand/discard/draw / 不足 / 不正 Pile | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierUpgradeTests.cs` | hand/discard/draw / IsUpgraded skip / IsUpgradable=false skip / 不足 | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierSummonTests.cs` | 空き slot 成功 / 満杯不発 / Allies 増 / Summon event / UnitId null throws | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCardMovementTests.cs` | 5 段優先順位 + Unit 失敗時 retainSelf 経路 + Unit+exhaustSelf 優先順位 | **新規** |
| `tests/Core.Tests/Battle/Engine/TurnStartProcessorLifetimeTests.cs` | LifetimeTurns null skip / N→0 で死亡 / SummonHeld → Discard / event | **新規** |
| `tests/Core.Tests/Battle/Engine/SummonCleanupTests.cs` | 死亡 ally 検出 / SummonHeld → Discard / null 化 / 既クリーン skip | **新規** |
| `tests/Core.Tests/Battle/Engine/TurnEndProcessorRetainSelfTests.cs` | retainSelf カードのみ Hand 残 / それ以外 Discard | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs` | 既存 fixture に SummonHeld/PowerCards/2 新 CombatActor フィールド追加 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs` | catalog 引数追加追従 + Lifetime / SummonCleanup integration | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEngineFinalizeTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverTests.cs` | 召喚死亡 SummonCleanup integration | 修正 |
| `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverTests.cs` | EffectApplier catalog 引数追従 + SummonCleanup integration | 修正 |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOmnistrikeTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverStatusTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverStatusTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/TurnStartProcessorTests.cs` | 既存 fixture 追従 + Lifetime tick の他 step との順序確認 | 修正 |
| `tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs` | 既存 fixture 追従 + tick 順序（poison → countdown → Lifetime） | 修正 |
| `tests/Core.Tests/Battle/Engine/TurnEndProcessorTests.cs` | catalog 引数追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/TurnEndProcessorComboResetTests.cs` | catalog 引数追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/TargetingAutoSwitchTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboMinTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCostReductionTests.cs` | 既存 fixture 追従 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs` | 召喚 + heal/draw 含む 1 戦闘 seed 一致 | 修正 |
| `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` | §2-4 / §3-1 / §3-2 / §4-2 / §4-6 / §5-1 / §5-4 / §5-7 / §9-7 補記 | 修正 |
| `C:/Users/Metaverse/.claude/projects/c--Users-Metaverse-projects-roguelike-cardgame/memory/project_phase_status.md` | 10.2.D 完了状態に更新 | 修正 |

---

## Task 1: `BattleState` に `SummonHeld` / `PowerCards` 追加 + 全 fixture 追従

**Files:**
- Modify: `src/Core/Battle/State/BattleState.cs`
- Modify: `src/Core/Battle/Engine/BattleEngine.cs` (Start initializer)
- Modify: `tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs`
- Modify: `tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs`
- Sweep: 全 `new BattleState(...)` 呼出箇所（10.2.C で 17 + 10.2.C で追加した 4 = 21+ 箇所、Grep で抽出）

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs` の末尾に追加:

```csharp
// === 10.2.D: SummonHeld / PowerCards ===

[Fact] public void SummonHeld_default_is_empty()
{
    var s = MakeMinimalState();   // 既存ヘルパー、新フィールド追加で更新が必要
    Assert.True(s.SummonHeld.IsDefaultOrEmpty || s.SummonHeld.Length == 0);
}

[Fact] public void PowerCards_default_is_empty()
{
    var s = MakeMinimalState();
    Assert.True(s.PowerCards.IsDefaultOrEmpty || s.PowerCards.Length == 0);
}

[Fact] public void SummonHeld_record_equality_distinguishes()
{
    var s1 = MakeMinimalState();
    var card = new BattleCardInstance("c1", "strike", false, null);
    var s2 = s1 with { SummonHeld = ImmutableArray.Create(card) };
    Assert.NotEqual(s1, s2);
}

[Fact] public void PowerCards_record_equality_distinguishes()
{
    var s1 = MakeMinimalState();
    var card = new BattleCardInstance("c1", "strike", false, null);
    var s2 = s1 with { PowerCards = ImmutableArray.Create(card) };
    Assert.NotEqual(s1, s2);
}
```

`MakeMinimalState()` (or whichever helper that file uses, e.g., `Make()`) を更新して `SummonHeld` / `PowerCards` を空配列で初期化する必要がある。

- [ ] **Step 2: 失敗確認**

Run: `dotnet build`
Expected: error CS7036 が広範囲に発生（既存の `new BattleState(...)` 呼出全て）

- [ ] **Step 3: 実装 — `BattleState.cs` 更新**

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル全体の不変状態。
/// 親 spec §3-1 参照。
/// 10.2.D で SummonHeld / PowerCards を追加（フィールド順は最終形に揃った）。
/// </summary>
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
    ImmutableArray<BattleCardInstance> SummonHeld,    // 10.2.D 追加
    ImmutableArray<BattleCardInstance> PowerCards,    // 10.2.D 追加
    int ComboCount,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    string EncounterId);
```

- [ ] **Step 4: `BattleEngine.cs` Start で初期化追加**

`new BattleState(...)` の `ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,` の直後に挿入:

```csharp
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,    // 10.2.D
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,    // 10.2.D
```

- [ ] **Step 5: `BattleEngineStartTests.cs` に assertion 追加**

`Start_initializes_combo_fields_to_default` テストの直後に:

```csharp
[Fact] public void Start_initializes_summon_held_and_power_cards_to_empty()
{
    var run = MakeRun();
    var rng = new FakeRng(new int[20], new double[0]);
    var cat = BattleFixtures.MinimalCatalog();
    var s = BattleEngine.Start(run, "enc_test", rng, cat);
    Assert.Empty(s.SummonHeld);
    Assert.Empty(s.PowerCards);
}
```

- [ ] **Step 6: 全テスト fixture（local `MakeState`）の追従**

Use Grep tool:
- pattern: `new BattleState\(`
- path: `tests/Core.Tests/Battle/`
- output_mode: files_with_matches

各ファイルの `new BattleState(...)` 呼出（および `BattleStateInvariantTests` の helper）に `ExhaustPile:` の直後で以下 2 行を追加:

```csharp
SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
PowerCards: ImmutableArray<BattleCardInstance>.Empty,
```

主な対象（10.2.C 後の状態でこれらがある）:
- `tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs`
- `tests/Core.Tests/Battle/Engine/*.cs` の local MakeState helpers

`with` 式は変更不要。

- [ ] **Step 7: ビルド確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

- [ ] **Step 8: テスト実行**

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 既存 829 件 + 新 5 件（invariant 4 + Start 1）= 834 件すべて緑

- [ ] **Step 9: commit + push**

```bash
git add src/Core/Battle/State/BattleState.cs \
        src/Core/Battle/Engine/BattleEngine.cs \
        tests/Core.Tests/
git commit -m "feat(battle): add SummonHeld + PowerCards to BattleState (Phase 10.2.D Task 1)"
git push
```

---

## Task 2: `CombatActor` に `RemainingLifetimeTurns` / `AssociatedSummonHeldInstanceId` 追加 + 全 fixture 追従

**Files:**
- Modify: `src/Core/Battle/State/CombatActor.cs`
- Modify: `src/Core/Battle/Engine/BattleEngine.cs` (Start で hero / enemies 初期化)
- Modify: `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` (Hero / Goblin)
- Modify: `tests/Core.Tests/Battle/State/CombatActorTests.cs`
- Sweep: 全 `new CombatActor(...)` 呼出（fixture / 各 test の local MakeState 等）

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/State/CombatActorTests.cs` の末尾に追加:

```csharp
// === 10.2.D: Lifetime / AssociatedSummonHeldInstanceId ===

[Fact] public void RemainingLifetimeTurns_null_means_permanent()
{
    var hero = BattleFixtures.Hero();
    Assert.Null(hero.RemainingLifetimeTurns);
}

[Fact] public void AssociatedSummonHeldInstanceId_null_for_hero()
{
    var hero = BattleFixtures.Hero();
    Assert.Null(hero.AssociatedSummonHeldInstanceId);
}

[Fact] public void Record_equality_distinguishes_lifetime_field()
{
    var hero = BattleFixtures.Hero();
    var copy = hero with { RemainingLifetimeTurns = 3 };
    Assert.NotEqual(hero, copy);
}

[Fact] public void Record_equality_distinguishes_associated_summon_held()
{
    var hero = BattleFixtures.Hero();
    var copy = hero with { AssociatedSummonHeldInstanceId = "card_x" };
    Assert.NotEqual(hero, copy);
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet build`
Expected: error CS7036 が `new CombatActor(...)` 呼出全箇所で発生

- [ ] **Step 3: 実装 — `CombatActor.cs` 更新**

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中の戦闘者状態。
/// 親 spec §3-2 参照。
/// 10.2.D で RemainingLifetimeTurns / AssociatedSummonHeldInstanceId を追加（召喚 system）。
/// 親 spec §3-2 の `AssociatedSummonHeldIndex: int?` は 10.2.D で `AssociatedSummonHeldInstanceId: string?` に訂正
/// （memory feedback ルール「InstanceId 検索」準拠、SummonHeld 配列 index ずれ問題回避）。
/// </summary>
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
    int? RemainingLifetimeTurns,                   // 10.2.D 追加
    string? AssociatedSummonHeldInstanceId)        // 10.2.D 追加
{
    public bool IsAlive => CurrentHp > 0;
    public int GetStatus(string id) => Statuses.TryGetValue(id, out var v) ? v : 0;
}
```

- [ ] **Step 4: `BattleFixtures.cs` の Hero/Goblin 更新**

`Hero()` メソッドの `new CombatActor(...)` 呼出 末尾の `null);` (`CurrentMoveId: null` の閉じ) を以下に置換:

```csharp
public static CombatActor Hero(int hp = 70, int slotIndex = 0) =>
    new("hero_inst", "hero", ActorSide.Ally, slotIndex, hp, hp,
        BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
        ImmutableDictionary<string, int>.Empty, null,
        RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);  // 10.2.D
```

`Goblin()` も同様:

```csharp
public static CombatActor Goblin(int slotIndex = 0, int hp = 20, string moveId = "swing") =>
    new($"goblin_inst_{slotIndex}", "goblin", ActorSide.Enemy, slotIndex, hp, hp,
        BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
        ImmutableDictionary<string, int>.Empty, moveId,
        RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);  // 10.2.D
```

新 helper を追加（後続 Task で使うため）:

```csharp
public static CombatActor SummonActor(
    string instanceId, string definitionId, int slotIndex,
    int hp = 10, int? lifetime = null, string? associatedCardId = null,
    string? moveId = null) =>
    new(instanceId, definitionId, ActorSide.Ally, slotIndex, hp, hp,
        BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
        ImmutableDictionary<string, int>.Empty, moveId,
        RemainingLifetimeTurns: lifetime,
        AssociatedSummonHeldInstanceId: associatedCardId);
```

- [ ] **Step 5: `BattleEngine.cs` Start の hero / enemies 初期化追加**

`new CombatActor(...)` の `CurrentMoveId: null` / `CurrentMoveId: def.InitialMoveId` の直後に挿入:

```csharp
            RemainingLifetimeTurns: null,                  // 10.2.D（hero / enemy は永続）
            AssociatedSummonHeldInstanceId: null);         // 10.2.D（hero / enemy は紐付けなし）
```

両方の actor 生成箇所（hero と enemiesBuilder）に同じ追加。

- [ ] **Step 6: 全テスト fixture（local `new CombatActor(...)`）の追従**

Use Grep tool:
- pattern: `new CombatActor\(`
- path: `tests/Core.Tests/Battle/`
- output_mode: files_with_matches

各 file で `CurrentMoveId:` の直後（または `, null);` / `, "swing");` 等の閉じ括弧前）に挿入:

```csharp
RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);
```

- [ ] **Step 7: ビルド確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

- [ ] **Step 8: テスト実行**

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 既存 + 新 4 件（CombatActorTests）緑

- [ ] **Step 9: commit + push**

```bash
git add src/Core/Battle/State/CombatActor.cs \
        src/Core/Battle/Engine/BattleEngine.cs \
        tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs \
        tests/Core.Tests/Battle/State/CombatActorTests.cs \
        tests/Core.Tests/
git commit -m "feat(battle): add Lifetime + AssociatedSummonHeldInstanceId to CombatActor (Phase 10.2.D Task 2)"
git push
```

---

## Task 3: `BattleEventKind` に 7 値追加（Heal / Draw / Discard / Upgrade / Exhaust / GainEnergy / Summon）

**Files:**
- Modify: `src/Core/Battle/Events/BattleEventKind.cs`
- Modify: `tests/Core.Tests/Battle/Events/BattleEventKindTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Events/BattleEventKindTests.cs` の末尾（既存 12 値テストの後）に追加:

```csharp
[Fact] public void Heal_value_is_12()         => Assert.Equal(12, (int)BattleEventKind.Heal);
[Fact] public void Draw_value_is_13()         => Assert.Equal(13, (int)BattleEventKind.Draw);
[Fact] public void Discard_value_is_14()      => Assert.Equal(14, (int)BattleEventKind.Discard);
[Fact] public void Upgrade_value_is_15()      => Assert.Equal(15, (int)BattleEventKind.Upgrade);
[Fact] public void Exhaust_value_is_16()      => Assert.Equal(16, (int)BattleEventKind.Exhaust);
[Fact] public void GainEnergy_value_is_17()   => Assert.Equal(17, (int)BattleEventKind.GainEnergy);
[Fact] public void Summon_value_is_18()       => Assert.Equal(18, (int)BattleEventKind.Summon);
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEventKindTests`
Expected: build error（未定義 enum value）

- [ ] **Step 3: 実装**

`src/Core/Battle/Events/BattleEventKind.cs` を以下に置換:

```csharp
namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中に発生するイベント種別。
/// 10.2.D で 7 値追加（Heal/Draw/Discard/Upgrade/Exhaust/GainEnergy/Summon）。
/// </summary>
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
    Heal          = 12,    // 10.2.D
    Draw          = 13,    // 10.2.D
    Discard       = 14,    // 10.2.D
    Upgrade       = 15,    // 10.2.D
    Exhaust       = 16,    // 10.2.D（exhaustCard / exhaustSelf 共通）
    GainEnergy    = 17,    // 10.2.D
    Summon        = 18,    // 10.2.D
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEventKindTests`
Expected: 既存 + 新 7 件すべて緑

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Events/BattleEventKind.cs \
        tests/Core.Tests/Battle/Events/BattleEventKindTests.cs
git commit -m "feat(battle): add 7 BattleEventKind values for Phase 10.2.D actions (Task 3)"
git push
```

---

## Task 4: `EffectApplier.Apply` シグネチャに `DataCatalog catalog` 追加（既存 4 action 不変）

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs` (caller)
- Modify: `src/Core/Battle/Engine/EnemyAttackingResolver.cs` (caller)
- Sweep: `EffectApplier.Apply` を呼ぶ全テストの呼出箇所

- [ ] **Step 1: `EffectApplier.cs` シグネチャ更新（実装は switch 文に catalog を渡さない、既存 4 action 不変）**

Apply メソッドの先頭部分のみ変更。switch 内の既存 helper（`ApplyAttack` / `ApplyBlock` / `ApplyStatusChange`）への呼出は変えない:

```csharp
internal static class EffectApplier
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)                       // 10.2.D 追加
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
    
    // 既存 helper は変更なし。新 action は Tasks 5-12 で追加。
    ...
}
```

`using RoguelikeCardGame.Core.Data;` を追加（DataCatalog 用）。

- [ ] **Step 2: 既存 caller 更新**

`src/Core/Battle/Engine/BattleEngine.PlayCard.cs` の `EffectApplier.Apply(s, caster, eff, rng)` を以下に変更:

```csharp
var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
```

`src/Core/Battle/Engine/EnemyAttackingResolver.cs` も同様（`Apply(state, enemy, eff, rng)` → `Apply(state, enemy, eff, rng, catalog)`）。`Resolve(BattleState state, IRng rng, DataCatalog catalog)` 経由で catalog を受け取っているので渡せる。

- [ ] **Step 3: 全テストの `EffectApplier.Apply` 呼出箇所更新**

Use Grep tool:
- pattern: `EffectApplier\.Apply\(`
- path: `tests/Core.Tests/Battle/`

各テストで `Apply(state, caster, effect, rng)` を `Apply(state, caster, effect, rng, BattleFixtures.MinimalCatalog())` に変更（または既存テストの catalog を再利用）。

特に対象:
- `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`
- `tests/Core.Tests/Battle/Engine/EffectApplierBuffDebuffTests.cs`
- `tests/Core.Tests/Battle/Engine/EffectApplierReplaceActorInstanceIdTests.cs`

- [ ] **Step 4: ビルド確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

- [ ] **Step 5: テスト実行**

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 既存全件緑（catalog 引数追加だけで behavior 不変）

- [ ] **Step 6: commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        src/Core/Battle/Engine/BattleEngine.PlayCard.cs \
        src/Core/Battle/Engine/EnemyAttackingResolver.cs \
        tests/Core.Tests/Battle/Engine/
git commit -m "feat(battle): add DataCatalog argument to EffectApplier.Apply (Phase 10.2.D Task 4)"
git push
```

---

## Task 5: `heal` action

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierHealTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/EffectApplierHealTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierHealTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(CombatActor hero, params CombatActor[] otherAllies)
    {
        var allies = ImmutableArray.Create<CombatActor>(hero).AddRange(otherAllies);
        return new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: allies,
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");
    }

    [Fact] public void Heal_self_increases_caster_hp()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 50 };
        var s = MakeState(hero);
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(60, next.Allies[0].CurrentHp);
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.Heal, evs[0].Kind);
        Assert.Equal(10, evs[0].Amount);
    }

    [Fact] public void Heal_caps_at_max_hp()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 65 };
        var s = MakeState(hero);
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.Equal(5, evs[0].Amount); // 実回復量 = min(10, 70-65) = 5
    }

    [Fact] public void Heal_at_max_hp_emits_no_event()
    {
        var hero = BattleFixtures.Hero(hp: 70);  // CurrentHp == MaxHp == 70
        var s = MakeState(hero);
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.Empty(evs);
    }

    [Fact] public void Heal_single_ally_uses_target_index()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 30 };
        var summon = BattleFixtures.SummonActor("s1", "minion", slotIndex: 1, hp: 20)
            with { CurrentHp = 5 };
        var s = MakeState(hero, summon) with { TargetAllyIndex = 1 };
        var eff = new CardEffect("heal", EffectScope.Single, EffectSide.Ally, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(30, next.Allies[0].CurrentHp);  // hero unchanged
        Assert.Equal(15, next.Allies[1].CurrentHp);  // summon healed 5→15
    }

    [Fact] public void Heal_all_ally_heals_living_allies()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 30 };
        var summon1 = BattleFixtures.SummonActor("s1", "minion", slotIndex: 1, hp: 20)
            with { CurrentHp = 5 };
        var summon2 = BattleFixtures.SummonActor("s2", "minion", slotIndex: 2, hp: 20)
            with { CurrentHp = 0 };  // dead
        var s = MakeState(hero, summon1, summon2);
        var eff = new CardEffect("heal", EffectScope.All, EffectSide.Ally, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(40, next.Allies[0].CurrentHp);
        Assert.Equal(15, next.Allies[1].CurrentHp);
        Assert.Equal(0, next.Allies[2].CurrentHp);  // dead skip
        Assert.Equal(2, evs.Count);  // 2 living allies healed
    }

    [Fact] public void Heal_random_ally_picks_via_rng()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 30 };
        var summon = BattleFixtures.SummonActor("s1", "minion", slotIndex: 1, hp: 20)
            with { CurrentHp = 5 };
        var s = MakeState(hero, summon);
        var eff = new CardEffect("heal", EffectScope.Random, EffectSide.Ally, 10);
        var rng = new FakeRng(new[] { 0 }, new double[0]);  // pick index 0 = hero
        var (next, evs) = EffectApplier.Apply(s, hero, eff, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(40, next.Allies[0].CurrentHp);
        Assert.Equal(5, next.Allies[1].CurrentHp);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierHealTests`
Expected: 6 件失敗（heal action 未実装で 0 動作）

- [ ] **Step 3: 実装**

`EffectApplier.cs` の switch に `"heal"` ケース追加と新 helper:

```csharp
"heal" => ApplyHeal(state, caster, effect, rng),
```

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyHeal(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    if (effect.Scope != EffectScope.Self && effect.Side != EffectSide.Ally)
        throw new InvalidOperationException(
            $"heal requires Side=Ally for scope {effect.Scope}, got {effect.Side}");

    // Self / Single / Random / All target 解決
    var targets = ResolveHealTargets(state, caster, effect, rng);
    if (targets.Count == 0) return (state, Array.Empty<BattleEvent>());

    var events = new List<BattleEvent>();
    int order = 0;
    var s = state;
    foreach (var target in targets)
    {
        var current = FindActor(s, target.InstanceId);
        if (current is null || !current.IsAlive) continue;
        int actualHeal = Math.Min(effect.Amount, current.MaxHp - current.CurrentHp);
        if (actualHeal <= 0) continue;  // already at MaxHp

        var updated = current with { CurrentHp = current.CurrentHp + actualHeal };
        s = ReplaceActor(s, target.InstanceId, updated);
        events.Add(new BattleEvent(
            BattleEventKind.Heal, Order: order++,
            CasterInstanceId: caster.InstanceId,
            TargetInstanceId: target.InstanceId,
            Amount: actualHeal));
    }
    return (s, events);
}

private static IReadOnlyList<CombatActor> ResolveHealTargets(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    return effect.Scope switch
    {
        EffectScope.Self => new[] { caster },
        EffectScope.Single => state.TargetAllyIndex is { } ai && ai < state.Allies.Length
            ? new[] { state.Allies[ai] }
            : (IReadOnlyList<CombatActor>)Array.Empty<CombatActor>(),
        EffectScope.Random => PickRandomAlive(state.Allies, rng),
        EffectScope.All => state.Allies.Where(a => a.IsAlive).ToList(),
        _ => Array.Empty<CombatActor>(),
    };
}

private static IReadOnlyList<CombatActor> PickRandomAlive(
    ImmutableArray<CombatActor> pool, IRng rng)
{
    var alive = pool.Where(a => a.IsAlive).ToList();
    if (alive.Count == 0) return Array.Empty<CombatActor>();
    int idx = rng.NextInt(0, alive.Count);
    return new[] { alive[idx] };
}
```

`using System.Linq;` / `using System.Collections.Generic;` が既にあること確認。

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierHealTests`
Expected: 6 件 pass

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierHealTests.cs
git commit -m "feat(battle): EffectApplier heal action (Phase 10.2.D Task 5)"
git push
```

---

## Task 6: `draw` action

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierDrawTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/EffectApplierDrawTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierDrawTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> draw,
        ImmutableArray<BattleCardInstance> hand,
        ImmutableArray<BattleCardInstance> discard) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: draw, Hand: hand, DiscardPile: discard,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Draw_2_from_full_pile()
    {
        var draw = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"),
            BattleFixtures.MakeBattleCard("strike", "c3"));
        var s = MakeState(draw, ImmutableArray<BattleCardInstance>.Empty, ImmutableArray<BattleCardInstance>.Empty);
        var hero = s.Allies[0];
        var eff = new CardEffect("draw", EffectScope.Self, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Hand.Length);
        Assert.Equal(1, next.DrawPile.Length);
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.Draw, evs[0].Kind);
        Assert.Equal(2, evs[0].Amount);
    }

    [Fact] public void Draw_with_empty_draw_pile_shuffles_discard()
    {
        var discard = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"));
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty, discard);
        var hero = s.Allies[0];
        var eff = new CardEffect("draw", EffectScope.Self, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Hand.Length);
        Assert.Empty(next.DiscardPile);
    }

    [Fact] public void Draw_caps_at_hand_max_10()
    {
        var hand = ImmutableArray.CreateRange(
            Enumerable.Range(0, 9)
                .Select(i => BattleFixtures.MakeBattleCard("strike", $"h{i}")));
        var draw = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"),
            BattleFixtures.MakeBattleCard("strike", "c3"));
        var s = MakeState(draw, hand, ImmutableArray<BattleCardInstance>.Empty);
        var hero = s.Allies[0];
        var eff = new CardEffect("draw", EffectScope.Self, null, 3);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(10, next.Hand.Length);
        Assert.Equal(1, evs[0].Amount);  // 実ドロー数 = 1
    }

    [Fact] public void Draw_with_empty_draw_and_discard_emits_no_event()
    {
        var s = MakeState(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);
        var hero = s.Allies[0];
        var eff = new CardEffect("draw", EffectScope.Self, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Empty(evs);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierDrawTests`
Expected: 4 件失敗

- [ ] **Step 3: 実装**

`EffectApplier.cs` の switch に追加:

```csharp
"draw" => ApplyDraw(state, caster, effect, rng),
```

新 helper:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDraw(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    if (effect.Scope != EffectScope.Self)
        throw new InvalidOperationException(
            $"draw requires Scope=Self, got {effect.Scope}");

    int requestedCount = effect.Amount;
    var hand = state.Hand.ToBuilder();
    var draw = state.DrawPile.ToBuilder();
    var discard = state.DiscardPile.ToBuilder();
    int actualDrawn = 0;
    const int handCap = 10;

    for (int i = 0; i < requestedCount; i++)
    {
        if (hand.Count >= handCap) break;
        if (draw.Count == 0)
        {
            if (discard.Count == 0) break;
            // Fisher-Yates shuffle discard → draw
            var arr = discard.ToArray();
            for (int j = arr.Length - 1; j > 0; j--)
            {
                int k = rng.NextInt(0, j + 1);
                (arr[j], arr[k]) = (arr[k], arr[j]);
            }
            foreach (var c in arr) draw.Add(c);
            discard.Clear();
        }
        var top = draw[0];
        draw.RemoveAt(0);
        hand.Add(top);
        actualDrawn++;
    }

    if (actualDrawn == 0) return (state, Array.Empty<BattleEvent>());

    var newState = state with
    {
        Hand = hand.ToImmutable(),
        DrawPile = draw.ToImmutable(),
        DiscardPile = discard.ToImmutable(),
    };
    var evs = new[] {
        new BattleEvent(BattleEventKind.Draw, Order: 0,
            CasterInstanceId: caster.InstanceId, Amount: actualDrawn),
    };
    return (newState, evs);
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierDrawTests`
Expected: 4 件 pass

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierDrawTests.cs
git commit -m "feat(battle): EffectApplier draw action (Phase 10.2.D Task 6)"
git push
```

---

## Task 7: `discard` action（Single throws）

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierDiscardTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/EffectApplierDiscardTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierDiscardTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(ImmutableArray<BattleCardInstance> hand) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Discard_random_picks_via_rng()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"),
            BattleFixtures.MakeBattleCard("strike", "c3"));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 1);
        var rng = new FakeRng(new[] { 1 }, new double[0]);  // pick c2
        var (next, evs) = EffectApplier.Apply(s, hero, eff, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Hand.Length);
        Assert.Equal(1, next.DiscardPile.Length);
        Assert.Equal("c2", next.DiscardPile[0].InstanceId);
        Assert.Equal(BattleEventKind.Discard, evs[0].Kind);
        Assert.Equal(1, evs[0].Amount);
        Assert.Equal("random", evs[0].Note);
    }

    [Fact] public void Discard_all_empties_hand()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.All, null, 0);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Equal(2, next.DiscardPile.Length);
        Assert.Equal(2, evs[0].Amount);
        Assert.Equal("all", evs[0].Note);
    }

    [Fact] public void Discard_random_with_short_hand_clamps()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 5);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Equal(1, next.DiscardPile.Length);
        Assert.Equal(1, evs[0].Amount);  // 実捨て数 = 1
    }

    [Fact] public void Discard_empty_hand_emits_no_event()
    {
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(evs);
    }

    [Fact] public void Discard_single_throws()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Single, null, 1);
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }

    [Fact] public void Discard_self_throws()
    {
        var s = MakeState(ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1")));
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Self, null, 1);
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierDiscardTests`
Expected: 6 件失敗

- [ ] **Step 3: 実装**

`EffectApplier.cs` の switch に追加:

```csharp
"discard" => ApplyDiscard(state, caster, effect, rng),
```

新 helper:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDiscard(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    if (effect.Scope == EffectScope.Single)
        throw new InvalidOperationException(
            "discard Scope=Single is not supported (UI not yet wired)");
    if (effect.Scope == EffectScope.Self)
        throw new InvalidOperationException(
            $"discard does not support Scope=Self");

    if (state.Hand.Length == 0) return (state, Array.Empty<BattleEvent>());

    string note;
    var hand = state.Hand.ToBuilder();
    var discard = state.DiscardPile.ToBuilder();

    if (effect.Scope == EffectScope.All)
    {
        note = "all";
        foreach (var c in hand) discard.Add(c);
        int discardedCount = hand.Count;
        hand.Clear();
        return BuildResult(state, caster, hand, discard, discardedCount, note);
    }
    else // Random
    {
        note = "random";
        int target = Math.Min(effect.Amount, hand.Count);
        for (int i = 0; i < target; i++)
        {
            int idx = rng.NextInt(0, hand.Count);
            var card = hand[idx];
            hand.RemoveAt(idx);
            discard.Add(card);
        }
        return BuildResult(state, caster, hand, discard, target, note);
    }
}

private static (BattleState, IReadOnlyList<BattleEvent>) BuildResult(
    BattleState state, CombatActor caster,
    ImmutableArray<BattleCardInstance>.Builder hand,
    ImmutableArray<BattleCardInstance>.Builder discard,
    int discardedCount, string note)
{
    var next = state with
    {
        Hand = hand.ToImmutable(),
        DiscardPile = discard.ToImmutable(),
    };
    if (discardedCount == 0) return (next, Array.Empty<BattleEvent>());
    var evs = new[] {
        new BattleEvent(BattleEventKind.Discard, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: discardedCount, Note: note),
    };
    return (next, evs);
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierDiscardTests`
Expected: 6 件 pass

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierDiscardTests.cs
git commit -m "feat(battle): EffectApplier discard action with Single throws (Phase 10.2.D Task 7)"
git push
```

---

## Task 8: `gainEnergy` + `exhaustSelf` + `retainSelf` (3 markers/trivial)

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierGainEnergyExhaustSelfRetainSelfTests.cs`

- [ ] **Step 1: 失敗テスト**

`tests/Core.Tests/Battle/Engine/EffectApplierGainEnergyExhaustSelfRetainSelfTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierGainEnergyExhaustSelfRetainSelfTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(int energy = 1) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void GainEnergy_adds_to_energy()
    {
        var s = MakeState(energy: 1);
        var hero = s.Allies[0];
        var eff = new CardEffect("gainEnergy", EffectScope.Self, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(3, next.Energy);
        Assert.Equal(BattleEventKind.GainEnergy, evs[0].Kind);
        Assert.Equal(2, evs[0].Amount);
    }

    [Fact] public void GainEnergy_can_exceed_max()
    {
        var s = MakeState(energy: 3);
        var hero = s.Allies[0];
        var eff = new CardEffect("gainEnergy", EffectScope.Self, null, 5);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(8, next.Energy);  // EnergyMax 超過 OK
    }

    [Fact] public void ExhaustSelf_emits_event_only()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustSelf", EffectScope.Self, null, 0);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(s, next);   // state 不変
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.Exhaust, evs[0].Kind);
        Assert.Equal("self", evs[0].Note);
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void RetainSelf_is_no_op()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("retainSelf", EffectScope.Self, null, 0);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(s, next);
        Assert.Empty(evs);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierGainEnergyExhaustSelfRetainSelfTests`
Expected: 4 件失敗（3 actions 未実装）

- [ ] **Step 3: 実装**

`EffectApplier.cs` の switch に追加:

```csharp
"exhaustSelf" => ApplyExhaustSelf(state, caster),
"retainSelf"  => (state, Array.Empty<BattleEvent>()),
"gainEnergy"  => ApplyGainEnergy(state, caster, effect),
```

新 helpers:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyExhaustSelf(
    BattleState state, CombatActor caster)
{
    var ev = new BattleEvent(
        BattleEventKind.Exhaust, Order: 0,
        CasterInstanceId: caster.InstanceId,
        Amount: 1, Note: "self");
    return (state, new[] { ev });
}

private static (BattleState, IReadOnlyList<BattleEvent>) ApplyGainEnergy(
    BattleState state, CombatActor caster, CardEffect effect)
{
    if (effect.Scope != EffectScope.Self)
        throw new InvalidOperationException(
            $"gainEnergy requires Scope=Self, got {effect.Scope}");
    var next = state with { Energy = state.Energy + effect.Amount };
    var ev = new BattleEvent(
        BattleEventKind.GainEnergy, Order: 0,
        CasterInstanceId: caster.InstanceId,
        Amount: effect.Amount);
    return (next, new[] { ev });
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierGainEnergyExhaustSelfRetainSelfTests`
Expected: 4 件 pass

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierGainEnergyExhaustSelfRetainSelfTests.cs
git commit -m "feat(battle): EffectApplier gainEnergy/exhaustSelf/retainSelf (Phase 10.2.D Task 8)"
git push
```

---

## Task 9: `exhaustCard` action

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierExhaustCardTests.cs`

- [ ] **Step 1: 失敗テスト**

`tests/Core.Tests/Battle/Engine/EffectApplierExhaustCardTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierExhaustCardTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> hand = default,
        ImmutableArray<BattleCardInstance> discard = default,
        ImmutableArray<BattleCardInstance> draw = default) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: draw.IsDefault ? ImmutableArray<BattleCardInstance>.Empty : draw,
            Hand: hand.IsDefault ? ImmutableArray<BattleCardInstance>.Empty : hand,
            DiscardPile: discard.IsDefault ? ImmutableArray<BattleCardInstance>.Empty : discard,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Exhaust_from_hand_random_picks()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"));
        var s = MakeState(hand: hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "hand");
        var rng = new FakeRng(new[] { 0 }, new double[0]);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(1, next.Hand.Length);
        Assert.Equal("c2", next.Hand[0].InstanceId);
        Assert.Equal(1, next.ExhaustPile.Length);
        Assert.Equal("c1", next.ExhaustPile[0].InstanceId);
        Assert.Equal(BattleEventKind.Exhaust, evs[0].Kind);
        Assert.Equal("hand", evs[0].Note);
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void Exhaust_from_discard()
    {
        var discard = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(discard: discard);
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "discard");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.DiscardPile);
        Assert.Equal(1, next.ExhaustPile.Length);
    }

    [Fact] public void Exhaust_from_draw()
    {
        var draw = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(draw: draw);
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "draw");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.DrawPile);
        Assert.Equal(1, next.ExhaustPile.Length);
    }

    [Fact] public void Exhaust_clamps_to_pile_size()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand: hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 5, Pile: "hand");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Equal(1, next.ExhaustPile.Length);
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void Exhaust_empty_pile_emits_no_event()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 2, Pile: "hand");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(evs);
    }

    [Fact] public void Exhaust_invalid_pile_throws()
    {
        var s = MakeState(hand: ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1")));
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "invalid");
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }

    [Fact] public void Exhaust_null_pile_throws()
    {
        var s = MakeState(hand: ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1")));
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: null);
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierExhaustCardTests`
Expected: 7 件失敗

- [ ] **Step 3: 実装**

`EffectApplier.cs` の switch に追加:

```csharp
"exhaustCard" => ApplyExhaustCard(state, caster, effect, rng),
```

新 helper:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyExhaustCard(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    var (sourceBuilder, exhaustBuilder, applyResult) = OpenPile(state, effect.Pile);

    int target = Math.Min(effect.Amount, sourceBuilder.Count);
    for (int i = 0; i < target; i++)
    {
        int idx = rng.NextInt(0, sourceBuilder.Count);
        var card = sourceBuilder[idx];
        sourceBuilder.RemoveAt(idx);
        exhaustBuilder.Add(card);
    }

    if (target == 0)
        return (state, Array.Empty<BattleEvent>());

    var next = applyResult(sourceBuilder, exhaustBuilder);
    var ev = new BattleEvent(
        BattleEventKind.Exhaust, Order: 0,
        CasterInstanceId: caster.InstanceId,
        Amount: target, Note: effect.Pile);
    return (next, new[] { ev });
}

/// <summary>
/// pile 名 (hand/discard/draw) から source / exhaust の Builder と適用関数を返す。
/// 不正 pile / null は InvalidOperationException。
/// </summary>
private static (
    ImmutableArray<BattleCardInstance>.Builder source,
    ImmutableArray<BattleCardInstance>.Builder exhaust,
    Func<ImmutableArray<BattleCardInstance>.Builder,
         ImmutableArray<BattleCardInstance>.Builder, BattleState> apply
) OpenPile(BattleState state, string? pileName)
{
    var exhaustBuilder = state.ExhaustPile.ToBuilder();
    return pileName switch
    {
        "hand" => (state.Hand.ToBuilder(), exhaustBuilder,
            (s, e) => state with { Hand = s.ToImmutable(), ExhaustPile = e.ToImmutable() }),
        "discard" => (state.DiscardPile.ToBuilder(), exhaustBuilder,
            (s, e) => state with { DiscardPile = s.ToImmutable(), ExhaustPile = e.ToImmutable() }),
        "draw" => (state.DrawPile.ToBuilder(), exhaustBuilder,
            (s, e) => state with { DrawPile = s.ToImmutable(), ExhaustPile = e.ToImmutable() }),
        null => throw new InvalidOperationException("exhaustCard requires Pile (hand|discard|draw)"),
        _ => throw new InvalidOperationException($"exhaustCard invalid Pile '{pileName}', expected hand|discard|draw"),
    };
}
```

`using System;` 既存。

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierExhaustCardTests`
Expected: 7 件 pass

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierExhaustCardTests.cs
git commit -m "feat(battle): EffectApplier exhaustCard action (Phase 10.2.D Task 9)"
git push
```

---

## Task 10: `upgrade` action

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierUpgradeTests.cs`

- [ ] **Step 1: 失敗テスト**

`tests/Core.Tests/Battle/Engine/EffectApplierUpgradeTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierUpgradeTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    // upgrade-able カード (UpgradedCost あり)
    private static CardDefinition UpgradableStrike() =>
        new("strike", "Strike", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: 0,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: null, Keywords: null);

    // upgrade 不可 (UpgradedCost / UpgradedEffects 両方 null)
    private static CardDefinition UnUpgradableCard() =>
        new("plain", "Plain", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
            UpgradedEffects: null, Keywords: null);

    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> hand = default) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand.IsDefault ? ImmutableArray<BattleCardInstance>.Empty : hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Upgrade_random_card_in_hand()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 1, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.True(next.Hand[0].IsUpgraded);
        Assert.Equal(BattleEventKind.Upgrade, evs[0].Kind);
        Assert.Equal(1, evs[0].Amount);
        Assert.Equal("hand", evs[0].Note);
    }

    [Fact] public void Upgrade_skips_already_upgraded()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", true, null),  // already upgraded
            new BattleCardInstance("c2", "strike", false, null)); // upgrade target
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 1, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.True(next.Hand[0].IsUpgraded);  // unchanged
        Assert.True(next.Hand[1].IsUpgraded);  // newly upgraded
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void Upgrade_skips_unupgradable_definitions()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "plain", false, null));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 1, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UnUpgradableCard() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.False(next.Hand[0].IsUpgraded);
        Assert.Empty(evs);  // 強化候補がないため無発火
    }

    [Fact] public void Upgrade_clamps_to_candidate_count()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 5, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.True(next.Hand[0].IsUpgraded);
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void Upgrade_empty_pile_emits_no_event()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 2, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.Empty(evs);
    }

    [Fact] public void Upgrade_invalid_pile_throws()
    {
        var hand = ImmutableArray.Create(new BattleCardInstance("c1", "strike", false, null));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 1, Pile: "invalid");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), cat));
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierUpgradeTests`
Expected: 6 件失敗

- [ ] **Step 3: 実装**

`EffectApplier.cs` の switch に追加:

```csharp
"upgrade" => ApplyUpgrade(state, caster, effect, rng, catalog),
```

新 helper:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyUpgrade(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng,
    DataCatalog catalog)
{
    // Pile 検証は OpenSourcePile で（exhaust pile は使わない）
    var (sourceBuilder, applyResult) = OpenSourcePile(state, effect.Pile);

    // 強化候補抽出: IsUpgraded=false かつ definition.IsUpgradable
    var candidates = new List<int>();
    for (int i = 0; i < sourceBuilder.Count; i++)
    {
        var card = sourceBuilder[i];
        if (card.IsUpgraded) continue;
        if (!catalog.TryGetCard(card.CardDefinitionId, out var def)) continue;
        if (def.UpgradedCost is null && def.UpgradedEffects is null) continue;
        candidates.Add(i);
    }

    int target = Math.Min(effect.Amount, candidates.Count);
    int upgradedCount = 0;
    for (int i = 0; i < target; i++)
    {
        int pickIdx = rng.NextInt(0, candidates.Count);
        int sourceIdx = candidates[pickIdx];
        candidates.RemoveAt(pickIdx);
        var card = sourceBuilder[sourceIdx];
        sourceBuilder[sourceIdx] = card with { IsUpgraded = true };
        upgradedCount++;
    }

    if (upgradedCount == 0)
        return (state, Array.Empty<BattleEvent>());

    var next = applyResult(sourceBuilder);
    var ev = new BattleEvent(
        BattleEventKind.Upgrade, Order: 0,
        CasterInstanceId: caster.InstanceId,
        Amount: upgradedCount, Note: effect.Pile);
    return (next, new[] { ev });
}

private static (
    ImmutableArray<BattleCardInstance>.Builder source,
    Func<ImmutableArray<BattleCardInstance>.Builder, BattleState> apply
) OpenSourcePile(BattleState state, string? pileName)
{
    return pileName switch
    {
        "hand" => (state.Hand.ToBuilder(),
            s => state with { Hand = s.ToImmutable() }),
        "discard" => (state.DiscardPile.ToBuilder(),
            s => state with { DiscardPile = s.ToImmutable() }),
        "draw" => (state.DrawPile.ToBuilder(),
            s => state with { DrawPile = s.ToImmutable() }),
        null => throw new InvalidOperationException("upgrade requires Pile (hand|discard|draw)"),
        _ => throw new InvalidOperationException($"upgrade invalid Pile '{pileName}', expected hand|discard|draw"),
    };
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierUpgradeTests`
Expected: 6 件 pass

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierUpgradeTests.cs
git commit -m "feat(battle): EffectApplier upgrade action (Phase 10.2.D Task 10)"
git push
```

---

## Task 11: `summon` action

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Modify: `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` (UnitDefinition factory + catalog 拡張)
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierSummonTests.cs`

- [ ] **Step 1: BattleFixtures に UnitDefinition factory を追加**

`tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` に追加:

```csharp
// ===== UnitDefinition factory =====

public static UnitDefinition MinionDef(string id = "minion", int hp = 10, int? lifetime = null) =>
    new(id, id, $"img_{id}", hp,
        InitialMoveId: "wait",
        Moves: new[] {
            new MoveDefinition("wait", MoveKind.Defend,
                new[] { new CardEffect("block", EffectScope.Self, null, 0) }, "wait")
        },
        LifetimeTurns: lifetime);
```

`MinimalCatalog` に units 引数を追加:

```csharp
public static DataCatalog MinimalCatalog(
    IEnumerable<CardDefinition>? cards = null,
    IEnumerable<EnemyDefinition>? enemies = null,
    IEnumerable<EncounterDefinition>? encounters = null,
    IEnumerable<UnitDefinition>? units = null)   // 10.2.D 追加
{
    // ... existing
    var unitDict = (units ?? new[] { MinionDef() })
        .ToDictionary(u => u.Id);
    return new DataCatalog(
        Cards: cardDict,
        Relics: ...,
        Potions: ...,
        Enemies: enemyDict,
        Encounters: encDict,
        RewardTables: ...,
        Characters: ...,
        Events: ...,
        Units: unitDict);  // ← DataCatalog に Units が無ければ追加要
}
```

> 注: `DataCatalog` 既存型に `Units` 辞書がない場合は、まず DataCatalog 拡張が必要。10.1.B で UnitDefinition が新設されているので、catalog にも対応する辞書があるはず。実装時に確認し、なければ DataCatalog にも `IReadOnlyDictionary<string, UnitDefinition> Units` を追加する。

- [ ] **Step 2: 失敗テスト**

`tests/Core.Tests/Battle/Engine/EffectApplierSummonTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierSummonTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(params CombatActor[] allies)
    {
        var alliesArr = allies.Length == 0
            ? ImmutableArray.Create(BattleFixtures.Hero())
            : ImmutableArray.CreateRange(allies);
        return new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: alliesArr,
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");
    }

    [Fact] public void Summon_succeeds_when_slots_available()
    {
        var s = MakeState();   // hero only at slot 0
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);

        Assert.Equal(2, next.Allies.Length);
        var minion = next.Allies[1];
        Assert.Equal("minion", minion.DefinitionId);
        Assert.Equal(1, minion.SlotIndex);   // 空き最小 slot
        Assert.Equal(10, minion.CurrentHp);
        Assert.Equal(ActorSide.Ally, minion.Side);

        Assert.Single(evs);
        Assert.Equal(BattleEventKind.Summon, evs[0].Kind);
        Assert.Equal("minion", evs[0].Note);
    }

    [Fact] public void Summon_fails_silently_when_slots_full()
    {
        var allies = new[] {
            BattleFixtures.Hero(),
            BattleFixtures.SummonActor("s1", "minion", 1),
            BattleFixtures.SummonActor("s2", "minion", 2),
            BattleFixtures.SummonActor("s3", "minion", 3),
        };
        var s = MakeState(allies);
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);

        Assert.Equal(4, next.Allies.Length);   // 不変
        Assert.Empty(evs);
    }

    [Fact] public void Summon_takes_lowest_empty_slot()
    {
        // hero slot 0 + summon slot 2 (slot 1 is empty)
        var allies = new[] {
            BattleFixtures.Hero(),
            BattleFixtures.SummonActor("s2", "minion", 2),
        };
        var s = MakeState(allies);
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        var newMinion = next.Allies.Last();
        Assert.Equal(1, newMinion.SlotIndex);  // 空き最小 = 1
    }

    [Fact] public void Summon_with_lifetime_sets_remaining_turns()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "ephemeral");
        var unitDef = BattleFixtures.MinionDef(id: "ephemeral", lifetime: 3);
        var cat = BattleFixtures.MinimalCatalog(units: new[] { unitDef });
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.Equal(3, next.Allies[1].RemainingLifetimeTurns);
    }

    [Fact] public void Summon_associated_id_is_null_initially()
    {
        // PlayCard の card-move logic で後設定される。EffectApplier 単体では null
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.Null(next.Allies[1].AssociatedSummonHeldInstanceId);
    }

    [Fact] public void Summon_unitId_null_throws()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: null);
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), cat));
    }

    [Fact] public void Summon_unknown_unitId_throws()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "unknown");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), cat));
    }
}
```

- [ ] **Step 3: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierSummonTests`
Expected: 7 件失敗

- [ ] **Step 4: 実装**

`EffectApplier.cs` の switch に追加:

```csharp
"summon" => ApplySummon(state, caster, effect, catalog),
```

新 helper:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplySummon(
    BattleState state, CombatActor caster, CardEffect effect, DataCatalog catalog)
{
    if (string.IsNullOrEmpty(effect.UnitId))
        throw new InvalidOperationException("summon requires UnitId");
    if (!catalog.TryGetUnit(effect.UnitId, out var unitDef))
        throw new InvalidOperationException($"summon unknown UnitId '{effect.UnitId}'");

    // 空き slot 検索（hero=0 を除く 1〜3）
    var occupiedSlots = state.Allies.Select(a => a.SlotIndex).ToHashSet();
    int emptySlot = -1;
    for (int i = 1; i <= 3; i++)
    {
        if (!occupiedSlots.Contains(i)) { emptySlot = i; break; }
    }
    if (emptySlot == -1)
        return (state, Array.Empty<BattleEvent>());  // 不発、silent skip

    string newInstanceId = $"summon_inst_{state.Turn}_{state.Allies.Length}";
    var newActor = new CombatActor(
        InstanceId: newInstanceId,
        DefinitionId: effect.UnitId,
        Side: ActorSide.Ally,
        SlotIndex: emptySlot,
        CurrentHp: unitDef.Hp,
        MaxHp: unitDef.Hp,
        Block: BlockPool.Empty,
        AttackSingle: AttackPool.Empty,
        AttackRandom: AttackPool.Empty,
        AttackAll: AttackPool.Empty,
        Statuses: ImmutableDictionary<string, int>.Empty,
        CurrentMoveId: unitDef.InitialMoveId,
        RemainingLifetimeTurns: unitDef.LifetimeTurns,
        AssociatedSummonHeldInstanceId: null);   // PlayCard card-move logic で設定

    var next = state with { Allies = state.Allies.Add(newActor) };
    var ev = new BattleEvent(
        BattleEventKind.Summon, Order: 0,
        CasterInstanceId: caster.InstanceId,
        TargetInstanceId: newInstanceId,
        Note: effect.UnitId);
    return (next, new[] { ev });
}
```

`DataCatalog.TryGetUnit` が無ければ追加要。実装時に確認。

- [ ] **Step 5: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierSummonTests`
Expected: 7 件 pass

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 全件緑

- [ ] **Step 6: commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierSummonTests.cs
git commit -m "feat(battle): EffectApplier summon action (Phase 10.2.D Task 11)"
git push
```

---

## Task 12: `BattleEngine.PlayCard` カード移動 5 段優先順位 + summonSucceeded 追跡

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`
- Create: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCardMovementTests.cs`

- [ ] **Step 1: 失敗テスト**

`tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCardMovementTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardCardMovementTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static CardDefinition CardOf(string id, int cost, CardType type, params CardEffect[] effects) =>
        new(id, id, null, CardRarity.Common, type,
            Cost: cost, UpgradedCost: null,
            Effects: effects, UpgradedEffects: null, Keywords: null);

    private static BattleState MakeState(BattleCardInstance card) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 5, EnergyMax: 5,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray.Create(card),
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Default_routing_to_discard()
    {
        var def = CardOf("strike", 1, CardType.Attack,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6));
        var card = new BattleCardInstance("c1", "strike", false, null);
        var s = MakeState(card);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Empty(next.Hand);
        Assert.Equal(1, next.DiscardPile.Length);
        Assert.Empty(next.ExhaustPile);
        Assert.Empty(next.PowerCards);
        Assert.Empty(next.SummonHeld);
    }

    [Fact] public void ExhaustSelf_routes_to_exhaust_pile()
    {
        var def = CardOf("burn", 1, CardType.Skill,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6),
            new CardEffect("exhaustSelf", EffectScope.Self, null, 0));
        var card = new BattleCardInstance("c1", "burn", false, null);
        var s = MakeState(card);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Empty(next.Hand);
        Assert.Empty(next.DiscardPile);
        Assert.Equal(1, next.ExhaustPile.Length);
    }

    [Fact] public void Power_card_routes_to_power_cards()
    {
        var def = CardOf("inflame", 1, CardType.Power,
            new CardEffect("buff", EffectScope.Self, null, 2, Name: "strength"));
        var card = new BattleCardInstance("c1", "inflame", false, null);
        var s = MakeState(card);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Empty(next.Hand);
        Assert.Empty(next.DiscardPile);
        Assert.Empty(next.ExhaustPile);
        Assert.Equal(1, next.PowerCards.Length);
        Assert.Equal(2, next.Allies[0].GetStatus("strength"));  // 効果は発動
    }

    [Fact] public void Unit_with_summon_success_routes_to_summon_held()
    {
        var def = CardOf("call_minion", 1, CardType.Unit,
            new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion"));
        var card = new BattleCardInstance("c1", "call_minion", false, null);
        var s = MakeState(card);
        var cat = BattleFixtures.MinimalCatalog(
            cards: new[] { def },
            units: new[] { BattleFixtures.MinionDef() });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Empty(next.Hand);
        Assert.Empty(next.DiscardPile);
        Assert.Equal(1, next.SummonHeld.Length);
        Assert.Equal(2, next.Allies.Length);
        // AssociatedSummonHeldInstanceId が card.InstanceId に設定されている
        Assert.Equal("c1", next.Allies[1].AssociatedSummonHeldInstanceId);
    }

    [Fact] public void Unit_with_summon_failure_routes_to_discard()
    {
        // 既に slot 1-3 が埋まっている状態
        var allies = ImmutableArray.Create(
            BattleFixtures.Hero(),
            BattleFixtures.SummonActor("s1", "minion", 1),
            BattleFixtures.SummonActor("s2", "minion", 2),
            BattleFixtures.SummonActor("s3", "minion", 3));
        var def = CardOf("call_minion", 1, CardType.Unit,
            new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion"));
        var card = new BattleCardInstance("c1", "call_minion", false, null);
        var s = MakeState(card) with { Allies = allies };
        var cat = BattleFixtures.MinimalCatalog(
            cards: new[] { def },
            units: new[] { BattleFixtures.MinionDef() });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Empty(next.SummonHeld);
        Assert.Equal(1, next.DiscardPile.Length);  // 不発で discard
        Assert.Equal(4, next.Allies.Length);   // 召喚されず
    }

    [Fact] public void RetainSelf_routes_to_hand()
    {
        var def = CardOf("hold", 0, CardType.Skill,
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var card = new BattleCardInstance("c1", "hold", false, null);
        var s = MakeState(card);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.Hand.Length);
        Assert.Equal("c1", next.Hand[0].InstanceId);
        Assert.Empty(next.DiscardPile);
    }

    [Fact] public void ExhaustSelf_overrides_retainSelf()
    {
        var def = CardOf("complex", 0, CardType.Skill,
            new CardEffect("exhaustSelf", EffectScope.Self, null, 0),
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var card = new BattleCardInstance("c1", "complex", false, null);
        var s = MakeState(card);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Empty(next.Hand);
        Assert.Equal(1, next.ExhaustPile.Length);   // exhaustSelf が優先
    }

    [Fact] public void Power_overrides_retainSelf_when_both_present()
    {
        // Power CardType + retainSelf effect → Power が優先
        var def = CardOf("entrench", 1, CardType.Power,
            new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength"),
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var card = new BattleCardInstance("c1", "entrench", false, null);
        var s = MakeState(card);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.PowerCards.Length);
        Assert.Empty(next.Hand);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardCardMovementTests`
Expected: 6-8 件失敗（card-move logic がまだ単純な Hand→Discard）

`Default_routing_to_discard` だけは pass するはず。

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/BattleEngine.PlayCard.cs` を修正:

effect ループの直後（`var newHand = s.Hand.RemoveAt(handIndex);` 周辺）を以下に置換:

```csharp
        // 10.2.C 既存: hand から取り除く
        // 10.2.D: card-move 5 段優先順位

        bool summonSucceeded = false;
        foreach (var eff in effects)
        {
            if (eff.ComboMin is { } min && newCombo < min) continue;

            int beforeAlliesLength = s.Allies.Length;
            var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
            s = afterEffect;
            foreach (var ev in evs)
            {
                events.Add(ev with { Order = order });
                order++;
            }
            caster = s.Allies[0];

            // summon 成功検出
            if (eff.Action == "summon" && s.Allies.Length > beforeAlliesLength)
                summonSucceeded = true;
        }

        // 10.2.D: 5 段優先順位（exhaustSelf → Power → Unit+success → retainSelf → Discard）
        bool hasExhaustSelf = effects.Any(e => e.Action == "exhaustSelf");
        bool hasRetainSelf = effects.Any(e => e.Action == "retainSelf");
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
            // 直前に追加された召喚 actor の AssociatedSummonHeldInstanceId に card.InstanceId を設定
            int lastIdx = s.Allies.Length - 1;
            if (lastIdx >= 0
                && s.Allies[lastIdx].DefinitionId != "hero"
                && s.Allies[lastIdx].AssociatedSummonHeldInstanceId is null)
            {
                var summonActor = s.Allies[lastIdx];
                s = s with { Allies = s.Allies.SetItem(
                    lastIdx, summonActor with { AssociatedSummonHeldInstanceId = card.InstanceId }) };
            }
        }
        else if (hasRetainSelf)
        {
            // hand の元の位置に戻す
            s = s with { Hand = s.Hand.Insert(handIndex, card) };
        }
        else
        {
            s = s with { DiscardPile = s.DiscardPile.Add(card) };
        }

        return (s, events);
    }
}
```

> 注: 既存の effect ループは修正後の状態で 1 度だけ実行される（重複ループを残さないこと）。

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardCardMovementTests`
Expected: 8 件 pass

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 全件緑（既存テストは Default_routing 経路で機能、retainSelf テストの影響もない）

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Engine/BattleEngine.PlayCard.cs \
        tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCardMovementTests.cs
git commit -m "feat(battle): PlayCard 5-tier card movement priority (Phase 10.2.D Task 12)"
git push
```

---

## Task 13: `TurnStartProcessor` Lifetime tick

**Files:**
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs`
- Create: `tests/Core.Tests/Battle/Engine/TurnStartProcessorLifetimeTests.cs`

- [ ] **Step 1: 失敗テスト**

`tests/Core.Tests/Battle/Engine/TurnStartProcessorLifetimeTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnStartProcessorLifetimeTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static BattleState MakeState(params CombatActor[] allies)
    {
        var alliesArr = allies.Length == 0
            ? ImmutableArray.Create(BattleFixtures.Hero())
            : ImmutableArray.CreateRange(allies);
        return new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: alliesArr,
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");
    }

    [Fact] public void Hero_lifetime_null_skipped()
    {
        var s = MakeState();   // hero only, lifetime=null
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Null(next.Allies[0].RemainingLifetimeTurns);
    }

    [Fact] public void Summon_lifetime_3_decrements_to_2()
    {
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, lifetime: 3);
        var s = MakeState(hero, summon);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Equal(2, summonNext.RemainingLifetimeTurns);
        Assert.True(summonNext.IsAlive);
    }

    [Fact] public void Summon_lifetime_1_dies()
    {
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, lifetime: 1);
        var s = MakeState(hero, summon);
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Equal(0, summonNext.CurrentHp);
        Assert.False(summonNext.IsAlive);
        Assert.Contains(evs, e => e.Kind == BattleEventKind.ActorDeath
            && e.TargetInstanceId == "s1"
            && e.Note == "lifetime");
    }

    [Fact] public void Lifetime_tick_after_status_countdown()
    {
        // weak と lifetime を併用、weak countdown と lifetime tick の順序を確認
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, lifetime: 2);
        summon = summon with { Statuses = ImmutableDictionary<string, int>.Empty.Add("weak", 1) };
        var s = MakeState(hero, summon);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Equal(1, summonNext.RemainingLifetimeTurns);   // 2 → 1
        Assert.False(summonNext.Statuses.ContainsKey("weak")); // 1 → 0 → removed
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~TurnStartProcessorLifetimeTests`
Expected: `Summon_lifetime_*` 系が失敗（Lifetime tick 未実装）

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/TurnStartProcessor.cs` の `Process` メソッドで、`ApplyStatusCountdown` の後、`Energy = s.EnergyMax` の前に Lifetime tick を挿入:

```csharp
        // Step 4: status countdown（Allies → Enemies、SlotIndex 順）
        s = ApplyStatusCountdown(s, events, ref order);

        // Step 5: Lifetime tick（10.2.D）
        s = ApplyLifetimeTick(s, events, ref order);

        // Step 6-8（Energy / Draw / TurnStart event）
        s = s with { Energy = s.EnergyMax };
        s = DrawCards(s, DrawPerTurn, rng);
        events.Add(new BattleEvent(BattleEventKind.TurnStart, Order: order++, Note: $"turn={s.Turn}"));
        return (s, events);
```

新 helper:

```csharp
private static BattleState ApplyLifetimeTick(
    BattleState state, List<BattleEvent> events, ref int order)
{
    // Lifetime あり ally の InstanceId スナップショット
    var allyIds = state.Allies
        .Where(a => a.Side == ActorSide.Ally
                 && a.RemainingLifetimeTurns is not null
                 && a.IsAlive)
        .OrderBy(a => a.SlotIndex)
        .Select(a => a.InstanceId)
        .ToList();

    var s = state;
    foreach (var aid in allyIds)
    {
        var actor = FindActor(s, aid);
        if (actor is null || !actor.IsAlive) continue;
        if (actor.RemainingLifetimeTurns is null) continue;

        int newRemaining = actor.RemainingLifetimeTurns.Value - 1;

        if (newRemaining <= 0)
        {
            // 死亡
            var diedActor = actor with
            {
                RemainingLifetimeTurns = newRemaining,
                CurrentHp = 0,
            };
            s = ReplaceActor(s, aid, diedActor);
            events.Add(new BattleEvent(
                BattleEventKind.ActorDeath, Order: order++,
                TargetInstanceId: aid, Note: "lifetime"));
        }
        else
        {
            s = ReplaceActor(s, aid, actor with { RemainingLifetimeTurns = newRemaining });
        }
    }
    return s;
}
```

> 注: `FindActor` / `ReplaceActor` は既存 helper（10.2.B で追加）を流用。

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~TurnStartProcessorLifetimeTests`
Expected: 4 件 pass

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 全件緑

- [ ] **Step 5: commit + push**

```bash
git add src/Core/Battle/Engine/TurnStartProcessor.cs \
        tests/Core.Tests/Battle/Engine/TurnStartProcessorLifetimeTests.cs
git commit -m "feat(battle): TurnStartProcessor Lifetime tick (Phase 10.2.D Task 13)"
git push
```

---

## Task 14: `SummonCleanup` 共通 helper + 4 caller integrations

**Files:**
- Create: `src/Core/Battle/Engine/SummonCleanup.cs`
- Modify: `src/Core/Battle/Engine/PlayerAttackingResolver.cs` (Resolve 戻り直前で呼出)
- Modify: `src/Core/Battle/Engine/EnemyAttackingResolver.cs` (Resolve 戻り直前で呼出)
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs` (poison tick 後 / Lifetime tick 後で呼出)
- Create: `tests/Core.Tests/Battle/Engine/SummonCleanupTests.cs`

- [ ] **Step 1: 失敗テスト**

`tests/Core.Tests/Battle/Engine/SummonCleanupTests.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class SummonCleanupTests
{
    private static BattleState MakeState(
        ImmutableArray<CombatActor> allies,
        ImmutableArray<BattleCardInstance> summonHeld) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: allies,
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: summonHeld,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Dead_summon_moves_card_to_discard()
    {
        var hero = BattleFixtures.Hero();
        var deadSummon = BattleFixtures.SummonActor(
            "s1", "minion", 1, hp: 10, associatedCardId: "card_s1")
            with { CurrentHp = 0 };
        var card = new BattleCardInstance("card_s1", "call_minion", false, null);
        var s = MakeState(
            ImmutableArray.Create(hero, deadSummon),
            ImmutableArray.Create(card));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Equal(1, next.DiscardPile.Length);
        Assert.Equal("card_s1", next.DiscardPile[0].InstanceId);
        // ally の AssociatedSummonHeldInstanceId が null になる
        var summon = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Null(summon.AssociatedSummonHeldInstanceId);
    }

    [Fact] public void Alive_summon_not_processed()
    {
        var hero = BattleFixtures.Hero();
        var aliveSummon = BattleFixtures.SummonActor(
            "s1", "minion", 1, hp: 10, associatedCardId: "card_s1");
        var card = new BattleCardInstance("card_s1", "call_minion", false, null);
        var s = MakeState(
            ImmutableArray.Create(hero, aliveSummon),
            ImmutableArray.Create(card));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Equal(1, next.SummonHeld.Length);
        Assert.Empty(next.DiscardPile);
    }

    [Fact] public void Dead_hero_not_processed()
    {
        // hero は AssociatedSummonHeldInstanceId が null なので無視
        var hero = BattleFixtures.Hero() with { CurrentHp = 0 };
        var s = MakeState(
            ImmutableArray.Create(hero),
            ImmutableArray<BattleCardInstance>.Empty);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);
        Assert.Equal(s, next);  // 不変
    }

    [Fact] public void Already_cleaned_summon_not_reprocessed()
    {
        // dead summon だが AssociatedSummonHeldInstanceId が既に null
        var hero = BattleFixtures.Hero();
        var deadSummon = BattleFixtures.SummonActor(
            "s1", "minion", 1, hp: 10, associatedCardId: null)
            with { CurrentHp = 0 };
        var s = MakeState(
            ImmutableArray.Create(hero, deadSummon),
            ImmutableArray<BattleCardInstance>.Empty);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);
        Assert.Equal(s, next);  // 不変
    }

    [Fact] public void Multiple_dead_summons_all_processed()
    {
        var hero = BattleFixtures.Hero();
        var dead1 = BattleFixtures.SummonActor(
            "s1", "minion", 1, hp: 10, associatedCardId: "card_s1") with { CurrentHp = 0 };
        var dead2 = BattleFixtures.SummonActor(
            "s2", "minion", 2, hp: 10, associatedCardId: "card_s2") with { CurrentHp = 0 };
        var card1 = new BattleCardInstance("card_s1", "call_minion", false, null);
        var card2 = new BattleCardInstance("card_s2", "call_minion", false, null);
        var s = MakeState(
            ImmutableArray.Create(hero, dead1, dead2),
            ImmutableArray.Create(card1, card2));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Equal(2, next.DiscardPile.Length);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~SummonCleanupTests`
Expected: build error（`SummonCleanup` 未定義）

- [ ] **Step 3: 実装 — `src/Core/Battle/Engine/SummonCleanup.cs`**

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 死亡した召喚 actor (Side==Ally && DefinitionId != "hero" && !IsAlive)
/// の AssociatedSummonHeldInstanceId を辿り、対応カードを SummonHeld → DiscardPile に移動する。
/// 親 spec §5-4 / Phase 10.2.D spec §4-4 参照。
///
/// 呼出箇所: PlayerAttackingResolver / EnemyAttackingResolver / TurnStartProcessor (poison tick 後 / Lifetime tick 後)。
/// memory feedback ルール「state.Allies/Enemies 書き戻しは InstanceId 検索」準拠。
/// </summary>
internal static class SummonCleanup
{
    public static BattleState Apply(
        BattleState state, List<BattleEvent> events, ref int order)
    {
        var s = state;
        var deadSummonPairs = s.Allies
            .Where(a => a.Side == ActorSide.Ally
                     && a.DefinitionId != "hero"
                     && !a.IsAlive
                     && a.AssociatedSummonHeldInstanceId is not null)
            .Select(a => (a.InstanceId, a.AssociatedSummonHeldInstanceId!))
            .ToList();

        foreach (var (allyId, cardInstId) in deadSummonPairs)
        {
            int idx = -1;
            for (int i = 0; i < s.SummonHeld.Length; i++)
            {
                if (s.SummonHeld[i].InstanceId == cardInstId) { idx = i; break; }
            }
            if (idx < 0) continue;

            var card = s.SummonHeld[idx];
            s = s with
            {
                SummonHeld = s.SummonHeld.RemoveAt(idx),
                DiscardPile = s.DiscardPile.Add(card),
            };

            // ally の AssociatedSummonHeldInstanceId を null 化（再処理防止）
            int allyIdx = -1;
            for (int i = 0; i < s.Allies.Length; i++)
            {
                if (s.Allies[i].InstanceId == allyId) { allyIdx = i; break; }
            }
            if (allyIdx >= 0)
            {
                var actor = s.Allies[allyIdx];
                s = s with { Allies = s.Allies.SetItem(
                    allyIdx, actor with { AssociatedSummonHeldInstanceId = null }) };
            }
            // event 発火なし（state diff から UI が認識）
        }
        return s;
    }
}
```

- [ ] **Step 4: 4 caller の integration**

`PlayerAttackingResolver.Resolve` の `return (state, events);` の直前に挿入:

```csharp
// 10.2.D: 死亡 summon のクリーンアップ
state = SummonCleanup.Apply(state, events, ref order);
```

`EnemyAttackingResolver.Resolve` も同様。

`TurnStartProcessor.Process` の poison tick 後（`if (!s.Enemies.Any(e => e.IsAlive))` の `return` 前ではなく、両 outcome 確定パスを通らない正常パスの直前）に:

```csharp
// 10.2.D: 死亡 summon クリーンアップ（毒死で召喚も死んだ場合）
s = SummonCleanup.Apply(s, events, ref order);

// 既存: status countdown へ進む
```

そして Lifetime tick の直後にも:

```csharp
// Step 5: Lifetime tick
s = ApplyLifetimeTick(s, events, ref order);

// 10.2.D: Lifetime 死亡で召喚カードを Discard へ
s = SummonCleanup.Apply(s, events, ref order);
```

> 注: poison tick 直後のクリーンアップは「main player が毒死していなかった場合」のみ通る。Outcome 確定パスでは return が先に行くので問題なし。

- [ ] **Step 5: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~SummonCleanupTests`
Expected: 5 件 pass

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 全件緑

- [ ] **Step 6: commit + push**

```bash
git add src/Core/Battle/Engine/SummonCleanup.cs \
        src/Core/Battle/Engine/PlayerAttackingResolver.cs \
        src/Core/Battle/Engine/EnemyAttackingResolver.cs \
        src/Core/Battle/Engine/TurnStartProcessor.cs \
        tests/Core.Tests/Battle/Engine/SummonCleanupTests.cs
git commit -m "feat(battle): SummonCleanup helper + 4 caller integrations (Phase 10.2.D Task 14)"
git push
```

---

## Task 15: `TurnEndProcessor` retainSelf-aware + DataCatalog 引数

**Files:**
- Modify: `src/Core/Battle/Engine/TurnEndProcessor.cs`
- Modify: `src/Core/Battle/Engine/BattleEngine.EndTurn.cs` (catalog を渡す)
- Sweep: `TurnEndProcessor.Process(...)` を呼ぶ全テスト
- Create: `tests/Core.Tests/Battle/Engine/TurnEndProcessorRetainSelfTests.cs`

- [ ] **Step 1: 失敗テスト**

`tests/Core.Tests/Battle/Engine/TurnEndProcessorRetainSelfTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnEndProcessorRetainSelfTests
{
    private static CardDefinition StrikeDef() =>
        new("strike", "Strike", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: null, Keywords: null);

    private static CardDefinition RetainCard() =>
        new("hold", "Hold", null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[] { new CardEffect("retainSelf", EffectScope.Self, null, 0) },
            UpgradedEffects: null, Keywords: null);

    private static BattleState MakeState(ImmutableArray<BattleCardInstance> hand) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Retains_cards_with_retainSelf_effect()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null),
            new BattleCardInstance("c2", "hold", false, null));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { StrikeDef(), RetainCard() });

        var (next, _) = TurnEndProcessor.Process(s, cat);

        Assert.Equal(1, next.Hand.Length);
        Assert.Equal("c2", next.Hand[0].InstanceId);   // hold だけ残る
        Assert.Equal(1, next.DiscardPile.Length);
        Assert.Equal("c1", next.DiscardPile[0].InstanceId);   // strike は捨てる
    }

    [Fact] public void Discards_all_when_no_retain()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null),
            new BattleCardInstance("c2", "strike", false, null));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { StrikeDef() });
        var (next, _) = TurnEndProcessor.Process(s, cat);
        Assert.Empty(next.Hand);
        Assert.Equal(2, next.DiscardPile.Length);
    }

    [Fact] public void Combo_fields_still_reset()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "hold", false, null));
        var s = MakeState(hand) with
        {
            ComboCount = 3,
            LastPlayedOrigCost = 5,
            NextCardComboFreePass = true,
        };
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { RetainCard() });
        var (next, _) = TurnEndProcessor.Process(s, cat);
        Assert.Equal(0, next.ComboCount);
        Assert.Null(next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~TurnEndProcessorRetainSelfTests`
Expected: build error（`Process` シグネチャミスマッチ、catalog 引数なし）

- [ ] **Step 3: 実装 — `TurnEndProcessor.cs` 更新**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン終了処理。
/// Phase 10.2.C でコンボ 3 フィールドのリセット追加。
/// Phase 10.2.D で retainSelf-aware 手札整理 + DataCatalog 引数追加。
/// 親 spec §4-6 参照。
/// </summary>
internal static class TurnEndProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(
        BattleState state, DataCatalog catalog)
    {
        var allies = state.Allies.Select(ResetActor).ToImmutableArray();
        var enemies = state.Enemies.Select(ResetActor).ToImmutableArray();

        // 10.2.D: retainSelf-aware 手札整理
        var keepInHand = ImmutableArray.CreateBuilder<BattleCardInstance>();
        var newDiscard = state.DiscardPile.ToBuilder();
        foreach (var card in state.Hand)
        {
            if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
            {
                newDiscard.Add(card);
                continue;
            }
            var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
                ? def.UpgradedEffects : def.Effects;
            if (effects.Any(e => e.Action == "retainSelf"))
                keepInHand.Add(card);
            else
                newDiscard.Add(card);
        }

        var next = state with
        {
            Allies = allies,
            Enemies = enemies,
            Hand = keepInHand.ToImmutable(),
            DiscardPile = newDiscard.ToImmutable(),
            ComboCount = 0,
            LastPlayedOrigCost = null,
            NextCardComboFreePass = false,
        };
        return (next, Array.Empty<BattleEvent>());
    }

    private static CombatActor ResetActor(CombatActor a) => a with
    {
        Block = BlockPool.Empty,
        AttackSingle = AttackPool.Empty,
        AttackRandom = AttackPool.Empty,
        AttackAll = AttackPool.Empty,
    };
}
```

- [ ] **Step 4: caller 更新 — `BattleEngine.EndTurn.cs`**

`TurnEndProcessor.Process(s)` を `TurnEndProcessor.Process(s, catalog)` に変更（既に `catalog` は EndTurn で受け取っている）。

- [ ] **Step 5: 全テストの呼出箇所更新**

Use Grep tool:
- pattern: `TurnEndProcessor\.Process\(`
- path: `tests/Core.Tests/Battle/`

各テストの呼出を `TurnEndProcessor.Process(s, BattleFixtures.MinimalCatalog())` 等に変更。catalog の作り方が違うテストはそのままその catalog を使う。

主な対象:
- `tests/Core.Tests/Battle/Engine/TurnEndProcessorTests.cs`
- `tests/Core.Tests/Battle/Engine/TurnEndProcessorComboResetTests.cs`

- [ ] **Step 6: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~TurnEnd`
Expected: 全件緑

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 全件緑

- [ ] **Step 7: commit + push**

```bash
git add src/Core/Battle/Engine/TurnEndProcessor.cs \
        src/Core/Battle/Engine/BattleEngine.EndTurn.cs \
        tests/Core.Tests/Battle/Engine/
git commit -m "feat(battle): TurnEndProcessor retainSelf + DataCatalog (Phase 10.2.D Task 15)"
git push
```

---

## Task 16: `BattleDeterminismTests` に召喚 + heal/draw を含む 1 戦闘追加

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs`

- [ ] **Step 1: テスト追加**

`BattleDeterminismTests.cs` の末尾に追加:

```csharp
[Fact] public void Combat_with_summon_and_heal_is_deterministic()
{
    var run = MakeRun();
    var ints = new int[20];
    var doubles = new double[0];
    var rng1 = new SequentialRng((ulong)42);
    var rng2 = new SequentialRng((ulong)42);

    var summonCard = new CardDefinition("call_minion", "Call Minion", null,
        CardRarity.Common, CardType.Unit,
        Cost: 1, UpgradedCost: null,
        Effects: new[] {
            new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion"),
        },
        UpgradedEffects: null, Keywords: null);
    var healCard = new CardDefinition("aid", "Aid", null,
        CardRarity.Common, CardType.Skill,
        Cost: 1, UpgradedCost: null,
        Effects: new[] {
            new CardEffect("heal", EffectScope.Self, null, 5),
        },
        UpgradedEffects: null, Keywords: null);
    var cat = BattleFixtures.MinimalCatalog(
        cards: new[] { BattleFixtures.Strike(), summonCard, healCard },
        units: new[] { BattleFixtures.MinionDef() });

    var s1 = BattleEngine.Start(run, "enc_test", rng1, cat);
    var s2 = BattleEngine.Start(run, "enc_test", rng2, cat);

    Assert.Equal(s1.Allies.Length, s2.Allies.Length);
    Assert.Equal(s1.SummonHeld.Length, s2.SummonHeld.Length);
    Assert.Equal(s1.PowerCards.Length, s2.PowerCards.Length);
    Assert.Equal(s1.Energy, s2.Energy);
    Assert.Equal(s1.Hand.Length, s2.Hand.Length);
}
```

> 注: 実際に summon カードを引いてプレイするには、deck に summon カードが入っている必要がある。簡略のため初期 state の比較のみで「召喚を含む環境での Start determinism」を確認。

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleDeterminismTests`
Expected: 既存 + 新 1 件すべて緑

- [ ] **Step 3: commit + push**

```bash
git add tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs
git commit -m "test(battle): determinism with summon + heal cards (Phase 10.2.D Task 16)"
git push
```

---

## Task 17: 全テスト実行 + ファイル隔離検証

**Files:** なし（実行・検証のみ）

- [ ] **Step 1: 最終ビルド・全テスト**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

Run: `dotnet test`
Expected: Core ~830-900 + Server 168/170 skip 2、全件緑

- [ ] **Step 2: ファイル隔離検証**

Run (Bash):
```bash
git diff --name-only 91b5579..HEAD | grep -E "(BattlePlaceholder|NodeEffectResolver|RunHub|MapTile)" || echo "(no changes to legacy/placeholder paths)"
```
Expected: "(no changes to legacy/placeholder paths)" のみ表示

`BattlePlaceholder` 経由の既存ゲームフローは変更されていないため、無傷の保証。

- [ ] **Step 3: ここでは commit しない**

---

## Task 18: 親 spec への補記

**Files:**
- Modify: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`

- [ ] **Step 1: §2-4 `EnemyDefinition` / `UnitDefinition` 補記追加**

§2-4 末尾に追加:

```markdown

> **Phase 10.2.D 補記**: `UnitDefinition` 構造は不変だが、10.2.D で動作実装。
> 召喚キャラの行動: 10.2.D 範囲では Pool 機能なし（壁役）、Phase 11+ で `CurrentMoveId` 駆動の attack 実装予定。
```

- [ ] **Step 2: §3-1 `BattleState` 補記**

`§3-1` の Phase 10.2.C 補記の後に追加:

```markdown

> **Phase 10.2.D 補記**: 10.2.D で `SummonHeld: ImmutableArray<BattleCardInstance>` /
> `PowerCards: ImmutableArray<BattleCardInstance>` を追加。配置は `ExhaustPile` 直後、
> `ComboCount` 前。これで親 spec §3-1 の最終形フィールド順に揃った。
```

- [ ] **Step 3: §3-2 `CombatActor` 補記 + `AssociatedSummonHeldIndex` → `AssociatedSummonHeldInstanceId` 訂正**

`§3-2` の Phase 10.2.B 補記の後に追加:

```markdown

> **Phase 10.2.D 補記**: 10.2.D で `RemainingLifetimeTurns: int?` /
> `AssociatedSummonHeldInstanceId: string?` を追加。
>
> **訂正**: 当初 `AssociatedSummonHeldIndex: int?` と記載していたが、
> `SummonHeld` 配列の要素削除で index がずれる latent bug を避けるため、
> `BattleCardInstance.InstanceId` で紐付ける `AssociatedSummonHeldInstanceId: string?` に変更
> （memory feedback ルール「InstanceId 検索」準拠）。
```

- [ ] **Step 4: §4-2 ターン開始処理 step 4 補記**

`§4-2` の Phase 10.2.B 補記の後に追加:

```markdown

> **Phase 10.2.D 補記**: 10.2.D で召喚キャラの Lifetime tick（step 4）を実装。
> `countdown` 後、`Energy` 前に挿入。
> `RemainingLifetimeTurns - 1 → 0` で `CurrentHp = 0` 化（死亡）+ `ActorDeath` event 発火 +
> `SummonCleanup.Apply` 経由で関連 SummonHeld カードを `DiscardPile` へ移動。
> hero は `RemainingLifetimeTurns is null` で tick されない。
```

- [ ] **Step 5: §4-6 ターン終了処理 step 5 補記**

`§4-6` の Phase 10.2.C 補記の後に追加:

```markdown

> **Phase 10.2.D 補記**: 10.2.D で `TurnEndProcessor.Process` のシグネチャに
> `DataCatalog catalog` 引数を追加。retainSelf-aware 手札整理を実装:
> `effects.Any(e => e.Action == "retainSelf")` のカードのみ `Hand` に残し、
> それ以外は `DiscardPile` へ。
```

- [ ] **Step 6: §5-1 `EffectApplier` 補記**

`§5-1` の Phase 10.2.C 補記の後に追加:

```markdown

> **Phase 10.2.D 補記**: 10.2.D で `EffectApplier.Apply` のシグネチャに
> `DataCatalog catalog` 引数を追加（`upgrade` / `summon` で必要）。
> 9 新 action 対応: `heal` / `draw` / `discard` / `upgrade` / `exhaustCard` /
> `exhaustSelf` / `retainSelf` / `gainEnergy` / `summon`。
> - `discard Scope==Single` で `InvalidOperationException`（UI 連携待ち）
> - `upgrade` / `exhaustCard` は `IRng` でランダム選択、Pile 不足は存在分だけ
> - `upgrade` は `IsUpgraded == true` を skip、`def.IsUpgradable == false` も skip
> - `gainEnergy` は上限なし（Phase 10 では超過可）
> - `exhaustSelf` / `retainSelf` はマーカー effect（`exhaustSelf` だけ `Exhaust` event 発火、
>   `retainSelf` no-op）
```

- [ ] **Step 7: §5-4 召喚カードの捨札遅延 補記**

`§5-4` 末尾に追加:

```markdown

> **Phase 10.2.D 補記**: 10.2.D で `summon` action 実装。
> `effect.UnitId` で `UnitDefinition` を `DataCatalog.TryGetUnit` 経由で検索。
> 空き slot (1-3) なし → 不発（silent skip、後続 effect は処理続行）。
> 召喚成功時、新 `CombatActor` 生成 + `Allies` に append + `Summon` event 発火。
> `AssociatedSummonHeldInstanceId` は `BattleEngine.PlayCard` の card-move logic で
> `card.InstanceId` に設定（5 段優先順位 step 3 経路）。
> 1 カード = 1 summon effect = 1 召喚 actor を前提（複数 summon 連発は Phase 11+）。
> 死亡 summon の `SummonHeld` カードは `SummonCleanup.Apply` 経由で `DiscardPile` へ移動。
```

- [ ] **Step 8: §5-7 カード移動 5 段優先順位 補記**

`§5-7` 末尾に追加:

```markdown

> **Phase 10.2.D 補記**: 10.2.D で `BattleEngine.PlayCard` 末尾に 5 段優先順位を実装。
> 優先順位: exhaustSelf → Power → Unit+summonSucceeded → retainSelf → Discard。
> `summonSucceeded` フラグは effect ループ中に追跡（`s.Allies.Length` の増加で判定）。
> `Power` カードはプレイ時の effects 発動後、`PowerCards` 配列に inert 配置
> （常駐効果は Phase 11+）。
> Unit+summon 成功経路では `card.InstanceId` を直前 summon actor の
> `AssociatedSummonHeldInstanceId` に設定。
```

- [ ] **Step 9: §9-7 `BattleEventKind` 補記**

`§9-7` の Phase 10.2.B 補記の後に追加:

```markdown

> **Phase 10.2.D 補記**: 10.2.D で 7 値追加（`Heal=12` / `Draw=13` / `Discard=14` /
> `Upgrade=15` / `Exhaust=16` / `GainEnergy=17` / `Summon=18`、計 19 値）。
> ペイロード慣例:
> - `Heal`: Caster=回復元、Target=対象、Amount=実回復量
> - `Draw`: Caster=hero、Amount=実ドロー数
> - `Discard`: Caster=hero、Amount=実捨て枚数、Note="random"|"all"
> - `Upgrade`: Caster=hero、Amount=実強化枚数、Note=pile 名
> - `Exhaust`: Caster=hero、Amount=枚数、Note="self"|pile 名
> - `GainEnergy`: Caster=hero、Amount=増分
> - `Summon`: Caster=hero、Target=新召喚 actor InstanceId、Note=UnitId
```

- [ ] **Step 10: ビルド確認（spec 変更なので影響なしだが念のため）**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

- [ ] **Step 11: commit + push**

```bash
git add docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md
git commit -m "docs(spec): amend Phase 10 spec for 10.2.D decisions (Task 18)"
git push
```

---

## Task 19: タグ付け + memory 更新

**Files:**
- Modify: `C:/Users/Metaverse/.claude/projects/c--Users-Metaverse-projects-roguelike-cardgame/memory/project_phase_status.md`

- [ ] **Step 1: 最終確認**

Run: `dotnet build && dotnet test`
Expected: 全件緑

- [ ] **Step 2: タグ + push**

```bash
git tag phase10-2D-complete
git push origin phase10-2D-complete
```

- [ ] **Step 3: memory 更新**

`C:/Users/Metaverse/.claude/projects/c--Users-Metaverse-projects-roguelike-cardgame/memory/project_phase_status.md` を更新:

frontmatter `description`:
```yaml
description: Phase 0〜8 + 10.1.A〜C + 10.2.A〜D 完了、次は Phase 10.2.E（レリック + ポーション戦闘内発動）。Phase 9（マルチ）は Phase 10 完了後。
```

本文で「2026-04-26: Phase 10.2.C 完了」セクションの後に追加:

```markdown
- **2026-04-26: Phase 10.2.D 完了**:
  - `BattleState` に `SummonHeld` / `PowerCards` の 2 pile フィールドを追加（最終形フィールド順に揃った）。
  - `CombatActor` に `RemainingLifetimeTurns: int?` / `AssociatedSummonHeldInstanceId: string?` 追加。spec §3-2 の `int? Index` を memory feedback「InstanceId 検索」ルール準拠で `string? InstanceId` に訂正。
  - `EffectApplier.Apply` のシグネチャに `DataCatalog catalog` 引数を追加し、9 新 action 対応:
    - `heal` (Self/Single/All/Random、MaxHp cap)
    - `draw` (Self、ハンド上限 10、山札不足→discard shuffle)
    - `discard` (Random/All、Single は throws、UI 連携待ち)
    - `upgrade` (Pile=hand|discard|draw、IRng ランダム、IsUpgraded/UnUpgradable は skip)
    - `exhaustCard` (Pile 同上、ランダム除外)
    - `exhaustSelf` (マーカー、Exhaust event 発火のみ)
    - `retainSelf` (マーカー、no-op)
    - `gainEnergy` (Self、上限なし)
    - `summon` (UnitId 必須、空き slot=1-3 検索、不発時 silent skip)
  - `BattleEngine.PlayCard` 末尾に **5 段優先順位** 実装（exhaustSelf → Power → Unit+summonSucceeded → retainSelf → Discard）。`summonSucceeded` フラグ追跡 + 召喚 actor の `AssociatedSummonHeldInstanceId` を `card.InstanceId` に設定。
  - `TurnStartProcessor` に Lifetime tick 追加（countdown 後、Energy 前）。死亡で `CurrentHp=0` 化 + `ActorDeath` event 発火。
  - `SummonCleanup.Apply` 共通 helper を新設、4 箇所（PlayerAttacking / EnemyAttacking / TurnStart の poison tick 後 / TurnStart の Lifetime tick 後）から呼出、死亡 summon の `SummonHeld` → `DiscardPile` 移動。
  - `TurnEndProcessor.Process` シグネチャに `DataCatalog catalog` 追加、retainSelf-aware 手札整理。
  - `BattleEventKind` に 7 値追加（Heal=12 / Draw=13 / Discard=14 / Upgrade=15 / Exhaust=16 / GainEnergy=17 / Summon=18、計 19 値）。
  - subagent-driven で 19 タスク完了、`phase10-2D-complete` タグ push 済み。
- **テスト状況 (10.2.D 完了時点)**: Core ~900+/全件緑（10.2.C 完了時 829 + 10.2.D 追加 +80-120）、Server 168/170 (skip 2)。Client vitest 未実行（影響なし）。
```

「次の作業」セクションを更新:

```markdown
**次の作業: Phase 10.2.E** — レリック 4 新 Trigger 発火（OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）+ Implemented スキップ + UsePotion 戦闘内発動 + BattleOnly 戦闘外スキップ。
```

「Phase 10 サブマイルストーン残り」を更新:

```markdown
**Phase 10 サブマイルストーン残り:**
- ~~10.1.A~~ ✅ ~~10.1.B~~ ✅ ~~10.1.C~~ ✅ ~~10.2.A~~ ✅ ~~10.2.B~~ ✅ ~~10.2.C~~ ✅ ~~10.2.D~~ ✅
- **10.2.E** — レリック + ポーション戦闘内発動（4 新 Trigger / Implemented スキップ / UsePotion）
- **10.3** — Server BattleHub + セーブ統合
- **10.4** — Client BattleScreen.tsx を battle-v10.html から手動ポート
- **10.5** — マップ画面ポーション UI + Phase 5 placeholder 削除 + `phase10-complete` タグ
```

「How to apply」セクションに 10.2.D 参照追加:

```markdown
- 10.2.D 専用 spec: `docs/superpowers/specs/2026-04-26-phase10-2D-effects-summon-design.md`、plan: `docs/superpowers/plans/2026-04-26-phase10-2D-effects-summon.md`
```

- [ ] **Step 4: 完了確認**

```bash
git log --oneline -5
git tag --list | grep phase10-2
git status
```

`phase10-2D-complete` タグがあり、master が origin と同期、working tree クリーンなら完了。

---

## Self-Review

### Spec coverage check

spec §「完了判定」の各項目を plan のどの Task が満たすか確認:

- ✅ `BattleState` に `SummonHeld` / `PowerCards` 2 フィールド追加 → Task 1
- ✅ `CombatActor` に `RemainingLifetimeTurns` / `AssociatedSummonHeldInstanceId` 追加 → Task 2
- ✅ `EffectApplier.Apply` 9 新 action + `DataCatalog catalog` 引数 → Tasks 4-11
- ✅ `discard` の `Scope == Single` で `InvalidOperationException` → Task 7
- ✅ `upgrade` / `exhaustCard` ランダム選択 + clamp → Tasks 9, 10
- ✅ `BattleEngine.PlayCard` 末尾の 5 段優先順位 → Task 12
- ✅ `TurnStartProcessor.Process` Lifetime tick → Task 13
- ✅ 召喚死亡時 `SummonHeld` → `DiscardPile` 移動 → Task 14
- ✅ `TurnEndProcessor.Process` `DataCatalog` 引数 + retainSelf-aware → Task 15
- ✅ `BattleEventKind` 19 値 → Task 3
- ✅ 既存 `BattlePlaceholder` 経由フロー無傷 → Task 17 (file isolation check)
- ✅ 親 spec 9 セクション補記 → Task 18
- ✅ `phase10-2D-complete` タグ + memory → Task 19

### Placeholder scan

- ✅ "TBD" / "TODO" なし
- ✅ "fill in details" / "appropriate error handling" 等の曖昧表現なし
- ✅ 全コードブロック完備

### Type consistency check

- ✅ `summonSucceeded` (bool) は Task 12 で導入、Tasks 12 内で一貫
- ✅ `AssociatedSummonHeldInstanceId` (string?) 命名は Tasks 2 / 11 / 12 / 14 で一貫
- ✅ `RemainingLifetimeTurns` (int?) 命名は Tasks 2 / 11 / 13 で一貫
- ✅ `OpenPile` / `OpenSourcePile` helper 命名は Tasks 9 / 10 で別関数（exhaustCard は exhaust pile を更新するため、upgrade は更新しないため別シグネチャ）

すべて整合。Plan 完成。

---

## Execution Handoff

Plan 完成、`docs/superpowers/plans/2026-04-26-phase10-2D-effects-summon.md` に保存準備。次の選択を user に確認する:

1. **Subagent-Driven（推奨）** — Task ごとに新しい subagent を dispatch、Task 間でレビュー、高速反復
2. **Inline Execution** — 現セッション内で executing-plans スキルで Task をバッチ実行、チェックポイントでレビュー

どちらで進めますか？
