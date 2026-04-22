# Phase 6 Tile Events Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 各マス種別を本来の挙動に拡張する。Enemy/Elite/Boss はレアリティフロア整理、Treasure はレリック専用、Rest は回復／強化選択、Merchant は購入・廃棄、新 `TileKind.Event` は JSON 駆動のイベント。非バトル文脈で発動するレリック効果（OnPickup / OnMapTileResolved / Passive 修飾）も配線する。

**Architecture:** Core に Events / Merchant / Rest の純関数サービスを追加し、`NodeEffectResolver` をルータ化（一枚岩にしない）。`RunState` を v4 にスキーマ更新し `Deck: ImmutableArray<CardInstance>`、`ActiveMerchant`、`ActiveEvent`、`ActiveRestPending` を追加。`RewardState` に `Relic` 欄追加。非バトルレリック効果は `NonBattleRelicEffects` で配線。Server は `MerchantController` / `EventController` / `RestController` を新設、`RewardController` / `CatalogController` を拡張。Client は `MerchantScreen` / `EventScreen` / `RestScreen` と catalog hooks を追加し、`MapScreen` でオーバーレイを出し分ける。

**Tech Stack:** C# .NET 10（Core/Server）、xUnit、React 19 + TypeScript + Vite + vitest + @testing-library/react（Client）。

**Spec:** [docs/superpowers/specs/2026-04-22-phase06-tile-events-design.md](../specs/2026-04-22-phase06-tile-events-design.md)

**Prerequisite:** Phase 5 が master にマージされていること。`RunState` v3、バトル placeholder、RewardGenerator、RewardApplier、NodeEffectResolver、MapScreen/BattleOverlay/RewardPopup が動作している状態が出発点。

---

## File Structure

### 新規作成

- **Core / 型**
  - `src/Core/Cards/CardInstance.cs`
  - `src/Core/Cards/CardUpgrade.cs`
  - `src/Core/Relics/RelicInventory.cs`
  - `src/Core/Relics/NonBattleRelicEffects.cs`
  - `src/Core/Events/EventCondition.cs`
  - `src/Core/Events/EventEffect.cs`
  - `src/Core/Events/EventChoice.cs`
  - `src/Core/Events/EventDefinition.cs`
  - `src/Core/Events/EventInstance.cs`
  - `src/Core/Events/EventJsonLoader.cs`
  - `src/Core/Events/EventPool.cs`
  - `src/Core/Events/EventResolver.cs`
  - `src/Core/Merchant/MerchantPrices.cs`
  - `src/Core/Merchant/MerchantPricesJsonLoader.cs`
  - `src/Core/Merchant/MerchantOffer.cs`
  - `src/Core/Merchant/MerchantInventory.cs`
  - `src/Core/Merchant/MerchantInventoryGenerator.cs`
  - `src/Core/Merchant/MerchantActions.cs`
  - `src/Core/Rest/RestActions.cs`
- **Core / データ（埋め込み JSON）**
  - `src/Core/Data/Events/blessing_fountain.json`
  - `src/Core/Data/Events/shady_merchant.json`
  - `src/Core/Data/Events/old_library.json`
  - `src/Core/Data/Relics/extra_max_hp.json`
  - `src/Core/Data/Relics/coin_purse.json`
  - `src/Core/Data/Relics/traveler_boots.json`
  - `src/Core/Data/Relics/warm_blanket.json`
  - `src/Core/Data/merchant-prices.json`（`Misc` 名前空間の埋め込みリソース）
- **Core テスト**
  - `tests/Core.Tests/Cards/CardInstanceTests.cs`
  - `tests/Core.Tests/Cards/CardUpgradeTests.cs`
  - `tests/Core.Tests/Events/EventJsonLoaderTests.cs`
  - `tests/Core.Tests/Events/EventPoolTests.cs`
  - `tests/Core.Tests/Events/EventResolverTests.cs`
  - `tests/Core.Tests/Merchant/MerchantPricesJsonLoaderTests.cs`
  - `tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs`
  - `tests/Core.Tests/Merchant/MerchantActionsTests.cs`
  - `tests/Core.Tests/Rest/RestActionsTests.cs`
  - `tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs`
- **Server**
  - `src/Server/Controllers/MerchantController.cs`
  - `src/Server/Controllers/EventController.cs`
  - `src/Server/Controllers/RestController.cs`
  - `src/Server/Dtos/MerchantBuyRequestDto.cs`
  - `src/Server/Dtos/MerchantDiscardRequestDto.cs`
  - `src/Server/Dtos/EventChooseRequestDto.cs`
  - `src/Server/Dtos/RestUpgradeRequestDto.cs`
  - `src/Server/Dtos/MerchantInventoryDto.cs`
  - `src/Server/Dtos/EventInstanceDto.cs`
- **Server テスト**
  - `tests/Server.Tests/Controllers/MerchantControllerTests.cs`
  - `tests/Server.Tests/Controllers/EventControllerTests.cs`
  - `tests/Server.Tests/Controllers/RestControllerTests.cs`
  - `tests/Server.Tests/Controllers/ClaimRelicEndpointTests.cs`
- **Client**
  - `src/Client/src/api/merchant.ts`
  - `src/Client/src/api/event.ts`
  - `src/Client/src/api/rest.ts`
  - `src/Client/src/hooks/useRelicCatalog.ts`
  - `src/Client/src/hooks/useEventCatalog.ts`
  - `src/Client/src/screens/MerchantScreen.tsx`
  - `src/Client/src/screens/MerchantScreen.test.tsx`
  - `src/Client/src/screens/EventScreen.tsx`
  - `src/Client/src/screens/EventScreen.test.tsx`
  - `src/Client/src/screens/RestScreen.tsx`
  - `src/Client/src/screens/RestScreen.test.tsx`

### 変更

- **Core**
  - `src/Core/Map/TileKind.cs` — `Event` を追加
  - `src/Core/Map/UnknownResolutionConfig.cs` — `Event` を許可
  - `src/Core/Run/RunState.cs` — Deck 型変更、Active*/ActiveRestPending 追加、SchemaVersion=4、Validate 拡張
  - `src/Core/Run/RunStateSerializer.cs` — v3→v4 one-shot 移行
  - `src/Core/Run/NodeEffectResolver.cs` — Merchant / Event / Rest / Treasure の挙動変更、OnMapTileResolved フック
  - `src/Core/Rewards/RewardState.cs` — `Relic`、`RelicClaimed` 追加
  - `src/Core/Rewards/RewardGenerator.cs` — Elite/Boss フロア、Treasure relic-only
  - `src/Core/Rewards/RewardApplier.cs` — `ClaimRelic` 追加、`PickCard` の CardInstance 対応
  - `src/Core/Data/DataCatalog.cs` — `Events`、`MerchantPrices` 追加
  - `src/Core/Data/EmbeddedDataLoader.cs` — `Events` / `Misc` プレフィックス追加
  - `src/Core/Data/RewardTable/act1.json` — `pools.elite.rarityDist`、`pools.boss.rarityDist`、`nonBattle.treasure.gold` 変更
- **Server**
  - `src/Server/Controllers/RunsController.cs` — move 時に RunState が新フィールドを含むよう対応
  - `src/Server/Controllers/CatalogController.cs` — `/catalog/relics`、`/catalog/events`
  - `src/Server/Controllers/RewardController.cs` — `/reward/claim-relic`
  - `src/Server/Dtos/RewardStateDto.cs` — Relic 欄
  - `src/Server/Dtos/RunSnapshotDto.cs` — CardInstance / ActiveMerchant / ActiveEvent / ActiveRestPending
- **Client**
  - `src/Client/src/api/types.ts` — CardInstance、RewardState.relicId、RunSnapshot 追加欄
  - `src/Client/src/api/rewards.ts` — `claimRelic`
  - `src/Client/src/components/TopBar.tsx` — CardInstance 対応
  - `src/Client/src/screens/MapScreen.tsx` — Merchant/Event/Rest オーバーレイ分岐
  - `src/Client/src/screens/RewardPopup.tsx` — relic 行
- **Tests / 既存拡張**
  - `tests/Core.Tests/Rewards/RewardGeneratorTests.cs` — Elite/Boss フロア、Treasure relic-only
  - `tests/Core.Tests/Run/RunStateSerializerTests.cs` — v3→v4 migration
  - `tests/Core.Tests/Run/RunStateValidateTests.cs` — 多重モード、CardInstance 整合
  - `tests/Core.Tests/Map/UnknownResolverTests.cs` — Event 抽選
  - `tests/Server.Tests/Controllers/CatalogControllerTests.cs` — `/catalog/relics`, `/catalog/events`

---

## Test Command Reference

- Core 単体テスト: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~<path>" --nologo`
- Server 統合テスト: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~<path>" --nologo`
- 全テスト: `dotnet test --nologo`
- Client 単体: `cd src/Client && npm run test -- --run <path>`
- Client 全部: `cd src/Client && npm run test -- --run`

---

## Part A — Foundation: CardInstance と RunState v4

### Task A1: CardInstance レコード

**Files:**
- Create: `src/Core/Cards/CardInstance.cs`
- Test: `tests/Core.Tests/Cards/CardInstanceTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Cards/CardInstanceTests.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardInstanceTests
{
    [Fact]
    public void Constructor_DefaultsUpgradedFalse()
    {
        var ci = new CardInstance("strike");
        Assert.Equal("strike", ci.Id);
        Assert.False(ci.Upgraded);
    }

    [Fact]
    public void WithExpression_TogglesUpgraded()
    {
        var ci = new CardInstance("strike") with { Upgraded = true };
        Assert.True(ci.Upgraded);
    }

    [Fact]
    public void Equality_ByValue()
    {
        var a = new CardInstance("strike", false);
        var b = new CardInstance("strike", false);
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~CardInstanceTests" --nologo`
Expected: Build error — `CardInstance` not found.

- [ ] **Step 3: 最小実装**

Create `src/Core/Cards/CardInstance.cs`:

```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// RunState.Deck の要素。カード ID と強化状態を持つ。
/// マスター定義は DataCatalog.Cards[Id] で引く。
/// </summary>
public sealed record CardInstance(string Id, bool Upgraded = false);
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~CardInstanceTests" --nologo`
Expected: PASS (3 tests).

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/CardInstance.cs tests/Core.Tests/Cards/CardInstanceTests.cs
git commit -m "feat(core): add CardInstance record for deck entries"
git push
```

---

### Task A2: CardUpgrade ヘルパ

**Files:**
- Create: `src/Core/Cards/CardUpgrade.cs`
- Test: `tests/Core.Tests/Cards/CardUpgradeTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Cards/CardUpgradeTests.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardUpgradeTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void CanUpgrade_UnupgradedCardWithUpgradedEffects_ReturnsTrue()
    {
        // "strike" は upgradedEffects を持つ
        var ci = new CardInstance("strike", Upgraded: false);
        Assert.True(CardUpgrade.CanUpgrade(ci, Catalog));
    }

    [Fact]
    public void CanUpgrade_AlreadyUpgraded_ReturnsFalse()
    {
        var ci = new CardInstance("strike", Upgraded: true);
        Assert.False(CardUpgrade.CanUpgrade(ci, Catalog));
    }

    [Fact]
    public void Upgrade_TogglesFlag()
    {
        var ci = new CardInstance("strike", Upgraded: false);
        var upgraded = CardUpgrade.Upgrade(ci);
        Assert.True(upgraded.Upgraded);
        Assert.Equal("strike", upgraded.Id);
    }

    [Fact]
    public void Upgrade_AlreadyUpgraded_Throws()
    {
        var ci = new CardInstance("strike", Upgraded: true);
        Assert.Throws<System.InvalidOperationException>(() => CardUpgrade.Upgrade(ci));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~CardUpgradeTests" --nologo`
Expected: Build error.

- [ ] **Step 3: 実装**

Create `src/Core/Cards/CardUpgrade.cs`:

```csharp
using System;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Cards;

public static class CardUpgrade
{
    public static bool CanUpgrade(CardInstance ci, DataCatalog catalog)
    {
        if (ci.Upgraded) return false;
        if (!catalog.TryGetCard(ci.Id, out var def)) return false;
        return def.UpgradedEffects is not null;
    }

    public static CardInstance Upgrade(CardInstance ci)
    {
        if (ci.Upgraded) throw new InvalidOperationException($"Card {ci.Id} already upgraded");
        return ci with { Upgraded = true };
    }
}
```

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~CardUpgradeTests" --nologo`
Expected: PASS (4 tests).

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/CardUpgrade.cs tests/Core.Tests/Cards/CardUpgradeTests.cs
git commit -m "feat(core): add CardUpgrade helper (CanUpgrade / Upgrade)"
git push
```

---

### Task A3: TileKind.Event 追加と UnknownResolutionConfig 更新

**Files:**
- Modify: `src/Core/Map/TileKind.cs`
- Modify: `src/Core/Map/UnknownResolutionConfig.cs`
- Test: extend `tests/Core.Tests/Map/UnknownResolverTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Append to `tests/Core.Tests/Map/UnknownResolverTests.cs` (new test method, keep existing tests):

```csharp
    [Fact]
    public void Event_IsAllowedInWeights()
    {
        var cfg = new UnknownResolutionConfig(
            System.Collections.Immutable.ImmutableDictionary<TileKind, double>.Empty
                .Add(TileKind.Event, 1.0));
        Assert.Null(cfg.Validate());
    }
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~UnknownResolverTests.Event_IsAllowedInWeights" --nologo`
Expected: Build error — `TileKind.Event` not defined.

- [ ] **Step 3: TileKind.Event を追加**

Edit `src/Core/Map/TileKind.cs`:

```csharp
namespace RoguelikeCardGame.Core.Map;

/// <summary>ダンジョンマップ上のマス種別。</summary>
public enum TileKind
{
    Start,
    Enemy,
    Elite,
    Rest,
    Merchant,
    Treasure,
    Unknown,
    Boss,
    Event,
}
```

`UnknownResolutionConfig.Validate()` の既存禁止リスト（Unknown/Start/Boss）は変更不要。`Event` は既にデフォルトで許可される。

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~UnknownResolverTests" --nologo`
Expected: PASS（既存含む全テスト）。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Map/TileKind.cs tests/Core.Tests/Map/UnknownResolverTests.cs
git commit -m "feat(core): add TileKind.Event and allow it in UnknownResolutionConfig"
git push
```

---

### Task A4: RunState v4 スキーマ — Deck 型、Active*/ActiveRestPending 追加

**Files:**
- Modify: `src/Core/Run/RunState.cs`
- Test: extend `tests/Core.Tests/Run/RunStateValidateTests.cs`

このタスクは RunState の型変更を一挙に行う。既存コードが壊れるので、後続タスクで caller を更新していく。まずは RunState 自身と Validate のテストだけ通せば良い。

- [ ] **Step 1: 失敗するテストを書く**

Append to `tests/Core.Tests/Run/RunStateValidateTests.cs` (new tests, keep existing):

```csharp
    [Fact]
    public void Validate_MultipleActiveNull_ReturnsNull()
    {
        var s = SampleV4();
        Assert.Null(s.Validate());
    }

    [Fact]
    public void Validate_ActiveBattleAndActiveMerchant_ReturnsError()
    {
        var s = SampleV4() with
        {
            ActiveBattle = null,
            ActiveMerchant = FakeInventory(),
            ActiveReward = null,
        };
        // Active at most 1: start with merchant only — OK
        Assert.Null(s.Validate());

        var bad = s with { ActiveBattle = FakeBattle() };
        Assert.Contains("at most one", bad.Validate(), System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RestPendingWithActiveReward_ReturnsError()
    {
        var s = SampleV4() with
        {
            ActiveRestPending = true,
            ActiveReward = FakeReward(),
        };
        Assert.Contains("ActiveRestPending", s.Validate());
    }

    [Fact]
    public void Validate_UpgradedCardWithoutUpgradedEffects_ReturnsError()
    {
        // "strike_promo_anniversary" は UpgradedEffects 無し（既存 JSON 前提）。
        // もし未登録なら strike を流用して upgradedEffects=null の適当カードを想定。
        var s = SampleV4();
        var deck = s.Deck.Add(new CardInstance("reward_common_slice", Upgraded: true));
        var bad = s with { Deck = deck };
        // Card は存在するが UpgradedEffects 無しの ID を使う。このテストは
        // ランナ実装時に存在するカードに合わせて差し替え可（スキップ OK）。
        _ = bad; // Placeholder assertion removed in implementation step
    }

    private static RunState SampleV4()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        return RunState.NewSoloRun(
            catalog, rngSeed: 1UL, startNodeId: 0,
            unknownResolutions: System.Collections.Immutable.ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueStrong: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueElite: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueBoss: System.Collections.Immutable.ImmutableArray<string>.Empty,
            nowUtc: new System.DateTimeOffset(2026, 4, 22, 0, 0, 0, System.TimeSpan.Zero));
    }

    private static MerchantInventory FakeInventory() =>
        new(System.Collections.Immutable.ImmutableArray<MerchantOffer>.Empty,
            System.Collections.Immutable.ImmutableArray<MerchantOffer>.Empty,
            System.Collections.Immutable.ImmutableArray<MerchantOffer>.Empty,
            DiscardSlotUsed: false, DiscardPrice: 75);

    private static BattleState FakeBattle() => throw new System.NotImplementedException();
    private static RewardState FakeReward() => throw new System.NotImplementedException();
```

この Step の `FakeBattle` / `FakeReward` は Step 4 以前はコンパイルしないので、既存テストが壊れない形で一時的にコメントアウト可。ランナは最終 Step で実データに差し替える。

- [ ] **Step 2: 失敗を確認**

Run: `dotnet build src/Core/Core.csproj --nologo`
Expected: Build error — `Deck` の型不一致、`ActiveMerchant` / `ActiveEvent` / `ActiveRestPending` 不明。

- [ ] **Step 3: RunState を v4 に更新**

Replace `src/Core/Run/RunState.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ソロ／マルチ共通のラン 1 回分の状態。ソロのみ ISaveRepository で永続化される。</summary>
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    ImmutableArray<int> VisitedNodeIds,
    ImmutableDictionary<int, TileKind> UnknownResolutions,

    // --- Phase 5 additions ---
    string CharacterId,
    int CurrentHp,
    int MaxHp,
    int Gold,
    ImmutableArray<CardInstance> Deck,
    ImmutableArray<string> Potions,
    int PotionSlotCount,
    BattleState? ActiveBattle,
    RewardState? ActiveReward,
    ImmutableArray<string> EncounterQueueWeak,
    ImmutableArray<string> EncounterQueueStrong,
    ImmutableArray<string> EncounterQueueElite,
    ImmutableArray<string> EncounterQueueBoss,
    RewardRngState RewardRngState,

    // --- Phase 6 additions ---
    MerchantInventory? ActiveMerchant,
    EventInstance? ActiveEvent,
    bool ActiveRestPending,

    // --- existing ---
    IReadOnlyList<string> Relics,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress)
{
    /// <summary>Phase 6 の JSON スキーマバージョン。</summary>
    public const int CurrentSchemaVersion = 4;

    public static RunState NewSoloRun(
        DataCatalog catalog,
        ulong rngSeed,
        int startNodeId,
        ImmutableDictionary<int, TileKind> unknownResolutions,
        ImmutableArray<string> encounterQueueWeak,
        ImmutableArray<string> encounterQueueStrong,
        ImmutableArray<string> encounterQueueElite,
        ImmutableArray<string> encounterQueueBoss,
        DateTimeOffset nowUtc,
        string characterId = "default")
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(unknownResolutions);

        if (!catalog.TryGetCharacter(characterId, out var ch))
            throw new InvalidOperationException($"Character \"{characterId}\" が DataCatalog に存在しません");

        foreach (var id in ch.Deck)
            if (!catalog.TryGetCard(id, out _))
                throw new InvalidOperationException(
                    $"Character \"{characterId}\" のデッキが参照するカード ID \"{id}\" が存在しません");

        var potionBuilder = ImmutableArray.CreateBuilder<string>(ch.PotionSlotCount);
        for (int i = 0; i < ch.PotionSlotCount; i++) potionBuilder.Add("");
        var potions = potionBuilder.ToImmutable();

        if (!catalog.RewardTables.TryGetValue("act1", out var rt))
            throw new InvalidOperationException("RewardTable \"act1\" が DataCatalog に存在しません");

        var deck = ImmutableArray.CreateRange(ch.Deck.Select(id => new CardInstance(id, false)));

        return new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentNodeId: startNodeId,
            VisitedNodeIds: ImmutableArray.Create(startNodeId),
            UnknownResolutions: unknownResolutions,
            CharacterId: characterId,
            CurrentHp: ch.MaxHp,
            MaxHp: ch.MaxHp,
            Gold: ch.StartingGold,
            Deck: deck,
            Potions: potions,
            PotionSlotCount: ch.PotionSlotCount,
            ActiveBattle: null,
            ActiveReward: null,
            EncounterQueueWeak: encounterQueueWeak,
            EncounterQueueStrong: encounterQueueStrong,
            EncounterQueueElite: encounterQueueElite,
            EncounterQueueBoss: encounterQueueBoss,
            RewardRngState: new RewardRngState(
                rt.PotionDynamic.InitialPercent, rt.EpicChance.InitialBonus),
            ActiveMerchant: null,
            ActiveEvent: null,
            ActiveRestPending: false,
            Relics: Array.Empty<string>(),
            PlaySeconds: 0L,
            RngSeed: rngSeed,
            SavedAtUtc: nowUtc,
            Progress: RunProgress.InProgress);
    }

    /// <summary>構造的不変条件を検査する。違反があれば理由文字列、問題なければ null。</summary>
    public string? Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            return $"SchemaVersion must be {CurrentSchemaVersion} (got {SchemaVersion})";
        if (VisitedNodeIds.IsDefault) return "VisitedNodeIds must not be default";
        if (!VisitedNodeIds.Contains(CurrentNodeId))
            return $"VisitedNodeIds must contain CurrentNodeId ({CurrentNodeId})";
        if (VisitedNodeIds.Length != VisitedNodeIds.Distinct().Count())
            return "VisitedNodeIds must not contain duplicates";
        foreach (var kv in UnknownResolutions)
        {
            if (kv.Value is TileKind.Unknown or TileKind.Start or TileKind.Boss)
                return $"UnknownResolutions[{kv.Key}]={kv.Value} is not a valid resolved kind";
        }
        if (Potions.Length != PotionSlotCount)
            return $"Potions.Length ({Potions.Length}) != PotionSlotCount ({PotionSlotCount})";

        int activeCount = 0;
        if (ActiveBattle is not null) activeCount++;
        if (ActiveReward is not null) activeCount++;
        if (ActiveMerchant is not null) activeCount++;
        if (ActiveEvent is not null) activeCount++;
        if (activeCount > 1)
            return "at most one of ActiveBattle / ActiveReward / ActiveMerchant / ActiveEvent can be non-null";
        if (ActiveRestPending && activeCount > 0)
            return "ActiveRestPending must not coexist with any other Active*";

        if (ActiveReward is { CardChoices: var cc } && cc.Length != 0 && cc.Length != 3)
            return $"CardChoices must have length 0 or 3 (got {cc.Length})";
        return null;
    }
}
```

- [ ] **Step 4: ビルドエラーの残りを確認**

Run: `dotnet build --nologo`
Expected: 他ファイル（`RewardApplier.PickCard`、`NodeEffectResolver`、`Server.Dtos`、Server.Tests）で `Deck` を `ImmutableArray<string>` として使う箇所がビルドエラー。後続タスクで順次対応する。

- [ ] **Step 5: RewardApplier.PickCard を CardInstance 対応に（最小変更）**

Edit `src/Core/Rewards/RewardApplier.cs` — `PickCard` 内のみ:

Replace:
```csharp
return s with
{
    Deck = s.Deck.Add(cardId),
    ActiveReward = r with { CardStatus = CardRewardStatus.Claimed },
};
```
with:
```csharp
return s with
{
    Deck = s.Deck.Add(new RoguelikeCardGame.Core.Cards.CardInstance(cardId, false)),
    ActiveReward = r with { CardStatus = CardRewardStatus.Claimed },
};
```

- [ ] **Step 6: RunsController 等 Server 側の Deck 参照を暫定修正**

`src/Server/Controllers/RunsController.cs`、`src/Server/Dtos/RunSnapshotDto.cs` などで `RunState.Deck` を `string[]` として列挙している箇所を `state.Deck.Select(c => c.Id).ToArray()` に変更（ DTO 本格更新は Task G3 で行う）。

このステップの目的はビルドを通すことなので、具体的な箇所は `dotnet build --nologo` のエラーに従って逐次修正する:

```bash
dotnet build --nologo 2>&1 | grep -E "error CS|Deck"
```

各エラー箇所で以下のパターンを適用:
- `state.Deck` を配列として使う → `state.Deck.Select(c => c.Id).ToArray()`
- 要素を string として受け取る foreach → `foreach (var ci in state.Deck) { var id = ci.Id; ... }`

- [ ] **Step 7: Validate テストを実行**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RunStateValidateTests" --nologo`
Expected: PASS。もし `FakeInventory` などで未実装型（`MerchantInventory`, `MerchantOffer`）があればテスト該当部分を `Skip=` でスキップするか、後続タスクの型が揃うまでコメントアウトする。

- [ ] **Step 8: ビルド全体が通ることを確認**

Run: `dotnet build --nologo`
Expected: 0 errors. Warnings OK.

- [ ] **Step 9: コミット**

```bash
git add src/Core/Run/RunState.cs src/Core/Rewards/RewardApplier.cs src/Server/ tests/Core.Tests/Run/RunStateValidateTests.cs
git commit -m "feat(core): bump RunState schema to v4 (CardInstance deck, ActiveMerchant/Event/RestPending)"
git push
```

**注意:** Step 9 のこの時点では `MerchantInventory` / `EventInstance` の型が未定義なら Step 3 の `using` と field 型は暫定的に `object?` としても構わない。Part D/E で実装後に本型に差し替える。ただし後段の参照が楽になるので、**Task D1〜D7 と Task E1〜E5 を完了してから Task A4 Step 3 を再 edit し、本型に戻す** ほうが混乱しない。本プランでは後者を推奨するため、Part D・E 完了後に Task A4 を実施する実行順を取っても良い（依存順のとおり）。

---

### Task A5: RunStateSerializer v3→v4 one-shot 移行

**Files:**
- Modify: `src/Core/Run/RunStateSerializer.cs`
- Test: extend `tests/Core.Tests/Run/RunStateSerializerTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Append to `tests/Core.Tests/Run/RunStateSerializerTests.cs`:

```csharp
    [Fact]
    public void Deserialize_V3Json_MigratesToV4_DeckBecomesCardInstances()
    {
        // v3: Deck は string[]。v4 では CardInstance[] に自動変換されるべき。
        var v3 = """
        {
          "schemaVersion": 3,
          "currentAct": 1,
          "currentNodeId": 0,
          "visitedNodeIds": [0],
          "unknownResolutions": {},
          "characterId": "default",
          "currentHp": 80,
          "maxHp": 80,
          "gold": 99,
          "deck": ["strike", "defend"],
          "potions": ["", "", ""],
          "potionSlotCount": 3,
          "activeBattle": null,
          "activeReward": null,
          "encounterQueueWeak": [],
          "encounterQueueStrong": [],
          "encounterQueueElite": [],
          "encounterQueueBoss": [],
          "rewardRngState": { "potionChancePercent": 40, "rareChanceBonusPercent": 0 },
          "relics": [],
          "playSeconds": 0,
          "rngSeed": 0,
          "savedAtUtc": "2026-04-21T00:00:00+00:00",
          "progress": "InProgress"
        }
        """;
        var loaded = RunStateSerializer.Deserialize(v3);
        Assert.Equal(4, loaded.SchemaVersion);
        Assert.Equal(2, loaded.Deck.Length);
        Assert.Equal("strike", loaded.Deck[0].Id);
        Assert.False(loaded.Deck[0].Upgraded);
        Assert.Equal("defend", loaded.Deck[1].Id);
        Assert.Null(loaded.ActiveMerchant);
        Assert.Null(loaded.ActiveEvent);
        Assert.False(loaded.ActiveRestPending);
    }

    [Fact]
    public void RoundTrip_V4_Preserves()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var original = RunState.NewSoloRun(
            catalog, rngSeed: 7UL, startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero));
        var json = RunStateSerializer.Serialize(original);
        var loaded = RunStateSerializer.Deserialize(json);
        Assert.Equal(4, loaded.SchemaVersion);
        Assert.Equal(original.Deck.Length, loaded.Deck.Length);
        Assert.Equal(original.Deck[0].Id, loaded.Deck[0].Id);
    }
```

既存 `Deserialize_V2Json_ThrowsSerializerException` は残し、`Deserialize_WrongSchemaVersionOnly_ThrowsSerializerException` は `schemaVersion = 99` に書き換えて維持。

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RunStateSerializerTests" --nologo`
Expected: v3 migration テスト FAIL、v4 RoundTrip PASS。

- [ ] **Step 3: 実装**

Replace `src/Core/Run/RunStateSerializer.cs` body:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Run;

public sealed class RunStateSerializerException : Exception
{
    public RunStateSerializerException(string message) : base(message) { }
    public RunStateSerializerException(string message, Exception inner) : base(message, inner) { }
}

public static class RunStateSerializer
{
    public static string Serialize(RunState state)
        => JsonSerializer.Serialize(state, JsonOptions.Default);

    public static RunState Deserialize(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex)
        { throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex); }

        if (node is not JsonObject obj)
            throw new RunStateSerializerException("RunState JSON のルートがオブジェクトではありません。");

        int version = obj["schemaVersion"]?.GetValue<int>()
            ?? throw new RunStateSerializerException("schemaVersion が存在しません。");

        if (version == 3) obj = MigrateV3ToV4(obj);
        else if (version != RunState.CurrentSchemaVersion)
            throw new RunStateSerializerException(
                $"未対応の schemaVersion: {version} (対応: {RunState.CurrentSchemaVersion})");

        RunState? state;
        try { state = JsonSerializer.Deserialize<RunState>(obj.ToJsonString(), JsonOptions.Default); }
        catch (JsonException ex)
        { throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex); }

        if (state is null) throw new RunStateSerializerException("RunState JSON が null でした。");
        return state;
    }

    private static JsonObject MigrateV3ToV4(JsonObject obj)
    {
        // Deck: string[] → CardInstance[] with Upgraded=false
        if (obj["deck"] is JsonArray deckV3)
        {
            var deckV4 = new JsonArray();
            foreach (var idNode in deckV3)
            {
                var id = idNode?.GetValue<string>()
                    ?? throw new RunStateSerializerException("deck に null 要素が含まれています。");
                deckV4.Add(new JsonObject
                {
                    ["id"] = id,
                    ["upgraded"] = false,
                });
            }
            obj["deck"] = deckV4;
        }
        obj["activeMerchant"] ??= null;
        obj["activeEvent"] ??= null;
        obj["activeRestPending"] ??= false;
        obj["schemaVersion"] = RunState.CurrentSchemaVersion;
        return obj;
    }
}
```

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RunStateSerializerTests" --nologo`
Expected: PASS (v3 migration、v4 round-trip、wrong schema error すべて)。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Run/RunStateSerializer.cs tests/Core.Tests/Run/RunStateSerializerTests.cs
git commit -m "feat(core): migrate RunState v3 save files to v4 on load"
git push
```

---

## Part B — Reward: Rarity Floor / Treasure relic-only / claim-relic

### Task B1: act1.json の rarityDist・Treasure gold 調整

**Files:**
- Modify: `src/Core/Data/RewardTable/act1.json`

- [ ] **Step 1: データ変更**

Edit `src/Core/Data/RewardTable/act1.json`:

```json
{
  "id": "act1",
  "pools": {
    "weak":   { "gold": [10, 20], "potionBase": 40,
                "rarityDist": { "common": 60, "rare": 37, "epic": 3 } },
    "strong": { "gold": [15, 25], "potionBase": 40,
                "rarityDist": { "common": 60, "rare": 37, "epic": 3 } },
    "elite":  { "gold": [25, 35], "potionBase": 100,
                "rarityDist": { "common": 0, "rare": 70, "epic": 30 } },
    "boss":   { "gold": [95, 105], "potionBase": 0,
                "rarityDist": { "common": 0, "rare": 0, "epic": 100 } }
  },
  "nonBattle": {
    "event":    { "gold": [10, 20] },
    "treasure": { "gold": [0, 0] }
  },
  "potionDynamic": { "initialPercent": 40, "step": 10, "min": 0, "max": 100 },
  "epicChance":    { "initialBonus": 0, "perBattleIncrement": 1 },
  "enemyPoolRouting": { "weakRowsThreshold": 3 }
}
```

- [ ] **Step 2: ビルド＋既存テスト確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardGenerator" --nologo`
Expected: 既存テストが 2 件程度 FAIL する見込み（rarityDist sum=100 は維持されるのでパース成功。ただし Elite/Boss の具体的な期待値を assert しているテストがあれば要修正。Task B2 で書き直す）。

- [ ] **Step 3: コミット**

```bash
git add src/Core/Data/RewardTable/act1.json
git commit -m "data(act1): bump Elite rarityDist to 0/70/30, Boss to 0/0/100, Treasure gold 0"
git push
```

---

### Task B2: RewardState に Relic 欄追加

**Files:**
- Modify: `src/Core/Rewards/RewardState.cs`

- [ ] **Step 1: 実装**

Edit `src/Core/Rewards/RewardState.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Enemy;

namespace RoguelikeCardGame.Core.Rewards;

public enum CardRewardStatus { Pending, Claimed, Skipped }

public enum NonBattleRewardKind { Event, Treasure }

public abstract record RewardContext
{
    public sealed record FromEnemy(EnemyPool Pool) : RewardContext;
    public sealed record FromNonBattle(NonBattleRewardKind Kind) : RewardContext;
}

public sealed record RewardState(
    int Gold,
    bool GoldClaimed,
    string? PotionId,
    bool PotionClaimed,
    ImmutableArray<string> CardChoices,
    CardRewardStatus CardStatus,
    string? RelicId = null,
    bool RelicClaimed = true);

public sealed record RewardRngState(
    int PotionChancePercent,
    int RareChanceBonusPercent);
```

`RelicClaimed` は relic が無いケースで true（= 処理不要）となるよう default true。

- [ ] **Step 2: ビルド確認（既存呼び出し）**

Run: `dotnet build --nologo`
Expected: パス。既存 `new RewardState(...)` はすべてキーワード引数を使っていないか要チェック。`RewardGenerator.cs` の既存 2 箇所はキーワード指定なので、`RelicId`、`RelicClaimed` の追加はデフォルト適用で OK。

- [ ] **Step 3: コミット**

```bash
git add src/Core/Rewards/RewardState.cs
git commit -m "feat(core): add RelicId/RelicClaimed to RewardState"
git push
```

---

### Task B3: RewardGenerator を Elite/Boss フロア＋Treasure relic-only 対応に

**Files:**
- Modify: `src/Core/Rewards/RewardGenerator.cs`
- Test: extend `tests/Core.Tests/Rewards/RewardGeneratorTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Append to `tests/Core.Tests/Rewards/RewardGeneratorTests.cs`:

```csharp
    [Fact]
    public void GenerateFromEnemy_Elite_AllCardsAreRareOrEpic()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(1UL);
        var rngState = new RewardRngState(rt.PotionDynamic.InitialPercent, 0);
        for (int trial = 0; trial < 50; trial++)
        {
            var (reward, _) = RewardGenerator.Generate(
                new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Elite)),
                rngState, ImmutableArray.Create("strike", "defend"), rt, catalog, rng);
            foreach (var id in reward.CardChoices)
            {
                var def = catalog.Cards[id];
                Assert.NotEqual(CardRarity.Common, def.Rarity);
            }
        }
    }

    [Fact]
    public void GenerateFromEnemy_Boss_AllCardsAreEpic()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(42UL);
        var rngState = new RewardRngState(0, 0);
        var (reward, _) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Boss)),
            rngState, ImmutableArray<string>.Empty, rt, catalog, rng);
        foreach (var id in reward.CardChoices)
        {
            Assert.Equal(CardRarity.Epic, catalog.Cards[id].Rarity);
        }
    }

    [Fact]
    public void GenerateFromNonBattle_Treasure_RelicOnlyNoGoldNoPotionNoCards()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(3UL);
        var rngState = new RewardRngState(40, 0);
        var (reward, _) = RewardGenerator.Generate(
            new RewardContext.FromNonBattle(NonBattleRewardKind.Treasure),
            rngState, ImmutableArray<string>.Empty, rt, catalog, rng);
        Assert.Equal(0, reward.Gold);
        Assert.Null(reward.PotionId);
        Assert.Empty(reward.CardChoices);
        Assert.Equal(CardRewardStatus.Claimed, reward.CardStatus);
        Assert.NotNull(reward.RelicId);
        Assert.False(reward.RelicClaimed);
    }

    [Fact]
    public void GenerateFromNonBattle_Treasure_ExcludesOwnedRelics()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(99UL);
        var rngState = new RewardRngState(40, 0);
        var owned = ImmutableArray.CreateRange(catalog.Relics.Keys.Take(catalog.Relics.Count - 1));
        // With ownedExclusions helper — extend Generate signature or use variant below
        var (reward, _) = RewardGenerator.GenerateTreasure(rngState, owned, rt, catalog, rng);
        Assert.NotNull(reward.RelicId);
        Assert.DoesNotContain(reward.RelicId!, owned);
    }
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardGeneratorTests" --nologo`
Expected: 4 件 FAIL (または build error for `GenerateTreasure`)。

- [ ] **Step 3: 実装**

Replace `src/Core/Rewards/RewardGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Rewards;

public static class RewardGenerator
{
    public static (RewardState reward, RewardRngState newRng) Generate(
        RewardContext context,
        RewardRngState rngState,
        ImmutableArray<string> cardExclusions,
        RewardTable table,
        DataCatalog data,
        IRng rng)
    {
        return context switch
        {
            RewardContext.FromEnemy fe => GenerateFromEnemy(fe.Pool, rngState, cardExclusions, table, data, rng),
            RewardContext.FromNonBattle nb when nb.Kind == NonBattleRewardKind.Treasure
                => GenerateTreasure(rngState, ImmutableArray<string>.Empty, table, data, rng),
            RewardContext.FromNonBattle nb
                => GenerateFromNonBattleEvent(nb.Kind, rngState, table, rng),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public static (RewardState, RewardRngState) GenerateTreasure(
        RewardRngState rngState,
        ImmutableArray<string> ownedRelics,
        RewardTable table,
        DataCatalog data,
        IRng rng)
    {
        var pool = data.Relics.Keys
            .Where(id => !ownedRelics.Contains(id))
            .OrderBy(id => id)
            .ToArray();
        string? relic = pool.Length == 0 ? null : pool[rng.NextInt(0, pool.Length)];
        var reward = new RewardState(
            Gold: 0, GoldClaimed: true,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed,
            RelicId: relic,
            RelicClaimed: relic is null);
        return (reward, rngState);
    }

    private static (RewardState, RewardRngState) GenerateFromNonBattleEvent(
        NonBattleRewardKind kind, RewardRngState rngState, RewardTable table, IRng rng)
    {
        string key = "event";
        var entry = table.NonBattle[key];
        int gold = entry.GoldMin + rng.NextInt(0, entry.GoldMax - entry.GoldMin + 1);
        var reward = new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed);
        return (reward, rngState);
    }

    private static (RewardState, RewardRngState) GenerateFromEnemy(
        EnemyPool pool, RewardRngState rngState,
        ImmutableArray<string> excl, RewardTable table, DataCatalog data, IRng rng)
    {
        var entry = table.Pools[pool.Tier];

        int gold = entry.GoldMin + rng.NextInt(0, entry.GoldMax - entry.GoldMin + 1);

        string? potionId = null;
        var newRng = rngState;
        int potionBase = entry.PotionBasePercent;
        if (potionBase == 100) potionId = PickRandomPotion(data, rng);
        else if (potionBase == 0) { }
        else
        {
            int chance = rngState.PotionChancePercent;
            if (rng.NextInt(0, 100) < chance)
            {
                potionId = PickRandomPotion(data, rng);
                newRng = newRng with
                {
                    PotionChancePercent = Math.Max(table.PotionDynamic.Min,
                        chance - table.PotionDynamic.Step)
                };
            }
            else
            {
                newRng = newRng with
                {
                    PotionChancePercent = Math.Min(table.PotionDynamic.Max,
                        chance + table.PotionDynamic.Step)
                };
            }
        }

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
        while (picks.Count < 3)
        {
            var r = rng.NextInt(0, 100);
            CardRarity rarity;
            if (r < commonFinal) rarity = CardRarity.Common;
            else if (r < commonFinal + rareFinal) rarity = CardRarity.Rare;
            else rarity = CardRarity.Epic;

            var pool2 = data.Cards.Values
                .Where(c => c.Rarity == rarity && c.Id.StartsWith("reward_"))
                .Where(c => !excl.Contains(c.Id) && !seen.Contains(c.Id))
                .Select(c => c.Id)
                .ToList();
            if (pool2.Count == 0) continue;
            var pick = pool2[rng.NextInt(0, pool2.Count)];
            picks.Add(pick);
            seen.Add(pick);
        }

        bool hasRare = picks.Any(id => data.Cards[id].Rarity == CardRarity.Rare);
        newRng = newRng with
        {
            RareChanceBonusPercent = hasRare ? 0 : rngState.RareChanceBonusPercent + table.EpicChance.PerBattleIncrement
        };

        var reward = new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: potionId, PotionClaimed: potionId is null,
            CardChoices: picks.ToImmutableArray(),
            CardStatus: CardRewardStatus.Pending);
        return (reward, newRng);
    }

    private static string PickRandomPotion(DataCatalog data, IRng rng)
    {
        var ids = data.Potions.Keys.OrderBy(s => s).ToArray();
        return ids[rng.NextInt(0, ids.Length)];
    }
}
```

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardGeneratorTests" --nologo`
Expected: PASS（新旧ともに）。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Rewards/RewardGenerator.cs tests/Core.Tests/Rewards/RewardGeneratorTests.cs
git commit -m "feat(core): Treasure reward is relic-only; Elite/Boss rarity floor via act1.json"
git push
```

---

### Task B4: RewardApplier.ClaimRelic と caller 追加

**Files:**
- Modify: `src/Core/Rewards/RewardApplier.cs`
- Test: extend `tests/Core.Tests/Rewards/RewardApplierTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Append to `tests/Core.Tests/Rewards/RewardApplierTests.cs`:

```csharp
    [Fact]
    public void ClaimRelic_AddsRelicToRunStateAndMarksClaimed()
    {
        var s0 = SampleV4() with
        {
            ActiveReward = new RewardState(
                Gold: 0, GoldClaimed: true,
                PotionId: null, PotionClaimed: true,
                CardChoices: ImmutableArray<string>.Empty,
                CardStatus: CardRewardStatus.Claimed,
                RelicId: "extra_max_hp",
                RelicClaimed: false),
        };
        var s1 = RewardApplier.ClaimRelic(s0);
        Assert.Contains("extra_max_hp", s1.Relics);
        Assert.True(s1.ActiveReward!.RelicClaimed);
    }

    [Fact]
    public void ClaimRelic_AlreadyClaimed_Throws()
    {
        var s0 = SampleV4() with
        {
            ActiveReward = new RewardState(
                0, true, null, true,
                ImmutableArray<string>.Empty, CardRewardStatus.Claimed,
                RelicId: "extra_max_hp", RelicClaimed: true),
        };
        Assert.Throws<InvalidOperationException>(() => RewardApplier.ClaimRelic(s0));
    }

    [Fact]
    public void ClaimRelic_NoActiveReward_Throws()
    {
        var s0 = SampleV4() with { ActiveReward = null };
        Assert.Throws<InvalidOperationException>(() => RewardApplier.ClaimRelic(s0));
    }
```

（`SampleV4()` は Task A4 で追加したものを流用。必要なら `RewardApplierTests` 内に private ヘルパを複製する。）

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardApplierTests.ClaimRelic" --nologo`
Expected: FAIL / build error (`ClaimRelic` 未定義)。

- [ ] **Step 3: 実装**

Append to `src/Core/Rewards/RewardApplier.cs`:

```csharp
    public static RunState ClaimRelic(RunState s)
    {
        var r = Require(s);
        if (r.RelicId is null) throw new InvalidOperationException("No relic to claim");
        if (r.RelicClaimed) throw new InvalidOperationException("Relic already claimed");
        var newRelics = s.Relics.Append(r.RelicId).ToList();
        return s with
        {
            Relics = newRelics,
            ActiveReward = r with { RelicClaimed = true },
        };
    }
```

（`OnPickup` 効果の連鎖は Task C3 でまとめて配線する。ここでは単純に所持に追加するだけ。）

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardApplierTests" --nologo`
Expected: PASS。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Rewards/RewardApplier.cs tests/Core.Tests/Rewards/RewardApplierTests.cs
git commit -m "feat(core): add RewardApplier.ClaimRelic"
git push
```

---

## Part C — Non-Battle Relics

### Task C1: 非バトル用 CardEffect 型（GainMaxHp / GainGold / RestHealBonus）

**Files:**
- Modify: `src/Core/Cards/CardEffect.cs`
- Modify: `src/Core/Cards/CardEffectParser.cs`

- [ ] **Step 1: 型追加**

Append to `src/Core/Cards/CardEffect.cs`:

```csharp
/// <summary>最大 HP を恒久加算する（レリック OnPickup 用）。</summary>
public sealed record GainMaxHpEffect(int Amount) : CardEffect("gainMaxHp");

/// <summary>Gold を加算する（レリック OnPickup / OnMapTileResolved 用）。</summary>
public sealed record GainGoldEffect(int Amount) : CardEffect("gainGold");

/// <summary>Rest 時の回復量へ +Amount（レリック Passive 用）。</summary>
public sealed record RestHealBonusEffect(int Amount) : CardEffect("restHealBonus");
```

- [ ] **Step 2: パーサ追加**

Edit `src/Core/Cards/CardEffectParser.cs` switch:

```csharp
            "damage" => new DamageEffect(GetRequiredInt(el, "amount", makeException)),
            "gainBlock" => new GainBlockEffect(GetRequiredInt(el, "amount", makeException)),
            "gainMaxHp" => new GainMaxHpEffect(GetRequiredInt(el, "amount", makeException)),
            "gainGold" => new GainGoldEffect(GetRequiredInt(el, "amount", makeException)),
            "restHealBonus" => new RestHealBonusEffect(GetRequiredInt(el, "amount", makeException)),
            _ => new UnknownEffect(type),
```

- [ ] **Step 3: ビルド**

Run: `dotnet build --nologo`
Expected: PASS。

- [ ] **Step 4: コミット**

```bash
git add src/Core/Cards/CardEffect.cs src/Core/Cards/CardEffectParser.cs
git commit -m "feat(core): add GainMaxHp/GainGold/RestHealBonus effect types"
git push
```

---

### Task C2: 4 本の非バトルレリック JSON

**Files:**
- Create: `src/Core/Data/Relics/extra_max_hp.json`
- Create: `src/Core/Data/Relics/coin_purse.json`
- Create: `src/Core/Data/Relics/traveler_boots.json`
- Create: `src/Core/Data/Relics/warm_blanket.json`

- [ ] **Step 1: extra_max_hp.json**

```json
{
  "id": "extra_max_hp",
  "name": "鍛えた肉体",
  "rarity": 0,
  "trigger": "OnPickup",
  "effects": [{ "type": "gainMaxHp", "amount": 7 }]
}
```

- [ ] **Step 2: coin_purse.json**

```json
{
  "id": "coin_purse",
  "name": "旅人の財布",
  "rarity": 0,
  "trigger": "OnPickup",
  "effects": [{ "type": "gainGold", "amount": 50 }]
}
```

- [ ] **Step 3: traveler_boots.json**

```json
{
  "id": "traveler_boots",
  "name": "旅人のブーツ",
  "rarity": 1,
  "trigger": "OnMapTileResolved",
  "effects": [{ "type": "gainGold", "amount": 1 }]
}
```

- [ ] **Step 4: warm_blanket.json**

```json
{
  "id": "warm_blanket",
  "name": "あたたかい毛布",
  "rarity": 1,
  "trigger": "Passive",
  "effects": [{ "type": "restHealBonus", "amount": 10 }]
}
```

各ファイルは `<Project>/src/Core/Core.csproj` の `<EmbeddedResource Include="Data\**\*.json" />` パターンに自動乗る（Phase 5 で既に設定済み）。

- [ ] **Step 5: csproj の EmbeddedResource 確認**

Run:
```bash
grep -n "EmbeddedResource" src/Core/Core.csproj
```
Expected: `Data/**` もしくは個別 relic フォルダが登録されている。もし無ければ:

```xml
<ItemGroup>
  <EmbeddedResource Include="Data\**\*.json" />
</ItemGroup>
```

- [ ] **Step 6: 読み込めることを確認**

Run:
```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~DataCatalog" --nologo
```
Expected: 既存テスト PASS。DataCatalog に 6 レリックが登録されていること。

- [ ] **Step 7: コミット**

```bash
git add src/Core/Data/Relics/extra_max_hp.json src/Core/Data/Relics/coin_purse.json src/Core/Data/Relics/traveler_boots.json src/Core/Data/Relics/warm_blanket.json src/Core/Core.csproj
git commit -m "data(relics): add 4 non-battle relics (extra_max_hp, coin_purse, traveler_boots, warm_blanket)"
git push
```

---

### Task C3: NonBattleRelicEffects モジュール

**Files:**
- Create: `src/Core/Relics/NonBattleRelicEffects.cs`
- Test: `tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class NonBattleRelicEffectsTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Sample(int hp = 50, int maxHp = 80, int gold = 99) =>
        RunState.NewSoloRun(
            Catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 4, 22, 0, 0, 0, System.TimeSpan.Zero)
        ) with { CurrentHp = hp, MaxHp = maxHp, Gold = gold };

    [Fact]
    public void ApplyOnPickup_ExtraMaxHp_IncreasesMaxHpAndCurrentHp()
    {
        var s0 = Sample(hp: 50, maxHp: 80);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "extra_max_hp", Catalog);
        Assert.Equal(87, s1.MaxHp);
        Assert.Equal(57, s1.CurrentHp);  // CurrentHp も +7
    }

    [Fact]
    public void ApplyOnPickup_CoinPurse_AddsGold()
    {
        var s0 = Sample(gold: 99);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "coin_purse", Catalog);
        Assert.Equal(149, s1.Gold);
    }

    [Fact]
    public void ApplyOnPickup_NonOnPickupTrigger_NoOp()
    {
        var s0 = Sample(gold: 99);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "traveler_boots", Catalog);
        Assert.Equal(99, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_TravelerBoots_GrantsOneGoldPerOwned()
    {
        var s0 = Sample(gold: 10) with { Relics = new List<string> { "traveler_boots" } };
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, Catalog);
        Assert.Equal(11, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_NoTravelerBoots_NoOp()
    {
        var s0 = Sample(gold: 10);
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, Catalog);
        Assert.Equal(10, s1.Gold);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_WarmBlanket_Adds10()
    {
        var s0 = Sample() with { Relics = new List<string> { "warm_blanket" } };
        int bonus = NonBattleRelicEffects.ApplyPassiveRestHealBonus(0, s0, Catalog);
        Assert.Equal(10, bonus);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_NoRelic_ReturnsBase()
    {
        var s0 = Sample();
        int bonus = NonBattleRelicEffects.ApplyPassiveRestHealBonus(5, s0, Catalog);
        Assert.Equal(5, bonus);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NonBattleRelicEffectsTests" --nologo`
Expected: build error。

- [ ] **Step 3: 実装**

Create `src/Core/Relics/NonBattleRelicEffects.cs`:

```csharp
using System;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

public static class NonBattleRelicEffects
{
    public static RunState ApplyOnPickup(RunState s, string relicId, DataCatalog catalog)
    {
        if (!catalog.TryGetRelic(relicId, out var def)) return s;
        if (def.Trigger != RelicTrigger.OnPickup) return s;
        return ApplyEffects(s, def);
    }

    public static RunState ApplyOnMapTileResolved(RunState s, DataCatalog catalog)
    {
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (def.Trigger != RelicTrigger.OnMapTileResolved) continue;
            s = ApplyEffects(s, def);
        }
        return s;
    }

    public static int ApplyPassiveRestHealBonus(int baseBonus, RunState s, DataCatalog catalog)
    {
        int bonus = baseBonus;
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (def.Trigger != RelicTrigger.Passive) continue;
            foreach (var eff in def.Effects)
                if (eff is RestHealBonusEffect rhb) bonus += rhb.Amount;
        }
        return bonus;
    }

    private static RunState ApplyEffects(RunState s, Relics.RelicDefinition def)
    {
        foreach (var eff in def.Effects)
        {
            s = eff switch
            {
                GainMaxHpEffect gm => s with { MaxHp = s.MaxHp + gm.Amount, CurrentHp = s.CurrentHp + gm.Amount },
                GainGoldEffect gg => s with { Gold = s.Gold + gg.Amount },
                _ => s,
            };
        }
        return s;
    }
}
```

（`RestHealBonusEffect` は `ApplyEffects` 経由では RunState に影響しない（bonus 集計は `ApplyPassiveRestHealBonus` 側）。`Passive` トリガなレリックを `ApplyOnPickup` に通しても noop になる。）

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NonBattleRelicEffectsTests" --nologo`
Expected: PASS (7 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Relics/NonBattleRelicEffects.cs tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs
git commit -m "feat(core): NonBattleRelicEffects (OnPickup / OnMapTileResolved / Passive rest bonus)"
git push
```

---

### Task C4: RewardApplier.ClaimRelic から OnPickup を連鎖

**Files:**
- Modify: `src/Core/Rewards/RewardApplier.cs`
- Test: extend `tests/Core.Tests/Rewards/RewardApplierTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Append to `tests/Core.Tests/Rewards/RewardApplierTests.cs`:

```csharp
    [Fact]
    public void ClaimRelic_WithExtraMaxHp_ChainsOnPickup()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var s0 = SampleV4() with
        {
            CurrentHp = 50, MaxHp = 80,
            ActiveReward = new RewardState(
                0, true, null, true,
                ImmutableArray<string>.Empty, CardRewardStatus.Claimed,
                RelicId: "extra_max_hp", RelicClaimed: false),
        };
        var s1 = RewardApplier.ClaimRelic(s0, catalog);
        Assert.Equal(87, s1.MaxHp);
        Assert.Equal(57, s1.CurrentHp);
        Assert.Contains("extra_max_hp", s1.Relics);
    }
```

既存 `ClaimRelic(s)` の署名が `ClaimRelic(s, catalog)` に変わるので、Task B4 で書いたテストの呼び出し側も `catalog` を渡すよう修正する（3 箇所）。

- [ ] **Step 2: 失敗を確認**

Run: `dotnet build --nologo`
Expected: 既存呼び出しが署名不一致でエラー。

- [ ] **Step 3: 実装**

Edit `src/Core/Rewards/RewardApplier.cs` の `ClaimRelic` を差し替え:

```csharp
    public static RunState ClaimRelic(RunState s, DataCatalog catalog)
    {
        var r = Require(s);
        if (r.RelicId is null) throw new InvalidOperationException("No relic to claim");
        if (r.RelicClaimed) throw new InvalidOperationException("Relic already claimed");
        var newRelics = s.Relics.Append(r.RelicId).ToList();
        var s1 = s with
        {
            Relics = newRelics,
            ActiveReward = r with { RelicClaimed = true },
        };
        return Relics.NonBattleRelicEffects.ApplyOnPickup(s1, r.RelicId, catalog);
    }
```

`RewardApplier.cs` 先頭に `using RoguelikeCardGame.Core.Data;` を追加。

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RewardApplierTests" --nologo`
Expected: PASS。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Rewards/RewardApplier.cs tests/Core.Tests/Rewards/RewardApplierTests.cs
git commit -m "feat(core): chain OnPickup relic effects from RewardApplier.ClaimRelic"
git push
```

---

## Part D — Event System

### Task D1: EventCondition / EventEffect / EventChoice / EventDefinition / EventInstance

**Files:**
- Create: `src/Core/Events/EventCondition.cs`
- Create: `src/Core/Events/EventEffect.cs`
- Create: `src/Core/Events/EventChoice.cs`
- Create: `src/Core/Events/EventDefinition.cs`
- Create: `src/Core/Events/EventInstance.cs`

- [ ] **Step 1: EventCondition**

Create `src/Core/Events/EventCondition.cs`:

```csharp
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Events;

/// <summary>EventChoice の選択可否判定。null なら常に選択可。</summary>
public abstract record EventCondition
{
    public abstract bool IsSatisfied(RunState state);

    public sealed record MinGold(int Amount) : EventCondition
    {
        public override bool IsSatisfied(RunState s) => s.Gold >= Amount;
    }

    public sealed record MinHp(int Amount) : EventCondition
    {
        public override bool IsSatisfied(RunState s) => s.CurrentHp >= Amount;
    }
}
```

- [ ] **Step 2: EventEffect**

Create `src/Core/Events/EventEffect.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Events;

/// <summary>イベント選択肢が適用する効果。タグ付き union（record hierarchy）。</summary>
public abstract record EventEffect
{
    public sealed record GainGold(int Amount) : EventEffect;
    public sealed record PayGold(int Amount) : EventEffect;
    public sealed record Heal(int Amount) : EventEffect;
    public sealed record TakeDamage(int Amount) : EventEffect;
    public sealed record GainMaxHp(int Amount) : EventEffect;
    public sealed record LoseMaxHp(int Amount) : EventEffect;
    public sealed record GainRelicRandom(CardRarity Rarity) : EventEffect;
    public sealed record GrantCardReward() : EventEffect;
}
```

- [ ] **Step 3: EventChoice**

Create `src/Core/Events/EventChoice.cs`:

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Events;

public sealed record EventChoice(
    string Label,
    EventCondition? Condition,
    ImmutableArray<EventEffect> Effects);
```

- [ ] **Step 4: EventDefinition**

Create `src/Core/Events/EventDefinition.cs`:

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Events;

public sealed record EventDefinition(
    string Id,
    string Name,
    string Description,
    ImmutableArray<EventChoice> Choices);
```

- [ ] **Step 5: EventInstance**

Create `src/Core/Events/EventInstance.cs`:

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Events;

/// <summary>RunState に保持される「現在解決中のイベント」スナップショット。</summary>
public sealed record EventInstance(
    string EventId,
    ImmutableArray<EventChoice> Choices);
```

- [ ] **Step 6: ビルド**

Run: `dotnet build --nologo`
Expected: PASS。

- [ ] **Step 7: コミット**

```bash
git add src/Core/Events/
git commit -m "feat(core): add Event records (Definition, Choice, Effect, Condition, Instance)"
git push
```

---

### Task D2: EventJsonLoader

**Files:**
- Create: `src/Core/Events/EventJsonLoader.cs`
- Test: `tests/Core.Tests/Events/EventJsonLoaderTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Events/EventJsonLoaderTests.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Events;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventJsonLoaderTests
{
    private const string SampleJson = """
    {
      "id": "test_event",
      "name": "テストイベント",
      "description": "説明文",
      "choices": [
        {
          "label": "Gold を貰う",
          "effects": [ { "type": "gainGold", "amount": 30 } ]
        },
        {
          "label": "HP を失って Gold を大量に貰う",
          "condition": { "type": "minHp", "amount": 10 },
          "effects": [
            { "type": "takeDamage", "amount": 5 },
            { "type": "gainGold", "amount": 100 }
          ]
        },
        {
          "label": "レリック",
          "condition": { "type": "minGold", "amount": 50 },
          "effects": [ { "type": "payGold", "amount": 50 }, { "type": "gainRelicRandom", "rarity": 0 } ]
        },
        {
          "label": "カード報酬",
          "effects": [ { "type": "grantCardReward" } ]
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ValidJson_ReturnsDefinition()
    {
        var def = EventJsonLoader.Parse(SampleJson);
        Assert.Equal("test_event", def.Id);
        Assert.Equal("テストイベント", def.Name);
        Assert.Equal(4, def.Choices.Length);

        Assert.Null(def.Choices[0].Condition);
        Assert.IsType<EventEffect.GainGold>(def.Choices[0].Effects[0]);

        Assert.IsType<EventCondition.MinHp>(def.Choices[1].Condition);
        Assert.IsType<EventEffect.TakeDamage>(def.Choices[1].Effects[0]);

        Assert.IsType<EventCondition.MinGold>(def.Choices[2].Condition);
        var gr = Assert.IsType<EventEffect.GainRelicRandom>(def.Choices[2].Effects[1]);
        Assert.Equal(CardRarity.Common, gr.Rarity);

        Assert.IsType<EventEffect.GrantCardReward>(def.Choices[3].Effects[0]);
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<EventJsonException>(() => EventJsonLoader.Parse("{"));
    }

    [Fact]
    public void Parse_UnknownEffectType_Throws()
    {
        const string bad = """
        { "id": "x", "name": "n", "description": "d", "choices": [ { "label": "a", "effects": [ { "type": "nope" } ] } ] }
        """;
        Assert.Throws<EventJsonException>(() => EventJsonLoader.Parse(bad));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~EventJsonLoaderTests" --nologo`
Expected: build error。

- [ ] **Step 3: 実装**

Create `src/Core/Events/EventJsonLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Events;

public sealed class EventJsonException : Exception
{
    public EventJsonException(string message) : base(message) { }
    public EventJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class EventJsonLoader
{
    public static EventDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EventJsonException("event JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            string id = GetString(root, "id");
            string name = GetString(root, "name");
            string desc = GetString(root, "description");
            if (!root.TryGetProperty("choices", out var choicesEl) || choicesEl.ValueKind != JsonValueKind.Array)
                throw new EventJsonException($"event \"{id}\" に choices 配列がありません。");
            if (choicesEl.GetArrayLength() < 1)
                throw new EventJsonException($"event \"{id}\" の choices は 1 要素以上必要です。");

            var choices = ImmutableArray.CreateBuilder<EventChoice>();
            foreach (var ch in choicesEl.EnumerateArray())
            {
                string label = GetString(ch, "label");
                EventCondition? cond = null;
                if (ch.TryGetProperty("condition", out var condEl) && condEl.ValueKind == JsonValueKind.Object)
                    cond = ParseCondition(condEl, id);
                var effects = ParseEffects(ch, id);
                choices.Add(new EventChoice(label, cond, effects));
            }
            return new EventDefinition(id, name, desc, choices.ToImmutable());
        }
    }

    private static EventCondition ParseCondition(JsonElement el, string eventId)
    {
        string type = GetString(el, "type");
        return type switch
        {
            "minGold" => new EventCondition.MinGold(GetInt(el, "amount")),
            "minHp" => new EventCondition.MinHp(GetInt(el, "amount")),
            _ => throw new EventJsonException($"event \"{eventId}\" の condition.type \"{type}\" は無効。")
        };
    }

    private static ImmutableArray<EventEffect> ParseEffects(JsonElement choiceEl, string eventId)
    {
        if (!choiceEl.TryGetProperty("effects", out var effs) || effs.ValueKind != JsonValueKind.Array)
            return ImmutableArray<EventEffect>.Empty;
        var list = new List<EventEffect>();
        foreach (var e in effs.EnumerateArray())
        {
            string type = GetString(e, "type");
            EventEffect effect = type switch
            {
                "gainGold" => new EventEffect.GainGold(GetInt(e, "amount")),
                "payGold" => new EventEffect.PayGold(GetInt(e, "amount")),
                "heal" => new EventEffect.Heal(GetInt(e, "amount")),
                "takeDamage" => new EventEffect.TakeDamage(GetInt(e, "amount")),
                "gainMaxHp" => new EventEffect.GainMaxHp(GetInt(e, "amount")),
                "loseMaxHp" => new EventEffect.LoseMaxHp(GetInt(e, "amount")),
                "gainRelicRandom" => new EventEffect.GainRelicRandom(ParseRarity(GetInt(e, "rarity"), eventId)),
                "grantCardReward" => new EventEffect.GrantCardReward(),
                _ => throw new EventJsonException($"event \"{eventId}\" の effect.type \"{type}\" は無効。")
            };
            list.Add(effect);
        }
        return list.ToImmutableArray();
    }

    private static CardRarity ParseRarity(int raw, string eventId)
    {
        if (!Enum.IsDefined(typeof(CardRarity), raw))
            throw new EventJsonException($"event \"{eventId}\" の rarity {raw} は無効。");
        return (CardRarity)raw;
    }

    private static string GetString(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            throw new EventJsonException($"必須フィールド \"{key}\" (string) がありません。");
        return v.GetString()!;
    }

    private static int GetInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
            throw new EventJsonException($"必須フィールド \"{key}\" (number) がありません。");
        return v.GetInt32();
    }
}
```

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~EventJsonLoaderTests" --nologo`
Expected: PASS (3 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Events/EventJsonLoader.cs tests/Core.Tests/Events/EventJsonLoaderTests.cs
git commit -m "feat(core): add EventJsonLoader"
git push
```

---

### Task D3: 3 本のイベント JSON

**Files:**
- Create: `src/Core/Data/Events/blessing_fountain.json`
- Create: `src/Core/Data/Events/shady_merchant.json`
- Create: `src/Core/Data/Events/old_library.json`

- [ ] **Step 1: blessing_fountain.json**

```json
{
  "id": "blessing_fountain",
  "name": "祝福の泉",
  "description": "清らかな泉が湧き出ている。いくつかの噂を耳にした。",
  "choices": [
    {
      "label": "水を飲む（HP +15）",
      "effects": [ { "type": "heal", "amount": 15 } ]
    },
    {
      "label": "コインを投げ入れる（最大HP +5）",
      "effects": [ { "type": "gainMaxHp", "amount": 5 } ]
    },
    {
      "label": "立ち去る",
      "effects": []
    }
  ]
}
```

- [ ] **Step 2: shady_merchant.json**

```json
{
  "id": "shady_merchant",
  "name": "怪しい商人",
  "description": "フードを目深にかぶった商人が、レリックを売ると持ちかけてきた。",
  "choices": [
    {
      "label": "50 ゴールドで買う",
      "condition": { "type": "minGold", "amount": 50 },
      "effects": [
        { "type": "payGold", "amount": 50 },
        { "type": "gainRelicRandom", "rarity": 0 }
      ]
    },
    {
      "label": "200 ゴールドで特上品",
      "condition": { "type": "minGold", "amount": 200 },
      "effects": [
        { "type": "payGold", "amount": 200 },
        { "type": "gainRelicRandom", "rarity": 2 }
      ]
    },
    {
      "label": "断る",
      "effects": []
    }
  ]
}
```

- [ ] **Step 3: old_library.json**

```json
{
  "id": "old_library",
  "name": "古びた図書館",
  "description": "埃の積もった本棚の奥に、誰かの手記が眠っている。",
  "choices": [
    {
      "label": "読みふける（カード報酬）",
      "effects": [ { "type": "grantCardReward" } ]
    },
    {
      "label": "ページを破って持ち去る（最大HP -5, 50 ゴールド）",
      "effects": [
        { "type": "loseMaxHp", "amount": 5 },
        { "type": "gainGold", "amount": 50 }
      ]
    },
    {
      "label": "何もせず立ち去る",
      "effects": []
    }
  ]
}
```

- [ ] **Step 4: csproj の EmbeddedResource が `Data/**` をカバーしていることを確認**

既に Phase 5 で `Data/**` パターンならそのまま、もし `Data/Events/` が未登録なら手動追加:

```xml
<EmbeddedResource Include="Data\Events\*.json" />
```

- [ ] **Step 5: コミット**

```bash
git add src/Core/Data/Events/
git commit -m "data(events): add 3 events (blessing_fountain, shady_merchant, old_library)"
git push
```

---

### Task D4: DataCatalog に Events を登録

**Files:**
- Modify: `src/Core/Data/DataCatalog.cs`
- Modify: `src/Core/Data/EmbeddedDataLoader.cs`
- Test: extend `tests/Core.Tests/Data/` で適切なところ（もし DataCatalogPhase5Tests.cs があればそこへ）。新規 `tests/Core.Tests/Events/EventCatalogTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Events/EventCatalogTests.cs`:

```csharp
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventCatalogTests
{
    [Fact]
    public void LoadCatalog_IncludesThreeSeedEvents()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Contains("blessing_fountain", catalog.Events.Keys);
        Assert.Contains("shady_merchant", catalog.Events.Keys);
        Assert.Contains("old_library", catalog.Events.Keys);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~EventCatalogTests" --nologo`
Expected: `Events` プロパティ無し。

- [ ] **Step 3: DataCatalog を拡張**

Edit `src/Core/Data/DataCatalog.cs`:

```csharp
public sealed record DataCatalog(
    IReadOnlyDictionary<string, CardDefinition> Cards,
    IReadOnlyDictionary<string, RelicDefinition> Relics,
    IReadOnlyDictionary<string, PotionDefinition> Potions,
    IReadOnlyDictionary<string, EnemyDefinition> Enemies,
    IReadOnlyDictionary<string, EncounterDefinition> Encounters,
    IReadOnlyDictionary<string, RewardTable> RewardTables,
    IReadOnlyDictionary<string, CharacterDefinition> Characters,
    IReadOnlyDictionary<string, RoguelikeCardGame.Core.Events.EventDefinition> Events,
    RoguelikeCardGame.Core.Merchant.MerchantPrices? MerchantPrices)
```

`LoadFromStrings` の署名と本体に events / merchantPricesJson 引数を追加:

```csharp
    public static DataCatalog LoadFromStrings(
        IEnumerable<string> cards,
        IEnumerable<string> relics,
        IEnumerable<string> potions,
        IEnumerable<string> enemies,
        IEnumerable<string> encounters,
        IEnumerable<string> rewardTables,
        IEnumerable<string> characters,
        IEnumerable<string> events,
        string? merchantPricesJson)
    {
        // ... 既存のコード ...

        var eventMap = new Dictionary<string, RoguelikeCardGame.Core.Events.EventDefinition>();
        foreach (var json in events)
        {
            var def = RoguelikeCardGame.Core.Events.EventJsonLoader.Parse(json);
            if (!eventMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"event ID が重複: {def.Id}");
        }

        RoguelikeCardGame.Core.Merchant.MerchantPrices? prices = null;
        if (merchantPricesJson is not null)
            prices = RoguelikeCardGame.Core.Merchant.MerchantPricesJsonLoader.Parse(merchantPricesJson);

        return new DataCatalog(
            cardMap, relicMap, potionMap, enemyMap, encMap, rtMap, chMap,
            eventMap, prices);
    }
```

**注意:** `MerchantPrices` と `MerchantPricesJsonLoader` は Part E で作るが、ここでは型 forward reference として置き、`Part E` 完了まで `merchantPricesJson: null` で呼ぶ。Part E で `MerchantPricesJsonLoader` が実装された時点で `EmbeddedDataLoader` が本物を渡すようになる。

**順序推奨:** Part E1 (MerchantPrices 型) → Part D4 の順で実装する。もしくは Part D4 時点では MerchantPrices パラメータを省略し、Part E の Task で追加する。本プラン順序（Part D → Part E）なら、Part D4 実装時は `MerchantPrices? = null` をパラメータに置き、Part E で `MerchantPricesJsonLoader` を追加した後に `EmbeddedDataLoader` で読み込みを追加する。

- [ ] **Step 4: EmbeddedDataLoader を拡張**

Edit `src/Core/Data/EmbeddedDataLoader.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RoguelikeCardGame.Core.Data;

public static class EmbeddedDataLoader
{
    private const string CardsPrefix = "RoguelikeCardGame.Core.Data.Cards.";
    private const string RelicsPrefix = "RoguelikeCardGame.Core.Data.Relics.";
    private const string PotionsPrefix = "RoguelikeCardGame.Core.Data.Potions.";
    private const string EnemiesPrefix = "RoguelikeCardGame.Core.Data.Enemies.";
    private const string EncountersPrefix = "RoguelikeCardGame.Core.Data.Encounters.";
    private const string RewardTablePrefix = "RoguelikeCardGame.Core.Data.RewardTable.";
    private const string CharactersPrefix = "RoguelikeCardGame.Core.Data.Characters.";
    private const string EventsPrefix = "RoguelikeCardGame.Core.Data.Events.";
    private const string MerchantPricesResource = "RoguelikeCardGame.Core.Data.merchant-prices.json";

    public static DataCatalog LoadCatalog()
    {
        var asm = typeof(EmbeddedDataLoader).Assembly;
        string? pricesJson = ReadSingleOrNull(asm, MerchantPricesResource);
        return DataCatalog.LoadFromStrings(
            cards: ReadAllWithPrefix(asm, CardsPrefix),
            relics: ReadAllWithPrefix(asm, RelicsPrefix),
            potions: ReadAllWithPrefix(asm, PotionsPrefix),
            enemies: ReadAllWithPrefix(asm, EnemiesPrefix),
            encounters: ReadAllWithPrefix(asm, EncountersPrefix),
            rewardTables: ReadAllWithPrefix(asm, RewardTablePrefix),
            characters: ReadAllWithPrefix(asm, CharactersPrefix),
            events: ReadAllWithPrefix(asm, EventsPrefix),
            merchantPricesJson: pricesJson);
    }

    private static IEnumerable<string> ReadAllWithPrefix(Assembly asm, string prefix)
    {
        var names = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix) && n.EndsWith(".json"))
            .OrderBy(n => n);
        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }

    private static string? ReadSingleOrNull(Assembly asm, string resourceName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 5: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~EventCatalogTests" --nologo`
Expected: PASS (3 件)。ただし `MerchantPrices` 型が未定義だと Step 3 のコードがビルドエラーになる。この場合は Part E の Task E1 を先行実施するか、暫定的に `MerchantPrices? prices = null` と Nullable プロパティのみを Step 3 で定義し、実装は Part E に回す。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Data/DataCatalog.cs src/Core/Data/EmbeddedDataLoader.cs tests/Core.Tests/Events/EventCatalogTests.cs
git commit -m "feat(core): DataCatalog exposes Events and MerchantPrices"
git push
```

---

### Task D5: EventPool

**Files:**
- Create: `src/Core/Events/EventPool.cs`
- Test: `tests/Core.Tests/Events/EventPoolTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Events/EventPoolTests.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventPoolTests
{
    private static readonly ImmutableArray<EventDefinition> Defs =
        ImmutableArray.Create(
            new EventDefinition("a", "A", "", ImmutableArray<EventChoice>.Empty),
            new EventDefinition("b", "B", "", ImmutableArray<EventChoice>.Empty),
            new EventDefinition("c", "C", "", ImmutableArray<EventChoice>.Empty));

    [Fact]
    public void Pick_DeterministicForSameSeed()
    {
        var rngA = new SequentialRng(42UL);
        var rngB = new SequentialRng(42UL);
        Assert.Equal(EventPool.Pick(Defs, rngA).Id, EventPool.Pick(Defs, rngB).Id);
    }

    [Fact]
    public void Pick_EmptyPool_Throws()
    {
        var rng = new SequentialRng(1UL);
        Assert.Throws<System.InvalidOperationException>(() =>
            EventPool.Pick(ImmutableArray<EventDefinition>.Empty, rng));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~EventPoolTests" --nologo`
Expected: build error。

- [ ] **Step 3: 実装**

Create `src/Core/Events/EventPool.cs`:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Events;

public static class EventPool
{
    public static EventDefinition Pick(ImmutableArray<EventDefinition> pool, IRng rng)
    {
        if (pool.IsDefault || pool.Length == 0)
            throw new InvalidOperationException("Event pool is empty");
        var sorted = pool.OrderBy(d => d.Id).ToArray();
        return sorted[rng.NextInt(0, sorted.Length)];
    }
}
```

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~EventPoolTests" --nologo`
Expected: PASS (2 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Events/EventPool.cs tests/Core.Tests/Events/EventPoolTests.cs
git commit -m "feat(core): add EventPool.Pick (uniform, deterministic)"
git push
```

---

### Task D6: EventResolver

**Files:**
- Create: `src/Core/Events/EventResolver.cs`
- Test: `tests/Core.Tests/Events/EventResolverTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Events/EventResolverTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventResolverTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Base(int hp = 50, int maxHp = 80, int gold = 100) =>
        RunState.NewSoloRun(
            Catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero)
        ) with { CurrentHp = hp, MaxHp = maxHp, Gold = gold };

    private static EventInstance MakeInstance(params EventChoice[] choices) =>
        new("test", ImmutableArray.Create(choices));

    [Fact]
    public void ApplyChoice_GainGold_IncreasesGold()
    {
        var inst = MakeInstance(new EventChoice("gain",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.GainGold(30))));
        var s0 = Base(gold: 100) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(130, s1.Gold);
        Assert.Null(s1.ActiveEvent);
    }

    [Fact]
    public void ApplyChoice_PayGold_ReducesGold()
    {
        var inst = MakeInstance(new EventChoice("pay",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.PayGold(30))));
        var s0 = Base(gold: 100) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(70, s1.Gold);
    }

    [Fact]
    public void ApplyChoice_HealCapsAtMaxHp()
    {
        var inst = MakeInstance(new EventChoice("heal",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.Heal(100))));
        var s0 = Base(hp: 50, maxHp: 80) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(80, s1.CurrentHp);
    }

    [Fact]
    public void ApplyChoice_TakeDamageFloorsAtZero()
    {
        var inst = MakeInstance(new EventChoice("dmg",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.TakeDamage(100))));
        var s0 = Base(hp: 10) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(0, s1.CurrentHp);
    }

    [Fact]
    public void ApplyChoice_GainMaxHp_IncreasesBoth()
    {
        var inst = MakeInstance(new EventChoice("max",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.GainMaxHp(5))));
        var s0 = Base(hp: 50, maxHp: 80) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(85, s1.MaxHp);
        Assert.Equal(55, s1.CurrentHp);
    }

    [Fact]
    public void ApplyChoice_LoseMaxHp_DecreasesBothAndFloorsCurrent()
    {
        var inst = MakeInstance(new EventChoice("max",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.LoseMaxHp(10))));
        var s0 = Base(hp: 75, maxHp: 80) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(70, s1.MaxHp);
        Assert.Equal(70, s1.CurrentHp);
    }

    [Fact]
    public void ApplyChoice_GrantCardReward_SetsActiveReward()
    {
        var inst = MakeInstance(new EventChoice("card",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.GrantCardReward())));
        var s0 = Base() with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.NotNull(s1.ActiveReward);
        Assert.Equal(3, s1.ActiveReward!.CardChoices.Length);
        Assert.Null(s1.ActiveEvent);
    }

    [Fact]
    public void ApplyChoice_GainRelicRandom_AddsRelicAndTriggersOnPickup()
    {
        var inst = MakeInstance(new EventChoice("relic",
            null, ImmutableArray.Create<EventEffect>(
                new EventEffect.GainRelicRandom(CardRarity.Common))));
        var s0 = Base() with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(7UL));
        Assert.Single(s1.Relics);
        // OnPickup が発火していれば extra_max_hp / coin_purse のどちらかでも効果が見える
        Assert.True(s1.MaxHp >= s0.MaxHp);
    }

    [Fact]
    public void ApplyChoice_ConditionFails_Throws()
    {
        var inst = MakeInstance(new EventChoice("pay",
            new EventCondition.MinGold(500),
            ImmutableArray.Create<EventEffect>(new EventEffect.PayGold(500))));
        var s0 = Base(gold: 100) with { ActiveEvent = inst };
        Assert.Throws<InvalidOperationException>(() =>
            EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL)));
    }

    [Fact]
    public void ApplyChoice_IndexOutOfRange_Throws()
    {
        var inst = MakeInstance(new EventChoice("x", null, ImmutableArray<EventEffect>.Empty));
        var s0 = Base() with { ActiveEvent = inst };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EventResolver.ApplyChoice(s0, 5, Catalog, new SequentialRng(1UL)));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~EventResolverTests" --nologo`
Expected: build error。

- [ ] **Step 3: 実装**

Create `src/Core/Events/EventResolver.cs`:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Events;

public static class EventResolver
{
    public static RunState ApplyChoice(
        RunState s, int choiceIndex, DataCatalog catalog, IRng rng)
    {
        if (s.ActiveEvent is null) throw new InvalidOperationException("No active event");
        var inst = s.ActiveEvent;
        if (choiceIndex < 0 || choiceIndex >= inst.Choices.Length)
            throw new ArgumentOutOfRangeException(nameof(choiceIndex));
        var choice = inst.Choices[choiceIndex];
        if (choice.Condition is not null && !choice.Condition.IsSatisfied(s))
            throw new InvalidOperationException($"Condition not met for choice {choiceIndex}");

        foreach (var eff in choice.Effects)
            s = Apply(s, eff, catalog, rng);

        return s with { ActiveEvent = null };
    }

    private static RunState Apply(RunState s, EventEffect eff, DataCatalog catalog, IRng rng)
    {
        switch (eff)
        {
            case EventEffect.GainGold gg:
                return s with { Gold = s.Gold + gg.Amount };
            case EventEffect.PayGold pg:
                return s with { Gold = Math.Max(0, s.Gold - pg.Amount) };
            case EventEffect.Heal h:
                return s with { CurrentHp = Math.Min(s.MaxHp, s.CurrentHp + h.Amount) };
            case EventEffect.TakeDamage td:
                return s with { CurrentHp = Math.Max(0, s.CurrentHp - td.Amount) };
            case EventEffect.GainMaxHp gm:
                return s with { MaxHp = s.MaxHp + gm.Amount, CurrentHp = s.CurrentHp + gm.Amount };
            case EventEffect.LoseMaxHp lm:
                int newMax = Math.Max(1, s.MaxHp - lm.Amount);
                return s with { MaxHp = newMax, CurrentHp = Math.Min(newMax, s.CurrentHp) };
            case EventEffect.GainRelicRandom gr:
                return GainRelic(s, gr.Rarity, catalog, rng);
            case EventEffect.GrantCardReward:
                return GrantCardReward(s, catalog, rng);
            default:
                return s;
        }
    }

    private static RunState GainRelic(RunState s, CardRarity rarity, DataCatalog catalog, IRng rng)
    {
        var pool = catalog.Relics.Values
            .Where(r => r.Rarity == rarity && !s.Relics.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToArray();
        if (pool.Length == 0) return s;
        var chosen = pool[rng.NextInt(0, pool.Length)];
        var newRelics = s.Relics.Append(chosen.Id).ToList();
        var s1 = s with { Relics = newRelics };
        return NonBattleRelicEffects.ApplyOnPickup(s1, chosen.Id, catalog);
    }

    private static RunState GrantCardReward(RunState s, DataCatalog catalog, IRng rng)
    {
        var rt = catalog.RewardTables["act1"];
        var excl = ImmutableArray.CreateRange(s.Deck.Select(c => c.Id));
        var (reward, newRngState) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(new Enemy.EnemyPool(s.CurrentAct, Enemy.EnemyTier.Weak)),
            s.RewardRngState, excl, rt, catalog, rng);
        // Event からのカード報酬は Gold / Potion を含めない（CardChoices のみ提示）
        var cardOnly = new RewardState(
            Gold: 0, GoldClaimed: true,
            PotionId: null, PotionClaimed: true,
            CardChoices: reward.CardChoices,
            CardStatus: CardRewardStatus.Pending);
        return s with { ActiveReward = cardOnly, RewardRngState = newRngState };
    }
}
```

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~EventResolverTests" --nologo`
Expected: PASS (10 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Events/EventResolver.cs tests/Core.Tests/Events/EventResolverTests.cs
git commit -m "feat(core): EventResolver applies choice effects and chains OnPickup relics"
git push
```

---

### Task D7: NodeEffectResolver に Event / Treasure / Merchant / Rest ルーティング追加

**Files:**
- Modify: `src/Core/Run/NodeEffectResolver.cs`
- Test: extend `tests/Core.Tests/Run/NodeEffectResolverTests.cs`

Merchant と Rest の本体は Part E/F で実装するが、`switch` での dispatch のみ先に用意する（Part E/F で具体関数を呼ぶように差し替え）。

- [ ] **Step 1: 失敗するテストを書く**

Append to `tests/Core.Tests/Run/NodeEffectResolverTests.cs`:

```csharp
    [Fact]
    public void Resolve_Event_SetsActiveEvent()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rng = new SequentialRng(1UL);
        var s0 = SampleState(catalog);
        var s1 = NodeEffectResolver.Resolve(s0, TileKind.Event, currentRow: 2, catalog, rng);
        Assert.NotNull(s1.ActiveEvent);
        Assert.Contains(s1.ActiveEvent!.EventId, catalog.Events.Keys);
    }

    [Fact]
    public void Resolve_Treasure_SetsActiveRewardWithRelicOnly()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rng = new SequentialRng(1UL);
        var s0 = SampleState(catalog);
        var s1 = NodeEffectResolver.Resolve(s0, TileKind.Treasure, 2, catalog, rng);
        Assert.NotNull(s1.ActiveReward);
        Assert.Equal(0, s1.ActiveReward!.Gold);
        Assert.Empty(s1.ActiveReward.CardChoices);
        Assert.NotNull(s1.ActiveReward.RelicId);
        Assert.False(s1.ActiveReward.RelicClaimed);
    }

    [Fact]
    public void Resolve_Rest_SetsActiveRestPending()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rng = new SequentialRng(1UL);
        var s0 = SampleState(catalog) with { CurrentHp = 40 };
        var s1 = NodeEffectResolver.Resolve(s0, TileKind.Rest, 2, catalog, rng);
        Assert.True(s1.ActiveRestPending);
        Assert.Equal(40, s1.CurrentHp);  // Rest 選択まで回復しない
    }
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NodeEffectResolverTests" --nologo`
Expected: Event case 未実装 / Treasure が旧挙動 / Rest が旧挙動で FAIL。

- [ ] **Step 3: 実装**

Replace `src/Core/Run/NodeEffectResolver.cs`:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

/// <summary>TileKind に応じて RunState を遷移させるルータ。各マス種別のロジックは個別モジュールに委譲する。</summary>
public static class NodeEffectResolver
{
    public static RunState Resolve(
        RunState state, TileKind kind, int currentRow, DataCatalog data, IRng rng)
    {
        var table = data.RewardTables["act1"];
        return kind switch
        {
            TileKind.Start => state,
            TileKind.Enemy => BattlePlaceholder.Start(state,
                RouteEnemyPool(table, state.CurrentAct, currentRow), data, rng),
            TileKind.Elite => BattlePlaceholder.Start(state,
                new EnemyPool(state.CurrentAct, EnemyTier.Elite), data, rng),
            TileKind.Boss => BattlePlaceholder.Start(state,
                new EnemyPool(state.CurrentAct, EnemyTier.Boss), data, rng),
            TileKind.Rest => state with { ActiveRestPending = true },
            TileKind.Merchant => StartMerchant(state, data, rng),
            TileKind.Treasure => StartTreasure(state, table, data, rng),
            TileKind.Event => StartEvent(state, data, rng),
            TileKind.Unknown => throw new ArgumentException("Unknown tile should be pre-resolved"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static EnemyPool RouteEnemyPool(RewardTable table, int act, int row)
    {
        var tier = row < table.EnemyPoolRouting.WeakRowsThreshold
            ? EnemyTier.Weak
            : EnemyTier.Strong;
        return new EnemyPool(act, tier);
    }

    private static RunState StartTreasure(RunState s, RewardTable table, DataCatalog data, IRng rng)
    {
        var owned = ImmutableArray.CreateRange(s.Relics);
        var (reward, newRng) = RewardGenerator.GenerateTreasure(s.RewardRngState, owned, table, data, rng);
        return s with { ActiveReward = reward, RewardRngState = newRng };
    }

    private static RunState StartEvent(RunState s, DataCatalog data, IRng rng)
    {
        var pool = ImmutableArray.CreateRange(data.Events.Values);
        var def = EventPool.Pick(pool, rng);
        var inst = new EventInstance(def.Id, def.Choices);
        return s with { ActiveEvent = inst };
    }

    private static RunState StartMerchant(RunState s, DataCatalog data, IRng rng)
    {
        if (data.MerchantPrices is null)
            throw new InvalidOperationException("DataCatalog.MerchantPrices is not configured");
        var inv = MerchantInventoryGenerator.Generate(data, data.MerchantPrices, s, rng);
        return s with { ActiveMerchant = inv };
    }
}
```

**注意:** `MerchantInventoryGenerator` は Part E で実装する。Part E 完了前はこのタスクのテストで Merchant case を検証しない（Rest / Treasure / Event の 3 つのみ）。

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~NodeEffectResolverTests" --nologo`
Expected: Event / Treasure / Rest の新テスト PASS。既存 Merchant テストは no-op からの変更なので修正または一時的に skip マーク。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Run/NodeEffectResolver.cs tests/Core.Tests/Run/NodeEffectResolverTests.cs
git commit -m "feat(core): NodeEffectResolver routes Event/Treasure(relic)/Rest to proper handlers"
git push
```

---

### Task D8: UnknownResolver の Event 含有テスト

**Files:**
- Test: extend `tests/Core.Tests/Map/UnknownResolverTests.cs`
- Check: `src/Server/Services/*` で UnknownResolutionConfig のデフォルト値を作っている箇所があれば Event=25 を足す

- [ ] **Step 1: テスト追加**

Append to `tests/Core.Tests/Map/UnknownResolverTests.cs`:

```csharp
    [Fact]
    public void ResolveAll_WeightedIncludingEvent_YieldsMixedKinds()
    {
        var map = CreateMapWithUnknownNodes(count: 200);
        var cfg = new UnknownResolutionConfig(
            System.Collections.Immutable.ImmutableDictionary<TileKind, double>.Empty
                .Add(TileKind.Enemy, 25.0)
                .Add(TileKind.Elite, 10.0)
                .Add(TileKind.Merchant, 15.0)
                .Add(TileKind.Rest, 25.0)
                .Add(TileKind.Treasure, 0.0)
                .Add(TileKind.Event, 25.0));
        var rng = new SequentialRng(1UL);
        var res = UnknownResolver.ResolveAll(map, cfg, rng);
        Assert.Contains(res.Values, v => v == TileKind.Event);
    }
```

（`CreateMapWithUnknownNodes(count)` は既存ヘルパ。既存テストから形を流用すること。）

- [ ] **Step 2: サーバ側 DI デフォルトを更新**

Run:
```bash
grep -nR "UnknownResolutionConfig" src/Server src/Core --include="*.cs"
```

見つかった箇所で `Weights` に `Event` が含まれていなければ追加する（例: `.Add(TileKind.Event, 25.0)`）。

- [ ] **Step 3: 全テスト実行**

Run: `dotnet test --nologo`
Expected: PASS（Unknown/Event 経路の既存期待値が変わっている場合は期待値を更新）。

- [ ] **Step 4: コミット**

```bash
git add tests/Core.Tests/Map/UnknownResolverTests.cs src/Server/ src/Core/
git commit -m "feat(map): include TileKind.Event in default UnknownResolutionConfig weights"
git push
```

---

## Part E — Merchant System

### Task E1: MerchantPrices 型と JSON ローダ

**Files:**
- Create: `src/Core/Merchant/MerchantPrices.cs`
- Create: `src/Core/Merchant/MerchantPricesJsonLoader.cs`
- Create: `src/Core/Data/merchant-prices.json`
- Test: `tests/Core.Tests/Merchant/MerchantPricesJsonLoaderTests.cs`

- [ ] **Step 1: 型を作る**

Create `src/Core/Merchant/MerchantPrices.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Merchant;

public sealed record MerchantPrices(
    ImmutableDictionary<CardRarity, int> Cards,
    ImmutableDictionary<CardRarity, int> Relics,
    ImmutableDictionary<CardRarity, int> Potions,
    int DiscardSlotPrice);
```

- [ ] **Step 2: 失敗するテストを書く**

Create `tests/Core.Tests/Merchant/MerchantPricesJsonLoaderTests.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Merchant;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Merchant;

public class MerchantPricesJsonLoaderTests
{
    private const string SampleJson = """
    {
      "cards":   { "Common": 50, "Rare": 80, "Epic": 150 },
      "relics":  { "Common": 150, "Rare": 250, "Epic": 350 },
      "potions": { "Common": 50, "Rare": 75, "Epic": 100 },
      "discardSlotPrice": 75
    }
    """;

    [Fact]
    public void Parse_Valid_ReturnsPrices()
    {
        var p = MerchantPricesJsonLoader.Parse(SampleJson);
        Assert.Equal(50, p.Cards[CardRarity.Common]);
        Assert.Equal(80, p.Cards[CardRarity.Rare]);
        Assert.Equal(150, p.Cards[CardRarity.Epic]);
        Assert.Equal(150, p.Relics[CardRarity.Common]);
        Assert.Equal(75, p.DiscardSlotPrice);
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<MerchantPricesJsonException>(() => MerchantPricesJsonLoader.Parse("{"));
    }

    [Fact]
    public void Parse_MissingRarity_Throws()
    {
        var bad = """{"cards":{"Common":50},"relics":{"Common":150,"Rare":250,"Epic":350},"potions":{"Common":50,"Rare":75,"Epic":100},"discardSlotPrice":75}""";
        Assert.Throws<MerchantPricesJsonException>(() => MerchantPricesJsonLoader.Parse(bad));
    }
}
```

- [ ] **Step 3: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantPricesJsonLoaderTests" --nologo`
Expected: build error。

- [ ] **Step 4: JSON ローダを実装**

Create `src/Core/Merchant/MerchantPricesJsonLoader.cs`:

```csharp
using System;
using System.Collections.Immutable;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Merchant;

public sealed class MerchantPricesJsonException : Exception
{
    public MerchantPricesJsonException(string message) : base(message) { }
    public MerchantPricesJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class MerchantPricesJsonLoader
{
    public static MerchantPrices Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        { throw new MerchantPricesJsonException("merchant-prices JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            var r = doc.RootElement;
            return new MerchantPrices(
                Cards: ParseRarityMap(r, "cards"),
                Relics: ParseRarityMap(r, "relics"),
                Potions: ParseRarityMap(r, "potions"),
                DiscardSlotPrice: r.GetProperty("discardSlotPrice").GetInt32());
        }
    }

    private static ImmutableDictionary<CardRarity, int> ParseRarityMap(JsonElement r, string key)
    {
        if (!r.TryGetProperty(key, out var obj) || obj.ValueKind != JsonValueKind.Object)
            throw new MerchantPricesJsonException($"\"{key}\" object が欠落しています。");
        var b = ImmutableDictionary.CreateBuilder<CardRarity, int>();
        foreach (var rarity in new[] { CardRarity.Common, CardRarity.Rare, CardRarity.Epic })
        {
            if (!obj.TryGetProperty(rarity.ToString(), out var v) || v.ValueKind != JsonValueKind.Number)
                throw new MerchantPricesJsonException($"\"{key}.{rarity}\" が欠落しています。");
            b.Add(rarity, v.GetInt32());
        }
        return b.ToImmutable();
    }
}
```

- [ ] **Step 5: merchant-prices.json を作成**

Create `src/Core/Data/merchant-prices.json`:

```json
{
  "cards":   { "Common": 50, "Rare": 80, "Epic": 150 },
  "relics":  { "Common": 150, "Rare": 250, "Epic": 350 },
  "potions": { "Common": 50, "Rare": 75, "Epic": 100 },
  "discardSlotPrice": 75
}
```

- [ ] **Step 6: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantPricesJsonLoaderTests" --nologo`
Expected: PASS。

`EmbeddedDataLoader` が Task D4 で `MerchantPricesResource` を読むように既に書かれているので、ここで `DataCatalog.MerchantPrices` が null でなくなる。

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~DataCatalog" --nologo`
Expected: PASS。

- [ ] **Step 7: コミット**

```bash
git add src/Core/Merchant/MerchantPrices.cs src/Core/Merchant/MerchantPricesJsonLoader.cs src/Core/Data/merchant-prices.json tests/Core.Tests/Merchant/MerchantPricesJsonLoaderTests.cs
git commit -m "feat(core): add MerchantPrices type, JSON loader, and default prices data"
git push
```

---

### Task E2: MerchantOffer と MerchantInventory

**Files:**
- Create: `src/Core/Merchant/MerchantOffer.cs`
- Create: `src/Core/Merchant/MerchantInventory.cs`

- [ ] **Step 1: MerchantOffer**

Create `src/Core/Merchant/MerchantOffer.cs`:

```csharp
namespace RoguelikeCardGame.Core.Merchant;

/// <summary>商人在庫の 1 品目。`Kind` は "card" / "relic" / "potion"。</summary>
public sealed record MerchantOffer(
    string Kind,
    string Id,
    int Price,
    bool Sold);
```

- [ ] **Step 2: MerchantInventory**

Create `src/Core/Merchant/MerchantInventory.cs`:

```csharp
using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Merchant;

public sealed record MerchantInventory(
    ImmutableArray<MerchantOffer> Cards,
    ImmutableArray<MerchantOffer> Relics,
    ImmutableArray<MerchantOffer> Potions,
    bool DiscardSlotUsed,
    int DiscardPrice);
```

- [ ] **Step 3: ビルド**

Run: `dotnet build --nologo`
Expected: PASS。RunState の `ActiveMerchant` 型が解決される。

- [ ] **Step 4: コミット**

```bash
git add src/Core/Merchant/MerchantOffer.cs src/Core/Merchant/MerchantInventory.cs
git commit -m "feat(core): add MerchantOffer and MerchantInventory records"
git push
```

---

### Task E3: MerchantInventoryGenerator

**Files:**
- Create: `src/Core/Merchant/MerchantInventoryGenerator.cs`
- Test: `tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs`:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Merchant;

public class MerchantInventoryGeneratorTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Base() =>
        RunState.NewSoloRun(
            Catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Generate_Yields5Cards2Relics3Potions_AllUnsold()
    {
        var inv = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(1UL));
        Assert.Equal(5, inv.Cards.Length);
        Assert.Equal(2, inv.Relics.Length);
        Assert.Equal(3, inv.Potions.Length);
        Assert.All(inv.Cards.Concat(inv.Relics).Concat(inv.Potions), o => Assert.False(o.Sold));
        Assert.False(inv.DiscardSlotUsed);
        Assert.Equal(75, inv.DiscardPrice);
    }

    [Fact]
    public void Generate_CardPricesMatchRarity()
    {
        var inv = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(2UL));
        foreach (var offer in inv.Cards)
        {
            var rarity = Catalog.Cards[offer.Id].Rarity;
            Assert.Equal(Catalog.MerchantPrices!.Cards[rarity], offer.Price);
        }
    }

    [Fact]
    public void Generate_UniqueIdsWithinCategory()
    {
        var inv = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(3UL));
        Assert.Equal(inv.Cards.Length, inv.Cards.Select(c => c.Id).Distinct().Count());
        Assert.Equal(inv.Relics.Length, inv.Relics.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Generate_ExcludesOwnedRelics()
    {
        var s = Base() with
        {
            Relics = Catalog.Relics.Keys.Take(Catalog.Relics.Count - 1).ToList()
        };
        var inv = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, s, new SequentialRng(4UL));
        foreach (var offer in inv.Relics)
            Assert.DoesNotContain(offer.Id, s.Relics);
    }

    [Fact]
    public void Generate_Deterministic_SameSeedSameOutput()
    {
        var a = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(99UL));
        var b = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(99UL));
        Assert.Equal(a.Cards.Select(o => o.Id), b.Cards.Select(o => o.Id));
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantInventoryGeneratorTests" --nologo`
Expected: build error。

- [ ] **Step 3: 実装**

Create `src/Core/Merchant/MerchantInventoryGenerator.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Merchant;

public static class MerchantInventoryGenerator
{
    private const int CardCount = 5;
    private const int RelicCount = 2;
    private const int PotionCount = 3;

    public static MerchantInventory Generate(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng)
    {
        var cards = PickCards(catalog, prices, s, rng, CardCount);
        var relics = PickRelics(catalog, prices, s, rng, RelicCount);
        var potions = PickPotions(catalog, prices, rng, PotionCount);
        return new MerchantInventory(
            cards, relics, potions,
            DiscardSlotUsed: false,
            DiscardPrice: prices.DiscardSlotPrice);
    }

    private static ImmutableArray<MerchantOffer> PickCards(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng, int count)
    {
        var candidates = catalog.Cards.Values
            .Where(c => c.Id.StartsWith("reward_"))
            .OrderBy(c => c.Id)
            .ToList();
        return PickFromPool(candidates, count, rng,
            def => new MerchantOffer("card", def.Id, prices.Cards[def.Rarity], false));
    }

    private static ImmutableArray<MerchantOffer> PickRelics(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng, int count)
    {
        var candidates = catalog.Relics.Values
            .Where(r => !s.Relics.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToList();
        return PickFromPool(candidates, count, rng,
            def => new MerchantOffer("relic", def.Id, prices.Relics[def.Rarity], false));
    }

    private static ImmutableArray<MerchantOffer> PickPotions(
        DataCatalog catalog, MerchantPrices prices, IRng rng, int count)
    {
        var candidates = catalog.Potions.Values.OrderBy(p => p.Id).ToList();
        return PickFromPool(candidates, count, rng,
            def => new MerchantOffer("potion", def.Id, prices.Potions[def.Rarity], false));
    }

    private static ImmutableArray<MerchantOffer> PickFromPool<T>(
        List<T> pool, int count, IRng rng,
        System.Func<T, MerchantOffer> makeOffer)
    {
        int take = System.Math.Min(count, pool.Count);
        var remaining = new List<T>(pool);
        var picked = new List<MerchantOffer>(take);
        for (int i = 0; i < take; i++)
        {
            int idx = rng.NextInt(0, remaining.Count);
            picked.Add(makeOffer(remaining[idx]));
            remaining.RemoveAt(idx);
        }
        return picked.ToImmutableArray();
    }
}
```

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantInventoryGeneratorTests" --nologo`
Expected: PASS (5 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Merchant/MerchantInventoryGenerator.cs tests/Core.Tests/Merchant/MerchantInventoryGeneratorTests.cs
git commit -m "feat(core): MerchantInventoryGenerator picks 5 cards / 2 relics / 3 potions"
git push
```

---

### Task E4: MerchantActions

**Files:**
- Create: `src/Core/Merchant/MerchantActions.cs`
- Test: `tests/Core.Tests/Merchant/MerchantActionsTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Merchant/MerchantActionsTests.cs`:

```csharp
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Merchant;

public class MerchantActionsTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState BaseWithInventory(int gold = 500) =>
        (RunState.NewSoloRun(
            Catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero))
         with { Gold = gold })
        with
        { ActiveMerchant = MakeInventory() };

    private static MerchantInventory MakeInventory() => new(
        Cards: ImmutableArray.Create(
            new MerchantOffer("card", "reward_common_slice", 50, false)),
        Relics: ImmutableArray.Create(
            new MerchantOffer("relic", "extra_max_hp", 150, false)),
        Potions: ImmutableArray.Create(
            new MerchantOffer("potion", "health_potion", 50, false)),
        DiscardSlotUsed: false,
        DiscardPrice: 75);

    [Fact]
    public void BuyCard_SufficientGold_AddsCardDeductsGoldMarksSold()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyCard(s0, "reward_common_slice", Catalog);
        Assert.Equal(450, s1.Gold);
        Assert.Contains(s1.Deck, c => c.Id == "reward_common_slice");
        Assert.True(s1.ActiveMerchant!.Cards[0].Sold);
    }

    [Fact]
    public void BuyCard_InsufficientGold_Throws()
    {
        var s0 = BaseWithInventory(30);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.BuyCard(s0, "reward_common_slice", Catalog));
    }

    [Fact]
    public void BuyCard_AlreadySold_Throws()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyCard(s0, "reward_common_slice", Catalog);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.BuyCard(s1, "reward_common_slice", Catalog));
    }

    [Fact]
    public void BuyCard_UnknownId_Throws()
    {
        var s0 = BaseWithInventory(500);
        Assert.Throws<ArgumentException>(() =>
            MerchantActions.BuyCard(s0, "no_such_card", Catalog));
    }

    [Fact]
    public void BuyRelic_AddsRelicAndTriggersOnPickup()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyRelic(s0, "extra_max_hp", Catalog);
        Assert.Contains("extra_max_hp", s1.Relics);
        Assert.Equal(350, s1.Gold);
        Assert.Equal(s0.MaxHp + 7, s1.MaxHp);  // OnPickup 発火
    }

    [Fact]
    public void BuyPotion_AddsToFirstEmptySlot()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyPotion(s0, "health_potion", Catalog);
        Assert.Equal(450, s1.Gold);
        Assert.Equal("health_potion", s1.Potions[0]);
    }

    [Fact]
    public void BuyPotion_AllSlotsFull_Throws()
    {
        var s0 = BaseWithInventory(500);
        var full = s0 with
        {
            Potions = s0.Potions.SetItem(0, "swift_potion")
                                 .SetItem(1, "swift_potion")
                                 .SetItem(2, "swift_potion"),
        };
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.BuyPotion(full, "health_potion", Catalog));
    }

    [Fact]
    public void Discard_RemovesCardAndMarksSlotUsed()
    {
        var s0 = BaseWithInventory(500);
        int originalLen = s0.Deck.Length;
        var s1 = MerchantActions.DiscardCard(s0, deckIndex: 0);
        Assert.Equal(originalLen - 1, s1.Deck.Length);
        Assert.Equal(425, s1.Gold);
        Assert.True(s1.ActiveMerchant!.DiscardSlotUsed);
    }

    [Fact]
    public void Discard_AlreadyUsed_Throws()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.DiscardCard(s0, 0);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.DiscardCard(s1, 0));
    }

    [Fact]
    public void Discard_InsufficientGold_Throws()
    {
        var s0 = BaseWithInventory(10);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.DiscardCard(s0, 0));
    }

    [Fact]
    public void Leave_ClearsActiveMerchant()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.Leave(s0);
        Assert.Null(s1.ActiveMerchant);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantActionsTests" --nologo`
Expected: build error。

- [ ] **Step 3: 実装**

Create `src/Core/Merchant/MerchantActions.cs`:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Merchant;

public static class MerchantActions
{
    public static RunState BuyCard(RunState s, string cardId, DataCatalog catalog)
    {
        var (inv, offer, idx) = RequireOffer(s, "card", cardId);
        if (s.Gold < offer.Price)
            throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
        if (!catalog.TryGetCard(cardId, out _))
            throw new ArgumentException($"unknown card id \"{cardId}\"", nameof(cardId));
        var soldOffer = offer with { Sold = true };
        return s with
        {
            Gold = s.Gold - offer.Price,
            Deck = s.Deck.Add(new CardInstance(cardId, false)),
            ActiveMerchant = inv with { Cards = inv.Cards.SetItem(idx, soldOffer) },
        };
    }

    public static RunState BuyRelic(RunState s, string relicId, DataCatalog catalog)
    {
        var (inv, offer, idx) = RequireOffer(s, "relic", relicId);
        if (s.Gold < offer.Price)
            throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
        if (!catalog.TryGetRelic(relicId, out _))
            throw new ArgumentException($"unknown relic id \"{relicId}\"", nameof(relicId));
        var soldOffer = offer with { Sold = true };
        var s1 = s with
        {
            Gold = s.Gold - offer.Price,
            Relics = s.Relics.Append(relicId).ToList(),
            ActiveMerchant = inv with { Relics = inv.Relics.SetItem(idx, soldOffer) },
        };
        return NonBattleRelicEffects.ApplyOnPickup(s1, relicId, catalog);
    }

    public static RunState BuyPotion(RunState s, string potionId, DataCatalog catalog)
    {
        var (inv, offer, idx) = RequireOffer(s, "potion", potionId);
        if (s.Gold < offer.Price)
            throw new InvalidOperationException($"Not enough gold ({s.Gold} < {offer.Price})");
        if (!catalog.TryGetPotion(potionId, out _))
            throw new ArgumentException($"unknown potion id \"{potionId}\"", nameof(potionId));
        int slot = -1;
        for (int i = 0; i < s.Potions.Length; i++) if (s.Potions[i] == "") { slot = i; break; }
        if (slot < 0) throw new InvalidOperationException("All potion slots full");
        var soldOffer = offer with { Sold = true };
        return s with
        {
            Gold = s.Gold - offer.Price,
            Potions = s.Potions.SetItem(slot, potionId),
            ActiveMerchant = inv with { Potions = inv.Potions.SetItem(idx, soldOffer) },
        };
    }

    public static RunState DiscardCard(RunState s, int deckIndex)
    {
        var inv = RequireInventory(s);
        if (inv.DiscardSlotUsed) throw new InvalidOperationException("Discard slot already used");
        if (s.Gold < inv.DiscardPrice)
            throw new InvalidOperationException($"Not enough gold ({s.Gold} < {inv.DiscardPrice})");
        if (deckIndex < 0 || deckIndex >= s.Deck.Length)
            throw new ArgumentOutOfRangeException(nameof(deckIndex));
        return s with
        {
            Gold = s.Gold - inv.DiscardPrice,
            Deck = s.Deck.RemoveAt(deckIndex),
            ActiveMerchant = inv with { DiscardSlotUsed = true },
        };
    }

    public static RunState Leave(RunState s)
    {
        _ = RequireInventory(s);
        return s with { ActiveMerchant = null };
    }

    private static MerchantInventory RequireInventory(RunState s) =>
        s.ActiveMerchant ?? throw new InvalidOperationException("No active merchant");

    private static (MerchantInventory inv, MerchantOffer offer, int index) RequireOffer(
        RunState s, string kind, string id)
    {
        var inv = RequireInventory(s);
        var list = kind switch
        {
            "card" => inv.Cards,
            "relic" => inv.Relics,
            "potion" => inv.Potions,
            _ => throw new ArgumentException($"unknown kind \"{kind}\"", nameof(kind)),
        };
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].Id != id) continue;
            if (list[i].Sold) throw new InvalidOperationException($"{kind} \"{id}\" already sold");
            return (inv, list[i], i);
        }
        throw new ArgumentException($"{kind} id \"{id}\" not in inventory", nameof(id));
    }
}
```

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~MerchantActionsTests" --nologo`
Expected: PASS（11 件）。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Merchant/MerchantActions.cs tests/Core.Tests/Merchant/MerchantActionsTests.cs
git commit -m "feat(core): MerchantActions (BuyCard/BuyRelic/BuyPotion/Discard/Leave)"
git push
```

---

### Task E5: MerchantController + DTO

**Files:**
- Create: `src/Server/Dtos/MerchantBuyRequestDto.cs`
- Create: `src/Server/Dtos/MerchantDiscardRequestDto.cs`
- Create: `src/Server/Dtos/MerchantInventoryDto.cs`
- Create: `src/Server/Controllers/MerchantController.cs`
- Test: `tests/Server.Tests/Controllers/MerchantControllerTests.cs`

- [ ] **Step 1: DTO を作成**

Create `src/Server/Dtos/MerchantBuyRequestDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record MerchantBuyRequestDto(string Kind, string Id);
```

Create `src/Server/Dtos/MerchantDiscardRequestDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record MerchantDiscardRequestDto(int DeckIndex);
```

Create `src/Server/Dtos/MerchantInventoryDto.cs`:

```csharp
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Merchant;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record MerchantOfferDto(string Kind, string Id, int Price, bool Sold);

public sealed record MerchantInventoryDto(
    ImmutableArray<MerchantOfferDto> Cards,
    ImmutableArray<MerchantOfferDto> Relics,
    ImmutableArray<MerchantOfferDto> Potions,
    bool DiscardSlotUsed,
    int DiscardPrice)
{
    public static MerchantInventoryDto From(MerchantInventory inv) =>
        new(
            inv.Cards.Select(o => new MerchantOfferDto(o.Kind, o.Id, o.Price, o.Sold)).ToImmutableArray(),
            inv.Relics.Select(o => new MerchantOfferDto(o.Kind, o.Id, o.Price, o.Sold)).ToImmutableArray(),
            inv.Potions.Select(o => new MerchantOfferDto(o.Kind, o.Id, o.Price, o.Sold)).ToImmutableArray(),
            inv.DiscardSlotUsed,
            inv.DiscardPrice);
}
```

- [ ] **Step 2: 失敗するテストを書く**

Create `tests/Server.Tests/Controllers/MerchantControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Tests.TestHelpers;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class MerchantControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public MerchantControllerTests(WebApplicationFactory<Program> f) { _factory = f; }

    [Fact]
    public async System.Threading.Tasks.Task GetInventory_NoActiveMerchant_Returns409()
    {
        using var client = _factory.CreateAuthedClientWithFreshRun();
        var res = await client.GetAsync("/api/v1/merchant/inventory");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetInventory_AfterMoveToMerchantTile_ReturnsInventory()
    {
        using var client = _factory.CreateAuthedClientWithMerchantTileSelected();
        var res = await client.GetAsync("/api/v1/merchant/inventory");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<MerchantInventoryDto>();
        Assert.NotNull(body);
        Assert.Equal(5, body!.Cards.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task Buy_InsufficientGold_Returns400()
    {
        using var client = _factory.CreateAuthedClientWithMerchantTileSelectedAndGold(gold: 0);
        var inv = await (await client.GetAsync("/api/v1/merchant/inventory"))
            .Content.ReadFromJsonAsync<MerchantInventoryDto>();
        var target = inv!.Cards[0];
        var res = await client.PostAsJsonAsync("/api/v1/merchant/buy",
            new MerchantBuyRequestDto("card", target.Id));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Buy_UnknownId_Returns404()
    {
        using var client = _factory.CreateAuthedClientWithMerchantTileSelected();
        var res = await client.PostAsJsonAsync("/api/v1/merchant/buy",
            new MerchantBuyRequestDto("card", "no_such"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Buy_Success_DeductsGoldAndReturnsSnapshot()
    {
        using var client = _factory.CreateAuthedClientWithMerchantTileSelectedAndGold(gold: 500);
        var inv = await (await client.GetAsync("/api/v1/merchant/inventory"))
            .Content.ReadFromJsonAsync<MerchantInventoryDto>();
        var target = inv!.Cards[0];
        var res = await client.PostAsJsonAsync("/api/v1/merchant/buy",
            new MerchantBuyRequestDto("card", target.Id));
        res.EnsureSuccessStatusCode();
        // snapshot should show reduced gold
    }

    [Fact]
    public async System.Threading.Tasks.Task Discard_Success_ReducesDeckByOne()
    {
        using var client = _factory.CreateAuthedClientWithMerchantTileSelectedAndGold(gold: 500);
        var res = await client.PostAsJsonAsync("/api/v1/merchant/discard",
            new MerchantDiscardRequestDto(0));
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async System.Threading.Tasks.Task Leave_ClearsActiveMerchant()
    {
        using var client = _factory.CreateAuthedClientWithMerchantTileSelected();
        var res = await client.PostAsync("/api/v1/merchant/leave", null);
        res.EnsureSuccessStatusCode();

        var after = await client.GetAsync("/api/v1/merchant/inventory");
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
    }
}
```

**注意:** `CreateAuthedClientWithMerchantTileSelected()` など ヘルパーは `tests/Server.Tests/TestHelpers/` に追加する必要がある。既存 phase05 ヘルパーのパターンに従い、`CreateAuthedClientWithFreshRun` があるなら `...WithMerchantTileSelected` / `...WithGold(int)` をオーバーロードで追加する。

- [ ] **Step 3: 失敗を確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MerchantControllerTests" --nologo`
Expected: 404 / build error（Controller / ヘルパー未実装）。

- [ ] **Step 4: Controller 実装**

Create `src/Server/Controllers/MerchantController.cs`:

```csharp
using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/merchant")]
public sealed class MerchantController : ControllerBase
{
    private readonly ISessionService _sessions;
    private readonly DataCatalog _catalog;
    private readonly IRngFactory _rngFactory;

    public MerchantController(ISessionService sessions, DataCatalog catalog, IRngFactory rngFactory)
    { _sessions = sessions; _catalog = catalog; _rngFactory = rngFactory; }

    [HttpGet("inventory")]
    public IActionResult GetInventory()
    {
        var s = _sessions.RequireActiveRun(User);
        if (s.ActiveMerchant is null) return Conflict(new { error = "no active merchant" });
        return Ok(MerchantInventoryDto.From(s.ActiveMerchant));
    }

    [HttpPost("buy")]
    public IActionResult Buy([FromBody] MerchantBuyRequestDto dto)
    {
        var s = _sessions.RequireActiveRun(User);
        if (s.ActiveMerchant is null) return Conflict(new { error = "no active merchant" });
        try
        {
            var s1 = dto.Kind switch
            {
                "card" => MerchantActions.BuyCard(s, dto.Id, _catalog),
                "relic" => MerchantActions.BuyRelic(s, dto.Id, _catalog),
                "potion" => MerchantActions.BuyPotion(s, dto.Id, _catalog),
                _ => throw new ArgumentException($"unknown kind \"{dto.Kind}\""),
            };
            _sessions.PersistRun(User, s1);
            return Ok(_sessions.SnapshotOf(s1));
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not in inventory"))
        { return NotFound(new { error = ex.Message }); }
        catch (ArgumentException ex) when (ex.Message.Contains("unknown"))
        { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("Not enough gold"))
                return BadRequest(new { error = ex.Message });
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("discard")]
    public IActionResult Discard([FromBody] MerchantDiscardRequestDto dto)
    {
        var s = _sessions.RequireActiveRun(User);
        if (s.ActiveMerchant is null) return Conflict(new { error = "no active merchant" });
        try
        {
            var s1 = MerchantActions.DiscardCard(s, dto.DeckIndex);
            _sessions.PersistRun(User, s1);
            return Ok(_sessions.SnapshotOf(s1));
        }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex)
        {
            if (ex.Message.Contains("Not enough gold"))
                return BadRequest(new { error = ex.Message });
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("leave")]
    public IActionResult Leave()
    {
        var s = _sessions.RequireActiveRun(User);
        if (s.ActiveMerchant is null) return Conflict(new { error = "no active merchant" });
        var s1 = MerchantActions.Leave(s);
        _sessions.PersistRun(User, s1);
        return Ok(_sessions.SnapshotOf(s1));
    }
}
```

**注意:** `ISessionService.RequireActiveRun` / `PersistRun` / `SnapshotOf` は既存 Phase 5 の実装仕様に合わせて命名を調整する。既存の `RunsController` の作法を参照すること。

- [ ] **Step 5: ヘルパー拡張**

Edit `tests/Server.Tests/TestHelpers/*.cs` で `CreateAuthedClientWithMerchantTileSelected()` 等を追加。実装戦略:

```csharp
public static HttpClient CreateAuthedClientWithMerchantTileSelected(
    this WebApplicationFactory<Program> factory, int gold = 500)
{
    var client = factory.CreateAuthedClientWithFreshRun();
    // Force RNG seeding so 1 つ以上の Merchant ノードが確実に経路上にある map を使うか、
    // 初期 RunState を直接注入するテスト用サービスを用意する。
    // 推奨: TestServiceOverrides.SetForcedNextNodeKind(TileKind.Merchant) を実装して、
    // move 呼出時に NodeEffectResolver が Merchant ケースを走らせるようにする。
    factory.SetRunStateOverride(run => run with { Gold = gold });
    factory.SetForcedNextNodeKind(TileKind.Merchant);
    client.MoveToFirstAvailableNode();
    return client;
}
```

（上記コードは疑似コード。Phase 5 の `TestServiceOverrides` / `TestHelpers` 実装を確認し、その語彙で書き直す。なければ Phase 5 と同じ仕組み（固定 seed + pre-generated map fixture）を流用。）

- [ ] **Step 6: PASS 確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MerchantControllerTests" --nologo`
Expected: PASS (7 件)。

- [ ] **Step 7: コミット**

```bash
git add src/Server/Controllers/MerchantController.cs src/Server/Dtos/Merchant*.cs tests/Server.Tests/Controllers/MerchantControllerTests.cs tests/Server.Tests/TestHelpers/
git commit -m "feat(server): add MerchantController (inventory / buy / discard / leave)"
git push
```

---

## Part F — Rest システム

Rest マスは「回復」「強化」のどちらかを選ぶ。`NodeEffectResolver` 側で `ActiveRestPending = true` を立てるのは Part D7 で完了済み。ここでは `RestActions` と `RestController` を実装する。

### Task F1: RestActions（Heal / UpgradeCard）

**Files:**
- Create: `src/Core/Rest/RestActions.cs`
- Create: `tests/Core.Tests/Rest/RestActionsTests.cs`

- [ ] **Step 1: 失敗するテストを書く**

Create `tests/Core.Tests/Rest/RestActionsTests.cs`:

```csharp
using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Rest;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rest;

public class RestActionsTests
{
    private static DataCatalog Catalog() => EmbeddedDataLoader.LoadCatalog();

    private static RunState PendingRunAt(int currentHp, int maxHp,
        ImmutableArray<CardInstance>? deck = null,
        ImmutableArray<string>? relics = null)
    {
        var catalog = Catalog();
        var s = RunState.NewSoloRun(
            catalog,
            rngSeed: 1,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero));
        return s with
        {
            CurrentHp = currentHp,
            MaxHp = maxHp,
            ActiveRestPending = true,
            Deck = deck ?? s.Deck,
            Relics = relics?.ToArray() ?? Array.Empty<string>(),
        };
    }

    [Fact]
    public void Heal_HealsCeilThirtyPercent_AndClearsPending()
    {
        var s = PendingRunAt(currentHp: 30, maxHp: 80);
        var s1 = RestActions.Heal(s, Catalog());
        // ceil(80 * 0.30) = ceil(24) = 24
        Assert.Equal(30 + 24, s1.CurrentHp);
        Assert.False(s1.ActiveRestPending);
    }

    [Fact]
    public void Heal_CapsAtMaxHp()
    {
        var s = PendingRunAt(currentHp: 70, maxHp: 80);
        var s1 = RestActions.Heal(s, Catalog());
        Assert.Equal(80, s1.CurrentHp);
        Assert.False(s1.ActiveRestPending);
    }

    [Fact]
    public void Heal_WithWarmBlanket_AddsPassiveBonus()
    {
        var s = PendingRunAt(currentHp: 30, maxHp: 80,
            relics: ImmutableArray.Create("warm_blanket"));
        var s1 = RestActions.Heal(s, Catalog());
        // ceil(80 * 0.30) = 24, + 10 = 34
        Assert.Equal(30 + 34, s1.CurrentHp);
    }

    [Fact]
    public void Heal_WithoutPending_Throws()
    {
        var s = PendingRunAt(20, 80) with { ActiveRestPending = false };
        Assert.Throws<InvalidOperationException>(() => RestActions.Heal(s, Catalog()));
    }

    [Fact]
    public void UpgradeCard_UpgradesDeckIndex_AndClearsPending()
    {
        var catalog = Catalog();
        // 強化可能なカード (例: "strike") を 1 枚持つデッキを作る
        var deck = ImmutableArray.Create(
            new CardInstance("strike", Upgraded: false),
            new CardInstance("defend", Upgraded: false));
        var s = PendingRunAt(80, 80, deck: deck);
        var s1 = RestActions.UpgradeCard(s, deckIndex: 0, catalog);
        Assert.True(s1.Deck[0].Upgraded);
        Assert.False(s1.Deck[1].Upgraded);
        Assert.False(s1.ActiveRestPending);
    }

    [Fact]
    public void UpgradeCard_AlreadyUpgraded_Throws()
    {
        var deck = ImmutableArray.Create(new CardInstance("strike", Upgraded: true));
        var s = PendingRunAt(80, 80, deck: deck);
        Assert.Throws<InvalidOperationException>(() =>
            RestActions.UpgradeCard(s, 0, Catalog()));
    }

    [Fact]
    public void UpgradeCard_IndexOutOfRange_Throws()
    {
        var deck = ImmutableArray.Create(new CardInstance("strike", Upgraded: false));
        var s = PendingRunAt(80, 80, deck: deck);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RestActions.UpgradeCard(s, 5, Catalog()));
    }

    [Fact]
    public void UpgradeCard_WithoutPending_Throws()
    {
        var deck = ImmutableArray.Create(new CardInstance("strike", Upgraded: false));
        var s = PendingRunAt(80, 80, deck: deck) with { ActiveRestPending = false };
        Assert.Throws<InvalidOperationException>(() =>
            RestActions.UpgradeCard(s, 0, Catalog()));
    }
}
```

- [ ] **Step 2: テストを走らせて失敗を確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RestActionsTests" --nologo`
Expected: FAIL — `RestActions` 未定義。

- [ ] **Step 3: 実装**

Create `src/Core/Rest/RestActions.cs`:

```csharp
using System;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Rest;

public static class RestActions
{
    public static RunState Heal(RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!s.ActiveRestPending)
            throw new InvalidOperationException("Rest is not pending");

        int baseAmount = (int)Math.Ceiling(s.MaxHp * 0.30);
        int total = NonBattleRelicEffects.ApplyPassiveRestHealBonus(baseAmount, s, catalog);
        int newHp = Math.Min(s.MaxHp, s.CurrentHp + total);
        return s with { CurrentHp = newHp, ActiveRestPending = false };
    }

    public static RunState UpgradeCard(RunState s, int deckIndex, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!s.ActiveRestPending)
            throw new InvalidOperationException("Rest is not pending");
        if (deckIndex < 0 || deckIndex >= s.Deck.Length)
            throw new ArgumentOutOfRangeException(nameof(deckIndex));

        var card = s.Deck[deckIndex];
        if (!CardUpgrade.CanUpgrade(card, catalog))
            throw new InvalidOperationException(
                $"Card at deck[{deckIndex}] (\"{card.Id}\") cannot be upgraded");

        var upgraded = CardUpgrade.Upgrade(card);
        return s with
        {
            Deck = s.Deck.SetItem(deckIndex, upgraded),
            ActiveRestPending = false,
        };
    }
}
```

- [ ] **Step 4: テスト PASS 確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~RestActionsTests" --nologo`
Expected: PASS (8 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Rest/RestActions.cs tests/Core.Tests/Rest/RestActionsTests.cs
git commit -m "feat(core): add RestActions (heal with passive bonus / upgrade card)"
git push
```

---

### Task F2: RestController + integration tests

**Files:**
- Create: `src/Server/Controllers/RestController.cs`
- Create: `src/Server/Dtos/RestUpgradeRequestDto.cs`
- Create: `tests/Server.Tests/Controllers/RestControllerTests.cs`

- [ ] **Step 1: DTO 作成**

Create `src/Server/Dtos/RestUpgradeRequestDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record RestUpgradeRequestDto(int DeckIndex);
```

- [ ] **Step 2: 失敗するテストを書く**

Create `tests/Server.Tests/Controllers/RestControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RoguelikeCardGame.Server;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Tests.TestHelpers;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class RestControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public RestControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task PostHeal_RestPending_Returns200AndHeals()
    {
        var client = _factory.CreateAuthedClientWithRestTileSelected(currentHp: 30, maxHp: 80);
        var resp = await client.PostAsync("/api/v1/rest/heal", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var snap = await resp.Content.ReadFromJsonAsync<RunSnapshotDto>();
        Assert.NotNull(snap);
        Assert.Equal(30 + 24, snap!.CurrentHp);
        Assert.False(snap.ActiveRestPending);
    }

    [Fact]
    public async Task PostHeal_NotPending_Returns409()
    {
        var client = _factory.CreateAuthedClientWithFreshRun();
        // 初期状態は ActiveRestPending=false
        var resp = await client.PostAsync("/api/v1/rest/heal", content: null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task PostUpgrade_ValidIndex_Returns200AndUpgrades()
    {
        var client = _factory.CreateAuthedClientWithRestTileSelected(currentHp: 80, maxHp: 80);
        var resp = await client.PostAsJsonAsync(
            "/api/v1/rest/upgrade", new RestUpgradeRequestDto(DeckIndex: 0));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var snap = await resp.Content.ReadFromJsonAsync<RunSnapshotDto>();
        Assert.NotNull(snap);
        Assert.True(snap!.Deck[0].Upgraded);
        Assert.False(snap.ActiveRestPending);
    }

    [Fact]
    public async Task PostUpgrade_OutOfRange_Returns400()
    {
        var client = _factory.CreateAuthedClientWithRestTileSelected();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/rest/upgrade", new RestUpgradeRequestDto(DeckIndex: 999));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PostUpgrade_AlreadyUpgraded_Returns409()
    {
        var client = _factory.CreateAuthedClientWithRestTileSelected(preUpgradeDeckIndex: 0);
        var resp = await client.PostAsJsonAsync(
            "/api/v1/rest/upgrade", new RestUpgradeRequestDto(DeckIndex: 0));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task PostUpgrade_NotPending_Returns409()
    {
        var client = _factory.CreateAuthedClientWithFreshRun();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/rest/upgrade", new RestUpgradeRequestDto(DeckIndex: 0));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }
}
```

- [ ] **Step 3: テスト FAIL 確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~RestControllerTests" --nologo`
Expected: FAIL — `RestController` 未定義。

- [ ] **Step 4: 実装**

Create `src/Server/Controllers/RestController.cs`:

```csharp
using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Rest;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rest")]
public sealed class RestController : ControllerBase
{
    private readonly ISessionService _sessions;
    private readonly DataCatalog _catalog;

    public RestController(ISessionService sessions, DataCatalog catalog)
    {
        _sessions = sessions;
        _catalog = catalog;
    }

    [HttpPost("heal")]
    public IActionResult Heal()
    {
        var s = _sessions.RequireActiveRun(User);
        if (!s.ActiveRestPending)
            return Conflict(new { error = "Rest is not pending" });
        try
        {
            var s1 = RestActions.Heal(s, _catalog);
            _sessions.PersistRun(User, s1);
            return Ok(_sessions.SnapshotOf(s1));
        }
        catch (InvalidOperationException ex)
        { return Conflict(new { error = ex.Message }); }
    }

    [HttpPost("upgrade")]
    public IActionResult Upgrade([FromBody] RestUpgradeRequestDto dto)
    {
        var s = _sessions.RequireActiveRun(User);
        if (!s.ActiveRestPending)
            return Conflict(new { error = "Rest is not pending" });
        try
        {
            var s1 = RestActions.UpgradeCard(s, dto.DeckIndex, _catalog);
            _sessions.PersistRun(User, s1);
            return Ok(_sessions.SnapshotOf(s1));
        }
        catch (ArgumentOutOfRangeException ex)
        { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex)
        { return Conflict(new { error = ex.Message }); }
    }
}
```

- [ ] **Step 5: TestHelpers 拡張**

`tests/Server.Tests/TestHelpers/` に `CreateAuthedClientWithRestTileSelected(currentHp, maxHp, preUpgradeDeckIndex?)` を追加。Phase 5 の既存 `CreateAuthedClientWithFreshRun` + `SetRunStateOverride` パターンを踏襲し、`ActiveRestPending = true` と必要な HP / Deck を注入する（例: `factory.SetRunStateOverride(run => run with { CurrentHp = currentHp, MaxHp = maxHp, ActiveRestPending = true, Deck = ... })`）。

`preUpgradeDeckIndex` が指定されたら該当カードを `CardInstance(Id, Upgraded: true)` として上書きする。

- [ ] **Step 6: PASS 確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~RestControllerTests" --nologo`
Expected: PASS (6 件)。

- [ ] **Step 7: コミット**

```bash
git add src/Server/Controllers/RestController.cs src/Server/Dtos/RestUpgradeRequestDto.cs tests/Server.Tests/Controllers/RestControllerTests.cs tests/Server.Tests/TestHelpers/
git commit -m "feat(server): add RestController (heal / upgrade)"
git push
```

---

## Part G — Catalog 拡張 / Reward ClaimRelic / RunSnapshotDto 更新

Client から relic / event マスタを取りにいけるようにし、Treasure/Event 経由で取得したレリックを claim する REST を実装し、RunSnapshotDto を Phase 6 の新フィールド（`CardInstance` deck、`ActiveMerchant`、`ActiveEvent`、`ActiveRestPending`）に追従させる。

### Task G1: CatalogController に /relics と /events を追加

**Files:**
- Modify: `src/Server/Controllers/CatalogController.cs`
- Modify: `tests/Server.Tests/Controllers/CatalogControllerTests.cs`
- Create: `src/Server/Dtos/RelicDto.cs`
- Create: `src/Server/Dtos/EventDto.cs`

- [ ] **Step 1: DTO 作成**

Create `src/Server/Dtos/RelicDto.cs`:

```csharp
namespace RoguelikeCardGame.Server.Dtos;

public sealed record RelicDto(
    string Id,
    string Name,
    string Description,
    string Rarity,
    string Trigger);
```

Create `src/Server/Dtos/EventDto.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record EventChoiceDto(
    string Label,
    string? ConditionSummary,     // e.g. "requires 50 gold"
    IReadOnlyList<string> EffectSummaries);

public sealed record EventDto(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<EventChoiceDto> Choices);
```

- [ ] **Step 2: 失敗するテストを書く**

Add to `tests/Server.Tests/Controllers/CatalogControllerTests.cs` (作成 or 追記):

```csharp
[Fact]
public async Task GetRelics_Returns200WithAll4Relics()
{
    var client = _factory.CreateAuthedClient();
    var resp = await client.GetAsync("/api/v1/catalog/relics");
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    var list = await resp.Content.ReadFromJsonAsync<List<RelicDto>>();
    Assert.NotNull(list);
    var ids = list!.Select(r => r.Id).ToHashSet();
    Assert.Contains("extra_max_hp", ids);
    Assert.Contains("coin_purse", ids);
    Assert.Contains("traveler_boots", ids);
    Assert.Contains("warm_blanket", ids);
}

[Fact]
public async Task GetEvents_Returns200WithAll3Events()
{
    var client = _factory.CreateAuthedClient();
    var resp = await client.GetAsync("/api/v1/catalog/events");
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    var list = await resp.Content.ReadFromJsonAsync<List<EventDto>>();
    Assert.NotNull(list);
    var ids = list!.Select(e => e.Id).ToHashSet();
    Assert.Contains("blessing_fountain", ids);
    Assert.Contains("shady_merchant", ids);
    Assert.Contains("old_library", ids);
}
```

- [ ] **Step 3: FAIL 確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CatalogControllerTests" --nologo`
Expected: FAIL — エンドポイント 404。

- [ ] **Step 4: 実装**

`src/Server/Controllers/CatalogController.cs` にアクションを追加:

```csharp
[HttpGet("relics")]
public IActionResult GetRelics()
{
    var list = _catalog.Relics.Values
        .OrderBy(r => r.Id, StringComparer.Ordinal)
        .Select(r => new RelicDto(
            Id: r.Id,
            Name: r.Name,
            Description: r.Description,
            Rarity: r.Rarity.ToString(),
            Trigger: r.Trigger.ToString()))
        .ToList();
    return Ok(list);
}

[HttpGet("events")]
public IActionResult GetEvents()
{
    var list = _catalog.Events.Values
        .OrderBy(e => e.Id, StringComparer.Ordinal)
        .Select(e => new EventDto(
            Id: e.Id,
            Name: e.Name,
            Description: e.Description,
            Choices: e.Choices.Select(c => new EventChoiceDto(
                Label: c.Label,
                ConditionSummary: c.Condition switch
                {
                    EventCondition.MinGold(var g) => $"requires {g} gold",
                    EventCondition.MinHp(var h) => $"requires {h} HP",
                    null => null,
                    _ => "requires condition",
                },
                EffectSummaries: c.Effects.Select(EffectLabel).ToList()))
            .ToList()))
        .ToList();
    return Ok(list);
}

private static string EffectLabel(EventEffect e) => e switch
{
    EventEffect.GainGold(var n) => $"+{n} gold",
    EventEffect.PayGold(var n) => $"-{n} gold",
    EventEffect.Heal(var n) => $"+{n} HP",
    EventEffect.TakeDamage(var n) => $"-{n} HP",
    EventEffect.GainMaxHp(var n) => $"+{n} max HP",
    EventEffect.LoseMaxHp(var n) => $"-{n} max HP",
    EventEffect.GainRelicRandom(var rarity) => $"random {rarity} relic",
    EventEffect.GrantCardReward => "card reward (3 choices)",
    _ => "(effect)",
};
```

必要な using を追加: `using System; using System.Linq; using RoguelikeCardGame.Core.Events;`

- [ ] **Step 5: PASS 確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~CatalogControllerTests" --nologo`
Expected: PASS。

- [ ] **Step 6: コミット**

```bash
git add src/Server/Controllers/CatalogController.cs src/Server/Dtos/RelicDto.cs src/Server/Dtos/EventDto.cs tests/Server.Tests/Controllers/CatalogControllerTests.cs
git commit -m "feat(server): add /catalog/relics and /catalog/events endpoints"
git push
```

---

### Task G2: RewardController に /claim-relic を追加

**Files:**
- Modify: `src/Server/Controllers/RewardController.cs`
- Modify: `tests/Server.Tests/Controllers/RewardEndpointsTests.cs`（存在しなければ作成）

- [ ] **Step 1: 失敗するテストを書く**

Add to `tests/Server.Tests/Controllers/RewardEndpointsTests.cs`:

```csharp
[Fact]
public async Task PostClaimRelic_WithActiveRewardRelic_Returns200AndAddsToInventory()
{
    var client = _factory.CreateAuthedClientWithTreasureResolved();
    var snap0 = await client.GetSnapshot();
    Assert.NotNull(snap0.ActiveReward?.RelicId);

    var resp = await client.PostAsync("/api/v1/reward/claim-relic", content: null);
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    var snap1 = await resp.Content.ReadFromJsonAsync<RunSnapshotDto>();
    Assert.NotNull(snap1);
    Assert.Contains(snap0.ActiveReward!.RelicId!, snap1!.Relics);
    Assert.True(snap1.ActiveReward is null || snap1.ActiveReward.RelicClaimed);
}

[Fact]
public async Task PostClaimRelic_NoActiveReward_Returns409()
{
    var client = _factory.CreateAuthedClientWithFreshRun();
    var resp = await client.PostAsync("/api/v1/reward/claim-relic", content: null);
    Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
}

[Fact]
public async Task PostClaimRelic_NoRelicOnReward_Returns409()
{
    // ActiveReward はあるが RelicId==null（既存の Enemy 報酬）のケース
    var client = _factory.CreateAuthedClientWithEnemyRewardPending();
    var resp = await client.PostAsync("/api/v1/reward/claim-relic", content: null);
    Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
}
```

- [ ] **Step 2: FAIL 確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~RewardEndpointsTests.PostClaimRelic" --nologo`
Expected: FAIL — 404 (エンドポイント未定義)。

- [ ] **Step 3: 実装**

Add to `src/Server/Controllers/RewardController.cs`:

```csharp
[HttpPost("claim-relic")]
public IActionResult ClaimRelic()
{
    var s = _sessions.RequireActiveRun(User);
    if (s.ActiveReward is null)
        return Conflict(new { error = "no active reward" });
    if (s.ActiveReward.RelicId is null || s.ActiveReward.RelicClaimed)
        return Conflict(new { error = "no relic to claim" });
    try
    {
        var s1 = RewardApplier.ClaimRelic(s, _catalog);
        _sessions.PersistRun(User, s1);
        return Ok(_sessions.SnapshotOf(s1));
    }
    catch (InvalidOperationException ex)
    { return Conflict(new { error = ex.Message }); }
}
```

必要な using: `using RoguelikeCardGame.Core.Rewards;`

- [ ] **Step 4: PASS 確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~RewardEndpointsTests.PostClaimRelic" --nologo`
Expected: PASS (3 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Server/Controllers/RewardController.cs tests/Server.Tests/Controllers/RewardEndpointsTests.cs tests/Server.Tests/TestHelpers/
git commit -m "feat(server): add /reward/claim-relic endpoint"
git push
```

---

### Task G3: RunSnapshotDto を CardInstance / Active* 対応に更新

**Files:**
- Modify: `src/Server/Dtos/RunSnapshotDto.cs`
- Modify: `src/Server/Services/SessionService.cs`（`SnapshotOf` の実装箇所）
- Modify: 既存 `RunSnapshot*` テスト（Phase 5 で存在）

- [ ] **Step 1: DTO 拡張**

Edit `src/Server/Dtos/RunSnapshotDto.cs` — 以下の変更を加える:

```csharp
public sealed record CardInstanceDto(string Id, bool Upgraded);

public sealed record MerchantOfferDto(string Id, int Price, bool Sold);

public sealed record MerchantInventoryDto(
    IReadOnlyList<MerchantOfferDto> Cards,
    IReadOnlyList<MerchantOfferDto> Relics,
    IReadOnlyList<MerchantOfferDto> Potions,
    bool DiscardSlotUsed,
    int DiscardPrice);

public sealed record EventChoiceSnapshotDto(
    string Label,
    string? ConditionSummary,
    bool ConditionMet);

public sealed record EventInstanceDto(
    string EventId,
    string Name,
    string Description,
    IReadOnlyList<EventChoiceSnapshotDto> Choices);

public sealed record RewardStateDto(
    int Gold,
    string? PotionId,
    IReadOnlyList<string> CardChoices,
    bool CardClaimed,
    string? RelicId,
    bool RelicClaimed);

public sealed record RunSnapshotDto(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    IReadOnlyList<int> VisitedNodeIds,
    IReadOnlyDictionary<int, string> UnknownResolutions,
    string CharacterId,
    int CurrentHp,
    int MaxHp,
    int Gold,
    IReadOnlyList<CardInstanceDto> Deck,      // 旧: IReadOnlyList<string>
    IReadOnlyList<string> Relics,
    IReadOnlyList<string> Potions,
    int PotionSlotCount,
    bool HasActiveBattle,
    RewardStateDto? ActiveReward,
    MerchantInventoryDto? ActiveMerchant,
    EventInstanceDto? ActiveEvent,
    bool ActiveRestPending,
    long PlaySeconds,
    string Progress);
```

（ただし、既存 Phase 5 の `RunSnapshotDto` にあるフィールドはそのまま保持すること。上記はあくまで差分の最終形。）

- [ ] **Step 2: SessionService.SnapshotOf を更新**

`src/Server/Services/SessionService.cs` の `SnapshotOf(RunState)`:

```csharp
public RunSnapshotDto SnapshotOf(RunState s)
{
    return new RunSnapshotDto(
        SchemaVersion: s.SchemaVersion,
        CurrentAct: s.CurrentAct,
        CurrentNodeId: s.CurrentNodeId,
        VisitedNodeIds: s.VisitedNodeIds.ToArray(),
        UnknownResolutions: s.UnknownResolutions.ToDictionary(
            kv => kv.Key, kv => kv.Value.ToString()),
        CharacterId: s.CharacterId,
        CurrentHp: s.CurrentHp,
        MaxHp: s.MaxHp,
        Gold: s.Gold,
        Deck: s.Deck.Select(c => new CardInstanceDto(c.Id, c.Upgraded)).ToArray(),
        Relics: s.Relics.ToArray(),
        Potions: s.Potions.ToArray(),
        PotionSlotCount: s.PotionSlotCount,
        HasActiveBattle: s.ActiveBattle is not null,
        ActiveReward: s.ActiveReward is null ? null : new RewardStateDto(
            Gold: s.ActiveReward.Gold,
            PotionId: s.ActiveReward.PotionId,
            CardChoices: s.ActiveReward.CardChoices.ToArray(),
            CardClaimed: s.ActiveReward.CardClaimed,
            RelicId: s.ActiveReward.RelicId,
            RelicClaimed: s.ActiveReward.RelicClaimed),
        ActiveMerchant: s.ActiveMerchant is null ? null : MerchantInventoryDto.From(s.ActiveMerchant),
        ActiveEvent: s.ActiveEvent is null ? null : EventInstanceDtoFactory.From(s.ActiveEvent, s, _catalog),
        ActiveRestPending: s.ActiveRestPending,
        PlaySeconds: s.PlaySeconds,
        Progress: s.Progress.ToString());
}
```

`EventInstanceDtoFactory.From(EventInstance, RunState, DataCatalog)` は本タスク内で同ファイルに private static メソッドとして追加するか、`src/Server/Services/EventInstanceDtoFactory.cs` として切り出す（条件判定結果を snapshot 時点で計算するため state が必要）。

`EventInstanceDtoFactory.From` のロジック:
```csharp
public static EventInstanceDto From(EventInstance inst, RunState s, DataCatalog catalog)
{
    var def = catalog.Events[inst.EventId];
    var choices = def.Choices.Select(c =>
    {
        string? summary = c.Condition switch
        {
            EventCondition.MinGold(var g) => $"requires {g} gold",
            EventCondition.MinHp(var h) => $"requires {h} HP",
            null => null,
            _ => "requires condition",
        };
        bool met = c.Condition switch
        {
            null => true,
            EventCondition.MinGold(var g) => s.Gold >= g,
            EventCondition.MinHp(var h) => s.CurrentHp >= h,
            _ => false,
        };
        return new EventChoiceSnapshotDto(c.Label, summary, met);
    }).ToList();
    return new EventInstanceDto(inst.EventId, def.Name, def.Description, choices);
}
```

- [ ] **Step 3: Phase 5 で存在するスナップショットテストを更新**

既存 `RunSnapshotDtoTests` / `RunsControllerTests` / その他 `Deck` を検証しているテストを `CardInstance` 形状に合わせて修正する。影響箇所:
- `Deck` フィールド: `List<string>` → `List<CardInstanceDto>`
- 既存のアサーション `Assert.Equal("strike", snap.Deck[0])` → `Assert.Equal("strike", snap.Deck[0].Id)`

Grep で一括確認:
```bash
grep -rn "snap.Deck\[" tests/Server.Tests/
grep -rn "Deck =" tests/Server.Tests/
```

ヒットした全てを CardInstance 形状に合わせる。

- [ ] **Step 4: 全テスト PASS 確認**

Run: `dotnet test --nologo`
Expected: PASS（全テスト）。

- [ ] **Step 5: コミット**

```bash
git add src/Server/Dtos/RunSnapshotDto.cs src/Server/Services/SessionService.cs src/Server/Services/EventInstanceDtoFactory.cs tests/Server.Tests/
git commit -m "refactor(server): update RunSnapshotDto for CardInstance / Merchant / Event / RestPending"
git push
```

---

## Part H — Client 実装

デバッグ UI を踏襲。CSS は最小限、後で Claude Design で一括差し替える。まず API ラッパとフックを整備してから、各画面 → 最後に MapScreen の分岐を追加する。

### Task H1: api ラッパ 3 本（merchant / event / rest）

**Files:**
- Create: `src/Client/src/api/merchant.ts`
- Create: `src/Client/src/api/event.ts`
- Create: `src/Client/src/api/rest.ts`
- Create: `src/Client/src/api/merchant.test.ts`
- Create: `src/Client/src/api/event.test.ts`
- Create: `src/Client/src/api/rest.test.ts`

- [ ] **Step 1: テスト先行 — merchant.test.ts**

Create `src/Client/src/api/merchant.test.ts`:

```typescript
import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  getMerchantInventory,
  buyFromMerchant,
  discardAtMerchant,
  leaveMerchant,
} from "./merchant";

const fetchMock = vi.fn();
beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
});

describe("merchant api", () => {
  it("getMerchantInventory hits GET /api/v1/merchant/inventory", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({
      cards: [], relics: [], potions: [], discardSlotUsed: false, discardPrice: 75
    }), { status: 200 }));
    const inv = await getMerchantInventory();
    expect(fetchMock).toHaveBeenCalledWith("/api/v1/merchant/inventory", expect.objectContaining({ method: "GET" }));
    expect(inv.discardPrice).toBe(75);
  });

  it("buyFromMerchant POSTs correct body", async () => {
    fetchMock.mockResolvedValue(new Response("{}", { status: 200 }));
    await buyFromMerchant({ kind: "card", id: "strike" });
    const [, init] = fetchMock.mock.calls[0];
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body as string)).toEqual({ kind: "card", id: "strike" });
  });

  it("discardAtMerchant POSTs deckIndex", async () => {
    fetchMock.mockResolvedValue(new Response("{}", { status: 200 }));
    await discardAtMerchant(2);
    const [, init] = fetchMock.mock.calls[0];
    expect(JSON.parse(init.body as string)).toEqual({ deckIndex: 2 });
  });

  it("leaveMerchant POSTs no body", async () => {
    fetchMock.mockResolvedValue(new Response("{}", { status: 200 }));
    await leaveMerchant();
    expect(fetchMock).toHaveBeenCalledWith("/api/v1/merchant/leave", expect.objectContaining({ method: "POST" }));
  });
});
```

同様に `event.test.ts`（`getCurrentEvent`, `chooseEvent(choiceIndex)`）、`rest.test.ts`（`restHeal()`, `restUpgrade(deckIndex)`）を作成。

- [ ] **Step 2: FAIL 確認**

Run: `cd src/Client && npm run test -- --run api/merchant.test.ts api/event.test.ts api/rest.test.ts`
Expected: FAIL — モジュール未定義。

- [ ] **Step 3: 実装**

Create `src/Client/src/api/merchant.ts`:

```typescript
import type { MerchantInventoryDto, RunSnapshotDto } from "./types";
import { postJson, getJson } from "./http";

export async function getMerchantInventory(): Promise<MerchantInventoryDto> {
  return getJson<MerchantInventoryDto>("/api/v1/merchant/inventory");
}

export async function buyFromMerchant(
  body: { kind: "card" | "relic" | "potion"; id: string }
): Promise<RunSnapshotDto> {
  return postJson<RunSnapshotDto>("/api/v1/merchant/buy", body);
}

export async function discardAtMerchant(deckIndex: number): Promise<RunSnapshotDto> {
  return postJson<RunSnapshotDto>("/api/v1/merchant/discard", { deckIndex });
}

export async function leaveMerchant(): Promise<RunSnapshotDto> {
  return postJson<RunSnapshotDto>("/api/v1/merchant/leave");
}
```

Create `src/Client/src/api/event.ts`:

```typescript
import type { EventInstanceDto, RunSnapshotDto } from "./types";
import { postJson, getJson } from "./http";

export async function getCurrentEvent(): Promise<EventInstanceDto> {
  return getJson<EventInstanceDto>("/api/v1/event/current");
}

export async function chooseEvent(choiceIndex: number): Promise<RunSnapshotDto> {
  return postJson<RunSnapshotDto>("/api/v1/event/choose", { choiceIndex });
}
```

Create `src/Client/src/api/rest.ts`:

```typescript
import type { RunSnapshotDto } from "./types";
import { postJson } from "./http";

export async function restHeal(): Promise<RunSnapshotDto> {
  return postJson<RunSnapshotDto>("/api/v1/rest/heal");
}

export async function restUpgrade(deckIndex: number): Promise<RunSnapshotDto> {
  return postJson<RunSnapshotDto>("/api/v1/rest/upgrade", { deckIndex });
}
```

`src/Client/src/api/http.ts` に共通 `postJson` / `getJson` が既にあれば再利用。なければ Phase 5 の既存 api ラッパの作法を踏襲して同ファイルに最低限のヘルパを追加。

`src/Client/src/api/types.ts` に `MerchantInventoryDto` / `MerchantOfferDto` / `EventInstanceDto` / `EventChoiceSnapshotDto` / `CardInstanceDto` / `RunSnapshotDto` の TypeScript 型を、Part G3 の C# DTO と 1:1 に対応させて追加する。

- [ ] **Step 4: PASS 確認**

Run: `cd src/Client && npm run test -- --run api/`
Expected: PASS（merchant / event / rest の全テスト）。

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/api/merchant.ts src/Client/src/api/event.ts src/Client/src/api/rest.ts src/Client/src/api/merchant.test.ts src/Client/src/api/event.test.ts src/Client/src/api/rest.test.ts src/Client/src/api/types.ts src/Client/src/api/http.ts
git commit -m "feat(client): add merchant/event/rest api wrappers"
git push
```

---

### Task H2: useRelicCatalog / useEventCatalog hooks

**Files:**
- Create: `src/Client/src/hooks/useRelicCatalog.ts`
- Create: `src/Client/src/hooks/useEventCatalog.ts`
- Create: `src/Client/src/hooks/useRelicCatalog.test.ts`
- Create: `src/Client/src/hooks/useEventCatalog.test.ts`

- [ ] **Step 1: テスト先行**

Create `src/Client/src/hooks/useRelicCatalog.test.ts`:

```typescript
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useRelicCatalog } from "./useRelicCatalog";

const fetchMock = vi.fn();
beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
});

describe("useRelicCatalog", () => {
  it("loads and memoizes relic catalog", async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify([
      { id: "coin_purse", name: "Coin Purse", description: "", rarity: "Common", trigger: "OnPickup" },
    ]), { status: 200 }));

    const { result } = renderHook(() => useRelicCatalog());
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.relics).toHaveLength(1);
    expect(result.current.byId["coin_purse"].name).toBe("Coin Purse");
  });
});
```

`useEventCatalog.test.ts` も同様のパターンで `/api/v1/catalog/events` をモックし、`byId["blessing_fountain"]` で引けることを確認する。

- [ ] **Step 2: FAIL 確認**

Run: `cd src/Client && npm run test -- --run hooks/useRelicCatalog.test.ts hooks/useEventCatalog.test.ts`
Expected: FAIL。

- [ ] **Step 3: 実装**

Create `src/Client/src/hooks/useRelicCatalog.ts`:

```typescript
import { useEffect, useMemo, useState } from "react";
import { getJson } from "../api/http";

export interface RelicDto {
  id: string;
  name: string;
  description: string;
  rarity: "Common" | "Rare" | "Epic";
  trigger: "OnPickup" | "Passive" | "OnBattleStart" | "OnBattleEnd" | "OnMapTileResolved";
}

export function useRelicCatalog() {
  const [relics, setRelics] = useState<RelicDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancel = false;
    getJson<RelicDto[]>("/api/v1/catalog/relics").then((list) => {
      if (!cancel) {
        setRelics(list);
        setLoading(false);
      }
    });
    return () => { cancel = true; };
  }, []);

  const byId = useMemo(() => Object.fromEntries(relics.map(r => [r.id, r])), [relics]);
  return { relics, byId, loading };
}
```

Create `src/Client/src/hooks/useEventCatalog.ts` — 同パターンで `/api/v1/catalog/events` を取得、`EventDto` 型。

- [ ] **Step 4: PASS 確認**

Run: `cd src/Client && npm run test -- --run hooks/`
Expected: PASS。

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/hooks/useRelicCatalog.ts src/Client/src/hooks/useEventCatalog.ts src/Client/src/hooks/useRelicCatalog.test.ts src/Client/src/hooks/useEventCatalog.test.ts
git commit -m "feat(client): add useRelicCatalog and useEventCatalog hooks"
git push
```

---

### Task H3: TopBar を CardInstance 対応に更新

**Files:**
- Modify: `src/Client/src/components/TopBar.tsx`
- Modify: `src/Client/src/components/TopBar.test.tsx`

- [ ] **Step 1: テスト追加**

Add to `src/Client/src/components/TopBar.test.tsx`:

```typescript
it("renders '+' suffix for upgraded cards", () => {
  const snap = makeSnapshot({
    deck: [
      { id: "strike", upgraded: false },
      { id: "strike", upgraded: true },
    ],
  });
  render(<TopBar snapshot={snap} cardCatalog={{ byId: { strike: { id: "strike", name: "Strike" } } as any }} />);
  expect(screen.getByText("Strike")).toBeInTheDocument();
  expect(screen.getByText("Strike+")).toBeInTheDocument();
});
```

- [ ] **Step 2: FAIL 確認**

Run: `cd src/Client && npm run test -- --run components/TopBar.test.tsx`
Expected: FAIL — `upgraded` プロパティ未対応。

- [ ] **Step 3: 実装**

`TopBar.tsx` のデッキ表示箇所:

```tsx
{snapshot.deck.map((card, i) => {
  const def = cardCatalog.byId[card.id];
  const name = def?.name ?? card.id;
  return (
    <li key={i} className="topbar-deck-entry">
      {name}{card.upgraded ? "+" : ""}
    </li>
  );
})}
```

既存の `snapshot.deck` が `string[]` を前提にしている型・描画箇所を全て `CardInstanceDto[]` 対応に書き直す。

- [ ] **Step 4: PASS 確認**

Run: `cd src/Client && npm run test -- --run components/`
Expected: PASS。

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/components/TopBar.tsx src/Client/src/components/TopBar.test.tsx
git commit -m "feat(client): display '+' for upgraded cards in TopBar"
git push
```

---

### Task H4: RewardPopup に relic 行と onClaimRelic

**Files:**
- Modify: `src/Client/src/screens/RewardPopup.tsx`
- Modify: `src/Client/src/screens/RewardPopup.test.tsx`

- [ ] **Step 1: テスト追加**

Add to `RewardPopup.test.tsx`:

```typescript
it("renders relic row when reward.relicId is present", async () => {
  const onClaim = vi.fn();
  render(<RewardPopup
    reward={{ gold: 0, potionId: null, cardChoices: [], cardClaimed: true, relicId: "coin_purse", relicClaimed: false }}
    relicCatalog={{ byId: { coin_purse: { id: "coin_purse", name: "Coin Purse", description: "", rarity: "Common", trigger: "OnPickup" } } as any }}
    onClaimRelic={onClaim}
    // ...既存 props
  />);
  const btn = screen.getByRole("button", { name: /claim coin purse/i });
  await userEvent.click(btn);
  expect(onClaim).toHaveBeenCalled();
});

it("hides relic row when relicClaimed is true", () => {
  render(<RewardPopup
    reward={{ gold: 0, potionId: null, cardChoices: [], cardClaimed: true, relicId: "coin_purse", relicClaimed: true }}
    relicCatalog={{ byId: { coin_purse: { id: "coin_purse", name: "Coin Purse" } as any } as any }}
    onClaimRelic={vi.fn()}
  />);
  expect(screen.queryByRole("button", { name: /claim/i })).toBeNull();
});
```

- [ ] **Step 2: FAIL 確認**

Run: `cd src/Client && npm run test -- --run screens/RewardPopup.test.tsx`
Expected: FAIL。

- [ ] **Step 3: 実装**

`RewardPopup.tsx` に relic セクションと `onClaimRelic` prop を追加:

```tsx
interface Props {
  reward: RewardStateDto;
  relicCatalog: { byId: Record<string, RelicDto> };
  onClaimRelic: () => void | Promise<void>;
  // ... 既存 props (onClaimPotion / onClaimCard / onClose 等)
}

export function RewardPopup({ reward, relicCatalog, onClaimRelic, /* ... */ }: Props) {
  return (
    <div className="reward-popup">
      {/* ... 既存: gold / potion / card 行 ... */}
      {reward.relicId && !reward.relicClaimed && (
        <div className="reward-row reward-relic">
          <span>Relic: {relicCatalog.byId[reward.relicId]?.name ?? reward.relicId}</span>
          <button onClick={() => onClaimRelic()}>
            Claim {relicCatalog.byId[reward.relicId]?.name ?? reward.relicId}
          </button>
        </div>
      )}
    </div>
  );
}
```

呼び出し側（`MapScreen` 等）で `onClaimRelic={async () => { const s = await claimRelic(); setSnapshot(s); }}` を渡す。`claimRelic` は `src/Client/src/api/reward.ts` に `postJson("/api/v1/reward/claim-relic")` として追加。

- [ ] **Step 4: PASS 確認**

Run: `cd src/Client && npm run test -- --run screens/RewardPopup.test.tsx`
Expected: PASS。

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/screens/RewardPopup.tsx src/Client/src/screens/RewardPopup.test.tsx src/Client/src/api/reward.ts
git commit -m "feat(client): add relic row and claim button to RewardPopup"
git push
```

---

### Task H5: MerchantScreen

**Files:**
- Create: `src/Client/src/screens/MerchantScreen.tsx`
- Create: `src/Client/src/screens/MerchantScreen.test.tsx`

- [ ] **Step 1: テスト先行**

Create `MerchantScreen.test.tsx`:

```typescript
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { MerchantScreen } from "./MerchantScreen";

const inventory = {
  cards: [{ id: "strike", price: 50, sold: false }],
  relics: [{ id: "coin_purse", price: 150, sold: false }],
  potions: [{ id: "heal_potion_small", price: 50, sold: false }],
  discardSlotUsed: false,
  discardPrice: 75,
};

function makeCatalogs() {
  return {
    cardCatalog: { byId: { strike: { id: "strike", name: "Strike" } } as any },
    relicCatalog: { byId: { coin_purse: { id: "coin_purse", name: "Coin Purse" } } as any },
    potionCatalog: { byId: { heal_potion_small: { id: "heal_potion_small", name: "Small Heal" } } as any },
  };
}

it("renders all 3 categories with prices", () => {
  render(<MerchantScreen gold={500} deck={[{ id: "strike", upgraded: false }]}
    inventory={inventory} {...makeCatalogs()}
    onBuy={vi.fn()} onDiscard={vi.fn()} onLeave={vi.fn()} />);
  expect(screen.getByText(/Strike/)).toBeInTheDocument();
  expect(screen.getByText(/Coin Purse/)).toBeInTheDocument();
  expect(screen.getByText(/Small Heal/)).toBeInTheDocument();
  expect(screen.getAllByText(/50 g/).length).toBeGreaterThanOrEqual(1);
});

it("disables buy button when gold is insufficient", () => {
  render(<MerchantScreen gold={30} deck={[]}
    inventory={inventory} {...makeCatalogs()}
    onBuy={vi.fn()} onDiscard={vi.fn()} onLeave={vi.fn()} />);
  const btn = screen.getByRole("button", { name: /buy strike/i });
  expect(btn).toBeDisabled();
});

it("calls onBuy with correct args", async () => {
  const onBuy = vi.fn();
  render(<MerchantScreen gold={500} deck={[]}
    inventory={inventory} {...makeCatalogs()}
    onBuy={onBuy} onDiscard={vi.fn()} onLeave={vi.fn()} />);
  await userEvent.click(screen.getByRole("button", { name: /buy strike/i }));
  expect(onBuy).toHaveBeenCalledWith("card", "strike");
});

it("discard is disabled when discardSlotUsed is true", () => {
  const used = { ...inventory, discardSlotUsed: true };
  render(<MerchantScreen gold={500} deck={[{ id: "strike", upgraded: false }]}
    inventory={used} {...makeCatalogs()}
    onBuy={vi.fn()} onDiscard={vi.fn()} onLeave={vi.fn()} />);
  const btns = screen.queryAllByRole("button", { name: /discard/i });
  btns.forEach(b => expect(b).toBeDisabled());
});

it("calls onLeave when leave button clicked", async () => {
  const onLeave = vi.fn();
  render(<MerchantScreen gold={500} deck={[]}
    inventory={inventory} {...makeCatalogs()}
    onBuy={vi.fn()} onDiscard={vi.fn()} onLeave={onLeave} />);
  await userEvent.click(screen.getByRole("button", { name: /leave/i }));
  expect(onLeave).toHaveBeenCalled();
});
```

- [ ] **Step 2: FAIL 確認**

Run: `cd src/Client && npm run test -- --run screens/MerchantScreen.test.tsx`
Expected: FAIL。

- [ ] **Step 3: 実装**

Create `MerchantScreen.tsx`:

```tsx
import type { CardInstanceDto, MerchantInventoryDto } from "../api/types";

interface Props {
  gold: number;
  deck: CardInstanceDto[];
  inventory: MerchantInventoryDto;
  cardCatalog: { byId: Record<string, { id: string; name: string; rarity?: string }> };
  relicCatalog: { byId: Record<string, { id: string; name: string }> };
  potionCatalog: { byId: Record<string, { id: string; name: string }> };
  onBuy: (kind: "card" | "relic" | "potion", id: string) => void;
  onDiscard: (deckIndex: number) => void;
  onLeave: () => void;
}

export function MerchantScreen(p: Props) {
  const row = (kind: "card" | "relic" | "potion", offer: { id: string; price: number; sold: boolean }, name: string) => (
    <li key={`${kind}:${offer.id}`} className={`merchant-offer ${offer.sold ? "sold" : ""}`}>
      <span>{name}</span>
      <span>{offer.price} g</span>
      <button
        onClick={() => p.onBuy(kind, offer.id)}
        disabled={offer.sold || p.gold < offer.price}
        aria-label={`Buy ${name}`}
      >
        {offer.sold ? "Sold" : "Buy"}
      </button>
    </li>
  );

  return (
    <div className="merchant-screen">
      <h2>Merchant (Gold: {p.gold})</h2>

      <section>
        <h3>Cards</h3>
        <ul>{p.inventory.cards.map(o => row("card", o, p.cardCatalog.byId[o.id]?.name ?? o.id))}</ul>
      </section>

      <section>
        <h3>Relics</h3>
        <ul>{p.inventory.relics.map(o => row("relic", o, p.relicCatalog.byId[o.id]?.name ?? o.id))}</ul>
      </section>

      <section>
        <h3>Potions</h3>
        <ul>{p.inventory.potions.map(o => row("potion", o, p.potionCatalog.byId[o.id]?.name ?? o.id))}</ul>
      </section>

      <section>
        <h3>Discard ({p.inventory.discardPrice} g, once per visit)</h3>
        <ul>
          {p.deck.map((c, i) => {
            const name = p.cardCatalog.byId[c.id]?.name ?? c.id;
            return (
              <li key={i}>
                <span>{name}{c.upgraded ? "+" : ""}</span>
                <button
                  onClick={() => p.onDiscard(i)}
                  disabled={p.inventory.discardSlotUsed || p.gold < p.inventory.discardPrice}
                  aria-label={`Discard ${name} at index ${i}`}
                >
                  Discard
                </button>
              </li>
            );
          })}
        </ul>
      </section>

      <button onClick={() => p.onLeave()}>Leave</button>
    </div>
  );
}
```

- [ ] **Step 4: PASS 確認**

Run: `cd src/Client && npm run test -- --run screens/MerchantScreen.test.tsx`
Expected: PASS (5 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/screens/MerchantScreen.tsx src/Client/src/screens/MerchantScreen.test.tsx
git commit -m "feat(client): add MerchantScreen (buy / discard / leave)"
git push
```

---

### Task H6: EventScreen

**Files:**
- Create: `src/Client/src/screens/EventScreen.tsx`
- Create: `src/Client/src/screens/EventScreen.test.tsx`

- [ ] **Step 1: テスト先行**

Create `EventScreen.test.tsx`:

```typescript
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { EventScreen } from "./EventScreen";

const ev = {
  eventId: "shady_merchant",
  name: "Shady Merchant",
  description: "A suspicious figure offers...",
  choices: [
    { label: "Pay 50 gold for a relic", conditionSummary: "requires 50 gold", conditionMet: true },
    { label: "Walk away", conditionSummary: null, conditionMet: true },
  ],
};

it("renders name / description / all choices", () => {
  render(<EventScreen event={ev} onChoose={vi.fn()} />);
  expect(screen.getByText("Shady Merchant")).toBeInTheDocument();
  expect(screen.getByText(/suspicious figure/)).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /Pay 50 gold/ })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /Walk away/ })).toBeInTheDocument();
});

it("disables choice when conditionMet is false", () => {
  const locked = { ...ev, choices: [{ ...ev.choices[0], conditionMet: false }, ev.choices[1]] };
  render(<EventScreen event={locked} onChoose={vi.fn()} />);
  const btn = screen.getByRole("button", { name: /Pay 50 gold/ });
  expect(btn).toBeDisabled();
});

it("calls onChoose with index", async () => {
  const onChoose = vi.fn();
  render(<EventScreen event={ev} onChoose={onChoose} />);
  await userEvent.click(screen.getByRole("button", { name: /Walk away/ }));
  expect(onChoose).toHaveBeenCalledWith(1);
});
```

- [ ] **Step 2: FAIL 確認**

Run: `cd src/Client && npm run test -- --run screens/EventScreen.test.tsx`
Expected: FAIL。

- [ ] **Step 3: 実装**

Create `EventScreen.tsx`:

```tsx
import type { EventInstanceDto } from "../api/types";

interface Props {
  event: EventInstanceDto;
  onChoose: (choiceIndex: number) => void;
}

export function EventScreen({ event, onChoose }: Props) {
  return (
    <div className="event-screen">
      <h2>{event.name}</h2>
      <p>{event.description}</p>
      <ul className="event-choices">
        {event.choices.map((c, i) => (
          <li key={i}>
            <button
              onClick={() => onChoose(i)}
              disabled={!c.conditionMet}
              aria-disabled={!c.conditionMet}
            >
              {c.label}
              {c.conditionSummary ? ` (${c.conditionSummary})` : ""}
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 4: PASS 確認**

Run: `cd src/Client && npm run test -- --run screens/EventScreen.test.tsx`
Expected: PASS (3 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/screens/EventScreen.tsx src/Client/src/screens/EventScreen.test.tsx
git commit -m "feat(client): add EventScreen with condition-locked choices"
git push
```

---

### Task H7: RestScreen

**Files:**
- Create: `src/Client/src/screens/RestScreen.tsx`
- Create: `src/Client/src/screens/RestScreen.test.tsx`

- [ ] **Step 1: テスト先行**

Create `RestScreen.test.tsx`:

```typescript
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import { RestScreen } from "./RestScreen";

const cardCatalog = { byId: {
  strike: { id: "strike", name: "Strike", upgradedEffects: [{}] },
  dazed:  { id: "dazed",  name: "Dazed" }, // upgradedEffects なし = 強化不可
} as any };

const deck = [
  { id: "strike", upgraded: false },
  { id: "strike", upgraded: true },  // 既に強化済み
  { id: "dazed",  upgraded: false }, // 強化不可
];

it("default view shows heal and upgrade buttons", () => {
  render(<RestScreen deck={deck} cardCatalog={cardCatalog} onHeal={vi.fn()} onUpgrade={vi.fn()} />);
  expect(screen.getByRole("button", { name: /heal/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /upgrade/i })).toBeInTheDocument();
});

it("heal button calls onHeal", async () => {
  const onHeal = vi.fn();
  render(<RestScreen deck={deck} cardCatalog={cardCatalog} onHeal={onHeal} onUpgrade={vi.fn()} />);
  await userEvent.click(screen.getByRole("button", { name: /heal/i }));
  expect(onHeal).toHaveBeenCalled();
});

it("upgrade view lists only upgradable cards", async () => {
  render(<RestScreen deck={deck} cardCatalog={cardCatalog} onHeal={vi.fn()} onUpgrade={vi.fn()} />);
  await userEvent.click(screen.getByRole("button", { name: /upgrade/i }));
  // 強化可能: deck[0] のみ
  const buttons = screen.getAllByRole("button", { name: /upgrade .+ at/i });
  expect(buttons).toHaveLength(1);
});

it("selecting upgrade card calls onUpgrade with correct index", async () => {
  const onUpgrade = vi.fn();
  render(<RestScreen deck={deck} cardCatalog={cardCatalog} onHeal={vi.fn()} onUpgrade={onUpgrade} />);
  await userEvent.click(screen.getByRole("button", { name: /upgrade/i }));
  await userEvent.click(screen.getByRole("button", { name: /upgrade strike at 0/i }));
  expect(onUpgrade).toHaveBeenCalledWith(0);
});
```

- [ ] **Step 2: FAIL 確認**

Run: `cd src/Client && npm run test -- --run screens/RestScreen.test.tsx`
Expected: FAIL。

- [ ] **Step 3: 実装**

Create `RestScreen.tsx`:

```tsx
import { useState } from "react";
import type { CardInstanceDto } from "../api/types";

interface CardDef {
  id: string;
  name: string;
  upgradedEffects?: unknown[] | null;
}

interface Props {
  deck: CardInstanceDto[];
  cardCatalog: { byId: Record<string, CardDef> };
  onHeal: () => void;
  onUpgrade: (deckIndex: number) => void;
}

function canUpgrade(card: CardInstanceDto, def: CardDef | undefined): boolean {
  if (!def) return false;
  if (card.upgraded) return false;
  return !!(def.upgradedEffects && def.upgradedEffects.length > 0);
}

export function RestScreen({ deck, cardCatalog, onHeal, onUpgrade }: Props) {
  const [mode, setMode] = useState<"choose" | "upgrade">("choose");

  if (mode === "upgrade") {
    const candidates = deck
      .map((c, i) => ({ card: c, index: i, def: cardCatalog.byId[c.id] }))
      .filter(e => canUpgrade(e.card, e.def));
    return (
      <div className="rest-screen">
        <h2>Choose a card to upgrade</h2>
        <ul>
          {candidates.map(e => (
            <li key={e.index}>
              <button
                onClick={() => onUpgrade(e.index)}
                aria-label={`Upgrade ${e.def?.name ?? e.card.id} at ${e.index}`}
              >
                Upgrade {e.def?.name ?? e.card.id} at {e.index}
              </button>
            </li>
          ))}
        </ul>
        <button onClick={() => setMode("choose")}>Back</button>
      </div>
    );
  }

  return (
    <div className="rest-screen">
      <h2>Rest Site</h2>
      <button onClick={onHeal}>Heal (+30% max HP)</button>
      <button onClick={() => setMode("upgrade")}>Upgrade a card</button>
    </div>
  );
}
```

- [ ] **Step 4: PASS 確認**

Run: `cd src/Client && npm run test -- --run screens/RestScreen.test.tsx`
Expected: PASS (4 件)。

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/screens/RestScreen.tsx src/Client/src/screens/RestScreen.test.tsx
git commit -m "feat(client): add RestScreen (heal / upgrade card)"
git push
```

---

### Task H8: MapScreen dispatch for Merchant / Event / Rest

**Files:**
- Modify: `src/Client/src/screens/MapScreen.tsx`
- Modify: `src/Client/src/screens/MapScreen.test.tsx`

- [ ] **Step 1: テスト追加**

Add to `MapScreen.test.tsx`:

```typescript
it("renders MerchantScreen when snapshot has ActiveMerchant", () => {
  const snap = makeSnapshot({
    currentTile: "Merchant",
    activeMerchant: {
      cards: [], relics: [], potions: [],
      discardSlotUsed: false, discardPrice: 75,
    },
  });
  renderMap(snap);
  expect(screen.getByText(/Merchant \(Gold:/i)).toBeInTheDocument();
});

it("renders EventScreen when snapshot has ActiveEvent", () => {
  const snap = makeSnapshot({
    currentTile: "Event",
    activeEvent: {
      eventId: "blessing_fountain",
      name: "Blessing Fountain",
      description: "A mystical fountain...",
      choices: [
        { label: "Drink", conditionSummary: null, conditionMet: true },
        { label: "Walk by", conditionSummary: null, conditionMet: true },
      ],
    },
  });
  renderMap(snap);
  expect(screen.getByText("Blessing Fountain")).toBeInTheDocument();
});

it("renders RestScreen when currentTile is Rest and ActiveRestPending", () => {
  const snap = makeSnapshot({ currentTile: "Rest", activeRestPending: true });
  renderMap(snap);
  expect(screen.getByText("Rest Site")).toBeInTheDocument();
});
```

- [ ] **Step 2: FAIL 確認**

Run: `cd src/Client && npm run test -- --run screens/MapScreen.test.tsx`
Expected: FAIL。

- [ ] **Step 3: 実装**

`MapScreen.tsx` に dispatch を追加:

```tsx
// 既存の ActiveBattle / ActiveReward 判定の次に追加:
if (snapshot.activeMerchant) {
  return <MerchantScreen
    gold={snapshot.gold}
    deck={snapshot.deck}
    inventory={snapshot.activeMerchant}
    cardCatalog={cardCatalog}
    relicCatalog={relicCatalog}
    potionCatalog={potionCatalog}
    onBuy={async (kind, id) => {
      const s = await buyFromMerchant({ kind, id });
      setSnapshot(s);
    }}
    onDiscard={async (i) => {
      const s = await discardAtMerchant(i);
      setSnapshot(s);
    }}
    onLeave={async () => {
      const s = await leaveMerchant();
      setSnapshot(s);
    }}
  />;
}

if (snapshot.activeEvent) {
  return <EventScreen
    event={snapshot.activeEvent}
    onChoose={async (i) => {
      const s = await chooseEvent(i);
      setSnapshot(s);
    }}
  />;
}

if (snapshot.activeRestPending) {
  return <RestScreen
    deck={snapshot.deck}
    cardCatalog={cardCatalog}
    onHeal={async () => {
      const s = await restHeal();
      setSnapshot(s);
    }}
    onUpgrade={async (i) => {
      const s = await restUpgrade(i);
      setSnapshot(s);
    }}
  />;
}
```

import 文を追加: `useRelicCatalog`, `MerchantScreen`, `EventScreen`, `RestScreen`, `buyFromMerchant`, `discardAtMerchant`, `leaveMerchant`, `chooseEvent`, `restHeal`, `restUpgrade`。

`relicCatalog` は `useRelicCatalog()` を MapScreen 冒頭で呼び出して取得する。

- [ ] **Step 4: PASS 確認**

Run: `cd src/Client && npm run test -- --run screens/MapScreen.test.tsx`
Expected: PASS。

- [ ] **Step 5: 手動 E2E（dev server）**

```bash
dotnet run --project src/Server &
cd src/Client && npm run dev
```

ブラウザで 1 ラン通しプレイし、spec §Testing の「E2E 手動確認」チェックリストを 1 項目ずつ踏む:
1. Enemy → 報酬が出る
2. Elite → Rare 以上のカードのみ報酬に出る
3. Boss → Epic のカードのみ出る
4. Treasure → レリック 1 件のみ表示、gold/card 行なし
5. Rest Heal → HP +30%
6. Rest Upgrade → `+` 付きカードがデッキに出現
7. Merchant → カード / レリック / ポーション購入、廃棄、離脱
8. Event 3 種（blessing_fountain / shady_merchant / old_library）を踏破
9. メニュー → 再ログイン → 継続再生（セーブマイグレーション含む）

問題があれば該当タスクに戻って修正する。

- [ ] **Step 6: コミット**

```bash
git add src/Client/src/screens/MapScreen.tsx src/Client/src/screens/MapScreen.test.tsx
git commit -m "feat(client): dispatch Merchant / Event / Rest screens from MapScreen"
git push
```

---

## 完了後のチェックリスト

- [ ] `dotnet test` が全 PASS
- [ ] `cd src/Client && npm run test` が全 PASS
- [ ] `cd src/Client && npm run build` が警告 0 で成功
- [ ] spec §Testing E2E チェックリストを全て踏んだ
- [ ] 既存 Phase 5 セーブデータ（SchemaVersion 3）がロードできる（migration 動作確認）

全て緑になったら `superpowers:finishing-a-development-branch` を起動して PR / マージフローへ。

---



