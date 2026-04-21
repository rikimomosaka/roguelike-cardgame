# Phase 5 — 暫定バトル（placeholder）+ 報酬生成 設計書

**日付:** 2026-04-22
**スコープ:** マップからノード選択 → 戦闘 (placeholder 即勝利) または非戦闘イベント → 報酬画面 → マップに戻る、の 1 周が通しで動く状態にする。実戦闘ロジックは Phase 6 以降。
**前提タグ:** Phase 4（マップ進行とセーブ連動）完了。

---

## 1. スコープとゴール

**含む**
- `RunState` v3 昇格（Gold, HP, Deck, Potions, ActiveBattle, ActiveReward, Encounter Queue, RewardRngState を追加）
- Core: `BattlePlaceholder`, `RewardGenerator`, `RewardApplier`, `NodeEffectResolver`, `EncounterQueue`
- Core: 全コンテンツの JSON 駆動化（`card-pool.json`, `potion-pool.json`, `enemy-definitions.json`, `enemy-encounters.json`, `reward-table.json`, `starter-deck.json`, スキーマのみ `relic-pool.json`）
- Server: battle/win、reward/{gold|potion|card|proceed}、potion/discard エンドポイント
- Client: 3 レイヤー UI（Map ベース + Battle オーバーレイ + Reward ポップアップ）、HP/Gold/Potion トップバー、Potion 捨てる機能
- 非戦闘ノード placeholder 効果: Event / Treasure で Gold 獲得、Rest で HP 全回復、Shop は素通り

**含まない（後続フェーズ）**
- 実戦闘ロジック（カードプレイ、エネルギー、敵 AI、ダメージ計算）: Phase 6
- Shop の在庫と購入: Phase 7
- Event の分岐選択肢: Phase 7
- Relic の獲得と効果: Phase 7 以降（JSON スキーマだけ Phase 5 で用意）
- Act 移動、Boss 撃破後の演出: Phase 8
- キャラクタ別デッキ切替（Phase 5 では single "default" character）: Phase 9 以降

**運用上の到達点**
- 新規ラン → Enemy マス選択 → Battle オーバーレイで敵情報表示 + 勝利ボタン → Reward popup で Gold / Potion / Card 3 択 → Proceed でマップに戻る、という流れが通しで動く
- Event / Rest / Treasure / Shop マスも Phase 5 の placeholder 効果で「反応する」（「バトル画面以外はすべて動く」方針）
- リロードすると Battle / Reward の途中状態から復帰できる
- カード・ポーション・敵の内容追加は JSON 編集のみで可能（ビルド不要な本体変更）

---

## 2. 全体アーキテクチャ

### 層構成

```
Client (React)
  GameRoot
    ├ MapScreen (常時ベースレイヤー)
    ├ BattleOverlay (ActiveBattle != null のとき z-index 10)
    ├ RewardPopup   (ActiveReward != null のとき z-index 20)
    └ InGameMenu    (既存, z-index 30)
    ↓ HTTP
Server (ASP.NET Core)
  RunsController (move + battle/win + reward/* + potion/discard)
    ↓ 呼び出し
Core
  BattlePlaceholder / RewardGenerator / RewardApplier / NodeEffectResolver / EncounterQueue
  ゲームデータ (embedded JSON) ローダー (GameDataLoader)
```

### 責務分離

| 層 | 新規成果物 | 責務 |
|---|---|---|
| Core/Battle | `BattlePlaceholder` | Battle 開始 / 勝利 の純関数。Phase 6 で差し替え予定 |
| Core/Rewards | `RewardGenerator` | Gold / Potion / Card を pool と rngState から抽選 |
| Core/Rewards | `RewardApplier` | RewardState を RunState（Gold / Potions / Deck）に反映 |
| Core/Run | `NodeEffectResolver` | NodeKind に応じて Battle 開始 or 非戦闘 placeholder 効果を適用 |
| Core/Battle | `EncounterQueue` | STS 非重複キューの生成と dequeue |
| Core/Data | `GameDataLoader` | 埋込み JSON をすべて読み、`CardCatalog` / `PotionCatalog` / `EnemyCatalog` / `EncounterCatalog` / `RewardTable` / `StarterDeckCatalog` / `RelicCatalog` を構築 |
| Server/Services | `RunStartService` 拡張 | 開始時に StarterDeck から Deck/HP/Gold/PotionSlotCount を初期化、Encounter Queue を seed からシャッフル |
| Server/Controllers | `RunsController` 拡張 | 上表 エンドポイント |
| Client/api | `runs.ts` 拡張 | battle/win、reward/*、potion/discard のクライアント関数 |
| Client/screens | `BattleOverlay.tsx` | 敵情報 + 勝利ボタン + マップ peek |
| Client/screens | `RewardPopup.tsx` | Gold/Potion/Card の 3 行 + Card 3 枚サブ画面 + Skip + Proceed |
| Client/screens | `MapScreen.tsx` 拡張 | トップバー (HP/Gold/Potions)、Potion 捨てるメニュー |

---

## 3. RunState v3 スキーマ

### 追加フィールド

```csharp
// キャラクタ
string CharacterId                  // 当面 "default" 固定、Phase 9 以降複数対応

// 戦闘/報酬で使うリソース
int CurrentHp
int MaxHp
int Gold
ImmutableArray<string> Deck         // CardDefinition.Id の多重集合
ImmutableArray<string> Potions      // PotionDefinition.Id、スロット順（空きは空文字 or 専用トリック）
int PotionSlotCount                 // 初期 3、将来可変

// アクティブサブ状態（併用不可: 片方 non-null のときは他方 null）
BattleState? ActiveBattle
RewardState? ActiveReward

// Encounter 非重複キュー
ImmutableArray<string> EncounterQueueWeak
ImmutableArray<string> EncounterQueueStrong
// Elite / Boss はプールが小さいため毎回再シャッフルでも可（実装は同じ EncounterQueue 機構を使う）
ImmutableArray<string> EncounterQueueElite
ImmutableArray<string> EncounterQueueBoss

// 動的確率補正
RewardRngState RewardRngState
```

### 新規 record 型

```csharp
public enum Rarity { Common, Uncommon, Rare }
public enum EnemyPoolId { Act1Weak, Act1Strong, Act1Elite, Act1Boss }
public enum BattleOutcome { Pending, Victory }
public enum CardRewardStatus { Pending, Claimed, Skipped }

public sealed record BattleState(
    string EncounterId,
    ImmutableArray<EnemyInstance> Enemies,  // Encounter から展開された実体、HP は乱数確定済み
    BattleOutcome Outcome);

public sealed record EnemyInstance(
    string EnemyDefinitionId,               // slime_acid_s 等
    int CurrentHp,
    int MaxHp,
    string CurrentMoveId);                  // initialMoveId からスタート（Phase 6 で使う）

public sealed record RewardState(
    int Gold,                               // Gold 額（未受取）
    bool GoldClaimed,
    string? PotionId,                       // ドロップ無しなら null
    bool PotionClaimed,
    ImmutableArray<string> CardChoices,     // 3 枚の CardDefinition.Id、カード報酬なしなら empty
    CardRewardStatus CardStatus);

public sealed record RewardRngState(
    int PotionChancePercent,                // 初期 40、drop で -10、miss で +10、[0,100]
    int RareChanceBonusPercent);            // Card の rare 率補正、Rare 出現でリセット、miss で +1
```

### 不変条件

- `ActiveBattle != null` のとき `ActiveReward == null`、逆も同じ（両方 non-null は不許可）
- Battle Victory endpoint は `ActiveBattle` を null 化してから `ActiveReward` を立てる（原子操作）
- `Potions.Length == PotionSlotCount`（空きは sentinel で埋める。実装は empty string "" とする）
- `RewardState.CardChoices.Length` は 0 または 3（部分提示なし）
- `Deck.Length >= 10`（初期デッキ10枚 + 報酬加算）

### 初期値（StarterDeck "default"）

- `CharacterId = "default"`
- `MaxHp = CurrentHp = 80`
- `Gold = 99`
- `Deck = ["c_starter_attack" × 5, "c_starter_defend" × 5]`
- `Potions = ["","",""]`, `PotionSlotCount = 3`
- `ActiveBattle = ActiveReward = null`
- `EncounterQueue*` = `EncounterQueue.Initialize(pool, encounters, rng)` の結果
- `RewardRngState = new(40, 0)`

---

## 4. Core サービス

### 4.1 `BattlePlaceholder`

```csharp
public static class BattlePlaceholder
{
    // NodeEffectResolver から呼ばれる。Encounter をプールから 1 つ引いて BattleState(Pending) を返し
    // RunState.ActiveBattle にセット。Encounter 内の敵ごとに HP を乱数確定。
    public static RunState Start(
        RunState state,
        EnemyPoolId pool,
        EncounterCatalog encounters,
        EnemyCatalog enemies,
        IRng rng);

    // Battle Victory endpoint から呼ばれる。ActiveBattle.Outcome を Victory にして返す。
    // ActiveBattle の null 化と Reward 生成は呼び側 (Server) で順次行う。
    public static RunState Win(RunState state);
}
```

### 4.2 `RewardGenerator`

```csharp
public static class RewardGenerator
{
    public static (RewardState reward, RewardRngState newRng) Generate(
        RewardContext context,              // EnemyPoolId or NonBattleKind (Event/Treasure)
        RewardRngState rngState,
        ImmutableArray<string> cardExclusions, // starter カード ID 等
        RewardTable table,
        CardCatalog cards,
        PotionCatalog potions,
        IRng rng);
}

public abstract record RewardContext
{
    public sealed record FromEnemy(EnemyPoolId Pool) : RewardContext;
    public sealed record FromNonBattle(NonBattleRewardKind Kind) : RewardContext;
}

public enum NonBattleRewardKind { Event, Treasure }
```

**Gold**: `table.pools[pool].gold = [min,max]` 範囲から一様乱数（NonBattle は `table.nonBattle[kind].gold`）。

**Potion**: 
- `table.pools[pool].potionBase`（Elite=100, Boss=0, その他=40）を使って抽選
- 通常プールでは `rngState.PotionChancePercent` が動的基準（初期 40）
- 判定: `rng.NextInt(100) < chance` で drop。drop 時は chance -= 10、miss 時は chance += 10、clamp [0, 100]
- Elite (100) / Boss (0) は動的補正対象外、chance は不変
- drop 時は `PotionCatalog` 全体から等確率で 1 つ

**Card**: 
- `table.pools[pool].rarityDist` と `rngState.RareChanceBonusPercent` を合成:
  - rare_final = min(100, dist.rare + bonus)
  - 余りを common/uncommon で比例按分
- 3 回独立抽選で Rarity 決定 → 各 Rarity pool から等確率で 1 枚
- 3 枚の Id が重複したら再抽選（同一レアリティに 3 種類なければ repeat 許容。Phase 5 では 10 種ずつなので問題なし）
- `cardExclusions` にある Id は全段階で除外（starter カードは報酬に出ない）
- 出た 3 枚に Rare が 1 枚以上含まれていれば `newRng.RareChanceBonusPercent = 0`、含まれていなければ `+= 1`
- NonBattle (Event/Treasure) では Card 報酬無し（CardChoices = empty, CardStatus = Claimed とみなして Proceed 可能）

### 4.3 `RewardApplier`

```csharp
public static class RewardApplier
{
    public static RunState ApplyGold(RunState s);
    public static RunState ApplyPotion(RunState s);            // スロット満杯なら InvalidOperationException
    public static RunState PickCard(RunState s, string cardId); // cardId は CardChoices に含まれる必要あり
    public static RunState SkipCard(RunState s);
    public static RunState Proceed(RunState s);                 // ActiveReward を null 化してマップに戻す
    public static RunState DiscardPotion(RunState s, int slotIndex);
}
```

**Proceed 条件**: `GoldClaimed && (PotionId == null || PotionClaimed) && CardStatus != Pending` が全て true のとき呼び出し可能。未達で呼ばれたら Server で 409。

**ApplyPotion 満杯判定**: `Potions` 内に空きスロット（`""`）が無ければ `InvalidOperationException`。Server はこれを 409 に変換。

### 4.4 `NodeEffectResolver`

```csharp
public static class NodeEffectResolver
{
    // move 成功時にサーバから呼ばれる。NodeKind を見て:
    //   Enemy/Elite/Boss → BattlePlaceholder.Start に委譲（ActiveBattle セット）
    //   Event  → RewardGenerator(NonBattle.Event)  → ActiveReward セット
    //   Treasure → RewardGenerator(NonBattle.Treasure) → ActiveReward セット
    //   Rest   → CurrentHp = MaxHp、ActiveReward 無し
    //   Shop   → 何もしない（素通り。Phase 7 で商人 UI を追加）
    //   Start  → 何もしない（初期マスは既に current になっている）
    public static RunState Resolve(
        RunState state,
        NodeKind kind,
        GameData data,          // 全 JSON データのファサード
        IRng rng);
}
```

**NodeKind → EnemyPoolId マッピング（Q6 A, Row ベース）**:
- `Enemy` node:
  - Row index を map の総 Row 数で正規化、前半 3 Row（Row 0-2）→ `Act1Weak`
  - Row 3 以降 → `Act1Strong`
- `Elite` → `Act1Elite`
- `Boss` → `Act1Boss`

Row 境界の具体値（STS 準拠: Act1 は 16 Row、3 Row まで weak）は `reward-table.json` の `weakRowsThreshold: 3` に置き、ハードコード回避。

### 4.5 `EncounterQueue`

```csharp
public static class EncounterQueue
{
    // Run 開始時に pool のみ抽出してシャッフル → Queue。
    public static ImmutableArray<string> Initialize(
        EnemyPoolId pool,
        EncounterCatalog encounters,
        IRng rng);

    // 先頭を取り、末尾に push した新キューを返す（非重複ローテ）。
    public static (string encounterId, ImmutableArray<string> newQueue) Draw(
        ImmutableArray<string> queue);
}
```

Phase 5 では Elite / Boss も同じ機構で扱う（シャッフルのみで、小さいため実質的な重複回避はあまり効かないが実装は共通化）。

---

## 5. Server エンドポイント

全て `X-Account-Id` ヘッダ必須。Account 不在は 404、ヘッダ欠落は 400、Run 状態ミスマッチは 409。

| Method | Path | body | 意味 | 応答 |
|---|---|---|---|---|
| POST | `/api/v1/runs/current/move` | `{ nodeId, elapsedSeconds }` | 既存。受け取ったノードに移動し、`NodeEffectResolver.Resolve` を呼ぶ | 204 |
| POST | `/api/v1/runs/current/battle/win` | `{ elapsedSeconds }` | `BattlePlaceholder.Win` → `ActiveBattle` を null、`RewardGenerator.Generate(FromEnemy(pool))` で `ActiveReward` を立てる | 204 (ActiveBattle 無しなら 409) |
| POST | `/api/v1/runs/current/reward/gold` | - | `RewardApplier.ApplyGold` | 204 (`ActiveReward` 無し / 既に Claimed なら 409) |
| POST | `/api/v1/runs/current/reward/potion` | - | `RewardApplier.ApplyPotion` | 204 / 409（満杯 or 既に Claimed or Potion 無し） |
| POST | `/api/v1/runs/current/reward/card` | `{ cardId?, skip? }` | Pick or Skip。cardId が Choices 外 or 両方指定 / 両方未指定は 400 | 204 / 400 / 409 |
| POST | `/api/v1/runs/current/reward/proceed` | `{ elapsedSeconds }` | `RewardApplier.Proceed`。未完了なら 409 | 204 |
| POST | `/api/v1/runs/current/potion/discard` | `{ slotIndex }` | `RewardApplier.DiscardPotion` | 204 / 400（空スロット指定 or out of range） |
| GET | `/api/v1/runs/current` | - | 既存。`RunSnapshotDto` を拡張して新フィールドを返す | 200 |

**`move` 内部処理**:
1. 既存の隣接チェック + `CurrentNodeId` 更新
2. `NodeEffectResolver.Resolve(state, kind, data, rng)` を呼ぶ
3. 結果を `SaveAsync` で永続化
4. 204 を返す（クライアントは `GET /current` で新状態を取得）

**elapsedSeconds 加算**: 既存の `PlaySeconds` 加算ルール（`Math.Clamp(elapsed, 0, 86400)`）を全 endpoint で踏襲。battle/win、reward/proceed は特に秒数を積む機会が多いので必ず受け付ける。

**`RunSnapshotDto` 拡張**:
```json
{
  "run": {
    // 既存: accountId, rngSeed, currentNodeId, playSeconds, progress, savedAtUtc
    "characterId": "default",
    "currentHp": 80, "maxHp": 80, "gold": 99,
    "deck": ["c_starter_attack", ...],
    "potions": ["p_health", "", ""], "potionSlotCount": 3,
    "activeBattle": { "encounterId": "...", "enemies": [...], "outcome": "Pending" } | null,
    "activeReward":  { "gold": 15, "goldClaimed": false, "potionId": null, "potionClaimed": true,
                       "cardChoices": ["c_common_01", ...], "cardStatus": "Pending" } | null
  },
  "map": { ... }  // 既存
}
```

Encounter 内の敵は DTO 層で nameJa, imageId まで展開して返す（UI 側のルックアップを省く）。

---

## 6. Client UI

### 6.1 ルート構造

```tsx
<GameRoot>
  <MapScreen ... />
  {activeBattle && <BattleOverlay ... />}
  {activeReward && <RewardPopup ... />}
  {menuOpen && <InGameMenu ... />}
</GameRoot>
```

画面遷移ではなく **同一ルート上でレイヤーを重ねる**。Map は常に描画され、Battle / Reward はオーバーレイとして z-index で前面に出る。

### 6.2 `MapScreen` 拡張

既存の SVG マップに加え、画面上部に **トップバー** を追加:

```
[HP 80/80] [Gold 99] [Potion: 🧪🧪⬜]
```

- HP: `currentHp/maxHp`
- Gold: 現在値
- Potion: スロット毎にアイコン or 空欄。ホバーで名前、**クリックで「捨てる」メニュー** のみ表示（Phase 5 では使用ボタン無し）
- スロット数は `potionSlotCount` に応じて動的（将来の拡張に備え CSS Grid で可変）

非戦闘ノード（Event/Rest/Treasure/Shop/Start）をクリックして move が成功したあとの表示:
- Rest 後: 一瞬 HP バーがフラッシュ（UI のみの演出）
- Shop 踏破: ポップアップ「商人は旅支度中だ…」（Phase 7 まで一時メッセージ、移動自体は成功）
- Event/Treasure: `activeReward` が立つので RewardPopup が自動で上に出る

### 6.3 `BattleOverlay`

半透明背景の上に:
- 敵情報パネル: 各敵の日本語名 + HP バー + イメージ placeholder（`imageId` のみ表示）を横並び
- 「勝利」ボタン（押下で `POST /battle/win` → `GET /current` → activeBattle は null、activeReward が立つ）
- 「マップを peek」ボタン: オーバーレイを一時的に非表示（ローカル state のみ、サーバ状態は不変）。マップのクリックは peek 中は無効化（クリックで戻る）

### 6.4 `RewardPopup`（STS-A フロー）

縦積みの 3 行（CardChoices が empty のときは 2 行）:
1. `+ XX Gold`（`goldClaimed` で checkmark）
2. `🧪 ポーション名`（`potionClaimed` で checkmark、PotionId == null のときは行自体非表示）
3. `✨ カードの報酬`（`cardStatus == Claimed/Skipped` で checkmark）

行クリック:
- Gold 行 → `POST /reward/gold`
- Potion 行 → `POST /reward/potion`。409（満杯）が返ったら「スロット満杯、先に捨ててください」アラート。この状態でもポップアップを閉じずに Potion スロットを表示して捨てる操作ができる（MapScreen 相当のスロット UI を RewardPopup 内に共通コンポーネントで表示）
- Card 行 → 3 枚提示サブ画面（または同一ポップアップ内の別 view）にトランジション。1 枚クリックで `POST /reward/card { cardId }`、下部の「Skip」で `POST /reward/card { skip: true }`

**Proceed ボタン**: 全行が checkmark になったときのみ有効。押下で `POST /reward/proceed` → popup 閉じる。

### 6.5 画面遷移（リロード復帰）

ロード時 `GET /current` の `activeBattle` / `activeReward` で復帰先を決定:
- `activeBattle != null` → MapScreen + BattleOverlay
- `activeReward != null` → MapScreen + RewardPopup
- 両方 null → MapScreen のみ

Battle 中 → Reward 画面は `POST /battle/win` のレスポンス後の `GET /current` で自動遷移（クライアント state に追加のフラグは不要）。

---

## 7. ゲームデータ（埋込み JSON）

配置: `src/Core/Data/`、`.csproj` に `<EmbeddedResource Include="Data/*.json" />` を追加（既存 `map-act1.json` と同じパターン）。

### 7.1 `card-pool.json`

報酬抽選用の **仮カード** 30 枚（Common 10 / Uncommon 10 / Rare 10）。名称・効果テキストは暫定で、無色カード相当を意識したファンタジー的命名。starter カードは **含まない**。

```json
{
  "cards": [
    { "id": "c_common_01", "nameJa": "鋭い一撃", "rarity": "Common",   "cost": 1, "effectText": "6 ダメージ" },
    { "id": "c_common_02", "nameJa": "鉄の守り", "rarity": "Common",   "cost": 1, "effectText": "5 ブロック" },
    ...（Common 10, Uncommon 10, Rare 10）
  ]
}
```

### 7.2 `starter-deck.json`

```json
{
  "characters": {
    "default": {
      "nameJa": "見習い冒険者",
      "maxHp": 80,
      "startingGold": 99,
      "potionSlotCount": 3,
      "deck": [
        "c_starter_attack","c_starter_attack","c_starter_attack","c_starter_attack","c_starter_attack",
        "c_starter_defend","c_starter_defend","c_starter_defend","c_starter_defend","c_starter_defend"
      ],
      "starterCards": [
        { "id": "c_starter_attack", "nameJa": "攻撃", "rarity": "Common", "cost": 1, "effectText": "6 ダメージ" },
        { "id": "c_starter_defend", "nameJa": "防御", "rarity": "Common", "cost": 1, "effectText": "5 ブロック" }
      ]
    }
  }
}
```

- `starterCards` はこのファイル内でカード定義を持つ（`card-pool.json` とは別管理、報酬抽選対象外）
- `deck` はキャラ開始時の多重集合
- 将来のキャラ追加は `characters` に別キーで並べるだけ

### 7.3 `potion-pool.json`

Phase 5 では使用機能なし、所持・捨てる・ドロップのみ。7 種:

```json
{
  "potions": [
    { "id": "p_health",   "nameJa": "体力ポーション",   "rarity": "Common",   "effectText": "HP を回復する" },
    { "id": "p_block",    "nameJa": "防御ポーション",   "rarity": "Common",   "effectText": "ブロックを得る" },
    { "id": "p_swift",    "nameJa": "速攻ポーション",   "rarity": "Common",   "effectText": "カードを引く" },
    { "id": "p_energy",   "nameJa": "エネルギーポーション","rarity":"Common", "effectText": "エネルギーを得る" },
    { "id": "p_strength", "nameJa": "力のポーション",   "rarity": "Uncommon", "effectText": "力を得る" },
    { "id": "p_fire",     "nameJa": "発火ポーション",   "rarity": "Uncommon", "effectText": "全体にダメージ" },
    { "id": "p_poison",   "nameJa": "毒ポーション",     "rarity": "Uncommon", "effectText": "毒を付与する" }
  ]
}
```

ドロップ時は等確率で 1 つ選択（Rarity はメタ情報として保持、Phase 5 では抽選重みに使わない）。

### 7.4 `enemy-definitions.json`

敵個体ごとの定義。**同じ見た目（imageId）でも move パターンや初期行動が違う場合は別 id で定義**する。これにより、複数体同時出現時に全員がコピーの動きをしない・弱/強プールで同じキャラを別調整できる。

```json
{
  "enemies": [
    {
      "id": "slime_acid_s",
      "nameJa": "スライム",
      "imageId": "slime_green_s",
      "hpRange": [30, 35],
      "initialMoveId": "tackle",
      "moves": [
        { "id": "tackle", "kind": "attack", "damageRange": [10, 12], "hits": 1, "nextMoveId": "defend" },
        { "id": "defend", "kind": "block",  "blockRange":  [5, 6],              "nextMoveId": "buff" },
        { "id": "buff",   "kind": "buff",   "buff": "strength",
          "amountRange": [1, 1], "nextMoveId": "tackle" }
      ]
    },
    {
      "id": "slime_spike_s",
      "nameJa": "スライム",
      "imageId": "slime_green_s",
      "hpRange": [30, 35],
      "initialMoveId": "defend",
      "moves": [
        { "id": "tackle", "kind": "attack", "damageRange": [10, 11], "hits": 1, "nextMoveId": "defend" },
        { "id": "defend", "kind": "block",  "blockRange":  [6, 7],              "nextMoveId": "buff" },
        { "id": "buff",   "kind": "buff",   "buff": "strength",
          "amountRange": [1, 1], "nextMoveId": "tackle" }
      ]
    },
    { "id": "fang_worm",    "nameJa": "ファングワーム", ... },
    { "id": "dark_cultist", "nameJa": "ダークカルティスト", ... },
    { "id": "cave_bat_a",   "nameJa": "ケイブバット", ... },
    { "id": "cave_bat_b",   "nameJa": "ケイブバット", ... },
    { "id": "blue_orc",     "nameJa": "ブルーオーク", ... },
    { "id": "red_orc",      "nameJa": "レッドオーク", ... },
    { "id": "goblin_a",     "nameJa": "ゴブリン", ... },
    { "id": "goblin_b",     "nameJa": "ゴブリン", ... },
    { "id": "goblin_c",     "nameJa": "ゴブリン", ... },
    { "id": "big_slime",    "nameJa": "ビッグスライム", ... },
    { "id": "dire_wolf",    "nameJa": "ダイアウルフ", ... },
    { "id": "mushroom_a",   "nameJa": "マッシュルーム", ... },
    { "id": "mushroom_b",   "nameJa": "マッシュルーム", ... },
    { "id": "bandit",       "nameJa": "バンディット", ... },
    { "id": "ogre",         "nameJa": "オーガ", ... },
    { "id": "hobgoblin",    "nameJa": "ホブゴブリン", ... },
    { "id": "sleeping_dragon", "nameJa": "眠れるドラゴン", ... },
    { "id": "iron_golem_a", "nameJa": "アイアンゴーレム", ... },
    { "id": "iron_golem_b", "nameJa": "アイアンゴーレム", ... },
    { "id": "iron_golem_c", "nameJa": "アイアンゴーレム", ... },
    { "id": "slime_king",   "nameJa": "キングスライム", ... },
    { "id": "six_ghost",    "nameJa": "シックスゴースト", ... },
    { "id": "guardian_golem","nameJa": "ガーディアンゴーレム", ... }
  ]
}
```

数値は STS A0 を参考に最終確定、各敵に moves（attack / block / buff / debuff）を少なくとも 2 種類定義。**Phase 5 placeholder 戦闘では `nameJa` / `imageId` / `hpRange` しか使われない**が、Phase 6 で AI を実装する際にこの JSON を直接流用する前提で完全なスキーマを書く。

### 7.5 `enemy-encounters.json`

STS Act1 相当の encounter 構成（名前はファンタジー寄せ）:

```json
{
  "encounters": [
    { "id": "enc_w_fang_worm",   "pool": "act1_weak",   "enemies": ["fang_worm"] },
    { "id": "enc_w_dark_cultist","pool": "act1_weak",   "enemies": ["dark_cultist"] },
    { "id": "enc_w_cave_bats",   "pool": "act1_weak",   "enemies": ["cave_bat_a", "cave_bat_b"] },
    { "id": "enc_w_small_slimes","pool": "act1_weak",   "enemies": ["slime_acid_s", "slime_spike_s"] },

    { "id": "enc_s_goblin_gang", "pool": "act1_strong", "enemies": ["goblin_a", "goblin_b", "goblin_c"] },
    { "id": "enc_s_big_slime",   "pool": "act1_strong", "enemies": ["big_slime"] },
    { "id": "enc_s_slime_rush",  "pool": "act1_strong", "enemies": ["slime_acid_s","slime_spike_s","big_slime"] },
    { "id": "enc_s_blue_orc",    "pool": "act1_strong", "enemies": ["blue_orc"] },
    { "id": "enc_s_red_orc",     "pool": "act1_strong", "enemies": ["red_orc"] },
    { "id": "enc_s_cave_bats3",  "pool": "act1_strong", "enemies": ["cave_bat_a","cave_bat_b","cave_bat_a"] },
    { "id": "enc_s_mushrooms",   "pool": "act1_strong", "enemies": ["mushroom_a","mushroom_b"] },
    { "id": "enc_s_bandit",      "pool": "act1_strong", "enemies": ["bandit"] },
    { "id": "enc_s_thugs",       "pool": "act1_strong", "enemies": ["ogre","bandit"] },
    { "id": "enc_s_wildlife",    "pool": "act1_strong", "enemies": ["dire_wolf","mushroom_a"] },

    { "id": "enc_e_hobgoblin",   "pool": "act1_elite",  "enemies": ["hobgoblin"] },
    { "id": "enc_e_dragon",      "pool": "act1_elite",  "enemies": ["sleeping_dragon"] },
    { "id": "enc_e_golems",      "pool": "act1_elite",  "enemies": ["iron_golem_a","iron_golem_b","iron_golem_c"] },

    { "id": "enc_b_slime_king",  "pool": "act1_boss",   "enemies": ["slime_king"] },
    { "id": "enc_b_six_ghost",   "pool": "act1_boss",   "enemies": ["six_ghost"] },
    { "id": "enc_b_guardian",    "pool": "act1_boss",   "enemies": ["guardian_golem"] }
  ]
}
```

### 7.6 `reward-table.json`

```json
{
  "pools": {
    "act1_weak":   { "gold": [10, 20], "potionBase": 40,
                     "rarityDist": { "common": 60, "uncommon": 37, "rare": 3 } },
    "act1_strong": { "gold": [15, 25], "potionBase": 40,
                     "rarityDist": { "common": 60, "uncommon": 37, "rare": 3 } },
    "act1_elite":  { "gold": [25, 35], "potionBase": 100,
                     "rarityDist": { "common": 50, "uncommon": 40, "rare": 10 } },
    "act1_boss":   { "gold": [95,105], "potionBase": 0,
                     "rarityDist": { "common": 0,  "uncommon": 40, "rare": 60 } }
  },
  "nonBattle": {
    "event":    { "gold": [10, 20] },
    "treasure": { "gold": [25, 35] }
  },
  "potionDynamic": { "initialPercent": 40, "step": 10, "min": 0, "max": 100 },
  "rareChance":    { "initialBonus": 0, "perBattleIncrement": 1 },
  "enemyPoolRouting": {
    "weakRowsThreshold": 3
  }
}
```

### 7.7 `relic-pool.json`（Phase 5 ではスキーマのみ）

```json
{
  "relics": [
    { "id": "r_placeholder_burning_blood", "nameJa": "燃える血潮",
      "rarity": "Starter", "effectText": "戦闘終了時 HP を 6 回復" }
  ]
}
```

Phase 5 では RunState にレリック欄を持たせず、この JSON は読み込んで `RelicCatalog` オブジェクトとして存在するだけ（将来 Phase 7 で機能実装）。

### 7.8 `GameDataLoader`

```csharp
public sealed record GameData(
    CardCatalog Cards,
    PotionCatalog Potions,
    EnemyCatalog Enemies,
    EncounterCatalog Encounters,
    RewardTable Rewards,
    StarterDeckCatalog Starters,
    RelicCatalog Relics);

public static class GameDataLoader
{
    public static GameData LoadEmbedded();
}
```

DI では singleton 登録（`AddSingleton<GameData>(_ => GameDataLoader.LoadEmbedded())`）。

---

## 8. エラー処理

### 8.1 Server 層

- 401/403: Phase 5 では扱わない（`X-Account-Id` ヘッダのみ、既存踏襲）
- 400: ヘッダ欠落、cardId と skip の両指定 / 両欠落、slotIndex 範囲外
- 404: Account 不在（Phase 4 からの踏襲）
- 409:
  - `move`: ActiveBattle/ActiveReward が既に立っている（状態遷移ミスマッチ）
  - `battle/win`: ActiveBattle 無し
  - `reward/gold/potion/card`: ActiveReward 無し or 対象が既に Claimed
  - `reward/potion`: Potion スロット満杯
  - `reward/proceed`: 未完了項目あり
  - `potion/discard`: 空スロット指定

### 8.2 Core 層

- `ArgumentException`: 不正な NodeKind、cardId が Choices 外 等
- `InvalidOperationException`: Potion スロット満杯、Proceed 条件未達 等
- Server はこれらを適切な HTTP status にマップ（既存 RunsController パターン踏襲）

### 8.3 クライアント層

- API エラー時は既存の Phase 4 `runs.ts` と同じく例外を投げ、画面側で alert 表示
- 409（満杯）は特別扱い: Potion スロット UI を表示して discard できる導線を出す

---

## 9. テスト戦略

### 9.1 Core.Tests（xUnit）

| テスト対象 | 主なケース |
|---|---|
| `RewardGeneratorTests` | 各プールの Gold 範囲、Potion 動的確率推移、Card レア分布、3 枚重複排除、cardExclusions 除外、NonBattle では Card 無し |
| `RewardApplierTests` | Gold 受取、Potion 受取（満杯で例外）、Card Pick / Skip、Proceed 条件、DiscardPotion |
| `BattlePlaceholderTests` | Start で ActiveBattle Pending、Win で Outcome Victory |
| `NodeEffectResolverTests` | 各 NodeKind（Enemy/Elite/Boss/Event/Rest/Shop/Treasure/Start）の期待される RunState 遷移 |
| `EncounterQueueTests` | Initialize がプール全件を含む、Draw が先頭→末尾ローテ、非重複 |
| `GameDataLoaderTests` | 全 JSON がパース成功、ID 参照整合（Encounter.enemies が全て EnemyDefinition に存在、etc） |

### 9.2 Server.Tests

| テスト対象 | 主なケース |
|---|---|
| `BattleEndpointsTests`（新規） | `/battle/win` が ActiveReward を立てる、ActiveBattle 無しで 409 |
| `RewardEndpointsTests`（新規） | `/reward/gold`, `/reward/potion`, `/reward/card` それぞれの 204 / 400 / 409、`/reward/proceed` の完了判定 |
| `PotionDiscardTests`（新規） | 空スロット指定で 400、正常で 204、満杯 → discard → 受取可能 |
| `NonBattleMoveTests`（新規） | Event/Treasure で ActiveReward、Rest で HP 全回復、Shop で何も起きない |
| `RunsControllerTests`（既存） | RunSnapshotDto 拡張の後方互換（既存テストが壊れないことの確認） |

### 9.3 Client

Phase 4 と同じく **手動ブラウザ確認** を実装後の受入条件とする（自動 UI テストは Phase 5 では必須としない）。確認項目:

- 新規ラン → Enemy ノード → Battle オーバーレイ → 勝利 → Reward popup（Gold/Potion/Card）→ Proceed でマップに戻る
- Rest マスで HP 全回復、Event/Treasure で Reward popup（Card 無し）、Shop で placeholder メッセージ
- Reward 途中でリロード → 続きから再開できる
- Potion スロット満杯で報酬 Potion を受け取ろうとすると alert、捨てて受取可能
- 同種敵複数体（小スライム ×2 等）で別 move パターンが保持される（ただし Phase 5 では初期 move id のみ表示で OK）
- トップバー（HP/Gold/Potion）の同期、heartbeat で PlaySeconds 加算

---

## 10. 実装順序（writing-plans で細分化する粗い目安）

1. Core データ層: 全 JSON 定義 + `GameDataLoader` + record 型定義 + `GameDataLoaderTests`
2. Core Run: `RunState` v3 昇格 + migration（既存 v2 セーブデータは破棄 or 移行）
3. Core Battle: `EncounterQueue` + `BattlePlaceholder`
4. Core Rewards: `RewardGenerator` + `RewardApplier`
5. Core Run: `NodeEffectResolver`
6. Server: `RunStartService` 拡張（StarterDeck 初期化、EncounterQueue 初期化）
7. Server: `RunsController` 新規 endpoint（battle/win、reward/*、potion/discard）+ move 拡張
8. Server: `RunSnapshotDto` 拡張
9. Client: API 関数 + 型定義
10. Client: `MapScreen` トップバー + Potion スロット UI
11. Client: `BattleOverlay`
12. Client: `RewardPopup`
13. Client: 動作確認 + リロード復帰テスト
14. 最終レビュー

---

## 付録 A. 既存セーブデータ互換性

Phase 4 の RunState v2 セーブはサーバ起動時の JSON デシリアライズで v3 に移行できないため、**Phase 5 のデプロイ時に v2 以前のセーブは破棄** する方針（個人開発段階のため）。`SaveSchemaVersion` フィールドに `3` を入れ、読み込み時に `!= 3` なら silently drop してもよい。

## 付録 B. Phase 6 以降での差し替え予定

- `BattlePlaceholder` → 実戦闘（カードプレイ、敵 AI）に置き換え。Encounter Queue / EnemyDefinition の moves は Phase 6 でそのまま使う
- `NodeEffectResolver` の Event/Treasure/Shop/Rest → Phase 7 で本格実装（Event は分岐選択、Shop は在庫+購入、Rest は休息 or カードアップグレード、Treasure は relic 選択）
- `RelicCatalog` → Phase 7 で RunState.Relics / RelicEffects を追加
- キャラクタ別プール → Phase 9 以降、`starter-deck.json` に複数 characters を追加し、`card-pool.json` をクラス別に分割
