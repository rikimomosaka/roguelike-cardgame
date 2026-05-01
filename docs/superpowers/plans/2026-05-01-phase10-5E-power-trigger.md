# Phase 10.5.E — Power Trigger Engine 実装

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 10.5.B で formatter spec として整備した `CardEffect.Trigger` を engine で発火させる。Power カードの effect が `Trigger="OnTurnStart"` 等を持つとき、対応するイベント (ターン開始時 / カードプレイ時 / ダメージ受け時 / コンボ達成時) で実行されるようにする。

**Architecture:** `Core/Battle/Engine/PowerTriggerProcessor` を新設 (RelicTriggerProcessor の mirror)。`BattleState.PowerCards` にある各カード定義の effects を走査し、`Trigger` 一致で発火、EffectApplier に委譲する。各 trigger ポイントを engine に hook:

- **OnTurnStart**: `TurnStartProcessor.Process` 末尾で fire (既存 OnTurnStart relic fire の隣)
- **OnPlayCard**: `BattleEngine.PlayCard` 末尾、カードが destination pile に格納された後で fire
- **OnDamageReceived**: hero が damage を受ける 3 経路 (`EnemyAttackingResolver` / `TurnStartProcessor.ApplyPoisonTick` / `EffectApplier.ApplySelfDamage`) で fire
- **OnCombo**: `BattleEngine.PlayCard` で combo count 更新後、`ComboMin` 閾値以上なら fire

**Tech Stack:** C# .NET 10、xUnit。Server / Client は本フェーズで変更不要。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-3 Q1, Q4

**スコープ外:**
- OnPlayCard の **card type / id フィルタ** ("Attack カードをプレイした時のみ" 等) — 必要時に CardEffect に filter フィールド追加で拡張
- AmountSource 評価 → 10.5.D
- 新 BattleEventKind (`PowerTrigger` 等) は当面不要、既存 effect 由来 event の Note に `power:{cardId}` prefix を付けて識別する

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Battle/Engine/PowerTriggerProcessor.cs` | Create | RelicTriggerProcessor mirror、PowerCards から `Trigger` 一致 effect を抽出して EffectApplier に委譲 |
| `src/Core/Battle/Engine/TurnStartProcessor.cs` | Modify | Step 8 (OnTurnStart relic fire) の隣で PowerTriggerProcessor.Fire(OnTurnStart) を呼ぶ |
| `src/Core/Battle/Engine/BattleEngine.PlayCard.cs` | Modify | カード resolve 完了後に PowerTriggerProcessor.Fire(OnPlayCard) + combo 確認後に Fire(OnCombo) |
| `src/Core/Battle/Engine/EnemyAttackingResolver.cs` | Modify | hero に damage が入った場合 PowerTriggerProcessor.FireOnDamageReceived |
| `src/Core/Battle/Engine/EffectApplier.cs` | Modify | ApplySelfDamage で hero に damage が入った場合 (※caster=hero) FireOnDamageReceived |
| `src/Core/Battle/Engine/TurnStartProcessor.cs` | Modify | ApplyPoisonTick で hero に damage が入った場合 FireOnDamageReceived |
| `tests/Core.Tests/Battle/Engine/PowerTriggerProcessorTests.cs` | Create | 4 trigger ごとの xUnit テスト (発火条件、effect 適用、複数 power card の順序 等) |
| `tests/Core.Tests/Battle/Engine/BattleEnginePowerIntegrationTests.cs` | Create (任意) | end-to-end (PlayCard → Power 発火確認) integration test |

---

## Conventions

- **TDD strictly.**
- **Build clean.** `dotnet build` 警告 0 / エラー 0、既存テスト全件緑。
- **Pattern mirror.** RelicTriggerProcessor.cs と同じ構造 (FireInternal pattern + caster=hero + 死亡時 break + Note に prefix `power:{cardId}` 付与)。
- **CardEffect.Trigger は string.** 当面 enum にしない (formatter spec 拡張時に新値追加が容易、JSON 直書きも自然)。許可値: `"OnTurnStart"` / `"OnPlayCard"` / `"OnDamageReceived"` / `"OnCombo"`。
- **Trigger 不一致は無視.** `Trigger` が null か empty の effect は通常 (即時) 効果、Trigger 指定があれば対応イベントでだけ発火。
- **Recursion 想定なし.** 現状の effect actions に「カードを play する」action は存在しないため、Power → 別 card play → Power 連鎖は発生しない。将来 `playCard` action が追加されたら別途 recursion guard を検討。
- **OnCombo の閾値は ComboMin field を再利用.** `Trigger="OnCombo"` + `ComboMin=3` で「コンボ 3 以上達成時に発火」。
- **OnPlayCard は self-trigger 許容.** Power card 自身がプレイされた直後の OnPlayCard fire でも、自身の effect (Trigger=OnPlayCard) が発動する仕様。「他のカードがプレイされた時のみ」が必要なら別 sub-phase で filter field を導入。

---

## Task 1: PowerTriggerProcessor を新設 (TDD)

**Files:**
- Create: `src/Core/Battle/Engine/PowerTriggerProcessor.cs`
- Create: `tests/Core.Tests/Battle/Engine/PowerTriggerProcessorTests.cs`

### Step 1.1: テストを先に書く

- [ ] `PowerTriggerProcessorTests.cs` を新規作成。最初は単純 case:

```csharp
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PowerTriggerProcessorTests
{
    [Fact]
    public void OnTurnStart_fires_matching_effect_from_power_card()
    {
        // power_demo: Trigger=OnTurnStart で「カードを 1 枚引く」
        var powerDef = new CardDefinition(
            Id: "power_demo", Name: "デモパワー", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart"),
            },
            UpgradedEffects: null, Keywords: null, UpgradedKeywords: null);

        var hero = BattleFixtures.Hero();
        var instance = new BattleCardInstance("inst1", "power_demo", IsUpgraded: false, CostOverride: null);
        var draw1 = new BattleCardInstance("d1", "strike", IsUpgraded: false, CostOverride: null);
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
            DrawPile = ImmutableArray.Create(draw1),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, BattleFixtures.Rng(), orderStart: 0);

        Assert.Single(after.Hand);
        Assert.Empty(after.DrawPile);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Draw);
        Assert.Contains(events, e => e.Note != null && e.Note.Contains("power:power_demo"));
    }

    [Fact]
    public void Trigger_mismatch_does_not_fire()
    {
        var powerDef = new CardDefinition(
            Id: "p", Name: "p", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("draw", EffectScope.Self, null, 1, Trigger: "OnPlayCard"),
            },
            UpgradedEffects: null, Keywords: null, UpgradedKeywords: null);
        var instance = new BattleCardInstance("i1", "p", IsUpgraded: false, CostOverride: null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
            DrawPile = ImmutableArray.Create(new BattleCardInstance("d1", "strike", false, null)),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, BattleFixtures.Rng(), orderStart: 0);

        Assert.Empty(after.Hand);  // OnTurnStart には反応せず
        Assert.Empty(events);
    }

    [Fact]
    public void Effect_without_trigger_does_not_fire_via_processor()
    {
        // 通常 effect (Trigger=null) は power trigger では発火しない (play 時に既に走っている前提)
        var powerDef = new CardDefinition(
            Id: "p", Name: "p", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] { new("draw", EffectScope.Self, null, 1) },  // Trigger なし
            UpgradedEffects: null, Keywords: null, UpgradedKeywords: null);
        var instance = new BattleCardInstance("i1", "p", IsUpgraded: false, CostOverride: null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, BattleFixtures.Rng(), orderStart: 0);

        Assert.Empty(events);
    }
}
```

- [ ] テスト fail を確認 (PowerTriggerProcessor 未存在)。

### Step 1.2: 実装

- [ ] `src/Core/Battle/Engine/PowerTriggerProcessor.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// PowerCards の各 effect を Trigger 値で発火させる純関数群。RelicTriggerProcessor mirror。
/// 親 spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §1-3 Q1/Q4.
/// </summary>
internal static class PowerTriggerProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Fire(
        BattleState state, string trigger,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, trigger, comboCount: null, catalog, rng, orderStart);
    }

    /// <summary>
    /// OnCombo 専用エントリ。閾値 (ComboMin) 評価で combo count を渡す。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnCombo(
        BattleState state, int comboCount,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, "OnCombo", comboCount, catalog, rng, orderStart);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnDamageReceived(
        BattleState state, DataCatalog catalog, IRng rng, int orderStart)
    {
        return FireInternal(state, "OnDamageReceived", comboCount: null, catalog, rng, orderStart);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireInternal(
        BattleState state, string trigger, int? comboCount,
        DataCatalog catalog, IRng rng, int orderStart)
    {
        var events = new List<BattleEvent>();
        var s = state;
        int order = orderStart;

        var caster = s.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
        if (caster is null || !caster.IsAlive) return (s, events);

        // PowerCards のスナップショットで反復 (Apply 中に PowerCards が変動する可能性に備える)
        var snapshot = s.PowerCards.ToArray();
        foreach (var card in snapshot)
        {
            if (!catalog.Cards.TryGetValue(card.CardDefinitionId, out var def)) continue;
            var effects = card.IsUpgraded && def.UpgradedEffects is not null
                ? def.UpgradedEffects
                : def.Effects;

            foreach (var eff in effects)
            {
                if (string.IsNullOrEmpty(eff.Trigger)) continue;
                if (eff.Trigger != trigger) continue;
                // OnCombo は閾値判定
                if (trigger == "OnCombo")
                {
                    if (comboCount is null) continue;
                    var min = eff.ComboMin ?? 1;
                    if (comboCount.Value < min) continue;
                }

                var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
                s = afterEff;
                foreach (var ev in evs)
                {
                    var basePrefix = $"power:{card.CardDefinitionId}";
                    var newNote = string.IsNullOrEmpty(ev.Note)
                        ? basePrefix
                        : $"{ev.Note};{basePrefix}";
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

- [ ] Step 1.1 のテスト全件緑。

### Step 1.3: 追加テスト (combo, multiple powers, damage)

- [ ] `OnCombo_fires_when_count_meets_threshold` (ComboMin=3 の effect が combo=3 で発火、combo=2 では発火しない)
- [ ] `OnDamageReceived_fires_via_dedicated_entry`
- [ ] `Multiple_power_cards_fire_in_order` (同 trigger で 2 power 持ちの場合、PowerCards 配列順)
- [ ] `Hero_dead_skips_subsequent_powers` (途中で hero 死亡したら以降 skip)

実装は Step 1.2 のロジックで足りるはずなので、テストが緑になることを確認するのみ。

---

## Task 2: TurnStartProcessor に OnTurnStart hook 追加

**Files:**
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs`
- Modify: `tests/Core.Tests/Battle/Engine/TurnStartProcessorTests.cs` (or 新規)

### Step 2.1: テスト

- [ ] integration test: PowerCards に OnTurnStart=draw 1 のカードがある状態で TurnStartProcessor.Process を呼び、hand に 1 枚引かれることを確認。

### Step 2.2: 実装

- [ ] TurnStartProcessor.Process の Step 8 (OnTurnStart relic fire) の **直後** に PowerTriggerProcessor.Fire を呼ぶ:

```csharp
// Step 8: OnTurnStart レリック発動 (10.2.E)
var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
    s, RelicTrigger.OnTurnStart, catalog, rng, orderStart: order);
s = afterRelic;
foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

// Step 8.5: OnTurnStart power カード発動 (10.5.E)
var (afterPower, evsPower) = PowerTriggerProcessor.Fire(
    s, "OnTurnStart", catalog, rng, orderStart: order);
s = afterPower;
foreach (var ev in evsPower) { events.Add(ev with { Order = order++ }); }
```

---

## Task 3: BattleEngine.PlayCard に OnPlayCard / OnCombo hook 追加

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`
- Modify: `tests/Core.Tests/Battle/Engine/BattleEnginePlayCardTests.cs` (新規 or 既存に追加)

### Step 3.1: テスト

- [ ] integration: power カード (OnPlayCard=draw 1) を Played 状態で別カードを play すると hand が増える。
- [ ] OnCombo: power (OnCombo, ComboMin=2) で combo=2 達成時に発火、combo=1 では発火しない。

### Step 3.2: 実装

`PlayCard` 末尾、events に `PlayCard` event 追加した後、destination pile に振り分けた **後** で trigger fire:

```csharp
// 既存 PlayCard 末尾の destination pile assignment 直後に追加:

// 10.5.E: OnPlayCard power 発動
var (afterOnPlay, evsOnPlay) = PowerTriggerProcessor.Fire(
    s, "OnPlayCard", catalog, rng, orderStart: events.Count);
s = afterOnPlay;
events.AddRange(evsOnPlay);

// 10.5.E: OnCombo power 発動 (combo count update 後)
var (afterCombo, evsCombo) = PowerTriggerProcessor.FireOnCombo(
    s, s.ComboCount, catalog, rng, orderStart: events.Count);
s = afterCombo;
events.AddRange(evsCombo);
```

注意点:
- combo count はどこで update されるか確認 (既存 PlayCard 内か別 resolver か) → update **後** に hook する
- order の連番管理は既存 PlayCard と整合させる

---

## Task 4: OnDamageReceived hook を 3 経路に追加

**Files:**
- Modify: `src/Core/Battle/Engine/EnemyAttackingResolver.cs`
- Modify: `src/Core/Battle/Engine/EffectApplier.cs` (`ApplySelfDamage` 内)
- Modify: `src/Core/Battle/Engine/TurnStartProcessor.cs` (`ApplyPoisonTick` 内)

各箇所で hero が damage を受けた直後 (HP 減算後、ActorDeath 判定後) に PowerTriggerProcessor.FireOnDamageReceived を呼ぶ。死亡時は skip (caster=hero 不在)。

### Step 4.1: テスト

- [ ] enemy attack で hero に damage → OnDamageReceived 発火
- [ ] poison tick で hero に damage → OnDamageReceived 発火
- [ ] selfDamage で hero に damage → OnDamageReceived 発火
- [ ] hero に damage が 0 (吸収・回避) なら発火しない

### Step 4.2: 実装

各 damage 経路で:

```csharp
// damage applied to hero, hero still alive → fire
if (target.DefinitionId == "hero" && updated.IsAlive)
{
    var (afterPower, evsPower) = PowerTriggerProcessor.FireOnDamageReceived(
        s, catalog, rng, orderStart: events.Count);
    s = afterPower;
    events.AddRange(evsPower);
}
```

具体的な統合方法は各 resolver の構造による。`EnemyAttackingResolver` 内部メソッドに catalog パスがなければ signature 拡張が必要かも。

---

## Task 5: Self-review + 1 commit + push

### 1. Spec coverage チェック

- [ ] OnTurnStart / OnPlayCard / OnDamageReceived / OnCombo の 4 trigger 全て engine 動作 ✓
- [ ] formatter (10.5.B) と整合: `Trigger="OnTurnStart"` 等の effect が JSON にあれば formatter は `[T:OnTurnStart]の度に` を出し、engine がそれを発火 ✓
- [ ] PowerCards に複数 power がある場合、配列順に発火 ✓
- [ ] hero 死亡時 / 不在時は skip ✓

### 2. Engine 整合性

- [ ] 既存 RelicTriggerProcessor 動作 (relic OnTurnStart/OnCardPlay/OnEnemyDeath 等) が壊れていない
- [ ] 既存 BattleEnginePlayCard 動作が後方互換 (Trigger 持たない既存カードは挙動変化なし)

### 3. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Core 1096 + 新 ~10 / Server 200)
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑 (155)

### 4. Commit + push

- [ ] 1 commit (`feat(core): power trigger engine for OnTurnStart/OnPlayCard/OnDamageReceived/OnCombo (Phase 10.5.E)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] `PowerTriggerProcessor` が Core にあり、4 trigger 全てに対応
- [ ] TurnStartProcessor / BattleEngine.PlayCard / EnemyAttackingResolver / EffectApplier.ApplySelfDamage / TurnStartProcessor.ApplyPoisonTick の 5 hook 点で fire 呼出
- [ ] xUnit 新テスト ~10 件、全件緑
- [ ] commit + push 済み

## 今回スコープ外

- OnPlayCard の card type / id filter (将来 CardEffect に filter field 追加で拡張)
- AmountSource 評価 → 10.5.D
- 新 BattleEventKind (`PowerTrigger` 等) — 既存 effect 由来 event の Note prefix で識別、UI 演出は後続 polish
- ally / summon の power card 対応 — 当面 hero のみ

## ロールバック

万一不具合があれば `PowerTriggerProcessor.Fire` 呼出を全 hook 点でコメントアウトすれば、power card は通常 effect (Trigger なし) しか発動しない状態に戻る。最終的に PowerTriggerProcessor.cs を削除すれば完全 revert。

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5F-engine-actions.md`](2026-05-01-phase10-5F-engine-actions.md)
- 参考: `src/Core/Battle/Engine/RelicTriggerProcessor.cs` (mirror パターン)
