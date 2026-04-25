# Phase 10.1.B — MoveDefinition 統一 + CombatActorDefinition 共通基底 設計

> 作成日: 2026-04-26
> 対象フェーズ: Phase 10.1.B（Phase 10 サブマイルストーン 2 番目）
> 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
> 直前マイルストーン: Phase 10.1.A — CardEffect 統一（`phase10-1A-complete` タグ）
> 視覚リファレンス: `.superpowers/brainstorm/14705-1776939312/content/battle-v10.html`

## ゴール

旧 `MoveDefinition`（`DamageMin/Max` / `Hits` / `BlockMin/Max` / `Buff` / `AmountMin/Max` の数値フィールド散在型）を破棄し、Phase 10.1.A で統一済みの `CardEffect` プリミティブを再利用する `Effects: List<CardEffect>` 形式に統一する。同時に `CombatActorDefinition` 抽象基底を新設し、`EnemyDefinition` / `UnitDefinition` を派生として整理。敵 JSON 34 ファイルを全書き換え。Phase 5 placeholder バトルが従来通り動作することで完了判定。

Core ロジック先行・データ構造統一が主目的のフェーズで、戦闘実行時状態（`BattleState` 等）は Phase 10.2 のスコープ。

## 完了判定

- `dotnet build` 警告 0 / エラー 0
- `dotnet test` 全テスト緑
- 旧 `MoveDefinition` のフィールド名（`DamageMin` / `DamageMax` / `Hits` / `BlockMin` / `BlockMax` / `Buff` / `AmountMin` / `AmountMax`）が grep で 0 件
- `HpMin` / `HpMax` が grep で 0 件（spec 文書を除く）
- 敵 JSON 34 ファイルが新形式
- `src/Core/Enemy/` ディレクトリが消滅、`src/Core/Battle/Definitions/` 体系に統合
- Phase 5 placeholder バトルが手動で従来通り動作（敵マス → 即勝利 → 報酬画面）
- 親 spec（Phase 10）の該当章が新方針に合わせて修正済み
- `phase10-1B-complete` タグが切られ origin に push 済み

---

## 1. アーキテクチャ概要

Phase 10 の親 spec で定義された `CombatActorDefinition` 抽象基底とその派生（`EnemyDefinition` / `UnitDefinition`）を実装し、共通の `MoveDefinition` を導入する。Phase 10.1.A の `CardEffect` をそのまま再利用するため、新規プリミティブの追加は不要。Move JSON のパース層だけが新設項目。

実行時の挙動（敵 attack の発射タイミング・状態異常 tick 等）は Phase 10.2 のスコープ。Phase 10.1.B は **データ構造とローダー** だけを整える。

---

## 2. ファイル / namespace 構成

### 2-1. 新規・移動先

```
src/Core/Battle/
├── Definitions/                    [静的データ・JSON ロード対象・不変]
│   ├── CombatActorDefinition.cs    [新規] abstract record 共通基底
│   ├── MoveDefinition.cs           [移動+書換] 旧 src/Core/Enemy/MoveDefinition.cs
│   ├── MoveKind.cs                 [新規] enum 7 値
│   ├── EnemyDefinition.cs          [移動+書換] : CombatActorDefinition
│   ├── EnemyPool.cs                [移動] EnemyTier 分離後の本体
│   ├── EnemyTier.cs                [新設・分離] 旧 EnemyPool.cs から enum を抽出
│   ├── UnitDefinition.cs           [新規] : CombatActorDefinition
│   └── Loaders/
│       ├── MoveJsonLoader.cs       [新規] move 1 個分の JSON → MoveDefinition の共通 helper
│       ├── EnemyJsonLoader.cs      [移動+書換] MoveJsonLoader に委譲
│       └── UnitJsonLoader.cs       [新規]

src/Core/Data/
├── Enemies/*.json                  [全書換] 34 ファイル新形式
└── Units/                          [新設] 空フォルダ（Phase 10.2 で召喚カード追加と同時に実データ）
```

### 2-2. namespace

| 旧 | 新 |
|---|---|
| `RoguelikeCardGame.Core.Enemy` | `RoguelikeCardGame.Core.Battle.Definitions` |
| - | `RoguelikeCardGame.Core.Battle.Definitions.Loaders` |

### 2-3. 削除されるファイル

- `src/Core/Enemy/MoveDefinition.cs`
- `src/Core/Enemy/EnemyDefinition.cs`
- `src/Core/Enemy/EnemyPool.cs`
- `src/Core/Enemy/EnemyJsonLoader.cs`
- `src/Core/Enemy/` ディレクトリ自体

### 2-4. 設計意図

- **Definitions / State / Actions / Events のライフサイクル分割**: Phase 10.2 で `BattleState` / 実行時 `CombatActor` / `AttackPool` / `BlockPool` 等が追加された際、`Battle/State/`、`Battle/Actions/`、`Battle/Events/` の各サブフォルダに収まる構造を予め用意する。「JSON ロード対象の不変データ」と「戦闘中だけ存在する可変状態」をフォルダレベルで分離することで、Udon# 移植時の翻訳単位（ScriptableObject 候補 vs ランタイム class 候補）も明示的になる。
- **`Battle/Combat/` 等の冗語を避ける**: Battle と Combat は同義のため、`src/Core/Battle/Combat/...` のような重ね名は採用しない。
- **1 ファイル 1 型を徹底**: `EnemyPool.cs` 内に enum + record が同居する従来構造を解消（`EnemyTier.cs` を分離）。

---

## 3. データモデル

### 3-1. `MoveKind`

```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 敵 / 召喚キャラの move を intent UI 上どのカテゴリで表示するかの分類。
/// 値は battle-v10.html の .is-{attack|defend|buff|debuff|heal|unknown} CSS クラスへ対応。
/// </summary>
public enum MoveKind
{
    Attack  = 0,    // 自分→相手の攻撃
    Defend  = 1,    // 自分にブロック
    Buff    = 2,    // 自陣強化（自分または味方）
    Debuff  = 3,    // 相手弱体化（敵対側）
    Heal    = 4,    // 自陣回復（自分または味方）
    Multi   = 5,    // 複合（attack + block 等、複数カテゴリにまたがる）
    Unknown = 6,    // フォールバック / 未確定
}
```

### 3-2. `MoveDefinition`

```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

using RoguelikeCardGame.Core.Cards;

/// <summary>敵 / 召喚キャラの行動 1 ステップ。state-machine 形式の遷移を持つ。</summary>
public sealed record MoveDefinition(
    string Id,
    MoveKind Kind,
    IReadOnlyList<CardEffect> Effects,
    string NextMoveId);
```

旧 `MoveDefinition` の数値フィールド（`DamageMin/Max` / `Hits` / `BlockMin/Max` / `Buff` / `AmountMin/Max`）はすべて廃止。

### 3-3. `CombatActorDefinition`

```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 戦闘に参加するキャラクターの静的定義（敵・召喚キャラの共通基底）。
/// HP は単一値。乱数化は将来拡張ポイント。
/// </summary>
public abstract record CombatActorDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
```

### 3-4. `EnemyDefinition`

```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

public sealed record EnemyDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    EnemyPool Pool,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves)
    : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);
```

`Pool.Act` が canonical な act 値。重複した standalone `Act` フィールドは持たない。

### 3-5. `UnitDefinition`

```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

public sealed record UnitDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves,
    int? LifetimeTurns = null)
    : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);
```

`LifetimeTurns`: null = 永続、N = N ターン経過で自動消滅。

### 3-6. `EnemyPool` / `EnemyTier`

```csharp
namespace RoguelikeCardGame.Core.Battle.Definitions;

public enum EnemyTier { Weak, Strong, Elite, Boss }

public sealed record EnemyPool(int Act, EnemyTier Tier);
```

中身は据置（enum / record とも内容変更なし）。ファイルだけ分離。

---

## 4. JSON 形式

### 4-1. 敵 JSON 旧 → 新（jaw_worm の例）

**旧:**
```json
{
  "id": "jaw_worm",
  "hpMin": 40, "hpMax": 44,
  "act": 1, "tier": "Weak",
  "initialMoveId": "chomp",
  "moves": [
    { "id": "chomp",  "kind": "attack", "damageMin": 11, "damageMax": 11, "hits": 1, "nextMoveId": "thrash" },
    { "id": "thrash", "kind": "multi",  "damageMin": 7,  "damageMax": 7,  "hits": 1,
      "blockMin": 5, "blockMax": 5, "nextMoveId": "bellow" },
    { "id": "bellow", "kind": "buff",   "buff": "strength", "amountMin": 3, "amountMax": 5,
      "blockMin": 6, "blockMax": 6, "nextMoveId": "chomp" }
  ]
}
```

**新:**
```json
{
  "id": "jaw_worm",
  "name": "ジョウ・ワーム",
  "imageId": "jaw_worm",
  "hp": 42,
  "act": 1, "tier": "Weak",
  "initialMoveId": "chomp",
  "moves": [
    { "id": "chomp",  "kind": "Attack", "nextMoveId": "thrash",
      "effects": [
        { "action": "attack", "scope": "all", "side": "enemy", "amount": 11 }
      ] },
    { "id": "thrash", "kind": "Multi",  "nextMoveId": "bellow",
      "effects": [
        { "action": "attack", "scope": "all", "side": "enemy", "amount": 7 },
        { "action": "block",  "scope": "self", "amount": 5 }
      ] },
    { "id": "bellow", "kind": "Buff",   "nextMoveId": "chomp",
      "effects": [
        { "action": "buff",  "scope": "self", "name": "strength", "amount": 4 },
        { "action": "block", "scope": "self", "amount": 6 }
      ] }
  ]
}
```

JSON 設計の決定事項:

- `hp` は単一値（旧 `hpMin/Max` のレンジは廃止、中央値で固定）
- `act` / `tier` はトップレベル維持（loader が `EnemyPool` に詰める）
- 敵の `attack` effect は **JSON 段階で `scope: "all"` を直書き**（戦闘時の自動書換ロジックは行わない方針）
- `kind` 値はパスカルケース（`MoveKind` enum 名と一致）

### 4-2. 敵 move における `side` / `scope` の解釈規約

`CardEffect.Side` は **caster 視点の相対側**（spec 親文書 第 5-1 章準拠）。敵 move の場合、caster = 敵自身なので:

- `side: "enemy"` = 敵から見た敵対側 = **プレイヤー側（hero + summon）**
- `side: "ally"` = 敵から見た自陣側 = **敵側（自分 + 他の敵）**

代表的な敵 move パターン（Phase 10.1.B での JSON 規約）:

| 状況 | scope / side / 備考 |
|---|---|
| 敵が攻撃（プレイヤーへ） | `scope: "all", side: "enemy"`（プレイヤー側全員に着弾、Phase 10.2 で per-effect 即時発射） |
| 敵が自分にブロック | `scope: "self"`（side フィールドは省略、Normalize で null 化） |
| 敵が自分（または味方敵）に buff | `scope: "self"` で自身、`scope: "all", side: "ally"` で敵全体強化 |
| 敵がプレイヤーに debuff（weak / vulnerable 付与等） | `scope: "all", side: "enemy"` |

例: cave_bat_a の screech（プレイヤーに weak 付与）:
```json
{ "id": "screech", "kind": "Debuff", "nextMoveId": "bite",
  "effects": [
    { "action": "debuff", "scope": "all", "side": "enemy", "name": "weak", "amount": 1 }
  ] }
```

### 4-3. 旧 `kind` → 新 `kind` マッピング

| 旧 kind | 新 kind | 備考 |
|---|---|---|
| `attack` | `Attack` | |
| `block` | `Defend` | |
| `buff` | `Buff` | 自陣強化 |
| `debuff` | `Debuff` | 敵対側弱体化 |
| `multi` | `Multi` | attack + block 複合 |

### 4-4. hits 展開

| 旧 | 新 |
|---|---|
| `damageMin/Max: N, hits: 1` | `effects: [{action:"attack", ..., amount:N}]` |
| `damageMin/Max: N, hits: 2` | `effects: [{...amount:N}, {...amount:N}]` |
| `damageMin/Max: N, hits: K` | 同 attack effect を K 個並べる |

`AttackPool.AddCount` が自然に hits 数となり、力バフが per-hit に乗る Slay the Spire と同じ挙動になる。

### 4-5. 未対応 buff 名置換表

Phase 10 spec の状態異常リスト（strength / dexterity / vulnerable / weak / omnistrike / poison）に存在しない buff 名は、近似する Phase 10 status へ置換する（敵スペックは仮置きの前提で運用）。

| 旧 buff (amount) | 出現する敵 | 新形式 |
|---|---|---|
| `ritual` (3) | dark_cultist | `{action:"buff", scope:"self", name:"strength", amount:3}` |
| `enrage` (2) | hobgoblin | `{action:"buff", scope:"self", name:"strength", amount:2}` |
| `enrage` (5) | slime_king | `{action:"buff", scope:"self", name:"strength", amount:5}` |
| `curl_up` (3) | louse_red | `{action:"block", scope:"self", amount:3}` |
| `activate` (1) | six_ghost | `{action:"buff", scope:"self", name:"strength", amount:1}` |
| `split` (1) | slime_king | `{action:"buff", scope:"self", name:"strength", amount:1}` |

完了判定の一部として **新形式 JSON 中に上記 5 つの未対応 buff 名（`ritual` / `enrage` / `curl_up` / `activate` / `split`）が grep で 0 件** であることを確認する。

### 4-6. レンジ → 単一値の collapse 規則

- HP: `hpMin == hpMax` の場合はその値、異なる場合は中央値（小数点以下切り上げ）
- buff / debuff の `amountMin / amountMax`: 同上
- 攻撃ダメージ: 全データで `damageMin == damageMax` のため値そのまま
- block: 全データで `blockMin == blockMax` のため値そのまま

---

## 5. 影響範囲

`MoveDefinition` の数値フィールド（`DamageMin/Max` 等）は production コードのどこからもアクセスされていない（Phase 5 placeholder は HP しか参照していない）。`HpMin/Max` も Phase 5 placeholder の HP ロール 1 箇所のみで実質書き換えが必要。

### 5-1. namespace 変更だけで済むファイル群

production:
- `src/Core/Battle/BattlePlaceholder.cs`（HP ロール部分は別途修正）
- `src/Core/Battle/EncounterQueue.cs`
- `src/Core/Data/EncounterDefinition.cs` / `EncounterJsonLoader.cs`
- `src/Core/Data/DataCatalog.cs` / `RewardTable.cs` / `RewardTableJsonLoader.cs`
- `src/Core/Run/ActTransition.cs` / `NodeEffectResolver.cs` / `BossRewardFlow.cs`
- `src/Core/Rewards/RewardState.cs` / `RewardGenerator.cs`
- `src/Server/Controllers/RunsController.cs`
- `src/Server/Services/RunStartService.cs`

tests: 11 ファイル（`tests/Core.Tests/Battle/*`、`tests/Core.Tests/Cards/CardUpgradeTests.cs`、`tests/Core.Tests/Data/*`、`tests/Core.Tests/Enemy/*`、`tests/Core.Tests/Rewards/*`、`tests/Core.Tests/Run/*`、`tests/Server.Tests/Controllers/*`）。

### 5-2. 実質書き換えが必要なファイル

| ファイル | 変更内容 |
|---|---|
| `src/Core/Battle/BattlePlaceholder.cs:27` | `def.HpMin + rng.NextInt(0, def.HpMax - def.HpMin + 1)` → `def.Hp` |
| `tests/Core.Tests/Battle/BattlePlaceholderTests.cs:30` | `Assert.InRange(e.CurrentHp, def.HpMin, def.HpMax)` → `Assert.Equal(def.Hp, e.CurrentHp)` |
| `tests/Core.Tests/Enemy/EnemyDefinitionTests.cs` | コンストラクタ呼出を新シグネチャ（`Hp:` 単一）に書き換え + `tests/Core.Tests/Battle/Definitions/EnemyDefinitionTests.cs` へ移動 |
| `tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs` | テスト JSON を全件新形式に + `tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs` へ移動 |

テストファイルそのものの所属も `tests/Core.Tests/Enemy/` → `tests/Core.Tests/Battle/Definitions/` 系に移動してプロジェクト構造を一貫させる。

---

## 6. テスト戦略

Phase 10.1.A と同じ TDD 1 サイクル粒度（失敗テスト→実装→緑→commit）。

### 6-1. 新規テストファイル

| ファイル | カバレッジ |
|---|---|
| `tests/Core.Tests/Battle/Definitions/MoveKindTests.cs` | enum 7 値の存在とビット値（Attack=0 〜 Unknown=6） |
| `tests/Core.Tests/Battle/Definitions/MoveDefinitionTests.cs` | record 等価・空 effects・複数 effects |
| `tests/Core.Tests/Battle/Definitions/CombatActorDefinitionTests.cs` | abstract record の派生クラス（Enemy/Unit）でフィールド継承確認 |
| `tests/Core.Tests/Battle/Definitions/UnitDefinitionTests.cs` | LifetimeTurns null/値、CombatActor 継承 |
| `tests/Core.Tests/Battle/Definitions/Loaders/MoveJsonLoaderTests.cs` | move 1 個分の JSON → MoveDefinition、kind 値 7 種、effects 配列、エラーケース（不明 kind / 必須欠落） |
| `tests/Core.Tests/Battle/Definitions/Loaders/UnitJsonLoaderTests.cs` | unit JSON → UnitDefinition、`lifetimeTurns` の有無 |

### 6-2. 既存テストの移行

| 既存 | 新 |
|---|---|
| `tests/Core.Tests/Enemy/EnemyDefinitionTests.cs` | `tests/Core.Tests/Battle/Definitions/EnemyDefinitionTests.cs` |
| `tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs` | `tests/Core.Tests/Battle/Definitions/Loaders/EnemyJsonLoaderTests.cs` |

### 6-3. EmbeddedDataLoaderTests への追加

- `Units/` 空フォルダ取扱い（空辞書を返す）
- 34 敵 JSON 全件の `EmbeddedDataLoader.LoadCatalog()` が新形式で成功
- 未対応 buff 名（`ritual` / `enrage` / `curl_up` / `activate` / `split`）が **embedded JSON 中に grep で 0 件**（移行完了の network test）

### 6-4. テスト粒度

- 1 タスク = 1 ファイル単位 = 1 commit が原則
- Step 構成: 失敗テスト書く → 失敗確認 → 最小実装 → 緑確認 → commit
- 例外: namespace 一斉変更（旧 `Core.Enemy` → 新 `Core.Battle.Definitions`）は 1 commit にまとめる

---

## 7. 親 spec への補記事項

Phase 10.1.B の最終タスクで `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md` に以下の修正を入れる。

1. **第 2-4 章**: `CombatActorDefinition.HpMin / HpMax` → 単一 `Hp: int` に変更
2. **第 2-4 章**: `EnemyDefinition` の `required Pool + required Act` 重複を `Pool` 一本化に修正
3. **第 2-5 章**: `MoveKind` 列挙値を 6 → 7（`Debuff` 追加）
4. **第 4-5 章**: 「敵 attack の scope=all 強制」を「**JSON 段階で `scope: "all"` を直書きする運用とし、ロード時の自動書換は行わない**」に変更
5. **第 10-1 章**: 「HP は `[HpMin, HpMax]` 範囲の乱数」→「HP は `EnemyDefinition.Hp` をそのまま採用（乱数化は将来拡張）」
6. **第 5-2 / 5-3 / 4-5 章補記**: 「**敵 attack は per-effect 即時発射、プレイヤー attack は AttackPool 蓄積→ターン終了時単発発射**」という非対称運用を明記（現 spec はやや曖昧）

これら 6 項目は Phase 10.1.B 内で発生した設計判断の追記。コードと spec の乖離を残さない。

---

## 8. スコープ外（再確認）

### 8-1. Phase 10.1.B では触らない

- バトル実行時状態（`BattleState` / 実行時 `CombatActor` / `AttackPool` / `BlockPool`）→ Phase 10.2
- `StatusDefinition` 静的リスト → Phase 10.2
- カード→ AttackPool 加算ロジック・敵 Move 発射ロジック → Phase 10.2
- `RelicDefinition` の Trigger 拡張 / `Implemented` フラグ → Phase 10.1.C
- `PotionDefinition.IsUsableOutsideBattle` 計算 / `BattleOnly` 効果のスキップ → Phase 10.1.C
- 召喚カード（`CardType.Unit`）の具体データ → Phase 10.2
- `BattleHub` / `BattleStateDto` → Phase 10.3
- `BattleScreen.tsx` / battle-v10.html ポート → Phase 10.4
  - `.is-debuff` CSS class の追加（spec の 5 値からの拡張に伴う）も Phase 10.4
- `BattlePlaceholder.cs` の削除 → Phase 10.5（最終 cleanup）

### 8-2. Phase 10.1.B 完了後の状態

- データモデルが新形式で整備済み
- 敵 JSON 34 ファイルが新形式
- `Units/` フォルダが空のまま存在（Phase 10.2 で召喚キャラ実データ追加）
- 旧 `MoveDefinition` フィールド名 / `HpMin/Max` フィールド名 が production コードから消滅
- `src/Core/Enemy/` ディレクトリが消滅、`src/Core/Battle/Definitions/` 体系に統合
- Phase 5 placeholder バトルは引き続き動作
- 親 spec が新方針に合わせて修正済み
- `phase10-1B-complete` タグ push 済み

---

## 参照

- 親 spec: [`2026-04-25-phase10-battle-system-design.md`](2026-04-25-phase10-battle-system-design.md)
- 直前マイルストーン plan: [`../plans/2026-04-25-phase10-1A-card-effect-unification.md`](../plans/2026-04-25-phase10-1A-card-effect-unification.md)
- ロードマップ: [`../plans/2026-04-20-roadmap.md`](../plans/2026-04-20-roadmap.md)
