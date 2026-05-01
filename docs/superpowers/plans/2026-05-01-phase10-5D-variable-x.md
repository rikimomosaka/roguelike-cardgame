# Phase 10.5.D — Variable X / AmountSource Engine 実装

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 10.5.B で formatter spec として整備した `CardEffect.AmountSource` を engine が評価して、effect の `Amount` を runtime 値で動的決定する仕組みを実装する。例: AmountSource="handCount" の attack effect → 手札の数 = ダメージ量。

**Architecture:** `Core/Battle/Engine/AmountSourceEvaluator` 静的ヘルパで `(state, caster, source)` から int を計算。`EffectApplier.Apply` の入口で `effect.AmountSource` が non-null なら `Amount` を評価結果で上書きした effect を作って dispatch する。formatter (10.5.B) は `[V:X|手札の数]` マーカー出力で既に対応済み、本フェーズは engine 側の動作実装のみ。

**Tech Stack:** C# .NET 10、xUnit。Server / Client 変更不要。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-3 Q2

**スコープ外:**
- 複数変数 (Y / Z) の formatter 対応 — 当面 1 effect = 1 変数で「X」のみ。複数 AmountSource を持つカードでは全部 X 表示 (将来 formatter で序数 Y/Z 振り分け)
- formatter で「[V:X|手札の数=5]」のように runtime 値を併記する display 拡張 — 将来オプション
- AmountSource を AttackPool や status amount にも適用 — 当面 effect.Amount のみ

---

## サポートする AmountSource 値 (10.5.D MVP)

| value | 意味 | 計算方法 |
|---|---|---|
| `"handCount"` | 手札の数 | `state.Hand.Length` |
| `"drawPileCount"` | 山札の数 | `state.DrawPile.Length` |
| `"discardPileCount"` | 捨札の数 | `state.DiscardPile.Length` |
| `"exhaustPileCount"` | 除外の数 | `state.ExhaustPile.Length` |
| `"selfHp"` | 自身のHP | `caster.CurrentHp` |
| `"selfHpLost"` | 失った HP (max - current) | `caster.MaxHp - caster.CurrentHp` |
| `"selfBlock"` | 自身のブロック | `caster.Block.Total` (or 既存 BlockDisplay 計算式) |
| `"comboCount"` | 現在のコンボ | `state.ComboCount` |
| `"energy"` | 現在のエナジー | `state.Energy` |
| `"powerCardCount"` | 場の power カード数 | `state.PowerCards.Length` |

未知値は **engine が `InvalidOperationException` を throw** (typo / spec 漏れの早期検出)。

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Battle/Engine/AmountSourceEvaluator.cs` | Create | 静的 helper、`Evaluate(source, state, caster) → int` |
| `src/Core/Battle/Engine/EffectApplier.cs` | Modify | `Apply` 入口で AmountSource を resolve、新 Amount 持つ effect で dispatch |
| `tests/Core.Tests/Battle/Engine/AmountSourceEvaluatorTests.cs` | Create | 各 source の単体テスト + 未知値 throw |
| `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs` | Modify | AmountSource 経由の effect resolution integration test |

---

## Conventions

- **TDD strictly.**
- **Build clean.**
- **Pure function.** AmountSourceEvaluator は副作用なし、state を変更しない。
- **AmountSource null は 10.5.B 以前の挙動維持.** 既存全カードは AmountSource を持たないため、無影響。
- **Effect の他フィールドは触らない.** AmountSource 経由で書き換わるのは Amount のみ。Action / Scope / Side / Name 等は素通し。
- **Resolve は EffectApplier.Apply 入口で 1 回.** dispatch 後の各 ApplyXxx は resolved effect を見るだけ。
- **エラー方針.** 未知 AmountSource は `InvalidOperationException`。

---

## Task 1: AmountSourceEvaluator を新設 (TDD)

**Files:**
- Create: `src/Core/Battle/Engine/AmountSourceEvaluator.cs`
- Create: `tests/Core.Tests/Battle/Engine/AmountSourceEvaluatorTests.cs`

### Step 1.1: テストを先に書く

- [ ] `AmountSourceEvaluatorTests.cs` を新規作成、各 source の case と未知値 throw を網羅:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class AmountSourceEvaluatorTests
{
    [Fact]
    public void HandCount_returns_hand_length()
    {
        var hero = BattleFixtures.Hero();
        var hand = ImmutableArray.Create(
            new BattleCardInstance("a", "strike", false, null),
            new BattleCardInstance("b", "defend", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { Hand = hand };

        Assert.Equal(2, AmountSourceEvaluator.Evaluate("handCount", state, hero));
    }

    [Fact]
    public void DrawPileCount_returns_drawPile_length()
    {
        var hero = BattleFixtures.Hero();
        var draw = ImmutableArray.Create(
            new BattleCardInstance("a", "strike", false, null),
            new BattleCardInstance("b", "defend", false, null),
            new BattleCardInstance("c", "bash", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { DrawPile = draw };

        Assert.Equal(3, AmountSourceEvaluator.Evaluate("drawPileCount", state, hero));
    }

    [Fact]
    public void DiscardPileCount_returns_discardPile_length()
    {
        var hero = BattleFixtures.Hero();
        var disc = ImmutableArray.Create(new BattleCardInstance("a", "strike", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { DiscardPile = disc };

        Assert.Equal(1, AmountSourceEvaluator.Evaluate("discardPileCount", state, hero));
    }

    [Fact]
    public void ExhaustPileCount_returns_exhaust_length()
    {
        var hero = BattleFixtures.Hero();
        var ex = ImmutableArray.Create(new BattleCardInstance("a", "strike", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { ExhaustPile = ex };

        Assert.Equal(1, AmountSourceEvaluator.Evaluate("exhaustPileCount", state, hero));
    }

    [Fact]
    public void SelfHp_returns_caster_currentHp()
    {
        var hero = BattleFixtures.Hero(currentHp: 47, maxHp: 80);
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Equal(47, AmountSourceEvaluator.Evaluate("selfHp", state, hero));
    }

    [Fact]
    public void SelfHpLost_returns_maxHp_minus_currentHp()
    {
        var hero = BattleFixtures.Hero(currentHp: 47, maxHp: 80);
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Equal(33, AmountSourceEvaluator.Evaluate("selfHpLost", state, hero));
    }

    [Fact]
    public void ComboCount_returns_state_comboCount()
    {
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with { ComboCount = 4 };

        Assert.Equal(4, AmountSourceEvaluator.Evaluate("comboCount", state, hero));
    }

    [Fact]
    public void Energy_returns_state_energy()
    {
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with { Energy = 2, EnergyMax = 3 };

        Assert.Equal(2, AmountSourceEvaluator.Evaluate("energy", state, hero));
    }

    [Fact]
    public void PowerCardCount_returns_powerCards_length()
    {
        var hero = BattleFixtures.Hero();
        var powers = ImmutableArray.Create(
            new BattleCardInstance("p1", "x", false, null),
            new BattleCardInstance("p2", "y", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { PowerCards = powers };

        Assert.Equal(2, AmountSourceEvaluator.Evaluate("powerCardCount", state, hero));
    }

    [Fact]
    public void SelfBlock_returns_caster_block_display()
    {
        // BlockDisplay の取得方法は既存のものに合わせる。
        // BlockPool の値を読む (具体的な API 名は実装時に確認)。
        var hero = BattleFixtures.Hero() with { Block = BlockPool.WithFixed(7) };
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Equal(7, AmountSourceEvaluator.Evaluate("selfBlock", state, hero));
    }

    [Fact]
    public void Unknown_source_throws()
    {
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Throws<System.InvalidOperationException>(() =>
            AmountSourceEvaluator.Evaluate("nonexistentSource", state, hero));
    }
}
```

`BlockPool.WithFixed(7)` は既存 fixture / API の確認が必要。なければ block の代替表現 (`BlockPool.Empty.Add(7)` 等) で書く。

- [ ] テスト fail を確認。

### Step 1.2: 実装

- [ ] `src/Core/Battle/Engine/AmountSourceEvaluator.cs`:

```csharp
using System;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// CardEffect.AmountSource の値を runtime state から評価する純関数群。
/// 親 spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §1-3 Q2.
/// </summary>
internal static class AmountSourceEvaluator
{
    public static int Evaluate(string source, BattleState state, CombatActor caster)
    {
        return source switch
        {
            "handCount" => state.Hand.Length,
            "drawPileCount" => state.DrawPile.Length,
            "discardPileCount" => state.DiscardPile.Length,
            "exhaustPileCount" => state.ExhaustPile.Length,
            "selfHp" => caster.CurrentHp,
            "selfHpLost" => caster.MaxHp - caster.CurrentHp,
            "selfBlock" => GetBlockTotal(caster),
            "comboCount" => state.ComboCount,
            "energy" => state.Energy,
            "powerCardCount" => state.PowerCards.Length,
            _ => throw new InvalidOperationException(
                $"Unknown AmountSource: '{source}'"),
        };
    }

    private static int GetBlockTotal(CombatActor caster)
    {
        // BlockPool の値を読む。既存 BlockDisplay 計算と整合。
        // 実装時に BlockPool の API を確認して合わせる:
        //   - .Total / .Display() / 既存 BlockDisplay 計算式 等
        return caster.Block.Total;  // 仮: 実装時に BlockPool API に合わせる
    }
}
```

`BlockPool.Total` プロパティが無い場合、既存の `caster.BlockDisplay` 計算経路 (Server `BattleStateDtoMapper` 内で使われている計算式) を参考にして同等の値を返す関数を組む。BlockPool の構造によっては fixed amount を直接読むだけで OK。

- [ ] テスト全件緑。

---

## Task 2: EffectApplier に AmountSource resolve を統合 (TDD)

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Modify: `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`

### Step 2.1: テスト

- [ ] integration test 追加。AmountSource を持つ attack / draw / addCard 等の effect が runtime 値で評価される:

```csharp
[Fact]
public void Apply_attack_with_handCount_uses_runtime_hand_length_as_amount()
{
    // Hand に 3 枚 → attack の amount は 3 として処理
    var hero = BattleFixtures.Hero();
    var enemy = BattleFixtures.Goblin(hp: 20);
    var hand = ImmutableArray.Create(
        new BattleCardInstance("a", "x", false, null),
        new BattleCardInstance("b", "y", false, null),
        new BattleCardInstance("c", "z", false, null));
    var state = BattleFixtures.MakeStateWithHeroAndEnemies(hero, new[] { enemy }) with
    {
        Hand = hand,
        TargetEnemyIndex = 0,
    };
    var effect = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0,
        AmountSource: "handCount");

    var (after, _) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

    // attack pool に 3 が積まれて、後続 attacking で hp 17 になる想定だが、
    // ApplyAttack は pool に積むのみ。AttackPool の Amount を直接確認するか、
    // または PlayCardResolver 経由で end-to-end test するか。
    // ここでは hero の AttackSingle pool が 3 増加していることを確認:
    var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
    Assert.Equal(3, heroAfter.AttackSingle.Display(strength: 0, weak: 0));
}

[Fact]
public void Apply_draw_with_drawPileCount_uses_runtime_count()
{
    // DrawPile 5 枚 → draw effect は 5 枚引く (HandCap 越えは引ける範囲)
    var hero = BattleFixtures.Hero();
    var draw = ImmutableArray.CreateRange(
        Enumerable.Range(0, 5).Select(i =>
            new BattleCardInstance($"d{i}", "x", false, null)));
    var state = BattleFixtures.MakeStateWithHero(hero) with { DrawPile = draw };
    var effect = new CardEffect("draw", EffectScope.Self, null, 0,
        AmountSource: "drawPileCount");

    var (after, _) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

    Assert.Equal(5, after.Hand.Length);
    Assert.Empty(after.DrawPile);
}

[Fact]
public void Apply_with_unknown_amountSource_throws()
{
    var hero = BattleFixtures.Hero();
    var state = BattleFixtures.MakeStateWithHero(hero);
    var effect = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0,
        AmountSource: "nonexistent");

    Assert.Throws<InvalidOperationException>(() =>
        EffectApplier.Apply(state, hero, effect, _rng, _catalog));
}

[Fact]
public void Apply_without_amountSource_uses_amount_as_is()
{
    // AmountSource null → 既存挙動 (Amount=5 が直接使われる)
    var hero = BattleFixtures.Hero();
    var enemy = BattleFixtures.Goblin(hp: 20);
    var state = BattleFixtures.MakeStateWithHeroAndEnemies(hero, new[] { enemy }) with
    {
        TargetEnemyIndex = 0,
    };
    var effect = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
    // AmountSource = null

    var (after, _) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

    var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
    Assert.Equal(5, heroAfter.AttackSingle.Display(strength: 0, weak: 0));
}
```

### Step 2.2: 実装

- [ ] EffectApplier.Apply 入口で AmountSource resolve:

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng,
    DataCatalog catalog)
{
    // 10.5.D: AmountSource を runtime 値で resolve
    var resolved = ResolveAmount(effect, state, caster);

    return resolved.Action switch
    {
        "attack" => ApplyAttack(state, caster, resolved),
        "block"  => ApplyBlock(state, caster, resolved),
        // ... 既存の dispatch をすべて resolved に置換
    };
}

private static CardEffect ResolveAmount(CardEffect effect, BattleState state, CombatActor caster)
{
    if (string.IsNullOrEmpty(effect.AmountSource)) return effect;
    int evaluated = AmountSourceEvaluator.Evaluate(effect.AmountSource, state, caster);
    return effect with { Amount = evaluated };
}
```

- [ ] 既存 dispatch の各行を `effect` → `resolved` に置換 (機械的)。テスト緑。

### Step 2.3: 既存テスト互換性チェック

- [ ] AmountSource を持たない既存全テストが緑のまま。

---

## Task 3: Self-review + 1 commit + push

### 1. Spec coverage

- [ ] 10 種の AmountSource 値全て対応 ✓
- [ ] 未知値で InvalidOperationException ✓
- [ ] AmountSource null は素通し (10.5.D 以前と同じ挙動) ✓
- [ ] formatter (10.5.B) の `[V:X|...]` 出力と整合: AmountSource 持つ effect は formatter で X 表示、engine で runtime 値評価 ✓

### 2. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Core 1118 + 新 ~14)
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑 (155)

### 3. Commit + push

- [ ] 1 commit (`feat(core): runtime amount evaluation via AmountSource for variable X (Phase 10.5.D)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] `AmountSourceEvaluator` 純関数が 10 種の source 値を評価する
- [ ] EffectApplier.Apply 入口で AmountSource を resolve
- [ ] xUnit 新テスト ~14 件、全件緑
- [ ] commit + push 済み

## 今回スコープ外

- Y / Z 序数振り分け (formatter 内 cross-effect 配列処理)
- formatter で AmountSource 評価値併記 (`[V:X|手札の数=5]`)
- AttackPool / status amount への AmountSource 適用
- 演算式付き AmountSource (`"handCount * 2"` 等) — 必要なら別 sub-phase

## ロールバック

問題があれば `EffectApplier.Apply` の `ResolveAmount` 呼出を削除すれば AmountSource は無視されて Amount=0 として扱われる (effect は何も影響しない)。AmountSourceEvaluator.cs を削除すれば完全 revert。

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5E-power-trigger.md`](2026-05-01-phase10-5E-power-trigger.md)
