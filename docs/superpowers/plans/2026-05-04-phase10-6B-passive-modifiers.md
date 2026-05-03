# Phase 10.6.B: Passive Modifier System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 18 trigger 統一 enum のうち未実装の `Trigger == "Passive"` を engine 側で評価可能にし、11 modifier action + 報酬リロール + Unknown lazy resolve を導入する。

**Architecture:** `src/Core/Relics/PassiveModifiers.cs` に Approach 2 の薄い façade + 内部 helper を集約 (加算系 / ×系 / capability / unknown weight delta)。`s.Relics` を lazy にループ、`Trigger == "Passive" && Action == X` のみ集計。Battle 内 modifier (energy / draw) は battle 開始時に `BattleState` に snapshot。Reward 系 modifier は新ヘルパ `RewardActions.AssignReward` に集約 (Phase 10.6.A T8 の inline duplication も整理)。Unknown タイル解決は map 生成時の pre-resolve から、`NodeEffectResolver.Resolve` 内の lazy resolve に変更し、relic modifier を反映可能にする。

**Tech Stack:** C# .NET 10、xUnit、ImmutableArray ベースの record state、ASP.NET Core 10、React 19 + Vite。

**Spec reference:** [docs/superpowers/specs/2026-05-04-phase10-6B-passive-modifiers-design.md](docs/superpowers/specs/2026-05-04-phase10-6B-passive-modifiers-design.md)

---

## File Structure

**Create:**
- `src/Core/Relics/PassiveModifiers.cs` — façade + 内部 helper、全 modifier 評価
- `src/Core/Rewards/RewardActions.cs` — `AssignReward` (5 reward 集約) + `Reroll`
- `tests/Core.Tests/Relics/PassiveModifiersTests.cs`
- `tests/Core.Tests/Rewards/RewardActionsTests.cs`

**Modify:**
- `src/Core/Relics/NonBattleRelicEffects.cs` — `ApplyPassiveRestHealBonus` を削除 (PassiveModifiers に移動)
- `src/Core/Rest/RestActions.cs` — `Heal` の call site 切替
- `src/Core/Battle/State/BattleState.cs` — `DrawPerTurn` field 追加
- `src/Core/Battle/Engine/BattleEngine.cs` — Start で modifier 適用済 EnergyMax / DrawPerTurn を snapshot
- `src/Core/Battle/Engine/TurnStartProcessor.cs` — `DrawPerTurn` 定数参照を `s.DrawPerTurn` に切替
- `src/Core/Merchant/MerchantInventoryGenerator.cs` — `shopPriceMultiplier` 適用
- `src/Core/Rewards/RewardGenerator.cs` — `rewardCardChoicesBonus` 適用 + `RegenerateCardChoicesForReward` helper 公開
- `src/Core/Rewards/RewardState.cs` — `RerollUsed: bool` field 追加
- `src/Core/Run/NodeEffectResolver.cs` — Treasure / Boss / Event / Unknown 分岐で `RewardActions.AssignReward` 経由 + `TileKind.Unknown` lazy resolve
- `src/Core/Run/BossRewardFlow.cs` — `RewardActions.AssignReward` 経由
- `src/Core/Events/EventResolver.cs` — `RewardActions.AssignReward` 経由
- `src/Core/Map/UnknownResolver.cs` — `ResolveOne(weights, rng)` 追加
- `src/Core/Cards/CardTextFormatter.cs` — Passive action の自動文言出力対応
- `src/Server/Services/RunStartService.cs` — `UnknownResolver.ResolveAll` 呼び出し削除、`UnknownResolutions` を空で開始
- `src/Server/Controllers/RunsController.cs` — `RewardActions.AssignReward` 経由 + reroll endpoint
- `src/Server/Controllers/BattleController.cs` — `RewardActions.AssignReward` 経由
- `src/Server/Controllers/DevMetaController.cs` — `effectActions` リストに 11 個追加
- `src/Client/src/screens/RewardScreen.tsx` (or 該当コンポーネント) — リロールボタン
- `src/Client/src/api/runs.ts` (or 該当 api file) — reroll endpoint client

**Test (新規 / 既存追加):**
- `tests/Core.Tests/Relics/PassiveModifiersTests.cs` (新規)
- `tests/Core.Tests/Rewards/RewardActionsTests.cs` (新規)
- `tests/Core.Tests/Battle/Engine/BattleEngineEnergyDrawSnapshotTests.cs` (新規 or 既存に追加)
- `tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs` (既存追加)
- `tests/Core.Tests/Rewards/RewardGeneratorTests.cs` (既存追加)
- `tests/Core.Tests/Run/NodeEffectResolverTests.cs` (既存追加: lazy unknown resolve)
- `tests/Core.Tests/Cards/CardTextFormatterTests.cs` (既存追加: Passive 文言)
- `tests/Server.Tests/Controllers/RunsControllerTests.cs` (既存追加: reroll endpoint)

---

## Task 1: PassiveModifiers façade + restHealBonus 移動

**Files:**
- Create: `src/Core/Relics/PassiveModifiers.cs`
- Test: `tests/Core.Tests/Relics/PassiveModifiersTests.cs`
- Modify: `src/Core/Relics/NonBattleRelicEffects.cs` (delete `ApplyPassiveRestHealBonus`)
- Modify: `src/Core/Rest/RestActions.cs` (call site 切替)

- [ ] **Step 1.1: PassiveModifiersTests.cs を新規作成 (failing tests)**

`tests/Core.Tests/Relics/PassiveModifiersTests.cs`:
```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class PassiveModifiersTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Sample(int gold = 100, IReadOnlyList<string>? relics = null) =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 5, 4, 0, 0, 0, System.TimeSpan.Zero)
        ) with { Gold = gold, Relics = relics ?? new List<string>() };

    private static DataCatalog Cat(string id, CardEffect[] effects, bool implemented = true) =>
        RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog, id, effects, implemented);

    [Fact]
    public void ApplyEnergyPerTurnBonus_NoRelics_ReturnsBase()
    {
        var s = Sample();
        Assert.Equal(3, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, BaseCatalog));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_OnePassiveRelic_AddsAmount()
    {
        var fake = Cat("e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "e1" });
        Assert.Equal(4, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_TwoRelics_SumsAmounts()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(fake,
            "e2", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 2, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "e1", "e2" });
        Assert.Equal(6, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_NotImplementedRelic_NoOp()
    {
        var fake = Cat("e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 5, Trigger: "Passive") }, implemented: false);
        var s = Sample(relics: new List<string> { "e1" });
        Assert.Equal(3, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_NonPassiveTrigger_Ignored()
    {
        var fake = Cat("e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 5, Trigger: "OnPickup") });
        var s = Sample(relics: new List<string> { "e1" });
        Assert.Equal(3, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_FloorAtZero()
    {
        var fake = Cat("e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, -10, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "e1" });
        Assert.Equal(0, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyCardsDrawnPerTurnBonus_AddsAmount()
    {
        var fake = Cat("d1", new[] { new CardEffect("cardsDrawnPerTurnBonus", EffectScope.Self, null, 2, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "d1" });
        Assert.Equal(7, PassiveModifiers.ApplyCardsDrawnPerTurnBonus(5, s, fake));
    }

    [Fact]
    public void ApplyCardsDrawnPerTurnBonus_FloorAtZero()
    {
        var fake = Cat("d1", new[] { new CardEffect("cardsDrawnPerTurnBonus", EffectScope.Self, null, -10, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "d1" });
        Assert.Equal(0, PassiveModifiers.ApplyCardsDrawnPerTurnBonus(5, s, fake));
    }

    [Fact]
    public void ApplyGoldRewardMultiplier_PositiveDelta_Increases()
    {
        var fake = Cat("g1", new[] { new CardEffect("goldRewardMultiplier", EffectScope.Self, null, 50, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "g1" });
        Assert.Equal(150, PassiveModifiers.ApplyGoldRewardMultiplier(100, s, fake));
    }

    [Fact]
    public void ApplyGoldRewardMultiplier_NegativeDelta_FloorAtZero()
    {
        var fake = Cat("g1", new[] { new CardEffect("goldRewardMultiplier", EffectScope.Self, null, -200, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "g1" });
        Assert.Equal(0, PassiveModifiers.ApplyGoldRewardMultiplier(100, s, fake));
    }

    [Fact]
    public void ApplyShopPriceMultiplier_NegativeDelta_FloorAtOne()
    {
        var fake = Cat("s1", new[] { new CardEffect("shopPriceMultiplier", EffectScope.Self, null, -200, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "s1" });
        Assert.Equal(1, PassiveModifiers.ApplyShopPriceMultiplier(50, s, fake));
    }

    [Fact]
    public void ApplyRewardCardChoicesBonus_AddsAmount()
    {
        var fake = Cat("r1", new[] { new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "r1" });
        Assert.Equal(4, PassiveModifiers.ApplyRewardCardChoicesBonus(3, s, fake));
    }

    [Fact]
    public void ApplyRewardCardChoicesBonus_FloorAtOne()
    {
        var fake = Cat("r1", new[] { new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, -10, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "r1" });
        Assert.Equal(1, PassiveModifiers.ApplyRewardCardChoicesBonus(3, s, fake));
    }

    [Fact]
    public void HasPassiveCapability_PresentWithPositiveAmount_ReturnsTrue()
    {
        var fake = Cat("c1", new[] { new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "c1" });
        Assert.True(PassiveModifiers.HasPassiveCapability("rewardRerollAvailable", s, fake));
    }

    [Fact]
    public void HasPassiveCapability_NotPresent_ReturnsFalse()
    {
        var s = Sample();
        Assert.False(PassiveModifiers.HasPassiveCapability("rewardRerollAvailable", s, BaseCatalog));
    }

    [Fact]
    public void ApplyUnknownWeightDeltas_AddsToBaseWeights()
    {
        var fake = Cat("u1", new[] {
            new CardEffect("unknownEnemyWeightDelta", EffectScope.Self, null, -3, Trigger: "Passive"),
            new CardEffect("unknownTreasureWeightDelta", EffectScope.Self, null, 5, Trigger: "Passive"),
        });
        var s = Sample(relics: new List<string> { "u1" });
        var config = new UnknownResolutionConfig(
            ImmutableDictionary.CreateRange(new[] {
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Enemy, 10.0),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Treasure, 2.0),
            }));
        var weights = PassiveModifiers.ApplyUnknownWeightDeltas(config, s, fake);
        Assert.Equal(7.0, weights[TileKind.Enemy]);
        Assert.Equal(7.0, weights[TileKind.Treasure]);
    }

    [Fact]
    public void ApplyUnknownWeightDeltas_NegativeWouldGoBelowZero_FloorAtZero()
    {
        var fake = Cat("u1", new[] {
            new CardEffect("unknownEnemyWeightDelta", EffectScope.Self, null, -100, Trigger: "Passive"),
        });
        var s = Sample(relics: new List<string> { "u1" });
        var config = new UnknownResolutionConfig(
            ImmutableDictionary.CreateRange(new[] {
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Enemy, 10.0),
            }));
        var weights = PassiveModifiers.ApplyUnknownWeightDeltas(config, s, fake);
        Assert.Equal(0.0, weights[TileKind.Enemy]);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_RestHealBonus_AddsAmount()
    {
        var fake = Cat("h1", new[] { new CardEffect("restHealBonus", EffectScope.Self, null, 5, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "h1" });
        Assert.Equal(15, PassiveModifiers.ApplyPassiveRestHealBonus(10, s, fake));
    }
}
```

- [ ] **Step 1.2: Run tests to verify FAIL**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~PassiveModifiersTests"
```
Expected: コンパイルエラー (`PassiveModifiers` 未定義)。

- [ ] **Step 1.3: PassiveModifiers.cs を新規作成**

`src/Core/Relics/PassiveModifiers.cs`:
```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>
/// Phase 10.6.B で導入される passive modifier system の集約 façade。
/// 全 modifier (加算 / ×系 / capability / unknown weight delta) を
/// `Trigger == "Passive"` の relic effect から評価する純関数群。
/// </summary>
/// <remarks>
/// 評価方式: lazy (毎回 `RunState.Relics` をループ集計、caching なし)。
/// Phase 10.6.A で確立された `NonBattleRelicEffects.ApplyPassiveRestHealBonus`
/// パターンを generalize し、複数 modifier action に対応。
/// </remarks>
public static class PassiveModifiers
{
    // ---- 加算系 modifier ----

    public static int ApplyEnergyPerTurnBonus(int @base, RunState s, DataCatalog catalog)
        => Math.Max(0, @base + SumPassiveBonus("energyPerTurnBonus", s, catalog));

    public static int ApplyCardsDrawnPerTurnBonus(int @base, RunState s, DataCatalog catalog)
        => Math.Max(0, @base + SumPassiveBonus("cardsDrawnPerTurnBonus", s, catalog));

    public static int ApplyRewardCardChoicesBonus(int @base, RunState s, DataCatalog catalog)
        => Math.Max(1, @base + SumPassiveBonus("rewardCardChoicesBonus", s, catalog));

    public static int ApplyPassiveRestHealBonus(int @base, RunState s, DataCatalog catalog)
        => @base + SumPassiveBonus("restHealBonus", s, catalog);

    // ---- ×系 modifier (delta from 100, additive stacking) ----

    public static int ApplyGoldRewardMultiplier(int @base, RunState s, DataCatalog catalog)
    {
        int delta = SumPassiveMultiplierDelta("goldRewardMultiplier", s, catalog);
        return Math.Max(0, (int)((long)@base * (100 + delta) / 100));
    }

    public static int ApplyShopPriceMultiplier(int @base, RunState s, DataCatalog catalog)
    {
        int delta = SumPassiveMultiplierDelta("shopPriceMultiplier", s, catalog);
        return Math.Max(1, (int)((long)@base * (100 + delta) / 100));
    }

    // ---- Capability flag ----

    public static bool HasPassiveCapability(string action, RunState s, DataCatalog catalog)
        => SumPassiveBonus(action, s, catalog) > 0;

    // ---- Unknown 重み補正 (5 種別を 1 関数で処理、床 0) ----

    public static ImmutableDictionary<TileKind, double> ApplyUnknownWeightDeltas(
        UnknownResolutionConfig config, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);

        var deltaMap = new System.Collections.Generic.Dictionary<TileKind, int>
        {
            [TileKind.Enemy] = SumPassiveBonus("unknownEnemyWeightDelta", s, catalog),
            [TileKind.Elite] = SumPassiveBonus("unknownEliteWeightDelta", s, catalog),
            [TileKind.Merchant] = SumPassiveBonus("unknownMerchantWeightDelta", s, catalog),
            [TileKind.Rest] = SumPassiveBonus("unknownRestWeightDelta", s, catalog),
            [TileKind.Treasure] = SumPassiveBonus("unknownTreasureWeightDelta", s, catalog),
        };

        var builder = ImmutableDictionary.CreateBuilder<TileKind, double>();
        foreach (var kv in config.Weights)
        {
            int delta = deltaMap.GetValueOrDefault(kv.Key, 0);
            builder.Add(kv.Key, Math.Max(0.0, kv.Value + delta));
        }
        return builder.ToImmutable();
    }

    // ---- 内部 helper ----

    private static int SumPassiveBonus(string action, RunState s, DataCatalog catalog)
    {
        int sum = 0;
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (!def.Implemented) continue;
            foreach (var eff in def.Effects)
            {
                if (eff.Trigger != "Passive") continue;
                if (eff.Action == action) sum += eff.Amount;
            }
        }
        return sum;
    }

    private static int SumPassiveMultiplierDelta(string action, RunState s, DataCatalog catalog)
        => SumPassiveBonus(action, s, catalog);
}
```

(注: `SumPassiveBonus` と `SumPassiveMultiplierDelta` は実装上同一だが、API 上の意味論を分離するため別メソッド名を残す。将来 multiplier の合成方式を変える時の hook ポイント。)

- [ ] **Step 1.4: Run tests to verify PASS**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~PassiveModifiersTests"
```
Expected: 17 件 PASS。

- [ ] **Step 1.5: NonBattleRelicEffects から restHealBonus 削除 + RestActions の call site 切替**

`src/Core/Relics/NonBattleRelicEffects.cs` の `ApplyPassiveRestHealBonus` メソッドを削除 (PassiveModifiers に移動済)。

`src/Core/Rest/RestActions.cs` の `Heal` 内:
```csharp
// Before:
int total = NonBattleRelicEffects.ApplyPassiveRestHealBonus(baseAmount, s, catalog);
// After:
int total = PassiveModifiers.ApplyPassiveRestHealBonus(baseAmount, s, catalog);
```

`using RoguelikeCardGame.Core.Relics;` は既存。

- [ ] **Step 1.6: Run all Core tests to verify no regression**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj
```
Expected: 1178 + 17 = 1195 件 PASS。

- [ ] **Step 1.7: Commit**

```bash
git add src/Core/Relics/PassiveModifiers.cs src/Core/Relics/NonBattleRelicEffects.cs src/Core/Rest/RestActions.cs tests/Core.Tests/Relics/PassiveModifiersTests.cs
git commit -m "$(cat <<'EOF'
feat(relics): PassiveModifiers facade for Phase 10.6.B (T1)

Trigger == "Passive" の 11 modifier action を集約評価する façade を新規導入。
- 加算系 (energy/cards drawn/reward card choices/rest heal)
- ×系 (gold reward / shop price)
- capability flag (rewardRerollAvailable)
- unknown weight delta (5 tile kind)

NonBattleRelicEffects.ApplyPassiveRestHealBonus を
PassiveModifiers.ApplyPassiveRestHealBonus に移動 (RestActions.Heal call site も追従)。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 2: Formatter Passive Action 対応

**Files:**
- Modify: `src/Core/Cards/CardTextFormatter.cs`
- Test: `tests/Core.Tests/Cards/CardTextFormatterTests.cs`

- [ ] **Step 2.1: 失敗テストを追加 (CardTextFormatterTests)**

`tests/Core.Tests/Cards/CardTextFormatterTests.cs` の class 末尾に追加:
```csharp
[Fact]
public void FormatEffects_Passive_EnergyPerTurnBonus_RendersJapaneseText()
{
    var effects = new[] {
        new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("エナジー最大値 +[N:1]", text);
}

[Fact]
public void FormatEffects_Passive_CardsDrawnPerTurnBonus_RendersText()
{
    var effects = new[] {
        new CardEffect("cardsDrawnPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("ターン開始時の手札枚数 +[N:1]", text);
}

[Fact]
public void FormatEffects_Passive_GoldRewardMultiplier_Positive()
{
    var effects = new[] {
        new CardEffect("goldRewardMultiplier", EffectScope.Self, null, 50, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("戦闘ゴールド報酬 +[N:50]%", text);
}

[Fact]
public void FormatEffects_Passive_GoldRewardMultiplier_Negative()
{
    var effects = new[] {
        new CardEffect("goldRewardMultiplier", EffectScope.Self, null, -20, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("戦闘ゴールド報酬 -[N:20]%", text);
}

[Fact]
public void FormatEffects_Passive_ShopPriceMultiplier_Negative()
{
    var effects = new[] {
        new CardEffect("shopPriceMultiplier", EffectScope.Self, null, -20, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("ショップ価格 -[N:20]%", text);
}

[Fact]
public void FormatEffects_Passive_RewardCardChoicesBonus()
{
    var effects = new[] {
        new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, 1, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("カード報酬選択肢 +[N:1] 枚", text);
}

[Fact]
public void FormatEffects_Passive_RewardRerollAvailable()
{
    var effects = new[] {
        new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("カード報酬を [N:1] 回リロール可能", text);
}

[Fact]
public void FormatEffects_Passive_UnknownEnemyWeightDelta()
{
    var effects = new[] {
        new CardEffect("unknownEnemyWeightDelta", EffectScope.Self, null, 5, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("ハテナマスの敵戦闘出現率 +[N:5]", text);
}

[Fact]
public void FormatEffects_Passive_UnknownTreasureWeightDelta_Negative()
{
    var effects = new[] {
        new CardEffect("unknownTreasureWeightDelta", EffectScope.Self, null, -3, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("ハテナマスの宝箱出現率 -[N:3]", text);
}

[Fact]
public void FormatEffects_Passive_RestHealBonus()
{
    var effects = new[] {
        new CardEffect("restHealBonus", EffectScope.Self, null, 5, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.Contains("休憩所での回復 +[N:5]", text);
}

[Fact]
public void FormatEffects_Passive_NoTriggerPrefix()
{
    // Passive trigger には trigger プレフィックス (バトル開始時、等) は付かない
    var effects = new[] {
        new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive")
    };
    var text = CardTextFormatter.FormatEffects(effects);
    Assert.DoesNotContain("バトル開始時", text);
    Assert.DoesNotContain("ターン開始時、", text); // 前置詞「、」付きの "ターン開始時、" は除外
    // ただし「ターン開始時の手札枚数」(formatter本文) は OK なので、本文の方を別 test で確認
}
```

- [ ] **Step 2.2: Run tests to verify FAIL**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~CardTextFormatterTests.FormatEffects_Passive"
```
Expected: 11 件 FAIL (Passive action 用の文言生成ロジックがまだ無い)。

- [ ] **Step 2.3: CardTextFormatter に Passive 対応を追加**

`src/Core/Cards/CardTextFormatter.cs` の private method `DescribeGroup` を読み、`eff.Trigger == "Passive"` の場合に専用文言を返す分岐を最初に追加。具体的には、`DescribeGroup` (or 同等の dispatch メソッド) の冒頭に:

```csharp
// Phase 10.6.B: Trigger == "Passive" の effect は専用文言テーブルで処理
if (effect.Trigger == "Passive")
{
    return DescribePassiveEffect(effect);
}
```

ファイル末尾に新メソッドを追加:
```csharp
private static string DescribePassiveEffect(CardEffect eff)
{
    int amount = eff.Amount;
    string sign = amount >= 0 ? "+" : "-";
    int abs = System.Math.Abs(amount);
    string n = $"[N:{abs}]";

    return eff.Action switch
    {
        "energyPerTurnBonus"        => $"エナジー最大値 {sign}{n}",
        "cardsDrawnPerTurnBonus"    => $"ターン開始時の手札枚数 {sign}{n}",
        "goldRewardMultiplier"      => $"戦闘ゴールド報酬 {sign}{n}%",
        "shopPriceMultiplier"       => $"ショップ価格 {sign}{n}%",
        "rewardCardChoicesBonus"    => $"カード報酬選択肢 {sign}{n} 枚",
        "rewardRerollAvailable"     => $"カード報酬を {n} 回リロール可能",
        "unknownEnemyWeightDelta"   => $"ハテナマスの敵戦闘出現率 {sign}{n}",
        "unknownEliteWeightDelta"   => $"ハテナマスのエリート戦闘出現率 {sign}{n}",
        "unknownMerchantWeightDelta"=> $"ハテナマスのショップ出現率 {sign}{n}",
        "unknownRestWeightDelta"    => $"ハテナマスの休憩所出現率 {sign}{n}",
        "unknownTreasureWeightDelta"=> $"ハテナマスの宝箱出現率 {sign}{n}",
        "restHealBonus"             => $"休憩所での回復 {sign}{n}",
        _                           => $"(未対応 Passive action: {eff.Action})",
    };
}
```

- [ ] **Step 2.4: Run formatter tests to verify PASS**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~CardTextFormatterTests"
```
Expected: 既存テスト + 新 11 件 全 PASS。

- [ ] **Step 2.5: 全 Core テストで no regression 確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj
```
Expected: 1195 + 11 = 1206 PASS。

- [ ] **Step 2.6: Commit**

```bash
git add src/Core/Cards/CardTextFormatter.cs tests/Core.Tests/Cards/CardTextFormatterTests.cs
git commit -m "$(cat <<'EOF'
feat(formatter): Passive trigger action descriptions for Phase 10.6.B (T2)

CardTextFormatter に Passive trigger 用の文言テーブルを追加。
trigger プレフィックスなしで「エナジー最大値 +N」「ショップ価格 -N%」など
プレイヤー視点で意味の分かる表記を生成。

11 modifier action + 既存 restHealBonus 対応。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 3: Battle 内 modifier (energy / draw) を battle 開始時に snapshot

**Files:**
- Modify: `src/Core/Battle/State/BattleState.cs` (`DrawPerTurn` field 追加)
- Modify: `src/Core/Battle/Engine/BattleEngine.cs` (Start で modifier 適用)
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs` (`s.DrawPerTurn` 参照に切替)
- Test: `tests/Core.Tests/Battle/Engine/BattleEngineEnergyDrawSnapshotTests.cs` (新規)

- [ ] **Step 3.1: 失敗テスト追加**

新規 `tests/Core.Tests/Battle/Engine/BattleEngineEnergyDrawSnapshotTests.cs`:
```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineEnergyDrawSnapshotTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState SampleRunWithRelics(params string[] relicIds) =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 5, 4, 0, 0, 0, System.TimeSpan.Zero)
        ) with { Relics = relicIds };

    [Fact]
    public void BattleEngine_Start_WithEnergyPerTurnBonus_SnapsHigherEnergyMax()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "energy_charm",
            new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        var run = SampleRunWithRelics("energy_charm");

        var battleState = BattleEngine.Start(run, BattleEngineTestHelpers.MinimalEncounter(), fake, new SequentialRng(1UL)).state;

        Assert.Equal(BattleEngine.InitialEnergy + 1, battleState.EnergyMax);
    }

    [Fact]
    public void BattleEngine_Start_WithCardsDrawnPerTurnBonus_SnapsHigherDrawPerTurn()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "draw_charm",
            new[] { new CardEffect("cardsDrawnPerTurnBonus", EffectScope.Self, null, 2, Trigger: "Passive") });
        var run = SampleRunWithRelics("draw_charm");

        var battleState = BattleEngine.Start(run, BattleEngineTestHelpers.MinimalEncounter(), fake, new SequentialRng(1UL)).state;

        Assert.Equal(TurnStartProcessor.DrawPerTurn + 2, battleState.DrawPerTurn);
    }

    [Fact]
    public void BattleEngine_Start_NoRelics_BaseValuesUnchanged()
    {
        var run = SampleRunWithRelics();
        var battleState = BattleEngine.Start(run, BattleEngineTestHelpers.MinimalEncounter(), BaseCatalog, new SequentialRng(1UL)).state;

        Assert.Equal(BattleEngine.InitialEnergy, battleState.EnergyMax);
        Assert.Equal(TurnStartProcessor.DrawPerTurn, battleState.DrawPerTurn);
    }
}
```

(注: `BattleEngineTestHelpers.MinimalEncounter()` は既存テストで使われている fixture と同等のものを使用。テスト失敗時に既存 helper の名前を grep で確認、必要なら inline で構築。実際の `BattleEngine.Start` シグネチャは実装時に確認 — 上記は概念例。)

- [ ] **Step 3.2: BattleState に DrawPerTurn field 追加**

`src/Core/Battle/State/BattleState.cs` の record 末尾に追加:
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
    ImmutableArray<string> OwnedRelicIds,
    ImmutableArray<string> Potions,
    string EncounterId,
    bool WildUsedInCurrentCombo = false,
    int DrawPerTurn = 5);  // 新規 (Phase 10.6.B): default は 5 で既存 instantiation 互換
```

(注: default value `= 5` で既存の `new BattleState(...)` 全箇所が引数不要のまま動くようにする。)

- [ ] **Step 3.3: BattleEngine.Start で modifier を snapshot**

`src/Core/Battle/Engine/BattleEngine.cs` の `Start` メソッドで `BattleState` を初期化する箇所 (line 77 付近 `Energy: 0, EnergyMax: InitialEnergy,`) を変更:

```csharp
// Before:
Energy: 0, EnergyMax: InitialEnergy,
// 中略 ...

// After: PassiveModifiers を query して modifier 適用済値を計算
int energyMax = Relics.PassiveModifiers.ApplyEnergyPerTurnBonus(InitialEnergy, runState, catalog);
int drawPerTurn = Relics.PassiveModifiers.ApplyCardsDrawnPerTurnBonus(TurnStartProcessor.DrawPerTurn, runState, catalog);

var initialState = new BattleState(
    // 既存フィールド (Turn, Phase, Outcome, Allies, Enemies, ...) はそのまま
    Energy: 0,
    EnergyMax: energyMax,
    // ... 中略 ...
    DrawPerTurn: drawPerTurn,
    // ... 残りフィールド
);
```

(`runState` と `catalog` は既存の Start メソッドの引数なので使える。`using RoguelikeCardGame.Core.Relics;` 追加)。

- [ ] **Step 3.4: TurnStartProcessor で s.DrawPerTurn を参照**

`src/Core/Battle/Engine/TurnStartProcessor.cs:68`:
```csharp
// Before:
s = DrawHelper.Draw(s, DrawPerTurn, rng, out _);

// After:
s = DrawHelper.Draw(s, s.DrawPerTurn, rng, out _);
```

`public const int DrawPerTurn = 5;` は base default 値として保持 (削除せず、BattleEngine.Start から参照される)。

- [ ] **Step 3.5: Run tests to verify**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~BattleEngineEnergyDrawSnapshotTests"
dotnet test tests/Core.Tests/Core.Tests.csproj
```
Expected: 1206 + 3 = 1209 PASS、no regression。

- [ ] **Step 3.6: Commit**

```bash
git add src/Core/Battle/State/BattleState.cs src/Core/Battle/Engine/BattleEngine.cs src/Core/Battle/Engine/TurnStartProcessor.cs tests/Core.Tests/Battle/Engine/BattleEngineEnergyDrawSnapshotTests.cs
git commit -m "$(cat <<'EOF'
feat(battle): snapshot energy/draw modifiers at battle start (Phase 10.6.B T3)

BattleState に DrawPerTurn フィールドを追加し、BattleEngine.Start 時に
PassiveModifiers.ApplyEnergyPerTurnBonus / ApplyCardsDrawnPerTurnBonus で
modifier 適用済値を snapshot。TurnStartProcessor は s.DrawPerTurn を参照する形に。

これで「エナジー最大値+1」「ターン開始時の手札枚数+1」relic がバトル全体で発火する。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 4: shopPriceMultiplier を MerchantInventoryGenerator で適用

**Files:**
- Modify: `src/Core/Merchant/MerchantInventoryGenerator.cs`
- Test: `tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs` (既存)

- [ ] **Step 4.1: 失敗テスト追加**

`tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs` に追加:
```csharp
[Fact]
public void Generate_WithShopPriceMultiplier_DiscountsAllOffersAndDiscardPrice()
{
    // -20% relic を 1 個持つ state で merchant を生成し、全 offer + DiscardPrice が
    // base * 80 / 100 になっていることを確認
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "loyalty",
        new[] { new CardEffect("shopPriceMultiplier", EffectScope.Self, null, -20, Trigger: "Passive") });
    var s = MakeRunStateWithRelics(fake, "loyalty");
    var prices = MakeMerchantPricesFixture(); // 全 rarity に price=100、discardSlotPrice=50 等で構築 (既存 helper があれば再利用)
    var rng = new SequentialRng(1UL);

    var inv = MerchantInventoryGenerator.Generate(fake, prices, s, rng);

    foreach (var offer in inv.Cards) Assert.Equal(80, offer.Price);
    foreach (var offer in inv.Relics) Assert.Equal(80, offer.Price);
    foreach (var offer in inv.Potions) Assert.Equal(80, offer.Price);
    Assert.Equal(40, inv.DiscardPrice); // 50 * 80 / 100 = 40
}

[Fact]
public void Generate_WithExtremeNegativeMultiplier_FloorPriceAtOne()
{
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "extreme",
        new[] { new CardEffect("shopPriceMultiplier", EffectScope.Self, null, -200, Trigger: "Passive") });
    var s = MakeRunStateWithRelics(fake, "extreme");
    var prices = MakeMerchantPricesFixture();

    var inv = MerchantInventoryGenerator.Generate(fake, prices, s, new SequentialRng(1UL));

    foreach (var offer in inv.Cards) Assert.Equal(1, offer.Price);
    Assert.Equal(1, inv.DiscardPrice);
}
```

(`MakeRunStateWithRelics` と `MakeMerchantPricesFixture` は既存テストの fixture pattern を流用、無ければ inline で構築。)

- [ ] **Step 4.2: Run tests to verify FAIL**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantInventoryGeneratorTests.Generate_With"
```
Expected: 2 件 FAIL (modifier 適用ロジック未実装)。

- [ ] **Step 4.3: MerchantInventoryGenerator に modifier 適用**

`src/Core/Merchant/MerchantInventoryGenerator.cs` の `Generate` メソッドを変更:
```csharp
public static MerchantInventory Generate(
    DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng)
{
    var cards = PickCards(catalog, prices, s, rng, CardCount);
    var relics = PickRelics(catalog, prices, s, rng, RelicCount);
    var potions = PickPotions(catalog, prices, rng, PotionCount);
    int rawDiscard = prices.DiscardSlotPrice + DiscardPriceIncrement * s.DiscardUsesSoFar;

    // Phase 10.6.B T4: shopPriceMultiplier を全 offer と DiscardPrice に適用
    cards = cards.Select(o => o with {
        Price = Relics.PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog)
    }).ToImmutableArray();
    relics = relics.Select(o => o with {
        Price = Relics.PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog)
    }).ToImmutableArray();
    potions = potions.Select(o => o with {
        Price = Relics.PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog)
    }).ToImmutableArray();
    int discardPrice = Relics.PassiveModifiers.ApplyShopPriceMultiplier(rawDiscard, s, catalog);

    return new MerchantInventory(
        cards, relics, potions,
        DiscardSlotUsed: false,
        DiscardPrice: discardPrice);
}
```

`using RoguelikeCardGame.Core.Relics;` 追加。

- [ ] **Step 4.4: Run tests to verify PASS + no regression**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantInventoryGeneratorTests"
dotnet test tests/Core.Tests/Core.Tests.csproj
```
Expected: 1209 + 2 = 1211 PASS。

- [ ] **Step 4.5: Commit**

```bash
git add src/Core/Merchant/MerchantInventoryGenerator.cs tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs
git commit -m "$(cat <<'EOF'
feat(merchant): apply shopPriceMultiplier modifier (Phase 10.6.B T4)

MerchantInventoryGenerator.Generate で全 offer (cards/relics/potions) +
DiscardPrice に shopPriceMultiplier を適用。-20% relic で全価格 80% 化、
極端な -200% でも床 1 gold で clamp。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 5: rewardCardChoicesBonus を GenerateFromEnemy で適用 + RegenerateCardChoicesForReward 切り出し

**Files:**
- Modify: `src/Core/Rewards/RewardGenerator.cs`
- Test: `tests/Core.Tests/Rewards/RewardGeneratorTests.cs` (既存)

- [ ] **Step 5.1: 失敗テスト追加**

`tests/Core.Tests/Rewards/RewardGeneratorTests.cs` に追加:
```csharp
[Fact]
public void GenerateFromEnemy_WithRewardCardChoicesBonus_ProducesMoreChoices()
{
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "extra_choices",
        new[] { new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
    var s = MakeRunStateWithRelics(fake, "extra_choices");

    var (reward, _) = RewardGenerator.Generate(
        new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak)),
        s.RewardRngState,
        ImmutableArray<string>.Empty,
        fake.RewardTables["act1"],
        fake,
        new SequentialRng(1UL),
        s);

    Assert.Equal(4, reward.CardChoices.Length); // 3 + 1 = 4
}

[Fact]
public void GenerateFromEnemy_WithNegativeBonus_FloorAtOneChoice()
{
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "fewer_choices",
        new[] { new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, -10, Trigger: "Passive") });
    var s = MakeRunStateWithRelics(fake, "fewer_choices");

    var (reward, _) = RewardGenerator.Generate(
        new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak)),
        s.RewardRngState,
        ImmutableArray<string>.Empty,
        fake.RewardTables["act1"],
        fake,
        new SequentialRng(1UL),
        s);

    Assert.Equal(1, reward.CardChoices.Length); // 床 1
}
```

(注: 既存 `RewardGenerator.Generate` シグネチャは `RunState` を受けないので、シグネチャ拡張が必要。テストはその拡張前提で書く。)

- [ ] **Step 5.2: Run tests to verify FAIL**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardGeneratorTests.GenerateFromEnemy_With"
```
Expected: コンパイルエラー (Generate に `RunState` 引数追加されてない)。

- [ ] **Step 5.3: RewardGenerator.Generate / GenerateFromEnemy のシグネチャに RunState を追加**

`src/Core/Rewards/RewardGenerator.cs`:
```csharp
public static (RewardState reward, RewardRngState newRng) Generate(
    RewardContext context,
    RewardRngState rngState,
    ImmutableArray<string> cardExclusions,
    RewardTable table,
    DataCatalog data,
    IRng rng,
    RunState runState)  // 新規引数 (10.6.B T5)
{
    return context switch
    {
        RewardContext.FromEnemy fe => GenerateFromEnemy(fe.Pool, rngState, cardExclusions, table, data, rng, runState),
        RewardContext.FromNonBattle nb when nb.Kind == NonBattleRewardKind.Treasure
            => GenerateTreasure(rngState, ImmutableArray<string>.Empty, table, data, rng),
        RewardContext.FromNonBattle
            => GenerateFromNonBattleEvent(rngState, table, rng),
        _ => throw new ArgumentOutOfRangeException(nameof(context))
    };
}

private static (RewardState, RewardRngState) GenerateFromEnemy(
    EnemyPool pool, RewardRngState rngState,
    ImmutableArray<string> excl, RewardTable table, DataCatalog data, IRng rng,
    RunState runState)
{
    // ... 既存のロジック (gold / potion / pickup) はそのまま ...

    // Phase 10.6.B T5: rewardCardChoicesBonus を反映
    int targetCount = Relics.PassiveModifiers.ApplyRewardCardChoicesBonus(3, runState, data);

    var picks = new List<string>();
    var seen = new HashSet<string>();
    while (picks.Count < targetCount)
    {
        var r = rng.NextInt(0, 100);
        // ... 既存の rarity 選択 + pool 抽選 ロジック ...
    }
    // 残り (hasRare / newRng / RewardState 構築) 既存ロジック
    // ...
}
```

`using RoguelikeCardGame.Core.Run;` と `using RoguelikeCardGame.Core.Relics;` 追加。

- [ ] **Step 5.4: 既存 caller を全部更新**

`grep -rn "RewardGenerator.Generate\b" src/ tests/` で呼び出し元を全リストアップし、`runState` 引数を追加:
- `NodeEffectResolver.StartTreasure` (内部で `GenerateTreasure` 呼ぶので注意 — Treasure は `runState` 不要だが Generate dispatch 経由なら必要)
- `BossRewardFlow.cs:20` あたり
- `EventResolver.cs:79` あたり
- `BattleController.cs:332` あたり (実装時に再確認)
- `RunsController.cs:168` あたり

各 caller で `runState` (or 同等の `s`) を引数として追加。

- [ ] **Step 5.5: RegenerateCardChoicesForReward 内部 helper を切り出し (T7 用)**

`src/Core/Rewards/RewardGenerator.cs` に internal 公開メソッドを追加:
```csharp
/// <summary>
/// Phase 10.6.B T7 (reroll) 用: GenerateFromEnemy の card 抽選部分を切り出した helper。
/// 既存の picks ロジックを ImmutableArray として返す。
/// </summary>
public static ImmutableArray<string> RegenerateCardChoicesForReward(
    EnemyPool pool, RewardRngState rngState,
    ImmutableArray<string> exclusions,
    RewardTable table, DataCatalog data, IRng rng,
    RunState runState)
{
    int targetCount = Relics.PassiveModifiers.ApplyRewardCardChoicesBonus(3, runState, data);
    var entry = table.Pools[pool.Tier];
    int commonPct = entry.CommonPercent;
    int rarePct = entry.RarePercent;
    int epicPct = entry.EpicPercent;
    int bonus = rngState.RareChanceBonusPercent;
    int rareFinal = Math.Min(100, rarePct + bonus);
    int take = rareFinal - rarePct;
    int commonFinal = Math.Max(0, commonPct - take);
    int epicFinal = Math.Max(0, 100 - rareFinal - commonFinal);

    var picks = new List<string>();
    var seen = new HashSet<string>();
    while (picks.Count < targetCount)
    {
        var r = rng.NextInt(0, 100);
        CardRarity rarity;
        if (r < commonFinal) rarity = CardRarity.Common;
        else if (r < commonFinal + rareFinal) rarity = CardRarity.Rare;
        else rarity = CardRarity.Epic;

        var pool2 = data.Cards.Values
            .Where(c => c.Rarity != CardRarity.Token)
            .Where(c => c.Rarity == rarity && c.Id.StartsWith("reward_"))
            .Where(c => !exclusions.Contains(c.Id) && !seen.Contains(c.Id))
            .Select(c => c.Id)
            .ToList();
        if (pool2.Count == 0) continue;
        var pick = pool2[rng.NextInt(0, pool2.Count)];
        picks.Add(pick);
        seen.Add(pick);
    }
    return picks.ToImmutableArray();
}
```

`GenerateFromEnemy` 内の card 抽選部分は、この helper を呼ぶ形に書き換えて DRY 化:
```csharp
// GenerateFromEnemy 内
var picks = RegenerateCardChoicesForReward(pool, rngState, excl, table, data, rng, runState);
```

- [ ] **Step 5.6: Run tests to verify PASS + no regression**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardGeneratorTests"
dotnet test tests/Core.Tests/Core.Tests.csproj
```
Expected: 1211 + 2 = 1213 PASS。

- [ ] **Step 5.7: Commit**

```bash
git add src/Core/Rewards/RewardGenerator.cs tests/Core.Tests/Rewards/RewardGeneratorTests.cs src/Core/Run/NodeEffectResolver.cs src/Core/Run/BossRewardFlow.cs src/Core/Events/EventResolver.cs src/Server/Controllers/BattleController.cs src/Server/Controllers/RunsController.cs
git commit -m "$(cat <<'EOF'
feat(rewards): apply rewardCardChoicesBonus + extract RegenerateCardChoicesForReward (Phase 10.6.B T5)

RewardGenerator.Generate / GenerateFromEnemy に RunState 引数を追加し、
rewardCardChoicesBonus modifier で card 抽選枚数を 3 → 3+bonus に補正。
床 1 で clamp。

T7 (reroll) 用に GenerateFromEnemy 内の card 抽選ロジックを
RegenerateCardChoicesForReward 公開メソッドに切り出し。

呼び出し元 (NodeEffectResolver/BossRewardFlow/EventResolver/2 controllers) も
runState 引数を渡すように追従。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 6: RewardActions.AssignReward 集約ヘルパ + 5 site 切替 + goldRewardMultiplier 適用

**Files:**
- Create: `src/Core/Rewards/RewardActions.cs`
- Test: `tests/Core.Tests/Rewards/RewardActionsTests.cs` (新規)
- Modify: `src/Core/Run/NodeEffectResolver.cs`, `src/Core/Run/BossRewardFlow.cs`, `src/Core/Events/EventResolver.cs`
- Modify: `src/Server/Controllers/BattleController.cs`, `src/Server/Controllers/RunsController.cs`

- [ ] **Step 6.1: RewardActionsTests 新規作成 (failing tests)**

`tests/Core.Tests/Rewards/RewardActionsTests.cs`:
```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardActionsTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Sample(int gold = 50, params string[] relicIds) =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 5, 4, 0, 0, 0, System.TimeSpan.Zero)
        ) with { Gold = gold, Relics = relicIds };

    private static RewardState SampleReward(int gold = 100) =>
        new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed);

    [Fact]
    public void AssignReward_NoRelics_ActiveRewardSetWithBaseGold()
    {
        var s0 = Sample();
        var reward = SampleReward(gold: 100);
        var s1 = RewardActions.AssignReward(s0, reward, s0.RewardRngState, BaseCatalog);
        Assert.NotNull(s1.ActiveReward);
        Assert.Equal(100, s1.ActiveReward!.Gold);
    }

    [Fact]
    public void AssignReward_WithGoldRewardMultiplier_AdjustsGold()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "lucky",
            new[] { new CardEffect("goldRewardMultiplier", EffectScope.Self, null, 50, Trigger: "Passive") });
        var s0 = Sample(gold: 0, "lucky");
        var reward = SampleReward(gold: 100);

        var s1 = RewardActions.AssignReward(s0, reward, s0.RewardRngState, fake);

        Assert.Equal(150, s1.ActiveReward!.Gold);
    }

    [Fact]
    public void AssignReward_FiresOnRewardGeneratedTrigger()
    {
        // OnRewardGenerated relic で Gold +X が effect として走ることを確認
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "celebration",
            new[] { new CardEffect("gainGold", EffectScope.Self, null, 5, Trigger: "OnRewardGenerated") });
        var s0 = Sample(gold: 100, "celebration");
        var reward = SampleReward(gold: 50);

        var s1 = RewardActions.AssignReward(s0, reward, s0.RewardRngState, fake);

        // 100 (initial) + 5 (OnRewardGenerated) = 105
        Assert.Equal(105, s1.Gold);
        Assert.Equal(50, s1.ActiveReward!.Gold); // reward.Gold は claim 前なので未消費
    }
}
```

- [ ] **Step 6.2: Run tests FAIL**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardActionsTests"
```
Expected: コンパイルエラー (`RewardActions` 未定義)。

- [ ] **Step 6.3: RewardActions.cs を新規作成**

`src/Core/Rewards/RewardActions.cs`:
```csharp
using System;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Rewards;

/// <summary>
/// Phase 10.6.B で導入される reward flow control の集約点。
/// 5 reward 生成サイト (Treasure/Boss/Event/Battle 終了/Run 勝利) で共通の
/// 「ActiveReward 設定 + goldRewardMultiplier 適用 + OnRewardGenerated 発火」を
/// 1 関数に集約 (Phase 10.6.A T8 で 5 ヶ所に inline されていた logic を整理)。
/// </summary>
public static class RewardActions
{
    public static RunState AssignReward(
        RunState s, RewardState reward, RewardRngState newRng, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(reward);
        ArgumentNullException.ThrowIfNull(catalog);

        // Phase 10.6.B: goldRewardMultiplier を適用
        var goldAdjusted = PassiveModifiers.ApplyGoldRewardMultiplier(reward.Gold, s, catalog);
        var rewardWithGold = reward with { Gold = goldAdjusted };

        var s1 = s with { ActiveReward = rewardWithGold, RewardRngState = newRng };
        return NonBattleRelicEffects.ApplyOnRewardGenerated(s1, catalog);
    }
}
```

- [ ] **Step 6.4: 5 reward 生成サイトを `RewardActions.AssignReward` 経由に切替**

各サイトで現在 inline 書きされている `state with { ActiveReward = reward, RewardRngState = newRng };` + `NonBattleRelicEffects.ApplyOnRewardGenerated(s1, ...)` を `RewardActions.AssignReward(s, reward, newRng, catalog)` 1 行に置換。

例: `src/Core/Run/NodeEffectResolver.cs` の `StartTreasure`:
```csharp
// Before:
var s1 = s with { ActiveReward = reward, RewardRngState = newRng };
return Relics.NonBattleRelicEffects.ApplyOnRewardGenerated(s1, data);

// After:
return Rewards.RewardActions.AssignReward(s, reward, newRng, data);
```

同様に `BossRewardFlow.cs`、`EventResolver.GrantCardReward`、`BattleController.cs:342 付近`、`RunsController.cs:186 付近`。

`using` 追加が必要な場合 (`using RoguelikeCardGame.Core.Rewards;`)。

- [ ] **Step 6.5: Run tests PASS + no regression**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardActionsTests"
dotnet test tests/Core.Tests/Core.Tests.csproj
dotnet test tests/Server.Tests/Server.Tests.csproj
```
Expected: Core 1213 + 3 = 1216 PASS、Server pre-existing 4 件 failure 以外全 PASS。

- [ ] **Step 6.6: Commit**

```bash
git add src/Core/Rewards/RewardActions.cs tests/Core.Tests/Rewards/RewardActionsTests.cs src/Core/Run/NodeEffectResolver.cs src/Core/Run/BossRewardFlow.cs src/Core/Events/EventResolver.cs src/Server/Controllers/BattleController.cs src/Server/Controllers/RunsController.cs
git commit -m "$(cat <<'EOF'
feat(rewards): RewardActions.AssignReward central helper + goldRewardMultiplier (Phase 10.6.B T6)

5 reward 生成サイトで inline されていた "ActiveReward 設定 + OnRewardGenerated 発火"
ロジックを RewardActions.AssignReward に集約。同時に goldRewardMultiplier を適用
(Phase 10.6.A T8 で残っていた duplication が解消)。

5 サイト全部 (Treasure / Boss / Event / Battle終了 / Run勝利) で
RewardActions.AssignReward 1 行呼び出しに統一。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 7: Reroll mechanic (RewardState + Reroll method + endpoint + UI)

**Files:**
- Modify: `src/Core/Rewards/RewardState.cs` (`RerollUsed` field 追加)
- Modify: `src/Core/Rewards/RewardActions.cs` (`Reroll` method 追加)
- Modify: `src/Server/Controllers/RunsController.cs` (reroll endpoint)
- Modify: `src/Client/src/screens/RewardScreen.tsx` (or 該当: reroll ボタン)
- Modify: `src/Client/src/api/runs.ts` (or 該当: reroll API client)
- Test: `tests/Core.Tests/Rewards/RewardActionsTests.cs` (既存追加)
- Test: `tests/Server.Tests/Controllers/RunsControllerRerollTests.cs` (新規)

- [ ] **Step 7.1: RewardState.RerollUsed field 追加**

`src/Core/Rewards/RewardState.cs`:
```csharp
public sealed record RewardState(
    int Gold,
    bool GoldClaimed,
    string? PotionId,
    bool PotionClaimed,
    ImmutableArray<string> CardChoices,
    CardRewardStatus CardStatus,
    string? RelicId = null,
    bool RelicClaimed = true,
    bool IsBossReward = false,
    bool RerollUsed = false);  // 新規 (Phase 10.6.B T7)
```

(default `false` で既存 instantiation 互換 — schema migration 不要。)

- [ ] **Step 7.2: 失敗テスト追加 (RewardActionsTests)**

`tests/Core.Tests/Rewards/RewardActionsTests.cs` に追加:
```csharp
[Fact]
public void Reroll_NoCapability_Throws()
{
    var s0 = Sample(gold: 50);
    var reward = SampleReward(gold: 50) with {
        CardChoices = ImmutableArray.Create("strike", "defend", "bash"),
        CardStatus = CardRewardStatus.Pending,
    };
    var s1 = s0 with { ActiveReward = reward };
    Assert.Throws<System.InvalidOperationException>(() =>
        RewardActions.Reroll(s1, BaseCatalog, new SequentialRng(1UL),
            new EnemyPool(1, EnemyTier.Weak),
            BaseCatalog.RewardTables["act1"]));
}

[Fact]
public void Reroll_CardAlreadyResolved_Throws()
{
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "die", new[] { new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive") });
    var s0 = Sample(gold: 50, "die");
    var reward = SampleReward(gold: 50) with {
        CardChoices = ImmutableArray.Create("strike", "defend", "bash"),
        CardStatus = CardRewardStatus.Claimed, // already claimed
    };
    var s1 = s0 with { ActiveReward = reward };
    Assert.Throws<System.InvalidOperationException>(() =>
        RewardActions.Reroll(s1, fake, new SequentialRng(1UL),
            new EnemyPool(1, EnemyTier.Weak), fake.RewardTables["act1"]));
}

[Fact]
public void Reroll_AlreadyUsed_Throws()
{
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "die", new[] { new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive") });
    var s0 = Sample(gold: 50, "die");
    var reward = SampleReward(gold: 50) with {
        CardChoices = ImmutableArray.Create("strike", "defend", "bash"),
        CardStatus = CardRewardStatus.Pending,
        RerollUsed = true,
    };
    var s1 = s0 with { ActiveReward = reward };
    Assert.Throws<System.InvalidOperationException>(() =>
        RewardActions.Reroll(s1, fake, new SequentialRng(1UL),
            new EnemyPool(1, EnemyTier.Weak), fake.RewardTables["act1"]));
}

[Fact]
public void Reroll_Successful_RegeneratesCardChoicesAndMarksUsed()
{
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "die", new[] { new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive") });
    var s0 = Sample(gold: 50, "die");
    var oldChoices = ImmutableArray.Create("reward_common_01", "reward_common_02", "reward_common_03");
    var reward = SampleReward(gold: 50) with {
        CardChoices = oldChoices,
        CardStatus = CardRewardStatus.Pending,
    };
    var s1 = s0 with { ActiveReward = reward };

    var s2 = RewardActions.Reroll(s1, fake, new SequentialRng(99UL),
        new EnemyPool(1, EnemyTier.Weak), fake.RewardTables["act1"]);

    Assert.True(s2.ActiveReward!.RerollUsed);
    Assert.Equal(3, s2.ActiveReward!.CardChoices.Length); // bonus 無しなので 3 枚
    // (新 RNG 99 で生成された choices なので oldChoices と一致しない可能性が高いが、
    //  determinism 上同じ choice になるケースもあるので Length のみ assert)
}
```

- [ ] **Step 7.3: Run tests FAIL**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardActionsTests.Reroll"
```
Expected: 4 件 FAIL (`Reroll` メソッド未実装)。

- [ ] **Step 7.4: RewardActions.Reroll メソッド追加**

`src/Core/Rewards/RewardActions.cs` に追加:
```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Random;

// ... (既存 using に追加) ...

public static class RewardActions
{
    // 既存 AssignReward ...

    public static RunState Reroll(
        RunState s, DataCatalog catalog, IRng rng,
        EnemyPool sourcePool, RewardTable table)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(table);

        var r = s.ActiveReward
            ?? throw new InvalidOperationException("No ActiveReward to reroll");
        if (r.CardStatus != CardRewardStatus.Pending)
            throw new InvalidOperationException("Card already resolved, cannot reroll");
        if (r.RerollUsed)
            throw new InvalidOperationException("Reroll already used for this reward");
        if (!PassiveModifiers.HasPassiveCapability("rewardRerollAvailable", s, catalog))
            throw new InvalidOperationException("No relic grants reward reroll");

        var newPicks = RewardGenerator.RegenerateCardChoicesForReward(
            sourcePool, s.RewardRngState, ImmutableArray<string>.Empty,
            table, catalog, rng, s);

        return s with {
            ActiveReward = r with {
                CardChoices = newPicks,
                RerollUsed = true,
            }
        };
    }
}
```

- [ ] **Step 7.5: Run Core tests PASS**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardActionsTests"
dotnet test tests/Core.Tests/Core.Tests.csproj
```
Expected: 1216 + 4 = 1220 PASS。

- [ ] **Step 7.6: Server endpoint 追加**

`src/Server/Controllers/RunsController.cs` に endpoint 追加:
```csharp
[HttpPost("{id}/active-reward/reroll-card-choices")]
public async Task<IActionResult> RerollCardChoices(string id, CancellationToken ct)
{
    var s = await _saves.LoadAsync(id, ct);
    if (s is null) return NotFound();
    if (s.ActiveReward is null) return BadRequest("No active reward");

    // EnemyPool / RewardTable の reconstruct
    // (既存の reward 生成 path で使われている方法と同じ取得方法、実装時に grep で確認)
    var pool = ReconstructEnemyPoolFromContext(s); // existing helper を使うか inline で構築
    var table = _data.RewardTables["act1"]; // act 番号は s.CurrentAct から決定
    var rng = new SystemRng(/* s.RewardRngState から derive、or 別 seed */);

    try
    {
        s = RewardActions.Reroll(s, _data, rng, pool, table);
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(ex.Message);
    }

    await _saves.SaveAsync(s, ct);
    return Ok(/* RunState DTO mapping */);
}
```

(注: EnemyPool 再構築方法 / RewardRngState からの seed 取り出し方は実装時に既存 controller を読んで決定。`ReconstructEnemyPoolFromContext` は仮、実コードに合わせて調整。)

- [ ] **Step 7.7: Server.Tests で endpoint 動作確認**

新規 `tests/Server.Tests/Controllers/RunsControllerRerollTests.cs`:
```csharp
// 既存 RunsControllerTests と同じ pattern で
// 1. reroll relic 持ってない state で 400
// 2. reroll relic 持ってる state で 200 + CardChoices 変化 + RerollUsed=true
// 3. 2 回目 reroll で 400 (already used)
```

(具体的 test code は既存 RunsControllerTests の pattern に従う。Server start + TestClient + JSON body の構築を mirror。)

```bash
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~RunsControllerRerollTests"
```

- [ ] **Step 7.8: Client 実装 (UI ボタン + API call)**

`src/Client/src/api/runs.ts` (or 該当 api file):
```typescript
export async function rerollRewardCardChoices(runId: string): Promise<RunStateDto> {
  const res = await fetch(`/api/runs/${runId}/active-reward/reroll-card-choices`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
  });
  if (!res.ok) throw new Error(await res.text());
  return await res.json();
}
```

`src/Client/src/screens/RewardScreen.tsx` (or 該当): reward 画面に reroll ボタン追加:
```tsx
{activeReward.cardChoices.length > 0 &&
 activeReward.cardStatus === "Pending" &&
 !activeReward.rerollUsed &&
 hasRerollCapability(ownedRelics, catalog) && (
  <button onClick={async () => {
    const updated = await rerollRewardCardChoices(runId);
    setRunState(updated);
  }}>リロール</button>
)}
```

`hasRerollCapability` は relic def の effects を見て `Passive + rewardRerollAvailable + amount > 0` を check する client 側 helper (or `relicDef.flags.rewardRerollAvailable` の DTO field を server 側で expose する案も)。

`src/Client/src/types/runState.ts` (or 該当): `RewardState` 型に `rerollUsed: boolean` field 追加。

- [ ] **Step 7.9: Client tests + tsc**

```bash
cd src/Client && npx tsc --noEmit && npm run test:run
```
Expected: 全 PASS、tsc clean。

- [ ] **Step 7.10: Commit**

```bash
git add src/Core/Rewards/RewardState.cs src/Core/Rewards/RewardActions.cs tests/Core.Tests/Rewards/RewardActionsTests.cs src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/RunsControllerRerollTests.cs src/Client/src/api/ src/Client/src/screens/ src/Client/src/types/
git commit -m "$(cat <<'EOF'
feat(rewards): card choices reroll mechanic (Phase 10.6.B T7)

報酬カード選択肢を 1 reward につき 1 度リロール可能にする。
- RewardState.RerollUsed: bool フィールド追加 (default false)
- RewardActions.Reroll(s, catalog, rng, pool, table) メソッド追加
  → capability check (rewardRerollAvailable Passive)
  → CardStatus == Pending check
  → RerollUsed == false check
  → CardChoices を RegenerateCardChoicesForReward で再抽選
- Server endpoint POST /runs/{id}/active-reward/reroll-card-choices
- Client UI: 報酬画面に「リロール」ボタン (capability && !RerollUsed で表示)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 8: Lazy Unknown resolve (NodeEffectResolver overhaul)

**Files:**
- Modify: `src/Core/Map/UnknownResolver.cs` (`ResolveOne` 追加)
- Modify: `src/Core/Run/NodeEffectResolver.cs` (`TileKind.Unknown` lazy 分岐)
- Modify: `src/Server/Services/RunStartService.cs` (`ResolveAll` 呼び出し削除)
- Modify: `src/Core/Run/RunState.cs` (validation 緩和、必要なら)
- Test: `tests/Core.Tests/Run/NodeEffectResolverLazyUnknownTests.cs` (新規)

**重要な前提:** NodeEffectResolver.Resolve は現状 `(state, kind, currentRow, data, rng)` のシグネチャ。lazy resolve には `UnknownResolutionConfig` が必要。`DataCatalog` がそれを持っていない場合、どう渡すか実装時判断:
- (a) `DataCatalog` に `UnknownConfig` フィールド追加
- (b) `Resolve` に追加引数として渡す
- (c) RunState に config への参照を持たせる

実装時に最小変更を選ぶ。本プランでは (a) を仮定して書く。

- [ ] **Step 8.1: UnknownResolver.ResolveOne 追加**

`src/Core/Map/UnknownResolver.cs` に追加:
```csharp
/// <summary>
/// Phase 10.6.B T8: 1 ノードだけを与えられた重みで決定的に抽選する。
/// NodeEffectResolver から lazy resolve 時に呼ばれる。
/// 全 weight が 0 の場合は MapGenerationConfigException を投げる
/// (caller 側で fallback 処理)。
/// </summary>
public static TileKind ResolveOne(ImmutableDictionary<TileKind, double> weights, IRng rng)
{
    ArgumentNullException.ThrowIfNull(weights);
    ArgumentNullException.ThrowIfNull(rng);

    var entries = weights.Where(kv => kv.Value > 0).ToArray();
    double totalWeight = entries.Sum(kv => kv.Value);
    if (totalWeight <= 0)
        throw new MapGenerationConfigException("ResolveOne: all weights are zero");

    double r = rng.NextDouble() * totalWeight;
    double acc = 0;
    foreach (var kv in entries)
    {
        acc += kv.Value;
        if (r < acc) return kv.Key;
    }
    return entries[^1].Key;
}
```

- [ ] **Step 8.2: NodeEffectResolver の Unknown 分岐を lazy 化**

`src/Core/Run/NodeEffectResolver.cs`:
```csharp
public static RunState Resolve(
    RunState state, TileKind kind, int currentRow, DataCatalog data, IRng rng)
{
    state = state with {
        ActiveMerchant = null,
        ActiveEvent = null,
        ActiveRestPending = false,
        ActiveRestCompleted = false,
        ActiveActStartRelicChoice = null,
    };

    var table = data.RewardTables["act1"];
    return kind switch
    {
        TileKind.Start => ...,
        // ... 既存分岐 ...
        TileKind.Unknown => ResolveUnknownAndDispatch(state, currentRow, data, rng),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

private static RunState ResolveUnknownAndDispatch(
    RunState state, int currentRow, DataCatalog data, IRng rng)
{
    int nodeId = state.CurrentNodeId;

    // 既に解決済 (cache hit) ならその値で再 dispatch
    if (state.UnknownResolutions.TryGetValue(nodeId, out var cached))
        return Resolve(state, cached, currentRow, data, rng);

    // 未解決 → modifier 適用後に lazy resolve
    var weights = Relics.PassiveModifiers.ApplyUnknownWeightDeltas(
        data.UnknownConfig, state, data);

    // 全 weight 0 fallback: 元 config に戻す (defensive)
    if (weights.Values.Sum() <= 0)
        weights = data.UnknownConfig.Weights;

    var resolved = Map.UnknownResolver.ResolveOne(weights, rng);

    // 解決結果を cache に追記
    var newState = state with {
        UnknownResolutions = state.UnknownResolutions.SetItem(nodeId, resolved)
    };

    // resolved kind で再 dispatch (再帰、ただし resolved は Unknown ではないので 1 段)
    return Resolve(newState, resolved, currentRow, data, rng);
}
```

(注: `data.UnknownConfig` の正確な field 名は実装時 `DataCatalog` を読んで確認。無ければ追加するか、`Resolve` の引数として渡す形に変更。)

- [ ] **Step 8.3: RunStartService の ResolveAll 呼び出しを削除**

`src/Server/Services/RunStartService.cs:64` (run start) と `:118` (act transition):
```csharp
// Before:
var resolutions = UnknownResolver.ResolveAll(
    map, _mapConfig.UnknownResolutionWeights, new SystemRng(unchecked(seed + 1)));

// After:
var resolutions = ImmutableDictionary<int, TileKind>.Empty;
```

(同様に line 118 も `ResolveAll` 呼び出し削除して `Empty` を返す。)

- [ ] **Step 8.4: 失敗テスト追加**

新規 `tests/Core.Tests/Run/NodeEffectResolverLazyUnknownTests.cs`:
```csharp
[Fact]
public void Resolve_Unknown_LazyResolves_AndCachesResult()
{
    var state = SampleStateWithUnknownNode(BaseCatalog) with {
        UnknownResolutions = ImmutableDictionary<int, TileKind>.Empty
    };
    var s1 = NodeEffectResolver.Resolve(state, TileKind.Unknown, currentRow: 5, BaseCatalog, new SequentialRng(1UL));

    Assert.True(s1.UnknownResolutions.ContainsKey(state.CurrentNodeId));
    var resolved = s1.UnknownResolutions[state.CurrentNodeId];
    Assert.True(resolved is TileKind.Enemy or TileKind.Elite or TileKind.Merchant or TileKind.Rest or TileKind.Treasure);
}

[Fact]
public void Resolve_Unknown_WithRelicWeightDelta_BiasesOutcome()
{
    // unknownTreasureWeightDelta +1000 を持つ relic で Treasure に解決される
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "treasure_seeker",
        new[] { new CardEffect("unknownTreasureWeightDelta", EffectScope.Self, null, 1000, Trigger: "Passive") });
    var state = SampleStateWithUnknownNode(fake) with {
        UnknownResolutions = ImmutableDictionary<int, TileKind>.Empty,
        Relics = new[] { "treasure_seeker" }
    };

    var s1 = NodeEffectResolver.Resolve(state, TileKind.Unknown, currentRow: 5, fake, new SequentialRng(1UL));

    Assert.Equal(TileKind.Treasure, s1.UnknownResolutions[state.CurrentNodeId]);
}

[Fact]
public void Resolve_Unknown_AllWeightsZero_FallsBackToConfig()
{
    // 全 weight delta -infinity 級で全 weight 0 にしても、元 config で抽選される
    var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
        "anti_everything",
        new[] {
            new CardEffect("unknownEnemyWeightDelta", EffectScope.Self, null, -10000, Trigger: "Passive"),
            new CardEffect("unknownEliteWeightDelta", EffectScope.Self, null, -10000, Trigger: "Passive"),
            new CardEffect("unknownMerchantWeightDelta", EffectScope.Self, null, -10000, Trigger: "Passive"),
            new CardEffect("unknownRestWeightDelta", EffectScope.Self, null, -10000, Trigger: "Passive"),
            new CardEffect("unknownTreasureWeightDelta", EffectScope.Self, null, -10000, Trigger: "Passive"),
        });
    var state = SampleStateWithUnknownNode(fake) with {
        UnknownResolutions = ImmutableDictionary<int, TileKind>.Empty,
        Relics = new[] { "anti_everything" }
    };

    // fallback により例外なく解決される
    var s1 = NodeEffectResolver.Resolve(state, TileKind.Unknown, currentRow: 5, fake, new SequentialRng(1UL));
    Assert.True(s1.UnknownResolutions.ContainsKey(state.CurrentNodeId));
}
```

(注: `SampleStateWithUnknownNode` は既存テスト fixture を流用、無ければ inline で構築。)

- [ ] **Step 8.5: Run tests PASS + no regression**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NodeEffectResolverLazyUnknownTests"
dotnet test tests/Core.Tests/Core.Tests.csproj
dotnet test tests/Server.Tests/Server.Tests.csproj
```
Expected: Core 1220 + 3 = 1223 PASS、Server pre-existing 4 件 failure 以外全 PASS。

- [ ] **Step 8.6: Commit**

```bash
git add src/Core/Map/UnknownResolver.cs src/Core/Run/NodeEffectResolver.cs src/Server/Services/RunStartService.cs tests/Core.Tests/Run/NodeEffectResolverLazyUnknownTests.cs
# DataCatalog に UnknownConfig 追加した場合はそれも add
git commit -m "$(cat <<'EOF'
feat(map): lazy Unknown tile resolution + relic weight modifier (Phase 10.6.B T8)

Unknown タイルを map 生成時に pre-resolve するのをやめ、NodeEffectResolver で
ノード入場時に lazy 解決する形に変更。これにより relic 取得後に
unknownXxxWeightDelta modifier が反映される。

- UnknownResolver.ResolveOne(weights, rng) 新メソッド (1 ノード抽選)
- NodeEffectResolver.ResolveUnknownAndDispatch private method
- RunStartService の ResolveAll 呼び出しを削除、UnknownResolutions を空で開始
- 全 weight 0 fallback で元 config に戻す defensive 処理

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 9: DevMetaController に新 action 追加

**Files:**
- Modify: `src/Server/Controllers/DevMetaController.cs`
- Test: `tests/Server.Tests/Controllers/DevMetaControllerTests.cs` (既存)

- [ ] **Step 9.1: 失敗テスト追加 (or 既存テストに assertion 追加)**

`tests/Server.Tests/Controllers/DevMetaControllerTests.cs` に:
```csharp
[Fact]
public void GetMeta_EffectActions_IncludesPhase10_6B_PassiveActions()
{
    var meta = _controller.GetMeta();
    var actionsObj = meta.GetType().GetProperty("effectActions")?.GetValue(meta);
    var actions = (string[])actionsObj!;
    Assert.Contains("energyPerTurnBonus", actions);
    Assert.Contains("cardsDrawnPerTurnBonus", actions);
    Assert.Contains("goldRewardMultiplier", actions);
    Assert.Contains("shopPriceMultiplier", actions);
    Assert.Contains("rewardCardChoicesBonus", actions);
    Assert.Contains("rewardRerollAvailable", actions);
    Assert.Contains("unknownEnemyWeightDelta", actions);
    Assert.Contains("unknownEliteWeightDelta", actions);
    Assert.Contains("unknownMerchantWeightDelta", actions);
    Assert.Contains("unknownRestWeightDelta", actions);
    Assert.Contains("unknownTreasureWeightDelta", actions);
    Assert.Contains("restHealBonus", actions);
}
```

- [ ] **Step 9.2: Run test FAIL**

```bash
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~DevMetaControllerTests.GetMeta_EffectActions_IncludesPhase10_6B"
```
Expected: FAIL (action リストに新 12 項目が無い)。

- [ ] **Step 9.3: DevMetaController の effectActions 配列を拡張**

`src/Server/Controllers/DevMetaController.cs:43`:
```csharp
effectActions = new[]
{
    "attack", "block", "buff", "debuff", "heal", "draw", "discard",
    "gainEnergy", "gainMaxEnergy", "exhaustCard",
    "upgrade", "summon", "selfDamage", "addCard", "recoverFromDiscard",
    // Phase 10.6.B Passive modifier actions
    "energyPerTurnBonus", "cardsDrawnPerTurnBonus",
    "goldRewardMultiplier", "shopPriceMultiplier",
    "rewardCardChoicesBonus", "rewardRerollAvailable",
    "unknownEnemyWeightDelta", "unknownEliteWeightDelta",
    "unknownMerchantWeightDelta", "unknownRestWeightDelta", "unknownTreasureWeightDelta",
    "restHealBonus",
},
```

- [ ] **Step 9.4: Run test PASS + no regression**

```bash
dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~DevMetaControllerTests"
```
Expected: PASS。

- [ ] **Step 9.5: Commit**

```bash
git add src/Server/Controllers/DevMetaController.cs tests/Server.Tests/Controllers/DevMetaControllerTests.cs
git commit -m "$(cat <<'EOF'
feat(dev-meta): list Phase 10.6.B passive modifier actions (T9)

DevMetaController.effectActions に 12 個の Passive 用 action を追加し、
relic editor で新 action を選択肢として表示する。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git push origin master
```

---

## Task 10: 統合テスト + push 確認 + memory 更新

**Files:** memory のみ (production code 変更なし)

- [ ] **Step 10.1: 全 Core / Server / Client テスト緑確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj
dotnet test tests/Server.Tests/Server.Tests.csproj
cd src/Client && npx tsc --noEmit && npm run test:run
```
Expected:
- Core: ~1223 PASS (1178 + 45)
- Server: 既存 4 件 pre-existing failure 以外全 PASS + reroll 系新 test PASS
- Client: tsc clean、test 全 PASS

- [ ] **Step 10.2: 動作確認 (任意推奨)**

dev サーバ + Client 起動:
```bash
dotnet run --project src/Server &
cd src/Client && npm run dev
```

dev menu で fake relic 作成:
- 例: `{"trigger":"Passive","action":"goldRewardMultiplier","scope":"Self","amount":50}`
- relic を持って battle 終了 → reward gold が 1.5 倍になっていることを確認

- 例: `{"trigger":"Passive","action":"shopPriceMultiplier","scope":"Self","amount":-50}`
- merchant 入場 → 全価格 50% に確認

- 例: `{"trigger":"Passive","action":"rewardRerollAvailable","scope":"Self","amount":1}`
- reward 画面に「リロール」ボタン表示 → click で choices 変化 → 2 回目 click で disabled

- [ ] **Step 10.3: 全 push 確認**

```bash
git status --short
git log --oneline origin/master..HEAD
```
Expected: working tree clean、未 push commit 無し。

- [ ] **Step 10.4: Memory 更新**

`C:\Users\Metaverse\.claude\projects\c--Users-Metaverse-projects-roguelike-cardgame\memory\project_phase_status.md` を Phase 10.6.B 完了状態に更新:
```markdown
- **Phase 10.6.B: passive modifier system** (T1〜T10 全完了、master 直接 commit + push)
  - T1: PassiveModifiers façade (11 modifier action + 既存 restHealBonus 移動)
  - T2: CardTextFormatter Passive 文言対応
  - T3: BattleState.DrawPerTurn + BattleEngine.Start で energy/draw modifier snapshot
  - T4: MerchantInventoryGenerator で shopPriceMultiplier 適用
  - T5: RewardGenerator で rewardCardChoicesBonus + RegenerateCardChoicesForReward 切り出し
  - T6: RewardActions.AssignReward 集約ヘルパ + 5 site 切替 + goldRewardMultiplier 適用
  - T7: 報酬リロール mechanic (RewardState.RerollUsed + RewardActions.Reroll + endpoint + UI)
  - T8: Lazy Unknown resolve (NodeEffectResolver で入場時解決 + relic modifier 反映)
  - T9: DevMetaController に 12 action 追加
  - テスト: 1178 → ~1223 件 (+~45)
```

`MEMORY.md` の最初の行を更新:
```markdown
- [フェーズ進捗 (2026-05-04)](project_phase_status.md) — Phase 10.6.B (passive modifier system) 完了。次は 10.5.L2 (potion editor) / 10.5.L3 / 10.5.L4 / 10.5.M2-Choose / Phase 9 (multiplayer)
```

(memory ファイルは git 管理外なので commit 不要。)

- [ ] **Step 10.5: Final push 確認**

```bash
git status --short
```
Expected: clean。

---

## Out of Scope (本 phase 非対応、将来検討)

- **手札上限 +N** (`maxHandSizeBonus`): PassiveModifiers façade に 1 メソッド追加で対応可能
- **マップ先読み +N** (`mapNodeRevealBonus`): Map UI 介入が必要
- **ボス報酬選択肢 +N**: BossRewardFlow 改修
- **イベント遭遇率補正**: EventPool.Pick 改修
- **Status duration modifier**: 状態異常持続ターン数の補正 — 既存 status system 改修要
- **Cost reduction passive** (アップグレード済カードコスト -1): card cost 計算系改修
- **Multiplayer Unknown 共有解決 (X2)**: Phase 9 着手時に再検討、`PassiveModifiers.ApplyUnknownWeightDeltas` を `RunState[]` 型に拡張
- **Server pre-existing 4 件 failure**: `BattleStateDtoMapperTests` 2 件 + `DevRelicsControllerTests` 2 件 — Phase 10.6.A から繰越、独立 task として要対応
