# Phase 10.5.F — Engine 新 actions 実装 (selfDamage / addCard / recoverFromDiscard / gainMaxEnergy / discard.Select)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 10.5.B で formatter spec として整備した新 effect actions を engine 側で実行可能にする。これにより JSON にこれらの effect を持つカードを書いて実プレイで動かせるようになる。

**Architecture:** `Core/Battle/Engine/EffectApplier` の switch に 4 つの新 action ハンドラを追加 + 既存 `discard` を `Select` 対応に拡張。`BattleEventKind` に 3 つの新値 (`AddCard` / `RecoverFromDiscard` / `GainMaxEnergy`) を追加。`BattleState` の既存スキーマで対応可能 (新 field 不要、`EnergyMax` は既存)。`Select="choose"` は本フェーズでは engine が `NotImplementedException` を throw する (UI input flow が必要なので 10.5.M 以降に分割)。

**Tech Stack:** C# .NET 10、xUnit。Server / Client は **本フェーズで変更不要** (新 BattleEventKind を Server DTO がそのまま流すだけ、UI 側は当面 generic 表示で OK)。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §1-2 (10.5.F)

**スコープ外 (別 sub-phase):**
- `Select="choose"` の UI 連携 (player に選ばせる) → **10.5.M (仮称)** で別途
- Variable X 評価 (AmountSource) → 10.5.D
- Power trigger 発火 (Trigger) → 10.5.E
- 新 BattleEventKind に対する Client UI アニメ (当面は generic 表示で OK、後続 polish で対応)

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Battle/Events/BattleEventKind.cs` | Modify | `AddCard` / `RecoverFromDiscard` / `GainMaxEnergy` を末尾追加 |
| `src/Core/Battle/Engine/EffectApplier.cs` | Modify | `Apply` switch に新 action 追加、`ApplySelfDamage` / `ApplyAddCard` / `ApplyRecoverFromDiscard` / `ApplyGainMaxEnergy` 新設、`ApplyDiscard` を `Select` 対応に拡張 |
| `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs` | Modify | 5 アクションごとに xUnit テスト追加 |
| `src/Server/Dtos/BattleEventDto.cs` | (確認のみ) | Kind の string 変換が enum 名前ベースなら自動的に新値が流れる、要検証 |
| `src/Client/src/api/types.ts` | Modify (任意) | `BattleEventKind` 文字列 union に新 3 値を追加 (型補完用、なくても動く) |

---

## Conventions

- **TDD strictly.** テスト → fail → 実装 → green → 次タスク。
- **Build clean.** `dotnet build` 警告 0、`dotnet test` 全件緑。
- **Stale ref 対策.** `FindActor` / `ReplaceActor` を介して actor の最新版を取得 (既存パターン)。
- **Immutable update.** `state with { ... }` で全更新、ImmutableArray.Builder で pile を組む。
- **Default Order=0.** Event の `Order` は EffectApplier 呼出側 (`PlayCardResolver` 等) で再採番されるため、ここでは 0 で OK (既存 ApplyXxx も同様)。
- **InstanceId 採番.** `addCard` は `Guid.NewGuid().ToString("N").Substring(0, 8)` 等の RNG 非依存で衝突予防 (既存 `summon` の paradigm に合わせる、要確認)。テストでは決定論を保つため SystemRng / FakeRng を使う既存ヘルパーに合わせる。
- **エラー方針.** `Select="choose"` は `NotImplementedException("Select=choose requires UI input flow, planned for 10.5.M")` を throw。formatter 経由で文字列は出るが、engine 実行時に明示的にコケる。

---

## Task 1: BattleEventKind に新値追加

**Files:**
- Modify: `src/Core/Battle/Events/BattleEventKind.cs`

### Step 1.1: enum 末尾に追加

- [ ] 既存 enum (`UsePotion=19` まで) の末尾に:

```csharp
public enum BattleEventKind
{
    // ... 既存 ...
    UsePotion = 19,

    // Phase 10.5.F: engine 新 actions
    /// <summary>カードを effect 経由で zone (hand/draw/discard/exhaust) に追加した。</summary>
    AddCard = 20,
    /// <summary>カードを discard pile から hand or exhaust に戻した。</summary>
    RecoverFromDiscard = 21,
    /// <summary>EnergyMax を増加させた (永続)。</summary>
    GainMaxEnergy = 22,
}
```

- [ ] `dotnet build` パス。

---

## Task 2: ApplySelfDamage (TDD)

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Modify: `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`

**スコープ:**
- `selfDamage` は caster (hero) の HP を直接削る。block を貫通するか? StS 慣習では「Lose HP」は block 無視、HP のみ減らす。本実装でも block 無視、HP 直接減算を採用。
- caster 死亡時は `ActorDeath` event を出し、Outcome 確定は呼出側 (PlayCardResolver) に任せる (既存 attack と同じパターン)。

### Step 2.1: テスト

- [ ] 新テスト追加:

```csharp
[Fact]
public void SelfDamage_reduces_caster_hp_ignoring_block()
{
    var hero = BattleFixtures.Hero(currentHp: 50, maxHp: 80);
    hero = hero with { Block = BlockPool.WithFixed(10) };  // block あり
    var state = BattleFixtures.MakeStateWithHero(hero);
    var effect = new CardEffect("selfDamage", EffectScope.Self, null, 5);

    var (after, events) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

    var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
    Assert.Equal(45, heroAfter.CurrentHp);  // 50 - 5、block 無視
    Assert.Contains(events, e => e.Kind == BattleEventKind.DealDamage
        && e.TargetInstanceId == hero.InstanceId
        && e.Amount == 5);
}

[Fact]
public void SelfDamage_kills_caster_emits_actor_death()
{
    var hero = BattleFixtures.Hero(currentHp: 3);
    var state = BattleFixtures.MakeStateWithHero(hero);
    var effect = new CardEffect("selfDamage", EffectScope.Self, null, 5);

    var (after, events) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

    var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
    Assert.Equal(0, heroAfter.CurrentHp);
    Assert.Contains(events, e => e.Kind == BattleEventKind.ActorDeath
        && e.TargetInstanceId == hero.InstanceId);
}
```

(Hero / fixture は既存テストの helper を流用。BattleFixtures に block 付きヘルパーが無ければ既存 setter で組む)

- [ ] fail 確認。

### Step 2.2: 実装

- [ ] EffectApplier.Apply switch に追加:

```csharp
"selfDamage" => ApplySelfDamage(state, caster, effect),
```

- [ ] 新メソッド:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplySelfDamage(
    BattleState state, CombatActor caster, CardEffect effect)
{
    if (effect.Scope != EffectScope.Self)
        throw new InvalidOperationException(
            $"selfDamage requires Scope=Self, got {effect.Scope}");

    var current = FindActor(state, caster.InstanceId) ?? caster;
    int newHp = Math.Max(0, current.CurrentHp - effect.Amount);
    var updated = current with { CurrentHp = newHp };
    var next = ReplaceActor(state, caster.InstanceId, updated);

    var events = new List<BattleEvent>
    {
        new(BattleEventKind.DealDamage, Order: 0,
            CasterInstanceId: caster.InstanceId,
            TargetInstanceId: caster.InstanceId,
            Amount: effect.Amount,
            Note: "selfDamage"),
    };
    if (current.IsAlive && !updated.IsAlive)
    {
        events.Add(new BattleEvent(
            BattleEventKind.ActorDeath, Order: 0,
            TargetInstanceId: caster.InstanceId,
            Note: "selfDamage"));
    }

    return (next, events);
}
```

- [ ] テスト緑。

---

## Task 3: ApplyAddCard (TDD)

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Modify: `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`

**スコープ:**
- 新規 `BattleCardInstance` を生成し、指定 `Pile` (hand/draw/discard/exhaust) に追加。
- `CardRefId` は必須 (null/empty なら throw)。
- `CardRefId` が catalog に存在するかは validation せず、unknown でも instance だけ作って追加 (display は formatter / Card.tsx でフォールバックがある前提)。
- `Amount` 回繰り返して同じカードを N 個追加。
- `IsUpgraded`: 当面 false 固定 (後続で `effect` に upgraded flag を入れるなら拡張)。
- pile=hand で `Hand.Length >= HandCap (10)` の場合は overflow を `DiscardPile` に流す (既存 `DrawHelper.HandCap` 参照)。
- 挿入位置: hand → 末尾追加。draw → 先頭挿入 (top of deck = 次に引かれる)。discard / exhaust → 末尾追加。

### Step 3.1: テスト

- [ ] 4 つの zone それぞれに 1 件ずつ + overflow + missing CardRefId throw:

```csharp
[Fact]
public void AddCard_to_hand_appends_instance()
{
    var state = BattleFixtures.MakeMinimalState();
    var effect = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "hand", CardRefId: "strike");

    var (after, events) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Single(after.Hand);
    Assert.Equal("strike", after.Hand[0].CardDefinitionId);
    Assert.False(after.Hand[0].IsUpgraded);
    Assert.Contains(events, e => e.Kind == BattleEventKind.AddCard);
}

[Fact]
public void AddCard_to_drawpile_inserts_at_top()
{
    var state = BattleFixtures.MakeStateWithDrawPile(new[] { "defend" });
    var effect = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "draw", CardRefId: "strike");

    var (after, events) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Equal(2, after.DrawPile.Length);
    Assert.Equal("strike", after.DrawPile[0].CardDefinitionId);  // top = 先頭
}

[Fact]
public void AddCard_amount_n_creates_n_instances()
{
    var state = BattleFixtures.MakeMinimalState();
    var effect = new CardEffect("addCard", EffectScope.Self, null, 3, Pile: "hand", CardRefId: "strike");

    var (after, _) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Equal(3, after.Hand.Length);
    Assert.All(after.Hand, c => Assert.Equal("strike", c.CardDefinitionId));
    // InstanceId はそれぞれ unique
    Assert.Equal(3, after.Hand.Select(c => c.InstanceId).Distinct().Count());
}

[Fact]
public void AddCard_to_full_hand_overflows_to_discard()
{
    var state = BattleFixtures.MakeStateWithHand(Enumerable.Repeat("strike", 10).ToArray());  // 10 枚 = HandCap
    var effect = new CardEffect("addCard", EffectScope.Self, null, 2, Pile: "hand", CardRefId: "defend");

    var (after, _) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Equal(10, after.Hand.Length);  // 上限維持
    Assert.Equal(2, after.DiscardPile.Length);  // overflow 2 枚
    Assert.All(after.DiscardPile, c => Assert.Equal("defend", c.CardDefinitionId));
}

[Fact]
public void AddCard_missing_card_ref_id_throws()
{
    var state = BattleFixtures.MakeMinimalState();
    var effect = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "hand");  // CardRefId なし

    Assert.Throws<InvalidOperationException>(() =>
        EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog));
}
```

(`MakeStateWithDrawPile`、`MakeStateWithHand` は BattleFixtures に既存 or 追加)

- [ ] テスト fail を確認。

### Step 3.2: 実装

- [ ] EffectApplier.Apply switch に追加:

```csharp
"addCard" => ApplyAddCard(state, caster, effect),
```

- [ ] 新メソッド:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyAddCard(
    BattleState state, CombatActor caster, CardEffect effect)
{
    if (string.IsNullOrEmpty(effect.CardRefId))
        throw new InvalidOperationException(
            "addCard requires non-null CardRefId");
    if (string.IsNullOrEmpty(effect.Pile))
        throw new InvalidOperationException(
            "addCard requires non-null Pile (hand/draw/discard/exhaust)");
    if (effect.Amount <= 0) return (state, Array.Empty<BattleEvent>());

    var s = state;
    int added = 0;
    for (int i = 0; i < effect.Amount; i++)
    {
        var instance = new BattleCardInstance(
            InstanceId: NewBattleInstanceId(effect.CardRefId),
            CardDefinitionId: effect.CardRefId!,
            IsUpgraded: false,
            CostOverride: null);
        s = AddInstanceToPile(s, instance, effect.Pile!);
        added++;
    }

    var ev = new BattleEvent(
        BattleEventKind.AddCard, Order: 0,
        CasterInstanceId: caster.InstanceId,
        Amount: added,
        Note: $"{effect.CardRefId}:{effect.Pile}");
    return (s, new[] { ev });
}

private static BattleState AddInstanceToPile(
    BattleState state, BattleCardInstance instance, string pile) => pile switch
{
    "hand" => state.Hand.Length < DrawHelper.HandCap
        ? state with { Hand = state.Hand.Add(instance) }
        : state with { DiscardPile = state.DiscardPile.Add(instance) },
    "draw" => state with { DrawPile = state.DrawPile.Insert(0, instance) },
    "discard" => state with { DiscardPile = state.DiscardPile.Add(instance) },
    "exhaust" => state with { ExhaustPile = state.ExhaustPile.Add(instance) },
    _ => throw new InvalidOperationException($"Unknown pile: {pile}"),
};

private static string NewBattleInstanceId(string cardRefId)
    => $"{cardRefId}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
```

- [ ] テスト緑。

---

## Task 4: ApplyRecoverFromDiscard (TDD)

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Modify: `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`

**スコープ:**
- discard pile から N 枚を hand or exhaust に移動。
- `Select="all"`: 全枚数を移動 (Amount は無視)。
- `Select="random"`: ランダムに N 枚 (DiscardPile.Count を超えない範囲)。
- `Select="choose"`: `NotImplementedException` (UI input は 10.5.M)。
- `Select` が null なら "random" デフォルト。
- `Pile` は "hand" or "exhaust" のみ受け付け、それ以外は throw。
- pile=hand で overflow したら残りは discard に戻す。

### Step 4.1: テスト

```csharp
[Fact]
public void RecoverFromDiscard_random_to_hand_moves_n_cards()
{
    var state = BattleFixtures.MakeStateWithDiscardPile(new[] { "strike", "defend", "bash" });
    var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 2,
        Pile: "hand", Select: "random");

    var (after, events) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Equal(2, after.Hand.Length);
    Assert.Single(after.DiscardPile);
    Assert.Contains(events, e => e.Kind == BattleEventKind.RecoverFromDiscard
        && e.Amount == 2);
}

[Fact]
public void RecoverFromDiscard_all_to_hand_moves_all()
{
    var state = BattleFixtures.MakeStateWithDiscardPile(new[] { "a", "b" });
    var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 99,
        Pile: "hand", Select: "all");

    var (after, _) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Equal(2, after.Hand.Length);
    Assert.Empty(after.DiscardPile);
}

[Fact]
public void RecoverFromDiscard_to_exhaust_moves_to_exhaust()
{
    var state = BattleFixtures.MakeStateWithDiscardPile(new[] { "a", "b" });
    var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 1,
        Pile: "exhaust", Select: "random");

    var (after, _) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Single(after.ExhaustPile);
    Assert.Single(after.DiscardPile);
}

[Fact]
public void RecoverFromDiscard_choose_throws_not_implemented()
{
    var state = BattleFixtures.MakeStateWithDiscardPile(new[] { "a" });
    var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 1,
        Pile: "hand", Select: "choose");

    Assert.Throws<NotImplementedException>(() =>
        EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog));
}

[Fact]
public void RecoverFromDiscard_empty_discard_returns_no_events()
{
    var state = BattleFixtures.MakeMinimalState();  // discard 空
    var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 3,
        Pile: "hand", Select: "random");

    var (after, events) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Empty(events);
    Assert.Empty(after.Hand);
}
```

### Step 4.2: 実装

- [ ] EffectApplier.Apply switch に追加:

```csharp
"recoverFromDiscard" => ApplyRecoverFromDiscard(state, caster, effect, rng),
```

- [ ] 新メソッド:

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyRecoverFromDiscard(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    if (effect.Pile != "hand" && effect.Pile != "exhaust")
        throw new InvalidOperationException(
            $"recoverFromDiscard requires Pile='hand' or 'exhaust', got '{effect.Pile}'");

    var select = effect.Select ?? "random";
    if (select == "choose")
        throw new NotImplementedException(
            "Select='choose' requires UI input flow, planned for 10.5.M");

    if (state.DiscardPile.Length == 0)
        return (state, Array.Empty<BattleEvent>());

    var discard = state.DiscardPile.ToBuilder();
    var picked = new List<BattleCardInstance>();

    if (select == "all")
    {
        picked.AddRange(discard);
        discard.Clear();
    }
    else // "random"
    {
        int target = Math.Min(effect.Amount, discard.Count);
        for (int i = 0; i < target; i++)
        {
            int idx = rng.NextInt(0, discard.Count);
            picked.Add(discard[idx]);
            discard.RemoveAt(idx);
        }
    }

    if (picked.Count == 0)
        return (state, Array.Empty<BattleEvent>());

    var s = state with { DiscardPile = discard.ToImmutable() };
    foreach (var card in picked)
    {
        s = AddInstanceToPile(s, card, effect.Pile!);
    }

    var ev = new BattleEvent(
        BattleEventKind.RecoverFromDiscard, Order: 0,
        CasterInstanceId: caster.InstanceId,
        Amount: picked.Count,
        Note: $"{select}:{effect.Pile}");
    return (s, new[] { ev });
}
```

- [ ] テスト緑。

---

## Task 5: ApplyGainMaxEnergy (TDD)

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`
- Modify: `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`

**スコープ:**
- `state.EnergyMax += amount`。
- 当ターンの `Energy` も同時に + amount するか? StS 慣習では EnergyMax のみ増、Energy はそのまま (次ターン開始時に EnergyMax まで補充される)。本実装も EnergyMax のみ。
- 永続 (battle 中残存)。

### Step 5.1: テスト

```csharp
[Fact]
public void GainMaxEnergy_increases_energy_max_only()
{
    var state = BattleFixtures.MakeMinimalState() with
    {
        EnergyMax = 3,
        Energy = 1,  // 既に 2 消費済
    };
    var effect = new CardEffect("gainMaxEnergy", EffectScope.Self, null, 1);

    var (after, events) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Equal(4, after.EnergyMax);
    Assert.Equal(1, after.Energy);  // 当ターンの energy は変わらない
    Assert.Contains(events, e => e.Kind == BattleEventKind.GainMaxEnergy
        && e.Amount == 1);
}
```

### Step 5.2: 実装

```csharp
"gainMaxEnergy" => ApplyGainMaxEnergy(state, caster, effect),

private static (BattleState, IReadOnlyList<BattleEvent>) ApplyGainMaxEnergy(
    BattleState state, CombatActor caster, CardEffect effect)
{
    if (effect.Scope != EffectScope.Self)
        throw new InvalidOperationException(
            $"gainMaxEnergy requires Scope=Self, got {effect.Scope}");

    var next = state with { EnergyMax = state.EnergyMax + effect.Amount };
    var ev = new BattleEvent(
        BattleEventKind.GainMaxEnergy, Order: 0,
        CasterInstanceId: caster.InstanceId,
        Amount: effect.Amount);
    return (next, new[] { ev });
}
```

- [ ] テスト緑。

---

## Task 6: discard を Select 対応に拡張 (TDD)

**Files:**
- Modify: `src/Core/Battle/Engine/EffectApplier.cs` (既存 `ApplyDiscard`)
- Modify: `tests/Core.Tests/Battle/Engine/EffectApplierTests.cs`

**スコープ:**
- 既存 `ApplyDiscard` は `Scope=All` で全捨、`Scope=Random` で N 枚ランダム。
- 10.5.B で `CardEffect.Select` field を追加したので、これを優先するロジックに切替:
  - `Select="all"` → 全捨 (Amount 無視、既存 Scope=All と同等)
  - `Select="random"` → N 枚ランダム (既存 Scope=Random と同等)
  - `Select="choose"` → `NotImplementedException`
  - `Select` null → 既存 Scope ベース挙動 (後方互換)
- 既存 JSON に `select` field を持つカードはまだ無いので、後方互換維持で十分。

### Step 6.1: テスト

```csharp
[Fact]
public void Discard_select_all_discards_all_hand()
{
    var state = BattleFixtures.MakeStateWithHand(new[] { "a", "b", "c" });
    var effect = new CardEffect("discard", EffectScope.Self, null, 99, Select: "all");

    var (after, events) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Empty(after.Hand);
    Assert.Equal(3, after.DiscardPile.Length);
    Assert.Contains(events, e => e.Kind == BattleEventKind.Discard
        && e.Amount == 3);
}

[Fact]
public void Discard_select_random_discards_n_random()
{
    var state = BattleFixtures.MakeStateWithHand(new[] { "a", "b", "c" });
    var effect = new CardEffect("discard", EffectScope.Self, null, 2, Select: "random");

    var (after, _) = EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog);

    Assert.Single(after.Hand);
    Assert.Equal(2, after.DiscardPile.Length);
}

[Fact]
public void Discard_select_choose_throws()
{
    var state = BattleFixtures.MakeStateWithHand(new[] { "a", "b" });
    var effect = new CardEffect("discard", EffectScope.Self, null, 1, Select: "choose");

    Assert.Throws<NotImplementedException>(() =>
        EffectApplier.Apply(state, BattleFixtures.Hero(), effect, _rng, _catalog));
}
```

### Step 6.2: 実装

- [ ] `ApplyDiscard` を更新 (既存 Scope ベース挙動は `Select` null のとき走るよう保持):

```csharp
private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDiscard(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    // 10.5.F: Select 優先パス
    if (!string.IsNullOrEmpty(effect.Select))
    {
        return ApplyDiscardWithSelect(state, caster, effect, rng);
    }

    // 既存 Scope ベース (後方互換)
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

private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDiscardWithSelect(
    BattleState state, CombatActor caster, CardEffect effect, IRng rng)
{
    if (effect.Select == "choose")
        throw new NotImplementedException(
            "Select='choose' requires UI input flow, planned for 10.5.M");

    if (state.Hand.Length == 0) return (state, Array.Empty<BattleEvent>());

    var hand = state.Hand.ToBuilder();
    var discard = state.DiscardPile.ToBuilder();
    int target;
    string note;

    if (effect.Select == "all")
    {
        target = hand.Count;
        foreach (var c in hand) discard.Add(c);
        hand.Clear();
        note = "all";
    }
    else // "random" or unknown → random
    {
        target = Math.Min(effect.Amount, hand.Count);
        for (int i = 0; i < target; i++)
        {
            int idx = rng.NextInt(0, hand.Count);
            var card = hand[idx];
            hand.RemoveAt(idx);
            discard.Add(card);
        }
        note = "random";
    }

    return BuildResult(state, caster, hand, discard, target, note);
}
```

- [ ] テスト緑、既存 discard テストも緑のまま。

---

## Task 7: Client types に新 BattleEventKind 追加 (任意だが推奨)

**Files:**
- Modify: `src/Client/src/api/types.ts`

### Step 7.1: 文字列 union 拡張

- [ ] 既存:
```typescript
export type BattleEventKind =
  | 'BattleStart' | 'TurnStart' | 'PlayCard'
  | 'AttackFire' | 'DealDamage' | 'GainBlock'
  | 'ActorDeath' | 'EndTurn' | 'BattleEnd'
  | 'ApplyStatus' | 'RemoveStatus' | 'PoisonTick'
  | 'Heal' | 'Draw' | 'Discard' | 'Upgrade' | 'Exhaust'
  | 'GainEnergy' | 'Summon' | 'UsePotion'
```

- [ ] 末尾追加:
```typescript
  | 'AddCard' | 'RecoverFromDiscard' | 'GainMaxEnergy'
```

- [ ] `npx tsc --noEmit` パス。

### Step 7.2: BattleScreen の event handler 確認

- [ ] BattleScreen.tsx の playSteps event switch に新 kind 追加 (まずは log だけで OK、UI アニメは後続 polish):

```typescript
case 'AddCard':
case 'RecoverFromDiscard':
case 'GainMaxEnergy':
  // 当面は state 反映 (resp.state) で見た目が変わるだけで OK。
  // 個別アニメは後続 sub-phase で追加。
  break
```

(実際は default 分岐が既にあれば追加不要)

---

## Task 8: Self-review + 1 commit + push

### 1. Spec coverage チェック

- [ ] 5 actions (selfDamage / addCard / recoverFromDiscard / gainMaxEnergy / discard.Select) すべて engine 動作 ✓
- [ ] `Select="choose"` は明示的に NotImplementedException で fail (将来 10.5.M で実装) ✓
- [ ] 新 BattleEventKind 3 値が enum に追加 ✓
- [ ] formatter (10.5.B) と整合: 新 action の formatter 出力テキストと、engine 実行が同じ effect spec を解釈する ✓
- [ ] HandCap overflow 時に discard へ流れることを確認 ✓

### 2. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Core 1075 + 新 ~16 / Server 200)
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑 (155)

### 3. Commit + push

- [ ] 1 commit (`feat(core): engine for new effect actions selfDamage/addCard/recoverFromDiscard/gainMaxEnergy/discard.Select (Phase 10.5.F)`)
- [ ] origin master へ push

---

## 完了条件

- [ ] EffectApplier に 4 つの新 action ハンドラ追加 + 既存 discard が Select 対応
- [ ] BattleEventKind に 3 値追加
- [ ] xUnit 新テスト ~16 件、全件緑
- [ ] Server / Client は touch 最小 (Server 自動互換、Client 型 union 拡張のみ)
- [ ] commit + push 済み

## 今回スコープ外

- `Select="choose"` の UI 入力フロー → 10.5.M
- AmountSource 評価 → 10.5.D
- Trigger 発火 → 10.5.E
- 新 BattleEventKind に対する Client UI 演出 → 後続 polish
- `addCard` の `IsUpgraded=true` バリアント → 必要になったら CardEffect に flag 追加

## ロールバック

問題があれば EffectApplier.Apply switch から新 action 行を削除 + ApplyXxx メソッドを削除すれば、formatter spec に該当 effect が出てもデフォルト分岐で無視されて engine は何もしない。BattleEventKind の追加値も使われなくなるだけで害はない。

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md)
- 直前 sub-phase: [`2026-05-01-phase10-5C-battle-color.md`](2026-05-01-phase10-5C-battle-color.md)
- formatter spec: [`2026-05-01-phase10-5B-formatter-v2.md`](2026-05-01-phase10-5B-formatter-v2.md)
