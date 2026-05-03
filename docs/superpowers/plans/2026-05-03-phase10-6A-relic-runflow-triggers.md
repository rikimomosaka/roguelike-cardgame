# Phase 10.6.A: Relic Run-Flow Trigger 拡張 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 10.5.L1.5 で定義済の 18 値統一トリガーのうち、現在 engine 発火していない 5 値 (`OnEnterShop` / `OnEnterRestSite` / `OnRest` / `OnRewardGenerated` / `OnCardAddedToDeck`) を engine 側に実装し、relic がショップ入場・休憩 site 入場・実際の Heal 行動・報酬生成・デッキ追加時に副作用を起こせるようにする。

**Architecture:**
- Core 側のルートは [src/Core/Relics/NonBattleRelicEffects.cs](src/Core/Relics/NonBattleRelicEffects.cs) に集約。既存の `ApplyOnPickup` / `ApplyOnMapTileResolved` と同じ pattern (per-effect trigger filter + `ApplyEffectsForTrigger` switch) で 5 メソッド追加。
- 発火点: `OnEnterShop` / `OnEnterRestSite` は [src/Core/Run/NodeEffectResolver.cs](src/Core/Run/NodeEffectResolver.cs) の `TileKind.Merchant` / `TileKind.Rest` 分岐。`OnRest` は [src/Core/Rest/RestActions.cs](src/Core/Rest/RestActions.cs) の `Heal()`。`OnCardAddedToDeck` は新ヘルパ `src/Core/Run/RunDeckActions.cs` に集約し、`MerchantActions.BuyCard` と `RewardApplier.PickCard` を refactor。`OnRewardGenerated` は `RewardGenerator.Generate*` の 5 呼び出し元 (Core 3 + Server 2) で reward 確定直後に発火。
- Effect actions は既存 switch (`gainMaxHp` / `gainGold`) に `healHp` を 1 つだけ追加 (OnRest で必要)。それ以外の action 拡張は本フェーズ対象外。
- 既存の relic JSON (36 個) は L1.5 で effects=[] にリセット済なので動作変化なし。新トリガーのテストは fake relic を catalog 注入して構築する (NonBattleRelicEffectsTests の既存 pattern を踏襲)。

**Tech Stack:** C# .NET 10、xUnit、ImmutableArray ベースの record state。

---

## File Structure

**Create:**
- `src/Core/Run/RunDeckActions.cs` — `AddCardToDeck(s, cardId, catalog)` ヘルパ (OnCardAddedToDeck 発火集約点)
- `tests/Core.Tests/Run/RunDeckActionsTests.cs` — 上記の TDD

**Modify:**
- `src/Core/Relics/NonBattleRelicEffects.cs` — 5 つの新 trigger メソッド追加 + `healHp` action 追加
- `src/Core/Run/NodeEffectResolver.cs` — `OnEnterShop` / `OnEnterRestSite` 発火、`OnRewardGenerated` 発火 (Treasure 分岐)
- `src/Core/Rest/RestActions.cs` — `Heal()` で `OnRest` 発火
- `src/Core/Run/BossRewardFlow.cs` — `OnRewardGenerated` 発火
- `src/Core/Events/EventResolver.cs` — `OnRewardGenerated` 発火 (event 報酬時)
- `src/Core/Merchant/MerchantActions.cs` — `BuyCard` を `RunDeckActions.AddCardToDeck` に差し替え
- `src/Core/Rewards/RewardApplier.cs` — `PickCard` を `RunDeckActions.AddCardToDeck` に差し替え
- `src/Server/Controllers/BattleController.cs` — RewardGenerator 呼び出し直後に `OnRewardGenerated` 発火
- `src/Server/Controllers/RunsController.cs` — 同上

**Test:**
- `tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs` — 5 trigger × 各 1〜2 ケース、`healHp` action のケース
- `tests/Core.Tests/Run/NodeEffectResolverTests.cs` — Merchant/Rest 入場で fake relic が発火するケース、Treasure で OnRewardGenerated 発火
- `tests/Core.Tests/Rest/RestActionsTests.cs` (既存または新規) — `Heal()` で OnRest 発火
- `tests/Core.Tests/Run/RunDeckActionsTests.cs` (新規) — `AddCardToDeck` で OnCardAddedToDeck 発火 + Deck に追加されること
- `tests/Core.Tests/Merchant/MerchantActionsTests.cs` (既存) — `BuyCard` 経由で OnCardAddedToDeck 発火確認
- `tests/Core.Tests/Rewards/RewardApplierTests.cs` (既存) — `PickCard` 経由で OnCardAddedToDeck 発火確認、`ClaimRelic` で OnRewardGenerated 発火 (no — relic claim じゃなくて reward 生成タイミングなので 別メソッド)
- `tests/Core.Tests/Events/EventResolverTests.cs` (既存) — event reward 生成で OnRewardGenerated 発火
- `tests/Core.Tests/Run/BossRewardFlowTests.cs` (既存) — boss reward 生成で OnRewardGenerated 発火
- `tests/Server.Tests/...` — controller 経由 OnRewardGenerated 発火 (任意。Core 側 5 呼び出しでカバーできるなら省略可)

---

## Task 1: NonBattleRelicEffects に 5 trigger メソッド + healHp action 追加

**Files:**
- Modify: `src/Core/Relics/NonBattleRelicEffects.cs`
- Test: `tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs`

- [ ] **Step 1.1: 失敗するテストを書く (ApplyOnEnterShop の基本ケース)**

`tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs` の末尾 (closing brace の直前) に追記:

```csharp
[Fact]
public void ApplyOnEnterShop_GainGoldEffect_AddsGold()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "fake_shop",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 5, Trigger: "OnEnterShop") });
    var s0 = Sample(gold: 100) with { Relics = new System.Collections.Generic.List<string> { "fake_shop" } };
    var s1 = NonBattleRelicEffects.ApplyOnEnterShop(s0, fake);
    Assert.Equal(105, s1.Gold);
}

[Fact]
public void ApplyOnEnterRestSite_HealHpEffect_HealsCurrentHpClampedByMaxHp()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "fake_rest_site",
        effects: new[] { new CardEffect(
            "healHp", EffectScope.Self, null, 30, Trigger: "OnEnterRestSite") });
    var s0 = Sample(hp: 60, maxHp: 80) with { Relics = new System.Collections.Generic.List<string> { "fake_rest_site" } };
    var s1 = NonBattleRelicEffects.ApplyOnEnterRestSite(s0, fake);
    Assert.Equal(80, s1.CurrentHp); // clamped to max
}

[Fact]
public void ApplyOnRest_GainMaxHpEffect_IncreasesMaxAndCurrentHp()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "fake_rest",
        effects: new[] { new CardEffect(
            "gainMaxHp", EffectScope.Self, null, 1, Trigger: "OnRest") });
    var s0 = Sample(hp: 50, maxHp: 80) with { Relics = new System.Collections.Generic.List<string> { "fake_rest" } };
    var s1 = NonBattleRelicEffects.ApplyOnRest(s0, fake);
    Assert.Equal(81, s1.MaxHp);
    Assert.Equal(51, s1.CurrentHp);
}

[Fact]
public void ApplyOnRewardGenerated_GainGoldEffect_GrantsBonus()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "fake_reward",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 3, Trigger: "OnRewardGenerated") });
    var s0 = Sample(gold: 50) with { Relics = new System.Collections.Generic.List<string> { "fake_reward" } };
    var s1 = NonBattleRelicEffects.ApplyOnRewardGenerated(s0, fake);
    Assert.Equal(53, s1.Gold);
}

[Fact]
public void ApplyOnCardAddedToDeck_GainGoldEffect_GrantsBonus()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "fake_card_added",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 2, Trigger: "OnCardAddedToDeck") });
    var s0 = Sample(gold: 10) with { Relics = new System.Collections.Generic.List<string> { "fake_card_added" } };
    var s1 = NonBattleRelicEffects.ApplyOnCardAddedToDeck(s0, fake);
    Assert.Equal(12, s1.Gold);
}

[Fact]
public void ApplyOnRest_NonOnRestTriggerEffect_NoOp()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "fake_other",
        effects: new[] { new CardEffect(
            "gainMaxHp", EffectScope.Self, null, 5, Trigger: "OnPickup") });
    var s0 = Sample(hp: 50, maxHp: 80) with { Relics = new System.Collections.Generic.List<string> { "fake_other" } };
    var s1 = NonBattleRelicEffects.ApplyOnRest(s0, fake);
    Assert.Equal(80, s1.MaxHp);
    Assert.Equal(50, s1.CurrentHp);
}

[Fact]
public void ApplyOnEnterShop_ImplementedFalseRelic_NoOp()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "fake_unimpl",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 100, Trigger: "OnEnterShop") },
        implemented: false);
    var s0 = Sample(gold: 50) with { Relics = new System.Collections.Generic.List<string> { "fake_unimpl" } };
    var s1 = NonBattleRelicEffects.ApplyOnEnterShop(s0, fake);
    Assert.Equal(50, s1.Gold);
}
```

(注: `BuildCatalogWithFakeRelic` の `implemented` 引数は既存ヘルパが対応しているか要確認。していなければ既存ヘルパを修正する必要があるが、おそらく対応済。もし未対応の場合は `implemented: false` のテストケースだけ後回し。)

- [ ] **Step 1.2: テスト実行で失敗確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NonBattleRelicEffectsTests"
```

期待: 7 件失敗 (`ApplyOnEnterShop` などのメソッドが存在しないコンパイルエラー、または `healHp` action 未対応)

- [ ] **Step 1.3: 実装を `src/Core/Relics/NonBattleRelicEffects.cs` に追加**

ファイル全体を以下に置き換える:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>
/// 戦闘外（マップ／休憩／取得時／ショップ／報酬生成／デッキ追加時）でのレリック効果を適用する純粋関数群。
/// Phase 10 設計書 第 2-7 章 / Phase 10.5.L1.5 unified-triggers / Phase 10.6.A run-flow triggers 参照。
/// Action 文字列 (gainMaxHp / gainGold / healHp) で効果を識別する。
/// </summary>
public static class NonBattleRelicEffects
{
    public static RunState ApplyOnPickup(RunState s, string relicId, DataCatalog catalog)
    {
        if (!catalog.TryGetRelic(relicId, out var def)) return s;
        if (!def.Implemented) return s;
        return ApplyEffectsForTrigger(s, def, "OnPickup");
    }

    public static RunState ApplyOnMapTileResolved(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnMapTileResolved");

    public static RunState ApplyOnEnterShop(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnEnterShop");

    public static RunState ApplyOnEnterRestSite(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnEnterRestSite");

    public static RunState ApplyOnRest(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnRest");

    public static RunState ApplyOnRewardGenerated(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnRewardGenerated");

    public static RunState ApplyOnCardAddedToDeck(RunState s, DataCatalog catalog)
        => ApplyForAllOwnedRelics(s, catalog, "OnCardAddedToDeck");

    public static int ApplyPassiveRestHealBonus(int baseBonus, RunState s, DataCatalog catalog)
    {
        int bonus = baseBonus;
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (!def.Implemented) continue;
            foreach (var eff in def.Effects)
            {
                if (eff.Trigger != "Passive") continue;
                if (eff.Action == "restHealBonus") bonus += eff.Amount;
            }
        }
        return bonus;
    }

    private static RunState ApplyForAllOwnedRelics(RunState s, DataCatalog catalog, string trigger)
    {
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (!def.Implemented) continue;
            s = ApplyEffectsForTrigger(s, def, trigger);
        }
        return s;
    }

    private static RunState ApplyEffectsForTrigger(RunState s, RelicDefinition def, string trigger)
    {
        foreach (var eff in def.Effects)
        {
            if (eff.Trigger != trigger) continue;
            s = eff.Action switch
            {
                "gainMaxHp" => s with { MaxHp = s.MaxHp + eff.Amount, CurrentHp = s.CurrentHp + eff.Amount },
                "gainGold"  => s with { Gold = s.Gold + eff.Amount },
                "healHp"    => s with { CurrentHp = System.Math.Min(s.MaxHp, s.CurrentHp + eff.Amount) },
                _           => s,
            };
        }
        return s;
    }
}
```

- [ ] **Step 1.4: テスト実行で全合格確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NonBattleRelicEffectsTests"
```

期待: PASS (既存ケース + 新規 7 ケース)。

- [ ] **Step 1.5: Core 全体テスト**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj
```

期待: 1156+7 件 PASS (regression なし)。

- [ ] **Step 1.6: Commit**

```bash
git add src/Core/Relics/NonBattleRelicEffects.cs tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs
git commit -m "$(cat <<'EOF'
feat(relics): non-battle trigger handlers for run-flow events (Phase 10.6.A T1)

NonBattleRelicEffects に 5 trigger メソッドを追加:
- ApplyOnEnterShop / ApplyOnEnterRestSite / ApplyOnRest
- ApplyOnRewardGenerated / ApplyOnCardAddedToDeck

ApplyEffectsForTrigger の action switch に healHp を追加 (clamped by MaxHp)。
ApplyOnMapTileResolved も含め共通ヘルパ ApplyForAllOwnedRelics に集約。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: NodeEffectResolver で OnEnterShop 発火

**Files:**
- Modify: `src/Core/Run/NodeEffectResolver.cs:76-83` (`StartMerchant`)
- Test: `tests/Core.Tests/Run/NodeEffectResolverTests.cs`

- [ ] **Step 2.1: 失敗テストを書く**

`NodeEffectResolverTests.cs` 末尾に追記 (`BuildCatalogWithFakeRelic` 相当のヘルパが無い場合は同テストファイル内に既存パターンで追加):

```csharp
[Fact]
public void Resolve_TileKindMerchant_FiresOnEnterShopTrigger()
{
    var fake = BuildCatalogWithFakeRelicAndMerchantPrices(
        id: "shopper",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 7, Trigger: "OnEnterShop") });
    var s0 = MakeBaseState(fake) with {
        Gold = 100,
        Relics = new System.Collections.Generic.List<string> { "shopper" }
    };
    var rng = new FakeRng(new int[20], System.Array.Empty<double>());

    var s1 = NodeEffectResolver.Resolve(s0, TileKind.Merchant, currentRow: 5, fake, rng);

    Assert.Equal(107, s1.Gold);
    Assert.NotNull(s1.ActiveMerchant);
}
```

(注: `MakeBaseState` / `BuildCatalogWithFakeRelicAndMerchantPrices` は既存テストヘルパが無ければファイル先頭に最小実装で追加。EmbeddedDataLoader.LoadCatalog() で base catalog を取って `with { Relics = ... }` で fake 注入する pattern を踏襲。MerchantPrices が null だと StartMerchant が例外を投げるので、必ず MerchantPrices 付きの catalog を使う。既存 `NodeEffectResolverTests.cs` を読んで確立済 pattern があればそれを使う。)

- [ ] **Step 2.2: テスト失敗確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NodeEffectResolverTests.Resolve_TileKindMerchant_FiresOnEnterShopTrigger"
```

期待: FAIL (Gold = 100 のまま)。

- [ ] **Step 2.3: 実装**

`src/Core/Run/NodeEffectResolver.cs` の `StartMerchant` を変更:

```csharp
private static RunState StartMerchant(RunState s, DataCatalog data, IRng rng)
{
    if (data.MerchantPrices is null)
        throw new InvalidOperationException("DataCatalog.MerchantPrices is not configured");
    var inv = MerchantInventoryGenerator.Generate(data, data.MerchantPrices, s, rng);
    var next = s with { ActiveMerchant = inv };
    next = BestiaryTracker.NoteCardsSeen(next, inv.Cards.Select(o => o.Id));
    return Relics.NonBattleRelicEffects.ApplyOnEnterShop(next, data);
}
```

(file top に `using RoguelikeCardGame.Core.Relics;` 追加。)

- [ ] **Step 2.4: テスト合格確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NodeEffectResolverTests"
```

期待: PASS。

- [ ] **Step 2.5: Commit**

```bash
git add src/Core/Run/NodeEffectResolver.cs tests/Core.Tests/Run/NodeEffectResolverTests.cs
git commit -m "$(cat <<'EOF'
feat(run): fire OnEnterShop relic trigger on merchant entry (Phase 10.6.A T2)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: NodeEffectResolver で OnEnterRestSite 発火

**Files:**
- Modify: `src/Core/Run/NodeEffectResolver.cs:44` (`TileKind.Rest` 分岐)
- Test: `tests/Core.Tests/Run/NodeEffectResolverTests.cs`

- [ ] **Step 3.1: 失敗テスト**

```csharp
[Fact]
public void Resolve_TileKindRest_FiresOnEnterRestSiteTrigger()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "rest_camper",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 4, Trigger: "OnEnterRestSite") });
    var s0 = MakeBaseState(fake) with {
        Gold = 50,
        Relics = new System.Collections.Generic.List<string> { "rest_camper" }
    };
    var rng = new FakeRng(new int[20], System.Array.Empty<double>());

    var s1 = NodeEffectResolver.Resolve(s0, TileKind.Rest, currentRow: 5, fake, rng);

    Assert.True(s1.ActiveRestPending);
    Assert.Equal(54, s1.Gold);
}
```

- [ ] **Step 3.2: テスト失敗確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~Resolve_TileKindRest_FiresOnEnterRestSiteTrigger"
```

期待: FAIL (Gold = 50 のまま)。

- [ ] **Step 3.3: 実装**

`NodeEffectResolver.Resolve` の `TileKind.Rest` 分岐を変更:

```csharp
TileKind.Rest => Relics.NonBattleRelicEffects.ApplyOnEnterRestSite(
    state with { ActiveRestPending = true }, data),
```

- [ ] **Step 3.4: テスト合格確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NodeEffectResolverTests"
```

期待: PASS。

- [ ] **Step 3.5: Commit**

```bash
git add src/Core/Run/NodeEffectResolver.cs tests/Core.Tests/Run/NodeEffectResolverTests.cs
git commit -m "$(cat <<'EOF'
feat(run): fire OnEnterRestSite relic trigger on rest tile entry (Phase 10.6.A T3)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: RestActions.Heal で OnRest 発火

**Files:**
- Modify: `src/Core/Rest/RestActions.cs:11-24` (`Heal`)
- Test: `tests/Core.Tests/Rest/RestActionsTests.cs` (既存)

- [ ] **Step 4.1: 失敗テストを書く**

`tests/Core.Tests/Rest/RestActionsTests.cs` 末尾に追記:

```csharp
[Fact]
public void Heal_WithOnRestRelic_FiresGainMaxHpAfterHealing()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "rest_grower",
        effects: new[] { new CardEffect(
            "gainMaxHp", EffectScope.Self, null, 1, Trigger: "OnRest") });
    var s0 = MakeRestPendingState(fake) with {
        CurrentHp = 50,
        MaxHp = 80,
        Relics = new System.Collections.Generic.List<string> { "rest_grower" }
    };

    var s1 = RestActions.Heal(s0, fake);

    Assert.True(s1.ActiveRestCompleted);
    // base 30% of 80 = 24 heal → 74. Then +1 MaxHp/+1 CurrentHp from OnRest = 75/81.
    Assert.Equal(81, s1.MaxHp);
    Assert.Equal(75, s1.CurrentHp);
}

[Fact]
public void UpgradeCard_DoesNotFireOnRestTrigger()
{
    var fake = BuildCatalogWithFakeRelicAndUpgradableCard(
        relicId: "rest_grower",
        effects: new[] { new CardEffect(
            "gainMaxHp", EffectScope.Self, null, 99, Trigger: "OnRest") });
    var s0 = MakeRestPendingStateWithUpgradableDeck(fake) with {
        Relics = new System.Collections.Generic.List<string> { "rest_grower" }
    };
    int origMaxHp = s0.MaxHp;

    var s1 = RestActions.UpgradeCard(s0, deckIndex: 0, fake);

    Assert.True(s1.ActiveRestCompleted);
    Assert.Equal(origMaxHp, s1.MaxHp); // OnRest fires only on Heal, not Upgrade
}
```

(注: `MakeRestPendingState` / `BuildCatalogWithFakeRelic` 等のヘルパが既存テストにあれば再利用。なければ最小実装でファイル冒頭に追加。`MakeRestPendingStateWithUpgradableDeck` は base catalog から upgrade 可能なカード ID 1 つを deck 先頭に入れた state を作る。)

- [ ] **Step 4.2: テスト失敗確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RestActionsTests"
```

期待: 新規 2 件 FAIL。

- [ ] **Step 4.3: 実装**

`src/Core/Rest/RestActions.cs` の `Heal` を変更:

```csharp
public static RunState Heal(RunState s, DataCatalog catalog)
{
    ArgumentNullException.ThrowIfNull(s);
    ArgumentNullException.ThrowIfNull(catalog);
    if (!s.ActiveRestPending)
        throw new InvalidOperationException("Rest is not pending");
    if (s.ActiveRestCompleted)
        throw new InvalidOperationException("Rest already completed");

    int baseAmount = (int)Math.Ceiling(s.MaxHp * 0.30);
    int total = NonBattleRelicEffects.ApplyPassiveRestHealBonus(baseAmount, s, catalog);
    int newHp = Math.Min(s.MaxHp, s.CurrentHp + total);
    var s1 = s with { CurrentHp = newHp, ActiveRestCompleted = true };
    return NonBattleRelicEffects.ApplyOnRest(s1, catalog);
}
```

- [ ] **Step 4.4: テスト合格確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RestActionsTests"
```

期待: PASS。

- [ ] **Step 4.5: Commit**

```bash
git add src/Core/Rest/RestActions.cs tests/Core.Tests/Rest/RestActionsTests.cs
git commit -m "$(cat <<'EOF'
feat(rest): fire OnRest relic trigger after Heal action (Phase 10.6.A T4)

UpgradeCard では発火しない (Q1 設計確定)。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: RunDeckActions.AddCardToDeck ヘルパ作成 (OnCardAddedToDeck 集約点)

**Files:**
- Create: `src/Core/Run/RunDeckActions.cs`
- Test: `tests/Core.Tests/Run/RunDeckActionsTests.cs`

- [ ] **Step 5.1: 失敗テストを書く**

`tests/Core.Tests/Run/RunDeckActionsTests.cs` を新規作成:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunDeckActionsTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Sample() =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 5, 3, 0, 0, 0, System.TimeSpan.Zero));

    private static DataCatalog CatalogWithFakeRelic(string id, CardEffect[] effects, bool implemented = true)
    {
        // 既存 NonBattleRelicEffectsTests.BuildCatalogWithFakeRelic と同じ pattern。
        // BaseCatalog をベースに fake relic 1 つを追加した catalog を返す。
        // (実装はテストフィクスチャ helper を参照、または同じユーティリティを利用)
        return TestCatalogHelpers.With(BaseCatalog, fakeRelics: new[] {
            new RelicDefinition(id, id, "common", "fake", true, "auto", "テスト用", "", implemented, effects.ToImmutableArray(), ImmutableArray<string>.Empty)
        });
    }

    [Fact]
    public void AddCardToDeck_AppendsCardInstance()
    {
        var s0 = Sample();
        int origDeckLen = s0.Deck.Length;

        var s1 = RunDeckActions.AddCardToDeck(s0, "strike_basic", BaseCatalog);

        Assert.Equal(origDeckLen + 1, s1.Deck.Length);
        Assert.Equal("strike_basic", s1.Deck[^1].Id);
        Assert.False(s1.Deck[^1].Upgraded);
    }

    [Fact]
    public void AddCardToDeck_FiresOnCardAddedToDeckTrigger()
    {
        var fake = CatalogWithFakeRelic(
            id: "card_collector",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 5, Trigger: "OnCardAddedToDeck") });
        var s0 = Sample() with {
            Gold = 10,
            Relics = new List<string> { "card_collector" }
        };

        var s1 = RunDeckActions.AddCardToDeck(s0, "strike_basic", fake);

        Assert.Equal(15, s1.Gold);
    }

    [Fact]
    public void AddCardToDeck_UnknownCardId_Throws()
    {
        var s0 = Sample();
        Assert.Throws<System.ArgumentException>(() =>
            RunDeckActions.AddCardToDeck(s0, "no_such_card", BaseCatalog));
    }
}
```

(注: `TestCatalogHelpers.With` は既存テストヘルパに無ければ、このタスク内で `tests/Core.Tests/_Helpers/TestCatalogHelpers.cs` を新規作成して `NonBattleRelicEffectsTests.BuildCatalogWithFakeRelic` の private 実装を public に切り出す。テストヘルパの DRY 化はリファクタとして別 commit にしても良い。テストヘルパが既存にあるなら再利用。)

- [ ] **Step 5.2: テスト失敗確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RunDeckActionsTests"
```

期待: コンパイルエラー (RunDeckActions が存在しない)。

- [ ] **Step 5.3: 実装**

`src/Core/Run/RunDeckActions.cs` を新規作成:

```csharp
using System;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Run;

/// <summary>
/// RunState のデッキ操作を集約するヘルパ。Phase 10.6.A で OnCardAddedToDeck トリガー集約点として導入。
/// MerchantActions.BuyCard / RewardApplier.PickCard など全カード追加経路はこのメソッド経由にする。
/// </summary>
public static class RunDeckActions
{
    public static RunState AddCardToDeck(RunState s, string cardId, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cardId);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.TryGetCard(cardId, out _))
            throw new ArgumentException($"unknown card id \"{cardId}\"", nameof(cardId));
        var s1 = s with { Deck = s.Deck.Add(new CardInstance(cardId, false)) };
        return NonBattleRelicEffects.ApplyOnCardAddedToDeck(s1, catalog);
    }
}
```

- [ ] **Step 5.4: テスト合格確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RunDeckActionsTests"
```

期待: PASS。

- [ ] **Step 5.5: Commit**

```bash
git add src/Core/Run/RunDeckActions.cs tests/Core.Tests/Run/RunDeckActionsTests.cs
# テストヘルパ切り出しがある場合: tests/Core.Tests/_Helpers/TestCatalogHelpers.cs も add
git commit -m "$(cat <<'EOF'
feat(run): RunDeckActions.AddCardToDeck helper firing OnCardAddedToDeck (Phase 10.6.A T5)

カードをデッキに追加する全経路の集約点。
unknown card id は ArgumentException を投げ、relic OnCardAddedToDeck を発火する。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: MerchantActions.BuyCard を RunDeckActions.AddCardToDeck に差し替え

**Files:**
- Modify: `src/Core/Merchant/MerchantActions.cs:14-28` (`BuyCard`)
- Test: `tests/Core.Tests/Merchant/MerchantActionsTests.cs` (既存)

- [ ] **Step 6.1: 失敗テストを書く**

`MerchantActionsTests.cs` 末尾に追記:

```csharp
[Fact]
public void BuyCard_FiresOnCardAddedToDeckTrigger()
{
    var fake = BuildCatalogWithFakeShopRelic(
        relicId: "card_collector",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 3, Trigger: "OnCardAddedToDeck") });
    // ↓ inv に "strike_basic" 1 枚 + price=5 の merchant を持つ state を構築
    var s0 = MakeMerchantStateWithCardOffer(fake, "strike_basic", price: 5) with {
        Gold = 100,
        Relics = new System.Collections.Generic.List<string> { "card_collector" }
    };

    var s1 = MerchantActions.BuyCard(s0, "strike_basic", fake);

    // 100 - 5 (price) + 3 (relic) = 98
    Assert.Equal(98, s1.Gold);
    Assert.Contains(s1.Deck, c => c.Id == "strike_basic");
}
```

- [ ] **Step 6.2: テスト失敗確認**

期待: FAIL (現状 100-5=95 になる、relic は発火しない)。

- [ ] **Step 6.3: 実装**

`MerchantActions.BuyCard` を変更:

```csharp
public static RunState BuyCard(RunState s, string cardId, DataCatalog catalog)
{
    var (inv, offer, idx) = RequireOffer(s, "card", cardId);
    if (s.Gold < offer.Price)
        throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
    if (!catalog.TryGetCard(cardId, out _))
        throw new ArgumentException($"unknown card id \"{cardId}\"", nameof(cardId));
    var soldOffer = offer with { Sold = true };
    var s1 = s with
    {
        Gold = s.Gold - offer.Price,
        ActiveMerchant = inv with { Cards = inv.Cards.SetItem(idx, soldOffer) },
    };
    return Run.RunDeckActions.AddCardToDeck(s1, cardId, catalog);
}
```

(file top に `using RoguelikeCardGame.Core.Run;` 追加。)

- [ ] **Step 6.4: テスト合格確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantActionsTests"
```

期待: PASS (既存ケース全部 + 新規 1)。

- [ ] **Step 6.5: Commit**

```bash
git add src/Core/Merchant/MerchantActions.cs tests/Core.Tests/Merchant/MerchantActionsTests.cs
git commit -m "$(cat <<'EOF'
refactor(merchant): route BuyCard through RunDeckActions.AddCardToDeck (Phase 10.6.A T6)

これにより BuyCard 経由でも OnCardAddedToDeck relic trigger が発火する。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: RewardApplier.PickCard を RunDeckActions.AddCardToDeck に差し替え

**Files:**
- Modify: `src/Core/Rewards/RewardApplier.cs:42-55` (`PickCard`)
- Test: `tests/Core.Tests/Rewards/RewardApplierTests.cs` (既存)

- [ ] **Step 7.1: 失敗テストを書く**

```csharp
[Fact]
public void PickCard_FiresOnCardAddedToDeckTrigger()
{
    var fake = BuildCatalogWithFakeRelic(
        relicId: "card_collector",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 3, Trigger: "OnCardAddedToDeck") });
    var s0 = MakeRewardPendingStateWithChoices(fake, choices: new[] { "strike_basic", "defend_basic", "bash_basic" }) with {
        Gold = 50,
        Relics = new System.Collections.Generic.List<string> { "card_collector" }
    };

    var s1 = RewardApplier.PickCard(s0, "strike_basic");
    // PickCard が catalog を取らない既存 API なので、catalog 引数を追加する必要がある。
    // → API breaking change: PickCard(s, cardId, catalog) に変更。callers (Server controllers) も更新。
    // (本テストでは catalog 引数を渡す)
    // var s1 = RewardApplier.PickCard(s0, "strike_basic", fake);

    Assert.Equal(53, s1.Gold);
    Assert.Contains(s1.Deck, c => c.Id == "strike_basic");
}
```

(注: 現行 `PickCard(s, cardId)` は catalog を取らないので、relic trigger を発火するには catalog を渡す必要がある。**API 変更** が必要 — 本タスク内で `PickCard(s, cardId, catalog)` に変更し、呼び出し元も全部更新する。`SkipCard` / `Proceed` 等他のメソッドは現状維持。)

- [ ] **Step 7.2: テスト失敗確認**

期待: コンパイルエラー (catalog 引数が無い)。

- [ ] **Step 7.3: 実装**

`RewardApplier.PickCard` を変更:

```csharp
public static RunState PickCard(RunState s, string cardId, DataCatalog catalog)
{
    var r = Require(s);
    if (r.CardStatus == CardRewardStatus.Claimed)
        throw new InvalidOperationException("Card already claimed");
    if (!r.CardChoices.Contains(cardId))
        throw new ArgumentException($"cardId \"{cardId}\" is not in CardChoices", nameof(cardId));

    var s1 = s with
    {
        ActiveReward = r with { CardStatus = CardRewardStatus.Claimed },
    };
    return Run.RunDeckActions.AddCardToDeck(s1, cardId, catalog);
}
```

呼び出し元を grep で見つけて更新:

```bash
grep -rn "RewardApplier.PickCard" src/
```

期待ヒット: Server controllers (BattleController / RunsController など)。各呼び出しに `catalog` 引数を追加。

- [ ] **Step 7.4: テスト合格確認 + Server.Tests 含めて全合格**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardApplierTests"
dotnet test tests/Server.Tests/Server.Tests.csproj
```

期待: PASS (Server 側もシグネチャ変更を反映済)。

- [ ] **Step 7.5: Commit**

```bash
git add src/Core/Rewards/RewardApplier.cs tests/Core.Tests/Rewards/RewardApplierTests.cs src/Server/Controllers/
git commit -m "$(cat <<'EOF'
refactor(rewards): route PickCard through RunDeckActions, add catalog param (Phase 10.6.A T7)

RewardApplier.PickCard(s, cardId) → PickCard(s, cardId, catalog) に変更。
これにより PickCard 経由でも OnCardAddedToDeck relic trigger が発火する。
呼び出し元の Server controllers も catalog 引数を渡すように更新。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: 5 つの reward 生成サイトで OnRewardGenerated 発火

**Files:**
- Modify:
  - `src/Core/Run/NodeEffectResolver.cs:61-66` (`StartTreasure`)
  - `src/Core/Run/BossRewardFlow.cs:20`
  - `src/Core/Events/EventResolver.cs:79`
  - `src/Server/Controllers/BattleController.cs:332`
  - `src/Server/Controllers/RunsController.cs:168`
- Test:
  - `tests/Core.Tests/Run/NodeEffectResolverTests.cs` (Treasure ケース追加)
  - `tests/Core.Tests/Run/BossRewardFlowTests.cs`
  - `tests/Core.Tests/Events/EventResolverTests.cs`
  - (Server.Tests は任意。Core 経由で十分カバーできるなら省略)

- [ ] **Step 8.1: 失敗テストを書く (Treasure 経由)**

`NodeEffectResolverTests.cs` 末尾に追記:

```csharp
[Fact]
public void Resolve_TileKindTreasure_FiresOnRewardGeneratedTrigger()
{
    var fake = BuildCatalogWithFakeRelic(
        id: "lucky",
        effects: new[] { new CardEffect(
            "gainGold", EffectScope.Self, null, 11, Trigger: "OnRewardGenerated") });
    var s0 = MakeBaseState(fake) with {
        Gold = 100,
        Relics = new System.Collections.Generic.List<string> { "lucky" }
    };
    var rng = new FakeRng(new int[20], System.Array.Empty<double>());

    var s1 = NodeEffectResolver.Resolve(s0, TileKind.Treasure, currentRow: 5, fake, rng);

    Assert.NotNull(s1.ActiveReward);
    Assert.Equal(111, s1.Gold); // 100 + 11
}
```

同様に BossRewardFlowTests / EventResolverTests に 1 ケースずつ追加。

- [ ] **Step 8.2: テスト失敗確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~Resolve_TileKindTreasure_FiresOnRewardGeneratedTrigger|FullyQualifiedName~BossRewardFlowTests|FullyQualifiedName~EventResolverTests"
```

期待: 新規ケース全 FAIL。

- [ ] **Step 8.3: 実装 (Core 3 サイト)**

**a) `NodeEffectResolver.StartTreasure`:**

```csharp
private static RunState StartTreasure(RunState s, RewardTable table, DataCatalog data, IRng rng)
{
    var owned = ImmutableArray.CreateRange(s.Relics);
    var (reward, newRng) = RewardGenerator.GenerateTreasure(s.RewardRngState, owned, table, data, rng);
    var s1 = s with { ActiveReward = reward, RewardRngState = newRng };
    return Relics.NonBattleRelicEffects.ApplyOnRewardGenerated(s1, data);
}
```

**b) `BossRewardFlow.cs`:** `RewardGenerator.Generate(...)` を呼び出した直後の `state with { ActiveReward = reward, ... }` の後に `NonBattleRelicEffects.ApplyOnRewardGenerated(s1, data)` を挟む。

**c) `EventResolver.cs:79`:** 同様。

**d) Server controllers (`BattleController.cs:332`, `RunsController.cs:168`):** RewardGenerator 呼び出し直後、ActiveReward 代入後に `NonBattleRelicEffects.ApplyOnRewardGenerated(s, catalog)` を呼ぶ。controller 側で `using RoguelikeCardGame.Core.Relics;` 追加。

- [ ] **Step 8.4: テスト合格確認 + 全テスト**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj
dotnet test tests/Server.Tests/Server.Tests.csproj
```

期待: 全 PASS。

- [ ] **Step 8.5: Client テスト・型チェック**

```bash
cd src/Client && npm run test:run && npx tsc --noEmit
```

期待: PASS (本フェーズで Client 側の変更は無いはずだが regression 確認)。

- [ ] **Step 8.6: Commit**

```bash
git add src/Core/Run/NodeEffectResolver.cs src/Core/Run/BossRewardFlow.cs src/Core/Events/EventResolver.cs src/Server/Controllers/BattleController.cs src/Server/Controllers/RunsController.cs tests/Core.Tests/Run/NodeEffectResolverTests.cs tests/Core.Tests/Run/BossRewardFlowTests.cs tests/Core.Tests/Events/EventResolverTests.cs
git commit -m "$(cat <<'EOF'
feat(rewards): fire OnRewardGenerated relic trigger at all 5 reward generation sites (Phase 10.6.A T8)

Treasure / BossReward / Event / 戦闘終了 / Server runs endpoint で reward 確定直後に発火。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: 統合テスト + push

**Files:** なし (検証 + push のみ)

- [ ] **Step 9.1: 全テスト走らせて緑確認**

```bash
dotnet test
cd src/Client && npm run test:run && npx tsc --noEmit
```

期待:
- Core: 1156 + 新規 ~17 件 = ~1173 件 PASS
- Server: 既存 PASS (RewardApplier.PickCard シグネチャ変更を反映済)
- Client: 172 件 PASS、tsc クリーン

- [ ] **Step 9.2: 動作確認 (任意、推奨)**

`dotnet run --project src/Server` + `cd src/Client && npm run dev` を起動し、dev menu で fake relic を 1 つ作って (例: gainGold +5 / Trigger=OnEnterShop)、実プレイで「ショップに入った瞬間に Gold +5」を目視確認。

- [ ] **Step 9.3: Push**

```bash
git push origin master
```

- [ ] **Step 9.4: memory 更新**

`C:\Users\Metaverse\.claude\projects\c--Users-Metaverse-projects-roguelike-cardgame\memory\project_phase_status.md` を「Phase 10.6.A 完了」に更新。次フェーズ候補から 10.6.A を削除し、10.6.B / 10.5.L2 / M2-Choose を残す。

---

## Out of Scope (本フェーズ非対応、TODO 残し)

- `OnMapTileResolved` の発火点ハーネス: 既存 `NonBattleRelicEffects.ApplyOnMapTileResolved` は実装済だが engine 側でどこからも呼ばれていない。マスを resolved にする処理を grep で探して 1 箇所追加すべきだが、本フェーズの 5 trigger スコープ外。Phase 10.6.A の終了後に別 issue として記録 (memory に追加)。
- Effect action 拡張 (`addCardToReward` / `gainPotion` / `extraRewardCard` 等): Q3 で「OnRewardGenerated は通知のみ」と確定済。改変系は Phase 10.6.B (modifier system) で扱う。
- Battle 中の deck 直追加: 通常はバトル中に deck を直接いじらないが、もしそういう relic/effect が今後追加された場合は `RunDeckActions.AddCardToDeck` を必ず経由させること (CardEffect の枠ではなく engine 側のフロー設計事項)。
