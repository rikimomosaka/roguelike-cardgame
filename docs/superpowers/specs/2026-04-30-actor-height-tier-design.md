# Actor Height Tier + シルエット placeholder Design

**Date:** 2026-04-30
**Phase:** 10.4 prep / 戦闘表示の視覚情報補完
**Status:** Approved (brainstorm 2026-04-30)

## 1. 目的

現状、戦闘画面でプレイヤーは `/characters/player_stand.png` の立ち絵を `heightTier=5` で表示しているが、敵・召喚 ally は text sprite (`☗` 等の記号) のみで「見た目の大きさ」が無い。立ち絵アセットがまだ揃っていないため、即座に画像を差し込めない。

このタスクでは

- **データモデル**として `heightTier: int` を全 combat actor 系 JSON に導入し、
- 立ち絵未配置時の **視覚仮置きとしてシルエットの矩形 placeholder** を tier ベースで描画し、
- 各キャラの **名前を HP ゲージの直下に表示**（hero は accountId）

を実装する。実アセットがドロップされて `image` が設定されれば、placeholder は自動的に上書きされる。

## 2. 非ゴール

- 実際の敵・召喚立ち絵アセットの追加（asset 整備は別フロー、`使えそうなアイコン/キャラクター/` から `public/enemies/` 等へドロップする運用は手動）。
- `imageId` フィールド追加（既存）の意味変更。
- アセットファイル名規約の自動推論（`/enemies/<imageId>.png` 等の path 生成は当面 dtoAdapter 側で個別に必要に応じて行う）。
- text sprite (`☗` 等) 経路の完全削除。`heightTier` も `image` も持たない極端な fallback 用に残す。

## 3. データモデル

### 3.1 Core 側 (C# records)

`CombatActorDefinition`（base record、`EnemyDefinition` / `UnitDefinition` の親）に `int HeightTier` を追加：

```csharp
public abstract record CombatActorDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    int HeightTier,           // ← 新規
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
```

`EnemyDefinition` / `UnitDefinition` のコンストラクタ呼び出しは `base(...)` パススルーが必要なので、それぞれにも `HeightTier` を足す（同名・同型）。

`CharacterDefinition`（プレイアブル側、別系統）にも独立に `int HeightTier` を追加：

```csharp
public sealed record CharacterDefinition(
    string Id,
    string Name,
    int MaxHp,
    int StartingGold,
    int PotionSlotCount,
    IReadOnlyList<string> Deck,
    int HeightTier);          // ← 新規
```

### 3.2 JSON loader

3 つの loader (`EnemyJsonLoader` / `UnitJsonLoader` / `CharacterJsonLoader`) に `heightTier` パースを追加。

- 未指定 / `null` → **default 5**
- `1..10` 範囲外 → throw（`HeightTier の値 N は 1..10 の範囲外です (id=...)`）
- 整数以外 → throw（既存 `GetRequiredInt` パターンの optional 版を新規にヘルパーとして導入）

ヘルパー：

```csharp
private static int GetOptionalIntInRange(
    JsonElement el, string key, int defaultValue, int min, int max,
    string? id, Func<string, Exception> errFactory)
{
    if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
        return defaultValue;
    if (v.ValueKind != JsonValueKind.Number)
        throw errFactory($"\"{key}\" は数値である必要があります (id={id})。");
    int n = v.GetInt32();
    if (n < min || n > max)
        throw errFactory($"\"{key}\" の値 {n} は {min}..{max} の範囲外です (id={id})。");
    return n;
}
```

各 loader 固有の例外型を `errFactory` 経由で投げるので、共通化は不要（または `Loaders` 名前空間に置く）。

### 3.3 JSON 値（curated）

全 36 ファイルに `heightTier` を埋める。値は以下 tier 表に従う：

| tier | サイズ感 | 該当キャラ |
|---|---|---|
| 1 | 極小（手のひら大） | `slime_acid_s`, `slime_spike_s`, `louse_red` |
| 2 | 小型（猫犬大） | `cave_bat_a`, `cave_bat_b` |
| 3 | 小〜中型 | `big_slime`, `mushroom_a`, `mushroom_b`, `goblin_a`, `goblin_b`, `goblin_c`, **`wisp`** |
| 4 | 中型（人間級下限） | `bandit`, `jaw_worm`, `act2_grunt`, `act3_grunt` |
| 5 | 標準（人間） | **`default`（hero）**, `dark_cultist`, `six_ghost` |
| 6 | 大型（重鎧人型） | `blue_orc`, `red_orc`, `hobgoblin`, `dire_wolf` |
| 7 | 超大型 | `ogre`, `act2_brute`, `act3_brute` |
| 8 | 巨大（小型ゴーレム） | `iron_golem_a`, `iron_golem_b`, `iron_golem_c`, `act2_elite`, `act3_elite` |
| 9 | 巨大ボス級 | `guardian_golem`, `sleeping_dragon` |
| 10 | 超巨大（画面占領級） | `slime_king`, `act2_boss`, `act3_boss` |

未指定で default 5 を使うパスはあくまで「JSON で書き忘れた時の fallback」。今回は全 36 ファイル明示。

## 4. Server / Client plumbing

### 4.1 DTO

- Server side で enemies / units / characters のカタログ DTO に `heightTier: int` を追加。
- 既存 `BattleStateDtoMapper` には影響なし（catalog DTO は別経路）。具体的には `CatalogController` の enemies / units endpoint と、`RunsController.PostStart` 等で character を返す経路。
- 既存 catalog endpoint レスポンス型を拡張（破壊的ではないが追加フィールドなので client TS 側を同期する）。

### 4.2 Client TS types

`src/Client/src/api/catalog.ts` 等の `EnemyDto` / `UnitDto` / `CharacterDto` 型に `heightTier: number` 追加。型 source-of-truth が 1 箇所ある想定。

### 4.3 dtoAdapter

`src/Client/src/screens/battleScreen/dtoAdapter.ts` の `toCharacterDemo`：

- 第 3 引数として `accountId: string` を受け取る（hero name 表示用）。
- hero の name は `HERO_FALLBACK.name` (`"主人公"`) → `accountId` に置き換える。
- `heightTier`：
  - hero → `characterCatalog?.['default']?.heightTier ?? 5`（catalog 取れない時の fallback）
  - enemy → `enemyDef?.heightTier ?? 5`
  - summon → `unitDef?.heightTier ?? 5`
- `image`：hero は既存の `/characters/player_stand.png` で確定。enemy / summon は今回 `undefined`（asset 来たら個別に配線）。

`CharacterCatalogs` 型に `characters?: CharacterCatalog | null` を追加（または BattleScreen で使っている既存の hook で取れるならそちらを利用）。`useCharacterCatalog` hook が無ければ新設する。

### 4.4 BattleScreen 呼び出し側

`BattleScreen.tsx` の `toCharacterDemo` 呼び出し 2 箇所（line 1066, 1069）に `accountId` を渡す。

`accountId` は既に Props に含まれている（`accountId: string`）。

## 5. BattleScreen 描画変更

### 5.1 シルエット placeholder

`BattleScreen.tsx` の sprite render 部（現在 image 分岐 / text 分岐の二分岐）を 3 分岐に拡張：

```tsx
{char.image ? (
  /* 既存: 実画像 */
  <div
    className={`sprite sprite--image sprite--${char.spriteKind}`}
    style={{ '--tier-height': `${heightForTier(char.heightTier ?? 5)}px` } as CSSProperties}
  >
    <img src={char.image} alt={char.name} draggable={false} />
  </div>
) : char.heightTier !== undefined ? (
  /* 新規: シルエット placeholder */
  <div
    className={`sprite sprite--silhouette sprite--${char.spriteKind}`}
    style={{ '--tier-height': `${heightForTier(char.heightTier)}px` } as CSSProperties}
    aria-label={`${char.name} (placeholder)`}
  />
) : (
  /* 既存: text sprite (極端な fallback) */
  <div className={`sprite sprite--${char.spriteKind}${char.sprite.length > 2 ? ' sprite--text' : ''}`}>
    {char.sprite}
  </div>
)}
```

シルエット placeholder の中身は**空**（キャラ名は HP ゲージ下に別途表示するため、シルエット本体には乗せない）。

CSS（`BattleScreen.css` に追加）：

```css
.sprite--silhouette {
  height: var(--tier-height, 148px);
  /* Why: 人型寄りの縦長シルエット。tier=10 (260px) でも 143px 幅で画面を占領しすぎない。 */
  width: calc(var(--tier-height, 148px) * 0.55);
  border-radius: 12px 12px 8px 8px / 16px 16px 8px 8px;
  border: 1px solid;
}
.sprite--silhouette.sprite--enemy {
  background: rgba(220, 80, 80, 0.45);
  border-color: rgba(220, 80, 80, 0.85);
}
.sprite--silhouette.sprite--ally {
  background: rgba(80, 180, 100, 0.45);
  border-color: rgba(80, 180, 100, 0.85);
}
.sprite--silhouette.sprite--hero {
  background: rgba(100, 140, 220, 0.45);
  border-color: rgba(100, 140, 220, 0.85);
}
.sprite--silhouette.sprite--elite {
  background: rgba(220, 140, 80, 0.45);
  border-color: rgba(220, 140, 80, 0.85);
}
```

### 5.2 キャラ名表示（HP ゲージの下）

現状、キャラ名は描画されていない（`char.name` は tooltip/aria でしか使われていない）。

HP ゲージのブロック (`<div className="status-hp">`) の直下に新要素を追加：

```tsx
<div className="status-hp">
  ...
</div>
<div className="status-name">{char.name}</div>
```

CSS：

```css
.status-name {
  margin-top: 2px;
  font-size: 12px;
  text-align: center;
  color: rgba(255, 255, 255, 0.85);
  text-shadow: 0 1px 2px rgba(0, 0, 0, 0.6);
  /* Why: 長い名前 (アカウント ID 例: rikimomosaka@gmail.com) は省略表示。 */
  max-width: 12ch;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
```

### 5.3 hero の name は accountId

`toCharacterDemo` が `accountId` を受け取り、hero の `CharacterDemo.name` に accountId をそのまま流す。長い場合は CSS の `text-overflow: ellipsis` で切り詰め。

## 6. テスト

### 6.1 Core.Tests

- `EnemyJsonLoaderTests`：
  - `heightTier` 未指定で 5
  - `heightTier=7` 指定で 7
  - `heightTier=0` で throw、`heightTier=11` で throw、`heightTier="x"` で throw
- `UnitJsonLoaderTests`、`CharacterJsonLoaderTests`：同パターン
- 既存 fixture （`tests/Core.Tests/Fixtures/JsonFixtures.cs` 等）は heightTier フィールド未指定でも動くはず（default 5）— 念のため一回 build & test pass 確認

### 6.2 Server.Tests

- `CatalogController` の enemies / units / characters エンドポイントが `heightTier` をレスポンスに含む

### 6.3 Client vitest

- `dtoAdapter.test.ts` （存在すれば追加、なければ新設）：
  - hero に accountId が name に入る
  - enemy で `heightTier` が catalog から正しく取れる
  - 未取得 catalog で fallback 5 になる

### 6.4 手動スモーク

- `npm run dev` で戦闘進入 → 敵にカラフルなシルエットが表示される
- HP バー直下にキャラ名が表示される（hero は自分のアカウント ID）
- スライム vs キング・スライム vs ボス系で**目に見えて高さが違う**こと

## 7. ロールアウト

破壊的変更：
- `CombatActorDefinition` 派生 record（`EnemyDefinition`, `UnitDefinition`）のコンストラクタに引数追加 → fixture / test を 1 箇所修正必要（`BattleFixtures.GoblinDef` 等）。
- `CharacterDefinition` の record にコンストラクタ引数追加 → 全呼び出し箇所を修正。

JSON データ：
- `heightTier` 未指定でも default 5 で動くので、JSON 巡回は分割可能（最初に loader 拡張 + default 動作確認 → JSON 全更新を別 commit）。

Plan で詳細タスク分解する。

## 8. 関連ドキュメント

- 親 spec: `docs/superpowers/specs/2026-04-25-phase10-battle-system-design.md`
- heightForTier 既存実装: `src/Client/src/screens/BattleScreen.tsx:198-201` （`38 + clamped * 22`）
- Phase 10.3-MVP: `docs/superpowers/specs/2026-04-27-phase10-3-mvp-design.md`
