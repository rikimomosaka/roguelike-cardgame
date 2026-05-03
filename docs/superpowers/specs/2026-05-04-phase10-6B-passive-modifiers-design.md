# Phase 10.6.B: Passive Modifier System Design

**Status:** Draft (2026-05-04)
**Phase:** 10.6.B
**Predecessor:** Phase 10.6.A (relic run-flow trigger 拡張、`Trigger == "Passive"` enum 値は L1.5 で定義済だが engine 側は `restHealBonus` 1 アクションのみ対応)
**Successor candidates:** 10.6.B-Reroll (合流済 — 本 phase に統合)、Phase 9 (multiplayer) で Unknown 共有解決を再検討

---

## 1. Overview / Goals

Phase 10.6.B は「**常時補正型 (passive modifier)**」relic を engine 側で扱える基盤を導入する。Phase 10.6.A で 5 つの run-flow trigger を実装したのに続き、Phase 10.5.L1.5 で定義されながら未実装だった `Trigger == "Passive"` の評価ロジックを一般化し、複数の modifier action を載せる。

具体的には以下 3 種のメカニズムを提供する:

1. **加算 modifier** (`energyPerTurnBonus`, `cardsDrawnPerTurnBonus`, `rewardCardChoicesBonus`, `restHealBonus` 既存): base 値に owned relic の `amount` を合算
2. **乗算 modifier** (`goldRewardMultiplier`, `shopPriceMultiplier`): base 値に `(100 + Σdelta)%` を乗算
3. **capability flag** (`rewardRerollAvailable`): 「relic を持っているなら新 action を使えるようにする」フラグ
4. **重み補正** (`unknownXxxWeightDelta` × 5 種別): `UnknownResolutionConfig.Weights` に加算

加えて、relic 効果の柔軟性を確保するため **Unknown タイル解決を lazy 化** (map 生成時に決め打ちせず、入場時に relic modifier 反映で解決) する。

最後に **報酬リロール** を Passive capability + 既存報酬フローへの拡張として実装する (報酬 1 回につき card choices を 1 度だけ再抽選可能)。

---

## 2. Out of Scope (本 phase 非対応)

- **Damage / Block の常時加算** (`damageDealtBonus` / `blockGainedBonus`): 既存 status (Strength / Dexterity) と二重表現になり UX が混乱する。常時 +N ダメ/ブロック の relic は `OnBattleStart + applyStatus(strength/dexterity, +N)` で表現する (既存トリガで実現可能)。
- **Power card の Passive trigger 対応**: Power の常時補正は status system (Strength 等) で表現する慣行を維持。`Trigger == "Passive"` は relic 専用、Power の同 trigger は engine 側で発火しない (バリデータでも弾かない loose policy 維持)。
- **Cost modifier** (アップグレード済カードのコスト -1 系): 設計余地あるが MVP 外。
- **状態異常持続時間補正**: 設計余地あるが MVP 外。
- **手札上限 +N**, **マップ先読み +N**, **ボス報酬選択肢 +N** など: 必要になれば façade に 1 メソッド追加で済む構造になっているので個別 phase 対応。
- **Power card のリロール**: 報酬リロールは「battle 終了時の card choices」のみ。treasure / event / boss の reward に対する reroll は対応しない。

---

## 3. Design Decisions

### Q1. Scope

MVP scope は 11 modifier action + reroll mechanic (Section 4 参照)。

### Q2. Multiplier Stacking

**加算合成 (additive)**。複数 relic の delta を `Σ` し、`base * (100 + Σdelta) / 100` を 1 回適用。

例:
- relic A: `goldRewardMultiplier: +50`、relic B: `goldRewardMultiplier: +30` → `Σ = 80` → `gold * 1.80`
- relic A: `shopPriceMultiplier: -20`、relic B: `shopPriceMultiplier: -30` → `Σ = -50` → `price * 0.50`

**Why:** 直感的、デバッグ容易、UI で「+80%」のような単一表記が成立。乗算合成 (compound) は破綻しやすい。

### Q3. Relic-only Passive

`Trigger == "Passive"` の評価対象は **relic のみ**。Power card に同 trigger が書かれても無視 (engine が `RunState.Relics` だけを query する設計)。

**Why:** Power の常時補正 (Strength / Dexterity / Weak / Vulnerable) は既存 status system で完成しており、再実装すると二重表現になる。Passive は run-wide 永続補正の意味論に集中させる。

### Q4. Reward Card Choices Bonus (#1 追加)

`rewardCardChoicesBonus`: 加算 int、battle 終了 reward の card choices 数 (default 3) を `3 + Σbonus` に補正。床 1。

### Q5. Reward Reroll (#2 追加)

`rewardRerollAvailable` (Passive capability flag) + 既存 reward フローに reroll mechanism を追加。詳細 Section 8。

### Q6. Unknown Tile Resolution — Lazy (#3 案 B)

**現状:** `UnknownResolver.ResolveAll` が map 生成時に全 Unknown タイルを一括解決し `RunState.UnknownResolutions[nodeId] = TileKind` として固定化。

**変更後:** map 生成では Unknown を解決しない。`NodeEffectResolver.Resolve` で `TileKind.Unknown` 分岐に入った瞬間に、現在の relic modifier を考慮して 1 ノード分だけ解決し `UnknownResolutions[currentNodeId]` に cache。

**Why:** relic 取得後に効果が発動するためには「解決時点の relic 状態」を参照する必要がある。pre-resolve だと map 生成時の relic 状態に固定される。

**プレイヤー UX への影響:** プレイヤーは元々 Unknown タイルの中身を見られない (UI 上「？」表示) ので、解決タイミングが map 生成時か入場時かは不可視。決定論的シード再現の意味論は変わるが、本作では外部マップ解析機構がないので問題なし。

### Q7. Multiplayer Forward-Compat (Phase 9 想定)

将来のマルチプレイ実装時、Unknown 解決は **X2 (全プレイヤーの relic を合算して共有解決)** が想定される (cooperatve なゲームデザインで自然)。lazy resolve は X2 への移行を阻害しない (むしろ pre-resolve より親和性が高い):

- pre-resolve: map 生成時 (= host のみ) に解決完了 → ゲスト relic を反映できない
- lazy resolve: ノード入場時に「全プレイヤーの relic を合算して」解決可能

Phase 10.6.B の `PassiveModifiers.ApplyUnknownWeightDeltas(config, runState, catalog)` は単一 `RunState` を受ける形だが、将来 `RunState[]` (複数プレイヤー) を受けるように拡張する場合、内部の「`s.Relics` をループ」を「全 RunState の Relics を flatten してループ」に変えるだけで済む。

---

## 4. Action Catalog

### 4-1. Passive Modifier Actions (11 個)

| Action | 種別 | Amount 単位 | 例 | 計算式 | Hook |
|---|---|---|---|---|---|
| `energyPerTurnBonus` | 加算 | int | `1`, `-1` | `EnergyMax = baseMax + Σamount` | Battle 開始時 snapshot |
| `cardsDrawnPerTurnBonus` | 加算 | int | `1`, `2` | `DrawPerTurn = 5 + Σamount` | Battle 開始時 snapshot |
| `goldRewardMultiplier` | ×系 delta | int (%) | `+50`, `-20` | `gold = max(0, base * (100 + Σamount) / 100)` | 5 reward 生成サイト |
| `shopPriceMultiplier` | ×系 delta | int (%) | `-20`, `+30` | `price = max(1, base * (100 + Σamount) / 100)` | MerchantInventoryGenerator |
| `rewardCardChoicesBonus` | 加算 | int | `+1`, `-1` | `count = max(1, 3 + Σamount)` | RewardGenerator.GenerateFromEnemy |
| `rewardRerollAvailable` | capability | int (>0 で有効) | `1` | `Σamount > 0` で reroll button 有効 | RewardActions.Reroll 内 capability check |
| `unknownEnemyWeightDelta` | 加算 | int (重み) | `+5`, `-3` | `weight = max(0, baseWeight + Σamount)` | NodeEffectResolver.Unknown 分岐 |
| `unknownEliteWeightDelta` | 加算 | int (重み) | 同上 | 同上 | 同上 |
| `unknownMerchantWeightDelta` | 加算 | int (重み) | 同上 | 同上 | 同上 |
| `unknownRestWeightDelta` | 加算 | int (重み) | 同上 | 同上 | 同上 |
| `unknownTreasureWeightDelta` | 加算 | int (重み) | 同上 | 同上 | 同上 |

### 4-2. 既存 Passive Action (1 個、移動のみ)

| Action | 種別 | Amount | 計算 |
|---|---|---|---|
| `restHealBonus` | 加算 | int (HP) | `heal = baseHeal + Σamount` |

`NonBattleRelicEffects.ApplyPassiveRestHealBonus` を新ファイル `PassiveModifiers.cs` に移動 (or 委譲)。`RestActions.Heal` の call site は新ロケーションに切替。

### 4-3. Reroll Mechanic (Passive 系外の追加コンポーネント)

| 項目 | 内容 |
|---|---|
| `RewardState.RerollUsed: bool` | 新 field、default `false` |
| `RewardActions.Reroll(s, catalog, rng)` | CardChoices だけ再抽選、`RerollUsed = true` セット |
| Server endpoint | `POST /runs/{id}/active-reward/reroll-card-choices` |
| Client UI | 報酬画面リロールボタン (capability && !RerollUsed の時表示) |

---

## 5. Architecture

### 5-1. New File: `src/Core/Relics/PassiveModifiers.cs`

公開 façade + 内部 helper の集約場所。

```csharp
public static class PassiveModifiers
{
    // 加算系
    public static int ApplyEnergyPerTurnBonus(int @base, RunState s, DataCatalog catalog);
    public static int ApplyCardsDrawnPerTurnBonus(int @base, RunState s, DataCatalog catalog);
    public static int ApplyRewardCardChoicesBonus(int @base, RunState s, DataCatalog catalog);
    public static int ApplyPassiveRestHealBonus(int @base, RunState s, DataCatalog catalog); // 既存移動

    // ×系
    public static int ApplyGoldRewardMultiplier(int @base, RunState s, DataCatalog catalog);
    public static int ApplyShopPriceMultiplier(int @base, RunState s, DataCatalog catalog);

    // Capability
    public static bool HasPassiveCapability(string action, RunState s, DataCatalog catalog);

    // Unknown 重み補正 (5 種別を 1 関数で処理)
    public static ImmutableDictionary<TileKind, double> ApplyUnknownWeightDeltas(
        UnknownResolutionConfig config, RunState s, DataCatalog catalog);

    // 内部 helper
    private static int SumPassiveBonus(string action, RunState s, DataCatalog catalog);
    private static int SumPassiveMultiplierDelta(string action, RunState s, DataCatalog catalog);
}
```

すべての公開メソッドは:
- `s.Relics` を loop
- `catalog.TryGetRelic(id, out def)` で取得失敗ノード silent skip
- `def.Implemented == false` skip
- `def.Effects` 内で `eff.Trigger == "Passive" && eff.Action == <target>` をフィルタ
- amount を集計 / フラグ判定

### 5-2. Architecture: Lazy Unknown Resolve

**変更前 (現状):**
```
Run start → DungeonMapGenerator.Generate → UnknownResolver.ResolveAll → RunState.UnknownResolutions が完全 dictionary
NodeEffectResolver.Resolve(state, kind=Unknown, ...) → throw "should be pre-resolved"
```

**変更後:**
```
Run start → DungeonMapGenerator.Generate → UnknownResolutions = Empty
NodeEffectResolver.Resolve(state, kind=Unknown, ...):
  if state.UnknownResolutions.TryGetValue(nodeId, out cached): dispatch with cached
  else:
    weights = PassiveModifiers.ApplyUnknownWeightDeltas(config, state, catalog)
    resolved = UnknownResolver.ResolveOne(weights, rng)
    state' = state with { UnknownResolutions = state.UnknownResolutions.SetItem(nodeId, resolved) }
    return Resolve(state', resolved, currentRow, data, rng)  // 一段再帰
```

`UnknownResolutions` は「**解決済キャッシュ**」となる (完全 dictionary → 部分 dictionary)。

---

## 6. Engine Integration Points

### 6-1. Battle 内 modifier (B-1)

**`BattleEngine.Start`** (具体行は実装時 grep): battle 開始時に `EnergyMax` と `DrawPerTurn` を modifier 適用済値で snapshot:

```csharp
int baseEnergy = catalog.Characters[runState.CharacterId].EnergyMax; // or wherever
int energyMaxFinal = PassiveModifiers.ApplyEnergyPerTurnBonus(baseEnergy, runState, catalog);
int baseDraw = TurnStartProcessor.DrawPerTurn; // 5
int drawFinal = PassiveModifiers.ApplyCardsDrawnPerTurnBonus(baseDraw, runState, catalog);
// → BattleState 生成時に EnergyMax と新規 DrawPerTurn フィールドにセット
```

**`BattleState`**: 既存の `EnergyMax` field は活用。新規 `DrawPerTurn: int` field 追加 (battle 中 immutable)。

**`TurnStartProcessor.cs:68`**: `DrawHelper.Draw(s, DrawPerTurn, ...)` を `Draw(s, s.DrawPerTurn, ...)` に変更 (定数参照を BattleState field 参照に切替)。const `DrawPerTurn = 5` は base default 値として保持。

**Why snapshot at battle start (案 a):** battle 中に relic は変動しない前提なので 1 回 calc で十分。lazy 評価だと毎ターン query する overhead が無駄。

### 6-2. Run-flow modifier (B-2)

**Reward gold multiplier — 新ヘルパ `RewardActions.AssignReward` 経由で集約:**

新規ファイル `src/Core/Rewards/RewardActions.cs` を作成 (既存 `RewardApplier` とは責務分離 — Apply は player action、`RewardActions` は internal flow control):
```csharp
public static class RewardActions
{
    public static RunState AssignReward(RunState s, RewardState reward, RewardRngState newRng, DataCatalog catalog)
    {
        var goldAdjusted = PassiveModifiers.ApplyGoldRewardMultiplier(reward.Gold, s, catalog);
        var rewardWithGold = reward with { Gold = goldAdjusted };
        var s1 = s with { ActiveReward = rewardWithGold, RewardRngState = newRng };
        return NonBattleRelicEffects.ApplyOnRewardGenerated(s1, catalog);
    }
}
```

これで:
- `goldRewardMultiplier` 適用が 1 箇所
- `OnRewardGenerated` 発火も 1 箇所 (T8 review で指摘された inline duplication が解消)

5 reward 生成サイトを `AssignReward` 経由に切替 (Phase 10.6.A T8 で確認済の 5 サイト):
- `NodeEffectResolver.StartTreasure` (Core: Treasure tile)
- `BossRewardFlow.Resolve` (Core: Boss reward flow、両 controller から共通呼び出し)
- `EventResolver.GrantCardReward` (Core: Event の card reward 付与時)
- `BattleController` (Server: battle 終了の non-boss path)
- `RunsController` (Server: battle 終了の non-boss path)

**Shop price multiplier:**

`MerchantInventoryGenerator.Generate` を改修。各 offer の price と DiscardPrice に `PassiveModifiers.ApplyShopPriceMultiplier` を適用:

```csharp
public static MerchantInventory Generate(DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng)
{
    var cards = PickCards(catalog, prices, s, rng, CardCount);
    var relics = PickRelics(catalog, prices, s, rng, RelicCount);
    var potions = PickPotions(catalog, prices, rng, PotionCount);
    int rawDiscard = prices.DiscardSlotPrice + DiscardPriceIncrement * s.DiscardUsesSoFar;

    cards = cards.Select(o => o with { Price = PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog) }).ToImmutableArray();
    relics = relics.Select(o => o with { Price = PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog) }).ToImmutableArray();
    potions = potions.Select(o => o with { Price = PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog) }).ToImmutableArray();
    int discardPrice = PassiveModifiers.ApplyShopPriceMultiplier(rawDiscard, s, catalog);

    return new MerchantInventory(cards, relics, potions, DiscardSlotUsed: false, DiscardPrice: discardPrice);
}
```

**Reward card choices bonus:**

`RewardGenerator.GenerateFromEnemy` line 131: `while (picks.Count < 3)` を:

```csharp
int targetCount = Math.Max(1, PassiveModifiers.ApplyRewardCardChoicesBonus(3, runState, catalog));
while (picks.Count < targetCount) { ... }
```

ただし `RewardGenerator.GenerateFromEnemy` は現在 `RunState` を受け取っていないので、shape 変更が必要 (caller も追従)。

### 6-3. Lazy Unknown Resolve (B-3)

**`RunStartService.cs`**: line 64 と line 118 の `UnknownResolver.ResolveAll` 呼び出しを **削除**。`unknownResolutions` を `ImmutableDictionary<int, TileKind>.Empty` で run/act 開始。

**`UnknownResolver.cs`**: 新メソッド追加:
```csharp
public static TileKind ResolveOne(ImmutableDictionary<TileKind, double> weights, IRng rng)
{
    var entries = weights.Where(kv => kv.Value > 0).ToArray();
    double totalWeight = entries.Sum(kv => kv.Value);
    if (totalWeight <= 0) throw new MapGenerationConfigException("All Unknown weights are zero");
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

`ResolveAll` は test 用に保持 (将来的に削除候補だが MVP では残す)。

**`NodeEffectResolver.cs:48`**: `TileKind.Unknown => throw` を以下に置換:
```csharp
TileKind.Unknown => ResolveUnknownAndDispatch(state, currentRow, data, rng),
```

```csharp
private static RunState ResolveUnknownAndDispatch(RunState state, int currentRow, DataCatalog data, IRng rng)
{
    int nodeId = state.CurrentNodeId;
    if (state.UnknownResolutions.TryGetValue(nodeId, out var cached))
        return Resolve(state, cached, currentRow, data, rng);
    // 未解決 → modifier 適用後に解決
    var weights = PassiveModifiers.ApplyUnknownWeightDeltas(data.UnknownConfig, state, data); // data.UnknownConfig は実装時に確認
    // 全 weight 0 fallback: 元 config に戻す
    if (weights.Values.Sum() <= 0) weights = data.UnknownConfig.Weights;
    var resolved = UnknownResolver.ResolveOne(weights, rng);
    var newState = state with { UnknownResolutions = state.UnknownResolutions.SetItem(nodeId, resolved) };
    return Resolve(newState, resolved, currentRow, data, rng);
}
```

(注: `data.UnknownConfig` の正確なフィールド名は実装時に grep で確認。`MapGenerationConfig.UnknownResolutionWeights` 等の可能性あり。)

**`RunState.Validate`** (line 154-): 既存「value must not be Unknown」チェックは維持 (lazy 後も resolved 値しか書かない)。`UnknownResolutions.IsEmpty` を許容。

**`JourneyLogger.cs:17`**: 既に `TryGetValue` で null fallback 実装済 → 修正不要 (場合による)。

### 6-4. Reroll Mechanic (B-4)

**`RewardState.cs`**: field 追加:
```csharp
public sealed record RewardState(
    // 既存 fields ...
    bool RerollUsed = false  // 新 field、default false で migration 不要
);
```

**新メソッド `RewardActions.Reroll`** (Section 6-2 で導入する `RewardActions.cs` に追加、既存 `RewardApplier` とは責務分離):
```csharp
public static RunState Reroll(RunState s, DataCatalog catalog, IRng rng)
{
    var r = s.ActiveReward ?? throw new InvalidOperationException("No ActiveReward");
    if (r.CardStatus != CardRewardStatus.Pending)
        throw new InvalidOperationException("Card already resolved, cannot reroll");
    if (r.RerollUsed)
        throw new InvalidOperationException("Reroll already used for this reward");
    if (!PassiveModifiers.HasPassiveCapability("rewardRerollAvailable", s, catalog))
        throw new InvalidOperationException("No relic grants reward reroll");

    // 既存 GenerateFromEnemy の card 抽選ロジックを内部 helper に切り出し、再呼び出し
    var newPicks = RewardGenerator.RegenerateCardChoicesForReward(r, s, catalog, rng);
    return s with {
        ActiveReward = r with {
            CardChoices = newPicks,
            RerollUsed = true,
        }
    };
}
```

`RewardGenerator.RegenerateCardChoicesForReward` は既存 `GenerateFromEnemy` の card 抽選部分を切り出した internal helper。引数で `rewardCardChoicesBonus` を考慮した枚数 + base catalog を受け取る。

**Server**: `POST /runs/{id}/active-reward/reroll-card-choices` endpoint (`RunsController`):
```csharp
[HttpPost("{id}/active-reward/reroll-card-choices")]
public async Task<IActionResult> RerollCardChoices(string id, CancellationToken ct)
{
    var s = await _saves.LoadAsync(...);
    var rng = new SystemRng(/* seed derived from RewardRngState */);
    s = RewardActions.Reroll(s, _data, rng);
    await _saves.SaveAsync(...);
    return Ok(BattleStateDtoMapper.Map(s));
}
```

**Client**: `RewardModal.tsx` (or 該当コンポーネント):
- relic capability + `!rerollUsed` 時に「リロール」ボタン表示
- click → `apiClient.rerollCardChoices(runId)` → state 更新

---

## 7. JSON Schema + Formatter

### 7-1. Relic JSON 例

```json
{
  "id": "energy_charm",
  "name": "エネルギーの護符",
  "rarity": "Rare",
  "implemented": true,
  "effects": [
    { "trigger": "Passive", "action": "energyPerTurnBonus", "scope": "Self", "amount": 1 }
  ]
}

{
  "id": "merchant_loyalty_card",
  "name": "商人の名簿",
  "rarity": "Common",
  "implemented": true,
  "effects": [
    { "trigger": "Passive", "action": "shopPriceMultiplier", "scope": "Self", "amount": -20 }
  ]
}

{
  "id": "lucky_die",
  "name": "幸運のサイコロ",
  "rarity": "Rare",
  "implemented": true,
  "effects": [
    { "trigger": "Passive", "action": "rewardRerollAvailable", "scope": "Self", "amount": 1 }
  ]
}

{
  "id": "treasure_seeker_amulet",
  "name": "宝物探しの護符",
  "rarity": "Epic",
  "implemented": true,
  "effects": [
    { "trigger": "Passive", "action": "unknownTreasureWeightDelta", "scope": "Self", "amount": 5 },
    { "trigger": "Passive", "action": "unknownEnemyWeightDelta", "scope": "Self", "amount": -3 }
  ]
}
```

`scope: "Self"` は Passive では実質意味を持たないが、`CardEffect` schema との整合性で必須。validator は警告も出さない (loose policy)。

### 7-2. Formatter Auto-Generated Text (Passive 対応)

`CardTextFormatter` で `Trigger == "Passive"` の effect を以下に変換 (trigger プレフィックスなし):

| Action | 表示テキスト |
|---|---|
| `energyPerTurnBonus: +N` | **エナジー最大値 +N** |
| `cardsDrawnPerTurnBonus: +N` | **ターン開始時の手札枚数 +N** |
| `goldRewardMultiplier: +N` | 戦闘ゴールド報酬 +N% |
| `goldRewardMultiplier: -N` | 戦闘ゴールド報酬 -N% |
| `shopPriceMultiplier: -N` | ショップ価格 -N% |
| `shopPriceMultiplier: +N` | ショップ価格 +N% |
| `rewardCardChoicesBonus: +N` | カード報酬選択肢 +N 枚 |
| `rewardCardChoicesBonus: -N` | カード報酬選択肢 -N 枚 |
| `rewardRerollAvailable: 1` | カード報酬を 1 回リロール可能 |
| `unknownEnemyWeightDelta: ±N` | ハテナマスの敵戦闘出現率 ±N |
| `unknownEliteWeightDelta: ±N` | ハテナマスのエリート戦闘出現率 ±N |
| `unknownMerchantWeightDelta: ±N` | ハテナマスのショップ出現率 ±N |
| `unknownRestWeightDelta: ±N` | ハテナマスの休憩所出現率 ±N |
| `unknownTreasureWeightDelta: ±N` | ハテナマスの宝箱出現率 ±N |
| `restHealBonus: +N` | 休憩所での回復 +N (既存) |

複数 Passive effect は改行で並べる。Trigger プレフィックス (`バトル開始時、` 等) は付与しない (Passive は常時)。

### 7-3. DevMetaController 更新

[DevMetaController.cs](src/Server/Controllers/DevMetaController.cs) の `actions` リストに 11 個追加 (上記 Action カタログから)。`triggers` リストは既に `Passive` 含むので変更不要。

action × trigger validator は導入しない (loose policy 維持)。

---

## 8. Edge Cases / Clamps

| ケース | 挙動 |
|---|---|
| `goldRewardMultiplier` 合計 ≤ -100% | `gold = 0` (床) |
| `shopPriceMultiplier` 合計 ≤ -100% | `price = 1 gold` (床) |
| `rewardCardChoicesBonus` 合計 ≤ -2 | `count = 1` (床、selection を空にしない) |
| `rewardCardChoicesBonus` で枚数増 > 利用可能 reward カード数 | RewardGenerator の既存 pool 枯渇ロジックに任せる (`continue` で抜ける) |
| `energyPerTurnBonus` で `EnergyMax ≤ 0` | `EnergyMax = 0` (床、意図的 debuff として許容) |
| `cardsDrawnPerTurnBonus` で `DrawPerTurn ≤ 0` | `DrawPerTurn = 0` (床) |
| Unknown 全 weight delta で全カテゴリ weight = 0 | 元 `UnknownResolutionConfig.Weights` で抽選 (defensive fallback) |
| Reroll 試行時 `RerollUsed == true` | `InvalidOperationException` |
| Reroll 試行時 capability なし | `InvalidOperationException` |
| Reroll 試行時 `CardStatus != Pending` | `InvalidOperationException` |

---

## 9. Migration / Schema Versioning

- `RewardState.RerollUsed: bool = false` 追加 → record default value で旧 save 互換。SchemaVersion bump 不要。
- `UnknownResolutions` の部分的 dictionary 許容 → 既存 save data は完全 dictionary なので問題なし (lazy resolve 後も既存解決値は保持される)。
- `BattleState` の新規 `DrawPerTurn` field → battle 中の永続化フォーマットがあれば確認。現状 `BattleState` は in-memory only と思われるので migration 不要。実装時に確認。

---

## 10. Testing Strategy

| Layer | Coverage | 期待件数 |
|---|---|---|
| `PassiveModifiers` 単体 | 各 façade メソッド (加算 / ×系 / capability / unknown deltas)、零relic / 単一 / 複数 / `Implemented:false` ガード | 12〜15 |
| Battle integration | `BattleEngine.Start` で `EnergyMax` / `DrawPerTurn` modifier 反映、TurnStartProcessor 引数切替後の挙動不変 | 4 |
| Reward generation | 5 サイトの `goldRewardMultiplier` 適用 + `rewardCardChoicesBonus` で枚数変化、`AssignReward` 経由で `OnRewardGenerated` 発火 | 6 |
| Merchant | `shopPriceMultiplier` で全 offer + DiscardPrice 変動、床1 適用 | 3 |
| Reroll | RewardActions.Reroll 各 error path + 成功 path、capability チェック、RerollUsed フラグ管理 | 5 |
| Lazy Unknown resolve | NodeEffectResolver で Unknown 入場時に解決 + cache、relic modifier 反映、`UnknownResolutions` 部分的 dict で `Validate()` 通る、全 weight 0 fallback | 5 |
| Formatter | 各 Passive action の自動文言出力 (修正後文言含む) | 11 |
| Server / Client integration | reroll endpoint、UI button visibility | 3〜5 |
| **合計** | | **~50 件** |

---

## 11. Sub-task Breakdown

| # | Task | 主な変更ファイル | 依存 |
|---|---|---|---|
| **T1** | `PassiveModifiers.cs` 新規 + 内部 helper + façade 全メソッド + `restHealBonus` 移動 | `PassiveModifiers.cs` (新), `NonBattleRelicEffects.cs`, `RestActions.cs` (call site 切替) | — |
| **T2** | Formatter Passive 対応 (修正後文言含む 11 action) | `CardTextFormatter.cs`, formatter tests | T1 |
| **T3** | Battle 内 modifier (`energyPerTurnBonus` / `cardsDrawnPerTurnBonus`): `BattleState.DrawPerTurn` 追加、`BattleEngine.Start` で焼き込み、`TurnStartProcessor` で参照切替 | `BattleState.cs`, `BattleEngine.*.cs`, `TurnStartProcessor.cs` | T1 |
| **T4** | `MerchantInventoryGenerator` で `shopPriceMultiplier` 適用 (cards/relics/potions/discardPrice) | `MerchantInventoryGenerator.cs` | T1 |
| **T5** | `RewardGenerator.GenerateFromEnemy` で `rewardCardChoicesBonus` 適用 + 床1 + `RegenerateCardChoicesForReward` 切り出し (T7 用) | `RewardGenerator.cs` | T1 |
| **T6** | `RewardActions.AssignReward` 集約ヘルパ新規。5 reward 生成サイト経由切替 (`goldRewardMultiplier` 適用 + `OnRewardGenerated` 発火集約、T8 inline duplicate 整理) | `RewardActions.cs` (新), `NodeEffectResolver.cs`, `BossRewardFlow.cs`, `EventResolver.cs`, `BattleController.cs`, `RunsController.cs` | T1 |
| **T7** | Reroll mechanic: `RewardState.RerollUsed`, `RewardActions.Reroll`, `HasPassiveCapability` 連携, Server endpoint, Client UI button | `RewardState.cs`, `RewardActions.cs` (T6 で新規作成済), `PassiveModifiers.cs`, `RunsController.cs`, `RewardModal.tsx` | T1, T5, T6 |
| **T8** | Lazy Unknown resolve: `ResolveAll` 呼び出し削除, `ResolveOne` 新メソッド, `NodeEffectResolver` Unknown 分岐再構成, `UnknownResolutions` 部分的許容, `PassiveModifiers.ApplyUnknownWeightDeltas` | `RunStartService.cs`, `UnknownResolver.cs`, `NodeEffectResolver.cs`, `RunState.cs` (validation), `JourneyLogger.cs` | T1 |
| **T9** | `DevMetaController` actions リスト更新 + 既存 dev menu に新 action が出ることを確認 | `DevMetaController.cs`, dev test | — (parallel可) |
| **T10** | 統合テスト + push + memory 更新 + Phase 10.6.B 完了マーク | (新規ファイル無し、test と memory) | T1〜T9 |

### 推奨実装順序

`T1 → (T2, T3, T4, T5 並列可能、T9 も独立) → T6 → T7 → T8 → T10`

T6 は影響範囲広い (5 site touch) ため依存タスク (T1) 完了後に取り組む。
T7 と T8 は独立、どちらが先でも OK。
T9 は他と独立で並列実行候補。

### Subagent モデル選択指針

- T1, T2, T9: Sonnet (mechanical)
- T3, T4, T5, T8: Sonnet (multi-file integration)
- T6, T7: Sonnet (full-stack 系で touch list 多い)
- レビューは全 task で `superpowers:code-reviewer` (Sonnet)

### 推定コスト

Phase 10.6.A (9 task + 4 review fix = 13 commits、~22 test 追加、~5h 相当) 比較で約 1.5〜2x:

- 10.6.B: 10 task + 想定 review fix 数件 = ~14 commits
- ~50 test 追加
- ~8〜10h 相当
- touch list ~15 ファイル

---

## 12. Out-of-Scope (Future Phase 候補)

| 候補 | 概要 |
|---|---|
| **手札上限 +N** (`maxHandSizeBonus`) | passive 系、player 視点で手札枚数 cap が増える relic |
| **マップ先読み +N** (`mapNodeRevealBonus`) | 未訪問ノードを N 個先まで可視化 |
| **ボス報酬選択肢 +N** | BossRewardFlow に介入 |
| **イベント遭遇率補正** | EventPool.Pick への介入 |
| **Status duration modifier** | Vulnerable/Weak/Buff の持続ターン数補正 |
| **Damage type-specific bonus** | `attackCardDamageBonus` (基本攻撃カードのみ +N) など細分化系 |
| **Cost reduction passive** | アップグレード済カードのコスト -1 など |
| **Multiplayer Unknown 共有解決 (X2)** | Phase 9 (multiplayer) 着手時に再検討。`PassiveModifiers.ApplyUnknownWeightDeltas` を `RunState[]` 受け取り型に拡張 |

これらはいずれも `PassiveModifiers.cs` に façade メソッドを 1 個追加するだけで対応可能 (Approach 2 の利点)。
