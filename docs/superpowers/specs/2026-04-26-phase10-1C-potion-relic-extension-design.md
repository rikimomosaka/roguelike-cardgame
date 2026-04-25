# Phase 10.1.C — Potion / Relic 拡張 設計

> 作成日: 2026-04-26
> 対象フェーズ: Phase 10.1.C（Phase 10 サブマイルストーン 3 番目）
> 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
> 直前マイルストーン: Phase 10.1.B — MoveDefinition 統一（`phase10-1B-complete` タグ）

## ゴール

`RelicDefinition` に `Implemented` フラグを追加し、Phase 10 で実装する／しないを宣言的に分離。`RelicTrigger` enum を 5 → 9 値に拡張（戦闘内発火タイミング 4 種を追加）。`PotionDefinition` から冗長な `UsableInBattle` / `UsableOutOfBattle` フラグを削除し、per-effect の `BattleOnly` フラグから `IsUsableOutsideBattle` を派生。レリック JSON 36 件 + ポーション JSON 7 件を新形式に移行（ポーション JSON のレガシー action `applyPoison` / `gainStrength` / `drawCards` を標準語彙へ正規化）。

バトル本体の発火ロジック（OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）は **Phase 10.2 担当**で、本フェーズではコード追加なし。データ層の整備にスコープを絞る。

## 完了判定

- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑（10.1.B 完了時の 507 Core + 168 Server + 本フェーズ追加分）
- 旧 PotionDefinition フィールド `UsableInBattle` / `UsableOutOfBattle` が production / tests に grep で 0 件
- 旧ポーション action `applyPoison` / `gainStrength` / `drawCards` が `src/Core/Data/Potions/*.json` に grep で 0 件
- 旧 JSON フィールド `usableInBattle` / `usableOutOfBattle` が `src/Core/Data/Potions/*.json` に grep で 0 件
- 全 36 relic JSON が `implemented` フィールド（true / false）を持つ
- 全 7 potion JSON が新形式（per-effect `battleOnly` フラグ）
- 親 Phase 10 spec の第 2-7 章が新方針に合わせて補記済み（9 値の `RelicTrigger` 列挙、Implemented セマンティクス補強）
- `phase10-1C-complete` タグが切られ origin に push 済み

---

## 1. アーキテクチャ概要

Phase 10.1.C は「データ層整備」フェーズで、戦闘実行ロジックには手を入れない。3 つの観点で行う:

1. **`RelicDefinition` の拡張**: `Implemented` フラグでデータ宣言と実装乖離を明示化
2. **`RelicTrigger` の拡張**: 9 値に整理（`OnBattleEnd` / `OnMapTileResolved` を含む既存 5 + 新 4）
3. **`PotionDefinition` の整理**: 冗長 bool 2 つを削除、per-effect `BattleOnly` から派生プロパティ

新 4 トリガー（`OnTurnStart` / `OnTurnEnd` / `OnCardPlay` / `OnEnemyDeath`）は enum 値として追加するのみで、戦闘エンジン側で発火させるロジックは Phase 10.2 で実装する。同様に、`healPercent` / `extraEnergyOnFirstTurn` のような戦闘内 action は JSON に保持しつつ `Implemented: false` で隔離し、Phase 10.2 でデザイナーがアクション語彙を確定させながら個別対応していく。

---

## 2. ファイル / namespace 構成

### 2-1. 変更対象ファイル（production）

```
src/Core/Relics/
├── RelicDefinition.cs         [変更] Implemented フィールド追加
├── RelicTrigger.cs            [変更] 4 値追加 + 整数値明示
├── RelicJsonLoader.cs         [変更] implemented フィールド読み込み（optional, default true）
└── NonBattleRelicEffects.cs   [変更] Implemented:false 早期 return ガード

src/Core/Potions/
├── PotionDefinition.cs        [変更] UsableInBattle/UsableOutOfBattle 削除、IsUsableOutsideBattle 派生
└── PotionJsonLoader.cs        [変更] 旧フラグ読込削除（CardEffectParser に battleOnly は既に対応）
```

### 2-2. データファイル

```
src/Core/Data/Relics/*.json    [全 36 件編集] implemented 追加、未実装は [未実装] プレフィックス
src/Core/Data/Potions/*.json   [全 7 件書換] 旧フラグ削除、per-effect battleOnly 追加、3 件 action 正規化
```

### 2-3. namespace

変更なし。`RoguelikeCardGame.Core.Relics` / `RoguelikeCardGame.Core.Potions` のまま。

### 2-4. 設計意図

- **Implemented フラグの宣言性**: 「JSON にある effect が実際にエンジンで処理されるかどうか」を効果側から明示することで、未実装レリックの混入で `OnPickup` 等の発火が予期せず動くことを防ぐ。Phase 10.2 で flag を `true` に flip するだけで段階的に実装が可能。
- **`PotionDefinition` のフィールド削減**: 旧 `UsableInBattle` / `UsableOutOfBattle` は per-effect の `BattleOnly` フラグと冗長。後者を真の source of truth にすることで「戦闘内の一部 effect だけスキップ」のような将来パターン（例: 二重効果ポーション）に自然に対応できる。
- **action 名正規化**: Phase 10.1.A の「最小移行」で残った旧 action 名（`applyPoison` 等）を標準 CardEffect 語彙に揃えることで、Phase 10.2 のバトルエンジンが標準語彙だけ知っていれば良くなる（switch 分岐が増えない）。

---

## 3. データモデル

### 3-1. `RelicDefinition`

```csharp
namespace RoguelikeCardGame.Core.Relics;

using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

/// <summary>レリックのマスター定義。</summary>
public sealed record RelicDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    RelicTrigger Trigger,
    IReadOnlyList<CardEffect> Effects,
    string Description = "",
    bool Implemented = true);
```

**`Implemented` セマンティクス:**

- `true`（既定）: エンジンが effects を処理する。`OnPickup` / `Passive` / `OnMapTileResolved` 等の対応する発火タイミングで `NonBattleRelicEffects.cs` が実際に effects を適用。
- `false`: エンジンは effects を**完全スキップ**。プレイヤー所持・図鑑掲載は通常通り（カードが所持 / レリック一覧に表示される）。description には `[未実装] ` プレフィックス（半角スペース込み）を付ける運用とする。

### 3-2. `RelicTrigger`

```csharp
namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックの効果発動タイミング。</summary>
public enum RelicTrigger
{
    /// <summary>入手した瞬間に 1 度だけ発動する。</summary>
    OnPickup           = 0,
    /// <summary>所持している間、常に効果を発揮する（runtime 計算は呼び出し側）。</summary>
    Passive            = 1,
    /// <summary>戦闘開始時に発動する（Phase 10.2 で発火）。</summary>
    OnBattleStart      = 2,
    /// <summary>戦闘終了時に発動する（Phase 10.2 で発火）。</summary>
    OnBattleEnd        = 3,
    /// <summary>マスのイベント解決後に発動する（既存 NonBattleRelicEffects で発火）。</summary>
    OnMapTileResolved  = 4,
    /// <summary>各ターン開始時に発動する（Phase 10.2 で発火）。</summary>
    OnTurnStart        = 5,
    /// <summary>各ターン終了時に発動する（Phase 10.2 で発火）。</summary>
    OnTurnEnd          = 6,
    /// <summary>カードプレイ時に発動する（Phase 10.2 で発火、条件絞りは将来拡張）。</summary>
    OnCardPlay         = 7,
    /// <summary>敵撃破時に発動する（Phase 10.2 で発火）。</summary>
    OnEnemyDeath       = 8,
}
```

整数値を明示する理由: 将来の値追加で既存 serialize（save data 等）が壊れないように。Phase 10.1.B の `MoveKind` と同じ慣習。

### 3-3. `PotionDefinition`

```csharp
namespace RoguelikeCardGame.Core.Potions;

using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Cards;

/// <summary>ポーションのマスター定義。</summary>
public sealed record PotionDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    IReadOnlyList<CardEffect> Effects)
{
    /// <summary>
    /// 戦闘外で使用可能か。effects のいずれかが BattleOnly=false なら true。
    /// 全 effect が BattleOnly=true なら false（マップ画面でグレーアウト）。
    /// </summary>
    public bool IsUsableOutsideBattle => Effects.Any(e => !e.BattleOnly);
}
```

旧 `UsableInBattle` / `UsableOutOfBattle` 削除。`BattleOnly` は `CardEffect` の既存フィールド（Phase 10.1.A で追加）。

「戦闘内で使用可能か」は Phase 10.2 で「効果あるかどうか」と同義になる（全 effect が `BattleOnly=true` のポーションも戦闘内では普通に使える）。専用プロパティが必要になれば後出しで追加する（YAGNI）。

### 3-4. `NonBattleRelicEffects` の Implemented ガード

`ApplyOnPickup` / `ApplyOnMapTileResolved` / `ApplyPassiveRestHealBonus` の各メソッドで、relic を取り出した直後に `def.Implemented` チェックを追加し、`false` なら effect 適用をスキップ。

```csharp
public static RunState ApplyOnPickup(RunState s, string relicId, DataCatalog catalog)
{
    if (!catalog.TryGetRelic(relicId, out var def)) return s;
    if (!def.Implemented) return s;                       // ← 追加
    if (def.Trigger != RelicTrigger.OnPickup) return s;
    return ApplyEffects(s, def);
}

public static RunState ApplyOnMapTileResolved(RunState s, DataCatalog catalog)
{
    foreach (var id in s.Relics)
    {
        if (!catalog.TryGetRelic(id, out var def)) continue;
        if (!def.Implemented) continue;                   // ← 追加
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
        if (!def.Implemented) continue;                   // ← 追加
        if (def.Trigger != RelicTrigger.Passive) continue;
        foreach (var eff in def.Effects)
            if (eff.Action == "restHealBonus") bonus += eff.Amount;
    }
    return bonus;
}
```

これにより `Implemented: false` レリックは戦闘外でも完全に no-op。Phase 10.2 で戦闘内発火を実装するときも同じガードを敷く。

---

## 4. JSON 形式

### 4-1. レリック JSON

#### 旧 → 新（Implemented:true 例 / `coin_purse.json`）

**旧:**
```json
{
  "id": "coin_purse",
  "name": "コインポーチ",
  "rarity": 1,
  "trigger": "OnPickup",
  "description": "...",
  "effects": [{ "action": "gainGold", "scope": "self", "amount": 50 }]
}
```

**新:**
```json
{
  "id": "coin_purse",
  "name": "コインポーチ",
  "rarity": 1,
  "trigger": "OnPickup",
  "description": "...",
  "implemented": true,
  "effects": [{ "action": "gainGold", "scope": "self", "amount": 50 }]
}
```

#### 旧 → 新（Implemented:false 例 / `burning_blood.json`）

**旧:**
```json
{
  "id": "burning_blood",
  "name": "血のサンゴ",
  "rarity": 1,
  "trigger": "OnBattleEnd",
  "description": "深紅に脈打つ珊瑚。戦いを終えるたび、血肉に低く熱を返してくる。",
  "effects": [{ "action": "healPercent", "scope": "self", "amount": 6 }]
}
```

**新:**
```json
{
  "id": "burning_blood",
  "name": "血のサンゴ",
  "rarity": 1,
  "trigger": "OnBattleEnd",
  "description": "[未実装] 深紅に脈打つ珊瑚。戦いを終えるたび、血肉に低く熱を返してくる。",
  "implemented": false,
  "effects": [{ "action": "healPercent", "scope": "self", "amount": 6 }]
}
```

`effects` は **触らない**（Phase 10.2 のデザイナー参考用に保持）。

#### 旧 → 新（Implemented:false 空 effects 例 / `bell_earring.json`）

**旧:**
```json
{
  "id": "bell_earring",
  "name": "鈴のイヤリング",
  "rarity": 1,
  "trigger": "Passive",
  "description": "ちりんと鳴る、小さな鈴のイヤリング。...",
  "effects": []
}
```

**新:**
```json
{
  "id": "bell_earring",
  "name": "鈴のイヤリング",
  "rarity": 1,
  "trigger": "Passive",
  "description": "[未実装] ちりんと鳴る、小さな鈴のイヤリング。...",
  "implemented": false,
  "effects": []
}
```

### 4-2. レリック 36 件の Implemented 値割当

| 区分 | 件数 | Implemented | 説明 |
|---|---|---|---|
| `act_start_*`（act1/act2/act3 × 5 = 15 件） | 15 | **true** | gainMaxHp / gainGold / restHealBonus（NonBattleRelicEffects 対応 action） |
| `coin_purse`, `extra_max_hp`, `traveler_boots`, `warm_blanket` | 4 | **true** | 同上の対応 action のみ |
| `burning_blood`, `lantern` | 2 | **false** | 戦闘内 action（healPercent / extraEnergyOnFirstTurn）— Phase 10.2 待ち |
| 空 effects: `bell_earring`, `big_bag`, `bone_earring`, `claw_earring`, `gamble_dice`, `gauntlet`, `honeycomb_stone`, `magic_pouch`, `mana_tarot`, `nice_acorn`, `nyango_bell`, `ritual_chalice`, `skull_fish`, `skull_mushroom`, `thorn_collar` | 15 | **false** | 効果未実装（10.2 以降のデザイン裁量） |

合計: `true` 19 件 / `false` 17 件。

### 4-3. ポーション JSON

#### 旧 → 新（`block_potion.json` / 戦闘専用例）

**旧:**
```json
{
  "id": "block_potion",
  "name": "ブロックポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [{ "action": "block", "scope": "self", "amount": 12 }]
}
```

**新:**
```json
{
  "id": "block_potion",
  "name": "ブロックポーション",
  "rarity": 1,
  "effects": [{ "action": "block", "scope": "self", "amount": 12, "battleOnly": true }]
}
```

#### 旧 → 新（`health_potion.json` / 戦闘外でも使える例）

**旧:**
```json
{
  "id": "health_potion",
  "name": "ヘルスポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": true,
  "effects": [{ "action": "heal", "scope": "self", "amount": 15 }]
}
```

**新:**
```json
{
  "id": "health_potion",
  "name": "ヘルスポーション",
  "rarity": 1,
  "effects": [{ "action": "heal", "scope": "self", "amount": 15 }]
}
```

`battleOnly` 省略 = `false`（CardEffect 既定値）。`IsUsableOutsideBattle = true` になる。

### 4-4. ポーション 7 件の `BattleOnly` マッピング

| ポーション | 旧 `usableOutOfBattle` | 新 effect の `battleOnly` | `IsUsableOutsideBattle`（派生） |
|---|---|---|---|
| `block_potion` | false | true | false |
| `energy_potion` | false | true | false |
| `fire_potion` | false | true | false |
| **`health_potion`** | **true** | **(省略 = false)** | **true** |
| `poison_potion` | false | true | false |
| `strength_potion` | false | true | false |
| `swift_potion` | false | true | false |

旧挙動と完全一致。

### 4-5. action 名正規化（3 件）

| ポーション | 旧 action | 新 action |
|---|---|---|
| `poison_potion` | `applyPoison`, scope:self, amount:6 | `debuff`, scope:self, name:`"poison"`, amount:6 |
| `strength_potion` | `gainStrength`, scope:self, amount:2 | `buff`, scope:self, name:`"strength"`, amount:2 |
| `swift_potion` | `drawCards`, scope:self, amount:3 | `draw`, scope:self, amount:3 |

`block_potion` / `energy_potion` / `fire_potion` / `health_potion` は既に標準語彙で変更不要。

`poison_potion` の正規化に注意: 旧 `applyPoison` は scope:self だが、毒は debuff なので「自分にデバフ付与」というやや奇妙な記述になる。Phase 10.2 でバトルエンジンを実装する際にプレイヤー使用ポーションは「scope:single, side:enemy で対象敵に毒付与」の方が自然な可能性がある。**ただし 10.1.C のスコープでは旧挙動の保存を優先**し、「scope:self」を維持しつつ action を `debuff` 化のみ行う（Phase 10.2 で再検討）。

---

## 5. 影響範囲

### 5-1. production の変更ファイル

- `src/Core/Relics/RelicDefinition.cs`
- `src/Core/Relics/RelicTrigger.cs`
- `src/Core/Relics/RelicJsonLoader.cs`
- `src/Core/Relics/NonBattleRelicEffects.cs`
- `src/Core/Potions/PotionDefinition.cs`
- `src/Core/Potions/PotionJsonLoader.cs`

`UsableInBattle` / `UsableOutOfBattle` プロパティを参照していた他 production / Server コードがあれば同時に修正。**事前 grep で確認**:

```bash
grep -rn "UsableInBattle\|UsableOutOfBattle" src/ --include="*.cs"
```

### 5-2. データの変更ファイル

- `src/Core/Data/Relics/*.json` — 36 件
- `src/Core/Data/Potions/*.json` — 7 件

### 5-3. テストの変更ファイル（更新 + 新規）

| ファイル | 種類 | 内容 |
|---|---|---|
| `tests/Core.Tests/Relics/RelicDefinitionTests.cs` | 更新 | `Implemented` フィールド既定 true、明示 false、record equality |
| `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs` | 更新 | `implemented` 省略時 true、明示時値、不正型で例外 |
| `tests/Core.Tests/Relics/RelicTriggerTests.cs` | 新規 | enum 9 値の整数値検証 |
| `tests/Core.Tests/Relics/NonBattleRelicEffectsTests.cs` | 更新 | `Implemented:false` の relic は OnPickup / OnMapTileResolved / RestHealBonus いずれでも no-op |
| `tests/Core.Tests/Potions/PotionDefinitionTests.cs` | 更新 | `IsUsableOutsideBattle` 派生プロパティ（全 BattleOnly:true → false、いずれか false → true、空 effects → false） |
| `tests/Core.Tests/Potions/PotionJsonLoaderTests.cs` | 更新 | 旧フラグ削除確認、per-effect `battleOnly` 読込 |
| `tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs` | 更新 | 36 relic + 7 potion 全件新形式ロード成功、`health_potion.IsUsableOutsideBattle == true`、他 6 ポーション false |
| `tests/Core.Tests/Relics/RelicJsonMigrationTests.cs` | 新規 | grep ベース migration completeness（旧 action 名・旧フィールドが embedded JSON に 0 件） |

### 5-4. ドキュメント

- `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` — 第 2-7 章補記:
  - `RelicTrigger` 列挙を 9 値に補正（`OnBattleEnd` / `OnMapTileResolved` を新 4 と並記）
  - `Implemented:false` セマンティクス補強（`NonBattleRelicEffects` でもスキップする旨）

---

## 6. テスト戦略

Phase 10.1.A / 10.1.B と同じ TDD 1 サイクル粒度（失敗テスト → 実装 → 緑 → commit）。

### 6-1. 新規テストファイル

| ファイル | カバレッジ |
|---|---|
| `tests/Core.Tests/Relics/RelicTriggerTests.cs` | 9 値の整数値（OnPickup=0 〜 OnEnemyDeath=8）の serialize-safe 検証 |
| `tests/Core.Tests/Relics/RelicJsonMigrationTests.cs` | 旧フィールド (`usableInBattle` / `usableOutOfBattle`) と旧 action (`applyPoison` / `gainStrength` / `drawCards`) が embedded Potion JSON に 0 件、全 36 relic JSON が `implemented` フィールドを持つ |

### 6-2. 既存テスト更新の主なポイント

- `PotionDefinition` 構築呼び出し: `new PotionDefinition(id, name, rarity, usableInBattle, usableOutOfBattle, effects)` → `new PotionDefinition(id, name, rarity, effects)` に書き換え（4 引数化）。複数テストファイルで影響あり、grep で全件特定。
- `RelicDefinition` 構築: 既定値 `Implemented = true` のため、明示しないと既存呼出は動く。新規テストで `Implemented: false` を明示。
- `EmbeddedDataLoaderTests`: 旧 fixtures に `usableInBattle` / `usableOutOfBattle` が含まれていないか確認。あれば新形式に更新。

### 6-3. ビルド赤期間

10.1.B の Tasks 2-10 と異なり、本フェーズはビルド赤の中間状態がほぼ発生しない見込み:

- データ型変更（`PotionDefinition` の引数削減）は破壊的だが、単一コミットで全 production / test の同期書換が現実的（影響範囲が `usableInBattle` / `usableOutOfBattle` 文字列だけなので grep 一発で見つかる）。
- `RelicDefinition` への `Implemented` 追加は既定値ありなので非破壊的。

---

## 7. 親 spec への補記事項

Phase 10.1.C の最終タスクで `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` の第 2-7 章に以下を反映:

1. **`RelicTrigger` 列挙を 9 値に補正**:
   ```csharp
   public enum RelicTrigger {
       OnPickup           = 0,
       Passive            = 1,
       OnBattleStart      = 2,
       OnBattleEnd        = 3,                       // 親 spec 第 2-7 章に欠落していたが現実装で使用中
       OnMapTileResolved  = 4,                       // 同上
       OnTurnStart        = 5,                       // 新規
       OnTurnEnd          = 6,                       // 新規
       OnCardPlay         = 7,                       // 新規
       OnEnemyDeath       = 8,                       // 新規
   }
   ```

2. **Implemented セマンティクスの補強**:
   - `Implemented: false` の relic は `NonBattleRelicEffects.cs` 内でも早期 return される（戦闘外でも no-op）
   - description 先頭に `[未実装] ` プレフィックスを付ける運用

これら 2 項目は Phase 10.1.C 内で発生した設計判断の追記。コードと spec の乖離を残さない。

---

## 8. スコープ外（再確認）

### 8-1. Phase 10.1.C では触らない

- 新 `RelicTrigger` 4 値（OnTurnStart / OnTurnEnd / OnCardPlay / OnEnemyDeath）の戦闘内発火コード → Phase 10.2
- `healPercent` / `extraEnergyOnFirstTurn` 等の戦闘内 action の実装 → Phase 10.2
- `Implemented: false` レリックを `true` に flip するゲームデザイン作業 → Phase 10.2 以降にデザイナーが個別対応
- 空 effects レリック 15 件の effect 設計 → Phase 10.2 以降のデザイン裁量
- バトル中ポーション使用の UI 実装 → Phase 10.4
- マップ画面のポーションスロット UI → Phase 10.5
- カード（`CardDefinition`）への `Implemented` 拡張 → 必要になった時点で別フェーズ（Phase 10.1.A 完了時に整理済み）

### 8-2. Phase 10.1.C 完了後の状態

- データモデルが新形式で整備済み（`Implemented` フラグ、9 値 `RelicTrigger`、簡素化 `PotionDefinition`）
- レリック JSON 36 件 + ポーション JSON 7 件が新形式
- 旧 PotionDefinition フィールドと旧 action 名が production / tests / embedded JSON から消滅
- 親 Phase 10 spec が新方針に合わせて補記済み
- Phase 5 placeholder バトルは引き続き動作（戦闘経路に変更なし）
- `phase10-1C-complete` タグ push 済み

---

## 参照

- 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン spec: [`2026-04-26-phase10-1B-move-unification-design.md`](2026-04-26-phase10-1B-move-unification-design.md)
- 直前マイルストーン plan: [`../plans/2026-04-26-phase10-1B-move-unification.md`](../plans/2026-04-26-phase10-1B-move-unification.md)
- ロードマップ: [`../plans/2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md)
