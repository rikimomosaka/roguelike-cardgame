# Phase 10.2.A — Core バトル基盤スケルトン Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 10.2 (Core バトル本体) の最小単位 walking-skeleton を建てる。新 `BattleState` データモデル、`BattleEngine` 4 公開 API（`Start` / `PlayCard` / `EndTurn` / `Finalize`）、`attack` / `block` の 2 effect、Phase 進行、Victory / Defeat Outcome、`BattleEvent` 発火基盤を導入する。

**Architecture:** 旧 `BattleState` を `BattlePlaceholderState` にリネームして名前衝突を回避し、新 `BattleState` を `Core.Battle.State` namespace 配下に新設。`BattleEngine` は静的ファサード + internal static helper（`EffectApplier` / `*Resolver` / `*Processor`）の組み合わせ。`NodeEffectResolver` への wire-up は Phase 10.3 で行うため、本フェーズでは `BattleEngine` は xUnit テストでしか使われない pure Core API。

**Tech Stack:** C# .NET 10 / xUnit / `System.Collections.Immutable` / `System.Text.Json`

**前提:**
- Phase 10.1.C が master にマージ済みであること（`phase10-1C-complete` タグ）
- 開始時点で `dotnet build` 0 警告 0 エラー、`dotnet test` 全件緑

**完了判定（spec §「完了判定」と同期）:**
- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑
- `BattleEngine.Start` / `PlayCard` / `EndTurn` / `Finalize` の 4 公開 API が動作
- `attack` / `block` の 2 effect で Victory / Defeat 両方の戦闘が xUnit で完走
- 既存 `BattlePlaceholder` 経由のフローは無傷（手動プレイテストで確認）
- 旧 `BattleState` → `BattlePlaceholderState` リネーム完了
- 親 spec の該当章補記済み
- ブランチに `phase10-2A-complete` タグを切り origin に push

---

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `src/Core/Battle/BattleState.cs` | 旧 `BattleState` (placeholder) | **rename → `BattlePlaceholderState.cs`、型名変更** |
| `src/Core/Battle/BattlePlaceholderState.cs` | リネーム先（旧フィールド構造を維持） | **rename + 型名変更** |
| `src/Core/Battle/BattlePlaceholder.cs` | 内部の型参照を更新 | 修正 |
| `src/Core/Battle/State/BattlePhase.cs` | enum 4 値 | **新規** |
| `src/Core/Battle/State/BattleOutcome.cs` | enum 3 値 | **新規** |
| `src/Core/Battle/State/ActorSide.cs` | enum 2 値 | **新規** |
| `src/Core/Battle/State/AttackPool.cs` | readonly record struct | **新規** |
| `src/Core/Battle/State/BlockPool.cs` | readonly record struct | **新規** |
| `src/Core/Battle/State/CombatActor.cs` | record | **新規** |
| `src/Core/Battle/State/BattleCardInstance.cs` | record | **新規** |
| `src/Core/Battle/State/BattleState.cs` | 新 BattleState record | **新規** |
| `src/Core/Battle/Events/BattleEventKind.cs` | enum 9 値 | **新規** |
| `src/Core/Battle/Events/BattleEvent.cs` | record | **新規** |
| `src/Core/Battle/Engine/BattleEngine.cs` | public static partial class、ファサード | **新規** |
| `src/Core/Battle/Engine/BattleSummary.cs` | record | **新規** |
| `src/Core/Battle/Engine/EffectApplier.cs` | internal static | **新規** |
| `src/Core/Battle/Engine/DealDamageHelper.cs` | internal static、共有 DealDamage | **新規** |
| `src/Core/Battle/Engine/PlayerAttackingResolver.cs` | internal static | **新規** |
| `src/Core/Battle/Engine/EnemyAttackingResolver.cs` | internal static | **新規** |
| `src/Core/Battle/Engine/TurnStartProcessor.cs` | internal static | **新規** |
| `src/Core/Battle/Engine/TurnEndProcessor.cs` | internal static | **新規** |
| `src/Core/Battle/Engine/TargetingAutoSwitch.cs` | internal static | **新規** |
| `src/Core/Battle/Statuses/.gitkeep` | 空フォルダ受け皿 (10.2.B 用) | **新規** |
| `src/Core/Run/RunState.cs` | `ActiveBattle` の型を `BattlePlaceholderState?` に | 修正 |
| `tests/Core.Tests/Battle/BattlePlaceholderTests.cs` | 型参照更新 | 修正 |
| `tests/Core.Tests/Battle/BattlePlaceholderBestiaryTests.cs` | 型参照更新（必要なら） | 修正 |
| `tests/Core.Tests/Run/RunStateSerializerTests.cs` | 型参照更新 | 修正 |
| `tests/Core.Tests/Battle/State/BattlePhaseTests.cs` | enum 値検証 | **新規** |
| `tests/Core.Tests/Battle/State/BattleOutcomeTests.cs` | enum 値検証 | **新規** |
| `tests/Core.Tests/Battle/State/ActorSideTests.cs` | enum 値検証 | **新規** |
| `tests/Core.Tests/Battle/State/AttackPoolTests.cs` | 加算/Empty/RawTotal | **新規** |
| `tests/Core.Tests/Battle/State/BlockPoolTests.cs` | 加算/Consume/Empty | **新規** |
| `tests/Core.Tests/Battle/State/CombatActorTests.cs` | record 等価/IsAlive | **新規** |
| `tests/Core.Tests/Battle/State/BattleCardInstanceTests.cs` | record 等価 | **新規** |
| `tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs` | 不変条件 8 項目 | **新規** |
| `tests/Core.Tests/Battle/Events/BattleEventKindTests.cs` | enum 値検証 | **新規** |
| `tests/Core.Tests/Battle/Events/BattleEventEmissionTests.cs` | record 等価 + nullable | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs` | attack / block 各 scope | **新規** |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverTests.cs` | Single/Random/All / Block 吸収 / 死亡 | **新規** |
| `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverTests.cs` | per-effect 即時発射 / NextMoveId | **新規** |
| `tests/Core.Tests/Battle/Engine/TurnStartProcessorTests.cs` | Turn+1 / Energy 全回復 / 5 ドロー | **新規** |
| `tests/Core.Tests/Battle/Engine/TurnEndProcessorTests.cs` | Block / AttackPool リセット / 手札全捨て | **新規** |
| `tests/Core.Tests/Battle/Engine/TargetingAutoSwitchTests.cs` | 死亡時自動切替 | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs` | hero/敵生成、Deck シャッフル、初期対象 | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs` | エナジー支払い / Pool 加算 / 捨札移動 | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs` | フェーズ進行 / Victory / Defeat | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEngineFinalizeTests.cs` | HP 戻し / Reward 引き金 / GameOver | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs` | 同 seed 同 input 同 result | **新規** |
| `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` | Hero / Goblin / Strike / Defend / MinimalCatalog | **新規** |
| `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` | 第 3-1 / 3-3 / 3-4 / 4-7 / 5-2 / 9-7 章補記（spec §8 の 6 項目） | 修正 |

---

## Task 1: BattlePhase enum + テスト

**Files:**
- Create: `src/Core/Battle/State/BattlePhase.cs`
- Create: `tests/Core.Tests/Battle/State/BattlePhaseTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/State/BattlePhaseTests.cs` を新規作成:

```csharp
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattlePhaseTests
{
    [Fact] public void PlayerInput_value_is_zero() => Assert.Equal(0, (int)BattlePhase.PlayerInput);
    [Fact] public void PlayerAttacking_value_is_one() => Assert.Equal(1, (int)BattlePhase.PlayerAttacking);
    [Fact] public void EnemyAttacking_value_is_two() => Assert.Equal(2, (int)BattlePhase.EnemyAttacking);
    [Fact] public void Resolved_value_is_three() => Assert.Equal(3, (int)BattlePhase.Resolved);
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattlePhaseTests`
Expected: build error（型未定義）

- [ ] **Step 3: 実装**

`src/Core/Battle/State/BattlePhase.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>バトルの大局フェーズ。親 spec §3-1 / §4-1 参照。</summary>
public enum BattlePhase
{
    PlayerInput     = 0,
    PlayerAttacking = 1,
    EnemyAttacking  = 2,
    Resolved        = 3,
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattlePhaseTests`
Expected: 4 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/BattlePhase.cs tests/Core.Tests/Battle/State/BattlePhaseTests.cs
git commit -m "feat(battle): add BattlePhase enum (Phase 10.2.A Task 1)"
```

---

## Task 2: BattleOutcome enum (Defeat 追加版) + テスト

**Files:**
- Create: `src/Core/Battle/State/BattleOutcome.cs`
- Create: `tests/Core.Tests/Battle/State/BattleOutcomeTests.cs`

> **注**: 旧 `Core.Battle.BattleOutcome`（`Pending = 0, Victory = 1`）は Task 12 のリネームまで残す。新 enum は `Core.Battle.State.BattleOutcome`（namespace で完全分離）。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/State/BattleOutcomeTests.cs`:

```csharp
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattleOutcomeTests
{
    [Fact] public void Pending_value_is_zero() => Assert.Equal(0, (int)BattleOutcome.Pending);
    [Fact] public void Victory_value_is_one() => Assert.Equal(1, (int)BattleOutcome.Victory);
    [Fact] public void Defeat_value_is_two() => Assert.Equal(2, (int)BattleOutcome.Defeat);
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~BattleOutcomeTests`
Expected: build error（`Core.Battle.State.BattleOutcome` 未定義 — `Core.Battle.BattleOutcome` は別 namespace）

- [ ] **Step 3: 実装**

`src/Core/Battle/State/BattleOutcome.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>バトル結果。Defeat はソロモードでのみ発生（Phase 10.2.A 時点）。</summary>
public enum BattleOutcome
{
    Pending = 0,
    Victory = 1,
    Defeat  = 2,
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleOutcomeTests`
Expected: 3 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/BattleOutcome.cs tests/Core.Tests/Battle/State/BattleOutcomeTests.cs
git commit -m "feat(battle): add BattleOutcome enum with Defeat (Phase 10.2.A Task 2)"
```

---

## Task 3: ActorSide enum + テスト

**Files:**
- Create: `src/Core/Battle/State/ActorSide.cs`
- Create: `tests/Core.Tests/Battle/State/ActorSideTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class ActorSideTests
{
    [Fact] public void Ally_value_is_zero() => Assert.Equal(0, (int)ActorSide.Ally);
    [Fact] public void Enemy_value_is_one() => Assert.Equal(1, (int)ActorSide.Enemy);
}
```

- [ ] **Step 2: 失敗確認** — Run: `dotnet test --filter FullyQualifiedName~ActorSideTests` → build error

- [ ] **Step 3: 実装**

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

public enum ActorSide
{
    Ally  = 0,
    Enemy = 1,
}
```

- [ ] **Step 4: 緑確認** — `dotnet test --filter FullyQualifiedName~ActorSideTests` → 2 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/ActorSide.cs tests/Core.Tests/Battle/State/ActorSideTests.cs
git commit -m "feat(battle): add ActorSide enum (Phase 10.2.A Task 3)"
```

---

## Task 4: AttackPool struct + テスト

**Files:**
- Create: `src/Core/Battle/State/AttackPool.cs`
- Create: `tests/Core.Tests/Battle/State/AttackPoolTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class AttackPoolTests
{
    [Fact] public void Empty_has_zero_sum_and_count()
    {
        var p = AttackPool.Empty;
        Assert.Equal(0, p.Sum);
        Assert.Equal(0, p.AddCount);
        Assert.Equal(0, p.RawTotal);
    }

    [Fact] public void Add_increments_sum_and_addcount()
    {
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(8, p.Sum);
        Assert.Equal(2, p.AddCount);
        Assert.Equal(8, p.RawTotal);
    }

    [Fact] public void RawTotal_equals_Sum()
    {
        var p = AttackPool.Empty.Add(7).Add(0).Add(11);
        Assert.Equal(p.Sum, p.RawTotal);
    }

    [Fact] public void Add_zero_still_increments_addcount()
    {
        // 力バフ遡及計算 (10.2.B) で AddCount × strength が乗るため、amount=0 でも AddCount は +1
        var p = AttackPool.Empty.Add(0);
        Assert.Equal(0, p.Sum);
        Assert.Equal(1, p.AddCount);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/State/AttackPool.cs`:

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

- [ ] **Step 4: 緑確認** — 4 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/AttackPool.cs tests/Core.Tests/Battle/State/AttackPoolTests.cs
git commit -m "feat(battle): add AttackPool struct (Phase 10.2.A Task 4)"
```

---

## Task 5: BlockPool struct + テスト

**Files:**
- Create: `src/Core/Battle/State/BlockPool.cs`
- Create: `tests/Core.Tests/Battle/State/BlockPoolTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BlockPoolTests
{
    [Fact] public void Empty_is_zero()
    {
        var p = BlockPool.Empty;
        Assert.Equal(0, p.Sum);
        Assert.Equal(0, p.AddCount);
    }

    [Fact] public void Add_increments_sum_and_addcount()
    {
        var p = BlockPool.Empty.Add(5).Add(3);
        Assert.Equal(8, p.Sum);
        Assert.Equal(2, p.AddCount);
    }

    [Fact] public void Consume_partial_keeps_remainder_resets_addcount()
    {
        var p = BlockPool.Empty.Add(5).Add(5); // Sum=10, AddCount=2
        var after = p.Consume(3);              // 10 - 3 = 7
        Assert.Equal(7, after.Sum);
        Assert.Equal(0, after.AddCount);
    }

    [Fact] public void Consume_overflow_clamps_to_zero()
    {
        var p = BlockPool.Empty.Add(5);
        var after = p.Consume(20);
        Assert.Equal(0, after.Sum);
        Assert.Equal(0, after.AddCount);
    }

    [Fact] public void Consume_zero_keeps_sum_but_resets_addcount()
    {
        var p = BlockPool.Empty.Add(5).Add(2);
        var after = p.Consume(0);
        Assert.Equal(7, after.Sum);
        Assert.Equal(0, after.AddCount);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/State/BlockPool.cs`:

```csharp
using System;

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
    /// 攻撃の総量を受けて Block を消費。引数 `incomingAttack` は「ブロック適用前の攻撃値」を渡す。
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

- [ ] **Step 4: 緑確認** — 5 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/BlockPool.cs tests/Core.Tests/Battle/State/BlockPoolTests.cs
git commit -m "feat(battle): add BlockPool struct (Phase 10.2.A Task 5)"
```

---

## Task 6: BattleCardInstance record + テスト

**Files:**
- Create: `src/Core/Battle/State/BattleCardInstance.cs`
- Create: `tests/Core.Tests/Battle/State/BattleCardInstanceTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattleCardInstanceTests
{
    [Fact] public void Record_equality_holds()
    {
        var a = new BattleCardInstance("inst1", "strike", false, null);
        var b = new BattleCardInstance("inst1", "strike", false, null);
        Assert.Equal(a, b);
    }

    [Fact] public void CostOverride_can_be_null_or_value()
    {
        var noOverride = new BattleCardInstance("inst1", "strike", false, null);
        var withOverride = new BattleCardInstance("inst1", "strike", false, 0);
        Assert.Null(noOverride.CostOverride);
        Assert.Equal(0, withOverride.CostOverride);
    }

    [Fact] public void IsUpgraded_flag_distinguishes_records()
    {
        var plain = new BattleCardInstance("inst1", "strike", false, null);
        var upgraded = new BattleCardInstance("inst1", "strike", true, null);
        Assert.NotEqual(plain, upgraded);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/State/BattleCardInstance.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中のパイルカード instance。
/// `Cards.CardInstance`（RunState.Deck 用、Id+Upgraded のみ）とは別物。
/// バトル開始時に `Cards.CardInstance` から生成され、戦闘終了で破棄される。
/// 親 spec §3-4 参照。
/// </summary>
/// <param name="InstanceId">バトル中の一意 ID（重複カード識別用）</param>
/// <param name="CardDefinitionId">マスター定義 ID</param>
/// <param name="IsUpgraded">強化済みかどうか</param>
/// <param name="CostOverride">戦闘内一時上書き（10.2.A では未使用、後続 phase で利用）</param>
public sealed record BattleCardInstance(
    string InstanceId,
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride);
```

- [ ] **Step 4: 緑確認** — 3 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/BattleCardInstance.cs tests/Core.Tests/Battle/State/BattleCardInstanceTests.cs
git commit -m "feat(battle): add BattleCardInstance record (Phase 10.2.A Task 6)"
```

---

## Task 7: CombatActor record + テスト

**Files:**
- Create: `src/Core/Battle/State/CombatActor.cs`
- Create: `tests/Core.Tests/Battle/State/CombatActorTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class CombatActorTests
{
    private static CombatActor MakeHero(int hp = 70) =>
        new("hero1", "hero", ActorSide.Ally, 0, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, null);

    [Fact] public void IsAlive_true_when_hp_positive()
    {
        var a = MakeHero(70);
        Assert.True(a.IsAlive);
    }

    [Fact] public void IsAlive_false_when_hp_zero()
    {
        var a = MakeHero(70) with { CurrentHp = 0 };
        Assert.False(a.IsAlive);
    }

    [Fact] public void IsAlive_false_when_hp_negative()
    {
        var a = MakeHero(70) with { CurrentHp = -5 };
        Assert.False(a.IsAlive);
    }

    [Fact] public void Record_equality_holds()
    {
        Assert.Equal(MakeHero(70), MakeHero(70));
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/State/CombatActor.cs`:

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
    int SlotIndex,
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

- [ ] **Step 4: 緑確認** — 4 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/CombatActor.cs tests/Core.Tests/Battle/State/CombatActorTests.cs
git commit -m "feat(battle): add CombatActor record (Phase 10.2.A Task 7)"
```

---

## Task 8: BattleEventKind enum + テスト

**Files:**
- Create: `src/Core/Battle/Events/BattleEventKind.cs`
- Create: `tests/Core.Tests/Battle/Events/BattleEventKindTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using RoguelikeCardGame.Core.Battle.Events;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Events;

public class BattleEventKindTests
{
    [Fact] public void BattleStart_value_is_zero() => Assert.Equal(0, (int)BattleEventKind.BattleStart);
    [Fact] public void TurnStart_value_is_one()   => Assert.Equal(1, (int)BattleEventKind.TurnStart);
    [Fact] public void PlayCard_value_is_two()    => Assert.Equal(2, (int)BattleEventKind.PlayCard);
    [Fact] public void AttackFire_value_is_three()=> Assert.Equal(3, (int)BattleEventKind.AttackFire);
    [Fact] public void DealDamage_value_is_four() => Assert.Equal(4, (int)BattleEventKind.DealDamage);
    [Fact] public void GainBlock_value_is_five()  => Assert.Equal(5, (int)BattleEventKind.GainBlock);
    [Fact] public void ActorDeath_value_is_six()  => Assert.Equal(6, (int)BattleEventKind.ActorDeath);
    [Fact] public void EndTurn_value_is_seven()   => Assert.Equal(7, (int)BattleEventKind.EndTurn);
    [Fact] public void BattleEnd_value_is_eight() => Assert.Equal(8, (int)BattleEventKind.BattleEnd);
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Events/BattleEventKind.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中に発火されるイベント種別。Phase 10.2.A の最小セット 9 種。
/// 後続 phase で ApplyStatus / Summon / Exhaust / Upgrade /
/// RelicTrigger / UsePotion 等を追加していく。
/// </summary>
public enum BattleEventKind
{
    BattleStart = 0,
    TurnStart   = 1,
    PlayCard    = 2,
    AttackFire  = 3,
    DealDamage  = 4,
    GainBlock   = 5,
    ActorDeath  = 6,
    EndTurn     = 7,
    BattleEnd   = 8,
}
```

- [ ] **Step 4: 緑確認** — 9 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Events/BattleEventKind.cs tests/Core.Tests/Battle/Events/BattleEventKindTests.cs
git commit -m "feat(battle): add BattleEventKind enum (Phase 10.2.A Task 8)"
```

---

## Task 9: BattleEvent record + テスト

**Files:**
- Create: `src/Core/Battle/Events/BattleEvent.cs`
- Create: `tests/Core.Tests/Battle/Events/BattleEventEmissionTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using RoguelikeCardGame.Core.Battle.Events;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Events;

public class BattleEventEmissionTests
{
    [Fact] public void Default_optional_fields_are_null()
    {
        var ev = new BattleEvent(BattleEventKind.TurnStart, Order: 0);
        Assert.Null(ev.CasterInstanceId);
        Assert.Null(ev.TargetInstanceId);
        Assert.Null(ev.Amount);
        Assert.Null(ev.CardId);
        Assert.Null(ev.Note);
    }

    [Fact] public void All_fields_assignable()
    {
        var ev = new BattleEvent(
            BattleEventKind.DealDamage, Order: 3,
            CasterInstanceId: "hero1", TargetInstanceId: "goblin1",
            Amount: 5, CardId: "strike", Note: "single");
        Assert.Equal(BattleEventKind.DealDamage, ev.Kind);
        Assert.Equal(3, ev.Order);
        Assert.Equal("hero1", ev.CasterInstanceId);
        Assert.Equal("goblin1", ev.TargetInstanceId);
        Assert.Equal(5, ev.Amount);
        Assert.Equal("strike", ev.CardId);
        Assert.Equal("single", ev.Note);
    }

    [Fact] public void Record_equality_holds()
    {
        var a = new BattleEvent(BattleEventKind.PlayCard, 0, CardId: "strike");
        var b = new BattleEvent(BattleEventKind.PlayCard, 0, CardId: "strike");
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Events/BattleEvent.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中の 1 イベント。`BattleEngine` の各公開メソッドが
/// `IReadOnlyList&lt;BattleEvent&gt;` として時系列順に返す。
/// Phase 10.3 で `BattleEventDto` に変換され Client に push される。
/// 親 spec §9-7 参照。
/// </summary>
public sealed record BattleEvent(
    BattleEventKind Kind,
    int Order,
    string? CasterInstanceId = null,
    string? TargetInstanceId = null,
    int? Amount = null,
    string? CardId = null,
    string? Note = null);
```

- [ ] **Step 4: 緑確認** — 3 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Events/BattleEvent.cs tests/Core.Tests/Battle/Events/BattleEventEmissionTests.cs
git commit -m "feat(battle): add BattleEvent record (Phase 10.2.A Task 9)"
```

---

## Task 10: 新 BattleState record（型のみ・テストは Task 11 で）

**Files:**
- Create: `src/Core/Battle/State/BattleState.cs`

> **注**: この時点では同名の `Core.Battle.BattleState`（旧 placeholder）が `src/Core/Battle/BattleState.cs` に存在する。namespace が違うので衝突しない。Task 12 で旧 BattleState を rename する。

- [ ] **Step 1: 実装**（Invariant テストは次の Task 11 でまとめて書く）

`src/Core/Battle/State/BattleState.cs`:

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル全体の不変状態。Phase 10.2.A 版。
/// 10.2.B〜E で Statuses / コンボ / SummonHeld / PowerCards 等のフィールドが追加される。
/// 親 spec §3-1 参照。
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
    string EncounterId);
```

- [ ] **Step 2: build 確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0（旧 `Core.Battle.BattleState` と衝突しないことを確認）

- [ ] **Step 3: commit**

```bash
git add src/Core/Battle/State/BattleState.cs
git commit -m "feat(battle): add new BattleState record (Phase 10.2.A Task 10)"
```

---

## Task 11: BattleStateInvariantTests（不変条件 8 項目）

**Files:**
- Create: `tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs`

> 不変条件をユーティリティメソッドとして spec 化し、後続 Engine タスクから参照する。**spec §2-9 の 8 項目を網羅。**

- [ ] **Step 1: 失敗テストを書く**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattleStateInvariantTests
{
    private static CombatActor Hero(int hp = 70) =>
        new("hero1", "hero", ActorSide.Ally, 0, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, null);

    private static CombatActor Goblin(int slotIndex, int hp = 20) =>
        new($"goblin{slotIndex}", "goblin", ActorSide.Enemy, slotIndex, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, "swing");

    private static BattleState Make(
        ImmutableArray<CombatActor>? allies = null,
        ImmutableArray<CombatActor>? enemies = null,
        BattlePhase phase = BattlePhase.PlayerInput,
        BattleOutcome outcome = BattleOutcome.Pending,
        int turn = 1, int energy = 3, int energyMax = 3,
        int? tgtA = 0, int? tgtE = 0)
        => new(
            Turn: turn, Phase: phase, Outcome: outcome,
            Allies: allies ?? ImmutableArray.Create(Hero()),
            Enemies: enemies ?? ImmutableArray.Create(Goblin(0)),
            TargetAllyIndex: tgtA, TargetEnemyIndex: tgtE,
            Energy: energy, EnergyMax: energyMax,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: "enc1");

    [Fact] public void Allies_count_at_least_one_at_most_four()
    {
        var s = Make();
        Assert.InRange(s.Allies.Length, 1, 4);
    }

    [Fact] public void Enemies_count_at_most_four()
    {
        var s = Make();
        Assert.InRange(s.Enemies.Length, 0, 4);
    }

    [Fact] public void Hero_is_at_slot_zero()
    {
        var s = Make();
        Assert.Equal("hero", s.Allies[0].DefinitionId);
        Assert.Equal(0, s.Allies[0].SlotIndex);
    }

    [Fact] public void Phase_resolved_iff_outcome_not_pending_victory()
    {
        var s = Make(phase: BattlePhase.Resolved, outcome: BattleOutcome.Victory);
        Assert.True(s.Phase == BattlePhase.Resolved);
        Assert.NotEqual(BattleOutcome.Pending, s.Outcome);
    }

    [Fact] public void Phase_resolved_iff_outcome_not_pending_defeat()
    {
        var s = Make(phase: BattlePhase.Resolved, outcome: BattleOutcome.Defeat);
        Assert.NotEqual(BattleOutcome.Pending, s.Outcome);
    }

    [Fact] public void Energy_within_bounds()
    {
        var s = Make(energy: 3, energyMax: 3);
        Assert.InRange(s.Energy, 0, s.EnergyMax);
    }

    [Fact] public void Turn_starts_from_one_or_higher()
    {
        var s = Make(turn: 1);
        Assert.True(s.Turn >= 1);
    }

    [Fact] public void Target_indices_can_be_null()
    {
        var s = Make(tgtA: null, tgtE: null);
        Assert.Null(s.TargetAllyIndex);
        Assert.Null(s.TargetEnemyIndex);
    }

    [Fact] public void Pile_count_equals_initial_deck_when_no_consumption()
    {
        var deck = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null),
            new BattleCardInstance("c2", "defend", false, null));
        var s = Make() with { DrawPile = deck };
        var total = s.DrawPile.Length + s.Hand.Length + s.DiscardPile.Length + s.ExhaustPile.Length;
        Assert.Equal(2, total);
    }
}
```

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleStateInvariantTests`
Expected: 9 passed

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs
git commit -m "test(battle): add BattleState invariant tests (Phase 10.2.A Task 11)"
```

---

## Task 12: 旧 BattleState → BattlePlaceholderState リネーム（破壊的変更、1 commit）

**Files:**
- Move/Modify: `src/Core/Battle/BattleState.cs` → `src/Core/Battle/BattlePlaceholderState.cs`（型名 `BattleState` → `BattlePlaceholderState`、`EnemyInstance` → `PlaceholderEnemyInstance`）
- Modify: `src/Core/Battle/BattlePlaceholder.cs`（型参照更新）
- Modify: `src/Core/Run/RunState.cs`（`BattleState? ActiveBattle` → `BattlePlaceholderState? ActiveBattle`）
- Modify: `tests/Core.Tests/Battle/BattlePlaceholderTests.cs`
- Modify: `tests/Core.Tests/Battle/BattlePlaceholderBestiaryTests.cs`（必要に応じて）
- Modify: `tests/Core.Tests/Run/RunStateSerializerTests.cs`
- Modify: その他 `Core.Battle.BattleState` または `Core.Battle.EnemyInstance` を import / 使用している全ファイル

> **重要**: この Task は spec §6-3 で予告した「ビルド赤を最小化するため 1 commit でまとめる」ステップ。`git grep` で全参照を見つけ、一気に書き換える。

- [ ] **Step 1: 全参照を grep で洗い出す**

```bash
git grep -nl "Core\.Battle\.BattleState\|Core\.Battle\.EnemyInstance\|new BattleState(\|new EnemyInstance(\|BattleOutcome\b"
```

> **注**: `BattleOutcome` は新旧両方に存在するため、旧名前空間（`Core.Battle`）で使われている箇所だけを置換対象とする。新名前空間（`Core.Battle.State`）の使用は残す。

- [ ] **Step 2: 旧ファイルをリネーム**

```bash
git mv src/Core/Battle/BattleState.cs src/Core/Battle/BattlePlaceholderState.cs
```

- [ ] **Step 3: 旧ファイルの中身を書き換え**

`src/Core/Battle/BattlePlaceholderState.cs`（書き換え後の内容）:

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle;

/// <summary>
/// Phase 5 placeholder バトルの state。Phase 10.5 で削除予定。
/// 新本格バトルは <see cref="RoguelikeCardGame.Core.Battle.State.BattleState"/>。
/// </summary>
public sealed record BattlePlaceholderState(
    string EncounterId,
    ImmutableArray<PlaceholderEnemyInstance> Enemies,
    BattleOutcome Outcome);

public sealed record PlaceholderEnemyInstance(
    string EnemyDefinitionId,
    int CurrentHp,
    int MaxHp,
    string CurrentMoveId);

/// <summary>placeholder 用の旧 BattleOutcome。Phase 10.5 で削除し、新 <see cref="State.BattleOutcome"/> に統合。</summary>
public enum BattleOutcome { Pending, Victory }
```

- [ ] **Step 4: BattlePlaceholder.cs の型参照更新**

`src/Core/Battle/BattlePlaceholder.cs`:

旧コード:
```csharp
var enemies = ImmutableArray.CreateBuilder<EnemyInstance>(encounter.EnemyIds.Count);
foreach (var eid in encounter.EnemyIds)
{
    var def = data.Enemies[eid];
    int hp = def.Hp;
    enemies.Add(new EnemyInstance(eid, hp, hp, def.InitialMoveId));
}
var battle = new BattleState(encounterId, enemies.ToImmutable(), BattleOutcome.Pending);
```

新コード:
```csharp
var enemies = ImmutableArray.CreateBuilder<PlaceholderEnemyInstance>(encounter.EnemyIds.Count);
foreach (var eid in encounter.EnemyIds)
{
    var def = data.Enemies[eid];
    int hp = def.Hp;
    enemies.Add(new PlaceholderEnemyInstance(eid, hp, hp, def.InitialMoveId));
}
var battle = new BattlePlaceholderState(encounterId, enemies.ToImmutable(), BattleOutcome.Pending);
```

`Win` メソッドの `state.ActiveBattle with { Outcome = BattleOutcome.Victory }` は `state.ActiveBattle` の型変更で自動的に新型を使う（変更不要）。

- [ ] **Step 5: RunState.cs の ActiveBattle 型を更新**

`src/Core/Run/RunState.cs:31`:

```csharp
// 旧: BattleState? ActiveBattle,
BattlePlaceholderState? ActiveBattle,
```

`using RoguelikeCardGame.Core.Battle;` は既存（無変更）。型名のみ書き換え。

- [ ] **Step 6: RunState.NewSoloRun 内の `ActiveBattle: null` は型推論で自動追従（書換不要）**

ただし行頭コメント `ActiveBattle:` は識別子なので変更不要。

- [ ] **Step 7: tests/Core.Tests/Battle/BattlePlaceholderTests.cs の型参照更新**

`tests/Core.Tests/Battle/BattlePlaceholderTests.cs:43-45`:

```csharp
// 旧:
ActiveBattle = new BattleState("enc_w_jaw_worm",
    ImmutableArray.Create(new EnemyInstance("jaw_worm", 42, 42, "chomp")),
    BattleOutcome.Pending)

// 新:
ActiveBattle = new BattlePlaceholderState("enc_w_jaw_worm",
    ImmutableArray.Create(new PlaceholderEnemyInstance("jaw_worm", 42, 42, "chomp")),
    BattleOutcome.Pending)
```

その他の `BattleState` / `EnemyInstance` 参照も同様に置換。

- [ ] **Step 8: その他のテストファイル更新**

Step 1 の grep 結果に出た全ファイルで `BattleState` → `BattlePlaceholderState`、`EnemyInstance` → `PlaceholderEnemyInstance` を文脈に応じて置換。`BattleOutcome` は旧 `Core.Battle.BattleOutcome` のままでよい（型名は不変）。

具体的に予想される対象:
- `tests/Core.Tests/Battle/BattlePlaceholderBestiaryTests.cs`（型参照あれば）
- `tests/Core.Tests/Run/RunStateSerializerTests.cs:46`（`Assert.Null(loaded.ActiveBattle)` のみで shape 検証なし、型のみ追従）
- `src/Core/Run/RunStateSerializer.cs`（`JsonSerializer.Serialize/Deserialize<RunState>` の generic 経路、無変更）

- [ ] **Step 9: ビルド緑確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

Run: `dotnet test`
Expected: 全件緑（リネーム前と同じテスト数 + Tasks 1-11 の新規分）

- [ ] **Step 10: commit（破壊的、1 まとめ）**

```bash
git add -A
git commit -m "refactor(battle): rename BattleState→BattlePlaceholderState to free namespace for new engine (Phase 10.2.A Task 12)

旧 Core.Battle.BattleState (placeholder, Phase 5 由来) を BattlePlaceholderState にリネーム
し、EnemyInstance も PlaceholderEnemyInstance に追従。新 Core.Battle.State.BattleState
(Phase 10.2.A Task 10) との型衝突を回避。

JSON shape は変更なし（フィールド構造同一）のため、save schema migration 不要。
v8 移行は Phase 10.5 cleanup で実施予定。"
```

---

## Task 13: BattleSummary record + BattleFixtures（テストヘルパー）

**Files:**
- Create: `src/Core/Battle/Engine/BattleSummary.cs`
- Create: `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs`
- Create: `src/Core/Battle/Statuses/.gitkeep`（10.2.B 用空フォルダ）

> **注**: BattleFixtures は以降のタスクで使う共通 factory なので、Engine 実装の前に用意する。

- [ ] **Step 1: BattleSummary 実装**

`src/Core/Battle/Engine/BattleSummary.cs`:

```csharp
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 戦闘終了時に <see cref="BattleEngine.Finalize"/> が返すサマリ。
/// 親 spec §10-2 参照。
/// 10.2.E で ConsumedPotionIds / RunSideOperations が追加予定。
/// </summary>
public sealed record BattleSummary(
    int FinalHeroHp,
    BattleOutcome Outcome,
    string EncounterId);
```

- [ ] **Step 2: 空 Statuses フォルダの placeholder**

```bash
mkdir -p src/Core/Battle/Statuses
echo "" > src/Core/Battle/Statuses/.gitkeep
```

- [ ] **Step 3: BattleFixtures.cs を実装**

`tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Tests.Battle.Fixtures;

/// <summary>Phase 10.2.A バトルテスト用の共通 factory。インライン生成方針 (spec Q5)。</summary>
public static class BattleFixtures
{
    // ===== CombatActor factories =====

    public static CombatActor Hero(int hp = 70, int slotIndex = 0) =>
        new("hero_inst", "hero", ActorSide.Ally, slotIndex, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, null);

    public static CombatActor Goblin(int slotIndex = 0, int hp = 20, string moveId = "swing") =>
        new($"goblin_inst_{slotIndex}", "goblin", ActorSide.Enemy, slotIndex, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, moveId);

    // ===== CardDefinition factories =====

    public static CardDefinition Strike(int amount = 6) =>
        new("strike", "Strike", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, amount) },
            UpgradedEffects: null, Keywords: null);

    public static CardDefinition Defend(int amount = 5) =>
        new("defend", "Defend", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("block", EffectScope.Self, null, amount) },
            UpgradedEffects: null, Keywords: null);

    public static CardDefinition Cleave(int amount = 4) =>
        new("cleave", "Cleave", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, amount) },
            UpgradedEffects: null, Keywords: null);

    // ===== EnemyDefinition factories =====

    public static EnemyDefinition GoblinDef(int hp = 20, int attack = 5) =>
        new("goblin", "Goblin", "img_goblin", hp, new EnemyPool(1, EnemyTier.Weak),
            "swing",
            new[] {
                new MoveDefinition("swing", MoveKind.Attack,
                    new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, attack) },
                    "swing")
            });

    public static EncounterDefinition SingleGoblinEncounter() =>
        new("enc_test", new EnemyPool(1, EnemyTier.Weak), new[] { "goblin" });

    // ===== DataCatalog factory =====

    /// <summary>テスト用最小限の DataCatalog。必要に応じて defs を上書き可能。</summary>
    public static DataCatalog MinimalCatalog(
        IEnumerable<CardDefinition>? cards = null,
        IEnumerable<EnemyDefinition>? enemies = null,
        IEnumerable<EncounterDefinition>? encounters = null)
    {
        var cardDict = (cards ?? new[] { Strike(), Defend() })
            .ToDictionary(c => c.Id);
        var enemyDict = (enemies ?? new[] { GoblinDef() })
            .ToDictionary(e => e.Id);
        var encDict = (encounters ?? new[] { SingleGoblinEncounter() })
            .ToDictionary(e => e.Id);
        return new DataCatalog(
            Cards: cardDict,
            Relics: new Dictionary<string, RoguelikeCardGame.Core.Relics.RelicDefinition>(),
            Potions: new Dictionary<string, RoguelikeCardGame.Core.Potions.PotionDefinition>(),
            Enemies: enemyDict,
            Encounters: encDict,
            RewardTables: new Dictionary<string, RewardTable>(),
            Characters: new Dictionary<string, CharacterDefinition>(),
            Events: new Dictionary<string, RoguelikeCardGame.Core.Events.EventDefinition>());
    }

    // ===== BattleCardInstance helpers =====

    public static BattleCardInstance MakeBattleCard(string defId, string instId, bool upgraded = false) =>
        new(instId, defId, upgraded, null);
}
```

- [ ] **Step 4: ビルド緑確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleSummary.cs src/Core/Battle/Statuses/.gitkeep tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs
git commit -m "feat(battle): add BattleSummary record and BattleFixtures helper (Phase 10.2.A Task 13)"
```

---

## Task 14: TurnEndProcessor + テスト

**Files:**
- Create: `src/Core/Battle/Engine/TurnEndProcessor.cs`
- Create: `tests/Core.Tests/Battle/Engine/TurnEndProcessorTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnEndProcessorTests
{
    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> hand,
        CombatActor? hero = null,
        CombatActor? enemy = null)
    {
        hero ??= BattleFixtures.Hero();
        enemy ??= BattleFixtures.Goblin();
        return new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: ImmutableArray.Create(enemy),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: "enc_test");
    }

    [Fact] public void Resets_block_on_all_actors()
    {
        var hero = BattleFixtures.Hero() with { Block = BlockPool.Empty.Add(5) };
        var enemy = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(3) };
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty, hero, enemy);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Equal(BlockPool.Empty, next.Allies[0].Block);
        Assert.Equal(BlockPool.Empty, next.Enemies[0].Block);
    }

    [Fact] public void Resets_attack_pools_on_all_actors()
    {
        var hero = BattleFixtures.Hero() with {
            AttackSingle = AttackPool.Empty.Add(6),
            AttackAll    = AttackPool.Empty.Add(4) };
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty, hero);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Equal(AttackPool.Empty, next.Allies[0].AttackSingle);
        Assert.Equal(AttackPool.Empty, next.Allies[0].AttackRandom);
        Assert.Equal(AttackPool.Empty, next.Allies[0].AttackAll);
    }

    [Fact] public void Discards_all_hand_cards_to_discard_pile()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"));
        var s = MakeState(hand);
        var (next, _) = TurnEndProcessor.Process(s);
        Assert.Empty(next.Hand);
        Assert.Equal(2, next.DiscardPile.Length);
        Assert.Equal(new[] { "c1", "c2" }, next.DiscardPile.Select(c => c.InstanceId).ToArray());
    }

    [Fact] public void No_events_emitted_in_phase_10_2_a()
    {
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty);
        var (_, events) = TurnEndProcessor.Process(s);
        Assert.Empty(events);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/TurnEndProcessor.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン終了処理。10.2.A は最小限（Block / AttackPool リセット、手札全捨て）。
/// 10.2.B で OnTurnEnd レリック / コンボリセット, 10.2.D で retainSelf 対応が追加される。
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

`using System.Linq;` を追加（`Select` のため）。

- [ ] **Step 4: 緑確認** — `dotnet test --filter FullyQualifiedName~TurnEndProcessorTests` → 4 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/TurnEndProcessor.cs tests/Core.Tests/Battle/Engine/TurnEndProcessorTests.cs
git commit -m "feat(battle): add TurnEndProcessor (Phase 10.2.A Task 14)"
```

---

## Task 15: TurnStartProcessor + テスト

**Files:**
- Create: `src/Core/Battle/Engine/TurnStartProcessor.cs`
- Create: `tests/Core.Tests/Battle/Engine/TurnStartProcessorTests.cs`

> **注**: 5 ドローのシャッフルは `IRng.NextInt` ベースの Fisher-Yates を内部 helper で実装。`IRng` に Shuffle メソッドはない。

- [ ] **Step 1: 失敗テストを書く**

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

public class TurnStartProcessorTests
{
    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> drawPile,
        ImmutableArray<BattleCardInstance>? hand = null,
        int turn = 1, int energy = 0, int energyMax = 3)
        => new(
            Turn: turn, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: energyMax,
            DrawPile: drawPile,
            Hand: hand ?? ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: "enc_test");

    private static ImmutableArray<BattleCardInstance> Deck(int n) =>
        Enumerable.Range(0, n)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"c{i}"))
            .ToImmutableArray();

    [Fact] public void Increments_turn()
    {
        var s = MakeState(Deck(10), turn: 1);
        var rng = new FakeRng(new int[100], new double[0]); // shuffle 不要、すでに 10 枚
        var (next, _) = TurnStartProcessor.Process(s, rng);
        Assert.Equal(2, next.Turn);
    }

    [Fact] public void Refills_energy_to_max()
    {
        var s = MakeState(Deck(10), energy: 0, energyMax: 3);
        var rng = new FakeRng(new int[100], new double[0]);
        var (next, _) = TurnStartProcessor.Process(s, rng);
        Assert.Equal(3, next.Energy);
    }

    [Fact] public void Draws_five_cards_when_draw_pile_sufficient()
    {
        var s = MakeState(Deck(10));
        var rng = new FakeRng(new int[100], new double[0]);
        var (next, _) = TurnStartProcessor.Process(s, rng);
        Assert.Equal(5, next.Hand.Length);
        Assert.Equal(5, next.DrawPile.Length);
    }

    [Fact] public void Reshuffles_discard_into_draw_when_empty()
    {
        var hand = ImmutableArray<BattleCardInstance>.Empty;
        var s = MakeState(Deck(2), hand) with { DiscardPile = Deck(5) };
        // ハンドに既に 0 枚、山札 2 枚、捨札 5 枚 → 5 枚ドロー要求
        // 山札 2 枚 ドロー → 山札 0 枚 → 捨札 5 枚をシャッフルして山札へ → 残り 3 枚ドロー
        var rng = new FakeRng(new int[] { 0, 0, 0, 0, 0 }, new double[0]); // Fisher-Yates 用
        var (next, _) = TurnStartProcessor.Process(s, rng);
        Assert.Equal(5, next.Hand.Length);
        Assert.Empty(next.DiscardPile);
    }

    [Fact] public void Stops_when_both_piles_empty()
    {
        var s = MakeState(Deck(2));
        var rng = new FakeRng(new int[100], new double[0]);
        var (next, _) = TurnStartProcessor.Process(s, rng);
        Assert.Equal(2, next.Hand.Length);
        Assert.Empty(next.DrawPile);
    }

    [Fact] public void Stops_at_hand_cap_of_ten()
    {
        var hand = Enumerable.Range(0, 8)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"h{i}"))
            .ToImmutableArray();
        var s = MakeState(Deck(10), hand);
        var rng = new FakeRng(new int[100], new double[0]);
        var (next, _) = TurnStartProcessor.Process(s, rng);
        Assert.Equal(10, next.Hand.Length); // 8 既存 + 2 ドロー (5 ではなく 10 でストップ)
        Assert.Equal(8, next.DrawPile.Length); // 10 - 2 = 8 残り
    }

    [Fact] public void Emits_TurnStart_event()
    {
        var s = MakeState(Deck(10));
        var rng = new FakeRng(new int[100], new double[0]);
        var (_, events) = TurnStartProcessor.Process(s, rng);
        Assert.Contains(events, e => e.Kind == BattleEventKind.TurnStart);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/TurnStartProcessor.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン開始処理。10.2.A は最小限（ターン+1, Energy 全回復, 5 ドロー）。
/// 10.2.B で 毒・状態異常 tick / 召喚 Lifetime tick / OnTurnStart レリックが追加される。
/// 親 spec §4-2 参照。
/// </summary>
internal static class TurnStartProcessor
{
    public const int DrawPerTurn = 5;
    public const int HandCap = 10;

    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng)
    {
        var s = state with
        {
            Turn = state.Turn + 1,
            Energy = state.EnergyMax,
        };
        s = DrawCards(s, DrawPerTurn, rng);
        var events = new List<BattleEvent>
        {
            new(BattleEventKind.TurnStart, Order: 0, Note: $"turn={s.Turn}"),
        };
        return (s, events);
    }

    private static BattleState DrawCards(BattleState state, int count, IRng rng)
    {
        var hand = state.Hand.ToBuilder();
        var draw = state.DrawPile.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();

        for (int i = 0; i < count; i++)
        {
            if (hand.Count >= HandCap) break;
            if (draw.Count == 0)
            {
                if (discard.Count == 0) break;
                ShuffleInto(discard, draw, rng);
                discard.Clear();
            }
            // 山札末尾から取り出す（Fisher-Yates 後の順序が DrawPile の先頭から消費される慣習で、Take from front）
            var top = draw[0];
            draw.RemoveAt(0);
            hand.Add(top);
        }

        return state with
        {
            Hand = hand.ToImmutable(),
            DrawPile = draw.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
    }

    /// <summary>
    /// Fisher-Yates シャッフル。`source` の中身を `dest` に移しながらランダム順で並べる。
    /// </summary>
    private static void ShuffleInto(
        ImmutableArray<BattleCardInstance>.Builder source,
        ImmutableArray<BattleCardInstance>.Builder dest,
        IRng rng)
    {
        var arr = source.ToArray();
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        foreach (var c in arr) dest.Add(c);
    }
}
```

- [ ] **Step 4: 緑確認** — 7 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/TurnStartProcessor.cs tests/Core.Tests/Battle/Engine/TurnStartProcessorTests.cs
git commit -m "feat(battle): add TurnStartProcessor with shuffle (Phase 10.2.A Task 15)"
```

---

## Task 16: EffectApplier + テスト

**Files:**
- Create: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`

> 10.2.A は `attack` / `block` のみ対応。その他 action は no-op + イベントなし。

- [ ] **Step 1: 失敗テストを書く**

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

public class EffectApplierTests
{
    private static BattleState BasicState() => new(
        Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero()),
        Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    [Fact] public void Attack_single_adds_to_caster_AttackSingle()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng());
        Assert.Equal(6, next.Allies[0].AttackSingle.Sum);
        Assert.Equal(1, next.Allies[0].AttackSingle.AddCount);
    }

    [Fact] public void Attack_random_adds_to_caster_AttackRandom()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 4);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng());
        Assert.Equal(4, next.Allies[0].AttackRandom.Sum);
    }

    [Fact] public void Attack_all_adds_to_caster_AttackAll()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng());
        Assert.Equal(3, next.Allies[0].AttackAll.Sum);
    }

    [Fact] public void Block_self_adds_to_caster_block()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("block", EffectScope.Self, null, 5);
        var (next, events) = EffectApplier.Apply(s, caster, eff, Rng());
        Assert.Equal(5, next.Allies[0].Block.Sum);
        Assert.Contains(events, e => e.Kind == BattleEventKind.GainBlock && e.Amount == 5);
    }

    [Fact] public void Unimplemented_action_is_noop()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, events) = EffectApplier.Apply(s, caster, eff, Rng());
        // 状態変化なし、イベント emission なし (10.2.A スコープ外)
        Assert.Equal(s, next);
        Assert.Empty(events);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/EffectApplier.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 単一 CardEffect を BattleState に適用する。
/// Phase 10.2.A は "attack" / "block" のみ対応。その他 action は no-op。
/// 10.2.B〜E で対応 action を段階的に増やす。親 spec §5 参照。
/// </summary>
internal static class EffectApplier
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        return effect.Action switch
        {
            "attack" => ApplyAttack(state, caster, effect),
            "block"  => ApplyBlock(state, caster, effect),
            _        => (state, System.Array.Empty<BattleEvent>()),
        };
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyAttack(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        var updated = effect.Scope switch
        {
            EffectScope.Single => caster with { AttackSingle = caster.AttackSingle.Add(effect.Amount) },
            EffectScope.Random => caster with { AttackRandom = caster.AttackRandom.Add(effect.Amount) },
            EffectScope.All    => caster with { AttackAll    = caster.AttackAll.Add(effect.Amount) },
            _ => caster, // Self は CardEffect.Normalize で弾かれる想定
        };
        var next = ReplaceActor(state, caster, updated);
        return (next, System.Array.Empty<BattleEvent>());
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyBlock(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        // 10.2.A は scope=Self のみ実装（敵の block も self、プレイヤーの defend も self）
        // scope=All / Random は 10.2.D で対応
        var updated = caster with { Block = caster.Block.Add(effect.Amount) };
        var next = ReplaceActor(state, caster, updated);
        var ev = new BattleEvent(
            BattleEventKind.GainBlock, Order: 0,
            CasterInstanceId: caster.InstanceId,
            TargetInstanceId: caster.InstanceId,
            Amount: effect.Amount);
        return (next, new[] { ev });
    }

    private static BattleState ReplaceActor(BattleState state, CombatActor before, CombatActor after)
    {
        if (before.Side == ActorSide.Ally)
        {
            int idx = state.Allies.IndexOf(before);
            return state with { Allies = state.Allies.SetItem(idx, after) };
        }
        else
        {
            int idx = state.Enemies.IndexOf(before);
            return state with { Enemies = state.Enemies.SetItem(idx, after) };
        }
    }
}
```

- [ ] **Step 4: 緑確認** — 5 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs tests/Core.Tests/Battle/Engine/EffectApplierTests.cs
git commit -m "feat(battle): add EffectApplier with attack/block support (Phase 10.2.A Task 16)"
```

---

## Task 17: DealDamageHelper（共通 internal helper, テストは resolver 経由）

**Files:**
- Create: `src/Core/Battle/Engine/DealDamageHelper.cs`

> 単独テストは書かず、Tasks 18-19 の resolver テストでカバー。

- [ ] **Step 1: 実装**

`src/Core/Battle/Engine/DealDamageHelper.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 1 体への DealDamage 計算ヘルパー。PlayerAttackingResolver / EnemyAttackingResolver から共有。
/// 親 spec §4-4 参照。10.2.B で力 / 脱力 / 脆弱の補正を本ヘルパー内に統合する。
/// </summary>
internal static class DealDamageHelper
{
    /// <summary>
    /// 1 回の攻撃を target に着弾させる。Block 消費 → HP 減算 → イベント発火。
    /// </summary>
    /// <returns>(更新後 target, イベント列, target が今この攻撃で死亡したか)</returns>
    public static (CombatActor updatedTarget, IReadOnlyList<BattleEvent> events, bool diedNow) Apply(
        CombatActor attacker, CombatActor target, int totalAttack, string scopeNote, int orderBase)
    {
        bool wasAlive = target.IsAlive;
        int preBlock = target.Block.RawTotal;
        int damage = System.Math.Max(0, totalAttack - preBlock);
        var newBlock = target.Block.Consume(totalAttack);
        var newHp = target.CurrentHp - damage;
        var updated = target with { Block = newBlock, CurrentHp = newHp };
        bool diedNow = wasAlive && !updated.IsAlive;

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.AttackFire, Order: orderBase,
                CasterInstanceId: attacker.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: totalAttack, Note: scopeNote),
            new(BattleEventKind.DealDamage, Order: orderBase + 1,
                CasterInstanceId: attacker.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: damage, Note: scopeNote),
        };
        if (diedNow)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ActorDeath, Order: orderBase + 2,
                CasterInstanceId: attacker.InstanceId,
                TargetInstanceId: target.InstanceId,
                Note: scopeNote));
        }
        return (updated, events, diedNow);
    }
}
```

- [ ] **Step 2: ビルド緑確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

- [ ] **Step 3: commit**

```bash
git add src/Core/Battle/Engine/DealDamageHelper.cs
git commit -m "feat(battle): add DealDamageHelper internal (Phase 10.2.A Task 17)"
```

---

## Task 18: PlayerAttackingResolver + テスト

**Files:**
- Create: `src/Core/Battle/Engine/PlayerAttackingResolver.cs`
- Create: `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverTests.cs`

- [ ] **Step 1: 失敗テストを書く**

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

public class PlayerAttackingResolverTests
{
    private static BattleState MakeState(
        CombatActor hero,
        params CombatActor[] enemies)
        => new(
            Turn: 1, Phase: BattlePhase.PlayerAttacking, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: enemies.ToImmutableArray(),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: "enc_test");

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void Single_attack_hits_target_enemy_only()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(6) };
        var goblin0 = BattleFixtures.Goblin(slotIndex: 0, hp: 20);
        var goblin1 = BattleFixtures.Goblin(slotIndex: 1, hp: 20);
        var s = MakeState(hero, goblin0, goblin1);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(14, next.Enemies[0].CurrentHp);
        Assert.Equal(20, next.Enemies[1].CurrentHp);
    }

    [Fact] public void All_attack_hits_every_enemy()
    {
        var hero = BattleFixtures.Hero() with { AttackAll = AttackPool.Empty.Add(4) };
        var s = MakeState(hero,
            BattleFixtures.Goblin(0, 20),
            BattleFixtures.Goblin(1, 20));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(16, next.Enemies[0].CurrentHp);
        Assert.Equal(16, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Random_attack_uses_rng_to_pick_target()
    {
        var hero = BattleFixtures.Hero() with { AttackRandom = AttackPool.Empty.Add(7) };
        var s = MakeState(hero,
            BattleFixtures.Goblin(0, 20),
            BattleFixtures.Goblin(1, 20));
        var rng = Rng(1); // 2 体中 index=1 を選択
        var (next, _) = PlayerAttackingResolver.Resolve(s, rng);
        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(13, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Block_absorbs_damage_partially()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(6) };
        var goblin = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(4) };
        var s = MakeState(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(18, next.Enemies[0].CurrentHp); // 20 - (6 - 4) = 18
        Assert.Equal(0, next.Enemies[0].Block.Sum); // Consume(6) from 4 → 0
    }

    [Fact] public void Block_fully_absorbs_damage()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(3) };
        var goblin = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(5) };
        var s = MakeState(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(2, next.Enemies[0].Block.Sum); // 5 - 3 = 2
    }

    [Fact] public void Lethal_attack_emits_ActorDeath()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(99) };
        var s = MakeState(hero, BattleFixtures.Goblin(0, 5));
        var (next, events) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.False(next.Enemies[0].IsAlive);
        Assert.Contains(events, e => e.Kind == BattleEventKind.ActorDeath);
    }

    [Fact] public void Pool_zero_does_not_emit_AttackFire()
    {
        var hero = BattleFixtures.Hero(); // 全 Pool 0
        var s = MakeState(hero, BattleFixtures.Goblin());
        var (_, events) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.AttackFire);
    }

    [Fact] public void Order_is_Single_then_Random_then_All()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(1),
            AttackRandom = AttackPool.Empty.Add(1),
            AttackAll    = AttackPool.Empty.Add(1),
        };
        var s = MakeState(hero,
            BattleFixtures.Goblin(0, 20),
            BattleFixtures.Goblin(1, 20));
        var (_, events) = PlayerAttackingResolver.Resolve(s, Rng(0));
        var fireEvents = events.Where(e => e.Kind == BattleEventKind.AttackFire).ToList();
        Assert.Equal("single", fireEvents[0].Note);
        Assert.Equal("random", fireEvents[1].Note);
        Assert.Equal("all", fireEvents[2].Note);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/PlayerAttackingResolver.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// PlayerAttacking フェーズ実行。各 ally の Single→Random→All の順で発射。
/// 10.2.A は ally = 主人公 1 体のみ。10.2.D で召喚を inside-out で含める。
/// 親 spec §4-4 参照。
/// </summary>
internal static class PlayerAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(BattleState state, IRng rng)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        var allies = state.Allies.OrderBy(a => a.SlotIndex).ToList();
        foreach (var ally in allies)
        {
            if (!ally.IsAlive) continue;

            // 1. Single
            if (ally.AttackSingle.Sum > 0 && state.TargetEnemyIndex is { } ti && ti < state.Enemies.Length)
            {
                var target = state.Enemies[ti];
                var (updated, evs, _) = DealDamageHelper.Apply(
                    ally, target, ally.AttackSingle.RawTotal, scopeNote: "single", orderBase: order);
                state = state with { Enemies = state.Enemies.SetItem(ti, updated) };
                events.AddRange(evs);
                order += evs.Count;
            }

            // 2. Random
            if (ally.AttackRandom.Sum > 0 && state.Enemies.Length > 0)
            {
                int idx = rng.NextInt(0, state.Enemies.Length); // 死亡敵含む（spec §4-4 仕様）
                var target = state.Enemies[idx];
                var (updated, evs, _) = DealDamageHelper.Apply(
                    ally, target, ally.AttackRandom.RawTotal, scopeNote: "random", orderBase: order);
                state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
                events.AddRange(evs);
                order += evs.Count;
            }

            // 3. All
            if (ally.AttackAll.Sum > 0)
            {
                for (int i = 0; i < state.Enemies.Length; i++)
                {
                    var target = state.Enemies[i];
                    var (updated, evs, _) = DealDamageHelper.Apply(
                        ally, target, ally.AttackAll.RawTotal, scopeNote: "all", orderBase: order);
                    state = state with { Enemies = state.Enemies.SetItem(i, updated) };
                    events.AddRange(evs);
                    order += evs.Count;
                }
            }
        }

        return (state, events);
    }
}
```

- [ ] **Step 4: 緑確認** — 8 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/PlayerAttackingResolver.cs tests/Core.Tests/Battle/Engine/PlayerAttackingResolverTests.cs
git commit -m "feat(battle): add PlayerAttackingResolver (Phase 10.2.A Task 18)"
```

---

## Task 19: EnemyAttackingResolver + テスト

**Files:**
- Create: `src/Core/Battle/Engine/EnemyAttackingResolver.cs`
- Create: `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EnemyAttackingResolverTests
{
    private static BattleState MakeState(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.EnemyAttacking, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    [Fact] public void Enemy_attack_scope_all_hits_hero()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = MakeState(hero, goblin);
        var cat = BattleFixtures.MinimalCatalog(
            enemies: new[] { BattleFixtures.GoblinDef(hp: 20, attack: 5) });
        var (next, events) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        Assert.Equal(65, next.Allies[0].CurrentHp); // 70 - 5
        Assert.Contains(events, e => e.Kind == BattleEventKind.DealDamage && e.Amount == 5);
    }

    [Fact] public void Per_effect_immediate_fire_with_two_attacks()
    {
        var twoHits = new EnemyDefinition(
            "twohit", "Two Hit", "img", 30, new EnemyPool(1, EnemyTier.Weak), "double",
            new[] {
                new MoveDefinition("double", MoveKind.Attack,
                    new[] {
                        new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3),
                        new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3),
                    },
                    "double")
            });
        var hero = BattleFixtures.Hero();
        var enemy = new CombatActor("e1", "twohit", ActorSide.Enemy, 0, 30, 30,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, "double");
        var s = MakeState(hero, enemy);
        var cat = BattleFixtures.MinimalCatalog(enemies: new[] { twoHits });
        var (next, events) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        // 2 回 × 3 ダメージ = 6
        Assert.Equal(64, next.Allies[0].CurrentHp);
        var dealCount = events.Count(e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(2, dealCount);
    }

    [Fact] public void Enemy_block_self_increments_own_block()
    {
        var defender = new EnemyDefinition(
            "defender", "Defender", "img", 30, new EnemyPool(1, EnemyTier.Weak), "guard",
            new[] {
                new MoveDefinition("guard", MoveKind.Defend,
                    new[] { new CardEffect("block", EffectScope.Self, null, 5) },
                    "guard")
            });
        var hero = BattleFixtures.Hero();
        var enemy = new CombatActor("e1", "defender", ActorSide.Enemy, 0, 30, 30,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, "guard");
        var s = MakeState(hero, enemy);
        var cat = BattleFixtures.MinimalCatalog(enemies: new[] { defender });
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        Assert.Equal(5, next.Enemies[0].Block.Sum);
    }

    [Fact] public void Enemy_transitions_to_NextMoveId()
    {
        var moveA = new MoveDefinition("a", MoveKind.Attack,
            new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 1) },
            "b");
        var moveB = new MoveDefinition("b", MoveKind.Attack,
            new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 1) },
            "a");
        var def = new EnemyDefinition(
            "alt", "Alt", "img", 30, new EnemyPool(1, EnemyTier.Weak), "a",
            new[] { moveA, moveB });
        var hero = BattleFixtures.Hero();
        var enemy = new CombatActor("e1", "alt", ActorSide.Enemy, 0, 30, 30,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, "a");
        var s = MakeState(hero, enemy);
        var cat = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        Assert.Equal("b", next.Enemies[0].CurrentMoveId);
    }

    [Fact] public void Dead_enemies_skip_action()
    {
        var hero = BattleFixtures.Hero();
        var dead = BattleFixtures.Goblin() with { CurrentHp = 0 };
        var s = MakeState(hero, dead);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, events) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.AttackFire);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/EnemyAttackingResolver.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// EnemyAttacking フェーズ実行。各生存敵の MoveDefinition.Effects を per-effect 即時発射し、
/// NextMoveId へ遷移する。親 spec §5-2-1 参照（敵 attack は per-effect 即時発射）。
/// </summary>
internal static class EnemyAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        var enemies = state.Enemies.OrderBy(e => e.SlotIndex).ToList();
        foreach (var enemy in enemies)
        {
            if (!enemy.IsAlive) continue;
            if (!catalog.TryGetEnemy(enemy.DefinitionId, out var def)) continue;
            var move = def.Moves.FirstOrDefault(m => m.Id == enemy.CurrentMoveId);
            if (move is null) continue;

            var currentEnemyState = state.Enemies.First(e => e.InstanceId == enemy.InstanceId);

            foreach (var eff in move.Effects)
            {
                if (eff.Action == "attack")
                {
                    // 敵 attack は scope=all 直書き運用。生存味方全員に着弾
                    foreach (var ally in state.Allies.Where(a => a.IsAlive).OrderBy(a => a.SlotIndex).ToList())
                    {
                        var (updated, evs, _) = DealDamageHelper.Apply(
                            currentEnemyState, ally, eff.Amount, scopeNote: "enemy_attack", orderBase: order);
                        state = state with
                        {
                            Allies = state.Allies.SetItem(state.Allies.IndexOf(ally), updated),
                        };
                        events.AddRange(evs);
                        order += evs.Count;
                    }
                }
                else if (eff.Action == "block")
                {
                    // 敵 move の block effect は scope=self を前提（10.2.A の制約）
                    var newEnemy = currentEnemyState with { Block = currentEnemyState.Block.Add(eff.Amount) };
                    int idx = state.Enemies.IndexOf(currentEnemyState);
                    state = state with { Enemies = state.Enemies.SetItem(idx, newEnemy) };
                    events.Add(new BattleEvent(
                        BattleEventKind.GainBlock, Order: order,
                        CasterInstanceId: currentEnemyState.InstanceId,
                        TargetInstanceId: currentEnemyState.InstanceId,
                        Amount: eff.Amount));
                    order += 1;
                    currentEnemyState = newEnemy;
                }
                // その他の action は 10.2.B 以降で対応 (no-op)
            }

            // NextMoveId へ遷移
            int enemyIdx = state.Enemies.IndexOf(currentEnemyState);
            if (enemyIdx >= 0)
            {
                var transitioned = state.Enemies[enemyIdx] with { CurrentMoveId = move.NextMoveId };
                state = state with { Enemies = state.Enemies.SetItem(enemyIdx, transitioned) };
            }
        }

        return (state, events);
    }
}
```

- [ ] **Step 4: 緑確認** — 5 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/EnemyAttackingResolver.cs tests/Core.Tests/Battle/Engine/EnemyAttackingResolverTests.cs
git commit -m "feat(battle): add EnemyAttackingResolver (Phase 10.2.A Task 19)"
```

---

## Task 20: TargetingAutoSwitch + テスト

**Files:**
- Create: `src/Core/Battle/Engine/TargetingAutoSwitch.cs`
- Create: `tests/Core.Tests/Battle/Engine/TargetingAutoSwitchTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TargetingAutoSwitchTests
{
    private static BattleState Make(int? tgtE, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerAttacking, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero()),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: tgtE,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    [Fact] public void Dead_target_switches_to_innermost_alive()
    {
        var dead = BattleFixtures.Goblin(slotIndex: 0) with { CurrentHp = 0 };
        var alive1 = BattleFixtures.Goblin(slotIndex: 1, hp: 10);
        var alive2 = BattleFixtures.Goblin(slotIndex: 2, hp: 10);
        var s = Make(0, dead, alive1, alive2);
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Equal(1, next.TargetEnemyIndex);
    }

    [Fact] public void All_dead_sets_target_to_null()
    {
        var dead0 = BattleFixtures.Goblin(0) with { CurrentHp = 0 };
        var dead1 = BattleFixtures.Goblin(1) with { CurrentHp = 0 };
        var s = Make(0, dead0, dead1);
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Null(next.TargetEnemyIndex);
    }

    [Fact] public void Live_target_unchanged()
    {
        var s = Make(0, BattleFixtures.Goblin(0, 20));
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Equal(0, next.TargetEnemyIndex);
    }

    [Fact] public void Null_target_remains_null()
    {
        var s = Make(null, BattleFixtures.Goblin(0, 20));
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Null(next.TargetEnemyIndex);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/TargetingAutoSwitch.cs`:

```csharp
using System.Linq;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 死亡判定後に対象を自動切替するヘルパー。最小スロット生存者へ。
/// 親 spec §7-4 参照。
/// </summary>
internal static class TargetingAutoSwitch
{
    public static BattleState Apply(BattleState state)
    {
        int? newE = state.TargetEnemyIndex;
        if (newE is { } ti)
        {
            if (ti < 0 || ti >= state.Enemies.Length || !state.Enemies[ti].IsAlive)
            {
                newE = state.Enemies
                    .Where(e => e.IsAlive)
                    .OrderBy(e => e.SlotIndex)
                    .Select(e => (int?)e.SlotIndex)
                    .FirstOrDefault();
            }
        }

        int? newA = state.TargetAllyIndex;
        if (newA is { } ai)
        {
            if (ai < 0 || ai >= state.Allies.Length || !state.Allies[ai].IsAlive)
            {
                newA = state.Allies
                    .Where(a => a.IsAlive)
                    .OrderBy(a => a.SlotIndex)
                    .Select(a => (int?)a.SlotIndex)
                    .FirstOrDefault();
            }
        }

        return state with { TargetEnemyIndex = newE, TargetAllyIndex = newA };
    }
}
```

- [ ] **Step 4: 緑確認** — 4 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/TargetingAutoSwitch.cs tests/Core.Tests/Battle/Engine/TargetingAutoSwitchTests.cs
git commit -m "feat(battle): add TargetingAutoSwitch helper (Phase 10.2.A Task 20)"
```

---

## Task 21: BattleEngine.Start + テスト

**Files:**
- Create: `src/Core/Battle/Engine/BattleEngine.cs`（Start メソッドのみ）
- Create: `tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineStartTests
{
    private static RunState MakeRun(params string[] deck)
    {
        // hero hp=70 / max=70 のシンプルなラン
        var deckArr = deck.Select(id => new CardInstance(id, false)).ToImmutableArray();
        return new RunState(
            SchemaVersion: RunState.CurrentSchemaVersion,
            CurrentAct: 1, CurrentNodeId: 0,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
            CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
            Deck: deckArr, Potions: ImmutableArray<string>.Empty, PotionSlotCount: 0,
            ActiveBattle: null, ActiveReward: null,
            EncounterQueueWeak: ImmutableArray<string>.Empty,
            EncounterQueueStrong: ImmutableArray<string>.Empty,
            EncounterQueueElite: ImmutableArray<string>.Empty,
            EncounterQueueBoss: ImmutableArray<string>.Empty,
            RewardRngState: new RoguelikeCardGame.Core.Rewards.RewardRngState(0, 0),
            ActiveMerchant: null, ActiveEvent: null,
            ActiveRestPending: false, ActiveRestCompleted: false,
            Relics: System.Array.Empty<string>(),
            PlaySeconds: 0, RngSeed: 1,
            SavedAtUtc: System.DateTimeOffset.UtcNow,
            Progress: RunProgress.InProgress,
            RunId: "run1", ActiveActStartRelicChoice: null,
            SeenCardBaseIds: ImmutableArray<string>.Empty,
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<RoguelikeCardGame.Core.Run.JourneyEntry>.Empty);
    }

    private static IRng Rng() => new FakeRng(new int[200], new double[0]);

    [Fact] public void Builds_hero_at_slot_zero_with_run_hp()
    {
        var run = MakeRun("strike", "defend");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal("hero", s.Allies[0].DefinitionId);
        Assert.Equal(0, s.Allies[0].SlotIndex);
        Assert.Equal(70, s.Allies[0].CurrentHp);
        Assert.Equal(70, s.Allies[0].MaxHp);
    }

    [Fact] public void Builds_enemies_from_encounter()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Single(s.Enemies);
        Assert.Equal("goblin", s.Enemies[0].DefinitionId);
        Assert.Equal("swing", s.Enemies[0].CurrentMoveId);
    }

    [Fact] public void Copies_deck_and_draws_five()
    {
        var run = MakeRun("strike", "strike", "strike", "strike", "strike", "strike", "strike", "defend");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(5, s.Hand.Length);
        Assert.Equal(3, s.DrawPile.Length); // 8 - 5
    }

    [Fact] public void Initial_state_is_PlayerInput_pending_turn1()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(BattlePhase.PlayerInput, s.Phase);
        Assert.Equal(BattleOutcome.Pending, s.Outcome);
        Assert.Equal(2, s.Turn); // Start で Turn=1 で初期化、TurnStartProcessor が +1 して 2
        // 注: spec §3-1 の処理フロー 7 「ターン 1 開始処理を実行」を入れた後 Turn は 2 になる。
        //     "Turn=1" を期待する場合は spec 修正が必要。本実装は「Start で Turn=0 → TurnStart で +1 → 1」の方針に統一。
    }

    [Fact] public void Initial_target_indices_are_zero()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(0, s.TargetAllyIndex);
        Assert.Equal(0, s.TargetEnemyIndex);
    }

    [Fact] public void Energy_initial_is_three()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(3, s.Energy);
        Assert.Equal(3, s.EnergyMax);
    }

    [Fact] public void EncounterId_set_correctly()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal("enc_test", s.EncounterId);
    }
}
```

> **注**: テスト `Initial_state_is_PlayerInput_pending_turn1` で Turn=2 を期待する理由 — `Start` 内で `Turn=0` で初期化 → `TurnStartProcessor.Process` 呼出で `Turn += 1` → 結果 `Turn=1`。Assert は `Turn==1` に修正する。テスト記述ミスのため Step 1 で `Assert.Equal(1, s.Turn)` に直す。

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装（Start のみ）**

`src/Core/Battle/Engine/BattleEngine.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// バトルエンジンの公開ファサード。`Start` / `PlayCard` / `EndTurn` / `Finalize` を提供。
/// 親 spec §3-§10 参照。
/// </summary>
public static partial class BattleEngine
{
    public const int InitialEnergy = 3;

    public static BattleState Start(
        RunState run, string encounterId, IRng rng, DataCatalog catalog)
    {
        if (!catalog.TryGetEncounter(encounterId, out var encounter))
            throw new System.InvalidOperationException($"encounter '{encounterId}' not found in catalog");

        // 1. 主人公 CombatActor 生成
        var hero = new CombatActor(
            InstanceId: "hero_inst", DefinitionId: "hero",
            Side: ActorSide.Ally, SlotIndex: 0,
            CurrentHp: run.CurrentHp, MaxHp: run.MaxHp,
            Block: BlockPool.Empty,
            AttackSingle: AttackPool.Empty,
            AttackRandom: AttackPool.Empty,
            AttackAll: AttackPool.Empty,
            CurrentMoveId: null);

        // 2. 敵 CombatActor 生成
        var enemiesBuilder = ImmutableArray.CreateBuilder<CombatActor>();
        for (int i = 0; i < encounter.EnemyIds.Count; i++)
        {
            var eid = encounter.EnemyIds[i];
            if (!catalog.TryGetEnemy(eid, out var def))
                throw new System.InvalidOperationException($"enemy '{eid}' not found in catalog");
            enemiesBuilder.Add(new CombatActor(
                InstanceId: $"enemy_inst_{i}", DefinitionId: eid,
                Side: ActorSide.Enemy, SlotIndex: i,
                CurrentHp: def.Hp, MaxHp: def.Hp,
                Block: BlockPool.Empty,
                AttackSingle: AttackPool.Empty,
                AttackRandom: AttackPool.Empty,
                AttackAll: AttackPool.Empty,
                CurrentMoveId: def.InitialMoveId));
        }

        // 3. Deck コピー & シャッフル → 山札
        var deckCards = run.Deck
            .Select((c, idx) => new BattleCardInstance($"card_inst_{idx}", c.Id, c.Upgraded, null))
            .ToArray();
        ShuffleInPlace(deckCards, rng);
        var drawPile = deckCards.ToImmutableArray();

        // 4. 初期 BattleState（Turn=0、TurnStartProcessor で +1 して Turn=1 へ）
        var initial = new BattleState(
            Turn: 0,
            Phase: BattlePhase.PlayerInput,
            Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: enemiesBuilder.ToImmutable(),
            TargetAllyIndex: 0,
            TargetEnemyIndex: enemiesBuilder.Count > 0 ? 0 : (int?)null,
            Energy: 0, EnergyMax: InitialEnergy,
            DrawPile: drawPile,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: encounterId);

        // 5. ターン 1 開始処理（5 ドロー、Energy=3、TurnStart イベント発火）
        var (afterTurnStart, _) = TurnStartProcessor.Process(initial, rng);
        return afterTurnStart;
    }

    private static void ShuffleInPlace(BattleCardInstance[] arr, IRng rng)
    {
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }
}
```

- [ ] **Step 4: テストの Turn assert を修正**

`BattleEngineStartTests.Initial_state_is_PlayerInput_pending_turn1` の `Assert.Equal(2, s.Turn)` を `Assert.Equal(1, s.Turn)` に直し、コメント削除。

- [ ] **Step 5: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEngineStartTests`
Expected: 7 passed

- [ ] **Step 6: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.cs tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs
git commit -m "feat(battle): add BattleEngine.Start (Phase 10.2.A Task 21)"
```

---

## Task 22: BattleEngine.PlayCard + テスト

**Files:**
- Create: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`（partial class）
- Create: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs`

- [ ] **Step 1: 失敗テストを書く**

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

public class BattleEnginePlayCardTests
{
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
            EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    [Fact] public void Pays_energy_cost()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand, energy: 3);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.Energy);
    }

    [Fact] public void Strike_adds_to_AttackSingle()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(6, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void Defend_adds_to_BlockPool()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("defend", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].Block.Sum);
    }

    [Fact] public void Played_card_moves_to_discard()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Empty(next.Hand);
        Assert.Single(next.DiscardPile);
        Assert.Equal("c1", next.DiscardPile[0].InstanceId);
    }

    [Fact] public void Throws_when_energy_insufficient()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand, energy: 0);
        var cat = BattleFixtures.MinimalCatalog();
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat));
    }

    [Fact] public void Throws_when_not_in_PlayerInput_phase()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand) with { Phase = BattlePhase.PlayerAttacking };
        var cat = BattleFixtures.MinimalCatalog();
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat));
    }

    [Fact] public void Emits_PlayCard_event_first()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (_, events) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(BattleEventKind.PlayCard, events[0].Kind);
        Assert.Equal("strike", events[0].CardId);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/BattleEngine.PlayCard.cs`:

```csharp
using System;
using System.Collections.Generic;
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

        // 10.2.A: コンボ軽減なし。CostOverride 優先 → 強化版 cost → 通常 cost
        int? cost = card.CostOverride ?? (card.IsUpgraded ? def.UpgradedCost ?? def.Cost : def.Cost);
        if (cost is null)
            throw new InvalidOperationException($"card '{def.Id}' is unplayable (cost=null)");
        if (state.Energy < cost.Value)
            throw new InvalidOperationException($"insufficient energy: have {state.Energy}, need {cost}");

        // 対象切替（10.2.A は基本機能のみ）
        var s = state with
        {
            Energy = state.Energy - cost.Value,
            TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
            TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
        };

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.PlayCard, Order: 0,
                CasterInstanceId: state.Allies[0].InstanceId,
                CardId: def.Id,
                Amount: cost.Value),
        };

        var caster = s.Allies[0]; // 10.2.A: caster = hero 固定
        int order = 1;
        foreach (var eff in def.Effects)
        {
            var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng);
            s = afterEffect;
            // events に order を振り直す
            foreach (var ev in evs)
            {
                events.Add(ev with { Order = order });
                order++;
            }
            // caster は Pool 加算で更新されるので再取得
            caster = s.Allies[0];
        }

        // カードを Hand → DiscardPile へ移動（10.2.A: exhaust/retain/Power/Unit 未対応）
        var newHand = s.Hand.RemoveAt(handIndex);
        var newDiscard = s.DiscardPile.Add(card);
        s = s with { Hand = newHand, DiscardPile = newDiscard };

        return (s, events);
    }
}
```

- [ ] **Step 4: 緑確認** — 7 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.PlayCard.cs tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs
git commit -m "feat(battle): add BattleEngine.PlayCard (Phase 10.2.A Task 22)"
```

---

## Task 23: BattleEngine.EndTurn + テスト

**Files:**
- Create: `src/Core/Battle/Engine/BattleEngine.EndTurn.cs`（partial class）
- Create: `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs`

- [ ] **Step 1: 失敗テストを書く**

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

public class BattleEngineEndTurnTests
{
    private static BattleState MakeState(
        CombatActor hero,
        ImmutableArray<CombatActor> enemies,
        ImmutableArray<BattleCardInstance>? draw = null)
        => new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: enemies,
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: draw ?? Enumerable.Range(0, 5)
                .Select(i => BattleFixtures.MakeBattleCard("strike", $"c{i}"))
                .ToImmutableArray(),
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: "enc_test");

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void All_enemies_dead_yields_Victory()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(99) };
        var s = MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin(0, 5)));
        var cat = BattleFixtures.MinimalCatalog();
        var (next, events) = BattleEngine.EndTurn(s, Rng(), cat);
        Assert.Equal(BattleOutcome.Victory, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.Contains(events, e => e.Kind == BattleEventKind.BattleEnd);
    }

    [Fact] public void Hero_killed_yields_Defeat()
    {
        var hero = BattleFixtures.Hero(hp: 2); // 2 HP
        var s = MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin()));
        var cat = BattleFixtures.MinimalCatalog(
            enemies: new[] { BattleFixtures.GoblinDef(hp: 20, attack: 5) });
        var (next, events) = BattleEngine.EndTurn(s, Rng(), cat);
        Assert.Equal(BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.Contains(events, e => e.Kind == BattleEventKind.BattleEnd);
    }

    [Fact] public void Continues_to_next_turn_when_neither_side_dies()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin(0, 50); // tough
        var s = MakeState(hero, ImmutableArray.Create(goblin));
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.EndTurn(s, Rng(), cat);
        Assert.Equal(BattlePhase.PlayerInput, next.Phase);
        Assert.Equal(BattleOutcome.Pending, next.Outcome);
        Assert.Equal(2, next.Turn);
        Assert.Equal(3, next.Energy); // refilled
    }

    [Fact] public void Hand_discarded_and_redrawn()
    {
        var hero = BattleFixtures.Hero();
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "h1"));
        var s = MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin(0, 50))) with
        {
            Hand = hand
        };
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.EndTurn(s, Rng(), cat);
        // h1 が捨てられ、5 枚新規ドロー
        Assert.Equal(5, next.Hand.Length);
        Assert.Contains(next.DiscardPile, c => c.InstanceId == "h1");
    }

    [Fact] public void Throws_when_not_PlayerInput()
    {
        var hero = BattleFixtures.Hero();
        var s = MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin())) with
        {
            Phase = BattlePhase.EnemyAttacking
        };
        var cat = BattleFixtures.MinimalCatalog();
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.EndTurn(s, Rng(), cat));
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/BattleEngine.EndTurn.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (BattleState, IReadOnlyList<BattleEvent>) EndTurn(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException($"EndTurn requires Phase=PlayerInput, got {state.Phase}");

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.EndTurn, Order: 0),
        };
        int order = 1;

        // 1. PlayerAttacking
        var s = state with { Phase = BattlePhase.PlayerAttacking };
        var (afterPA, evsPA) = PlayerAttackingResolver.Resolve(s, rng);
        s = afterPA;
        AddWithOrder(events, evsPA, ref order);

        // 2. 死亡判定 + 自動切替
        s = TargetingAutoSwitch.Apply(s);
        if (!s.Enemies.Any(e => e.IsAlive))
        {
            return Resolve(s, BattleOutcome.Victory, events, ref order);
        }

        // 3. EnemyAttacking
        s = s with { Phase = BattlePhase.EnemyAttacking };
        var (afterEA, evsEA) = EnemyAttackingResolver.Resolve(s, rng, catalog);
        s = afterEA;
        AddWithOrder(events, evsEA, ref order);

        // 4. 死亡判定 + 自動切替
        s = TargetingAutoSwitch.Apply(s);
        if (!s.Allies.Any(a => a.IsAlive))
        {
            return Resolve(s, BattleOutcome.Defeat, events, ref order);
        }

        // 5. ターン終了処理
        var (afterEnd, evsEnd) = TurnEndProcessor.Process(s);
        s = afterEnd;
        AddWithOrder(events, evsEnd, ref order);

        // 6. ターン開始処理
        var (afterStart, evsStart) = TurnStartProcessor.Process(s, rng);
        s = afterStart with { Phase = BattlePhase.PlayerInput };
        AddWithOrder(events, evsStart, ref order);

        return (s, events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState s, BattleOutcome outcome, List<BattleEvent> events, ref int order)
    {
        s = s with { Phase = BattlePhase.Resolved, Outcome = outcome };
        events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order, Note: outcome.ToString()));
        order++;
        return (s, events);
    }

    private static void AddWithOrder(List<BattleEvent> dest, IReadOnlyList<BattleEvent> src, ref int order)
    {
        foreach (var ev in src)
        {
            dest.Add(ev with { Order = order });
            order++;
        }
    }
}
```

- [ ] **Step 4: 緑確認** — 5 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.EndTurn.cs tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs
git commit -m "feat(battle): add BattleEngine.EndTurn (Phase 10.2.A Task 23)"
```

---

## Task 24: BattleEngine.Finalize + テスト

**Files:**
- Create: `src/Core/Battle/Engine/BattleEngine.Finalize.cs`（partial class）
- Create: `tests/Core.Tests/Battle/Engine/BattleEngineFinalizeTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineFinalizeTests
{
    private static RunState MakeRun(int hp = 70)
    {
        return new RunState(
            SchemaVersion: RunState.CurrentSchemaVersion,
            CurrentAct: 1, CurrentNodeId: 0,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
            CharacterId: "default", CurrentHp: hp, MaxHp: 70, Gold: 0,
            Deck: ImmutableArray.Create(new CardInstance("strike", false)),
            Potions: ImmutableArray<string>.Empty, PotionSlotCount: 0,
            ActiveBattle: null, ActiveReward: null,
            EncounterQueueWeak: ImmutableArray<string>.Empty,
            EncounterQueueStrong: ImmutableArray<string>.Empty,
            EncounterQueueElite: ImmutableArray<string>.Empty,
            EncounterQueueBoss: ImmutableArray<string>.Empty,
            RewardRngState: new RoguelikeCardGame.Core.Rewards.RewardRngState(0, 0),
            ActiveMerchant: null, ActiveEvent: null,
            ActiveRestPending: false, ActiveRestCompleted: false,
            Relics: System.Array.Empty<string>(),
            PlaySeconds: 0, RngSeed: 1,
            SavedAtUtc: System.DateTimeOffset.UtcNow,
            Progress: RunProgress.InProgress,
            RunId: "run1", ActiveActStartRelicChoice: null,
            SeenCardBaseIds: ImmutableArray<string>.Empty,
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<RoguelikeCardGame.Core.Run.JourneyEntry>.Empty);
    }

    private static BattleState MakeResolved(int finalHp, BattleOutcome outcome) => new(
        Turn: 3, Phase: BattlePhase.Resolved, Outcome: outcome,
        Allies: ImmutableArray.Create(BattleFixtures.Hero(hp: finalHp)),
        Enemies: ImmutableArray<CombatActor>.Empty,
        TargetAllyIndex: 0, TargetEnemyIndex: null,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    [Fact] public void Throws_when_battle_not_resolved()
    {
        var run = MakeRun();
        var bs = MakeResolved(50, BattleOutcome.Victory) with { Phase = BattlePhase.PlayerInput };
        Assert.Throws<System.InvalidOperationException>(() => BattleEngine.Finalize(bs, run));
    }

    [Fact] public void Victory_returns_run_with_updated_hp_and_progress_inprogress()
    {
        var run = MakeRun(hp: 70);
        var bs = MakeResolved(45, BattleOutcome.Victory);
        var (after, summary) = BattleEngine.Finalize(bs, run);
        Assert.Equal(45, after.CurrentHp);
        Assert.Equal(RunProgress.InProgress, after.Progress);
        Assert.Equal(45, summary.FinalHeroHp);
        Assert.Equal(BattleOutcome.Victory, summary.Outcome);
    }

    [Fact] public void Defeat_sets_progress_to_GameOver()
    {
        var run = MakeRun(hp: 70);
        var bs = MakeResolved(0, BattleOutcome.Defeat);
        var (after, summary) = BattleEngine.Finalize(bs, run);
        Assert.Equal(0, after.CurrentHp);
        Assert.Equal(RunProgress.GameOver, after.Progress);
        Assert.Equal(BattleOutcome.Defeat, summary.Outcome);
    }

    [Fact] public void Battle_deck_does_not_leak_into_run()
    {
        var run = MakeRun(hp: 70);
        // 戦闘内パイルに余分なカードを置いた resolved BattleState
        var bs = MakeResolved(50, BattleOutcome.Victory) with
        {
            DrawPile = ImmutableArray.Create(BattleFixtures.MakeBattleCard("garbage", "g1")),
        };
        var (after, _) = BattleEngine.Finalize(bs, run);
        Assert.Single(after.Deck); // 元の "strike" 1 枚だけ
        Assert.Equal("strike", after.Deck[0].Id);
    }

    [Fact] public void ActiveBattle_cleared_to_null()
    {
        // 注: 10.2.A 段階では RunState.ActiveBattle は BattlePlaceholderState 型なので、
        // Finalize は ActiveBattle を直接いじらない。Phase 10.5 の wire-up で対応。
        var run = MakeRun(hp: 70);
        var bs = MakeResolved(50, BattleOutcome.Victory);
        var (after, _) = BattleEngine.Finalize(bs, run);
        Assert.Null(after.ActiveBattle);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/BattleEngine.Finalize.cs`:

```csharp
using System;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (RunState, BattleSummary) Finalize(BattleState state, RunState before)
    {
        if (state.Phase != BattlePhase.Resolved)
            throw new InvalidOperationException($"Finalize requires Phase=Resolved, got {state.Phase}");

        int finalHp = state.Allies[0].CurrentHp;

        var after = before with
        {
            CurrentHp = finalHp,
            ActiveBattle = null, // 戦闘終了 → 呼び出し側で次の遷移を決定
            Progress = state.Outcome == BattleOutcome.Defeat
                ? RunProgress.GameOver
                : before.Progress,
        };

        var summary = new BattleSummary(
            FinalHeroHp: finalHp,
            Outcome: state.Outcome,
            EncounterId: state.EncounterId);

        return (after, summary);
    }
}
```

- [ ] **Step 4: 緑確認** — 5 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.Finalize.cs tests/Core.Tests/Battle/Engine/BattleEngineFinalizeTests.cs
git commit -m "feat(battle): add BattleEngine.Finalize (Phase 10.2.A Task 24)"
```

---

## Task 25: BattleDeterminismTests（end-to-end 決定論検証）

**Files:**
- Create: `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs`

> 同 seed + 同 input 列で State / Events 完全一致を検証。

- [ ] **Step 1: テストを書く**

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleDeterminismTests
{
    private static RunState MakeRun() => new(
        SchemaVersion: RunState.CurrentSchemaVersion,
        CurrentAct: 1, CurrentNodeId: 0,
        VisitedNodeIds: ImmutableArray<int>.Empty,
        UnknownResolutions: ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
        CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
        Deck: Enumerable.Range(0, 10).Select(i => new CardInstance("strike", false)).ToImmutableArray(),
        Potions: ImmutableArray<string>.Empty, PotionSlotCount: 0,
        ActiveBattle: null, ActiveReward: null,
        EncounterQueueWeak: ImmutableArray<string>.Empty,
        EncounterQueueStrong: ImmutableArray<string>.Empty,
        EncounterQueueElite: ImmutableArray<string>.Empty,
        EncounterQueueBoss: ImmutableArray<string>.Empty,
        RewardRngState: new RoguelikeCardGame.Core.Rewards.RewardRngState(0, 0),
        ActiveMerchant: null, ActiveEvent: null,
        ActiveRestPending: false, ActiveRestCompleted: false,
        Relics: System.Array.Empty<string>(),
        PlaySeconds: 0, RngSeed: 1,
        SavedAtUtc: System.DateTimeOffset.UtcNow,
        Progress: RunProgress.InProgress,
        RunId: "run1", ActiveActStartRelicChoice: null,
        SeenCardBaseIds: ImmutableArray<string>.Empty,
        AcquiredRelicIds: ImmutableArray<string>.Empty,
        AcquiredPotionIds: ImmutableArray<string>.Empty,
        EncounteredEnemyIds: ImmutableArray<string>.Empty,
        JourneyLog: ImmutableArray<RoguelikeCardGame.Core.Run.JourneyEntry>.Empty);

    private static (BattleState, System.Collections.Generic.List<BattleEvent>) RunBattle(int seed)
    {
        var rng = new SequentialRng((ulong)seed);
        var run = MakeRun();
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", rng, cat);
        var allEvents = new System.Collections.Generic.List<BattleEvent>();

        // 1 ターン目：先頭の strike を打って EndTurn
        var (s2, evs1) = BattleEngine.PlayCard(s, 0, 0, 0, rng, cat);
        allEvents.AddRange(evs1);
        var (s3, evs2) = BattleEngine.EndTurn(s2, rng, cat);
        allEvents.AddRange(evs2);
        return (s3, allEvents);
    }

    [Fact] public void Same_seed_same_inputs_yields_identical_state()
    {
        var (a, _) = RunBattle(seed: 42);
        var (b, _) = RunBattle(seed: 42);
        Assert.Equal(a, b);
    }

    [Fact] public void Same_seed_same_inputs_yields_identical_events()
    {
        var (_, ea) = RunBattle(seed: 42);
        var (_, eb) = RunBattle(seed: 42);
        Assert.Equal(ea, eb);
    }
}
```

- [ ] **Step 2: 緑確認** — 2 passed

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs
git commit -m "test(battle): add Battle determinism tests (Phase 10.2.A Task 25)"
```

---

## Task 26: 親 spec への補記反映

**Files:**
- Modify: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`

10.2.A spec §8 の 6 項目を親 spec の該当章に反映。

- [ ] **Step 1: 補記項目 1（旧 BattleState → BattlePlaceholderState リネーム）**

親 spec §3-1 の冒頭または注記に追記:

```markdown
> **Phase 10.2.A 補記**: 旧 `Core.Battle.BattleState`（placeholder, EncounterId+Enemies+Outcome）は
> `BattlePlaceholderState` にリネームし、新 `Core.Battle.State.BattleState` (本章定義) との型衝突を回避する。
> Phase 10.5 cleanup で `BattlePlaceholder` 一式を削除し、`RunState.ActiveBattle` を新 BattleState? に切替する
> （save schema v8 マイグレーション同時導入）。
```

- [ ] **Step 2: 補記項目 2（BattleCardInstance 命名）**

親 spec §3-4 に追記:

```markdown
> **Phase 10.2.A 補記**: 親 spec の `CardInstance` 型は実装上 `BattleCardInstance`（`src/Core/Battle/State/`）として
> 新設する。`RunState.Deck` 用の `Cards.CardInstance` (Id+Upgraded のみ) とは別 record。`StartBattle` 時に変換される。
```

- [ ] **Step 3: 補記項目 3（AttackPool / BlockPool 暫定 API）**

親 spec §3-3 末尾に追記:

```markdown
> **Phase 10.2.A 補記**: 10.2.A では `RawTotal` プロパティのみ提供（遡及計算なし）。
> 10.2.B で `Display(strength, weak)` / `Display(dexterity)` / `Consume(incomingAttack, dexterity)` を追加し、
> `RawTotal` は internal な debug プロパティとして残す（API 変更を最小化）。
```

- [ ] **Step 4: 補記項目 4（BattleOutcome.Defeat 追加）**

親 spec §3-1 / §4-7 / §10-4 のいずれか妥当な位置に追記:

```markdown
> **Phase 10.2.A 補記**: 旧 `BattleOutcome { Pending, Victory }` は Phase 10.2.A で `Defeat = 2` を追加し、
> `Pending = 0, Victory = 1, Defeat = 2` の 3 値とする。Phase 5 placeholder では Defeat 経路がなかったが、
> ソロモード戦闘敗北を `RunProgress.GameOver` へ橋渡しするために必要。
```

- [ ] **Step 5: 補記項目 5（EffectApplier incremental）**

親 spec §5-2 or §5-1 末尾に追記:

```markdown
> **Phase 10.2.A 補記**: `EffectApplier.Apply` は incremental 実装方針を採用。
> 10.2.A は `attack` / `block` のみ対応、その他 action は no-op + イベントなし。
> 10.2.B〜E で対応 action を段階的に増やす（buff/debuff → heal/draw/discard/upgrade/exhaust*/retainSelf/gainEnergy → summon → relic/potion トリガー）。
> 各 phase で「未実装 action は no-op」の方針を維持し、データ層と実装層の段階的拡張を許容する。
```

- [ ] **Step 6: 補記項目 6（BattleEvent Core 型分離）**

親 spec §9-7 冒頭または末尾に追記:

```markdown
> **Phase 10.2.A 補記**: 親 spec §9-7 は `BattleEventDto` のみ定義していたが、Core 側に
> `BattleEvent` record + `BattleEventKind` enum を `src/Core/Battle/Events/` に新設する。
> Phase 10.3 で `BattleEvent` → `BattleEventDto` への変換層が追加される。
> 10.2.A の `BattleEventKind` は 9 種（BattleStart / TurnStart / PlayCard / AttackFire / DealDamage /
> GainBlock / ActorDeath / EndTurn / BattleEnd）、後続 phase で追加していく。
```

- [ ] **Step 7: 緑確認 + commit**

Run: `dotnet build && dotnet test`
Expected: 警告 0 / エラー 0、全テスト緑

```bash
git add docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md
git commit -m "docs(spec): amend Phase 10 spec for 10.2.A decisions (Task 26)"
```

---

## Task 27: 完了タグ作成と push

**Files:** なし（git tag 操作のみ）

- [ ] **Step 1: 最終ビルド・テスト確認**

```bash
dotnet build
dotnet test
```

Expected: 警告 0 / エラー 0、全テスト緑

- [ ] **Step 2: タグ作成**

```bash
git tag -a phase10-2A-complete -m "Phase 10.2.A — Core バトル基盤スケルトン 完了

新 BattleState データモデル / BattleEngine 4 公開 API
(Start/PlayCard/EndTurn/Finalize) / attack/block の 2 effect /
Phase 進行 / Victory & Defeat Outcome / BattleEvent 発火基盤を導入。
旧 BattleState は BattlePlaceholderState にリネーム。
NodeEffectResolver wire-up は Phase 10.3 / BattlePlaceholder 削除は Phase 10.5。"
```

- [ ] **Step 3: push**

```bash
git push origin master
git push origin phase10-2A-complete
```

- [ ] **Step 4: 完了確認**

```bash
git log -1 --oneline
git tag --list "phase10-2A-*"
```

Expected: 直近 commit が Task 26 の docs commit、タグ `phase10-2A-complete` が一覧に出る

---

## 完了後の状態（Phase 10.2.A 完了時）

- データモデル整備済み: `BattleState` / `CombatActor` / `AttackPool` / `BlockPool` / `BattleCardInstance` / `BattlePhase` / `BattleOutcome`(+Defeat) / `ActorSide` / `BattleEvent` / `BattleEventKind`
- `BattleEngine` 4 公開 API が `attack` / `block` の 2 effect で動作
- xUnit で Victory / Defeat 両経路の 1 戦闘が完走
- 旧 `BattleState` は `BattlePlaceholderState` にリネーム済み
- 既存ゲームフロー（`BattlePlaceholder` 経由）は無傷
- 親 spec が新方針に合わせて補記済み
- `phase10-2A-complete` タグ push 済み

## 次フェーズ（Phase 10.2.B）への引き継ぎ

- AttackPool に `Display(strength, weak)` を追加
- BlockPool に `Display(dexterity)` / `Consume(incoming, dex)` を追加
- 6 種 status を `src/Core/Battle/Statuses/StatusDefinition.cs` に静的リスト化
- `CombatActor` に `Statuses` フィールド追加
- `EffectApplier.Apply` で `buff` / `debuff` action 対応
- `TurnStartProcessor` でターン開始 tick（毒・状態異常 −1）
- `PlayerAttackingResolver` / `EnemyAttackingResolver` の `DealDamageHelper` で力 / 脱力 / 脆弱補正
- `omnistrike` 合算発射ロジック
- 親 spec §2-6 / §4-2 / §5-2 / §5-3 を実装に対応
