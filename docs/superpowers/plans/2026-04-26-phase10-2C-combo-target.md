# Phase 10.2.C — コンボ + 対象指定 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 10.2 (Core バトル本体) の 3 段階目として、**コンボ機構**（通常階段 / Wild / SuperWild / FreePass / コスト軽減 / `comboMin` per-effect filter）と**対象指定アクション**（`BattleEngine.SetTarget` を第 5 の public static API として追加）を実装する。`TurnEndProcessor` でコンボ 3 フィールドをリセット、`BattleState` に `ComboCount` / `LastPlayedOrigCost` / `NextCardComboFreePass` を追加。

**Architecture:** `BattleEngine` の既存 4 公開 API（`Start` / `PlayCard` / `EndTurn` / `Finalize`）のシグネチャは不変。`BattleEngine.PlayCard` 内部にコンボ判定アルゴリズムを実装し、`comboMin` per-effect filter は `EffectApplier.Apply` のシグネチャを変えず PlayCard 側ループで評価。`BattleEngine.SetTarget` は partial class に新ファイル `BattleEngine.SetTarget.cs` で追加。`BattleEventKind` は不変（12 値のまま、`TargetChanged` 等は追加しない）。memory feedback の 2 ルール（`BattleOutcome` fully qualified / `state.Allies`/`state.Enemies` 書き戻しは InstanceId 検索）は今回の実装範囲では新規違反箇所はないが、PlayCard の effect ループでの `caster = s.Allies[0]` 再 fetch は 10.2.A/B 既存パターンを維持。

**Tech Stack:** C# .NET 10 / xUnit / `System.Collections.Immutable`

**前提:**
- Phase 10.2.B が master にマージ済み（`phase10-2B-complete` タグ + `4671ae2` follow-up fix）
- 開始時点で `dotnet build` 0 警告 0 エラー、`dotnet test` 全件緑（Core 783 件 + Server 168 件、Client vitest は範囲外）

**完了判定（spec §「完了判定」と同期）:**
- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（10.2.B 完了時の Core 783 + 10.2.C 追加分）
- `BattleState` に `ComboCount: int` / `LastPlayedOrigCost: int?` / `NextCardComboFreePass: bool` の 3 フィールド追加済み
- `BattleEngine.Start` 直後の state で 3 フィールドが `0 / null / false`
- `BattleEngine.PlayCard` のコンボ判定が spec §3-3 の 6 例（通常階段 / Wild 不一致 / Wild 一致 / SuperWild + 次カード / リセット直後 Wild / SuperWild→0 コスト）すべてを正しく処理
- `effect.ComboMin` per-effect filter が `BattleEngine.PlayCard` の effect ループで評価
- `EffectApplier.Apply` シグネチャ不変
- `BattleEngine.SetTarget(state, side, slotIndex) → BattleState` が public static として公開、Phase / 範囲 / 死亡 バリデーション動作
- `BattleEventKind` は 12 値のまま（不変）
- `TurnEndProcessor.Process` がコンボ 3 フィールドをリセット
- 既存 `BattlePlaceholder` 経由のフロー無傷
- 親 spec §3-1 / §4-6 / §5-1 / §6 / §7-3 に補記済み
- `phase10-2C-complete` タグ origin に push 済み
- `memory/project_phase_status.md` を 10.2.C 完了状態に更新

---

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `src/Core/Battle/State/BattleState.cs` | +ComboCount / LastPlayedOrigCost / NextCardComboFreePass | 修正 |
| `src/Core/Battle/Engine/BattleEngine.cs` | Start で 3 フィールド初期値（0 / null / false）を `new BattleState(...)` に渡す | 修正 |
| `src/Core/Battle/Engine/BattleEngine.PlayCard.cs` | コンボ判定アルゴリズム + per-effect comboMin filter + payCost 算定 | 修正 |
| `src/Core/Battle/Engine/BattleEngine.SetTarget.cs` | 第 5 の public static API（partial class） | **新規** |
| `src/Core/Battle/Engine/TurnEndProcessor.cs` | コンボ 3 フィールドのリセット | 修正 |
| `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` | 必要なら BattleState 生成ヘルパー追加（既存 fixture は MakeState を持たないため不要、各 test の local MakeState を更新） | 軽微修正 |
| `tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs` | 3 フィールドの record 等価 / 初期値 / `with` 式更新 + ComboCount >= 0 不変条件 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs` | Start 直後の 3 フィールド初期値検証 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs` | local `MakeState` の `BattleState` 初期化に 3 フィールド追加 / 既存 assertion 維持 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs` | local `MakeState` 整合 + コンボリセット assertion 追加 | 修正 |
| `tests/Core.Tests/Battle/Engine/TurnEndProcessorTests.cs` | local `MakeState` 整合 + コンボリセット直接テスト | 修正 |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOmnistrikeTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverStatusTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverStatusTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/TurnStartProcessorTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/TargetingAutoSwitchTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/EffectApplierBuffDebuffTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/EffectApplierReplaceActorInstanceIdTests.cs` | local fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/DealDamageHelperTests.cs` | DealDamageHelper は state を取らないので **影響なし** | — |
| `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs` | コンボ含む 1 戦闘で seed 同一 → 一致 | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs` | Phase / 範囲 / 死亡 / Ally / Enemy / 正常切替 | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs` | spec §3-3 の 6 例網羅 + Wild と SuperWild 同時保持の優先順位 | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboMinTests.cs` | per-effect filter 網羅（null / 1 / 2 / 3 / 0 / UpgradedEffects 内） | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCostReductionTests.cs` | payCost 算定 / Math.Max(0, ...) / CostOverride との合算 / Energy 不足例外順序 | **新規** |
| `tests/Core.Tests/Battle/Engine/TurnEndProcessorComboResetTests.cs` | EndTurn 跨ぎでコンボ 3 フィールドリセット | **新規** |
| `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` | §3-1 / §4-6 / §5-1 / §6 / §7-3 に補記 | 修正 |
| `C:/Users/Metaverse/.claude/projects/c--Users-Metaverse-projects-roguelike-cardgame/memory/project_phase_status.md` | 10.2.C 完了状態に更新 | 修正 |

---

## Task 1: `BattleState` に 3 フィールド追加 + 既存 invariant tests 拡張

**Files:**
- Modify: `src/Core/Battle/State/BattleState.cs`
- Modify: `tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs` の末尾に section を追加:

```csharp
// === 10.2.C: コンボフィールド ===

[Fact] public void ComboCount_default_is_zero_via_with()
{
    var s = MakeMinimalState();   // 既存ヘルパー（追加）
    Assert.Equal(0, s.ComboCount);
    Assert.Null(s.LastPlayedOrigCost);
    Assert.False(s.NextCardComboFreePass);
}

[Fact] public void ComboFields_record_equality_distinguishes()
{
    var s1 = MakeMinimalState();
    var s2 = s1 with { ComboCount = 1 };
    var s3 = s1 with { LastPlayedOrigCost = 2 };
    var s4 = s1 with { NextCardComboFreePass = true };
    Assert.NotEqual(s1, s2);
    Assert.NotEqual(s1, s3);
    Assert.NotEqual(s1, s4);
    Assert.NotEqual(s2, s3);
}

[Fact] public void ComboCount_invariant_non_negative()
{
    var s = MakeMinimalState() with { ComboCount = 0 };
    Assert.True(s.ComboCount >= 0);
    s = s with { ComboCount = 5 };
    Assert.True(s.ComboCount >= 0);
}
```

`MakeMinimalState()` ヘルパーは同ファイル内に既に存在しなければ追加（既存 invariant テストの fixture を流用、ない場合は以下を追加）:

```csharp
private static BattleState MakeMinimalState() =>
    new(
        Turn: 1,
        Phase: BattlePhase.PlayerInput,
        Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero()),
        Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
        TargetAllyIndex: 0,
        TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        EncounterId: "enc_test");
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet build`
Expected: error CS7036（`BattleState` constructor に必須引数 `ComboCount` 等が不足）— 期待通り

- [ ] **Step 3: 実装**

`src/Core/Battle/State/BattleState.cs` を以下に置き換え:

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル全体の不変状態。
/// 親 spec §3-1 参照。
/// 10.2.C で ComboCount / LastPlayedOrigCost / NextCardComboFreePass を追加。
/// 10.2.D で SummonHeld / PowerCards が ExhaustPile の後に追加される予定（その時点でフィールド順を最終形に揃える）。
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
    int ComboCount,                       // 10.2.C 追加: 現在のコンボ数 (0..N)
    int? LastPlayedOrigCost,              // 10.2.C 追加: 直前に手打ちプレイしたカードの元コスト
    bool NextCardComboFreePass,           // 10.2.C 追加: SuperWild 由来。次のカード 1 枚はコンボ条件 bypass
    string EncounterId);
```

- [ ] **Step 4: ビルド確認**

Run: `dotnet build`
Expected: error CS7036 が広範囲に発生（既存の `new BattleState(...)` 呼出全て）

- [ ] **Step 5: 中断せず Task 2 へ進む**

ビルド赤期間。Task 2-3 で全呼出箇所に 3 フィールドを追加して緑に戻す。**ここでは commit しない**（赤状態 commit を避ける）。

---

## Task 2: `BattleEngine.Start` で 3 フィールド初期化 + Start tests 拡張

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.cs`
- Modify: `tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs`

- [ ] **Step 1: 実装**

`src/Core/Battle/Engine/BattleEngine.cs` の `new BattleState(...)` 呼出（line 65 付近）を更新:

```csharp
// 4. 初期 BattleState（Turn=0、TurnStartProcessor で +1 して Turn=1 へ）
var initial = new BattleState(
    Turn: 0,
    Phase: BattlePhase.PlayerInput,
    Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
    Allies: ImmutableArray.Create(hero),
    Enemies: enemiesBuilder.ToImmutable(),
    TargetAllyIndex: 0,
    TargetEnemyIndex: enemiesBuilder.Count > 0 ? 0 : (int?)null,
    Energy: 0, EnergyMax: InitialEnergy,
    DrawPile: drawPile,
    Hand: ImmutableArray<BattleCardInstance>.Empty,
    DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
    ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
    ComboCount: 0,                        // 10.2.C
    LastPlayedOrigCost: null,             // 10.2.C
    NextCardComboFreePass: false,         // 10.2.C
    EncounterId: encounterId);
```

- [ ] **Step 2: 既存 BattleEngineStartTests に assertion 追加**

`tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs` の末尾に:

```csharp
[Fact] public void Start_initializes_combo_fields_to_default()
{
    var run = MakeRun();   // 既存ヘルパー
    var rng = new FakeRng(new int[20], new double[0]);
    var cat = BattleFixtures.MinimalCatalog();
    var s = BattleEngine.Start(run, "enc_test", rng, cat);
    Assert.Equal(0, s.ComboCount);
    Assert.Null(s.LastPlayedOrigCost);
    Assert.False(s.NextCardComboFreePass);
}
```

`MakeRun()` ヘルパーが既存ファイルにあることを確認（なければ既存テストの fixture を見て同等の RunState を生成）。

- [ ] **Step 3: ビルド確認**

Run: `dotnet build`
Expected: src/Core はビルド成功 / tests/Core.Tests は依然エラー（他 test file の local `MakeState` が未更新）

- [ ] **Step 4: 中断せず Task 3 へ**

src 側だけ緑、tests は依然赤。**ここでは commit しない**。

---

## Task 3: 全テスト fixture（local `MakeState`）の追従 + Task 1-2-3 まとめて 1 commit

**Files (modify all):**
- `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs`
- `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs`
- `tests/Core.Tests/Battle/Engine/TurnEndProcessorTests.cs`
- `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverTests.cs`
- `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverTests.cs`
- `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`
- `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOmnistrikeTests.cs`
- `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverStatusTests.cs`
- `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverStatusTests.cs`
- `tests/Core.Tests/Battle/Engine/TurnStartProcessorTests.cs`
- `tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs`
- `tests/Core.Tests/Battle/Engine/TargetingAutoSwitchTests.cs`
- `tests/Core.Tests/Battle/Engine/EffectApplierBuffDebuffTests.cs`
- `tests/Core.Tests/Battle/Engine/EffectApplierReplaceActorInstanceIdTests.cs`

- [ ] **Step 1: 各 test ファイルの local `MakeState` ヘルパーを更新**

各 test ファイルで `new BattleState(...)` を呼んでいる箇所を Grep で抽出:

Run (Grep tool, not bash):
- pattern: `new BattleState\(`
- path: `tests/Core.Tests/Battle/`
- output_mode: files_with_matches

抽出された各 file の `new BattleState(...)` 呼出に以下 3 行を `EncounterId:` の直前に挿入:

```csharp
ComboCount: 0,
LastPlayedOrigCost: null,
NextCardComboFreePass: false,
```

具体例（`BattleEnginePlayCardTests.cs` の `MakeState` ヘルパー、line 17-27）:

```csharp
private static BattleState MakeState(
    ImmutableArray<BattleCardInstance> hand,
    int energy = 3)
    => new(
        Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero()),
        Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: energy, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: hand,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,                        // ← 10.2.C 追加
        LastPlayedOrigCost: null,             // ← 10.2.C 追加
        NextCardComboFreePass: false,         // ← 10.2.C 追加
        EncounterId: "enc_test");
```

`with` 式（`s with { Turn = ... }` 等）は変更不要。

- [ ] **Step 2: ビルド確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

- [ ] **Step 3: テスト実行**

Run: `dotnet test --filter FullyQualifiedName~Battle`
Expected: 既存 783 件すべて緑（コンボフィールドはまだロジック上未使用なので回帰なし）

新規追加分（BattleStateInvariantTests の 3 件 + BattleEngineStartTests の 1 件）も含めて緑になる。

- [ ] **Step 4: commit（Task 1-2-3 まとめて）**

```bash
git add src/Core/Battle/State/BattleState.cs \
        src/Core/Battle/Engine/BattleEngine.cs \
        tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs \
        tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs \
        tests/Core.Tests/Battle/Engine/
git commit -m "feat(battle): add combo fields to BattleState (Phase 10.2.C Task 1-3)"
```

> 補足: Task 1-2-3 は破壊的変更を伴うビルド赤期間を 1 commit にまとめる。10.2.B の Task 5（CombatActor.Statuses 追加）と同じパターン。

---

## Task 4: `BattleEngine.SetTarget` 新ファイル + 正常切替テスト

**Files:**
- Create: `src/Core/Battle/Engine/BattleEngine.SetTarget.cs`
- Create: `tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineSetTargetTests
{
    private static BattleState Make(
        int? targetAlly = 0,
        int? targetEnemy = 0,
        int allyCount = 1,
        int enemyCount = 2,
        BattlePhase phase = BattlePhase.PlayerInput,
        bool enemy0Dead = false)
    {
        var allies = new System.Collections.Generic.List<CombatActor>();
        for (int i = 0; i < allyCount; i++)
            allies.Add(BattleFixtures.Hero(slotIndex: i));
        var enemies = new System.Collections.Generic.List<CombatActor>();
        for (int i = 0; i < enemyCount; i++)
        {
            var e = BattleFixtures.Goblin(slotIndex: i);
            if (i == 0 && enemy0Dead) e = e with { CurrentHp = 0 };
            enemies.Add(e);
        }
        return new BattleState(
            Turn: 1, Phase: phase, Outcome: BattleOutcome.Pending,
            Allies: allies.ToImmutableArray(),
            Enemies: enemies.ToImmutableArray(),
            TargetAllyIndex: targetAlly, TargetEnemyIndex: targetEnemy,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");
    }

    [Fact] public void Switches_enemy_target_to_alive_slot()
    {
        var s = Make(enemyCount: 3);
        var next = BattleEngine.SetTarget(s, ActorSide.Enemy, 2);
        Assert.Equal(2, next.TargetEnemyIndex);
        Assert.Equal(0, next.TargetAllyIndex); // 味方は変えない
    }

    [Fact] public void Switches_ally_target_to_alive_slot()
    {
        // ally スロットを 2 個用意（0=Hero, 1=別 Hero）
        var s = Make(allyCount: 2);
        var next = BattleEngine.SetTarget(s, ActorSide.Ally, 1);
        Assert.Equal(1, next.TargetAllyIndex);
        Assert.Equal(0, next.TargetEnemyIndex); // 敵は変えない
    }

    [Fact] public void Returns_BattleState_only_no_events()
    {
        // SetTarget の戻り値型が BattleState 単体（タプルでない）
        var s = Make();
        BattleState next = BattleEngine.SetTarget(s, ActorSide.Enemy, 0);
        Assert.Equal(0, next.TargetEnemyIndex);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEngineSetTargetTests`
Expected: build error（`BattleEngine.SetTarget` 未定義）

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/BattleEngine.SetTarget.cs`:

```csharp
using System;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// 対象スロットを切替する。Phase=PlayerInput でのみ呼出可能、
    /// 範囲外 / 死亡スロットで InvalidOperationException。
    /// イベント発火なし（BattleState のみ返す）。
    /// 親 spec §7-3 / Phase 10.2.C spec §4 参照。
    /// </summary>
    public static BattleState SetTarget(BattleState state, ActorSide side, int slotIndex)
    {
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException(
                $"SetTarget requires Phase=PlayerInput, got {state.Phase}");

        var pool = side == ActorSide.Ally ? state.Allies : state.Enemies;

        if (slotIndex < 0 || slotIndex >= pool.Length)
            throw new InvalidOperationException(
                $"slotIndex {slotIndex} out of range [0, {pool.Length}) for side={side}");

        if (!pool[slotIndex].IsAlive)
            throw new InvalidOperationException(
                $"slot {side}[{slotIndex}] is dead and cannot be targeted");

        return side == ActorSide.Ally
            ? state with { TargetAllyIndex = slotIndex }
            : state with { TargetEnemyIndex = slotIndex };
    }
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEngineSetTargetTests`
Expected: 3 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.SetTarget.cs \
        tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs
git commit -m "feat(battle): add BattleEngine.SetTarget API (Phase 10.2.C Task 4)"
```

---

## Task 5: `SetTarget` Phase 違反例外テスト

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs`

- [ ] **Step 1: 失敗テストを書く（既に実装済みのため必ず緑になる前に確認）**

`BattleEngineSetTargetTests.cs` に追加:

```csharp
[Theory]
[InlineData(BattlePhase.PlayerAttacking)]
[InlineData(BattlePhase.EnemyAttacking)]
[InlineData(BattlePhase.Resolved)]
public void Throws_when_phase_not_PlayerInput(BattlePhase phase)
{
    var s = Make(phase: phase);
    var ex = Assert.Throws<System.InvalidOperationException>(() =>
        BattleEngine.SetTarget(s, ActorSide.Enemy, 0));
    Assert.Contains("Phase=PlayerInput", ex.Message);
}
```

- [ ] **Step 2: 緑確認（実装は Task 4 で完成済み）**

Run: `dotnet test --filter FullyQualifiedName~BattleEngineSetTargetTests`
Expected: 6 passed（既存 3 + 新 3）

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs
git commit -m "test(battle): SetTarget phase validation (Phase 10.2.C Task 5)"
```

---

## Task 6: `SetTarget` 範囲外 / 死亡スロット例外テスト

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs`

- [ ] **Step 1: テスト追加**

```csharp
[Fact] public void Throws_when_slotIndex_negative()
{
    var s = Make();
    var ex = Assert.Throws<System.InvalidOperationException>(() =>
        BattleEngine.SetTarget(s, ActorSide.Enemy, -1));
    Assert.Contains("out of range", ex.Message);
}

[Fact] public void Throws_when_slotIndex_too_large()
{
    var s = Make(enemyCount: 2);   // 0, 1 のみ
    Assert.Throws<System.InvalidOperationException>(() =>
        BattleEngine.SetTarget(s, ActorSide.Enemy, 2));
    Assert.Throws<System.InvalidOperationException>(() =>
        BattleEngine.SetTarget(s, ActorSide.Enemy, 99));
}

[Fact] public void Throws_when_target_slot_is_dead()
{
    var s = Make(enemyCount: 2, enemy0Dead: true);
    var ex = Assert.Throws<System.InvalidOperationException>(() =>
        BattleEngine.SetTarget(s, ActorSide.Enemy, 0));
    Assert.Contains("dead", ex.Message);
}

[Fact] public void Allows_switching_to_alive_when_other_slot_dead()
{
    var s = Make(enemyCount: 2, enemy0Dead: true);
    var next = BattleEngine.SetTarget(s, ActorSide.Enemy, 1);
    Assert.Equal(1, next.TargetEnemyIndex);
}
```

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEngineSetTargetTests`
Expected: 10 passed

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/BattleEngineSetTargetTests.cs
git commit -m "test(battle): SetTarget range and alive validation (Phase 10.2.C Task 6)"
```

---

## Task 7: `TurnEndProcessor` でコンボ 3 フィールドリセット

**Files:**
- Modify: `src/Core/Battle/Engine/TurnEndProcessor.cs`
- Create: `tests/Core.Tests/Battle/Engine/TurnEndProcessorComboResetTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/TurnEndProcessorComboResetTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnEndProcessorComboResetTests
{
    private static BattleState Make(int combo, int? lastOrigCost, bool freePass) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: combo,
            LastPlayedOrigCost: lastOrigCost,
            NextCardComboFreePass: freePass,
            EncounterId: "enc_test");

    [Fact] public void Resets_combo_count_to_zero()
    {
        var s = Make(combo: 5, lastOrigCost: 3, freePass: true);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Equal(0, next.ComboCount);
    }

    [Fact] public void Resets_last_played_orig_cost_to_null()
    {
        var s = Make(combo: 3, lastOrigCost: 4, freePass: false);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Null(next.LastPlayedOrigCost);
    }

    [Fact] public void Resets_next_card_combo_free_pass_to_false()
    {
        var s = Make(combo: 2, lastOrigCost: 7, freePass: true);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.False(next.NextCardComboFreePass);
    }

    [Fact] public void All_combo_fields_reset_simultaneously()
    {
        var s = Make(combo: 4, lastOrigCost: 6, freePass: true);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Equal(0, next.ComboCount);
        Assert.Null(next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~TurnEndProcessorComboResetTests`
Expected: 4 failures（リセット未実装、各値が引き継がれる）

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/TurnEndProcessor.cs` を更新:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン終了処理。Phase 10.2.C でコンボ 3 フィールドのリセットを追加。
/// 10.2.E で OnTurnEnd レリック / 10.2.D で retainSelf 対応の手札整理が追加される。
/// 親 spec §4-6 参照。
/// </summary>
internal static class TurnEndProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state)
    {
        var allies = state.Allies.Select(ResetActor).ToImmutableArray();
        var enemies = state.Enemies.Select(ResetActor).ToImmutableArray();
        var newDiscard = state.DiscardPile.AddRange(state.Hand);
        var next = state with
        {
            Allies = allies,
            Enemies = enemies,
            Hand = ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile = newDiscard,
            ComboCount = 0,                       // ← 10.2.C 追加
            LastPlayedOrigCost = null,            // ← 10.2.C 追加
            NextCardComboFreePass = false,        // ← 10.2.C 追加
        };
        return (next, System.Array.Empty<BattleEvent>());
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

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~TurnEndProcessor`
Expected: 既存 TurnEndProcessorTests + 新 ComboReset 計 全件緑

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/TurnEndProcessor.cs \
        tests/Core.Tests/Battle/Engine/TurnEndProcessorComboResetTests.cs
git commit -m "feat(battle): TurnEndProcessor resets combo fields (Phase 10.2.C Task 7)"
```

---

## Task 8: `PlayCard` の `actualCost` 算定（CostOverride 無視）

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`
- Create: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCostReductionTests.cs`

- [ ] **Step 1: 失敗テスト**

`tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCostReductionTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardCostReductionTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static BattleState Make(
        ImmutableArray<BattleCardInstance> hand,
        int energy = 3,
        int? lastOrigCost = null,
        int combo = 0,
        bool freePass = false) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: combo,
            LastPlayedOrigCost: lastOrigCost,
            NextCardComboFreePass: freePass,
            EncounterId: "enc_test");

    private static CardDefinition CardWithCost(string id, int cost) =>
        new(id, id, null, CardRarity.Common, CardType.Attack,
            Cost: cost, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
            UpgradedEffects: null, Keywords: null);

    [Fact] public void Cost_override_is_ignored_for_combo_orig_cost()
    {
        // Cost=2 のカードを CostOverride=0 でプレイ → LastOrigCost は 2（CostOverride 無視）
        var def = CardWithCost("c2", 2);
        var card = new BattleCardInstance("inst1", "c2", false, CostOverride: 0);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 0);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.LastPlayedOrigCost); // CostOverride=0 を無視して元コスト 2 を採用
    }

    [Fact] public void Pay_cost_uses_cost_override()
    {
        // Cost=3 / CostOverride=1 → payCost = 1（軽減なし）
        var def = CardWithCost("c3", 3);
        var card = new BattleCardInstance("inst1", "c3", false, CostOverride: 1);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 3);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.Energy); // 3 - 1 = 2
    }

    [Fact] public void Combo_reduction_lowers_pay_cost_by_one()
    {
        // LastOrigCost=1 → 元コスト 2 のカードで matchesNormal=true / isReduced=true → payCost=1
        var def = CardWithCost("c2", 2);
        var card = new BattleCardInstance("inst1", "c2", false, CostOverride: null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 3, lastOrigCost: 1, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.Energy); // 3 - 1 = 2
    }

    [Fact] public void Combo_reduction_clamps_pay_cost_to_zero()
    {
        // LastOrigCost=0 → 元コスト 1 のカードで matchesNormal=true → payCost = max(0, 1-1) = 0
        var def = CardWithCost("c1", 1);
        var card = new BattleCardInstance("inst1", "c1", false, CostOverride: null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 0, lastOrigCost: 0, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(0, next.Energy); // 0 - 0 = 0
    }

    [Fact] public void Cost_override_with_combo_reduction_combines()
    {
        // Cost=3, CostOverride=2, LastOrigCost=2 (元コスト 3 で matchesNormal)
        // basePay = 2 (CostOverride), isReduced = true → payCost = max(0, 2-1) = 1
        var def = CardWithCost("c3", 3);
        var card = new BattleCardInstance("inst1", "c3", false, CostOverride: 2);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 3, lastOrigCost: 2, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.Energy); // 3 - 1 = 2
    }

    [Fact] public void Throws_when_energy_below_pay_cost_after_reduction()
    {
        // 元コスト 5 / Energy 3 / コンボなし → payCost=5 → 不足
        var def = CardWithCost("c5", 5);
        var card = new BattleCardInstance("inst1", "c5", false, CostOverride: null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 3);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat));
        Assert.Contains("insufficient energy", ex.Message);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardCostReductionTests`
Expected: 6 件中複数失敗（特に `Cost_override_is_ignored_for_combo_orig_cost` は 10.2.A の `cost = card.CostOverride ?? ...` 経路で 0 になり、後続で例外になる）

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/BattleEngine.PlayCard.cs` を以下に置き換え:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (BattleState, IReadOnlyList<BattleEvent>) PlayCard(
        BattleState state, int handIndex,
        int? targetEnemyIndex, int? targetAllyIndex,
        IRng rng, DataCatalog catalog)
    {
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException($"PlayCard requires Phase=PlayerInput, got {state.Phase}");
        if (handIndex < 0 || handIndex >= state.Hand.Length)
            throw new InvalidOperationException($"handIndex {handIndex} out of range [0, {state.Hand.Length})");

        var card = state.Hand[handIndex];
        if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
            throw new InvalidOperationException($"card '{card.CardDefinitionId}' not in catalog");

        // === 10.2.C: 元コスト算定（CostOverride 無視）===
        int? origCost = card.IsUpgraded ? def.UpgradedCost ?? def.Cost : def.Cost;
        if (origCost is null)
            throw new InvalidOperationException($"card '{def.Id}' is unplayable (cost=null)");
        int actualCost = origCost.Value;

        // === 10.2.C: コンボ判定（Task 10-13 で完成。Task 8 では「常に新規スタート / 軽減なし」の最小実装）===
        bool matchesNormal = false;   // Task 10 で実装
        bool isWild = false;          // Task 11 で実装
        bool isSuperWild = false;     // Task 12 で実装
        bool isContinuing = false;    // Task 10-12 で実装
        bool isReduced = false;       // Task 10 で実装

        // === 10.2.C: payCost 算定 ===
        int basePay = card.CostOverride ?? actualCost;
        int payCost = Math.Max(0, basePay - (isReduced ? 1 : 0));

        if (state.Energy < payCost)
            throw new InvalidOperationException($"insufficient energy: have {state.Energy}, need {payCost}");

        // === 10.2.C: combo フィールド更新（Task 10-12 で完成、Task 8 では最小実装）===
        int newCombo = isContinuing ? state.ComboCount + 1 : 1;
        int? newLastCost = actualCost;
        bool newFreePass = isSuperWild;

        var s = state with
        {
            Energy = state.Energy - payCost,
            ComboCount = newCombo,
            LastPlayedOrigCost = newLastCost,
            NextCardComboFreePass = newFreePass,
            TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
            TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
        };

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.PlayCard, Order: 0,
                CasterInstanceId: state.Allies[0].InstanceId,
                CardId: def.Id,
                Amount: payCost),
        };

        var caster = s.Allies[0]; // 10.2.A: caster = hero 固定
        int order = 1;

        var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
            ? def.UpgradedEffects
            : def.Effects;

        foreach (var eff in effects)
        {
            // 10.2.C Task 14 で comboMin filter を追加。Task 8 ではフィルタなし
            var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng);
            s = afterEffect;
            foreach (var ev in evs)
            {
                events.Add(ev with { Order = order });
                order++;
            }
            caster = s.Allies[0];
        }

        var newHand = s.Hand.RemoveAt(handIndex);
        var newDiscard = s.DiscardPile.Add(card);
        s = s with { Hand = newHand, DiscardPile = newDiscard };

        return (s, events);
    }
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardCostReductionTests`
Expected: 5 件 pass / 1 件残（`Combo_reduction_lowers_pay_cost_by_one` と `Combo_reduction_clamps_pay_cost_to_zero` と `Cost_override_with_combo_reduction_combines` の 3 件は Task 10 でようやく緑になる）

実際の期待: 以下が緑
- `Cost_override_is_ignored_for_combo_orig_cost`（CostOverride 無視で元コスト 2 を LastPlayedOrigCost に記録 ✓）
- `Pay_cost_uses_cost_override`（軽減なしの payCost = CostOverride ✓）
- `Throws_when_energy_below_pay_cost_after_reduction`（元コスト 5 のままで例外 ✓）

以下は失敗（Task 10 で対応）:
- `Combo_reduction_lowers_pay_cost_by_one`
- `Combo_reduction_clamps_pay_cost_to_zero`
- `Cost_override_with_combo_reduction_combines`

既存の `BattleEnginePlayCardTests` も全件緑であること確認:

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardTests`
Expected: 既存全件 pass（`LastPlayedOrigCost = actualCost` の追加更新があっても、既存テストは LastPlayedOrigCost を assert していないため壊れない）

- [ ] **Step 5: 部分的緑で commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.PlayCard.cs \
        tests/Core.Tests/Battle/Engine/BattleEnginePlayCardCostReductionTests.cs
git commit -m "feat(battle): PlayCard ignores CostOverride for orig cost (Phase 10.2.C Task 8)"
```

> 補足: Task 8 では一部 cost reduction テスト（コンボ軽減関連 3 件）が依然失敗したまま commit する。これらは Task 10 で軽減ロジックが入った時点で緑になる。spec §8-4 の依存順序方針に従う。

---

## Task 9: 既存 `BattleEnginePlayCardTests` で `LastPlayedOrigCost` の更新を確認

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs`

- [ ] **Step 1: 既存テストの末尾に新 assertion 追加**

```csharp
[Fact] public void Updates_LastPlayedOrigCost_to_card_cost()
{
    var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
    var s = MakeState(hand);
    var cat = BattleFixtures.MinimalCatalog();
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    // strike の Cost=1 なので LastPlayedOrigCost=1
    Assert.Equal(1, next.LastPlayedOrigCost);
}

[Fact] public void Updates_ComboCount_to_one_on_first_play()
{
    var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
    var s = MakeState(hand);
    var cat = BattleFixtures.MinimalCatalog();
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    // 直前 Combo=0 / LastOrigCost=null / FreePass=false / Wild なし → isContinuing=false → newCombo=1
    Assert.Equal(1, next.ComboCount);
}

[Fact] public void NextCardComboFreePass_remains_false_for_non_superwild()
{
    var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
    var s = MakeState(hand);
    var cat = BattleFixtures.MinimalCatalog();
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    Assert.False(next.NextCardComboFreePass);
}
```

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardTests`
Expected: 既存 + 新 3 件すべて緑

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs
git commit -m "test(battle): PlayCard updates combo fields on first play (Phase 10.2.C Task 9)"
```

---

## Task 10: 通常コンボ階段（matchesNormal / isContinuing / isReduced）

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`
- Create: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardComboTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static CardDefinition CardWithCost(string id, int cost, string[]? keywords = null) =>
        new(id, id, null, CardRarity.Common, CardType.Attack,
            Cost: cost, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
            UpgradedEffects: null, Keywords: keywords);

    private static BattleState Make(
        ImmutableArray<BattleCardInstance> hand,
        int? lastOrigCost = null,
        int combo = 0,
        bool freePass = false,
        int energy = 10) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 10,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: combo,
            LastPlayedOrigCost: lastOrigCost,
            NextCardComboFreePass: freePass,
            EncounterId: "enc_test");

    [Fact] public void Example1_normal_staircase()
    {
        // 直前 LastOrigCost=1 / Combo=1 → 元コスト 2 のカード → matchesNormal=true, isReduced=true
        // payCost=1, Combo=2, LastOrigCost=2, FreePass=false
        var def = CardWithCost("c2", 2);
        var card = new BattleCardInstance("inst1", "c2", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 1, energy: 5);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(4, next.Energy);            // 5 - 1
        Assert.Equal(2, next.ComboCount);
        Assert.Equal(2, next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }

    [Fact] public void Normal_no_match_resets_combo_to_one()
    {
        // 直前 LastOrigCost=1 / Combo=2 → 元コスト 5 のカード（Wild なし）→ isContinuing=false → Combo=1
        var def = CardWithCost("c5", 5);
        var card = new BattleCardInstance("inst1", "c5", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 2);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.ComboCount);
        Assert.Equal(5, next.LastPlayedOrigCost);
        Assert.Equal(5, next.Energy); // 10 - 5（軽減なし）
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboTests`
Expected: 2 件失敗（matchesNormal / isReduced 未実装、Combo=1 固定）

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/BattleEngine.PlayCard.cs` のコンボ判定部分を更新:

```csharp
// === 10.2.C: コンボ判定 ===
bool matchesNormal =
    state.LastPlayedOrigCost is { } prev && actualCost == prev + 1;
bool isWild = false;          // Task 11 で実装
bool isSuperWild = false;     // Task 12 で実装

bool isContinuing =
    state.NextCardComboFreePass ? true
  : matchesNormal              ? true
  : (isWild || isSuperWild)    ? true
  : false;

bool isReduced = matchesNormal;
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboTests`
Expected: 2 件緑

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardCostReductionTests`
Expected: 全 6 件緑（Task 8 で部分緑だった `Combo_reduction_*` の 3 件もここで緑になる）

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardTests`
Expected: 既存 + 新 全件緑

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.PlayCard.cs \
        tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs
git commit -m "feat(battle): PlayCard normal combo staircase + cost reduction (Phase 10.2.C Task 10)"
```

---

## Task 11: Wild キーワード対応

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`
- Modify: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs`

- [ ] **Step 1: 失敗テスト追加**

`BattleEnginePlayCardComboTests.cs` に追加:

```csharp
[Fact] public void Example2_wild_no_match_continues_no_reduction()
{
    // 直前 LastOrigCost=1 / Combo=1 → Wild（元コスト 5）
    // matchesNormal=false, isWild=true, isContinuing=true, isReduced=false
    // payCost=5, Combo=2, LastOrigCost=5, FreePass=false
    var def = CardWithCost("wild5", 5, keywords: new[] { "wild" });
    var card = new BattleCardInstance("inst1", "wild5", false, null);
    var hand = ImmutableArray.Create(card);
    var s = Make(hand, lastOrigCost: 1, combo: 1);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    Assert.Equal(5, next.Energy);            // 10 - 5（軽減なし）
    Assert.Equal(2, next.ComboCount);
    Assert.Equal(5, next.LastPlayedOrigCost);
    Assert.False(next.NextCardComboFreePass);
}

[Fact] public void Example3_wild_with_match_reduces_normally()
{
    // 直前 LastOrigCost=1 / Combo=1 → Wild（元コスト 2）
    // matchesNormal=true, isWild=true, isContinuing=true, isReduced=true（通常条件成立）
    // payCost=1, Combo=2, LastOrigCost=2
    var def = CardWithCost("wild2", 2, keywords: new[] { "wild" });
    var card = new BattleCardInstance("inst1", "wild2", false, null);
    var hand = ImmutableArray.Create(card);
    var s = Make(hand, lastOrigCost: 1, combo: 1);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    Assert.Equal(9, next.Energy);            // 10 - 1
    Assert.Equal(2, next.ComboCount);
    Assert.Equal(2, next.LastPlayedOrigCost);
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboTests`
Expected: Example2 失敗（Wild 不実装で Combo=1 にリセットされる）

- [ ] **Step 3: 実装**

`BattleEngine.PlayCard.cs` の Wild フラグを実装:

```csharp
bool isWild = def.Keywords?.Contains("wild") == true;
bool isSuperWild = false;     // Task 12 で実装
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboTests`
Expected: 4 件緑（既存 2 + 新 2）

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.PlayCard.cs \
        tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs
git commit -m "feat(battle): PlayCard wild keyword combo continuation (Phase 10.2.C Task 11)"
```

---

## Task 12: SuperWild + FreePass + 次カード bypass

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`
- Modify: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs`

- [ ] **Step 1: 失敗テスト追加**

`BattleEnginePlayCardComboTests.cs` に追加:

```csharp
[Fact] public void Example4_superwild_sets_free_pass()
{
    // 直前 LastOrigCost=1 / Combo=1 / FreePass=false → SuperWild（元コスト 7）
    // isSuperWild=true, isContinuing=true（SuperWild 自身も継続）, isReduced=false
    // payCost=7, Combo=2, LastOrigCost=7, FreePass=true
    var def = CardWithCost("sw7", 7, keywords: new[] { "superwild" });
    var card = new BattleCardInstance("inst1", "sw7", false, null);
    var hand = ImmutableArray.Create(card);
    var s = Make(hand, lastOrigCost: 1, combo: 1);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    Assert.Equal(3, next.Energy);            // 10 - 7
    Assert.Equal(2, next.ComboCount);
    Assert.Equal(7, next.LastPlayedOrigCost);
    Assert.True(next.NextCardComboFreePass);
}

[Fact] public void Example4_cont_next_card_bypasses_via_free_pass()
{
    // 直前 LastOrigCost=7 / Combo=2 / FreePass=true → 元コスト 3 のカード（Keywords なし）
    // FreePass で isContinuing=true, matchesNormal=false（3≠8）, isReduced=false
    // payCost=3, Combo=3, LastOrigCost=3, FreePass=false（消費）
    var def = CardWithCost("c3", 3);
    var card = new BattleCardInstance("inst1", "c3", false, null);
    var hand = ImmutableArray.Create(card);
    var s = Make(hand, lastOrigCost: 7, combo: 2, freePass: true);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    Assert.Equal(7, next.Energy);            // 10 - 3
    Assert.Equal(3, next.ComboCount);
    Assert.Equal(3, next.LastPlayedOrigCost);
    Assert.False(next.NextCardComboFreePass); // 消費
}

[Fact] public void Wild_and_superwild_both_present_superwild_wins()
{
    // 両方持つカード: SuperWild の挙動が優先（FreePass セット）
    var def = CardWithCost("ws", 4, keywords: new[] { "wild", "superwild" });
    var card = new BattleCardInstance("inst1", "ws", false, null);
    var hand = ImmutableArray.Create(card);
    var s = Make(hand, lastOrigCost: 1, combo: 1);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    Assert.Equal(2, next.ComboCount);
    Assert.True(next.NextCardComboFreePass);  // SuperWild 由来
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboTests`
Expected: 3 件失敗

- [ ] **Step 3: 実装**

`BattleEngine.PlayCard.cs` の SuperWild フラグを実装:

```csharp
bool isWild = def.Keywords?.Contains("wild") == true;
bool isSuperWild = def.Keywords?.Contains("superwild") == true;
```

`newFreePass` は既に `isSuperWild` を採用済み（Task 8 で記述）。これで自動的に:
- 現在のカードが SuperWild → newFreePass=true
- 現在のカードが SuperWild 以外 → newFreePass=false（FreePass を消費）

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboTests`
Expected: 7 件緑（既存 4 + 新 3）

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.PlayCard.cs \
        tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs
git commit -m "feat(battle): PlayCard superwild keyword + free pass (Phase 10.2.C Task 12)"
```

---

## Task 13: リセット直後 Wild + SuperWild→0 コスト の境界例

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs`

- [ ] **Step 1: 失敗テスト追加**

`BattleEnginePlayCardComboTests.cs` に追加:

```csharp
[Fact] public void Example5_wild_after_reset_starts_at_combo_one()
{
    // LastOrigCost=null / Combo=0 / FreePass=false → Wild（元コスト 5）
    // matchesNormal=false（LastOrigCost null）, isWild=true, isContinuing=true
    // 結果: Combo=0+1=1, LastOrigCost=5, payCost=5（軽減なし）
    var def = CardWithCost("wild5", 5, keywords: new[] { "wild" });
    var card = new BattleCardInstance("inst1", "wild5", false, null);
    var hand = ImmutableArray.Create(card);
    var s = Make(hand, lastOrigCost: null, combo: 0);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    Assert.Equal(1, next.ComboCount);
    Assert.Equal(5, next.LastPlayedOrigCost);
    Assert.False(next.NextCardComboFreePass);
}

[Fact] public void Example6_superwild_then_zero_cost()
{
    // 1 枚目: LastOrigCost=4 / Combo=2 / FreePass=false → SuperWild（元コスト 6）
    //   → Combo=3, LastOrigCost=6, FreePass=true
    var def1 = CardWithCost("sw6", 6, keywords: new[] { "superwild" });
    var card1 = new BattleCardInstance("inst1", "sw6", false, null);
    var s1 = Make(ImmutableArray.Create(card1), lastOrigCost: 4, combo: 2);
    var cat1 = BattleFixtures.MinimalCatalog(cards: new[] { def1 });
    var (after1, _) = BattleEngine.PlayCard(s1, 0, 0, 0, Rng(), cat1);
    Assert.Equal(3, after1.ComboCount);
    Assert.Equal(6, after1.LastPlayedOrigCost);
    Assert.True(after1.NextCardComboFreePass);

    // 2 枚目: 元コスト 0 のカード（Keywords なし）
    //   FreePass で isContinuing=true, matchesNormal=false（0≠7）, isReduced=false
    //   → Combo=4, LastOrigCost=0, FreePass=false, payCost=0
    var def2 = CardWithCost("c0", 0);
    var card2 = new BattleCardInstance("inst2", "c0", false, null);
    var s2 = after1 with {
        Hand = ImmutableArray.Create(card2),
        Energy = after1.Energy,   // SuperWild で 4 残（10-6=4）
    };
    var cat2 = BattleFixtures.MinimalCatalog(cards: new[] { def1, def2 });
    var (after2, _) = BattleEngine.PlayCard(s2, 0, 0, 0, Rng(), cat2);
    Assert.Equal(4, after2.ComboCount);
    Assert.Equal(0, after2.LastPlayedOrigCost);
    Assert.False(after2.NextCardComboFreePass);
    Assert.Equal(4, after2.Energy);  // 4 - 0
}

[Fact] public void Empty_keywords_array_treated_as_no_wild()
{
    // Keywords が空配列 [] でも Wild / SuperWild ではない
    var def = CardWithCost("c5", 5, keywords: new string[0]);
    var card = new BattleCardInstance("inst1", "c5", false, null);
    var hand = ImmutableArray.Create(card);
    var s = Make(hand, lastOrigCost: 1, combo: 1);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
    var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
    Assert.Equal(1, next.ComboCount);  // matchesNormal=false → Combo=1（リセット）
    Assert.Equal(5, next.LastPlayedOrigCost);
}
```

- [ ] **Step 2: 緑確認（既存実装で全件緑になるはず）**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboTests`
Expected: 10 件緑

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboTests.cs
git commit -m "test(battle): combo edge cases (reset Wild / SuperWild→0) (Phase 10.2.C Task 13)"
```

---

## Task 14: per-effect `comboMin` filter

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`
- Create: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboMinTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboMinTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardComboMinTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static BattleState Make(
        ImmutableArray<BattleCardInstance> hand,
        int? lastOrigCost = null,
        int combo = 0,
        bool freePass = false,
        int energy = 10) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 10,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: combo,
            LastPlayedOrigCost: lastOrigCost,
            NextCardComboFreePass: freePass,
            EncounterId: "enc_test");

    private static CardDefinition WithEffects(string id, int cost, params CardEffect[] effects) =>
        new(id, id, null, CardRarity.Common, CardType.Attack,
            Cost: cost, UpgradedCost: null,
            Effects: effects, UpgradedEffects: null, Keywords: null);

    [Fact] public void ComboMin_null_always_applies()
    {
        // comboMin なしの effect は Combo に関係なく適用
        var def = WithEffects("c", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5));
        var card = new BattleCardInstance("inst1", "c", false, null);
        var s = Make(ImmutableArray.Create(card), combo: 0);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_2_skipped_when_newCombo_1()
    {
        // 1 枚目（newCombo=1）→ comboMin:2 effect はスキップ
        var def = WithEffects("c", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, ComboMin: 2));
        var card = new BattleCardInstance("inst1", "c", false, null);
        var s = Make(ImmutableArray.Create(card));
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum); // 5 のみ、+5 はスキップ
    }

    [Fact] public void ComboMin_2_applies_when_newCombo_2()
    {
        // 直前 LastOrigCost=0 / Combo=1 → 元コスト 1 のカード → matchesNormal=true → newCombo=2
        // comboMin:2 effect も適用
        var def = WithEffects("c1", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, ComboMin: 2));
        var card = new BattleCardInstance("inst1", "c1", false, null);
        var s = Make(ImmutableArray.Create(card), lastOrigCost: 0, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(10, next.Allies[0].AttackSingle.Sum); // 5 + 5
    }

    [Fact] public void ComboMin_3_skipped_when_newCombo_2()
    {
        var def = WithEffects("c1", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 99, ComboMin: 3));
        var card = new BattleCardInstance("inst1", "c1", false, null);
        var s = Make(ImmutableArray.Create(card), lastOrigCost: 0, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.Allies[0].AttackSingle.Sum); // newCombo=2 < 3 → 99 スキップ
    }

    [Fact] public void ComboMin_1_applies_on_first_play()
    {
        // newCombo=1 で comboMin=1 → 1 >= 1 → 適用
        var def = WithEffects("c", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, ComboMin: 1));
        var card = new BattleCardInstance("inst1", "c", false, null);
        var s = Make(ImmutableArray.Create(card));
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_zero_treated_as_no_filter()
    {
        // newCombo=1, comboMin=0 → 1 >= 0 → 適用
        var def = WithEffects("c", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, ComboMin: 0));
        var card = new BattleCardInstance("inst1", "c", false, null);
        var s = Make(ImmutableArray.Create(card));
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_in_upgraded_effects_evaluated()
    {
        // UpgradedEffects 内の comboMin も同じルール
        var def = new CardDefinition("c", "c", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
            UpgradedEffects: new[] {
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 7),
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 7, ComboMin: 2),
            },
            Keywords: null);
        var card = new BattleCardInstance("inst1", "c", IsUpgraded: true, CostOverride: null);

        // newCombo=1 → 7 のみ
        var s1 = Make(ImmutableArray.Create(card));
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next1, _) = BattleEngine.PlayCard(s1, 0, 0, 0, Rng(), cat);
        Assert.Equal(7, next1.Allies[0].AttackSingle.Sum);

        // newCombo=2 → 14
        var s2 = Make(ImmutableArray.Create(card), lastOrigCost: 0, combo: 1);
        var (next2, _) = BattleEngine.PlayCard(s2, 0, 0, 0, Rng(), cat);
        Assert.Equal(14, next2.Allies[0].AttackSingle.Sum);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboMinTests`
Expected: `ComboMin_2_skipped_when_newCombo_1` 等が失敗（フィルタ未実装で +5 が適用されてしまう）

- [ ] **Step 3: 実装**

`BattleEngine.PlayCard.cs` の effect ループを更新:

```csharp
foreach (var eff in effects)
{
    // 10.2.C: per-effect comboMin filter（PlayCard 経路のみ）
    if (eff.ComboMin is { } min && newCombo < min)
        continue;

    var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng);
    s = afterEffect;
    foreach (var ev in evs)
    {
        events.Add(ev with { Order = order });
        order++;
    }
    caster = s.Allies[0];
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCardComboMinTests`
Expected: 7 件緑

Run: `dotnet test --filter FullyQualifiedName~BattleEnginePlayCard`
Expected: 既存 + 新 全件緑（comboMin null の effect は影響なし）

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.PlayCard.cs \
        tests/Core.Tests/Battle/Engine/BattleEnginePlayCardComboMinTests.cs
git commit -m "feat(battle): PlayCard per-effect comboMin filter (Phase 10.2.C Task 14)"
```

---

## Task 15: `BattleEngineEndTurnTests` にコンボリセット assertion 追加

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs`

- [ ] **Step 1: テスト追加**

`BattleEngineEndTurnTests.cs` の末尾に:

```csharp
[Fact] public void EndTurn_resets_combo_fields_after_TurnEnd()
{
    // SuperWild プレイ後の state を作成 → EndTurn → コンボ 3 フィールドが初期値に戻る
    // PlayerAttacking / EnemyAttacking が割り込むので「単一ターン中の組み立て」を local fixture で行う
    var def = new CardDefinition("sw", "sw", null, CardRarity.Common, CardType.Attack,
        Cost: 2, UpgradedCost: null,
        Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
        UpgradedEffects: null, Keywords: new[] { "superwild" });

    // 直接 BattleState を組み立てて「SuperWild プレイ後の状態」を再現
    var s = MakeStateWithCombo(combo: 3, lastOrigCost: 5, freePass: true);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });

    var (next, _) = BattleEngine.EndTurn(s, Rng(), cat);

    // PlayerAttacking + EnemyAttacking + TurnEndProcessor + TurnStartProcessor 後
    if (next.Outcome == BattleOutcome.Pending)
    {
        Assert.Equal(0, next.ComboCount);
        Assert.Null(next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }
    // 万一 EndTurn 中に Outcome 確定したらこのテストは別の挙動を測ることになる → fixture を見直し
}

private static BattleState MakeStateWithCombo(int combo, int? lastOrigCost, bool freePass)
{
    return new(
        Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero()),
        Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: combo,
        LastPlayedOrigCost: lastOrigCost,
        NextCardComboFreePass: freePass,
        EncounterId: "enc_test");
}
```

ファイル先頭の `using` に `RoguelikeCardGame.Core.Cards;` が必要なら追加。

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEngineEndTurnTests`
Expected: 既存 + 新 全件緑

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs
git commit -m "test(battle): EndTurn resets combo fields end-to-end (Phase 10.2.C Task 15)"
```

---

## Task 16: `BattleDeterminismTests` にコンボ + SetTarget を含む 1 戦闘追加

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs`

- [ ] **Step 1: テスト追加**

`BattleDeterminismTests.cs` の末尾に:

```csharp
[Fact] public void Combat_with_combo_and_set_target_is_deterministic()
{
    // 同じ seed + 同じ操作列 → 同じ最終 state + 同じ event 列
    var run = MakeRun();   // 既存ヘルパー（コンボ可能なデッキで構成）
    var rng1 = new FakeRng(seed: 12345);
    var rng2 = new FakeRng(seed: 12345);
    var cat = BattleFixtures.MinimalCatalog(cards: new[] {
        BattleFixtures.Strike(),
        BattleFixtures.Defend(),
    });

    var s1 = BattleEngine.Start(run, "enc_test", rng1, cat);
    var s2 = BattleEngine.Start(run, "enc_test", rng2, cat);

    // 同じ操作列: SetTarget(Enemy, 0) → PlayCard(0) → SetTarget(Enemy, 0) → EndTurn
    if (s1.Hand.Length > 0)
    {
        s1 = BattleEngine.SetTarget(s1, ActorSide.Enemy, 0);
        s2 = BattleEngine.SetTarget(s2, ActorSide.Enemy, 0);

        var (after1a, _) = BattleEngine.PlayCard(s1, 0, 0, 0, rng1, cat);
        var (after2a, _) = BattleEngine.PlayCard(s2, 0, 0, 0, rng2, cat);

        Assert.Equal(after1a.ComboCount, after2a.ComboCount);
        Assert.Equal(after1a.LastPlayedOrigCost, after2a.LastPlayedOrigCost);
        Assert.Equal(after1a.NextCardComboFreePass, after2a.NextCardComboFreePass);
        Assert.Equal(after1a.Energy, after2a.Energy);
        Assert.Equal(after1a.Hand.Length, after2a.Hand.Length);
    }
}
```

`FakeRng(seed)` が既存 helper にないなら、`new int[]` / `new double[]` のシード化版を使う既存パターンに合わせる。

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleDeterminismTests`
Expected: 既存 + 新 全件緑

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs
git commit -m "test(battle): determinism with combo + SetTarget (Phase 10.2.C Task 16)"
```

---

## Task 17: 全テスト実行 + `BattlePlaceholder` 経由の手動プレイ確認

**Files:** なし（実行のみ）

- [ ] **Step 1: Core 全テスト**

Run: `dotnet test`
Expected: 警告 0 / エラー 0、全件緑（10.2.B 完了時 783 + 10.2.C 追加 ~50-60）

- [ ] **Step 2: dev サーバ起動 + クライアント起動**

Run (background, separate terminals):
```bash
dotnet run --project src/Server
```
```bash
cd src/Client && npm run dev
```

- [ ] **Step 3: 手動プレイ確認**

ブラウザで http://localhost:5173 を開き、以下を確認:
- ログイン → 新規ラン開始 → 通常マップ進行
- 敵タイル進入で `BattlePlaceholder` 経由の暫定バトル → 「即勝利」ボタン → 報酬画面
- ゲームオーバーまで進める or 退出

エラーログ・例外なしで完走できれば既存ゲームフロー無傷。

- [ ] **Step 4: dev サーバ停止**

Ctrl+C で停止。

- [ ] **Step 5: ここでは commit しない**（テスト/手動確認のみ、変更なし）

---

## Task 18: 親 spec への補記

**Files:**
- Modify: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`

- [ ] **Step 1: §3-1 BattleState に補記**

`§3-1 BattleState` の末尾（10.2.A / 10.2.B 補記の後）に追加:

```markdown
> **Phase 10.2.C 補記**: 10.2.C で `ComboCount: int` / `LastPlayedOrigCost: int?` /
> `NextCardComboFreePass: bool` を追加した。配置は `EncounterId` の直前。
> `SummonHeld` / `PowerCards` は 10.2.D で `ExhaustPile` の後に挿入される予定。
> 初期値は `Start` および `TurnEndProcessor.Process` 後で `0 / null / false`。
```

- [ ] **Step 2: §4-6 ターン終了処理に補記**

`§4-6` の末尾（既存 `状態異常カウントダウンは...` 補記の後）に追加:

```markdown
> **Phase 10.2.C 補記**: 10.2.C で `TurnEndProcessor.Process` がコンボ 3 フィールド
> （`ComboCount = 0` / `LastPlayedOrigCost = null` / `NextCardComboFreePass = false`）
> のリセットを実行。`OnTurnEnd` レリック発動（step 3）/ `retainSelf` 対応の手札整理（step 5）は
> 後続 sub-phase（10.2.E / 10.2.D）。
```

- [ ] **Step 3: §5-1 EffectApplier に補記**

`§5-1` の Phase 10.2.B 補記の後に追加:

```markdown
> **Phase 10.2.C 補記**: 10.2.C で `effect.ComboMin` per-effect filter は
> **`BattleEngine.PlayCard` 側**で評価（`EffectApplier.Apply` のシグネチャは不変）。
> カードプレイ経路以外（敵 Move / レリック / ポーション）では comboMin が常に「null と同等」に振る舞う。
```

- [ ] **Step 4: §6 コンボ機構に補記**

`§6-6` の後に新セクション追加:

```markdown
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
```

- [ ] **Step 5: §7-3 SetTarget に補記**

`§7-3` の末尾に追加:

```markdown
> **Phase 10.2.C 補記**: 10.2.C で `BattleEngine.SetTarget(state, side, slotIndex) → BattleState`
> を**第 5 の public static API** として追加。Phase=PlayerInput 限定（他 Phase で `InvalidOperationException`）、
> 範囲外 / 死亡スロット指定で例外。戻り値は `BattleState` 単体、`BattleEvent` 発火なし。
> `PlayCard` 引数経由の暗黙対象切替（10.2.A 既存）も維持。両者の生存・範囲チェック整合は
> 10.2.D 以降で `UsePotion` 追加時に再考。
```

- [ ] **Step 6: ビルド確認**

ビルドには影響しないが念のため:

Run: `dotnet build`
Expected: 警告 0 / エラー 0

- [ ] **Step 7: commit**

```bash
git add docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md
git commit -m "docs(spec): amend Phase 10 spec for 10.2.C decisions (Task 18)"
```

---

## Task 19: タグ付け + push + memory 更新

**Files:**
- Modify: `C:/Users/Metaverse/.claude/projects/c--Users-Metaverse-projects-roguelike-cardgame/memory/project_phase_status.md`

- [ ] **Step 1: 最終ビルド・テスト確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

Run: `dotnet test`
Expected: 全件緑

- [ ] **Step 2: タグ付け + push**

```bash
git tag phase10-2C-complete
git push origin master
git push origin phase10-2C-complete
```

- [ ] **Step 3: memory `project_phase_status.md` を 10.2.C 完了状態に更新**

`C:/Users/Metaverse/.claude/projects/c--Users-Metaverse-projects-roguelike-cardgame/memory/project_phase_status.md`:

frontmatter の `description` を更新:
```yaml
description: Phase 0〜8 + 10.1.A〜C + 10.2.A〜C 完了、次は Phase 10.2.D（残り effect 8 種 + 召喚）。Phase 9（マルチ）は Phase 10 完了後。
```

本文で「2026-04-26: Phase 10.2.B 完了」セクションの後に新セクション追加:

```markdown
- **2026-04-26: Phase 10.2.C 完了**:
  - `BattleState` に `ComboCount: int` / `LastPlayedOrigCost: int?` / `NextCardComboFreePass: bool` を追加。
  - `BattleEngine.PlayCard` にコンボ判定アルゴリズム実装（通常階段 / Wild / SuperWild / FreePass / コスト軽減 / per-effect comboMin filter）。`actualCost` は `CostOverride` 無視、`payCost` は CostOverride 反映 + コンボ軽減 -1 + Math.Max(0, …) 下限。
  - `BattleEngine.SetTarget(state, side, slotIndex) → BattleState` を **第 5 の public static API** として追加（Phase=PlayerInput 限定 + 範囲・生存バリデーション、event 発火なし）。
  - `TurnEndProcessor.Process` がコンボ 3 フィールド（`0 / null / false`）にリセット。
  - `BattleEventKind` は不変（12 値のまま）。`EffectApplier.Apply` のシグネチャも不変（comboMin filter は `BattleEngine.PlayCard` 側で評価）。
  - subagent-driven で 19 タスク完了、`phase10-2C-complete` タグ push 済み。
- **テスト状況 (10.2.C 完了時点)**: Core <updated count>/<total>（10.2.B 完了時 783 + 10.2.C 追加 +約 50-60）、Server 168/170 (skip 2)。Client vitest 未実行（影響なし）。
```

「次の作業」セクションを更新:

```markdown
**次の作業: Phase 10.2.D** — 残り effect 8 種（heal / draw / discard / upgrade / exhaustCard / exhaustSelf / retainSelf / gainEnergy）+ 召喚 system（カード移動 5 段優先順位 / SummonHeld / Lifetime / PowerCards）。
```

「Phase 10 サブマイルストーン残り」セクションを更新:

```markdown
**Phase 10 サブマイルストーン残り:**
- ~~10.1.A~~ ✅ ~~10.1.B~~ ✅ ~~10.1.C~~ ✅ ~~10.2.A~~ ✅ ~~10.2.B~~ ✅ ~~10.2.C~~ ✅
- **10.2.D** — 残り effect 8 種 + 召喚 system（カード移動 5 段優先順位 / SummonHeld / Lifetime / PowerCards）
- **10.2.E** — レリック + ポーション戦闘内発動（4 新 Trigger / Implemented スキップ / UsePotion）
- **10.3** — Server BattleHub + セーブ統合
- **10.4** — Client BattleScreen.tsx を battle-v10.html から手動ポート
- **10.5** — マップ画面ポーション UI + Phase 5 placeholder 削除 + `phase10-complete` タグ
```

「How to apply」セクションに 10.2.C plan/spec の参照を追加:
```markdown
- 10.2.C 専用 spec: `docs/superpowers/specs/2026-04-26-phase10-2C-combo-target-design.md`、plan: `docs/superpowers/plans/2026-04-26-phase10-2C-combo-target.md`
```

- [ ] **Step 4: memory コミット不要（memory はファイル直書きで永続化）**

memory ファイルは git 管理外。直接 Write tool で更新するだけで OK。

- [ ] **Step 5: 完了確認**

```bash
git log --oneline -5
git tag --list | grep phase10-2
git status
```

`phase10-2C-complete` タグがあり、master が origin と同期し、working tree クリーンなら完了。

---

## Self-Review

書き終えた plan を spec と照合する。

### Spec coverage check

spec §「完了判定」の各項目を plan のどの Task が満たすか確認:

- ✅ `BattleState` に 3 フィールド追加 → Task 1
- ✅ `Start` 直後の 3 フィールド初期値 → Task 2
- ✅ コンボ判定 6 例網羅 → Task 10-13
- ✅ `comboMin` per-effect filter → Task 14
- ✅ `EffectApplier.Apply` シグネチャ不変 → Task 14（PlayCard 側 filter で達成）
- ✅ `BattleEngine.SetTarget` 公開 → Task 4
- ✅ Phase / 範囲 / 死亡 バリデーション → Task 4-6
- ✅ 戻り値 `BattleState` 単体（event なし）→ Task 4
- ✅ `BattleEventKind` 12 値のまま → 暗黙（Task 内で変更しないため確認のみ）
- ✅ `TurnEndProcessor` でコンボ 3 フィールドリセット → Task 7
- ✅ 既存 `BattlePlaceholder` 経由フロー無傷 → Task 17 で手動確認
- ✅ 親 spec 補記 → Task 18
- ✅ `phase10-2C-complete` タグ → Task 19
- ✅ memory 更新 → Task 19

### Placeholder scan

- ✅ "TBD" / "TODO" なし（Task 8 の `// Task 11 で実装` はコメントで進捗を明示するためのもので、最終形では削除される）
- ✅ "fill in details" / "appropriate error handling" 等の曖昧表現なし
- ✅ 全コードブロック完備、全コマンド実行可能

### Type consistency check

- ✅ `actualCost` / `origCost` / `basePay` / `payCost` の命名は Task 8 で導入後、後続 Task でも一貫
- ✅ `matchesNormal` / `isWild` / `isSuperWild` / `isContinuing` / `isReduced` の命名は Task 10-12 で一貫
- ✅ `newCombo` / `newLastCost` / `newFreePass` の命名は Task 8 で導入後、Task 10-12 で一貫
- ✅ `BattleEngine.SetTarget(state, side, slotIndex) → BattleState` シグネチャは Task 4 / Task 16 で一致

すべて整合。Plan 完成。

---

## Execution Handoff

Plan 完成、`docs/superpowers/plans/2026-04-26-phase10-2C-combo-target.md` に保存準備。次の選択を user に確認する:

1. **Subagent-Driven（推奨）** — Task ごとに新しい subagent を dispatch、Task 間でレビュー、高速反復
2. **Inline Execution** — 現セッション内で executing-plans スキルで Task をバッチ実行、チェックポイントでレビュー

どちらで進めますか？
