# Phase 10.2.E — レリック発動 + UsePotion 戦闘内 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 10.2.E spec で定義された「レリック 4 新 Trigger 発火 + Implemented スキップ + UsePotion 第 6 公開 API + ConsumedPotion 反映」を実装し、Phase 10.2 の Core バトルロジック完成を達成する。

**Architecture:** `RelicTriggerProcessor` を統一ヘルパーとして新設し、4 Trigger を全 6 サイトで一貫した方式で発火。`BattleState` に `OwnedRelicIds` / `Potions` snapshot を追加して RunState 引き回しを回避。`BattleEngine.UsePotion` を第 6 公開 API として追加。`Finalize` で `state.Potions` を RunState に丸ごとコピー、`ConsumedPotionIds` は diff 派生。

**Tech Stack:** C# .NET 10 / xUnit / ImmutableArray / IRng (FakeRng for determinism)

**Spec:** [`../specs/2026-04-27-phase10-2E-relics-potions-design.md`](../specs/2026-04-27-phase10-2E-relics-potions-design.md)

---

## Conventions

- 全コミット末尾に `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>` を含める（`git commit -m "$(cat <<'EOF' ... EOF)"` HEREDOC で）
- `dotnet build` 警告 0 / エラー 0 を全タスク完了時に維持
- `dotnet test` 緑をタスク完了時に維持
- record 利用 / Core 独立性 / xUnit 先行
- BattleEngine 規約 (`memory/feedback_battle_engine_conventions.md`):
  - `BattleOutcome` は `RoguelikeCardGame.Core.Battle.State.BattleOutcome.X` で fully qualified
  - `state.Allies` / `state.Enemies` への書き戻しは InstanceId 検索（添字直書き禁止）
- 各タスク完了時に commit + push を Option B (タスク単位自動 commit) で実施

---

## Task 0: DrawHelper 共通化 (W5 修正)

**目的:** `TurnStartProcessor.DrawCards` (private) と `EffectApplier.ApplyDraw` の Fisher-Yates シャッフル + Hand 追加ロジック重複を解消。`HandCap = 10` を一元化。

**Files:**
- Create: `src/Core/Battle/Engine/DrawHelper.cs`
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs` (`DrawCards` 削除、`Process` 内呼出を `DrawHelper.Draw` に置換、`HandCap` / `DrawPerTurn` 定数整理)
- Modify: `src/Core/Battle/Engine/EffectApplier.cs` (`ApplyDraw` の inline ロジックを `DrawHelper.Draw` に置換)
- Test: `tests/Core.Tests/Battle/Engine/DrawHelperTests.cs`

- [ ] **Step 1: 失敗テスト `DrawHelperTests` を書く**

`tests/Core.Tests/Battle/Engine/DrawHelperTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class DrawHelperTests
{
    private static BattleState MakeStateWithPiles(
        ImmutableArray<BattleCardInstance> draw,
        ImmutableArray<BattleCardInstance> hand,
        ImmutableArray<BattleCardInstance> discard)
    {
        return new BattleState(
            Turn: 1,
            Phase: BattlePhase.PlayerInput,
            Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: draw, Hand: hand,
            DiscardPile: discard,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");
    }

    [Fact]
    public void Draw_takes_from_top_of_DrawPile()
    {
        var c1 = BattleFixtures.MakeBattleCard("strike", "c1");
        var c2 = BattleFixtures.MakeBattleCard("strike", "c2");
        var state = MakeStateWithPiles(
            ImmutableArray.Create(c1, c2),
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 1, new FakeRng(0), out int drawn);

        Assert.Equal(1, drawn);
        Assert.Single(result.Hand);
        Assert.Equal("c1", result.Hand[0].InstanceId);
        Assert.Single(result.DrawPile);
        Assert.Equal("c2", result.DrawPile[0].InstanceId);
    }

    [Fact]
    public void Draw_shuffles_discard_into_draw_when_drawpile_empty()
    {
        var c1 = BattleFixtures.MakeBattleCard("strike", "c1");
        var c2 = BattleFixtures.MakeBattleCard("strike", "c2");
        var state = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray.Create(c1, c2));

        var result = DrawHelper.Draw(state, 2, new FakeRng(0), out int drawn);

        Assert.Equal(2, drawn);
        Assert.Equal(2, result.Hand.Length);
        Assert.Empty(result.DiscardPile);
    }

    [Fact]
    public void Draw_caps_at_HandCap_10()
    {
        var hand = Enumerable.Range(0, 10)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"h{i}"))
            .ToImmutableArray();
        var draw = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "extra"));
        var state = MakeStateWithPiles(draw, hand, ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 5, new FakeRng(0), out int drawn);

        Assert.Equal(0, drawn);
        Assert.Equal(10, result.Hand.Length);
    }

    [Fact]
    public void Draw_returns_actuallyDrawn_when_count_exceeds_available()
    {
        var c1 = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = MakeStateWithPiles(
            ImmutableArray.Create(c1),
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 5, new FakeRng(0), out int drawn);

        Assert.Equal(1, drawn);
        Assert.Single(result.Hand);
    }

    [Fact]
    public void Draw_zero_count_is_noop()
    {
        var c1 = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = MakeStateWithPiles(
            ImmutableArray.Create(c1),
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 0, new FakeRng(0), out int drawn);

        Assert.Equal(0, drawn);
        Assert.Empty(result.Hand);
        Assert.Single(result.DrawPile);
    }

    [Fact]
    public void Draw_both_piles_empty_returns_zero()
    {
        var state = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 5, new FakeRng(0), out int drawn);

        Assert.Equal(0, drawn);
    }

    [Fact]
    public void Draw_is_deterministic_for_same_seed()
    {
        var cards = Enumerable.Range(0, 5)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"c{i}"))
            .ToImmutableArray();
        var state1 = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty, cards);
        var state2 = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty, cards);

        var r1 = DrawHelper.Draw(state1, 5, new FakeRng(42), out _);
        var r2 = DrawHelper.Draw(state2, 5, new FakeRng(42), out _);

        Assert.Equal(
            r1.Hand.Select(c => c.InstanceId),
            r2.Hand.Select(c => c.InstanceId));
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~DrawHelperTests
```
Expected: コンパイルエラー (`DrawHelper` 未定義)

- [ ] **Step 3: `DrawHelper.cs` 実装**

`src/Core/Battle/Engine/DrawHelper.cs`:
```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Hand 増分の共通ヘルパー。Phase 10.2.E (W5 修正) で TurnStartProcessor.DrawCards と
/// EffectApplier.ApplyDraw の Fisher-Yates シャッフル + Hand 追加ロジック重複を解消。
/// HandCap (10) もここで一元化。
/// </summary>
internal static class DrawHelper
{
    public const int HandCap = 10;

    /// <summary>
    /// state.Hand に最大 count 枚追加。山札不足時は捨札を Fisher-Yates シャッフルして補充。
    /// HandCap で打ち切り。実際にドローした枚数を out で返す。
    /// </summary>
    public static BattleState Draw(BattleState state, int count, IRng rng, out int actuallyDrawn)
    {
        actuallyDrawn = 0;
        if (count <= 0) return state;

        var hand = state.Hand.ToBuilder();
        var draw = state.DrawPile.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();

        for (int i = 0; i < count; i++)
        {
            if (hand.Count >= HandCap) break;
            if (draw.Count == 0)
            {
                if (discard.Count == 0) break;
                // Fisher-Yates shuffle: discard → draw
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
            actuallyDrawn++;
        }

        if (actuallyDrawn == 0) return state;

        return state with
        {
            Hand = hand.ToImmutable(),
            DrawPile = draw.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
    }
}
```

- [ ] **Step 4: テスト実行で緑確認**

```bash
dotnet test --filter FullyQualifiedName~DrawHelperTests
```
Expected: 7 tests pass

- [ ] **Step 5: `TurnStartProcessor.DrawCards` を `DrawHelper.Draw` に置換**

`src/Core/Battle/Engine/TurnStartProcessor.cs:213-257` の `DrawCards` メソッドと `ShuffleInto` メソッドを削除し、`Process` 内の呼出を更新:

`Process` 内の既存:
```csharp
s = DrawCards(s, DrawPerTurn, rng);
```

を以下に置換:
```csharp
s = DrawHelper.Draw(s, DrawPerTurn, rng, out _);
```

`HandCap = 10` 定数 (`TurnStartProcessor.cs:19`) は削除（`DrawHelper.HandCap` に集約）。

- [ ] **Step 6: `EffectApplier.ApplyDraw` を `DrawHelper.Draw` に置換**

`src/Core/Battle/Engine/EffectApplier.cs:257-306` の `ApplyDraw` メソッドを以下に書き換え:
```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDraw(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    if (effect.Scope != EffectScope.Self)
        throw new InvalidOperationException(
            $"draw requires Scope=Self, got {effect.Scope}");

    var newState = DrawHelper.Draw(state, effect.Amount, rng, out int actualDrawn);
    if (actualDrawn == 0) return (state, Array.Empty<BattleEvent>());

    var evs = new[] {
        new BattleEvent(BattleEventKind.Draw, Order: 0,
            CasterInstanceId: caster.InstanceId, Amount: actualDrawn),
    };
    return (newState, evs);
}
```

- [ ] **Step 7: 全テスト実行で緑確認**

```bash
dotnet build
dotnet test
```
Expected: 全テスト緑、警告 0 / エラー 0

- [ ] **Step 8: Commit + push**

```bash
git add src/Core/Battle/Engine/DrawHelper.cs src/Core/Battle/Engine/TurnStartProcessor.cs src/Core/Battle/Engine/EffectApplier.cs tests/Core.Tests/Battle/Engine/DrawHelperTests.cs
git commit -m "$(cat <<'EOF'
refactor(battle): extract DrawHelper to dedupe shuffle logic (Phase 10.2.E Task 0, W5)

10.2.D code review W5: TurnStartProcessor.DrawCards と EffectApplier.ApplyDraw の
Fisher-Yates シャッフル + Hand 追加ロジックが重複していた。DrawHelper.Draw に
集約し、HandCap=10 も一元化。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 1: summon InstanceId RNG ベース化 (W4 修正)

**目的:** `EffectApplier.ApplySummon` の InstanceId 生成を `Turn + Allies.Length` 方式から RNG ベース (`Turn + rng.NextInt(...).x`) に切替え、レリック由来 summon との衝突を回避。Determinism 維持。

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs:499` (newInstanceId 生成行)
- Modify: `tests/Core.Tests/Battle/Engine/EffectApplierSummonTests.cs` (InstanceId アサーションを prefix のみに緩和)
- Test: `tests/Core.Tests/Battle/Engine/EffectApplierSummonInstanceIdTests.cs`

- [ ] **Step 1: 失敗テスト `EffectApplierSummonInstanceIdTests` を書く**

`tests/Core.Tests/Battle/Engine/EffectApplierSummonInstanceIdTests.cs`:
```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierSummonInstanceIdTests
{
    private static BattleState MakeStateWithHero()
    {
        return new BattleState(
            Turn: 1,
            Phase: BattlePhase.PlayerInput,
            Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
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

    [Fact]
    public void Summon_InstanceId_starts_with_summon_inst_turn_prefix()
    {
        var state = MakeStateWithHero();
        var catalog = BattleFixtures.MinimalCatalog();
        var hero = state.Allies[0];
        var effect = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");

        var (afterState, _) = EffectApplier.Apply(state, hero, effect, new FakeRng(42), catalog);

        Assert.Equal(2, afterState.Allies.Length);
        var summon = afterState.Allies[1];
        Assert.StartsWith("summon_inst_1_", summon.InstanceId);
    }

    [Fact]
    public void Two_consecutive_summons_in_same_turn_have_unique_InstanceIds()
    {
        var state = MakeStateWithHero();
        var catalog = BattleFixtures.MinimalCatalog();
        var hero = state.Allies[0];
        var effect = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var rng = new FakeRng(42);

        var (s1, _) = EffectApplier.Apply(state, hero, effect, rng, catalog);
        var (s2, _) = EffectApplier.Apply(s1, hero, effect, rng, catalog);

        Assert.Equal(3, s2.Allies.Length);
        Assert.NotEqual(s2.Allies[1].InstanceId, s2.Allies[2].InstanceId);
    }

    [Fact]
    public void Summon_InstanceId_is_deterministic_for_same_seed()
    {
        var state = MakeStateWithHero();
        var catalog = BattleFixtures.MinimalCatalog();
        var hero = state.Allies[0];
        var effect = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");

        var (a, _) = EffectApplier.Apply(state, hero, effect, new FakeRng(42), catalog);
        var (b, _) = EffectApplier.Apply(state, hero, effect, new FakeRng(42), catalog);

        Assert.Equal(a.Allies[1].InstanceId, b.Allies[1].InstanceId);
    }

    [Fact]
    public void Summon_InstanceId_differs_for_different_seeds()
    {
        var state = MakeStateWithHero();
        var catalog = BattleFixtures.MinimalCatalog();
        var hero = state.Allies[0];
        var effect = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");

        var (a, _) = EffectApplier.Apply(state, hero, effect, new FakeRng(1), catalog);
        var (b, _) = EffectApplier.Apply(state, hero, effect, new FakeRng(2), catalog);

        Assert.NotEqual(a.Allies[1].InstanceId, b.Allies[1].InstanceId);
    }
}
```

- [ ] **Step 2: テスト実行で部分的に失敗確認**

```bash
dotnet test --filter FullyQualifiedName~EffectApplierSummonInstanceIdTests
```
Expected: 2 つは緑（既存実装でも prefix と decision は通る）、`Summon_InstanceId_differs_for_different_seeds` が失敗（現状は seed 無依存）

- [ ] **Step 3: `EffectApplier.ApplySummon` の InstanceId 生成を RNG ベースに変更**

`src/Core/Battle/Engine/EffectApplier.cs:499` を以下に置換:
```csharp
// 旧: string newInstanceId = $"summon_inst_{state.Turn}_{state.Allies.Length}";
string newInstanceId = $"summon_inst_{state.Turn}_{rng.NextInt(0, 1 << 30):x}";
```

- [ ] **Step 4: 影響を受ける既存テストの InstanceId アサーションを緩和**

```bash
grep -rn "summon_inst_" tests/Core.Tests
```
で hardcoded `summon_inst_X_Y` 期待値を含むテストを抽出。例えば `EffectApplierSummonTests.cs` に `Assert.Equal("summon_inst_1_1", ...)` のようなアサーションがあれば、`Assert.StartsWith("summon_inst_1_", ...)` または `Assert.Matches(@"^summon_inst_1_[0-9a-f]+$", ...)` に置換。

- [ ] **Step 5: 全テスト実行で緑確認**

```bash
dotnet build
dotnet test
```
Expected: 全テスト緑

- [ ] **Step 6: Commit + push**

```bash
git add src/Core/Battle/Engine/EffectApplier.cs tests/Core.Tests/Battle/Engine/EffectApplierSummonInstanceIdTests.cs tests/Core.Tests/Battle/Engine/EffectApplierSummonTests.cs
git commit -m "$(cat <<'EOF'
refactor(battle): summon InstanceId now RNG-based to prevent collision (Phase 10.2.E Task 1, W4)

10.2.D code review W4: summon_inst_{Turn}_{Allies.Length} 方式は 1 カード=1 召喚前提
で衝突しないが、10.2.E でレリック発動経路から同ターン複数 summon が起こる可能性が
ある。RNG ベース ID に切替えて衝突を予防。Determinism は IRng 注入で seed 一致時
同 ID を保証。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 2: BattleState に OwnedRelicIds / Potions 追加 + Start で snapshot

**目的:** spec §2-1 に従い `BattleState` に 2 フィールド追加。`BattleEngine.Start` で `RunState.Relics` と `RunState.Potions` から snapshot。全 `new BattleState(...)` fixture / テストを追従。

**Files:**
- Modify: `src/Core/Battle/State/BattleState.cs` (+ OwnedRelicIds / Potions)
- Modify: `src/Core/Battle/Engine/BattleEngine.cs` (`Start` で snapshot)
- Modify: `tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` (`MinimalState` ヘルパー追加 / `MinimalCatalog` で Relic / Potion 受入)
- Modify: 既存全テストの `new BattleState(...)` 呼出箇所（多数）に 2 引数追加

- [ ] **Step 1: 失敗テスト `BattleStateInvariantTests` に追加**

`tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs` に以下のテストメソッドを追加:
```csharp
[Fact]
public void OwnedRelicIds_is_empty_by_default_for_test_fixture()
{
    var s = BattleFixtures.MinimalState();
    Assert.Equal(0, s.OwnedRelicIds.Length);
}

[Fact]
public void Potions_length_matches_PotionSlotCount_in_test_fixture()
{
    var s = BattleFixtures.MinimalState(potions: ImmutableArray.Create("", "", ""));
    Assert.Equal(3, s.Potions.Length);
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~BattleStateInvariantTests.OwnedRelicIds
```
Expected: コンパイルエラー (`OwnedRelicIds` 未定義 / `MinimalState` 未定義)

- [ ] **Step 3: `BattleState.cs` に 2 フィールド追加**

`src/Core/Battle/State/BattleState.cs` の record に `NextCardComboFreePass` の直後・`EncounterId` の前に追加:
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
    ImmutableArray<string> OwnedRelicIds,         // ← 10.2.E 追加
    ImmutableArray<string> Potions,               // ← 10.2.E 追加
    string EncounterId);
```

- [ ] **Step 4: `BattleFixtures.MinimalState` ヘルパーと `MinimalCatalog` 拡張**

`tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs` に追加:
```csharp
// MinimalCatalog に relics / potions パラメータ追加
public static DataCatalog MinimalCatalog(
    IEnumerable<CardDefinition>? cards = null,
    IEnumerable<EnemyDefinition>? enemies = null,
    IEnumerable<EncounterDefinition>? encounters = null,
    IEnumerable<UnitDefinition>? units = null,
    IEnumerable<RoguelikeCardGame.Core.Relics.RelicDefinition>? relics = null,    // ← 10.2.E
    IEnumerable<RoguelikeCardGame.Core.Potions.PotionDefinition>? potions = null) // ← 10.2.E
{
    var cardDict = (cards ?? new[] { Strike(), Defend() }).ToDictionary(c => c.Id);
    var enemyDict = (enemies ?? new[] { GoblinDef() }).ToDictionary(e => e.Id);
    var encDict = (encounters ?? new[] { SingleGoblinEncounter() }).ToDictionary(e => e.Id);
    var unitDict = (units ?? new[] { MinionDef() }).ToDictionary(u => u.Id);
    var relicDict = (relics ?? System.Array.Empty<RoguelikeCardGame.Core.Relics.RelicDefinition>())
        .ToDictionary(r => r.Id);
    var potionDict = (potions ?? System.Array.Empty<RoguelikeCardGame.Core.Potions.PotionDefinition>())
        .ToDictionary(p => p.Id);
    return new DataCatalog(
        Cards: cardDict,
        Relics: relicDict,
        Potions: potionDict,
        Enemies: enemyDict,
        Encounters: encDict,
        RewardTables: new Dictionary<string, RewardTable>(),
        Characters: new Dictionary<string, CharacterDefinition>(),
        Events: new Dictionary<string, RoguelikeCardGame.Core.Events.EventDefinition>(),
        Units: unitDict);
}

// RelicDefinition factory
public static RoguelikeCardGame.Core.Relics.RelicDefinition Relic(
    string id = "test_relic",
    RoguelikeCardGame.Core.Relics.RelicTrigger trigger = RoguelikeCardGame.Core.Relics.RelicTrigger.OnTurnStart,
    bool implemented = true,
    params CardEffect[] effects) =>
    new(id, id, CardRarity.Common, trigger, effects, "", implemented);

// PotionDefinition factory
public static RoguelikeCardGame.Core.Potions.PotionDefinition Potion(
    string id = "test_potion",
    params CardEffect[] effects) =>
    new(id, id, CardRarity.Common, effects);

// MinimalState ヘルパー（OwnedRelicIds / Potions snapshot をデフォルト付きで構築）
public static BattleState MinimalState(
    ImmutableArray<CombatActor>? allies = null,
    ImmutableArray<CombatActor>? enemies = null,
    int turn = 1,
    BattlePhase phase = BattlePhase.PlayerInput,
    int energy = 3,
    int energyMax = 3,
    ImmutableArray<BattleCardInstance>? hand = null,
    ImmutableArray<BattleCardInstance>? draw = null,
    ImmutableArray<BattleCardInstance>? discard = null,
    ImmutableArray<string>? ownedRelicIds = null,
    ImmutableArray<string>? potions = null)
{
    return new BattleState(
        Turn: turn,
        Phase: phase,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: allies ?? ImmutableArray.Create(Hero()),
        Enemies: enemies ?? ImmutableArray.Create(Goblin()),
        TargetAllyIndex: 0,
        TargetEnemyIndex: 0,
        Energy: energy, EnergyMax: energyMax,
        DrawPile: draw ?? ImmutableArray<BattleCardInstance>.Empty,
        Hand: hand ?? ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: discard ?? ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
        OwnedRelicIds: ownedRelicIds ?? ImmutableArray<string>.Empty,
        Potions: potions ?? ImmutableArray<string>.Empty,
        EncounterId: "enc_test");
}
```

- [ ] **Step 5: `BattleEngine.Start` で snapshot を追加**

`src/Core/Battle/Engine/BattleEngine.cs:69-87` の `new BattleState(...)` を以下に書き換え（`OwnedRelicIds` / `Potions` を snapshot として追加、`EncounterId` の直前に配置）:
```csharp
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
    SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
    PowerCards: ImmutableArray<BattleCardInstance>.Empty,
    ComboCount: 0,
    LastPlayedOrigCost: null,
    NextCardComboFreePass: false,
    OwnedRelicIds: run.Relics.ToImmutableArray(),                // 10.2.E
    Potions: run.Potions,                                        // 10.2.E (既に ImmutableArray<string>)
    EncounterId: encounterId);
```

- [ ] **Step 6: 全 `new BattleState(...)` 呼出箇所を grep して 2 引数追加**

```bash
grep -rn "new BattleState(" src tests
```
すべての箇所で `EncounterId` の直前に以下 2 行を追加:
```csharp
OwnedRelicIds: ImmutableArray<string>.Empty,
Potions: ImmutableArray<string>.Empty,
```
※ `MinimalState` ヘルパーを使う既存テストはヘルパー側で吸収されるので追加不要。`new BattleState(` を直接書いているテストのみ更新。

- [ ] **Step 7: `dotnet build` で全コンパイル確認**

```bash
dotnet build
```
Expected: 警告 0 / エラー 0

- [ ] **Step 8: 全テスト実行で緑確認**

```bash
dotnet test
```
Expected: 全テスト緑

- [ ] **Step 9: Commit + push**

```bash
git add src/Core/Battle/State/BattleState.cs src/Core/Battle/Engine/BattleEngine.cs tests/Core.Tests/Battle/Fixtures/BattleFixtures.cs tests/Core.Tests/Battle/State/BattleStateInvariantTests.cs tests/
git commit -m "$(cat <<'EOF'
feat(battle): BattleState に OwnedRelicIds / Potions snapshot 追加 (Phase 10.2.E Task 2)

spec §2-1 に従い BattleState に 2 フィールド追加。BattleEngine.Start で
RunState.Relics と RunState.Potions から snapshot。BattleFixtures.MinimalState
ヘルパー新設で全 new BattleState(...) fixture を追従。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 3: BattleEventKind に UsePotion=19 追加 + Start シグネチャ変更

**目的:** `UsePotion = 19` を enum に追加。`BattleEngine.Start` の戻り値を `(BattleState, IReadOnlyList<BattleEvent>)` に変更し、events 列に `BattleStart` + TurnStart events を含める。10.2.E 後続タスクで OnBattleStart レリック events もここに追加。

**Files:**
- Modify: `src/Core/Battle/Events/BattleEventKind.cs` (+UsePotion=19)
- Modify: `src/Core/Battle/Engine/BattleEngine.cs` (`Start` 戻り値 tuple 化)
- Modify: `tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs` (戻り値 tuple 追従)
- 影響: `BattlePlaceholder` 等は `BattleEngine.Start` を呼ばないので無傷（grep で確認）

- [ ] **Step 1: 失敗テスト追加（`BattleEngineStartRelicTests` の最初の小テストとして）**

`tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs` 末尾に追加:
```csharp
[Fact]
public void Start_returns_events_with_BattleStart_and_TurnStart()
{
    var run = MakeRunForTest();
    var catalog = BattleFixtures.MinimalCatalog();

    var (state, events) = BattleEngine.Start(run, "enc_test", new FakeRng(0), catalog);

    Assert.Equal(BattlePhase.PlayerInput, state.Phase);
    Assert.Contains(events, e => e.Kind == BattleEventKind.BattleStart);
    Assert.Contains(events, e => e.Kind == BattleEventKind.TurnStart);
}
```
※ `MakeRunForTest` は既存ヘルパー。なければ既存 `BattleEngineStartTests.cs` の Run 構築パターンに従う。

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~BattleEngineStartTests.Start_returns_events
```
Expected: コンパイルエラー (`BattleEngine.Start` は `BattleState` のみを返す)

- [ ] **Step 3: `BattleEventKind.cs` に `UsePotion = 19` 追加**

`src/Core/Battle/Events/BattleEventKind.cs` を以下のように修正:
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
    UsePotion     = 19,    // 10.2.E
}
```

- [ ] **Step 4: `BattleEngine.Start` シグネチャ変更**

`src/Core/Battle/Engine/BattleEngine.cs` の `Start` メソッドを以下に書き換え:
```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Start(
    RunState run, string encounterId, IRng rng, DataCatalog catalog)
{
    if (!catalog.TryGetEncounter(encounterId, out var encounter))
        throw new System.InvalidOperationException($"encounter '{encounterId}' not found in catalog");

    // ...既存の hero / enemies / deckCards / drawPile 構築...
    // (省略: 10.2.D 既存コードそのまま)

    // 4. 初期 BattleState（OwnedRelicIds / Potions snapshot 含む、Task 2 で追加済み）
    var initial = new BattleState(/* ...上記 Task 2 と同じ... */);

    var events = new List<BattleEvent>();
    int order = 0;

    // BattleStart event 発火
    events.Add(new BattleEvent(
        BattleEventKind.BattleStart, Order: order++,
        Note: encounterId));

    // 5. ターン 1 開始処理
    var (afterTurnStart, evsTurnStart) = TurnStartProcessor.Process(initial, rng);  // ← 後の Task 5 で catalog 引数追加
    foreach (var ev in evsTurnStart)
    {
        events.Add(ev with { Order = order++ });
    }

    return (afterTurnStart, events);
}
```

注: `TurnStartProcessor.Process` の catalog 引数追加は Task 5 で行う。OnBattleStart レリック発動は Task 10 で追加。

- [ ] **Step 5: 既存 `BattleEngineStartTests` の全テストを `var (state, _) = Start(...)` 形式に修正**

`tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs` 内の全 `BattleEngine.Start(...)` 呼出を以下のように更新:
```csharp
// 旧
var state = BattleEngine.Start(run, "enc_test", new FakeRng(0), catalog);
// 新
var (state, _) = BattleEngine.Start(run, "enc_test", new FakeRng(0), catalog);
```

`BattlePlaceholder` 等で `BattleEngine.Start` を呼ぶ箇所は無いはず。念のため:
```bash
grep -rn "BattleEngine.Start(" src tests
```
で確認、tests 以外で呼ぶ箇所があれば追従。

- [ ] **Step 6: 全テスト実行で緑確認**

```bash
dotnet build
dotnet test
```
Expected: 全テスト緑、新規テスト含めて緑

- [ ] **Step 7: Commit + push**

```bash
git add src/Core/Battle/Events/BattleEventKind.cs src/Core/Battle/Engine/BattleEngine.cs tests/Core.Tests/Battle/Engine/BattleEngineStartTests.cs
git commit -m "$(cat <<'EOF'
feat(battle): Start シグネチャに events 戻り値追加, UsePotion event Kind 追加 (Phase 10.2.E Task 3)

spec §5-1 / §2-3: BattleEngine.Start を (BattleState, IReadOnlyList<BattleEvent>)
に変更し、BattleStart + TurnStart events を返すように。OnBattleStart レリック
発動 events も後続 Task 10 で追加。BattleEventKind に UsePotion=19 を追加（計
20 値）。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 4: RelicTriggerProcessor 新設 + 単体テスト

**目的:** spec §3 で定義された統一ヘルパー `RelicTriggerProcessor` を新設し、4 Trigger 共通の発動ロジック（所持順 + Implemented:false skip + caster=hero 検索 + Note prefix `relic:<id>`）を集約。単体テストで挙動を網羅。

**Files:**
- Create: `src/Core/Battle/Engine/RelicTriggerProcessor.cs`
- Test: `tests/Core.Tests/Battle/Engine/RelicTriggerProcessorTests.cs`

- [ ] **Step 1: 失敗テスト `RelicTriggerProcessorTests` を書く**

`tests/Core.Tests/Battle/Engine/RelicTriggerProcessorTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class RelicTriggerProcessorTests
{
    [Fact]
    public void Fire_with_no_owned_relics_returns_state_unchanged_and_empty_events()
    {
        var state = BattleFixtures.MinimalState();
        var catalog = BattleFixtures.MinimalCatalog();

        var (after, events) = RelicTriggerProcessor.Fire(
            state, RelicTrigger.OnTurnStart, catalog, new FakeRng(0), orderStart: 0);

        Assert.Same(state.Allies, after.Allies);
        Assert.Empty(events);
    }

    [Fact]
    public void Fire_with_matching_trigger_applies_relic_effects()
    {
        var blockRelic = BattleFixtures.Relic("block_relic", RelicTrigger.OnTurnStart,
            true, new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { blockRelic });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("block_relic"));

        var (after, events) = RelicTriggerProcessor.Fire(
            state, RelicTrigger.OnTurnStart, catalog, new FakeRng(0), orderStart: 0);

        Assert.Equal(5, after.Allies[0].Block.RawTotal);
        Assert.Single(events);
        Assert.Equal(BattleEventKind.GainBlock, events[0].Kind);
        Assert.Equal("relic:block_relic", events[0].Note);
    }

    [Fact]
    public void Fire_with_Implemented_false_relic_is_noop()
    {
        var unimpl = BattleFixtures.Relic("unimpl", RelicTrigger.OnTurnStart,
            implemented: false,
            new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { unimpl });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("unimpl"));

        var (after, events) = RelicTriggerProcessor.Fire(
            state, RelicTrigger.OnTurnStart, catalog, new FakeRng(0), orderStart: 0);

        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.Empty(events);
    }

    [Fact]
    public void Fire_with_mismatched_trigger_skips_relic()
    {
        var ts = BattleFixtures.Relic("ts", RelicTrigger.OnTurnStart,
            true, new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { ts });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("ts"));

        var (after, events) = RelicTriggerProcessor.Fire(
            state, RelicTrigger.OnTurnEnd, catalog, new FakeRng(0), orderStart: 0);

        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.Empty(events);
    }

    [Fact]
    public void Fire_unknown_relicId_in_catalog_silent_skip()
    {
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("unknown_id"));
        var catalog = BattleFixtures.MinimalCatalog();

        var (after, events) = RelicTriggerProcessor.Fire(
            state, RelicTrigger.OnTurnStart, catalog, new FakeRng(0), orderStart: 0);

        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.Empty(events);
    }

    [Fact]
    public void Fire_invokes_relics_in_owned_order()
    {
        var r1 = BattleFixtures.Relic("r1", RelicTrigger.OnTurnStart, true,
            new CardEffect("block", EffectScope.Self, null, 3));
        var r2 = BattleFixtures.Relic("r2", RelicTrigger.OnTurnStart, true,
            new CardEffect("block", EffectScope.Self, null, 7));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { r1, r2 });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("r1", "r2"));

        var (after, events) = RelicTriggerProcessor.Fire(
            state, RelicTrigger.OnTurnStart, catalog, new FakeRng(0), orderStart: 0);

        Assert.Equal(10, after.Allies[0].Block.RawTotal);
        Assert.Equal(2, events.Count);
        Assert.Equal("relic:r1", events[0].Note);
        Assert.Equal("relic:r2", events[1].Note);
    }

    [Fact]
    public void Fire_when_hero_dead_returns_noop()
    {
        var dead = BattleFixtures.Hero(hp: 0);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(dead),
            ownedRelicIds: ImmutableArray.Create("any"));
        var catalog = BattleFixtures.MinimalCatalog();

        var (after, events) = RelicTriggerProcessor.Fire(
            state, RelicTrigger.OnTurnStart, catalog, new FakeRng(0), orderStart: 0);

        Assert.Empty(events);
    }

    [Fact]
    public void FireOnEnemyDeath_attaches_deadEnemy_to_Note()
    {
        var relic = BattleFixtures.Relic("od_relic", RelicTrigger.OnEnemyDeath, true,
            new CardEffect("block", EffectScope.Self, null, 2));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("od_relic"));

        var (after, events) = RelicTriggerProcessor.FireOnEnemyDeath(
            state, "enemy_inst_X", catalog, new FakeRng(0), orderStart: 0);

        Assert.Equal(2, after.Allies[0].Block.RawTotal);
        Assert.Single(events);
        Assert.Equal("relic:od_relic;deadEnemy:enemy_inst_X", events[0].Note);
    }

    [Fact]
    public void Fire_orders_events_starting_from_orderStart()
    {
        var r = BattleFixtures.Relic("r", RelicTrigger.OnTurnStart, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { r });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("r"));

        var (_, events) = RelicTriggerProcessor.Fire(
            state, RelicTrigger.OnTurnStart, catalog, new FakeRng(0), orderStart: 7);

        Assert.Single(events);
        Assert.Equal(7, events[0].Order);
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~RelicTriggerProcessorTests
```
Expected: コンパイルエラー (`RelicTriggerProcessor` 未定義)

- [ ] **Step 3: `RelicTriggerProcessor.cs` 実装**

`src/Core/Battle/Engine/RelicTriggerProcessor.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 戦闘内 4 Trigger（OnBattleStart / OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）の
/// レリック発動を統一的に処理する internal static helper。
/// 所持順発動 (state.OwnedRelicIds 配列順) + Implemented:false スキップ + caster=hero を集約。
/// 親 spec §8-2 / 10.2.E spec §3 参照。
/// </summary>
internal static class RelicTriggerProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Fire(
        BattleState state, RelicTrigger trigger,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, trigger, deadEnemyInstanceId: null, catalog, rng, orderStart);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnEnemyDeath(
        BattleState state, string deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, RelicTrigger.OnEnemyDeath, deadEnemyInstanceId, catalog, rng, orderStart);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireInternal(
        BattleState state, RelicTrigger trigger,
        string? deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        var events = new List<BattleEvent>();
        var s = state;
        int order = orderStart;

        var caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
        if (caster is null || !caster.IsAlive) return (s, events);

        foreach (var relicId in s.OwnedRelicIds)
        {
            if (!catalog.TryGetRelic(relicId, out var def)) continue;
            if (!def.Implemented) continue;
            if (def.Trigger != trigger) continue;

            foreach (var eff in def.Effects)
            {
                var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
                s = afterEff;
                foreach (var ev in evs)
                {
                    var basePrefix = $"relic:{relicId}";
                    var suffix = deadEnemyInstanceId is not null
                        ? $";deadEnemy:{deadEnemyInstanceId}"
                        : "";
                    var newNote = string.IsNullOrEmpty(ev.Note)
                        ? basePrefix + suffix
                        : $"{ev.Note};{basePrefix}{suffix}";
                    events.Add(ev with { Order = order, Note = newNote });
                    order++;
                }
                caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
                if (caster is null || !caster.IsAlive) break;
            }

            if (caster is null || !caster.IsAlive) break;
        }

        return (s, events);
    }
}
```

- [ ] **Step 4: テスト実行で緑確認**

```bash
dotnet test --filter FullyQualifiedName~RelicTriggerProcessorTests
```
Expected: 9 tests pass

- [ ] **Step 5: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑、警告 0

- [ ] **Step 6: Commit + push**

```bash
git add src/Core/Battle/Engine/RelicTriggerProcessor.cs tests/Core.Tests/Battle/Engine/RelicTriggerProcessorTests.cs
git commit -m "$(cat <<'EOF'
feat(battle): RelicTriggerProcessor 新設 (Phase 10.2.E Task 4)

spec §3: 4 Trigger (OnBattleStart/OnTurnStart/OnTurnEnd/OnCardPlay/OnEnemyDeath)
の統一ヘルパー。所持順 + Implemented:false スキップ + caster=hero 検索 + Note
prefix relic:<id> を集約。後続 Task 5-10 で各発火サイトから呼出。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 5: TurnStartProcessor.Process に catalog 追加 + OnTurnStart 発火 (step 8)

**目的:** `TurnStartProcessor.Process` のシグネチャに `DataCatalog catalog` を追加し、step 8 (Draw 後 / TurnStart event 前) で `RelicTriggerProcessor.Fire(state, RelicTrigger.OnTurnStart, ...)` を呼ぶ。

**Files:**
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs` (sig 変更 + step 8 追加)
- Modify: `src/Core/Battle/Engine/BattleEngine.cs` (`Start` で catalog 渡す)
- Modify: `src/Core/Battle/Engine/BattleEngine.EndTurn.cs` (`TurnStartProcessor.Process` 呼出に catalog 渡す)
- Modify: 既存 TurnStart 系テスト全て（catalog 引数追加追従）
- Test: `tests/Core.Tests/Battle/Engine/TurnStartProcessorOnTurnStartTests.cs`

- [ ] **Step 1: 失敗テスト `TurnStartProcessorOnTurnStartTests` を書く**

`tests/Core.Tests/Battle/Engine/TurnStartProcessorOnTurnStartTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnStartProcessorOnTurnStartTests
{
    [Fact]
    public void OnTurnStart_relic_fires_after_Draw_before_TurnStart_event()
    {
        var relic = BattleFixtures.Relic("r", RelicTrigger.OnTurnStart, true,
            new CardEffect("gainEnergy", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("r"),
            energy: 0, energyMax: 3);

        var (after, events) = TurnStartProcessor.Process(state, new FakeRng(0), catalog);

        // Energy reset to EnergyMax (=3), then OnTurnStart relic adds 1 → final 4
        Assert.Equal(4, after.Energy);
        // events 順序: ... TurnStart event 最後
        var lastEv = events[^1];
        Assert.Equal(BattleEventKind.TurnStart, lastEv.Kind);
        // GainEnergy event は TurnStart event より前
        var gainIdx = events.ToList().FindIndex(e => e.Kind == BattleEventKind.GainEnergy);
        var tsIdx = events.ToList().FindIndex(e => e.Kind == BattleEventKind.TurnStart);
        Assert.True(gainIdx < tsIdx);
    }

    [Fact]
    public void OnTurnStart_with_no_relics_keeps_existing_behavior()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState(energy: 0);

        var (after, events) = TurnStartProcessor.Process(state, new FakeRng(0), catalog);

        Assert.Equal(after.EnergyMax, after.Energy);
        Assert.Contains(events, e => e.Kind == BattleEventKind.TurnStart);
    }

    [Fact]
    public void OnTurnStart_attack_relic_adds_to_hero_AttackPool()
    {
        var relic = BattleFixtures.Relic("attack_r", RelicTrigger.OnTurnStart, true,
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("attack_r"));

        var (after, _) = TurnStartProcessor.Process(state, new FakeRng(0), catalog);

        Assert.Equal(3, after.Allies[0].AttackAll.Sum);
    }

    [Fact]
    public void OnTurnStart_Implemented_false_skipped()
    {
        var unimpl = BattleFixtures.Relic("unimpl", RelicTrigger.OnTurnStart,
            implemented: false,
            new CardEffect("gainEnergy", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { unimpl });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("unimpl"),
            energy: 0, energyMax: 3);

        var (after, _) = TurnStartProcessor.Process(state, new FakeRng(0), catalog);

        Assert.Equal(3, after.Energy); // Energy 5 加算なし
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~TurnStartProcessorOnTurnStartTests
```
Expected: コンパイルエラー (`TurnStartProcessor.Process` は catalog 引数を取らない)

- [ ] **Step 3: `TurnStartProcessor.Process` シグネチャ変更 + step 8 追加**

`src/Core/Battle/Engine/TurnStartProcessor.cs` の `Process` を以下に書き換え（既存処理は維持しつつ、シグネチャと末尾 OnTurnStart 発火を追加）:
```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Process(
    BattleState state, IRng rng, DataCatalog catalog)
{
    var s = state with { Turn = state.Turn + 1 };
    var events = new List<BattleEvent>();
    int order = 0;

    // Step 2-5: 既存 (Poison tick / SummonCleanup / Death detection / Status countdown / Lifetime tick / SummonCleanup)
    // ... 既存コードそのまま（Outcome 確定の早期 return 含む）...

    // Step 6: Energy = EnergyMax
    s = s with { Energy = s.EnergyMax };

    // Step 7: Draw
    s = DrawHelper.Draw(s, DrawPerTurn, rng, out _);

    // Step 8: OnTurnStart レリック発動 (10.2.E)
    var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
        s, RelicTrigger.OnTurnStart, catalog, rng, orderStart: order);
    s = afterRelic;
    foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

    // Step 9: TurnStart event
    events.Add(new BattleEvent(BattleEventKind.TurnStart, Order: order++, Note: $"turn={s.Turn}"));
    return (s, events);
}
```

`using RoguelikeCardGame.Core.Data;` と `using RoguelikeCardGame.Core.Relics;` の import を追加。

- [ ] **Step 4: `BattleEngine.Start` から呼出時に catalog 渡す**

`src/Core/Battle/Engine/BattleEngine.cs` の `Start` 内:
```csharp
// 旧: var (afterTurnStart, evsTurnStart) = TurnStartProcessor.Process(initial, rng);
var (afterTurnStart, evsTurnStart) = TurnStartProcessor.Process(initial, rng, catalog);
```

- [ ] **Step 5: `BattleEngine.EndTurn` から呼出時に catalog 渡す**

`src/Core/Battle/Engine/BattleEngine.EndTurn.cs:57`:
```csharp
// 旧: var (afterStart, evsStart) = TurnStartProcessor.Process(s, rng);
var (afterStart, evsStart) = TurnStartProcessor.Process(s, rng, catalog);
```

- [ ] **Step 6: 既存 TurnStart 系テスト全て catalog 引数追加追従**

```bash
grep -rn "TurnStartProcessor.Process(" tests
```
で全呼出箇所を抽出し、第 3 引数に `BattleFixtures.MinimalCatalog()` を追加（既存テストの fixture に合わせて）。例:
```csharp
// 旧: var (s, evs) = TurnStartProcessor.Process(state, new FakeRng(0));
var (s, evs) = TurnStartProcessor.Process(state, new FakeRng(0), BattleFixtures.MinimalCatalog());
```

- [ ] **Step 7: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑

- [ ] **Step 8: Commit + push**

```bash
git add src/Core/Battle/Engine/TurnStartProcessor.cs src/Core/Battle/Engine/BattleEngine.cs src/Core/Battle/Engine/BattleEngine.EndTurn.cs tests/
git commit -m "$(cat <<'EOF'
feat(battle): TurnStartProcessor に OnTurnStart レリック発火 step 8 追加 (Phase 10.2.E Task 5)

spec §5-2: TurnStartProcessor.Process のシグネチャに DataCatalog catalog を追加。
Draw 後 / TurnStart event 前に RelicTriggerProcessor.Fire(OnTurnStart) を挿入。
レリックが Pool 加算した attack は当ターン PlayerAttacking で発射される。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 6: TurnEndProcessor.Process に rng 追加 + OnTurnEnd 発火 (step 3)

**目的:** `TurnEndProcessor.Process` のシグネチャに `IRng rng` を追加し、step 3 (AttackPool reset 後 / コンボリセット前) で `RelicTriggerProcessor.Fire(state, RelicTrigger.OnTurnEnd, ...)` を呼ぶ。

**Files:**
- Modify: `src/Core/Battle/Engine/TurnEndProcessor.cs` (sig 変更 + step 3 追加)
- Modify: `src/Core/Battle/Engine/BattleEngine.EndTurn.cs` (rng 渡す)
- Modify: 既存 TurnEnd 系テスト全て（rng 引数追加追従）
- Test: `tests/Core.Tests/Battle/Engine/TurnEndProcessorOnTurnEndTests.cs`

- [ ] **Step 1: 失敗テスト `TurnEndProcessorOnTurnEndTests` を書く**

`tests/Core.Tests/Battle/Engine/TurnEndProcessorOnTurnEndTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnEndProcessorOnTurnEndTests
{
    [Fact]
    public void OnTurnEnd_relic_fires_after_AttackPool_reset_before_combo_reset()
    {
        // attack relic: AttackPool reset 後に hero pool に加算 → 次 turn まで保持
        var relic = BattleFixtures.Relic("te", RelicTrigger.OnTurnEnd, true,
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 4));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        // 既存 hero に attack pool が積まれた状態を作る
        var heroWithAttack = BattleFixtures.Hero() with {
            AttackAll = AttackPool.Empty.Add(99),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(heroWithAttack),
            ownedRelicIds: ImmutableArray.Create("te"));

        var (after, _) = TurnEndProcessor.Process(state, new FakeRng(0), catalog);

        // AttackPool reset 後に relic が +4 → final 4 (元の 99 はリセット済み)
        Assert.Equal(4, after.Allies[0].AttackAll.Sum);
    }

    [Fact]
    public void OnTurnEnd_with_no_relics_keeps_existing_behavior()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState();

        var (after, events) = TurnEndProcessor.Process(state, new FakeRng(0), catalog);

        Assert.Equal(0, after.ComboCount);
    }

    [Fact]
    public void OnTurnEnd_combo_resets_after_relic_fires()
    {
        var relic = BattleFixtures.Relic("noop", RelicTrigger.OnTurnEnd, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("noop")) with {
            ComboCount = 5,
            LastPlayedOrigCost = 2,
            NextCardComboFreePass = true,
        };

        var (after, _) = TurnEndProcessor.Process(state, new FakeRng(0), catalog);

        // コンボ系 reset
        Assert.Equal(0, after.ComboCount);
        Assert.Null(after.LastPlayedOrigCost);
        Assert.False(after.NextCardComboFreePass);
        // relic effect は Block 1 として hero に乗ったまま
        Assert.Equal(1, after.Allies[0].Block.RawTotal);
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~TurnEndProcessorOnTurnEndTests
```
Expected: コンパイルエラー

- [ ] **Step 3: `TurnEndProcessor.Process` シグネチャ変更 + step 3 追加**

`src/Core/Battle/Engine/TurnEndProcessor.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class TurnEndProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        // Step 1-2: Block / AttackPool リセット
        var allies = state.Allies.Select(ResetActor).ToImmutableArray();
        var enemies = state.Enemies.Select(ResetActor).ToImmutableArray();
        var s = state with { Allies = allies, Enemies = enemies };

        var events = new List<BattleEvent>();
        int order = 0;

        // Step 3: OnTurnEnd レリック発動 (10.2.E)
        var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
            s, RelicTrigger.OnTurnEnd, catalog, rng, orderStart: order);
        s = afterRelic;
        foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

        // Step 4: コンボリセット
        s = s with
        {
            ComboCount = 0,
            LastPlayedOrigCost = null,
            NextCardComboFreePass = false,
        };

        // Step 5: 手札整理 (retainSelf-aware, 10.2.D 既存)
        var keepInHand = ImmutableArray.CreateBuilder<BattleCardInstance>();
        var newDiscard = s.DiscardPile.ToBuilder();
        foreach (var card in s.Hand)
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

        s = s with
        {
            Hand = keepInHand.ToImmutable(),
            DiscardPile = newDiscard.ToImmutable(),
        };

        return (s, events);
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

- [ ] **Step 4: `BattleEngine.EndTurn.cs` から呼出時に rng 渡す**

`src/Core/Battle/Engine/BattleEngine.EndTurn.cs:52`:
```csharp
// 旧: var (afterEnd, evsEnd) = TurnEndProcessor.Process(s, catalog);
var (afterEnd, evsEnd) = TurnEndProcessor.Process(s, rng, catalog);
```

- [ ] **Step 5: 既存 TurnEnd 系テスト全て rng 引数追加追従**

```bash
grep -rn "TurnEndProcessor.Process(" tests
```
全呼出に第 2 引数として `new FakeRng(0)` を追加（catalog は既に第 3 引数）:
```csharp
// 旧: TurnEndProcessor.Process(state, BattleFixtures.MinimalCatalog())
TurnEndProcessor.Process(state, new FakeRng(0), BattleFixtures.MinimalCatalog())
```

- [ ] **Step 6: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑

- [ ] **Step 7: Commit + push**

```bash
git add src/Core/Battle/Engine/TurnEndProcessor.cs src/Core/Battle/Engine/BattleEngine.EndTurn.cs tests/
git commit -m "$(cat <<'EOF'
feat(battle): TurnEndProcessor に OnTurnEnd レリック発火 step 3 追加 (Phase 10.2.E Task 6)

spec §5-3: TurnEndProcessor.Process のシグネチャに IRng rng を追加。AttackPool
reset 後 / コンボリセット前に RelicTriggerProcessor.Fire(OnTurnEnd) を挿入。
relic が pool に加算した attack は次 turn PlayerAttacking で発射される。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 7: BattleEngine.PlayCard で OnCardPlay 発火 (effect 適用後・カード移動前)

**目的:** spec §5-4 通り、`BattleEngine.PlayCard` の effect ループ完了直後・5 段優先順位カード移動の直前に `RelicTriggerProcessor.Fire(state, RelicTrigger.OnCardPlay, ...)` を呼ぶ。

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs` (OnCardPlay 発火追加)
- Test: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardOnCardPlayTests.cs`

- [ ] **Step 1: 失敗テスト `BattleEnginePlayCardOnCardPlayTests` を書く**

`tests/Core.Tests/Battle/Engine/BattleEnginePlayCardOnCardPlayTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardOnCardPlayTests
{
    [Fact]
    public void OnCardPlay_fires_after_card_effects_before_card_movement()
    {
        var relic = BattleFixtures.Relic("oc", RelicTrigger.OnCardPlay, true,
            new CardEffect("block", EffectScope.Self, null, 3));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() },
            relics: new[] { relic });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card),
            ownedRelicIds: ImmutableArray.Create("oc")) with { Energy = 1 };

        var (after, events) = BattleEngine.PlayCard(state, 0, 0, 0, new FakeRng(0), catalog);

        // strike effect (attack 6 → AttackSingle), then OnCardPlay relic block 3
        Assert.Equal(6, after.Allies[0].AttackSingle.Sum);
        Assert.Equal(3, after.Allies[0].Block.RawTotal);
        // events: PlayCard → AttackPool 加算は event なし → relic GainBlock with relic:oc
        var relicEv = events.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && e.Note != null && e.Note.Contains("relic:oc"));
        Assert.NotNull(relicEv);
        // カード移動: Discard へ (strike は exhaustSelf/retainSelf/Power/Unit でない)
        Assert.Single(after.DiscardPile);
        Assert.Equal("c1", after.DiscardPile[0].InstanceId);
    }

    [Fact]
    public void OnCardPlay_with_no_relics_keeps_existing_behavior()
    {
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card)) with { Energy = 1 };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, new FakeRng(0), catalog);

        Assert.Equal(6, after.Allies[0].AttackSingle.Sum);
        Assert.Single(after.DiscardPile);
    }

    [Fact]
    public void OnCardPlay_relic_summon_does_not_affect_card_self_summonSucceeded()
    {
        // strike (Attack カード, NonUnit) をプレイし、OnCardPlay レリックが summon 効果を持つ
        // → カード自身は Discard へ移動 (Unit でないので SummonHeld には行かない)
        var relic = BattleFixtures.Relic("summon_r", RelicTrigger.OnCardPlay, true,
            new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion"));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() },
            relics: new[] { relic });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card),
            ownedRelicIds: ImmutableArray.Create("summon_r")) with { Energy = 1 };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, new FakeRng(0), catalog);

        // Allies に minion が追加される
        Assert.Equal(2, after.Allies.Length);
        // strike カード自身は Discard へ (summon 成功フラグはカード自身の effect ループでセットされるため)
        Assert.Single(after.DiscardPile);
        Assert.Empty(after.SummonHeld);
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~BattleEnginePlayCardOnCardPlayTests
```
Expected: 1 つ目テストが失敗（OnCardPlay 発火なしで Block 3 が乗らない）

- [ ] **Step 3: `BattleEngine.PlayCard.cs` で OnCardPlay 発火追加**

`src/Core/Battle/Engine/BattleEngine.PlayCard.cs:108` （foreach effects loop の直後、5 段優先順位 if-else の直前）に追加:
```csharp
// 既存: foreach effects loop （10.2.D, summonSucceeded フラグ追跡含む）
foreach (var eff in effects) { /* ... */ }

// 10.2.E 追加: OnCardPlay レリック発動（effect 適用後・カード移動前）
var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
    s, RoguelikeCardGame.Core.Relics.RelicTrigger.OnCardPlay, catalog, rng, orderStart: order);
s = afterRelic;
foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

// 10.2.D 既存: 5 段優先順位カード移動
bool hasExhaustSelf = effects.Any(e => e.Action == "exhaustSelf");
// ...
```

- [ ] **Step 4: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑

- [ ] **Step 5: Commit + push**

```bash
git add src/Core/Battle/Engine/BattleEngine.PlayCard.cs tests/Core.Tests/Battle/Engine/BattleEnginePlayCardOnCardPlayTests.cs
git commit -m "$(cat <<'EOF'
feat(battle): PlayCard に OnCardPlay レリック発火追加 (Phase 10.2.E Task 7)

spec §5-4: BattleEngine.PlayCard の effect ループ完了直後・5 段優先順位カード
移動の直前に RelicTriggerProcessor.Fire(OnCardPlay) を挿入。relic 由来の summon
は state.Allies に追加されるが、カード自身の summonSucceeded フラグには影響
しない（フラグはカード effect ループ内でのみセット）。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 8: PlayerAttackingResolver に catalog 追加 + OnEnemyDeath 発火 (1 攻撃ごと)

**目的:** spec §5-5 通り、`PlayerAttackingResolver.Resolve` のシグネチャに `DataCatalog catalog` を追加し、各 Single / Random / All 発射の最後に「damage 適用前 IsAlive snapshot → 適用後新規死亡敵を slot 順に `RelicTriggerProcessor.FireOnEnemyDeath`」を行う。

**Files:**
- Modify: `src/Core/Battle/Engine/PlayerAttackingResolver.cs` (sig 変更 + 各 Resolve* で OnEnemyDeath 発火)
- Modify: `src/Core/Battle/Engine/BattleEngine.EndTurn.cs` (`PlayerAttackingResolver.Resolve` 呼出に catalog 渡す)
- Modify: 既存 PlayerAttacking 系テスト（catalog 引数追加追従）
- Test: `tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOnEnemyDeathTests.cs`

- [ ] **Step 1: 失敗テスト `PlayerAttackingResolverOnEnemyDeathTests` を書く**

`tests/Core.Tests/Battle/Engine/PlayerAttackingResolverOnEnemyDeathTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PlayerAttackingResolverOnEnemyDeathTests
{
    [Fact]
    public void Single_attack_killing_one_enemy_fires_OnEnemyDeath_once()
    {
        var relic = BattleFixtures.Relic("od", RelicTrigger.OnEnemyDeath, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with {
            AttackSingle = AttackPool.Empty.Add(100), // overkill
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(BattleFixtures.Goblin(slotIndex: 0, hp: 5)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, new FakeRng(0), catalog);

        Assert.False(after.Enemies[0].IsAlive);
        Assert.Equal(1, after.Allies[0].Block.RawTotal);
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Single(relicEvs);
    }

    [Fact]
    public void All_attack_killing_three_enemies_fires_OnEnemyDeath_in_slot_order()
    {
        var relic = BattleFixtures.Relic("od", RelicTrigger.OnEnemyDeath, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with {
            AttackAll = AttackPool.Empty.Add(100),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(
                BattleFixtures.Goblin(slotIndex: 0, hp: 5),
                BattleFixtures.Goblin(slotIndex: 1, hp: 5),
                BattleFixtures.Goblin(slotIndex: 2, hp: 5)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, new FakeRng(0), catalog);

        Assert.All(after.Enemies, e => Assert.False(e.IsAlive));
        // 3 回 fire
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Equal(3, relicEvs.Count);
        // slot 順 (内側→外側)
        Assert.Contains("deadEnemy:goblin_inst_0", relicEvs[0].Note);
        Assert.Contains("deadEnemy:goblin_inst_1", relicEvs[1].Note);
        Assert.Contains("deadEnemy:goblin_inst_2", relicEvs[2].Note);
        Assert.Equal(3, after.Allies[0].Block.RawTotal);
    }

    [Fact]
    public void Attack_killing_zero_enemies_fires_no_OnEnemyDeath()
    {
        var relic = BattleFixtures.Relic("od", RelicTrigger.OnEnemyDeath, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with {
            AttackSingle = AttackPool.Empty.Add(2),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(BattleFixtures.Goblin(hp: 100)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, new FakeRng(0), catalog);

        Assert.True(after.Enemies[0].IsAlive);
        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.Empty(events.Where(e => e.Note != null && e.Note.Contains("relic:od")));
    }

    [Fact]
    public void Already_dead_enemy_is_not_re_fired()
    {
        var relic = BattleFixtures.Relic("od", RelicTrigger.OnEnemyDeath, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var hero = BattleFixtures.Hero() with {
            AttackAll = AttackPool.Empty.Add(100),
        };
        var deadEnemy = BattleFixtures.Goblin(slotIndex: 0, hp: 5) with { CurrentHp = 0 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(
                deadEnemy,
                BattleFixtures.Goblin(slotIndex: 1, hp: 5)),
            ownedRelicIds: ImmutableArray.Create("od"));

        var (after, events) = PlayerAttackingResolver.Resolve(state, new FakeRng(0), catalog);

        // 既に死んでた enemy は fire 対象外、生きてた slot=1 の方だけ fire
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Single(relicEvs);
        Assert.Contains("deadEnemy:goblin_inst_1", relicEvs[0].Note);
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~PlayerAttackingResolverOnEnemyDeathTests
```
Expected: コンパイルエラー (`PlayerAttackingResolver.Resolve` は catalog 引数を取らない)

- [ ] **Step 3: `PlayerAttackingResolver.cs` を改修（catalog 引数追加 + 各 Resolve* で OnEnemyDeath 発火）**

`src/Core/Battle/Engine/PlayerAttackingResolver.cs` を以下に書き換え:
```csharp
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Battle.Engine;

internal static class PlayerAttackingResolver
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Resolve(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var events = new List<BattleEvent>();
        int order = 0;

        var allyIds = state.Allies.OrderBy(a => a.SlotIndex).Select(a => a.InstanceId).ToList();
        foreach (var aid in allyIds)
        {
            var ally = FindAlly(state, aid);
            if (ally is null || !ally.IsAlive) continue;

            bool omni = ally.GetStatus("omnistrike") > 0;
            if (omni)
            {
                state = ResolveOmnistrike(state, ally, events, ref order, catalog, rng);
            }
            else
            {
                state = ResolveSingle(state, ally, events, ref order, catalog, rng);
                state = ResolveRandom(state, ally, rng, events, ref order, catalog);
                state = ResolveAll(state, ally, events, ref order, catalog, rng);
            }
        }

        state = SummonCleanup.Apply(state, events, ref order);

        return (state, events);
    }

    private static BattleState ResolveOmnistrike(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        var combined = ally.AttackSingle + ally.AttackRandom + ally.AttackAll;
        if (combined.Sum <= 0) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
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

        state = FireOnEnemyDeathForNewlyDead(state, beforeAlive, events, ref order, catalog, rng);
        return state;
    }

    private static BattleState ResolveSingle(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        if (ally.AttackSingle.Sum <= 0) return state;
        if (state.TargetEnemyIndex is not { } ti || ti < 0 || ti >= state.Enemies.Length) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
        var (updated, evs, _) = DealDamageHelper.Apply(
            ally, state.Enemies[ti],
            baseSum: ally.AttackSingle.Sum, addCount: ally.AttackSingle.AddCount,
            scopeNote: "single", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(ti, updated) };
        events.AddRange(evs);
        order += evs.Count;

        state = FireOnEnemyDeathForNewlyDead(state, beforeAlive, events, ref order, catalog, rng);
        return state;
    }

    private static BattleState ResolveRandom(
        BattleState state, CombatActor ally, IRng rng, List<BattleEvent> events, ref int order,
        DataCatalog catalog)
    {
        if (ally.AttackRandom.Sum <= 0 || state.Enemies.Length == 0) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
        int idx = rng.NextInt(0, state.Enemies.Length);
        var (updated, evs, _) = DealDamageHelper.Apply(
            ally, state.Enemies[idx],
            baseSum: ally.AttackRandom.Sum, addCount: ally.AttackRandom.AddCount,
            scopeNote: "random", orderBase: order);
        state = state with { Enemies = state.Enemies.SetItem(idx, updated) };
        events.AddRange(evs);
        order += evs.Count;

        state = FireOnEnemyDeathForNewlyDead(state, beforeAlive, events, ref order, catalog, rng);
        return state;
    }

    private static BattleState ResolveAll(
        BattleState state, CombatActor ally, List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        if (ally.AttackAll.Sum <= 0) return state;

        var beforeAlive = SnapshotEnemyAliveIds(state);
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

        state = FireOnEnemyDeathForNewlyDead(state, beforeAlive, events, ref order, catalog, rng);
        return state;
    }

    private static System.Collections.Generic.HashSet<string> SnapshotEnemyAliveIds(BattleState state)
    {
        var set = new System.Collections.Generic.HashSet<string>();
        foreach (var e in state.Enemies)
            if (e.IsAlive) set.Add(e.InstanceId);
        return set;
    }

    private static BattleState FireOnEnemyDeathForNewlyDead(
        BattleState state, System.Collections.Generic.HashSet<string> beforeAlive,
        List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        var newlyDead = state.Enemies
            .Where(e => beforeAlive.Contains(e.InstanceId) && !e.IsAlive)
            .OrderBy(e => e.SlotIndex)
            .Select(e => e.InstanceId)
            .ToList();

        foreach (var deadId in newlyDead)
        {
            var (afterRelic, evsRelic) = RelicTriggerProcessor.FireOnEnemyDeath(
                state, deadId, catalog, rng, orderStart: order);
            state = afterRelic;
            foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }
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

- [ ] **Step 4: `BattleEngine.EndTurn.cs` から呼出時に catalog 渡す**

`src/Core/Battle/Engine/BattleEngine.EndTurn.cs:27`:
```csharp
// 旧: var (afterPA, evsPA) = PlayerAttackingResolver.Resolve(s, rng);
var (afterPA, evsPA) = PlayerAttackingResolver.Resolve(s, rng, catalog);
```

- [ ] **Step 5: 既存 PlayerAttacking 系テスト全て catalog 引数追加追従**

```bash
grep -rn "PlayerAttackingResolver.Resolve(" tests
```
全呼出に catalog 引数追加:
```csharp
// 旧: PlayerAttackingResolver.Resolve(state, new FakeRng(0))
PlayerAttackingResolver.Resolve(state, new FakeRng(0), BattleFixtures.MinimalCatalog())
```

- [ ] **Step 6: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑

- [ ] **Step 7: Commit + push**

```bash
git add src/Core/Battle/Engine/PlayerAttackingResolver.cs src/Core/Battle/Engine/BattleEngine.EndTurn.cs tests/
git commit -m "$(cat <<'EOF'
feat(battle): PlayerAttackingResolver に OnEnemyDeath 1 攻撃ごと発火 (Phase 10.2.E Task 8)

spec §5-5: PlayerAttackingResolver.Resolve のシグネチャに DataCatalog catalog
を追加。Single / Random / All / Omnistrike の各発射の最後に「damage 適用前
IsAlive snapshot → 適用後新規死亡敵を slot 順に FireOnEnemyDeath」を行う。
既に死亡していた敵は fire しない。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 9: TurnStartProcessor.ApplyPoisonTick で OnEnemyDeath 発火

**目的:** spec §5-6 通り、`TurnStartProcessor.ApplyPoisonTick` の毒死判定で「敵が毒死した瞬間」に `RelicTriggerProcessor.FireOnEnemyDeath` を呼ぶ。hero / summon の毒死は fire しない（敵限定）。

**Files:**
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs` (`ApplyPoisonTick` で OnEnemyDeath 発火)
- Test: `tests/Core.Tests/Battle/Engine/PoisonTickOnEnemyDeathTests.cs`

- [ ] **Step 1: 失敗テスト `PoisonTickOnEnemyDeathTests` を書く**

`tests/Core.Tests/Battle/Engine/PoisonTickOnEnemyDeathTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PoisonTickOnEnemyDeathTests
{
    [Fact]
    public void Enemy_dying_from_poison_fires_OnEnemyDeath()
    {
        var relic = BattleFixtures.Relic("od", RelicTrigger.OnEnemyDeath, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        var poisonedEnemy = BattleFixtures.WithPoison(
            BattleFixtures.Goblin(slotIndex: 0, hp: 1), 5);
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(poisonedEnemy),
            ownedRelicIds: ImmutableArray.Create("od")) with { Turn = 1 };

        var (after, events) = TurnStartProcessor.Process(state, new FakeRng(0), catalog);

        // poison 5 → enemy 死亡 → OnEnemyDeath 発火
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Single(relicEvs);
        Assert.Contains("deadEnemy:goblin_inst_0", relicEvs[0].Note);
    }

    [Fact]
    public void Hero_dying_from_poison_does_not_fire_OnEnemyDeath()
    {
        var relic = BattleFixtures.Relic("od", RelicTrigger.OnEnemyDeath, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        var poisonedHero = BattleFixtures.WithPoison(
            BattleFixtures.Hero(hp: 1), 5);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(poisonedHero),
            ownedRelicIds: ImmutableArray.Create("od")) with { Turn = 1 };

        var (after, events) = TurnStartProcessor.Process(state, new FakeRng(0), catalog);

        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Empty(relicEvs);
    }

    [Fact]
    public void Multiple_enemies_dying_simultaneously_fire_in_slot_order()
    {
        var relic = BattleFixtures.Relic("od", RelicTrigger.OnEnemyDeath, true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });

        var e0 = BattleFixtures.WithPoison(BattleFixtures.Goblin(slotIndex: 0, hp: 1), 5);
        var e1 = BattleFixtures.WithPoison(BattleFixtures.Goblin(slotIndex: 1, hp: 1), 5);
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(e0, e1),
            ownedRelicIds: ImmutableArray.Create("od")) with { Turn = 1 };

        var (after, events) = TurnStartProcessor.Process(state, new FakeRng(0), catalog);

        // 両方死亡 → Outcome=Victory に確定 (TurnStartProcessor 内 death detection で early return)
        // OnEnemyDeath fire は ApplyPoisonTick 中に発火するので Victory 確定前
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:od")).ToList();
        Assert.Equal(2, relicEvs.Count);
        Assert.Contains("deadEnemy:goblin_inst_0", relicEvs[0].Note);
        Assert.Contains("deadEnemy:goblin_inst_1", relicEvs[1].Note);
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~PoisonTickOnEnemyDeathTests
```
Expected: 1 つ目テスト失敗（OnEnemyDeath fire なし）

- [ ] **Step 3: `TurnStartProcessor.ApplyPoisonTick` を改修**

`src/Core/Battle/Engine/TurnStartProcessor.cs` の `ApplyPoisonTick` シグネチャと処理を更新:
```csharp
private static BattleState ApplyPoisonTick(
    BattleState state, List<BattleEvent> events, ref int order,
    DataCatalog catalog, IRng rng)
{
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
        bool wasEnemy = actor.Side == ActorSide.Enemy;
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

            // 10.2.E: 敵の毒死で OnEnemyDeath 発火
            if (wasEnemy)
            {
                var (afterRelic, evsRelic) = RelicTriggerProcessor.FireOnEnemyDeath(
                    s, aid, catalog, rng, orderStart: order);
                s = afterRelic;
                foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }
            }
        }
    }
    return s;
}
```

`Process` 内の `ApplyPoisonTick` 呼出を以下に更新:
```csharp
// 旧: s = ApplyPoisonTick(s, events, ref order);
s = ApplyPoisonTick(s, events, ref order, catalog, rng);
```

- [ ] **Step 4: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑

- [ ] **Step 5: Commit + push**

```bash
git add src/Core/Battle/Engine/TurnStartProcessor.cs tests/Core.Tests/Battle/Engine/PoisonTickOnEnemyDeathTests.cs
git commit -m "$(cat <<'EOF'
feat(battle): ApplyPoisonTick で敵毒死時 OnEnemyDeath 発火 (Phase 10.2.E Task 9)

spec §5-6: TurnStartProcessor.ApplyPoisonTick 内で敵が毒死した瞬間に
RelicTriggerProcessor.FireOnEnemyDeath を呼ぶ。hero / summon の毒死では
fire しない（敵限定）。複数敵が同 tick で毒死した場合は InstanceId snapshot
順 (slot 順) に fire。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 10: BattleEngine.Start で OnBattleStart レリック発動 (TurnStart 後)

**目的:** spec §5-1 通り、`BattleEngine.Start` の TurnStart 処理完了後に `RelicTriggerProcessor.Fire(state, RelicTrigger.OnBattleStart, ...)` を呼ぶ。events 列に OnBattleStart 由来 events を追加。

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.cs` (`Start` 末尾に OnBattleStart 発火)
- Test: `tests/Core.Tests/Battle/Engine/BattleEngineStartRelicTests.cs`

- [ ] **Step 1: 失敗テスト `BattleEngineStartRelicTests` を書く**

`tests/Core.Tests/Battle/Engine/BattleEngineStartRelicTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineStartRelicTests
{
    private static RunState MakeRun(string[]? relicIds = null, ImmutableArray<string>? potions = null)
    {
        // 既存 BattleEngineStartTests の MakeRunForTest と同じパターンで構築
        // (実装する人へ: 既存テストの factory を参照、なければ最小限の RunState を構築)
        return RunStateFactory.SoloRunWithRelicsAndPotions(
            relicIds ?? System.Array.Empty<string>(),
            potions ?? ImmutableArray.Create("", "", ""));
    }

    [Fact]
    public void Start_with_no_relics_emits_BattleStart_and_TurnStart_only()
    {
        var run = MakeRun();
        var catalog = BattleFixtures.MinimalCatalog();

        var (state, events) = BattleEngine.Start(run, "enc_test", new FakeRng(0), catalog);

        Assert.Contains(events, e => e.Kind == BattleEventKind.BattleStart);
        Assert.Contains(events, e => e.Kind == BattleEventKind.TurnStart);
        Assert.Empty(events.Where(e => e.Note != null && e.Note.Contains("relic:")));
    }

    [Fact]
    public void Start_with_OnBattleStart_relic_fires_after_TurnStart()
    {
        var relic = BattleFixtures.Relic("bs", RelicTrigger.OnBattleStart, true,
            new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var run = MakeRun(new[] { "bs" });

        var (state, events) = BattleEngine.Start(run, "enc_test", new FakeRng(0), catalog);

        Assert.Equal(5, state.Allies[0].Block.RawTotal);
        var relicEvs = events.Where(e => e.Note != null && e.Note.Contains("relic:bs")).ToList();
        Assert.Single(relicEvs);
        // OnBattleStart events は TurnStart event より後
        var tsIdx = events.ToList().FindIndex(e => e.Kind == BattleEventKind.TurnStart);
        var rsIdx = events.ToList().FindIndex(e => e.Note != null && e.Note.Contains("relic:bs"));
        Assert.True(rsIdx > tsIdx);
    }

    [Fact]
    public void Start_snapshots_OwnedRelicIds_from_RunState()
    {
        var relic = BattleFixtures.Relic("bs", RelicTrigger.OnBattleStart);
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var run = MakeRun(new[] { "bs" });

        var (state, _) = BattleEngine.Start(run, "enc_test", new FakeRng(0), catalog);

        Assert.Single(state.OwnedRelicIds);
        Assert.Equal("bs", state.OwnedRelicIds[0]);
    }

    [Fact]
    public void Start_snapshots_Potions_from_RunState()
    {
        var run = MakeRun(potions: ImmutableArray.Create("p1", "", "p2"));
        var catalog = BattleFixtures.MinimalCatalog();

        var (state, _) = BattleEngine.Start(run, "enc_test", new FakeRng(0), catalog);

        Assert.Equal(3, state.Potions.Length);
        Assert.Equal("p1", state.Potions[0]);
        Assert.Equal("", state.Potions[1]);
        Assert.Equal("p2", state.Potions[2]);
    }

    [Fact]
    public void Start_with_Implemented_false_OnBattleStart_skips()
    {
        var relic = BattleFixtures.Relic("unimpl", RelicTrigger.OnBattleStart, implemented: false,
            new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var run = MakeRun(new[] { "unimpl" });

        var (state, _) = BattleEngine.Start(run, "enc_test", new FakeRng(0), catalog);

        Assert.Equal(0, state.Allies[0].Block.RawTotal);
    }
}
```

注: `RunStateFactory.SoloRunWithRelicsAndPotions` は既存または新規の test helper。なければ既存 `BattleEngineStartTests.cs` の `MakeRunForTest` ヘルパーを拡張するか、`tests/Core.Tests/Battle/Fixtures/` 配下に `RunStateFactory.cs` を新設する:
```csharp
// tests/Core.Tests/Battle/Fixtures/RunStateFactory.cs
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Tests.Battle.Fixtures;

public static class RunStateFactory
{
    public static RunState SoloRunWithRelicsAndPotions(
        string[] relicIds, ImmutableArray<string> potions)
    {
        return new RunState(
            SchemaVersion: RunState.CurrentSchemaVersion,
            CurrentAct: 1, CurrentNodeId: 0,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
            Deck: ImmutableArray.Create(new CardInstance("strike", false)),
            Potions: potions,
            PotionSlotCount: potions.Length,
            ActiveBattle: null, ActiveReward: null,
            EncounterQueueWeak: ImmutableArray<string>.Empty,
            EncounterQueueStrong: ImmutableArray<string>.Empty,
            EncounterQueueElite: ImmutableArray<string>.Empty,
            EncounterQueueBoss: ImmutableArray<string>.Empty,
            RewardRngState: new RewardRngState(0, 0),
            ActiveMerchant: null, ActiveEvent: null,
            ActiveRestPending: false, ActiveRestCompleted: false,
            Relics: relicIds,
            PlaySeconds: 0L, RngSeed: 0UL,
            SavedAtUtc: DateTimeOffset.UnixEpoch,
            Progress: RunProgress.InProgress,
            RunId: "test-run",
            ActiveActStartRelicChoice: null,
            SeenCardBaseIds: ImmutableArray<string>.Empty,
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<JourneyEntry>.Empty);
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~BattleEngineStartRelicTests
```
Expected: コンパイルエラー (`RunStateFactory` 未定義 or OnBattleStart 発火なし)

- [ ] **Step 3: `BattleEngine.Start` 末尾に OnBattleStart 発火追加**

`src/Core/Battle/Engine/BattleEngine.cs` の `Start` メソッド末尾を以下に更新:
```csharp
// 既存 5: TurnStartProcessor.Process
var (afterTurnStart, evsTurnStart) = TurnStartProcessor.Process(initial, rng, catalog);
foreach (var ev in evsTurnStart) { events.Add(ev with { Order = order++ }); }

// 10.2.E 追加: OnBattleStart レリック発動
var (afterBattleStart, evsBattleStart) = RelicTriggerProcessor.Fire(
    afterTurnStart, RoguelikeCardGame.Core.Relics.RelicTrigger.OnBattleStart,
    catalog, rng, orderStart: order);
foreach (var ev in evsBattleStart) { events.Add(ev with { Order = order++ }); }

return (afterBattleStart, events);
```

- [ ] **Step 4: 必要なら `RunStateFactory.cs` を新設**

Step 1 の `RunStateFactory` が未存在なら作成。

- [ ] **Step 5: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑

- [ ] **Step 6: Commit + push**

```bash
git add src/Core/Battle/Engine/BattleEngine.cs tests/Core.Tests/Battle/Engine/BattleEngineStartRelicTests.cs tests/Core.Tests/Battle/Fixtures/
git commit -m "$(cat <<'EOF'
feat(battle): Start 末尾で OnBattleStart レリック発動 (Phase 10.2.E Task 10)

spec §5-1: BattleEngine.Start の TurnStart 処理完了後に
RelicTriggerProcessor.Fire(OnBattleStart) を挿入。OwnedRelicIds / Potions
snapshot は Start 内で完結し、events 列に OnBattleStart 由来 events を追加。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 11: BattleEngine.UsePotion 第 6 公開 API

**目的:** spec §4 通り、`BattleEngine.UsePotion(state, potionIndex, targetEnemyIndex?, targetAllyIndex?, rng, catalog)` を新設。Phase=PlayerInput 限定、cost なし、コンボ更新なし、捨札移動なし。effects 順次適用、消費 slot を `""` に。

**Files:**
- Create: `src/Core/Battle/Engine/BattleEngine.UsePotion.cs`
- Test: `tests/Core.Tests/Battle/Engine/BattleEngineUsePotionTests.cs`

- [ ] **Step 1: 失敗テスト `BattleEngineUsePotionTests` を書く**

`tests/Core.Tests/Battle/Engine/BattleEngineUsePotionTests.cs`:
```csharp
using System;
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

public class BattleEngineUsePotionTests
{
    [Fact]
    public void UsePotion_throws_when_phase_not_PlayerInput()
    {
        var potion = BattleFixtures.Potion("p1",
            new CardEffect("heal", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p1", "", "")) with { Phase = BattlePhase.PlayerAttacking };

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog));
    }

    [Fact]
    public void UsePotion_throws_when_potionIndex_out_of_range()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "", ""));

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UsePotion(state, 5, null, null, new FakeRng(0), catalog));
    }

    [Fact]
    public void UsePotion_throws_when_slot_empty()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "", ""));

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog));
    }

    [Fact]
    public void UsePotion_throws_when_potion_not_in_catalog()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("missing", "", ""));

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog));
    }

    [Fact]
    public void UsePotion_applies_heal_effect_and_consumes_slot()
    {
        var potion = BattleFixtures.Potion("heal_p",
            new CardEffect("heal", EffectScope.Self, null, 10));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var injuredHero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 30 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(injuredHero),
            potions: ImmutableArray.Create("heal_p", "", ""));

        var (after, events) = BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog);

        Assert.Equal(40, after.Allies[0].CurrentHp);
        Assert.Equal("", after.Potions[0]);
        Assert.Contains(events, e => e.Kind == BattleEventKind.UsePotion);
    }

    [Fact]
    public void UsePotion_applies_attack_effect_to_hero_pool()
    {
        var potion = BattleFixtures.Potion("atk_p",
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("atk_p", "", ""));

        var (after, _) = BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog);

        Assert.Equal(3, after.Allies[0].AttackAll.Sum);
    }

    [Fact]
    public void UsePotion_does_not_consume_energy()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p", "", "")) with { Energy = 2 };

        var (after, _) = BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog);

        Assert.Equal(2, after.Energy);
    }

    [Fact]
    public void UsePotion_does_not_update_combo_fields()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p", "", "")) with {
            ComboCount = 5,
            LastPlayedOrigCost = 2,
            NextCardComboFreePass = true,
        };

        var (after, _) = BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog);

        Assert.Equal(5, after.ComboCount);
        Assert.Equal(2, after.LastPlayedOrigCost);
        Assert.True(after.NextCardComboFreePass);
    }

    [Fact]
    public void UsePotion_updates_target_when_arg_provided()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(
                BattleFixtures.Goblin(slotIndex: 0),
                BattleFixtures.Goblin(slotIndex: 1)),
            potions: ImmutableArray.Create("p", "", ""));

        var (after, _) = BattleEngine.UsePotion(state, 0, targetEnemyIndex: 1, null, new FakeRng(0), catalog);

        Assert.Equal(1, after.TargetEnemyIndex);
    }

    [Fact]
    public void UsePotion_keeps_existing_target_when_arg_null()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p", "", "")) with { TargetEnemyIndex = 0 };

        var (after, _) = BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog);

        Assert.Equal(0, after.TargetEnemyIndex);
    }

    [Fact]
    public void UsePotion_event_fires_with_potion_id_and_slot_index()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "p", ""));

        var (_, events) = BattleEngine.UsePotion(state, 1, null, null, new FakeRng(0), catalog);

        var ev = events.First(e => e.Kind == BattleEventKind.UsePotion);
        Assert.Equal("p", ev.CardId);
        Assert.Equal(1, ev.Amount);
    }

    [Fact]
    public void UsePotion_consecutive_in_same_turn_works()
    {
        var p1 = BattleFixtures.Potion("p1",
            new CardEffect("heal", EffectScope.Self, null, 5));
        var p2 = BattleFixtures.Potion("p2",
            new CardEffect("heal", EffectScope.Self, null, 7));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { p1, p2 });
        var injuredHero = BattleFixtures.Hero() with { CurrentHp = 30 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(injuredHero),
            potions: ImmutableArray.Create("p1", "p2", ""));

        var (after1, _) = BattleEngine.UsePotion(state, 0, null, null, new FakeRng(0), catalog);
        var (after2, _) = BattleEngine.UsePotion(after1, 1, null, null, new FakeRng(0), catalog);

        Assert.Equal(42, after2.Allies[0].CurrentHp);
        Assert.Equal("", after2.Potions[0]);
        Assert.Equal("", after2.Potions[1]);
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~BattleEngineUsePotionTests
```
Expected: コンパイルエラー (`BattleEngine.UsePotion` 未定義)

- [ ] **Step 3: `BattleEngine.UsePotion.cs` 実装**

`src/Core/Battle/Engine/BattleEngine.UsePotion.cs`:
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
    /// <summary>
    /// 戦闘内でポーションを使用する。第 6 公開 API。
    /// Phase=PlayerInput 限定、cost なし、コンボ更新なし、捨札移動なし。
    /// effects は EffectApplier.Apply で順次適用、消費スロットは空文字に置換。
    /// 親 spec §7-3 / §8-1 / 10.2.E spec §4 参照。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) UsePotion(
        BattleState state,
        int potionIndex,
        int? targetEnemyIndex,
        int? targetAllyIndex,
        IRng rng,
        DataCatalog catalog)
    {
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException(
                $"UsePotion requires Phase=PlayerInput, got {state.Phase}");

        if (potionIndex < 0 || potionIndex >= state.Potions.Length)
            throw new InvalidOperationException(
                $"potionIndex {potionIndex} out of range [0, {state.Potions.Length})");

        var potionId = state.Potions[potionIndex];
        if (potionId == "")
            throw new InvalidOperationException($"potion slot {potionIndex} is empty");

        if (!catalog.TryGetPotion(potionId, out var def))
            throw new InvalidOperationException($"potion '{potionId}' not in catalog");

        var caster = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
        if (caster is null || !caster.IsAlive)
            throw new InvalidOperationException("hero not available");

        var s = state with
        {
            TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
            TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
        };

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.UsePotion, Order: 0,
                CasterInstanceId: caster.InstanceId,
                CardId: def.Id,
                Amount: potionIndex),
        };
        int order = 1;

        foreach (var eff in def.Effects)
        {
            var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
            s = afterEff;
            foreach (var ev in evs) { events.Add(ev with { Order = order++ }); }
            caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
            if (caster is null || !caster.IsAlive) break;
        }

        s = s with { Potions = s.Potions.SetItem(potionIndex, "") };

        return (s, events);
    }
}
```

- [ ] **Step 4: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑（12 新規 UsePotion テスト含む）

- [ ] **Step 5: Commit + push**

```bash
git add src/Core/Battle/Engine/BattleEngine.UsePotion.cs tests/Core.Tests/Battle/Engine/BattleEngineUsePotionTests.cs
git commit -m "$(cat <<'EOF'
feat(battle): UsePotion 第 6 公開 API 追加 (Phase 10.2.E Task 11)

spec §4: BattleEngine.UsePotion(state, potionIndex, targetEnemyIndex?,
targetAllyIndex?, rng, catalog) を実装。Phase=PlayerInput 限定、cost なし、
コンボ更新なし、捨札移動なし。effects を EffectApplier.Apply で順次適用、
消費 slot を "" に置換。Bestiary や CombatStat への反映は Finalize 経由。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 12: BattleSummary に ConsumedPotionIds + Finalize で Potions コピー

**目的:** spec §6 通り、`BattleSummary` に `ConsumedPotionIds: ImmutableArray<string>` を追加。`Finalize` で `state.Potions` を `RunState.Potions` に丸ごとコピー、`ConsumedPotionIds` は `before.Potions` vs `state.Potions` の diff として算出。

**Files:**
- Modify: `src/Core/Battle/Engine/BattleSummary.cs` (+ConsumedPotionIds)
- Modify: `src/Core/Battle/Engine/BattleEngine.Finalize.cs` (Potions コピー + diff 計算)
- Modify: `tests/Core.Tests/Battle/Engine/BattleEngineFinalizeTests.cs` (BattleSummary 追従)
- Test: `tests/Core.Tests/Battle/Engine/BattleEngineFinalizeConsumedPotionTests.cs`

- [ ] **Step 1: 失敗テスト `BattleEngineFinalizeConsumedPotionTests` を書く**

`tests/Core.Tests/Battle/Engine/BattleEngineFinalizeConsumedPotionTests.cs`:
```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineFinalizeConsumedPotionTests
{
    [Fact]
    public void Finalize_with_no_consumption_returns_empty_ConsumedPotionIds()
    {
        var before = RunStateFactory.SoloRunWithRelicsAndPotions(
            System.Array.Empty<string>(), ImmutableArray.Create("p1", "p2", ""));
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p1", "p2", "")) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, summary) = BattleEngine.Finalize(state, before);

        Assert.Empty(summary.ConsumedPotionIds);
        Assert.Equal(before.Potions, nextRun.Potions);
    }

    [Fact]
    public void Finalize_one_consumed_returns_potion_id_in_ConsumedPotionIds()
    {
        var before = RunStateFactory.SoloRunWithRelicsAndPotions(
            System.Array.Empty<string>(), ImmutableArray.Create("p1", "p2", ""));
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "p2", "")) with {  // p1 消費
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, summary) = BattleEngine.Finalize(state, before);

        Assert.Single(summary.ConsumedPotionIds);
        Assert.Equal("p1", summary.ConsumedPotionIds[0]);
        Assert.Equal("", nextRun.Potions[0]);
        Assert.Equal("p2", nextRun.Potions[1]);
    }

    [Fact]
    public void Finalize_same_id_in_two_slots_consumed_returns_two_entries()
    {
        var before = RunStateFactory.SoloRunWithRelicsAndPotions(
            System.Array.Empty<string>(), ImmutableArray.Create("p1", "p1", ""));
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "", "")) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, summary) = BattleEngine.Finalize(state, before);

        Assert.Equal(2, summary.ConsumedPotionIds.Length);
        Assert.Equal("p1", summary.ConsumedPotionIds[0]);
        Assert.Equal("p1", summary.ConsumedPotionIds[1]);
    }

    [Fact]
    public void Finalize_state_Potions_is_copied_to_RunState_Potions_wholesale()
    {
        var before = RunStateFactory.SoloRunWithRelicsAndPotions(
            System.Array.Empty<string>(), ImmutableArray.Create("p1", "p2", "p3"));
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p1", "", "p3")) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, _) = BattleEngine.Finalize(state, before);

        Assert.Equal(state.Potions, nextRun.Potions);
    }

    [Fact]
    public void Finalize_Defeat_sets_Progress_to_GameOver()
    {
        var before = RunStateFactory.SoloRunWithRelicsAndPotions(
            System.Array.Empty<string>(), ImmutableArray.Create("", "", ""));
        var state = BattleFixtures.MinimalState() with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat,
        };

        var (nextRun, _) = BattleEngine.Finalize(state, before);

        Assert.Equal(RoguelikeCardGame.Core.Run.RunProgress.GameOver, nextRun.Progress);
    }

    [Fact]
    public void Finalize_Victory_keeps_Progress()
    {
        var before = RunStateFactory.SoloRunWithRelicsAndPotions(
            System.Array.Empty<string>(), ImmutableArray.Create("", "", ""));
        var state = BattleFixtures.MinimalState() with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, _) = BattleEngine.Finalize(state, before);

        Assert.Equal(before.Progress, nextRun.Progress);
    }

    [Fact]
    public void Finalize_uses_hero_DefinitionId_search_for_finalHp()
    {
        var before = RunStateFactory.SoloRunWithRelicsAndPotions(
            System.Array.Empty<string>(), ImmutableArray.Create("", "", ""));
        var injuredHero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 25 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(injuredHero)) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory,
        };

        var (nextRun, summary) = BattleEngine.Finalize(state, before);

        Assert.Equal(25, summary.FinalHeroHp);
        Assert.Equal(25, nextRun.CurrentHp);
    }

    [Fact]
    public void Finalize_clamps_negative_hero_HP_to_zero()
    {
        var before = RunStateFactory.SoloRunWithRelicsAndPotions(
            System.Array.Empty<string>(), ImmutableArray.Create("", "", ""));
        var deadHero = BattleFixtures.Hero(hp: 70) with { CurrentHp = -5 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(deadHero)) with {
            Phase = BattlePhase.Resolved,
            Outcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat,
        };

        var (_, summary) = BattleEngine.Finalize(state, before);

        Assert.Equal(0, summary.FinalHeroHp);
    }
}
```

- [ ] **Step 2: テスト実行で失敗確認**

```bash
dotnet test --filter FullyQualifiedName~BattleEngineFinalizeConsumedPotionTests
```
Expected: コンパイルエラー (`BattleSummary.ConsumedPotionIds` 未定義)

- [ ] **Step 3: `BattleSummary.cs` に `ConsumedPotionIds` 追加**

`src/Core/Battle/Engine/BattleSummary.cs`:
```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 戦闘終了時に <see cref="BattleEngine.Finalize"/> が返すサマリ。
/// 親 spec §10-2 参照。
/// </summary>
public sealed record BattleSummary(
    int FinalHeroHp,
    RoguelikeCardGame.Core.Battle.State.BattleOutcome Outcome,
    string EncounterId,
    ImmutableArray<string> ConsumedPotionIds);    // 10.2.E
```

- [ ] **Step 4: `BattleEngine.Finalize.cs` 改修**

`src/Core/Battle/Engine/BattleEngine.Finalize.cs`:
```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (RunState, BattleSummary) Finalize(BattleState state, RunState before)
    {
        if (state.Phase != BattlePhase.Resolved)
            throw new InvalidOperationException($"Finalize requires Phase=Resolved, got {state.Phase}");

        var hero = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero")
                   ?? throw new InvalidOperationException("hero not found in Allies");
        int finalHp = Math.Max(0, hero.CurrentHp);

        // 10.2.E: ConsumedPotionIds を before vs state.Potions の diff として算出
        var consumed = ImmutableArray.CreateBuilder<string>();
        int slotCount = Math.Min(before.Potions.Length, state.Potions.Length);
        for (int i = 0; i < slotCount; i++)
        {
            if (before.Potions[i] != "" && state.Potions[i] == "")
                consumed.Add(before.Potions[i]);
        }
        var consumedIds = consumed.ToImmutable();

        var after = before with
        {
            CurrentHp = finalHp,
            Potions = state.Potions,                              // 10.2.E: 消費反映 (丸ごとコピー)
            ActiveBattle = null,
            Progress = state.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat
                ? RunProgress.GameOver
                : before.Progress,
        };

        var summary = new BattleSummary(
            FinalHeroHp: finalHp,
            Outcome: state.Outcome,
            EncounterId: state.EncounterId,
            ConsumedPotionIds: consumedIds);

        return (after, summary);
    }
}
```

- [ ] **Step 5: 既存 `BattleEngineFinalizeTests` の `BattleSummary` アサーションを追従**

```bash
grep -rn "new BattleSummary(" tests
```
で全箇所を抽出し、`ConsumedPotionIds: ImmutableArray<string>.Empty` を追加。

- [ ] **Step 6: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑

- [ ] **Step 7: Commit + push**

```bash
git add src/Core/Battle/Engine/BattleSummary.cs src/Core/Battle/Engine/BattleEngine.Finalize.cs tests/
git commit -m "$(cat <<'EOF'
feat(battle): BattleSummary に ConsumedPotionIds + Finalize で Potions コピー (Phase 10.2.E Task 12)

spec §6: BattleSummary に ConsumedPotionIds: ImmutableArray<string> を追加。
Finalize で state.Potions を RunState.Potions に丸ごとコピー、ConsumedPotionIds
は before.Potions vs state.Potions の slot index 順 diff として派生。
hero は DefinitionId 検索で索引、HP は 0 でクランプ。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 13: Determinism Test 拡張 (レリック + UsePotion 含む 1 戦闘 seed 一致)

**目的:** spec §8-2 の `BattleDeterminismTests` 拡張。レリック発動 + UsePotion を含む 1 戦闘で同 seed → state / events 完全一致を検証。

**Files:**
- Modify: `tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs`

- [ ] **Step 1: 失敗テスト追加**

`tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs` に追加:
```csharp
[Fact]
public void Combat_with_relic_and_potion_is_deterministic()
{
    var relic = BattleFixtures.Relic("ts_atk", RelicTrigger.OnTurnStart, true,
        new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 2));
    var potion = BattleFixtures.Potion("heal_p",
        new CardEffect("heal", EffectScope.Self, null, 5));
    var catalog = BattleFixtures.MinimalCatalog(
        cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend() },
        relics: new[] { relic },
        potions: new[] { potion });

    var run = RunStateFactory.SoloRunWithRelicsAndPotions(
        new[] { "ts_atk" },
        ImmutableArray.Create("heal_p", "", ""));

    BattleState Play(int seed)
    {
        var (state, _) = BattleEngine.Start(run, "enc_test", new FakeRng((ulong)seed), catalog);
        var (afterPotion, _) = BattleEngine.UsePotion(state, 0, null, null, new FakeRng((ulong)seed), catalog);
        return afterPotion;
    }

    var s1 = Play(42);
    var s2 = Play(42);

    // State JSON 一致 (RunStateSerializer の手法を流用)
    var json1 = System.Text.Json.JsonSerializer.Serialize(s1);
    var json2 = System.Text.Json.JsonSerializer.Serialize(s2);
    Assert.Equal(json1, json2);
}
```

- [ ] **Step 2: テスト実行で緑確認**

```bash
dotnet test --filter FullyQualifiedName~BattleDeterminismTests.Combat_with_relic_and_potion
```
Expected: PASS

- [ ] **Step 3: 全テスト実行で緑確認**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑

- [ ] **Step 4: Commit + push**

```bash
git add tests/Core.Tests/Battle/Engine/BattleDeterminismTests.cs
git commit -m "$(cat <<'EOF'
test(battle): determinism with relic + potion (Phase 10.2.E Task 13)

spec §8-2: BattleDeterminismTests に「レリック発動 + UsePotion 含む 1 戦闘」
の seed 一致テストを追加。同 seed で state JSON 完全一致を検証。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 14: 親 spec への補記事項反映

**目的:** spec §9-3 の 13 項目を `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に補記。

**Files:**
- Modify: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`

- [ ] **Step 1: 親 spec の該当章に「Phase 10.2.E 補記」を 13 項目分追加**

10.2.E spec §9-3 に列挙された 13 項目を、親 spec の各該当章 (§3-1 / §3-3 / §3-5 / §4-1 / §4-2 / §4-6 / §5-1 / §5-6 / §7-3 / §8-1 / §8-2-1 / §8-2-2 / §9-7) に「**Phase 10.2.E 補記**: ...」の形式で追加。10.2.D の補記スタイル（`> **Phase 10.2.D 補記**:` blockquote）を踏襲。

各補記内容は本 plan の元になった spec §9-3 を参照。

- [ ] **Step 2: 全テスト実行で緑確認 (spec 変更なので no-op)**

```bash
dotnet build && dotnet test
```
Expected: 全テスト緑（変更なし）

- [ ] **Step 3: Commit + push**

```bash
git add docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md
git commit -m "$(cat <<'EOF'
docs(spec): amend Phase 10 spec for Phase 10.2.E decisions (Task 14)

10.2.E spec §9-3 で予告した 13 項目を親 spec に補記:
- §3-1 BattleState +OwnedRelicIds/Potions
- §3-3 BattleSummary +ConsumedPotionIds
- §3-5 BattleEngine 公開 API 6 つ + Start シグネチャ変更
- §4-1 戦闘開幕初期化に OnBattleStart 追加
- §4-2 ターン開始 step 8 (OnTurnStart 発動)
- §4-6 ターン終了 step 3 (OnTurnEnd 発動) + rng 引数追加
- §5-1 EffectApplier シグネチャ不変、BattleOnly は戦闘内で無視
- §5-6 BattleOnly 戦闘外スキップは 10.5
- §7-3 UsePotion 第 6 API
- §8-1 ポーション戦闘内実装
- §8-2-1 4 Trigger 全 6 サイト発火位置
- §8-2-2 caster は DefinitionId 検索
- §9-7 BattleEventKind UsePotion=19 + Note prefix relic:<id> 慣例

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push
```

---

## Task 15: phase10-2E-complete タグ + push

**目的:** Phase 10.2.E 完了マーカー。Phase 10.2 全体（Core バトルロジック完成）の達成を記録。

**Files:**
- 新規ファイルなし、git tag のみ

- [ ] **Step 1: 全テスト最終確認**

```bash
dotnet build
dotnet test
```
Expected: 警告 0 / エラー 0、全テスト緑

- [ ] **Step 2: 既存 BattlePlaceholder 経由のフロー手動確認**

```bash
dotnet run --project src/Server &
```
ブラウザで手動プレイ:
- 敵タイル進入 → 即勝利ボタン → 報酬画面が無傷で動作することを確認
- マップ進行 → リスト → ボス → 全フローが既存通り動作することを確認

確認後、サーバ停止。

- [ ] **Step 3: タグ作成 + push**

```bash
git tag phase10-2E-complete
git push origin phase10-2E-complete
```

- [ ] **Step 4: 完了報告メモ**

10.2.E 完了に伴い、以下が達成された:
- Phase 10.2 全体（Core バトルロジック完成）
- 公開 API: 6 つ (`Start` / `PlayCard` / `EndTurn` / `SetTarget` / `UsePotion` / `Finalize`)
- レリック 4 新 Trigger 全 6 サイト発火統合
- BattleSummary に消費ポーション情報、Finalize で RunState 反映

次フェーズ:
- Phase 9 (マルチプレイ): 後回し方針
- Phase 10.3 (Server/SignalR): BattleHub 追加
- Phase 10.4 (Client): React BattleScreen ポート
- Phase 10.5 (cleanup): BattlePlaceholder 退役 + 戦闘外 UsePotion UI

---

## Self-Review

実施日: 2026-04-27（plan 作成時）

### 1. Spec coverage チェック

| spec section | 該当 task |
|---|---|
| §2-1 BattleState 2 フィールド追加 | Task 2 |
| §2-2 BattleSummary +ConsumedPotionIds | Task 12 |
| §2-3 BattleEventKind +UsePotion=19 | Task 3 |
| §2-5 不変条件追加 | Task 2 (テスト追加) |
| §3 RelicTriggerProcessor | Task 4 |
| §4 UsePotion API | Task 11 |
| §5-1 Start で OnBattleStart + シグネチャ変更 | Task 3 (sig) + Task 10 (OnBattleStart) |
| §5-2 TurnStart で OnTurnStart | Task 5 |
| §5-3 TurnEnd で OnTurnEnd | Task 6 |
| §5-4 PlayCard で OnCardPlay | Task 7 |
| §5-5 PlayerAttacking で OnEnemyDeath | Task 8 |
| §5-6 PoisonTick で OnEnemyDeath | Task 9 |
| §5-7 DrawHelper 共通化 (W5) | Task 0 |
| §5-8 summon RNG ID (W4) | Task 1 |
| §6 Finalize | Task 12 |
| §7 W4/W5 preparation | Task 0 + Task 1 |
| §8 テスト戦略 | 各 Task の Step 1-2 で TDD |
| §9-3 親 spec 補記 13 項目 | Task 14 |

全 spec section にタスクが対応している。

### 2. Placeholder スキャン

- "TBD" / "TODO" / "implement later" を grep → なし
- 各 Step に実コード / 実コマンドが記載
- exception messages, file paths, method signatures すべて具体

### 3. Type consistency チェック

- `BattleState.OwnedRelicIds` (Task 2) ↔ `state.OwnedRelicIds` (Task 4 RelicTriggerProcessor) ↔ `Start で snapshot` (Task 10) — 一貫
- `BattleState.Potions: ImmutableArray<string>` (Task 2) ↔ `state.Potions[i]=""` (Task 11 UsePotion) ↔ `state.Potions diff` (Task 12 Finalize) — 一貫
- `BattleSummary.ConsumedPotionIds: ImmutableArray<string>` (Task 12) — 単一定義
- `RelicTriggerProcessor.Fire` / `FireOnEnemyDeath` (Task 4) ↔ 全発火サイト呼出 (Task 5-10) — 一貫
- `UsePotion(state, potionIndex, targetEnemyIndex?, targetAllyIndex?, rng, catalog)` (Task 11) — spec §4-1 と一致
- `TurnStartProcessor.Process(state, rng, catalog)` (Task 5) ↔ 呼出側 (Start, EndTurn) — 一貫
- `TurnEndProcessor.Process(state, rng, catalog)` (Task 6) ↔ 呼出側 (EndTurn) — 一貫
- `PlayerAttackingResolver.Resolve(state, rng, catalog)` (Task 8) ↔ 呼出側 (EndTurn) — 一貫

### 4. memory feedback ルール遵守チェック

- `BattleOutcome` の fully qualified: Task 12 Finalize で `RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat` 使用
- `state.Allies` への書き戻し: Task 4 RelicTriggerProcessor で `Allies.FirstOrDefault(a => a.DefinitionId == "hero")` 検索、Task 11 UsePotion で同様、Task 12 Finalize で同様、Task 8 PlayerAttacking で `enemyIdsBefore` HashSet snapshot

### 5. ビルド赤期間管理

- Task 0/1 は既存挙動と等価（リファクタのみ） → 緑維持
- Task 2 は破壊的変更（BattleState フィールド追加）→ 全 fixture 一括追従、1 commit で緑復帰
- Task 3 は破壊的変更（Start シグネチャ）→ 限定的（テストのみ）、1 commit で緑復帰
- Task 5/6/8 は破壊的変更（catalog/rng 引数追加）→ テスト追従を 1 commit に含める
- Task 7/9-13 は内部実装追加のみ、外部互換維持

すべて plan 通りに進めれば、各 task 完了時に緑復帰する。

問題なし。
