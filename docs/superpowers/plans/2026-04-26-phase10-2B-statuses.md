# Phase 10.2.B — 状態異常 + 遡及計算 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 10.2 (Core バトル本体) の 2 段階目として、6 種の状態異常 (strength / dexterity / vulnerable / weak / omnistrike / poison) を導入する。`AttackPool.Display(str, weak)` / `AttackPool.operator +` / `BlockPool.Display(dex)` / `BlockPool.Consume(in, dex)` の遡及計算 API、`buff` / `debuff` action（4 scope）、ターン開始 tick（毒ダメージ + countdown）、omnistrike 合算発射、`DealDamageHelper` への str/weak/vuln/dex 補正統合を完成させる。

**Architecture:** 10.2.A の `BattleEngine` 4 公開 API シグネチャは不変。内部の `EffectApplier` / `DealDamageHelper` / `TurnStartProcessor` / `PlayerAttackingResolver` を拡張し、`CombatActor` に `Statuses: ImmutableDictionary<string,int>` フィールドを追加。`StatusDefinition` は静的リストとして `src/Core/Battle/Statuses/` に新設（10.2.A で空フォルダ準備済み）。memory feedback の 2 ルール（`BattleOutcome` fully qualified / `state.Allies`/`state.Enemies` 書き戻しは InstanceId 検索）を全 loop 箇所で遵守。10.2.A の `EffectApplier.ReplaceActor` の `IndexOf` ベース latent bug も同 phase で根治。

**Tech Stack:** C# .NET 10 / xUnit / `System.Collections.Immutable` / `System.Text.Json`

**前提:**
- Phase 10.2.A が master にマージ済み（`phase10-2A-complete` タグ + `aba890b` follow-up fix）
- 開始時点で `dotnet build` 0 警告 0 エラー、`dotnet test` 全件緑（Core 693 件 + Server 168 件）

**完了判定（spec §「完了判定」と同期）:**
- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑
- `CombatActor.Statuses` / `GetStatus(id)` 動作
- `StatusDefinition.All` 6 種が正しい属性で存在
- `AttackPool.Display(str, weak)` / `AttackPool.operator +` / `BlockPool.Display(dex)` / `BlockPool.Consume(in, dex)` 動作
- `EffectApplier` が `buff` / `debuff` を Self / Single / Random / All の 4 scope で処理し、ApplyStatus / RemoveStatus event を発火
- `EffectApplier.ReplaceActor` が InstanceId 検索化（10.2.A の latent bug 根治）
- `DealDamageHelper.Apply` が str / weak / vuln / dex の補正を統合
- `TurnStartProcessor` が tick（毒ダメージ → countdown）を実行し、毒死で Outcome 確定
- `BattleEngine.EndTurn` が TurnStart 後の Outcome 確定時に Phase 上書きをスキップ
- `PlayerAttackingResolver` が omnistrike 持ち ally で `Single + Random + All` 合算発射
- `BattleEventKind` が 12 値（10.2.A 9 + 10.2.B 3）
- 既存 `BattlePlaceholder` 経由のフロー無傷（手動プレイ確認）
- 親 spec §3-2 / §3-3 / §4-2 / §4-4 / §5-1 / §9-7 に補記済み
- ブランチに `phase10-2B-complete` タグを切り origin に push

---

## File Structure

| ファイル | 役割 | 操作 |
|---|---|---|
| `src/Core/Battle/Statuses/StatusKind.cs` | enum Buff / Debuff | **新規** |
| `src/Core/Battle/Statuses/StatusTickDirection.cs` | enum None / Decrement | **新規** |
| `src/Core/Battle/Statuses/StatusDefinition.cs` | record + static `All` 6 種 + `Get(id)` | **新規** |
| `src/Core/Battle/Events/BattleEventKind.cs` | +ApplyStatus / RemoveStatus / PoisonTick | 修正 |
| `src/Core/Battle/State/AttackPool.cs` | +Display(str, weak) / +operator + / RawTotal を internal | 修正 |
| `src/Core/Battle/State/BlockPool.cs` | +Display(dex) / Consume(in, dex) / RawTotal を internal / 旧 Consume(int) 削除 | 修正 |
| `src/Core/Battle/State/CombatActor.cs` | +Statuses フィールド + GetStatus(id) 便宜プロパティ | 修正 |
| `src/Core/Battle/Engine/EffectApplier.cs` | +buff / debuff action 4 scope / ReplaceActor を InstanceId 検索化 | 修正 |
| `src/Core/Battle/Engine/DealDamageHelper.cs` | シグネチャ変更（baseSum, addCount）+ str/weak/vuln/dex 補正統合 | 修正 |
| `src/Core/Battle/Engine/PlayerAttackingResolver.cs` | omnistrike 合算発射 + DealDamageHelper シグネチャ追従 + InstanceId 検索化 | 修正 |
| `src/Core/Battle/Engine/EnemyAttackingResolver.cs` | DealDamageHelper シグネチャ追従 | 修正 |
| `src/Core/Battle/Engine/TurnStartProcessor.cs` | +tick（毒ダメージ → countdown）+ 死亡判定で Outcome 確定 | 修正 |
| `src/Core/Battle/Engine/BattleEngine.cs` | Start で hero / enemies の Statuses=Empty 初期化 | 修正 |
| `src/Core/Battle/Engine/BattleEngine.EndTurn.cs` | TurnStart 後 Outcome 確定時に Phase 上書きをスキップ | 修正 |
| `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` | Statuses=Empty 初期化 + WithStatus / Strength / Vulnerable 等 | 修正 |
| `tests/Core.Tests/Battle/Statuses/StatusKindTests.cs` | enum 値検証 | **新規** |
| `tests/Core.Tests/Battle/Statuses/StatusTickDirectionTests.cs` | enum 値検証 | **新規** |
| `tests/Core.Tests/Battle/Statuses/StatusDefinitionTests.cs` | 6 種の存在 / 属性 / Get() | **新規** |
| `tests/Core.Tests/Battle/Events/BattleEventKindTests.cs` | 12 値検証 | 修正 |
| `tests/Core.Tests/Battle/State/AttackPoolTests.cs` | +Display / +operator | 修正 |
| `tests/Core.Tests/Battle/State/BlockPoolTests.cs` | +Display / +Consume(in, dex) / 旧 Consume 削除 | 修正 |
| `tests/Core.Tests/Battle/State/CombatActorTests.cs` | +Statuses + GetStatus | 修正 |
| `tests/Core.Tests/Battle/Engine/DealDamageHelperTests.cs` | str / weak / vuln / dex 各補正 / 組み合わせ / 死亡判定 | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierBuffDebuffTests.cs` | 4 scope × 重ね掛け / event 発火 | **新規** |
| `tests/Core.Tests/Battle/Engine/EffectApplierReplaceActorInstanceIdTests.cs` | latent bug 回帰防止 | **新規** |
| `tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs` | 毒ダメージ / countdown / 毒死で Outcome 確定 | **新規** |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOmnistrikeTests.cs` | 合算発射 / Empty で発射なし / combined.AddCount | **新規** |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverStatusTests.cs` | str / weak / vuln / dex を resolver 経由で確認 | **新規** |
| `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverStatusTests.cs` | 敵側 str / weak / 受け側 vuln / dex | **新規** |
| `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs` | +TurnStart 中の毒死で Outcome 確定パス | 修正 |
| `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs` | status 含む 1 戦闘で seed 同一 → 一致 | 修正 |
| `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverTests.cs` | DealDamageHelper シグネチャ追従の fixture 整合 | 修正 |
| `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverTests.cs` | 同上 | 修正 |
| `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs` | Statuses 初期化 fixture 整合 | 修正 |
| `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` | 親 spec §3-2 / §3-3 / §4-2 / §4-4 / §5-1 / §9-7 に補記 | 修正 |

---

## Task 1: StatusKind enum + tests

**Files:**
- Create: `src/Core/Battle/Statuses/StatusKind.cs`
- Create: `tests/Core.Tests/Battle/Statuses/StatusKindTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Statuses/StatusKindTests.cs`:

```csharp
using RoguelikeCardGame.Core.Battle.Statuses;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Statuses;

public class StatusKindTests
{
    [Fact] public void Buff_value_is_zero()   => Assert.Equal(0, (int)StatusKind.Buff);
    [Fact] public void Debuff_value_is_one()  => Assert.Equal(1, (int)StatusKind.Debuff);
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~StatusKindTests`
Expected: build error（型未定義）

- [ ] **Step 3: 実装**

`src/Core/Battle/Statuses/StatusKind.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>状態異常の種別。Buff = 自陣強化、Debuff = 相手弱体化。</summary>
public enum StatusKind
{
    Buff   = 0,
    Debuff = 1,
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~StatusKindTests`
Expected: 2 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Statuses/StatusKind.cs tests/Core.Tests/Battle/Statuses/StatusKindTests.cs
git commit -m "feat(battle): add StatusKind enum (Phase 10.2.B Task 1)"
```

---

## Task 2: StatusTickDirection enum + tests

**Files:**
- Create: `src/Core/Battle/Statuses/StatusTickDirection.cs`
- Create: `tests/Core.Tests/Battle/Statuses/StatusTickDirectionTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Statuses/StatusTickDirectionTests.cs`:

```csharp
using RoguelikeCardGame.Core.Battle.Statuses;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Statuses;

public class StatusTickDirectionTests
{
    [Fact] public void None_value_is_zero()      => Assert.Equal(0, (int)StatusTickDirection.None);
    [Fact] public void Decrement_value_is_one()  => Assert.Equal(1, (int)StatusTickDirection.Decrement);
}
```

- [ ] **Step 2: 失敗確認** — build error

- [ ] **Step 3: 実装**

`src/Core/Battle/Statuses/StatusTickDirection.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>ターン開始 tick 時の amount 減衰方向。strength / dexterity は None、それ以外は Decrement。</summary>
public enum StatusTickDirection
{
    None      = 0,
    Decrement = 1,
}
```

- [ ] **Step 4: 緑確認** — 2 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Statuses/StatusTickDirection.cs tests/Core.Tests/Battle/Statuses/StatusTickDirectionTests.cs
git commit -m "feat(battle): add StatusTickDirection enum (Phase 10.2.B Task 2)"
```

---

## Task 3: StatusDefinition record + static `All` + tests

**Files:**
- Create: `src/Core/Battle/Statuses/StatusDefinition.cs`
- Create: `tests/Core.Tests/Battle/Statuses/StatusDefinitionTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Statuses/StatusDefinitionTests.cs`:

```csharp
using System.Linq;
using RoguelikeCardGame.Core.Battle.Statuses;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Statuses;

public class StatusDefinitionTests
{
    [Fact] public void All_contains_six_statuses()
    {
        Assert.Equal(6, StatusDefinition.All.Count);
        var ids = StatusDefinition.All.Select(s => s.Id).ToHashSet();
        Assert.Contains("strength",   ids);
        Assert.Contains("dexterity",  ids);
        Assert.Contains("omnistrike", ids);
        Assert.Contains("vulnerable", ids);
        Assert.Contains("weak",       ids);
        Assert.Contains("poison",     ids);
    }

    [Fact] public void Strength_is_permanent_buff()
    {
        var s = StatusDefinition.Get("strength");
        Assert.Equal(StatusKind.Buff, s.Kind);
        Assert.True(s.IsPermanent);
        Assert.Equal(StatusTickDirection.None, s.TickDirection);
    }

    [Fact] public void Dexterity_is_permanent_buff()
    {
        var s = StatusDefinition.Get("dexterity");
        Assert.Equal(StatusKind.Buff, s.Kind);
        Assert.True(s.IsPermanent);
        Assert.Equal(StatusTickDirection.None, s.TickDirection);
    }

    [Fact] public void Omnistrike_is_decrementing_buff()
    {
        var s = StatusDefinition.Get("omnistrike");
        Assert.Equal(StatusKind.Buff, s.Kind);
        Assert.False(s.IsPermanent);
        Assert.Equal(StatusTickDirection.Decrement, s.TickDirection);
    }

    [Fact] public void Vulnerable_is_decrementing_debuff()
    {
        var s = StatusDefinition.Get("vulnerable");
        Assert.Equal(StatusKind.Debuff, s.Kind);
        Assert.False(s.IsPermanent);
        Assert.Equal(StatusTickDirection.Decrement, s.TickDirection);
    }

    [Fact] public void Weak_is_decrementing_debuff()
    {
        var s = StatusDefinition.Get("weak");
        Assert.Equal(StatusKind.Debuff, s.Kind);
        Assert.False(s.IsPermanent);
        Assert.Equal(StatusTickDirection.Decrement, s.TickDirection);
    }

    [Fact] public void Poison_is_decrementing_debuff()
    {
        var s = StatusDefinition.Get("poison");
        Assert.Equal(StatusKind.Debuff, s.Kind);
        Assert.False(s.IsPermanent);
        Assert.Equal(StatusTickDirection.Decrement, s.TickDirection);
    }

    [Fact] public void Get_unknown_throws()
    {
        Assert.Throws<System.InvalidOperationException>(() => StatusDefinition.Get("unknown"));
    }
}
```

- [ ] **Step 2: 失敗確認** — build error（型未定義）

- [ ] **Step 3: 実装**

`src/Core/Battle/Statuses/StatusDefinition.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>
/// 状態異常の静的定義。Phase 10 では JSON 化せず C# の static リストで保持。
/// 親 spec §2-6 / Phase 10.2.B spec §2-2 参照。
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

- [ ] **Step 4: 緑確認** — 8 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Statuses/StatusDefinition.cs tests/Core.Tests/Battle/Statuses/StatusDefinitionTests.cs
git commit -m "feat(battle): add StatusDefinition with 6 statuses (Phase 10.2.B Task 3)"
```

---

## Task 4: BattleEventKind に ApplyStatus / RemoveStatus / PoisonTick 追加

**Files:**
- Modify: `src/Core/Battle/Events/BattleEventKind.cs`
- Modify: `tests/Core.Tests/Battle/Events/BattleEventKindTests.cs`

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Battle/Events/BattleEventKindTests.cs` の最後に追記（既存 9 値テストは残す）:

```csharp
[Fact] public void ApplyStatus_value_is_nine()    => Assert.Equal(9,  (int)BattleEventKind.ApplyStatus);
[Fact] public void RemoveStatus_value_is_ten()    => Assert.Equal(10, (int)BattleEventKind.RemoveStatus);
[Fact] public void PoisonTick_value_is_eleven()   => Assert.Equal(11, (int)BattleEventKind.PoisonTick);
```

- [ ] **Step 2: 失敗確認** — build error（enum 値未定義）

- [ ] **Step 3: 実装**

`src/Core/Battle/Events/BattleEventKind.cs`:

```csharp
namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中に発火されるイベント種別。Phase 10.2.B で 12 種に拡張。
/// 後続 phase で Summon / Exhaust / Upgrade / RelicTrigger / UsePotion 等を追加していく。
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
    ApplyStatus   = 9,    // 10.2.B 新規（buff/debuff 付与・重ね掛け）
    RemoveStatus  = 10,   // 10.2.B 新規（countdown で 0 → 削除）
    PoisonTick    = 11,   // 10.2.B 新規（毒ダメージ）
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleEventKindTests`
Expected: 12 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Events/BattleEventKind.cs tests/Core.Tests/Battle/Events/BattleEventKindTests.cs
git commit -m "feat(battle): extend BattleEventKind with status events (Phase 10.2.B Task 4)"
```

---

## Task 5: AttackPool に Display(str, weak) と + operator 追加

**Files:**
- Modify: `src/Core/Battle/State/AttackPool.cs`
- Modify: `tests/Core.Tests/Battle/State/AttackPoolTests.cs`

> 注: 本タスクでは `RawTotal` は public のまま。internal 化は Task 11（DealDamageHelper シグネチャ変更時）でまとめて実施。

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Battle/State/AttackPoolTests.cs` の最後に追記:

```csharp
[Fact] public void Display_no_status_returns_sum()
{
    var p = AttackPool.Empty.Add(5).Add(3);
    Assert.Equal(8, p.Display(strength: 0, weak: 0));
}

[Fact] public void Display_strength_adds_per_addcount()
{
    // Sum=8, AddCount=2, strength=3 → 8 + 2*3 = 14
    var p = AttackPool.Empty.Add(5).Add(3);
    Assert.Equal(14, p.Display(strength: 3, weak: 0));
}

[Fact] public void Display_weak_applies_three_quarters_floor()
{
    // Sum=10, AddCount=1, strength=0, weak=1 → floor(10 * 0.75) = 7
    var p = AttackPool.Empty.Add(10);
    Assert.Equal(7, p.Display(strength: 0, weak: 1));
}

[Fact] public void Display_weak_with_strength()
{
    // Sum=8, AddCount=2, strength=3 → 8+6 = 14、weak: floor(14 * 0.75) = 10
    var p = AttackPool.Empty.Add(5).Add(3);
    Assert.Equal(10, p.Display(strength: 3, weak: 1));
}

[Fact] public void Display_zero_when_empty()
{
    Assert.Equal(0, AttackPool.Empty.Display(strength: 5, weak: 0));
}

[Fact] public void Operator_plus_sums_both_fields()
{
    var a = AttackPool.Empty.Add(5).Add(3);   // Sum=8, AddCount=2
    var b = AttackPool.Empty.Add(2);          // Sum=2, AddCount=1
    var c = a + b;                            // Sum=10, AddCount=3
    Assert.Equal(10, c.Sum);
    Assert.Equal(3, c.AddCount);
}

[Fact] public void Operator_plus_with_empty_returns_other()
{
    var a = AttackPool.Empty.Add(5);
    var c = a + AttackPool.Empty;
    Assert.Equal(a, c);
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~AttackPoolTests`
Expected: build error（`Display` / `+` operator 未定義）

- [ ] **Step 3: 実装**

`src/Core/Battle/State/AttackPool.cs` を以下に変更:

```csharp
namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// 攻撃値の蓄積プール。10.2.B で Display(str, weak) と + operator を追加。
/// 親 spec §3-3 / Phase 10.2.B spec §2-4 参照。
/// </summary>
public readonly record struct AttackPool(int Sum, int AddCount)
{
    public static AttackPool Empty => new(0, 0);

    public AttackPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>omnistrike 合算用。Sum / AddCount をペアで加算。</summary>
    public static AttackPool operator +(AttackPool a, AttackPool b) =>
        new(a.Sum + b.Sum, a.AddCount + b.AddCount);

    /// <summary>
    /// 力バフを遡及反映（×AddCount）し、脱力 weak > 0 で 0.75 倍切捨。
    /// 整数演算で誤差なし。long キャストで AddCount × strength のオーバーフロー防御。
    /// </summary>
    public int Display(int strength, int weak)
    {
        long boosted = (long)Sum + (long)AddCount * strength;
        return weak > 0 ? (int)(boosted * 3 / 4) : (int)boosted;
    }

    /// <summary>10.2.A の暫定 API。Task 11 で internal 化。</summary>
    public int RawTotal => Sum;
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~AttackPoolTests`
Expected: 既存 4 + 新規 7 = 11 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/AttackPool.cs tests/Core.Tests/Battle/State/AttackPoolTests.cs
git commit -m "feat(battle): add AttackPool.Display + operator+ (Phase 10.2.B Task 5)"
```

---

## Task 6: BlockPool に Display(dex) を追加（旧 Consume(int) は維持）

**Files:**
- Modify: `src/Core/Battle/State/BlockPool.cs`
- Modify: `tests/Core.Tests/Battle/State/BlockPoolTests.cs`

> 注: 本タスクでは `Consume(int)` は維持し、`Display(dex)` のみ追加。`Consume(in, dex)` への置換は Task 10 で実施。`RawTotal` の internal 化は Task 11。

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Battle/State/BlockPoolTests.cs` の最後に追記:

```csharp
[Fact] public void Display_no_dex_returns_sum()
{
    var p = BlockPool.Empty.Add(5).Add(3);
    Assert.Equal(8, p.Display(dexterity: 0));
}

[Fact] public void Display_dex_adds_per_addcount()
{
    // Sum=8, AddCount=2, dex=3 → 8 + 2*3 = 14
    var p = BlockPool.Empty.Add(5).Add(3);
    Assert.Equal(14, p.Display(dexterity: 3));
}

[Fact] public void Display_zero_when_empty()
{
    Assert.Equal(0, BlockPool.Empty.Display(dexterity: 10));
}
```

- [ ] **Step 2: 失敗確認** — build error（`Display` 未定義）

- [ ] **Step 3: 実装**

`src/Core/Battle/State/BlockPool.cs` を以下に変更（`Consume(int)` は残す）:

```csharp
using System;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// ブロック値の蓄積プール。10.2.B で Display(dex) を追加。
/// Consume の dexterity 対応版は Task 10 で導入予定。
/// 親 spec §3-3 / Phase 10.2.B spec §2-5 参照。
/// </summary>
public readonly record struct BlockPool(int Sum, int AddCount)
{
    public static BlockPool Empty => new(0, 0);

    public BlockPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>敏捷を遡及反映（×AddCount）した表示・吸収用 Block 量。</summary>
    public int Display(int dexterity) => Sum + AddCount * dexterity;

    /// <summary>10.2.A 互換 API。Task 10 で `Consume(int, int)` に置換予定。</summary>
    public BlockPool Consume(int incomingAttack)
    {
        var remaining = Math.Max(0, Sum - incomingAttack);
        return new(remaining, 0);
    }

    /// <summary>10.2.A の暫定 API。Task 11 で internal 化。</summary>
    public int RawTotal => Sum;
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BlockPoolTests`
Expected: 既存 5 + 新規 3 = 8 passed

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/State/BlockPool.cs tests/Core.Tests/Battle/State/BlockPoolTests.cs
git commit -m "feat(battle): add BlockPool.Display(dex) (Phase 10.2.B Task 6)"
```

---

## Task 7: CombatActor に Statuses フィールドと GetStatus を追加（破壊的）

**Files:**
- Modify: `src/Core/Battle/State/CombatActor.cs`
- Modify: `src/Core/Battle/Engine/BattleEngine.cs`（`Start` 内の hero / enemies 構築）
- Modify: `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs`（Hero / Goblin factory）
- Modify: `tests/Core.Tests/Battle/State/CombatActorTests.cs`（直接構築箇所 + テスト追加）

> **破壊的変更**: `CombatActor` の record 引数が増えるため、すべての構築箇所を同 commit で更新する。

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Battle/State/CombatActorTests.cs` を以下のように差し替え:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class CombatActorTests
{
    private static CombatActor MakeHero(int hp = 70) =>
        new("hero1", "hero", ActorSide.Ally, 0, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, null);

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

    [Fact] public void GetStatus_returns_zero_for_unknown()
    {
        var a = MakeHero();
        Assert.Equal(0, a.GetStatus("strength"));
    }

    [Fact] public void GetStatus_returns_amount_when_present()
    {
        var statuses = ImmutableDictionary<string, int>.Empty.Add("strength", 3);
        var a = MakeHero() with { Statuses = statuses };
        Assert.Equal(3, a.GetStatus("strength"));
    }

    [Fact] public void Record_inequality_when_statuses_differ()
    {
        var a = MakeHero();
        var b = MakeHero() with { Statuses = ImmutableDictionary<string, int>.Empty.Add("weak", 1) };
        Assert.NotEqual(a, b);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error（既存 fixture / Engine.Start / 全テストが新引数 `Statuses` を渡していない）

- [ ] **Step 3: production code 実装**

`src/Core/Battle/State/CombatActor.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中の戦闘者状態。10.2.B で Statuses フィールドと GetStatus を追加。
/// 親 spec §3-2 / Phase 10.2.B spec §2-3 参照。
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
    string? CurrentMoveId)
{
    public bool IsAlive => CurrentHp > 0;

    /// <summary>未保持なら 0 を返す。Statuses は 0 以下の amount を持たない不変条件。</summary>
    public int GetStatus(string id) => Statuses.TryGetValue(id, out var v) ? v : 0;
}
```

- [ ] **Step 4: BattleEngine.Start を更新**

`src/Core/Battle/Engine/BattleEngine.cs` の hero / enemy 構築箇所:

```csharp
// hero 生成（既存コードの 27 行目付近）
var hero = new CombatActor(
    InstanceId: "hero_inst", DefinitionId: "hero",
    Side: ActorSide.Ally, SlotIndex: 0,
    CurrentHp: run.CurrentHp, MaxHp: run.MaxHp,
    Block: BlockPool.Empty,
    AttackSingle: AttackPool.Empty,
    AttackRandom: AttackPool.Empty,
    AttackAll: AttackPool.Empty,
    Statuses: System.Collections.Immutable.ImmutableDictionary<string, int>.Empty,
    CurrentMoveId: null);

// enemy 生成（44-52 行目付近）
enemiesBuilder.Add(new CombatActor(
    InstanceId: $"enemy_inst_{i}", DefinitionId: eid,
    Side: ActorSide.Enemy, SlotIndex: i,
    CurrentHp: def.Hp, MaxHp: def.Hp,
    Block: BlockPool.Empty,
    AttackSingle: AttackPool.Empty,
    AttackRandom: AttackPool.Empty,
    AttackAll: AttackPool.Empty,
    Statuses: System.Collections.Immutable.ImmutableDictionary<string, int>.Empty,
    CurrentMoveId: def.InitialMoveId));
```

- [ ] **Step 5: BattleFixtures.Hero / Goblin を更新**

`tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs`:

```csharp
public static CombatActor Hero(int hp = 70, int slotIndex = 0) =>
    new("hero_inst", "hero", ActorSide.Ally, slotIndex, hp, hp,
        BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
        ImmutableDictionary<string, int>.Empty, null);

public static CombatActor Goblin(int slotIndex = 0, int hp = 20, string moveId = "swing") =>
    new($"goblin_inst_{slotIndex}", "goblin", ActorSide.Enemy, slotIndex, hp, hp,
        BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
        ImmutableDictionary<string, int>.Empty, moveId);
```

- [ ] **Step 6: 全 build 確認**

Run: `dotnet build`
Expected: 警告 0 / エラー 0

> もし他にも直接 `CombatActor` を構築している箇所が build エラーで判明した場合（typically `EnemyAttackingResolverTests.cs` など）、同 commit で `Statuses: ImmutableDictionary<string, int>.Empty,` を引数に追加して修正。

- [ ] **Step 7: 全 test 緑確認**

Run: `dotnet test`
Expected: 既存全テスト緑 + `CombatActorTests` 7 件（既存 4 + 新規 3）緑

- [ ] **Step 8: commit**

```bash
git add src/Core/Battle/State/CombatActor.cs \
        src/Core/Battle/Engine/BattleEngine.cs \
        tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs \
        tests/Core.Tests/Battle/State/CombatActorTests.cs
# build エラーで他テストが影響を受けて修正した場合は git add で追加
git commit -m "feat(battle): add CombatActor.Statuses + GetStatus (Phase 10.2.B Task 7)"
```

---

## Task 8: BattleFixtures に status helpers 追加

**Files:**
- Modify: `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs`

- [ ] **Step 1: helper を追加**

`tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` の `MakeBattleCard` の後ろに追記:

```csharp
// ===== Status helpers =====

/// <summary>actor に status を 1 つ追加した複製を返す。</summary>
public static CombatActor WithStatus(CombatActor actor, string id, int amount) =>
    actor with { Statuses = actor.Statuses.SetItem(id, amount) };

public static CombatActor WithStrength(CombatActor actor, int amount = 1) =>
    WithStatus(actor, "strength", amount);

public static CombatActor WithDexterity(CombatActor actor, int amount = 1) =>
    WithStatus(actor, "dexterity", amount);

public static CombatActor WithVulnerable(CombatActor actor, int amount = 1) =>
    WithStatus(actor, "vulnerable", amount);

public static CombatActor WithWeak(CombatActor actor, int amount = 1) =>
    WithStatus(actor, "weak", amount);

public static CombatActor WithPoison(CombatActor actor, int amount = 1) =>
    WithStatus(actor, "poison", amount);

public static CombatActor WithOmnistrike(CombatActor actor, int amount = 1) =>
    WithStatus(actor, "omnistrike", amount);
```

- [ ] **Step 2: 緑確認**

Run: `dotnet build && dotnet test`
Expected: 警告 0 / エラー 0、全テスト緑（追加分は使われていないが build OK）

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs
git commit -m "test(battle): add WithStatus helpers to BattleFixtures (Phase 10.2.B Task 8)"
```

---

## Task 9: EffectApplier.ReplaceActor を InstanceId 検索に切替（latent bug 根治）

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierReplaceActorInstanceIdTests.cs`

> 10.2.A の `ReplaceActor` は `state.Allies.IndexOf(before)` を使用。複数 effect が連続適用されると `before` が古い snapshot で `IndexOf == -1` になる latent bug を根治。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/EffectApplierReplaceActorInstanceIdTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// EffectApplier.ReplaceActor が InstanceId 検索で動作することを保証する回帰テスト。
/// 10.2.A の `IndexOf(before)` 経路は、複数 effect 連続適用で古い snapshot 参照になり
/// IndexOf == -1 / SetItem 例外を引き起こす latent bug を持っていた。
/// </summary>
public class EffectApplierReplaceActorInstanceIdTests
{
    private static BattleState MakeState(CombatActor hero, CombatActor enemy) => new(
        Turn: 1, Phase: BattlePhase.PlayerInput,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: ImmutableArray.Create(enemy),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    [Fact] public void Apply_attack_then_block_on_same_caster_succeeds()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = MakeState(hero, goblin);
        var rng = new FakeRng(new int[0], new double[0]);

        // 1 つ目: attack（caster.AttackSingle に加算）
        var atkEff = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
        var (s1, _) = EffectApplier.Apply(s, hero, atkEff, rng);
        Assert.Equal(5, s1.Allies[0].AttackSingle.Sum);

        // 2 つ目: block（caster.Block に加算）。caster ref は古い hero のまま渡しても InstanceId で再 fetch されて正しく動作することを検証
        var blkEff = new CardEffect("block", EffectScope.Self, null, 3);
        var caster1 = s1.Allies[0];   // 最新の hero
        var (s2, _) = EffectApplier.Apply(s1, caster1, blkEff, rng);
        Assert.Equal(5, s2.Allies[0].AttackSingle.Sum);  // attack 結果が保持
        Assert.Equal(3, s2.Allies[0].Block.Sum);          // block も加算
    }

    [Fact] public void Apply_block_with_stale_caster_ref_does_not_throw()
    {
        // ReplaceActor が IndexOf ベースだと、stale caster で IndexOf=-1 → SetItem 例外
        // InstanceId 検索なら例外なく更新できる
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = MakeState(hero, goblin);
        var rng = new FakeRng(new int[0], new double[0]);

        // hero に AttackSingle を加算（実 state を変える）
        var atkEff = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
        var (s1, _) = EffectApplier.Apply(s, hero, atkEff, rng);

        // 古い hero ref（=Statuses 等が更新前）を caster として block effect を再度適用
        var blkEff = new CardEffect("block", EffectScope.Self, null, 4);
        // stale caster (= 元の hero) を渡す。InstanceId 検索なら最新 actor を見つけて更新できる
        var (s2, _) = EffectApplier.Apply(s1, hero, blkEff, rng);

        // attack の結果が消失していないことを確認
        Assert.Equal(5, s2.Allies[0].AttackSingle.Sum);
        Assert.Equal(4, s2.Allies[0].Block.Sum);
    }
}
```

- [ ] **Step 2: 失敗確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierReplaceActorInstanceIdTests`
Expected: 1 つ目の test は緑（caster1 が最新参照なので IndexOf でも動く）、2 つ目の test は失敗（stale caster で IndexOf=-1 → 例外 / 結果消失）

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/EffectApplier.cs` の `ReplaceActor` を InstanceId 検索化:

```csharp
private static BattleState ReplaceActor(BattleState state, string instanceId, CombatActor after)
{
    if (after.Side == ActorSide.Ally)
    {
        for (int i = 0; i < state.Allies.Length; i++)
        {
            if (state.Allies[i].InstanceId == instanceId)
                return state with { Allies = state.Allies.SetItem(i, after) };
        }
    }
    else
    {
        for (int i = 0; i < state.Enemies.Length; i++)
        {
            if (state.Enemies[i].InstanceId == instanceId)
                return state with { Enemies = state.Enemies.SetItem(i, after) };
        }
    }
    return state;
}
```

そして既存の `ReplaceActor(state, before, updated)` 呼び出しを `ReplaceActor(state, before.InstanceId, updated)` に変更:

```csharp
// ApplyAttack 内
var next = ReplaceActor(state, caster.InstanceId, updated);

// ApplyBlock 内
var next = ReplaceActor(state, caster.InstanceId, updated);
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test`
Expected: 全テスト緑（既存 + 新規 2）

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierReplaceActorInstanceIdTests.cs
git commit -m "fix(battle): use InstanceId lookup in EffectApplier.ReplaceActor (Phase 10.2.B Task 9)"
```

---

## Task 10: BlockPool.Consume(int, int) 追加 + DealDamageHelper 呼び出し更新（旧 Consume 削除は Task 11）

**Files:**
- Modify: `src/Core/Battle/State/BlockPool.cs`
- Modify: `src/Core/Battle/Engine/DealDamageHelper.cs`（呼び出しを `Consume(in, dex)` に切替、ただし dex=0 で呼ぶ）
- Modify: `tests/Core.Tests/Battle/State/BlockPoolTests.cs`

> Task 6 で `Display(dex)` を追加済み。本タスクで `Consume(int, int)` を**追加**する（旧 `Consume(int)` は維持）。helper の呼び出しは新シグネチャに切替（dex=0 を渡す）。`Consume(int)` の削除と RawTotal の internal 化は Task 11 でまとめて。

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Battle/State/BlockPoolTests.cs` の最後に追記:

```csharp
[Fact] public void Consume_with_dex_uses_display()
{
    // Sum=5, AddCount=2, dex=3 → Display=11、Consume(4) で残量 7
    var p = BlockPool.Empty.Add(2).Add(3);
    var after = p.Consume(incomingAttack: 4, dexterity: 3);
    Assert.Equal(7, after.Sum);
    Assert.Equal(0, after.AddCount);
}

[Fact] public void Consume_dex_overflow_clamps_to_zero()
{
    // Display=8, attack=20 → 0
    var p = BlockPool.Empty.Add(5).Add(0); // Sum=5, AddCount=2, Display(dex=0)=5+2*0=... wait
    // Better: Sum=5, AddCount=1, dex=3 → Display=5+3=8、attack=20 → 0
    var p2 = BlockPool.Empty.Add(5);
    var after = p2.Consume(incomingAttack: 20, dexterity: 3);
    Assert.Equal(0, after.Sum);
    Assert.Equal(0, after.AddCount);
}

[Fact] public void Consume_with_zero_dex_matches_old_behavior()
{
    // dex=0 で呼べば旧 Consume(int) と等価
    var p = BlockPool.Empty.Add(5).Add(5); // Sum=10, AddCount=2
    var after = p.Consume(incomingAttack: 3, dexterity: 0);
    Assert.Equal(7, after.Sum);
    Assert.Equal(0, after.AddCount);
}
```

- [ ] **Step 2: 失敗確認** — build error（`Consume(int, int)` 未定義）

- [ ] **Step 3: 実装**

`src/Core/Battle/State/BlockPool.cs` に `Consume(int, int)` を追加（旧 `Consume(int)` は残す）:

```csharp
/// <summary>
/// 攻撃の総量を受けて Block を消費。`incomingAttack` は「ブロック適用前の攻撃値」を渡す。
/// 残量を新 Sum、AddCount=0 にリセット（消費後は遡及性を失う）。
/// 親 spec §3-3 / §4-4 参照。
/// </summary>
public BlockPool Consume(int incomingAttack, int dexterity)
{
    var current = Display(dexterity);
    var remaining = Math.Max(0, current - incomingAttack);
    return new(remaining, 0);
}
```

- [ ] **Step 4: DealDamageHelper の呼び出しを切替（dex=0 で互換）**

`src/Core/Battle/Engine/DealDamageHelper.cs` 内の `Consume(totalAttack)` を `Consume(totalAttack, dexterity: 0)` に変更:

```csharp
var newBlock = target.Block.Consume(totalAttack, dexterity: 0);
```

- [ ] **Step 5: 緑確認**

Run: `dotnet test`
Expected: 既存全テスト緑 + 新規 3 テスト緑

- [ ] **Step 6: commit**

```bash
git add src/Core/Battle/State/BlockPool.cs \
        src/Core/Battle/Engine/DealDamageHelper.cs \
        tests/Core.Tests/Battle/State/BlockPoolTests.cs
git commit -m "feat(battle): add BlockPool.Consume(in, dex) overload (Phase 10.2.B Task 10)"
```

---

## Task 11: DealDamageHelper シグネチャ拡張 + str/weak/vuln/dex 統合 + 旧 API 削除

**Files:**
- Modify: `src/Core/Battle/Engine/DealDamageHelper.cs`
- Modify: `src/Core/Battle/Engine/PlayerAttackingResolver.cs`（呼び出し変更）
- Modify: `src/Core/Battle/Engine/EnemyAttackingResolver.cs`（呼び出し変更）
- Modify: `src/Core/Battle/State/AttackPool.cs`（RawTotal を internal）
- Modify: `src/Core/Battle/State/BlockPool.cs`（旧 Consume(int) 削除 + RawTotal を internal）
- Create: `tests/Core.Tests/Battle/Engine/DealDamageHelperTests.cs`
- Modify: `tests/Core.Tests/Battle/State/AttackPoolTests.cs`（既存 RawTotal テストはそのまま動く、internal は InternalsVisibleTo 経由で OK）
- Modify: `tests/Core.Tests/Battle/State/BlockPoolTests.cs`（旧 `Consume(int)` のテスト 3 件を削除）

> **大型破壊的変更**: シグネチャ変更 + 旧 API 削除を 1 commit で。10.2.A の `BattlePlaceholderState` リネームと同パターン。

- [ ] **Step 1: DealDamageHelper の新テストを書く**

`tests/Core.Tests/Battle/Engine/DealDamageHelperTests.cs`:

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class DealDamageHelperTests
{
    [Fact] public void Baseline_no_status_no_block_full_damage()
    {
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin();
        var (updated, evs, died) = DealDamageHelper.Apply(
            attacker: att, target: tgt, baseSum: 6, addCount: 1, scopeNote: "single", orderBase: 0);
        Assert.Equal(20 - 6, updated.CurrentHp);
        Assert.False(died);
        Assert.Equal(2, evs.Count);
        Assert.Equal(BattleEventKind.AttackFire, evs[0].Kind);
        Assert.Equal(6, evs[0].Amount);
        Assert.Equal(BattleEventKind.DealDamage, evs[1].Kind);
        Assert.Equal(6, evs[1].Amount);
    }

    [Fact] public void Strength_adds_per_addcount()
    {
        // baseSum=8, addCount=2, strength=3 → totalAttack = 8 + 2*3 = 14
        var att = BattleFixtures.WithStrength(BattleFixtures.Hero(), 3);
        var tgt = BattleFixtures.Goblin(hp: 30);
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 8, 2, "single", 0);
        Assert.Equal(30 - 14, updated.CurrentHp);
        Assert.Equal(14, evs[0].Amount); // AttackFire
    }

    [Fact] public void Weak_applies_three_quarters_floor()
    {
        // baseSum=10, addCount=1, weak=1 → totalAttack = floor(10 * 0.75) = 7
        var att = BattleFixtures.WithWeak(BattleFixtures.Hero(), 1);
        var tgt = BattleFixtures.Goblin(hp: 30);
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.Equal(30 - 7, updated.CurrentHp);
        Assert.Equal(7, evs[0].Amount);
    }

    [Fact] public void Strength_with_weak_combines()
    {
        // baseSum=8, addCount=2, strength=3, weak=1 → boosted=14、× 0.75 = 10
        var att = BattleFixtures.WithWeak(BattleFixtures.WithStrength(BattleFixtures.Hero(), 3), 1);
        var tgt = BattleFixtures.Goblin(hp: 30);
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 8, 2, "single", 0);
        Assert.Equal(30 - 10, updated.CurrentHp);
    }

    [Fact] public void Block_absorbs_damage()
    {
        // baseSum=10, target.Block=Sum=4 (no dex) → absorbed=4, rawDamage=6
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin(hp: 30) with { Block = BlockPool.Empty.Add(4) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.Equal(30 - 6, updated.CurrentHp);
        Assert.Equal(0, updated.Block.Sum);  // 全消費
        Assert.Equal(0, updated.Block.AddCount);
    }

    [Fact] public void Dexterity_boosts_block()
    {
        // target.Block=Sum=2, AddCount=1, dex=3 → Display=5、attack=4 → absorbed=4, rawDamage=0、残 Block=1
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.WithDexterity(BattleFixtures.Goblin(hp: 30), 3) with { Block = BlockPool.Empty.Add(2) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 4, 1, "single", 0);
        Assert.Equal(30, updated.CurrentHp);  // 完全吸収
        Assert.Equal(1, updated.Block.Sum);   // 5 - 4 = 1
        Assert.Equal(0, updated.Block.AddCount);
    }

    [Fact] public void Vulnerable_after_block_multiplies_one_point_five()
    {
        // attack=10, Block=4 → rawDamage=6、vuln → floor(6 * 1.5) = 9
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with { Block = BlockPool.Empty.Add(4) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.Equal(30 - 9, updated.CurrentHp);
        Assert.Equal(10, evs[0].Amount); // AttackFire = totalAttack
        Assert.Equal(9, evs[1].Amount);  // DealDamage = damage 着弾
    }

    [Fact] public void Vulnerable_blocked_completely_no_amplification()
    {
        // attack=10, Block=100 → rawDamage=0、vuln → 0 のまま
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with { Block = BlockPool.Empty.Add(100) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.Equal(30, updated.CurrentHp);
    }

    [Fact] public void All_corrections_combined()
    {
        // baseSum=8, addCount=2, str=3, weak=1, dex=0, Block=Sum=2、vuln=1
        // totalAttack = floor((8 + 2*3) * 0.75) = floor(14 * 0.75) = 10
        // Block=Sum=2, dex=0 → absorbed=2, rawDamage=8
        // vuln → floor(8 * 1.5) = 12
        var att = BattleFixtures.WithWeak(BattleFixtures.WithStrength(BattleFixtures.Hero(), 3), 1);
        var tgt = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with { Block = BlockPool.Empty.Add(2) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 8, 2, "single", 0);
        Assert.Equal(30 - 12, updated.CurrentHp);
    }

    [Fact] public void Dies_now_emits_actor_death()
    {
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin(hp: 5);
        var (updated, evs, died) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.True(died);
        Assert.Equal(3, evs.Count);
        Assert.Equal(BattleEventKind.ActorDeath, evs[2].Kind);
    }

    [Fact] public void Already_dead_target_does_not_emit_death()
    {
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin(hp: 0);
        var (updated, evs, died) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.False(died);
        Assert.Equal(2, evs.Count); // AttackFire + DealDamage のみ
    }

    [Fact] public void Order_starts_at_orderBase()
    {
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin(hp: 5);
        var (_, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", orderBase: 5);
        Assert.Equal(5, evs[0].Order);
        Assert.Equal(6, evs[1].Order);
        Assert.Equal(7, evs[2].Order);
    }
}
```

- [ ] **Step 2: 失敗確認** — build error（新シグネチャ未実装）

- [ ] **Step 3: DealDamageHelper を実装**

`src/Core/Battle/Engine/DealDamageHelper.cs` を以下に置き換え:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 1 体への DealDamage 計算ヘルパー。
/// 攻撃側 strength × addCount で遡及加算 → weak で 0.75 切捨 →
/// dexterity 反映の Block で吸収 → 残量に vulnerable で 1.5 倍 → HP 減算。
/// 親 spec §4-4 / Phase 10.2.B spec §3 参照。
/// </summary>
internal static class DealDamageHelper
{
    public static (CombatActor updatedTarget, IReadOnlyList<BattleEvent> events, bool diedNow) Apply(
        CombatActor attacker, CombatActor target,
        int baseSum, int addCount,
        string scopeNote, int orderBase)
    {
        // 1. 攻撃側補正
        int strength = attacker.GetStatus("strength");
        int weak     = attacker.GetStatus("weak");
        long boosted = (long)baseSum + (long)addCount * strength;
        int totalAttack = weak > 0 ? (int)(boosted * 3 / 4) : (int)boosted;

        // 2. Block 消費（敏捷遡及込み）
        int dex = target.GetStatus("dexterity");
        int preBlock = target.Block.Display(dex);
        int absorbed = System.Math.Min(totalAttack, preBlock);
        int rawDamage = totalAttack - absorbed;
        var newBlock = target.Block.Consume(totalAttack, dex);

        // 3. 受け側補正（脆弱）— Block 通り後に適用
        int vulnerable = target.GetStatus("vulnerable");
        int damage = vulnerable > 0 ? (rawDamage * 3) / 2 : rawDamage;

        // 4. HP 減算
        bool wasAlive = target.IsAlive;
        var updated = target with
        {
            Block = newBlock,
            CurrentHp = target.CurrentHp - damage,
        };
        bool diedNow = wasAlive && !updated.IsAlive;

        // 5. イベント発火
        var events = new List<BattleEvent>
        {
            new(BattleEventKind.AttackFire, Order: orderBase,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Amount: totalAttack, Note: scopeNote),
            new(BattleEventKind.DealDamage, Order: orderBase + 1,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Amount: damage, Note: scopeNote),
        };
        if (diedNow)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ActorDeath, Order: orderBase + 2,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Note: scopeNote));
        }

        return (updated, events, diedNow);
    }
}
```

- [ ] **Step 4: PlayerAttackingResolver の呼び出し更新**

`src/Core/Battle/Engine/PlayerAttackingResolver.cs` 内の 3 箇所の `DealDamageHelper.Apply` 呼び出しを `(.Sum, .AddCount)` 渡しに変更:

```csharp
// Single
var (updated, evs, _) = DealDamageHelper.Apply(
    ally, target,
    baseSum: ally.AttackSingle.Sum, addCount: ally.AttackSingle.AddCount,
    scopeNote: "single", orderBase: order);

// Random
var (updated, evs, _) = DealDamageHelper.Apply(
    ally, target,
    baseSum: ally.AttackRandom.Sum, addCount: ally.AttackRandom.AddCount,
    scopeNote: "random", orderBase: order);

// All
var (updated, evs, _) = DealDamageHelper.Apply(
    ally, target,
    baseSum: ally.AttackAll.Sum, addCount: ally.AttackAll.AddCount,
    scopeNote: "all", orderBase: order);
```

- [ ] **Step 5: EnemyAttackingResolver の呼び出し更新**

`src/Core/Battle/Engine/EnemyAttackingResolver.cs` 内の per-effect attack 呼び出しを `(effect.Amount, addCount: 1)` 渡しに変更:

```csharp
var (updated, evs, _) = DealDamageHelper.Apply(
    currentEnemyState, currentAlly,
    baseSum: eff.Amount, addCount: 1,
    scopeNote: "enemy_attack", orderBase: order);
```

- [ ] **Step 6: AttackPool / BlockPool の RawTotal を internal、BlockPool.Consume(int) を削除**

`src/Core/Battle/State/AttackPool.cs`:

```csharp
/// <summary>10.2.A 互換のためテスト・debug 用に internal で温存。</summary>
internal int RawTotal => Sum;
```

`src/Core/Battle/State/BlockPool.cs`:
- 旧 `public BlockPool Consume(int incomingAttack)` メソッドを削除
- `public int RawTotal => Sum;` → `internal int RawTotal => Sum;`

最終的な `BlockPool.cs`:

```csharp
using System;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// ブロック値の蓄積プール。10.2.B で Display(dex) と Consume(in, dex) を導入。
/// 親 spec §3-3 / Phase 10.2.B spec §2-5 参照。
/// </summary>
public readonly record struct BlockPool(int Sum, int AddCount)
{
    public static BlockPool Empty => new(0, 0);

    public BlockPool Add(int amount) => new(Sum + amount, AddCount + 1);

    public int Display(int dexterity) => Sum + AddCount * dexterity;

    public BlockPool Consume(int incomingAttack, int dexterity)
    {
        var current = Display(dexterity);
        var remaining = Math.Max(0, current - incomingAttack);
        return new(remaining, 0);
    }

    /// <summary>10.2.A 互換のためテスト・debug 用に internal で温存。</summary>
    internal int RawTotal => Sum;
}
```

- [ ] **Step 7: BlockPoolTests の旧 Consume テスト 3 件を削除**

`tests/Core.Tests/Battle/State/BlockPoolTests.cs` から以下を削除:
- `Consume_partial_keeps_remainder_resets_addcount`
- `Consume_overflow_clamps_to_zero`
- `Consume_zero_keeps_sum_but_resets_addcount`

これら 3 件は Task 10 で追加した `Consume(int, int)` 版でカバー済み（dex=0 で同等）。

- [ ] **Step 8: 全 build / test 確認**

Run: `dotnet build && dotnet test`
Expected: 警告 0 / エラー 0、全テスト緑（既存全て + DealDamageHelperTests 12 件）

> もし他に `RawTotal` を使っている test ファイルがあれば、`InternalsVisibleTo("Core.Tests")` 経由でアクセス可能なため build エラーは起きないはず。生じた場合は同 commit で修正。

- [ ] **Step 9: commit**

```bash
git add src/Core/Battle/Engine/DealDamageHelper.cs \
        src/Core/Battle/Engine/PlayerAttackingResolver.cs \
        src/Core/Battle/Engine/EnemyAttackingResolver.cs \
        src/Core/Battle/State/AttackPool.cs \
        src/Core/Battle/State/BlockPool.cs \
        tests/Core.Tests/Battle/Engine/DealDamageHelperTests.cs \
        tests/Core.Tests/Battle/State/BlockPoolTests.cs
git commit -m "refactor(battle): integrate str/weak/vuln/dex into DealDamageHelper (Phase 10.2.B Task 11)"
```

---

## Task 12: EffectApplier に buff / debuff action（4 scope）追加 + 重ね掛け加算 + event 発火

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Create: `tests/Core.Tests/Battle/Engine/EffectApplierBuffDebuffTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/EffectApplierBuffDebuffTests.cs`:

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

public class EffectApplierBuffDebuffTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerInput,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void Buff_self_adds_strength_to_caster()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Self, null, 2, Name: "strength");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(2, next.Allies[0].GetStatus("strength"));
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.ApplyStatus, evs[0].Kind);
        Assert.Equal("strength", evs[0].Note);
        Assert.Equal(2, evs[0].Amount);
    }

    [Fact] public void Debuff_single_enemy_adds_vulnerable_to_target()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = State(hero, goblin);
        var eff = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1, Name: "vulnerable");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(1, next.Enemies[0].GetStatus("vulnerable"));
    }

    [Fact] public void Debuff_all_enemies_adds_weak_to_each()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin(0), BattleFixtures.Goblin(1));
        var eff = new CardEffect("debuff", EffectScope.All, EffectSide.Enemy, 1, Name: "weak");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(1, next.Enemies[0].GetStatus("weak"));
        Assert.Equal(1, next.Enemies[1].GetStatus("weak"));
        Assert.Equal(2, evs.Count(e => e.Kind == BattleEventKind.ApplyStatus));
    }

    [Fact] public void Debuff_random_enemy_uses_rng()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin(0), BattleFixtures.Goblin(1));
        var eff = new CardEffect("debuff", EffectScope.Random, EffectSide.Enemy, 1, Name: "weak");
        // FakeRng で index 1 を指す
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(1));
        Assert.Equal(0, next.Enemies[0].GetStatus("weak"));
        Assert.Equal(1, next.Enemies[1].GetStatus("weak"));
    }

    [Fact] public void Buff_stacks_amount()
    {
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 2);
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Self, null, 3, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(5, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void Negative_delta_below_zero_removes_status_and_emits_RemoveStatus()
    {
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 2);
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("debuff", EffectScope.Self, null, -5, Name: "strength");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.False(next.Allies[0].Statuses.ContainsKey("strength"));
        Assert.Equal(BattleEventKind.RemoveStatus, evs[0].Kind);
        Assert.Equal("strength", evs[0].Note);
    }

    [Fact] public void Buff_single_with_null_side_throws()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Single, null, 1, Name: "strength");
        Assert.Throws<System.InvalidOperationException>(() => EffectApplier.Apply(s, hero, eff, Rng()));
    }

    [Fact] public void Debuff_single_with_no_target_index_is_noop()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = State(hero, goblin) with { TargetEnemyIndex = null };
        var eff = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1, Name: "weak");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(0, next.Enemies[0].GetStatus("weak"));
        Assert.Empty(evs);
    }

    [Fact] public void Buff_random_ally_uses_rng()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Random, EffectSide.Ally, 1, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(0));
        Assert.Equal(1, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void ApplyStatus_event_caster_is_effect_caster()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = State(hero, goblin);
        var eff = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1, Name: "vulnerable");
        var (_, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(hero.InstanceId, evs[0].CasterInstanceId);
        Assert.Equal(goblin.InstanceId, evs[0].TargetInstanceId);
    }
}
```

- [ ] **Step 2: 失敗確認** — buff/debuff action 未実装で no-op、テスト失敗

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/EffectApplier.cs` を以下のように拡張:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 単一 CardEffect を BattleState に適用する。
/// Phase 10.2.B で buff / debuff action を追加（4 scope 対応）。
/// その他 action（heal/draw/discard/upgrade/exhaust*/retainSelf/gainEnergy/summon）は no-op。
/// 親 spec §5 / Phase 10.2.B spec §4 参照。
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
            "buff"   => ApplyStatusChange(state, caster, effect, rng),
            "debuff" => ApplyStatusChange(state, caster, effect, rng),
            _        => (state, Array.Empty<BattleEvent>()),
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
            _ => caster,
        };
        var next = ReplaceActor(state, caster.InstanceId, updated);
        return (next, Array.Empty<BattleEvent>());
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyBlock(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        var updated = caster with { Block = caster.Block.Add(effect.Amount) };
        var next = ReplaceActor(state, caster.InstanceId, updated);
        var ev = new BattleEvent(
            BattleEventKind.GainBlock, Order: 0,
            CasterInstanceId: caster.InstanceId,
            TargetInstanceId: caster.InstanceId,
            Amount: effect.Amount);
        return (next, new[] { ev });
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyStatusChange(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        if (string.IsNullOrEmpty(effect.Name))
            throw new InvalidOperationException($"buff/debuff effect requires Name (status id), got null/empty");

        var targets = ResolveTargets(state, caster, effect, rng);
        if (targets.Count == 0)
            return (state, Array.Empty<BattleEvent>());

        var events = new List<BattleEvent>();
        int order = 0;
        var s = state;

        // InstanceId snapshot で各 target を更新
        var targetIds = targets.Select(t => t.InstanceId).ToList();
        foreach (var tid in targetIds)
        {
            // 最新 state から再 fetch
            CombatActor? current = FindActor(s, tid);
            if (current is null) continue;

            int currentAmount = current.GetStatus(effect.Name);
            int newAmount = currentAmount + effect.Amount;

            ImmutableDictionary<string, int> newStatuses = newAmount <= 0
                ? current.Statuses.Remove(effect.Name)
                : current.Statuses.SetItem(effect.Name, newAmount);

            var updated = current with { Statuses = newStatuses };
            s = ReplaceActor(s, tid, updated);

            if (newAmount > 0 && currentAmount != newAmount)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ApplyStatus, Order: order++,
                    CasterInstanceId: caster.InstanceId,
                    TargetInstanceId: tid,
                    Amount: effect.Amount,
                    Note: effect.Name));
            }
            else if (newAmount <= 0 && currentAmount > 0)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus, Order: order++,
                    TargetInstanceId: tid,
                    Note: effect.Name));
            }
        }

        return (s, events);
    }

    private static IReadOnlyList<CombatActor> ResolveTargets(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        switch (effect.Scope)
        {
            case EffectScope.Self:
                return new[] { caster };

            case EffectScope.Single:
                if (effect.Side is null)
                    throw new InvalidOperationException(
                        $"effect '{effect.Action}' Scope=Single requires non-null Side");
                if (effect.Side == EffectSide.Ally)
                    return state.TargetAllyIndex is { } ai && ai < state.Allies.Length
                        ? new[] { state.Allies[ai] }
                        : Array.Empty<CombatActor>();
                else
                    return state.TargetEnemyIndex is { } ei && ei < state.Enemies.Length
                        ? new[] { state.Enemies[ei] }
                        : Array.Empty<CombatActor>();

            case EffectScope.Random:
            {
                if (effect.Side is null)
                    throw new InvalidOperationException(
                        $"effect '{effect.Action}' Scope=Random requires non-null Side");
                var pool = (effect.Side == EffectSide.Ally ? state.Allies : state.Enemies)
                    .Where(a => a.IsAlive).ToList();
                if (pool.Count == 0) return Array.Empty<CombatActor>();
                int idx = rng.NextInt(0, pool.Count);
                return new[] { pool[idx] };
            }

            case EffectScope.All:
            {
                if (effect.Side is null)
                    throw new InvalidOperationException(
                        $"effect '{effect.Action}' Scope=All requires non-null Side");
                return (effect.Side == EffectSide.Ally ? state.Allies : state.Enemies)
                    .Where(a => a.IsAlive).ToList();
            }
        }
        return Array.Empty<CombatActor>();
    }

    private static CombatActor? FindActor(BattleState state, string instanceId)
    {
        foreach (var a in state.Allies) if (a.InstanceId == instanceId) return a;
        foreach (var e in state.Enemies) if (e.InstanceId == instanceId) return e;
        return null;
    }

    private static BattleState ReplaceActor(BattleState state, string instanceId, CombatActor after)
    {
        if (after.Side == ActorSide.Ally)
        {
            for (int i = 0; i < state.Allies.Length; i++)
            {
                if (state.Allies[i].InstanceId == instanceId)
                    return state with { Allies = state.Allies.SetItem(i, after) };
            }
        }
        else
        {
            for (int i = 0; i < state.Enemies.Length; i++)
            {
                if (state.Enemies[i].InstanceId == instanceId)
                    return state with { Enemies = state.Enemies.SetItem(i, after) };
            }
        }
        return state;
    }
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EffectApplierBuffDebuffTests`
Expected: 10 passed

Run: `dotnet test`
Expected: 全テスト緑

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs \
        tests/Core.Tests/Battle/Engine/EffectApplierBuffDebuffTests.cs
git commit -m "feat(battle): add buff/debuff action with 4 scopes (Phase 10.2.B Task 12)"
```

---

## Task 13: TurnStartProcessor に毒ダメージ tick を追加

**Files:**
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs`
- Create: `tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs`

> 本タスクでは「毒ダメージ → tick 後の死亡判定 → Outcome 確定」のうち**毒ダメージのみ**を実装。死亡判定 + Outcome 確定は Task 14、status countdown は Task 15 で追加する。

- [ ] **Step 1: 失敗テストを書く（毒ダメージ部分のみ）**

`tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs`:

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

public class TurnStartProcessorTickTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerInput,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    [Fact] public void Poison_damages_target_ignoring_block()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 3) with { Block = BlockPool.Empty.Add(10) };
        var s = State(hero, BattleFixtures.Goblin());
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70 - 3, next.Allies[0].CurrentHp);
        Assert.Equal(10, next.Allies[0].Block.Sum); // Block は無傷
        Assert.Contains(evs, e => e.Kind == BattleEventKind.PoisonTick && e.Amount == 3);
    }

    [Fact] public void Poison_damages_all_actors_with_poison()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 2);
        var goblin0 = BattleFixtures.WithPoison(BattleFixtures.Goblin(0, hp: 20), 5);
        var goblin1 = BattleFixtures.Goblin(1, hp: 20); // poison なし
        var s = State(hero, goblin0, goblin1);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70 - 2, next.Allies[0].CurrentHp);
        Assert.Equal(20 - 5, next.Enemies[0].CurrentHp);
        Assert.Equal(20, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Dead_actor_skipped_in_poison_tick()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 2);
        var goblin = BattleFixtures.WithPoison(BattleFixtures.Goblin(hp: 20), 99) with { CurrentHp = 0 };
        var s = State(hero, goblin);
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70 - 2, next.Allies[0].CurrentHp);
        Assert.Equal(0, next.Enemies[0].CurrentHp); // 不変
        Assert.DoesNotContain(evs, e => e.Kind == BattleEventKind.PoisonTick && e.TargetInstanceId == goblin.InstanceId);
    }

    [Fact] public void No_poison_tick_when_no_status()
    {
        var hero = BattleFixtures.Hero(70);
        var s = State(hero, BattleFixtures.Goblin());
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.DoesNotContain(evs, e => e.Kind == BattleEventKind.PoisonTick);
    }

    [Fact] public void Energy_and_draw_still_happen_after_poison()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 2);
        var s = State(hero, BattleFixtures.Goblin()) with
        {
            DrawPile = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "c1"),
                BattleFixtures.MakeBattleCard("strike", "c2"),
                BattleFixtures.MakeBattleCard("strike", "c3"),
                BattleFixtures.MakeBattleCard("strike", "c4"),
                BattleFixtures.MakeBattleCard("strike", "c5")),
        };
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(3, next.Energy); // EnergyMax = 3
        Assert.Equal(5, next.Hand.Length);
    }
}
```

- [ ] **Step 2: 失敗確認** — `PoisonTick` event 未発火 / HP 不変

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/TurnStartProcessor.cs` の `Process` を以下に書き換え:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン開始処理。10.2.B で 毒ダメージ tick / status countdown / 死亡判定で Outcome 確定 を実装。
/// 召喚 Lifetime tick / OnTurnStart レリックは後続 phase。
/// 親 spec §4-2 / Phase 10.2.B spec §5 参照。
/// </summary>
internal static class TurnStartProcessor
{
    public const int DrawPerTurn = 5;
    public const int HandCap = 10;

    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng)
    {
        var s = state with { Turn = state.Turn + 1 };
        var events = new List<BattleEvent>();
        int order = 0;

        // Step 2: 毒ダメージ tick（Allies → Enemies、SlotIndex 順、InstanceId 検索で更新）
        s = ApplyPoisonTick(s, events, ref order);

        // Step 3-7（死亡判定 / countdown / Energy / Draw / TurnStart event）は Task 14, 15 以降で追加

        s = s with { Energy = s.EnergyMax };
        s = DrawCards(s, DrawPerTurn, rng);
        events.Add(new BattleEvent(BattleEventKind.TurnStart, Order: order++, Note: $"turn={s.Turn}"));
        return (s, events);
    }

    private static BattleState ApplyPoisonTick(BattleState state, List<BattleEvent> events, ref int order)
    {
        // Allies と Enemies の InstanceId スナップショットを採る
        var actorIds = state.Allies.OrderBy(a => a.SlotIndex).Select(a => a.InstanceId)
            .Concat(state.Enemies.OrderBy(e => e.SlotIndex).Select(e => e.InstanceId))
            .ToList();

        var s = state;
        foreach (var aid in actorIds)
        {
            CombatActor? actor = FindActor(s, aid);
            if (actor is null || !actor.IsAlive) continue;
            int poison = actor.GetStatus("poison");
            if (poison <= 0) continue;

            bool wasAlive = actor.IsAlive;
            var updated = actor with { CurrentHp = actor.CurrentHp - poison };
            s = ReplaceActor(s, aid, updated);

            events.Add(new BattleEvent(
                BattleEventKind.PoisonTick, Order: order++,
                TargetInstanceId: aid, Amount: poison, Note: "poison"));

            if (wasAlive && !updated.IsAlive)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ActorDeath, Order: order++,
                    TargetInstanceId: aid, Note: "poison"));
            }
        }
        return s;
    }

    private static CombatActor? FindActor(BattleState state, string instanceId)
    {
        foreach (var a in state.Allies) if (a.InstanceId == instanceId) return a;
        foreach (var e in state.Enemies) if (e.InstanceId == instanceId) return e;
        return null;
    }

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

- [ ] **Step 4: 緑確認**

Run: `dotnet test`
Expected: 全テスト緑（既存全 + 新規 5 件）

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/TurnStartProcessor.cs \
        tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs
git commit -m "feat(battle): add poison damage tick in TurnStartProcessor (Phase 10.2.B Task 13)"
```

---

## Task 14: TurnStartProcessor に死亡判定 + Outcome 確定 を追加

**Files:**
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs`
- Modify: `tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs`

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs` の最後に追記:

```csharp
[Fact] public void Poison_kills_all_enemies_outcome_victory()
{
    var hero = BattleFixtures.Hero(70);
    var goblin = BattleFixtures.WithPoison(BattleFixtures.Goblin(hp: 3), 5);
    var s = State(hero, goblin);
    var (next, evs) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory, next.Outcome);
    Assert.Equal(BattlePhase.Resolved, next.Phase);
    Assert.Contains(evs, e => e.Kind == BattleEventKind.BattleEnd);
}

[Fact] public void Poison_kills_hero_outcome_defeat()
{
    var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(hp: 2), 5);
    var s = State(hero, BattleFixtures.Goblin());
    var (next, evs) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat, next.Outcome);
    Assert.Equal(BattlePhase.Resolved, next.Phase);
    Assert.Contains(evs, e => e.Kind == BattleEventKind.BattleEnd);
}

[Fact] public void Outcome_confirmed_skips_energy_and_draw()
{
    var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(hp: 2), 5);
    var s = State(hero, BattleFixtures.Goblin()) with
    {
        DrawPile = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("strike", "c2")),
    };
    var (next, _) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(0, next.Energy);    // EnergyMax まで回復しない
    Assert.Empty(next.Hand);          // ドローしない
}

[Fact] public void Targeting_auto_switch_after_poison_kill()
{
    // 敵が複数、最内側が毒死 → TargetEnemyIndex が次の生存敵へ
    var hero = BattleFixtures.Hero();
    var goblin0 = BattleFixtures.WithPoison(BattleFixtures.Goblin(0, hp: 3), 5); // 死ぬ
    var goblin1 = BattleFixtures.Goblin(1, hp: 20);                              // 生存
    var s = State(hero, goblin0, goblin1);
    var (next, _) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending, next.Outcome);
    Assert.Equal(1, next.TargetEnemyIndex);
}
```

- [ ] **Step 2: 失敗確認** — 死亡判定 / Outcome 確定 / 早期 return が未実装

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/TurnStartProcessor.cs` の `Process` を以下に変更:

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng)
{
    var s = state with { Turn = state.Turn + 1 };
    var events = new List<BattleEvent>();
    int order = 0;

    // Step 2: 毒ダメージ tick
    s = ApplyPoisonTick(s, events, ref order);

    // Step 3: tick 後の死亡判定 + 自動切替 + Outcome 確定
    s = TargetingAutoSwitch.Apply(s);
    if (!s.Enemies.Any(e => e.IsAlive))
    {
        s = s with
        {
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
            Phase = BattlePhase.Resolved,
        };
        events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order++, Note: "Victory"));
        return (s, events);
    }
    if (!s.Allies.Any(a => a.IsAlive))
    {
        s = s with
        {
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat,
            Phase = BattlePhase.Resolved,
        };
        events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order++, Note: "Defeat"));
        return (s, events);
    }

    // Step 5-7（Energy / Draw / TurnStart event）。countdown は Task 15 で追加
    s = s with { Energy = s.EnergyMax };
    s = DrawCards(s, DrawPerTurn, rng);
    events.Add(new BattleEvent(BattleEventKind.TurnStart, Order: order++, Note: $"turn={s.Turn}"));
    return (s, events);
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test`
Expected: 全テスト緑

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/TurnStartProcessor.cs \
        tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs
git commit -m "feat(battle): confirm Outcome on poison death in TurnStart (Phase 10.2.B Task 14)"
```

---

## Task 15: TurnStartProcessor に status countdown を追加

**Files:**
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs`
- Modify: `tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs`

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs` の最後に追記:

```csharp
[Fact] public void Vulnerable_decrements_by_one_per_turn()
{
    var hero = BattleFixtures.Hero();
    var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(), 3);
    var s = State(hero, goblin);
    var (next, _) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(2, next.Enemies[0].GetStatus("vulnerable"));
}

[Fact] public void Status_at_one_decrements_to_zero_and_emits_RemoveStatus()
{
    var hero = BattleFixtures.Hero();
    var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(), 1);
    var s = State(hero, goblin);
    var (next, evs) = TurnStartProcessor.Process(s, Rng());
    Assert.False(next.Enemies[0].Statuses.ContainsKey("vulnerable"));
    Assert.Contains(evs, e => e.Kind == BattleEventKind.RemoveStatus
                              && e.Note == "vulnerable"
                              && e.TargetInstanceId == goblin.InstanceId);
}

[Fact] public void Strength_does_not_countdown()
{
    var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 5);
    var s = State(hero, BattleFixtures.Goblin());
    var (next, _) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(5, next.Allies[0].GetStatus("strength"));
}

[Fact] public void Dexterity_does_not_countdown()
{
    var hero = BattleFixtures.WithDexterity(BattleFixtures.Hero(), 4);
    var s = State(hero, BattleFixtures.Goblin());
    var (next, _) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(4, next.Allies[0].GetStatus("dexterity"));
}

[Fact] public void Poison_decrements_after_damage()
{
    // 毒 3 ターン → ダメージ 3、その後 countdown で 2 に
    var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 3);
    var s = State(hero, BattleFixtures.Goblin());
    var (next, _) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(70 - 3, next.Allies[0].CurrentHp);
    Assert.Equal(2, next.Allies[0].GetStatus("poison"));
}

[Fact] public void Multiple_decrement_statuses_all_tick()
{
    var hero = BattleFixtures.Hero();
    var goblin = BattleFixtures.Goblin() with
    {
        Statuses = ImmutableDictionary<string, int>.Empty
            .Add("vulnerable", 2)
            .Add("weak", 1)
            .Add("omnistrike", 3),
    };
    var s = State(hero, goblin);
    var (next, _) = TurnStartProcessor.Process(s, Rng());
    Assert.Equal(1, next.Enemies[0].GetStatus("vulnerable"));
    Assert.False(next.Enemies[0].Statuses.ContainsKey("weak")); // 1 → 0 で削除
    Assert.Equal(2, next.Enemies[0].GetStatus("omnistrike"));
}
```

- [ ] **Step 2: 失敗確認** — countdown 未実装

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/TurnStartProcessor.cs` の `Process` の中に countdown ステップを追加（死亡判定の後、Energy/Draw の前）:

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng)
{
    var s = state with { Turn = state.Turn + 1 };
    var events = new List<BattleEvent>();
    int order = 0;

    s = ApplyPoisonTick(s, events, ref order);

    s = TargetingAutoSwitch.Apply(s);
    if (!s.Enemies.Any(e => e.IsAlive))
    {
        s = s with
        {
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
            Phase = BattlePhase.Resolved,
        };
        events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order++, Note: "Victory"));
        return (s, events);
    }
    if (!s.Allies.Any(a => a.IsAlive))
    {
        s = s with
        {
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat,
            Phase = BattlePhase.Resolved,
        };
        events.Add(new BattleEvent(BattleEventKind.BattleEnd, Order: order++, Note: "Defeat"));
        return (s, events);
    }

    // Step 4: status countdown
    s = ApplyStatusCountdown(s, events, ref order);

    s = s with { Energy = s.EnergyMax };
    s = DrawCards(s, DrawPerTurn, rng);
    events.Add(new BattleEvent(BattleEventKind.TurnStart, Order: order++, Note: $"turn={s.Turn}"));
    return (s, events);
}

private static BattleState ApplyStatusCountdown(BattleState state, List<BattleEvent> events, ref int order)
{
    var actorIds = state.Allies.OrderBy(a => a.SlotIndex).Select(a => a.InstanceId)
        .Concat(state.Enemies.OrderBy(e => e.SlotIndex).Select(e => e.InstanceId))
        .ToList();

    var s = state;
    foreach (var aid in actorIds)
    {
        CombatActor? actor = FindActor(s, aid);
        if (actor is null) continue;
        // 死亡 actor も countdown は走らせる（仕様シンプル化のため、状態が見えないだけ）

        // 各 status を順次 −1
        foreach (var id in actor.Statuses.Keys.ToList())
        {
            var def = RoguelikeCardGame.Core.Battle.Statuses.StatusDefinition.Get(id);
            if (def.TickDirection != RoguelikeCardGame.Core.Battle.Statuses.StatusTickDirection.Decrement)
                continue;

            actor = FindActor(s, aid)!; // 同 actor 内で複数 status を更新するため再 fetch
            int newAmount = actor.GetStatus(id) - 1;
            ImmutableDictionary<string, int> newStatuses;
            if (newAmount <= 0)
            {
                newStatuses = actor.Statuses.Remove(id);
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus, Order: order++,
                    TargetInstanceId: aid, Note: id));
            }
            else
            {
                newStatuses = actor.Statuses.SetItem(id, newAmount);
                // ApplyStatus event は countdown では発火しない（spec §5-2）
            }
            s = ReplaceActor(s, aid, actor with { Statuses = newStatuses });
        }
    }
    return s;
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test`
Expected: 全テスト緑

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/TurnStartProcessor.cs \
        tests/Core.Tests/Battle/Engine/TurnStartProcessorTickTests.cs
git commit -m "feat(battle): add status countdown in TurnStart (Phase 10.2.B Task 15)"
```

---

## Task 16: BattleEngine.EndTurn が TurnStart 後 Outcome 確定で Phase 上書きをスキップ

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.EndTurn.cs`
- Modify: `tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs`

- [ ] **Step 1: 失敗テストを追加**

`tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs` の最後に追記:

```csharp
[Fact] public void EndTurn_with_poison_dying_hero_at_next_turn_keeps_resolved_phase()
{
    // 1. hero に毒を付ける（戦闘中に effect で）
    // 2. EndTurn → PlayerAttacking → EnemyAttacking → 次ターン TurnStart
    // 3. 次ターン開始時の毒 tick で hero が死亡 → Outcome=Defeat, Phase=Resolved
    // 4. EndTurn 末尾の `Phase = PlayerInput` 上書きが実行されないことを検証

    var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(hp: 2), 5);  // 次ターンで毒死確定
    var goblin = BattleFixtures.Goblin(hp: 100, moveId: "noop");

    // 敵 move が何もしない（攻撃なし）→ EndTurn 中は hero 生存、TurnStart で毒死
    var noopMove = new MoveDefinition("noop", MoveKind.Unknown, System.Array.Empty<CardEffect>(), "noop");
    var goblinDef = new EnemyDefinition("goblin", "Goblin", "img_goblin",
        100, new EnemyPool(1, EnemyTier.Weak), "noop", new[] { noopMove });

    var cards = new[] { BattleFixtures.Strike() };
    var enemies = new[] { goblinDef };
    var encs = new[] { new EncounterDefinition("enc_test", new EnemyPool(1, EnemyTier.Weak), new[] { "goblin" }) };
    var catalog = BattleFixtures.MinimalCatalog(cards, enemies, encs);

    var s = MakeState(hero, goblin) with
    {
        Phase = BattlePhase.PlayerInput,
        DrawPile = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1")),
    };
    var rng = new FakeRng(new int[0], new double[0]);
    var (next, _) = BattleEngine.EndTurn(s, rng, catalog);

    Assert.Equal(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat, next.Outcome);
    Assert.Equal(BattlePhase.Resolved, next.Phase); // PlayerInput に上書きされていない
}
```

> **注**: 既存の `MakeState` ヘルパーが `BattleEngineEndTurnTests` 内に存在する想定。なければ既存の inline 構築を踏襲。

- [ ] **Step 2: 失敗確認** — Phase=PlayerInput になってしまう（既存 EndTurn が無条件 Phase 上書き）

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/BattleEngine.EndTurn.cs` の末尾を以下に変更:

```csharp
// 6. ターン開始処理
var (afterStart, evsStart) = TurnStartProcessor.Process(s, rng);
AddWithOrder(events, evsStart, ref order);

// TurnStart 中に毒死で Outcome 確定した場合、Phase 上書きをスキップ
if (afterStart.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
    return (afterStart, events);

s = afterStart with { Phase = BattlePhase.PlayerInput };
return (s, events);
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test`
Expected: 全テスト緑

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/BattleEngine.EndTurn.cs \
        tests/Core.Tests/Battle/Engine/BattleEngineEndTurnTests.cs
git commit -m "feat(battle): skip Phase overwrite when TurnStart confirms Outcome (Phase 10.2.B Task 16)"
```

---

## Task 17: PlayerAttackingResolver に omnistrike 合算発射を追加

**Files:**
- Modify: `src/Core/Battle/Engine/PlayerAttackingResolver.cs`
- Create: `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOmnistrikeTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOmnistrikeTests.cs`:

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

public class PlayerAttackingResolverOmnistrikeTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerAttacking,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    [Fact] public void Omnistrike_combines_pools_and_hits_all_enemies()
    {
        // Single +5 を 1 枚、All +3 を 1 枚 → combined Sum=8, AddCount=2
        var hero = BattleFixtures.WithOmnistrike(BattleFixtures.Hero(), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(5),
            AttackAll    = AttackPool.Empty.Add(3),
        };
        var s = State(hero, BattleFixtures.Goblin(0, hp: 50), BattleFixtures.Goblin(1, hp: 50));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(50 - 8, next.Enemies[0].CurrentHp);
        Assert.Equal(50 - 8, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Omnistrike_AddCount_combines_for_strength_calc()
    {
        // Single +5 を 2 枚 (AddCount=2)、Random +3 を 1 枚 (AddCount=1) → combined Sum=13, AddCount=3
        // strength=2 → totalAttack = 13 + 3*2 = 19
        var hero = BattleFixtures.WithOmnistrike(BattleFixtures.WithStrength(BattleFixtures.Hero(), 2), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(5).Add(5),
            AttackRandom = AttackPool.Empty.Add(3),
        };
        var s = State(hero, BattleFixtures.Goblin(0, hp: 50));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(50 - 19, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Omnistrike_with_empty_pools_does_not_fire()
    {
        var hero = BattleFixtures.WithOmnistrike(BattleFixtures.Hero(), 1);
        var s = State(hero, BattleFixtures.Goblin(0, hp: 50));
        var (next, evs) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(50, next.Enemies[0].CurrentHp);
        Assert.DoesNotContain(evs, e => e.Kind == BattleEventKind.AttackFire);
    }

    [Fact] public void Omnistrike_emits_attack_fire_per_enemy()
    {
        var hero = BattleFixtures.WithOmnistrike(BattleFixtures.Hero(), 1) with
        {
            AttackAll = AttackPool.Empty.Add(3),
        };
        var s = State(hero, BattleFixtures.Goblin(0), BattleFixtures.Goblin(1), BattleFixtures.Goblin(2));
        var (_, evs) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(3, evs.Count(e => e.Kind == BattleEventKind.AttackFire));
        Assert.All(evs.Where(e => e.Kind == BattleEventKind.AttackFire),
                   e => Assert.Equal("omnistrike", e.Note));
    }

    [Fact] public void Without_omnistrike_uses_single_random_all_path()
    {
        // omnistrike なし → 既存挙動。Single のみで対象 1 体に着弾
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(5) };
        var s = State(hero, BattleFixtures.Goblin(0, hp: 20), BattleFixtures.Goblin(1, hp: 20));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(20 - 5, next.Enemies[0].CurrentHp);
        Assert.Equal(20, next.Enemies[1].CurrentHp);
    }
}
```

- [ ] **Step 2: 失敗確認** — omnistrike 経路未実装、テスト失敗

- [ ] **Step 3: 実装**

`src/Core/Battle/Engine/PlayerAttackingResolver.cs` を以下に書き換え:

```csharp
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// PlayerAttacking フェーズ実行。
/// omnistrike バフ持ち ally → Single+Random+All を合算して全敵に発射。
/// それ以外 → Single → Random → All の順で個別発射。
/// 親 spec §4-4 / Phase 10.2.B spec §6 参照。
/// </summary>
internal static class PlayerAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(BattleState state, IRng rng)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        // ally を SlotIndex 順で iterate（10.2.A は hero 1 体のみ、10.2.D で召喚を含める）
        var allyIds = state.Allies.OrderBy(a => a.SlotIndex).Select(a => a.InstanceId).ToList();
        foreach (var aid in allyIds)
        {
            var ally = FindAlly(state, aid);
            if (ally is null || !ally.IsAlive) continue;

            bool omni = ally.GetStatus("omnistrike") > 0;
            if (omni)
            {
                state = ResolveOmnistrike(state, ally, events, ref order);
            }
            else
            {
                state = ResolveSingle(state, ally, events, ref order);
                state = ResolveRandom(state, ally, rng, events, ref order);
                state = ResolveAll(state, ally, events, ref order);
            }
        }

        return (state, events);
    }

    private static BattleState ResolveOmnistrike(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order)
    {
        var combined = ally.AttackSingle + ally.AttackRandom + ally.AttackAll;
        if (combined.Sum <= 0) return state;

        var enemyIds = state.Enemies.Select(e => e.InstanceId).ToList();
        foreach (var eid in enemyIds)
        {
            int idx = -1;
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == eid) { idx = i; break; }
            if (idx < 0) continue;
            var current = state.Enemies[idx];

            var (updated, evs, _) = DealDamageHelper.Apply(
                ally, current,
                baseSum: combined.Sum, addCount: combined.AddCount,
                scopeNote: "omnistrike", orderBase: order);
            state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
            events.AddRange(evs);
            order += evs.Count;
        }
        return state;
    }

    private static BattleState ResolveSingle(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order)
    {
        if (ally.AttackSingle.Sum <= 0) return state;
        if (state.TargetEnemyIndex is not { } ti || ti < 0 || ti >= state.Enemies.Length) return state;

        var (updated, evs, _) = DealDamageHelper.Apply(
            ally, state.Enemies[ti],
            baseSum: ally.AttackSingle.Sum, addCount: ally.AttackSingle.AddCount,
            scopeNote: "single", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(ti, updated) };
        events.AddRange(evs);
        order += evs.Count;
        return state;
    }

    private static BattleState ResolveRandom(
        BattleState state, CombatActor ally, IRng rng, List<BattleEvent> events, ref int order)
    {
        if (ally.AttackRandom.Sum <= 0 || state.Enemies.Length == 0) return state;

        int idx = rng.NextInt(0, state.Enemies.Length); // 死亡敵含む（spec §4-4）
        var (updated, evs, _) = DealDamageHelper.Apply(
            ally, state.Enemies[idx],
            baseSum: ally.AttackRandom.Sum, addCount: ally.AttackRandom.AddCount,
            scopeNote: "random", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
        events.AddRange(evs);
        order += evs.Count;
        return state;
    }

    private static BattleState ResolveAll(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order)
    {
        if (ally.AttackAll.Sum <= 0) return state;

        var enemyIds = state.Enemies.Select(e => e.InstanceId).ToList();
        foreach (var eid in enemyIds)
        {
            int idx = -1;
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == eid) { idx = i; break; }
            if (idx < 0) continue;
            var current = state.Enemies[idx];

            var (updated, evs, _) = DealDamageHelper.Apply(
                ally, current,
                baseSum: ally.AttackAll.Sum, addCount: ally.AttackAll.AddCount,
                scopeNote: "all", orderBase: order);
            state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
            events.AddRange(evs);
            order += evs.Count;
        }
        return state;
    }

    private static CombatActor? FindAlly(BattleState state, string instanceId)
    {
        foreach (var a in state.Allies) if (a.InstanceId == instanceId) return a;
        return null;
    }
}
```

- [ ] **Step 4: 緑確認**

Run: `dotnet test`
Expected: 全テスト緑（既存 PlayerAttackingResolverTests + 新規 5 件）

- [ ] **Step 5: commit**

```bash
git add src/Core/Battle/Engine/PlayerAttackingResolver.cs \
        tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOmnistrikeTests.cs
git commit -m "feat(battle): add omnistrike combined firing (Phase 10.2.B Task 17)"
```

---

## Task 18: PlayerAttackingResolver の status 補正統合テスト

**Files:**
- Create: `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverStatusTests.cs`

> Resolver 経由で str / weak / vuln / dex の補正が正しく適用されることを E2E 検証。実装は既に Task 11 / 17 で完了。

- [ ] **Step 1: テストを書く**

`tests/Core.Tests/Battle/Engine/PlayerAttackingResolverStatusTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PlayerAttackingResolverStatusTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerAttacking,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    [Fact] public void Strength_boosts_single_attack()
    {
        // Sum=8, AddCount=2 (= 4 + 4 加算した結果), strength=3 → 8 + 2*3 = 14
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 3) with
        {
            AttackSingle = AttackPool.Empty.Add(4).Add(4),
        };
        var s = State(hero, BattleFixtures.Goblin(hp: 30));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(30 - 14, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Weak_reduces_attack()
    {
        // Sum=10, AddCount=1, weak=1 → floor(10*0.75) = 7
        var hero = BattleFixtures.WithWeak(BattleFixtures.Hero(), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(10),
        };
        var s = State(hero, BattleFixtures.Goblin(hp: 30));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(30 - 7, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Vulnerable_amplifies_damage_after_block()
    {
        // attack=10, target.Block=Sum=4, vuln=1 → rawDamage=6 → vuln 9
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(10) };
        var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with
        {
            Block = BlockPool.Empty.Add(4),
        };
        var s = State(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(30 - 9, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Dexterity_boosts_target_block_against_attack()
    {
        // attack=10, target.Block=Sum=2 AddCount=1, dex=5 → Display=7, absorbed=7, rawDamage=3
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(10) };
        var goblin = BattleFixtures.WithDexterity(BattleFixtures.Goblin(hp: 30), 5) with
        {
            Block = BlockPool.Empty.Add(2),
        };
        var s = State(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(30 - 3, next.Enemies[0].CurrentHp);
    }

    [Fact] public void All_corrections_combined_via_resolver()
    {
        // 入力: Sum=8 AddCount=2, str=3, weak=1, vuln=1, target.Block=Sum=2 AddCount=0, dex=0
        // attacker side: floor((8 + 2*3) * 0.75) = floor(14 * 0.75) = 10
        // block: 2 → absorbed=2, rawDamage=8
        // vuln: floor(8*1.5) = 12
        var hero = BattleFixtures.WithWeak(BattleFixtures.WithStrength(BattleFixtures.Hero(), 3), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(4).Add(4),
        };
        var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with
        {
            Block = BlockPool.Empty.Add(2),
        };
        var s = State(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(30 - 12, next.Enemies[0].CurrentHp);
    }
}
```

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~PlayerAttackingResolverStatusTests`
Expected: 5 passed

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/PlayerAttackingResolverStatusTests.cs
git commit -m "test(battle): integration test for status corrections in PlayerAttackingResolver (Phase 10.2.B Task 18)"
```

---

## Task 19: EnemyAttackingResolver の status 補正統合テスト

**Files:**
- Create: `tests/Core.Tests/Battle/Engine/EnemyAttackingResolverStatusTests.cs`

> 敵 attack で attacker 側 str / weak、target 側 vuln / dex が反映されることを検証。実装は既に Task 11 で完了。

- [ ] **Step 1: テストを書く**

`tests/Core.Tests/Battle/Engine/EnemyAttackingResolverStatusTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EnemyAttackingResolverStatusTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.EnemyAttacking,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    [Fact] public void Enemy_strength_boosts_per_effect_attack()
    {
        // 敵 attack 5 で、敵側 strength=3 → 1 effect で baseSum=5, addCount=1 → 5+1*3 = 8
        var hero = BattleFixtures.Hero(70);
        var goblin = BattleFixtures.WithStrength(BattleFixtures.Goblin(), 3);
        var def = BattleFixtures.GoblinDef(attack: 5);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = State(hero, goblin);
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), catalog);
        Assert.Equal(70 - 8, next.Allies[0].CurrentHp);
    }

    [Fact] public void Enemy_weak_reduces_attack()
    {
        // 敵 attack 8 で、敵側 weak=1 → floor(8 * 0.75) = 6
        var hero = BattleFixtures.Hero(70);
        var goblin = BattleFixtures.WithWeak(BattleFixtures.Goblin(), 1);
        var def = BattleFixtures.GoblinDef(attack: 8);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = State(hero, goblin);
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), catalog);
        Assert.Equal(70 - 6, next.Allies[0].CurrentHp);
    }

    [Fact] public void Hero_vulnerable_amplifies_damage()
    {
        // 敵 attack 10、hero vulnerable=1 → block 0 → rawDamage=10 → floor(10*1.5)=15
        var hero = BattleFixtures.WithVulnerable(BattleFixtures.Hero(70), 1);
        var goblin = BattleFixtures.Goblin();
        var def = BattleFixtures.GoblinDef(attack: 10);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = State(hero, goblin);
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), catalog);
        Assert.Equal(70 - 15, next.Allies[0].CurrentHp);
    }

    [Fact] public void Hero_dexterity_boosts_block()
    {
        // 敵 attack 10、hero block Sum=2 AddCount=1 dex=5 → Display=7、absorbed=7、rawDamage=3
        var hero = BattleFixtures.WithDexterity(BattleFixtures.Hero(70), 5) with
        {
            Block = BlockPool.Empty.Add(2),
        };
        var goblin = BattleFixtures.Goblin();
        var def = BattleFixtures.GoblinDef(attack: 10);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = State(hero, goblin);
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), catalog);
        Assert.Equal(70 - 3, next.Allies[0].CurrentHp);
    }
}
```

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~EnemyAttackingResolverStatusTests`
Expected: 4 passed

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/EnemyAttackingResolverStatusTests.cs
git commit -m "test(battle): integration test for status corrections in EnemyAttackingResolver (Phase 10.2.B Task 19)"
```

---

## Task 20: BattleDeterminismTests を status 含む戦闘で拡張

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs`

- [ ] **Step 1: 既存ファイルに新テストを追加**

`tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs` の最後に追記:

```csharp
[Fact] public void Same_seed_with_status_card_yields_identical_state()
{
    // strike + buff(strength) を含む 1 戦闘で 2 回回し、State 一致を検証
    BattleState RunWithBuff(int seed)
    {
        var rng = new SequentialRng((ulong)seed);
        var run = MakeRun();
        var cards = new[]
        {
            BattleFixtures.Strike(),
            new RoguelikeCardGame.Core.Cards.CardDefinition(
                "buff_self_str", "Buff Self Str", null,
                RoguelikeCardGame.Core.Cards.CardRarity.Common,
                RoguelikeCardGame.Core.Cards.CardType.Skill,
                Cost: 1, UpgradedCost: null,
                Effects: new[] { new RoguelikeCardGame.Core.Cards.CardEffect(
                    "buff", RoguelikeCardGame.Core.Cards.EffectScope.Self, null, 2,
                    Name: "strength") },
                UpgradedEffects: null, Keywords: null),
        };
        var cat = BattleFixtures.MinimalCatalog(cards: cards);
        var s = BattleEngine.Start(run, "enc_test", rng, cat);
        // 1 ターン目: buff カードを引いていれば打つ、そうでなければ EndTurn
        if (s.Hand.Length > 0)
        {
            var (s2, _) = BattleEngine.PlayCard(s, 0, 0, 0, rng, cat);
            var (s3, _) = BattleEngine.EndTurn(s2, rng, cat);
            return s3;
        }
        var (sNext, _) = BattleEngine.EndTurn(s, rng, cat);
        return sNext;
    }

    var a = RunWithBuff(seed: 100);
    var b = RunWithBuff(seed: 100);
    Assert.Equal(a, b);
}
```

> **注**: 既存 `MakeRun` ヘルパーを再利用。テストで `CardDefinition` を直接構築するため `using` を増やす必要があれば追加。

- [ ] **Step 2: 緑確認**

Run: `dotnet test --filter FullyQualifiedName~BattleDeterminismTests`
Expected: 既存 2 + 新規 1 = 3 passed

- [ ] **Step 3: commit**

```bash
git add tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs
git commit -m "test(battle): extend determinism with status card (Phase 10.2.B Task 20)"
```

---

## Task 21: 親 spec への補記反映

**Files:**
- Modify: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`

10.2.B spec §10 の 6 項目を親 spec の該当章に追記。各項目は既存 10.2.A 補記の直後に配置する。

- [ ] **Step 1: §3-2 CombatActor に Statuses 補記**

`docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` の §3-2 末尾に追記:

```markdown
> **Phase 10.2.B 補記**: `Statuses: ImmutableDictionary<string,int>` を 10.2.B で追加。
> `GetStatus(id)` 便宜プロパティで `Statuses.GetValueOrDefault(id, 0)` 相当を提供。
> 0 になった key は dict から削除する方針（不変条件: dict 内の値は常に > 0）。
```

- [ ] **Step 2: §3-3 AttackPool / BlockPool に補記**

§3-3 末尾（10.2.A 補記の直後）に追記:

```markdown
> **Phase 10.2.B 補記**: 10.2.B で `AttackPool.Display(strength, weak)` / `AttackPool.operator +` /
> `BlockPool.Display(dexterity)` / `BlockPool.Consume(incomingAttack, dexterity)` を追加。
> 10.2.A の `RawTotal` は internal 化（テスト・debug 用に温存）。
> 旧 `BlockPool.Consume(int)` は削除し、新シグネチャ `Consume(int, int)` のみとした。
```

- [ ] **Step 3: §4-2 ターン開始処理 に補記**

§4-2 末尾に追記:

```markdown
> **Phase 10.2.B 補記**: 10.2.B で 毒 tick / status countdown / 毒死で Outcome 確定（Victory / Defeat）を実装。
> 順序は spec §4-2 通り（Turn+1 → 毒ダメージ → 死亡判定 → countdown → Energy → Draw → TurnStart event）。
> tick 後の死亡判定は `TargetingAutoSwitch.Apply` を流用。
> countdown では `ApplyStatus` event を発火しない（negative delta は意味論が違う）。RemoveStatus（0 になった瞬間）のみ発火。
> `OnTurnStart` レリック発動（step 7）/ 召喚 Lifetime tick（step 4）は後続 phase。
```

- [ ] **Step 4: §4-4 DealDamage 擬似コード に補記**

§4-4 末尾に追記:

```markdown
> **Phase 10.2.B 補記**: 10.2.B で `DealDamageHelper.Apply(attacker, target, baseSum, addCount, scopeNote, orderBase)` シグネチャに更新。
> 攻撃側 strength × addCount / weak（×0.75 切捨）と受け側 vulnerable（×1.5 切捨）/ dexterity（Block 表示・消費）の補正を helper 内に統合。
> `AttackFire.Amount` は攻撃側補正後・Block 適用前、`DealDamage.Amount` は最終 HP 減算量。
> 切り捨ては全て integer 演算（`* 3 / 4`、`* 3 / 2`）で誤差なし。
```

- [ ] **Step 5: §5-1 EffectApplier に補記**

§5-1 末尾（10.2.A 補記の直後）に追記:

```markdown
> **Phase 10.2.B 補記**: 10.2.B で `buff` / `debuff` action に対応（Self / Single / Random / All の 4 scope 全対応）。
> `ReplaceActor` は memory feedback の InstanceId 検索ルールに準拠（10.2.A の `IndexOf` ベース latent bug を根治）。
> `Self` 以外の scope で `effect.Side == null` のときは ApplyEffect 内で例外を投げる。
> `effect.Name` が空の buff/debuff も例外（status id 必須）。
> 重ね掛けは existing.amount + new.amount。
```

- [ ] **Step 6: §9-7 BattleEventKind に補記**

§9-7 末尾（10.2.A 補記の直後）に追記:

```markdown
> **Phase 10.2.B 補記**: 10.2.B で `ApplyStatus = 9` / `RemoveStatus = 10` / `PoisonTick = 11` を追加（計 12 値）。
> ペイロード慣例:
> - `ApplyStatus`: Caster=付与主体, Target=対象, Amount=delta, Note=status_id
> - `RemoveStatus`: Caster=null, Target=対象, Amount=null, Note=status_id
> - `PoisonTick`: Caster=null, Target=対象, Amount=ダメージ量, Note="poison"
```

- [ ] **Step 7: 緑確認 + commit**

Run: `dotnet build && dotnet test`
Expected: 警告 0 / エラー 0、全テスト緑

```bash
git add docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md
git commit -m "docs(spec): amend Phase 10 spec for 10.2.B decisions (Task 21)"
```

---

## Task 22: 完了タグ作成と push

**Files:** なし（git tag 操作のみ）

- [ ] **Step 1: 最終ビルド・テスト確認**

Run:

```bash
dotnet build
dotnet test
```

Expected: 警告 0 / エラー 0、全テスト緑

- [ ] **Step 2: 手動プレイ確認（既存 BattlePlaceholder 経由フロー）**

`dotnet run --project src/Server` + `cd src/Client && npm run dev` で起動し、敵タイル進入 → 即勝利ボタン → 報酬画面が壊れていないことを確認（Phase 10.2.B は Core ロジック追加のみで Server / Client 接続は未着手のため）。

- [ ] **Step 3: タグ作成**

```bash
git tag -a phase10-2B-complete -m "Phase 10.2.B — 状態異常 + 遡及計算 完了

6 種の状態異常 (strength/dexterity/vulnerable/weak/omnistrike/poison) を導入。
AttackPool.Display(str, weak) / operator+ / BlockPool.Display(dex) /
Consume(in, dex) の遡及計算 API、buff/debuff action（4 scope）、
ターン開始 tick（毒ダメージ + countdown）、omnistrike 合算発射、
DealDamageHelper への str/weak/vuln/dex 補正統合を完成。

10.2.A の EffectApplier.ReplaceActor の IndexOf ベース latent bug も根治。
memory feedback の 2 ルール（BattleOutcome fully qualified /
state.Allies/Enemies 書き戻しは InstanceId 検索）を全 loop 箇所で遵守。"
```

- [ ] **Step 4: push**

```bash
git push origin master
git push origin phase10-2B-complete
```

- [ ] **Step 5: 完了確認**

Run:

```bash
git log -1 --oneline
git tag --list "phase10-2B-*"
```

Expected: 直近 commit が Task 21 の docs commit、タグ `phase10-2B-complete` が一覧に出る

---

## 完了後の状態（Phase 10.2.B 完了時）

- データモデル拡張済み: `CombatActor.Statuses` / `StatusDefinition` 6 種 / `StatusKind` / `StatusTickDirection` / `BattleEventKind` 12 値
- API 拡張済み: `AttackPool.Display(str, weak)` / `AttackPool.operator +` / `BlockPool.Display(dex)` / `BlockPool.Consume(in, dex)`
- `EffectApplier`: `attack` / `block` / `buff` / `debuff` の 4 action × Self / Single / Random / All の 4 scope（attack は 3 scope）対応
- `DealDamageHelper`: 攻撃補正の中核として str / weak / vuln / dex を統合（呼び出し側は (attacker, target, baseSum, addCount) を渡すだけ）
- `TurnStartProcessor`: ターン開始 tick（毒 → 死亡判定 → countdown → Energy → Draw）、毒死で Outcome 確定
- `PlayerAttackingResolver`: omnistrike 合算発射 + InstanceId 検索化
- `BattleEngine.EndTurn`: TurnStart 後 Outcome 確定で Phase 上書きをスキップ
- `EffectApplier.ReplaceActor`: InstanceId 検索化（10.2.A latent bug 根治）
- xUnit で「状態異常を含む 1 戦闘」が完走（attack / block / buff / debuff / 毒 tick / 力遡及 / 脆弱受け / omnistrike 合算 を含む E2E）
- 既存ゲームフロー（`BattlePlaceholder` 経由）は無傷
- 親 spec が 10.2.B の決定事項に合わせて補記済み
- `phase10-2B-complete` タグ push 済み

## 次フェーズ（Phase 10.2.C）への引き継ぎ

- コンボ機構（ComboCount / Wild / SuperWild / コスト軽減 / `comboMin` per-effect）
- `SetTarget(side, slotIndex)` アクションを `BattleEngine` に追加
- `BattleState` に `ComboCount` / `LastPlayedOrigCost` / `NextCardComboFreePass` フィールド追加
- `BattleEngine.PlayCard` のコンボ判定アルゴリズム実装（親 spec §6-3）
- `EffectApplier` の `ComboMin` フィルタ
- ターン終了処理でコンボリセット（`TurnEndProcessor` 拡張）
- 親 spec §6 / §7 を実装に対応
