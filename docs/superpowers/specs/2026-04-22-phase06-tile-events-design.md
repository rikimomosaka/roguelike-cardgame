# Phase 6 — マス別イベント（商人・宝箱・休憩・?・エリート）設計書

**Date:** 2026-04-22
**Depends on:** Phase 3（マップ生成）、Phase 4（マップ進行）、Phase 5（暫定バトル・報酬）
**Next:** Phase 7（ボス・層移動・HP 全回復）

## Goal

Phase 5 まででバトル／報酬／マップ進行が暫定動作している状態から、各マス種別を本来の挙動に拡張する。`Enemy/Elite/Boss` はレアリティフロアを整理、`Treasure` はレリック専用、`Rest` は回復／強化の選択、`Merchant` は購入・廃棄、`?` は新 TileKind `Event` として選択式イベントを実装する。非バトル文脈で発動するレリック効果（`OnPickup` / `OnMapTileResolved` / `Passive` 修飾）もここで配線する。バトル内レリック効果は Phase 10 へ先送り。

## Non-Goals

- バトル内レリック効果（`OnBattleStart` / `OnBattleEnd` / バトル中 `Passive`）の配線。Phase 10 で本格バトルと同時に対応。
- Event 4 件目以降のコンテンツ拡充。Phase 6 では構造検証用の 3 件のみ。
- 層移動・Boss 撃破後の結果画面・HP 全回復。Phase 7。
- 図鑑（Bestiary）への記録。Phase 8。
- マルチプレイでの Event/Merchant 同期。Phase 9。
- UI のビジュアルデザイン。Phase 6 では既存のデバッグスタイルを踏襲し、後で Claude Design (aidesigner) で一括実施する。

## Architecture

### 方針
- マス種別ごとのロジックは独立した純関数クラスに分割（`NodeEffectResolver` を一枚岩化しない）。
- Core は JSON マスタからロード済みの `DataCatalog` と `IRng` のみに依存。ネットワーク・I/O 不要。
- Server コントローラは薄く、Core の純関数を呼んで state を返すだけ。SignalR push は既存の run state 同期機構に乗せる。
- 同時に複数モードに入らない不変条件を `RunState.Validate()` で強制する（ActiveBattle / ActiveReward / ActiveMerchant / ActiveEvent は最大 1 つ）。

### Core 新規モジュール

| パス | 役割 |
|---|---|
| `src/Core/Events/EventDefinition.cs` | record: `Id`, `Name`, `Description`, `Choices` |
| `src/Core/Events/EventChoice.cs` | record: `Label`, `Condition?`, `Effects` |
| `src/Core/Events/EventEffect.cs` | タグ付き record: `GainMaxHp` / `LoseMaxHp` / `GainGold` / `PayGold` / `Heal` / `TakeDamage` / `GainRelicRandom(CardRarity)` / `GrantCardReward` |
| `src/Core/Events/EventCondition.cs` | record: `MinGold?` など。将来拡張可。 |
| `src/Core/Events/EventJsonLoader.cs` | JSON → `EventDefinition` |
| `src/Core/Events/EventPool.cs` | `PickEvent(IRng, IReadOnlyList<EventDefinition>) → EventDefinition`。Phase 6 は一様抽選。 |
| `src/Core/Events/EventResolver.cs` | `ApplyChoice(RunState, EventInstance, int choiceIndex, DataCatalog, IRng) → RunState` |
| `src/Core/Events/EventInstance.cs` | record: `EventId`, `Choices`（解決時のスナップショット）|
| `src/Core/Merchant/MerchantInventory.cs` | record: `Cards: IReadOnlyList<MerchantOffer>`, `Relics: IReadOnlyList<MerchantOffer>`, `Potions: IReadOnlyList<MerchantOffer>`, `DiscardSlotUsed: bool`, `DiscardPrice: int` |
| `src/Core/Merchant/MerchantOffer.cs` | record: `Id`, `Price`, `Sold: bool` |
| `src/Core/Merchant/MerchantInventoryGenerator.cs` | `Generate(DataCatalog, MerchantPrices, IRng) → MerchantInventory` |
| `src/Core/Merchant/MerchantActions.cs` | 純関数群: `BuyCard` / `BuyRelic` / `BuyPotion` / `DiscardCard` / `Leave` |
| `src/Core/Merchant/MerchantPrices.cs` | record: `Cards`, `Relics`, `Potions`（`Dictionary<CardRarity,int>`）, `DiscardSlotPrice` |
| `src/Core/Merchant/MerchantPricesJsonLoader.cs` | JSON → `MerchantPrices` |
| `src/Core/Rest/RestActions.cs` | `Heal(RunState) → RunState`, `UpgradeCard(RunState, int deckIndex, DataCatalog) → RunState` |
| `src/Core/Cards/CardInstance.cs` | record: `Id: string`, `Upgraded: bool` |
| `src/Core/Cards/CardUpgrade.cs` | `CanUpgrade(CardInstance, DataCatalog) → bool`、`Upgrade(CardInstance) → CardInstance` |
| `src/Core/Relics/RelicInventory.cs` | record: `Ids: ImmutableArray<string>`、追加・削除ヘルパ |
| `src/Core/Relics/NonBattleRelicEffects.cs` | `ApplyOnPickup(RunState, string relicId, DataCatalog) → RunState`、`ApplyOnMapTileResolved(RunState, DataCatalog) → RunState`、`ApplyPassiveRestHealBonus(int base, RunState, DataCatalog) → int` |

### Core 変更モジュール

| パス | 変更内容 |
|---|---|
| `src/Core/Run/RunState.cs` | `Deck: ImmutableArray<string>` → `ImmutableArray<CardInstance>` に変更。`ActiveMerchant: MerchantInventory?`、`ActiveEvent: EventInstance?`、`ActiveRestPending: bool` を追加。`SchemaVersion` を 3→4 に。`Validate()` に多重モード禁止チェック／Upgraded 不整合チェックを追加。 |
| `src/Core/Run/RunStateSerializer.cs` | SchemaVersion 4 対応。SchemaVersion 3 からの移行は「`Deck: string[]` → `CardInstance[]` (Upgraded=false) に変換」の one-shot migration を入れる（既存セーブを壊さない）。 |
| `src/Core/Run/NodeEffectResolver.cs` | `Merchant`: `MerchantInventoryGenerator.Generate` → `ActiveMerchant` にセット。`Event`: 新 case、`EventPool.PickEvent` → `ActiveEvent` にセット。`Rest`: 副作用なしに変更（選択待ち、画面側で `RestActions` を呼ぶ）。`Treasure`: `RewardGenerator.NonBattle` を Treasure 用に切り替え（relic-only、gold なし）。`OnMapTileResolved` トリガのレリック効果をマス解決後に適用。 |
| `src/Core/Rewards/RewardGenerator.cs` | `RewardContext.Battle` の `EnemyTier` に応じてレアリティ最低フロアを適用（Elite=Rare 以上、Boss=Epic のみ）。Treasure は gold を出さず relic 1 個のみ。 |
| `src/Core/Rewards/RewardState.cs` | `Relic: string?`、`RelicClaimed: bool` を追加。Treasure／将来の Event で使う。 |
| `src/Core/Rewards/RewardApplier.cs` | `ClaimRelic(RunState) → RunState` を追加。`NonBattleRelicEffects.ApplyOnPickup` を連鎖させる。 |
| `src/Core/Map/TileKind.cs` | `Event` を追加。 |
| `src/Core/Map/UnknownResolutionConfig.cs` | weights に `Event` を含める（Phase 6 の初期値 Weight: Event=25）。 |
| `src/Core/Data/DataCatalog.cs` | `Events: ImmutableDictionary<string, EventDefinition>`、`MerchantPrices: MerchantPrices` を追加。 |

### Core 新規データ（埋め込みリソース）

| パス | 内容 |
|---|---|
| `src/Core/Data/Events/blessing_fountain.json` | 無条件 Gold/HP トレードオフ |
| `src/Core/Data/Events/shady_merchant.json` | Gold 条件ロック + レリック取得 |
| `src/Core/Data/Events/old_library.json` | HP 喪失と引き換えに Gold、`grantCardReward` で RewardPopup 流用 |
| `src/Core/Data/Relics/extra_max_hp.json` | OnPickup: `gainMaxHp: 7` |
| `src/Core/Data/Relics/coin_purse.json` | OnPickup: `gainGold: 50` |
| `src/Core/Data/Relics/traveler_boots.json` | OnMapTileResolved: `gainGold: 1` |
| `src/Core/Data/Relics/warm_blanket.json` | Passive: Rest 回復量 +10HP |
| `src/Core/Data/merchant-prices.json` | カード/レリック/ポーション × レアリティの固定価格 + 廃棄枠価格 |

**merchant-prices.json 初期値:**
```json
{
  "cards":   { "Common": 50, "Rare": 80, "Epic": 150 },
  "relics":  { "Common": 150, "Rare": 250, "Epic": 350 },
  "potions": { "Common": 50, "Rare": 75, "Epic": 100 },
  "discardSlotPrice": 75
}
```

### Server 新規コントローラ

すべて `api/v1/...` 配下、既存の run セッションに対する操作。

| エンドポイント | 役割 |
|---|---|
| `GET /merchant/inventory` | `ActiveMerchant` を返す。無ければ 409。|
| `POST /merchant/buy` body: `{ kind: "card"|"relic"|"potion", id: string }` | 対応する `MerchantActions.BuyXxx` を呼ぶ。所持金不足 400、枠満杯 409、在庫なし 404。|
| `POST /merchant/discard` body: `{ deckIndex: int }` | 購入扱いで廃棄枠を消費。廃棄済み 409、所持金不足 400。|
| `POST /merchant/leave` | `ActiveMerchant=null`、マス消費を確定。|
| `GET /event/current` | `ActiveEvent` を返す。無ければ 409。|
| `POST /event/choose` body: `{ choiceIndex: int }` | `EventResolver.ApplyChoice`。条件未満 400、インデックス不正 400。|
| `POST /rest/heal` | `RestActions.Heal`。Rest 以外のマスにいるなら 409。|
| `POST /rest/upgrade` body: `{ deckIndex: int }` | `RestActions.UpgradeCard`。強化不可カード 409、インデックス不正 400。|
| `POST /reward/claim-relic` | 既存 `RewardController` に追加。Treasure / Event で `ActiveReward.Relic` を所持に移す。|

### Client 新規画面

| パス | 内容 |
|---|---|
| `src/Client/src/screens/MerchantScreen.tsx` | 3 カテゴリの在庫一覧、購入ボタン（不足/満杯時 disabled）、廃棄セクション、離脱ボタン |
| `src/Client/src/screens/EventScreen.tsx` | 説明文 + 選択肢ボタン群、条件未満は disabled |
| `src/Client/src/screens/RestScreen.tsx` | 「回復」「強化」2 ボタン、強化選択時はデッキ一覧から強化可能カードをピック |
| `src/Client/src/hooks/useRelicCatalog.ts` | 既存 `useCardCatalog` と同形。`/api/v1/catalog/relics` を叩く |
| `src/Client/src/hooks/useEventCatalog.ts` | `/api/v1/catalog/events` を叩く |
| `src/Client/src/api/merchant.ts` / `event.ts` / `rest.ts` | 対応 API ラッパ |

### Client 既存変更

| パス | 変更内容 |
|---|---|
| `src/Client/src/screens/MapScreen.tsx` | `TileKind` に応じて Merchant/Event/Rest の各オーバーレイを出し分ける分岐を追加。既存の BattleOverlay / RewardPopup と同じ仕組み。|
| `src/Client/src/screens/RewardPopup.tsx` | `reward.relicId` がある場合は relic 行を表示（`potionId` と同じパターン）。`onClaimRelic` を追加。|
| `src/Client/src/components/TopBar.tsx` | デッキ一覧表示を `CardInstance` 対応に（`id` + Upgraded フラグで名前に `+` を付ける）。|
| `src/Server/Controllers/CatalogController.cs` | `/catalog/relics`、`/catalog/events` エンドポイントを追加（既存 `/catalog/cards`, `/catalog/potions` と同様）。|

## Data Flow

### Merchant
1. Map 上で Merchant マスを選択 → `SelectNextNode` → `NodeEffectResolver.Resolve` が `MerchantInventoryGenerator.Generate` を呼び `ActiveMerchant` をセット。
2. Client は `TileKind.Merchant` かつ `ActiveMerchant != null` を検知して `MerchantScreen` を表示。
3. 購入: `/merchant/buy` → Gold 減算、対象アイテムを `Sold=true` にマーク、`Deck`/`Relics`/`Potions` に追加。レリックなら `onPickup` 効果発動。
4. 廃棄: `/merchant/discard` → Gold 減算、`Deck` から CardInstance を除外、`DiscardSlotUsed=true`。
5. 離脱: `/merchant/leave` → `ActiveMerchant=null`。以降このマスへは戻れない（Phase 4 の訪問済み制約）。

### Event
1. Map で Event マス（`TileKind.Event`）を選択 → `NodeEffectResolver` が `EventPool.PickEvent` で 1 件抽選、`ActiveEvent` にセット。
2. Client は `EventScreen` を表示。選択肢は `EventChoice.Condition` を満たさないと disabled。
3. `/event/choose` → `EventResolver.ApplyChoice` が各 `EventEffect` を順次適用:
   - 直接系（`gainGold` / `payGold` / `takeDamage` / `gainMaxHp` / `gainRelicRandom`）→ RunState を直接更新。`gainRelicRandom` は指定レアリティの未所持レリックから抽選して所持に追加、`onPickup` 効果を発動。
   - 報酬系（`grantCardReward`）→ `RewardGenerator.Battle` 相当のカード 3 択を `ActiveReward` にセット（通常敵プール使用）。
4. 効果適用後 `ActiveEvent=null`。`ActiveReward` があれば RewardPopup、無ければ Proceed 可能。

### Rest
1. Map で Rest マスを選択 → `NodeEffectResolver` が `ActiveRestPending = true` をセット（それ以外の副作用なし）。
2. Client は `TileKind.Rest` かつ `ActiveRestPending` を検知して `RestScreen` を表示。
3. 「回復」: `/rest/heal` → `CurrentHp = min(MaxHp, CurrentHp + ceil(MaxHp * 0.30) + passiveBonus)`、`ActiveRestPending = false`。`passiveBonus` は `NonBattleRelicEffects.ApplyPassiveRestHealBonus` で算出（warm_blanket 等）。
4. 「強化」: 強化可能なデッキカード一覧を表示 → `/rest/upgrade` body: `{ deckIndex: int }` → `CardInstance.Upgraded = true`、`ActiveRestPending = false`。
5. `ActiveRestPending = false` のまま `/rest/*` を再度呼ぶと 409。Client は画面から離れる／Proceed するまで Rest の完了状態を保つ。

### Treasure
1. Map で Treasure マス選択 → `NodeEffectResolver` が `RewardGenerator.Generate(FromNonBattle(Treasure), ...)` を呼ぶ。
2. `RewardGenerator.GenerateFromNonBattle` は Treasure の場合 gold / potion / card を生成せず、未所持レリックから 1 個抽選して `ActiveReward.Relic` にセット。
3. 対応して `act1.json` の `nonBattle.treasure.gold` を `[0, 0]` に変更（現状 `[25, 35]`）。
4. Client は既存 `RewardPopup` を表示。relic 行のみ出る（gold / potion / card 行は非表示）。
5. `/reward/claim-relic` → 所持レリックに追加、`onPickup` 効果発動。Proceed 可能。

### Elite / Boss（レアリティフロア）
- データ側の変更だけで実現。既存の `src/Core/Data/RewardTable/act1.json` を次のように調整:
  - `pools.elite.rarityDist`: 現 `{ common: 50, rare: 40, epic: 10 }` → `{ common: 0, rare: 70, epic: 30 }`
  - `pools.boss.rarityDist`: 現 `{ common: 0, rare: 40, epic: 60 }` → `{ common: 0, rare: 0, epic: 100 }`
- `RewardGenerator.PickCard` のロジックは変更不要（既存の重み付き抽選が rarityDist を参照するのみ）。

### OnMapTileResolved トリガ
- 各マスの処理が「プレイヤーが完全に次マスへ進める状態」になったタイミングで `NonBattleRelicEffects.ApplyOnMapTileResolved` を 1 回適用。具体的には:
  - Battle: 勝利後の Reward 取得完了時
  - Treasure: relic claim 完了時
  - Merchant: Leave 時
  - Event: 選択確定かつ ActiveReward なしの時 / ActiveReward ありなら RewardPopup 取得完了時
  - Rest: Heal または Upgrade 選択完了時
- 全ての経路が `RunActions.Proceed`（または同等の遷移関数）を通る形に統一し、そこで適用する。

## Error Handling / Invariants

### RunState.Validate() 追加検証
- `ActiveBattle`, `ActiveReward`, `ActiveMerchant`, `ActiveEvent` のうち非 null は最大 1 つ。`ActiveRestPending == true` のときは他の 4 つは全て null。
- `Deck` の全 `CardInstance.Id` は `DataCatalog.Cards` に存在する。
- `CardInstance.Upgraded == true` のものは、対応する `CardDefinition.UpgradedEffects != null`。
- `ActiveEvent.Choices.Length >= 1`。

### 例外マッピング
- Core の純関数は不正入力で `InvalidOperationException` / `ArgumentException` を投げる。
- Server コントローラは以下のマップで HTTP ステータスに変換:
  - 所持金不足 / インデックス範囲外 → **400 Bad Request**
  - ポーション枠満杯 / 廃棄済み / 強化済み / モード不一致（例: Rest 以外で `/rest/heal`）→ **409 Conflict**
  - 在庫に無い item id → **404 Not Found**
- 既存 Phase 5 のエラーレスポンス形式を踏襲（ASP.NET Core 既定の `ProblemDetails` JSON）。

### セーブマイグレーション
- `RunStateSerializer` は SchemaVersion 3 → 4 への one-shot 変換をサポート:
  - `Deck: string[]` → `CardInstance[]`（全て `Upgraded=false`）。
  - `ActiveMerchant` / `ActiveEvent` は存在しなければ null。
  - SchemaVersion を 4 に書き換え、次回セーブで最新形式に確定。
- SchemaVersion 2 以下からの移行はサポート対象外（Phase 5 で既に 3 に移行済み）。

## Testing

### Core.Tests 新規

- `Events/EventDefinitionJsonTests.cs` — 3 件の JSON を読み込めて構造が一致
- `Events/EventResolverTests.cs` — 各 EventEffect の適用、条件ロック、複合 effects、`grantCardReward` で ActiveReward がセットされる
- `Events/EventPoolTests.cs` — 決定的抽選、同 seed で同結果
- `Merchant/MerchantInventoryGeneratorTests.cs` — カード 5 / レリック 2 / ポーション 3 の件数、重複なし、価格がレアリティ通り
- `Merchant/MerchantActionsTests.cs` — 各 Buy（所持金十分 / 不足 / 枠満杯 / 売り切れ）、Discard（十分 / 不足 / 二重使用）
- `Rest/RestActionsTests.cs` — Heal: 30% ceil、MaxHp キャップ、warm_blanket 保持時 +10。Upgrade: 強化可カードだけ対象、既に強化済みは拒否
- `Cards/CardInstanceSerializerTests.cs` — CardInstance の JSON 往復、SchemaVersion 3→4 マイグレーション
- `Cards/CardUpgradeTests.cs` — `CanUpgrade` の真偽
- `Relics/NonBattleRelicEffectsTests.cs` — extra_max_hp/coin_purse の OnPickup、traveler_boots の OnMapTileResolved、warm_blanket の Rest 修飾
- `Rewards/RewardGeneratorTests.cs` 拡張 — Elite 抽選結果が全部 Rare 以上、Boss 全部 Epic、Treasure は relic のみで gold/card なし
- `Run/RunStateValidationTests.cs` — 多重 Active 不変条件違反検出、Upgraded 不整合検出
- `Run/RunStateSerializerTests.cs` 拡張 — SchemaVersion 4 往復、旧形式（v3）ロード時のマイグレーション
- `Map/UnknownResolverTests.cs` 拡張 — Event kind が weights に含まれる、decision が決定的

### Server.Tests 新規

- `Controllers/MerchantControllerTests.cs` — GET inventory / Buy 全種 / Discard / Leave、エラーケース一式
- `Controllers/EventControllerTests.cs` — GET current / Choose、条件未満 400、choiceIndex 範囲外 400
- `Controllers/RestControllerTests.cs` — Heal / Upgrade、モード不一致 409
- `Controllers/RewardEndpointsTests.cs` 拡張 — `claim-relic` の成功・失敗
- `Controllers/CatalogControllerTests.cs` 拡張 — `/catalog/relics`, `/catalog/events` の存在と内容

### Client Vitest 新規

- `screens/MerchantScreen.test.tsx` — 在庫表示、購入ボタン disabled 条件、廃棄フロー
- `screens/EventScreen.test.tsx` — 選択肢表示、条件未満 disabled、選択後の遷移
- `screens/RestScreen.test.tsx` — 回復 / 強化 選択、強化可カードのフィルタ
- `api/merchant.test.ts` / `api/event.test.ts` / `api/rest.test.ts` — ラッパ関数の引数と返り値
- `hooks/useRelicCatalog.test.ts` / `useEventCatalog.test.ts`

### E2E 手動確認（Done 判定）

1 ランの中で以下の各マスを踏み、すべて破綻なく進行できる:
- Enemy（既存）/ Elite（Rare 以上のカードが出る）/ Boss（Epic のみ出る）
- Treasure（レリックのみ、相応の表示）
- Rest（回復を選んで HP が 30% 増、再訪不可）
- Rest（強化を選んでデッキに `+` 付きカードが 1 枚増える）
- Merchant（カード / レリック / ポーション購入、廃棄、離脱）
- Event（3 件それぞれ踏めるまで繰り返し、各選択肢を選べること）
- 途中でメニュー →「メニューに戻る」→ 再ログインで再開できる（セーブ互換性）

## Out-of-Scope Reminders

| 項目 | 対応フェーズ |
|---|---|
| バトル内レリック効果 | Phase 10 |
| 追加イベントコンテンツ | Phase 8 以降 |
| 図鑑（発見済みレリック/イベントの記録）| Phase 8 |
| HP 全回復 / 層移動 / Run 結果画面 | Phase 7 |
| Event/Merchant のマルチプレイ同期 | Phase 9 |
| ビジュアルデザインの作り込み | Phase 6-8 完了後、Claude Design で一括 |
