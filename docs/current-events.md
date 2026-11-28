# イベント 現行一覧

> 更新日: 2026-11-28
> 定義は `src/Core/Data/Events/<id>.json`（正）。数値は仮で、あとから調整する前提。
> 背景は `art/source/backgrounds/events/<id>.png`（480×320）。画面はイベント ID から自動で背景を選ぶ（CSS の追加は不要）。
> 背景の光の演出は `src/Client/src/components/ambient/ambientScenes.ts` の `event_<id>`。確認は `?dev=scenes`。

## 仕組み（要点）

- 出現: 幕（tiers）が合うイベントから、出やすさ（rarity）の重みで選ぶ。common 3・uncommon 2・rare 1。
- 選択肢の条件: `minGold`（ゴールドが足りる）・`minHp`（HP が足りる。ダメージで倒れないよう、ダメージ量 +1 を条件にする）。
- 効果: `gainGold`・`payGold`・`heal`・`takeDamage`・`gainMaxHp`・`loseMaxHp`・`gainRelicRandom`（rarity 1 コモン／2 レア／3 エピック）・`grantCardReward`（カード報酬を選ぶ）・`gainCard`（指定のカード）。

## 一覧

| ID | 名前 | 幕 | 出やすさ | 選択肢（要約） |
|---|---|---|---|---|
| blessing_fountain | 祝福の泉 | 1・2・3 | common | HP +15 ／ 最大HP +5 ／ 立ち去る |
| forgotten_forge | 忘れられた鍛冶場 | 1・2 | common | 最大HP -4 でカード報酬 ／ ゴールド +80 ／ 立ち去る |
| lost_caravan | 迷子の隊商 | 1・2 | uncommon | ゴールド +45 ／ 6 ダメージでカード報酬 ／ 75G 支払う ／ 立ち去る |
| moonlit_garden | 月夜の庭園 | 1・2・3 | common | HP +20 ／ 8 ダメージで最大HP +6 ／ 立ち去る |
| old_library | 古びた図書館 | 1・2・3 | uncommon | カード報酬 ／ 最大HP -5 でゴールド +50 ／ 立ち去る |
| sealed_vault | 封印された宝物庫 | 2・3 | rare | 15 ダメージでレリック（レア） ／ 120G 支払う ／ 立ち去る |
| shady_merchant | 怪しい商人 | 2・3 | uncommon | 50G 支払う ／ 200G 支払う ／ 立ち去る |
| forest_shrine | 森の祠 | 1 | common | HP +10 ／ 25G 払って最大HP +4 ／ 立ち去る |
| fairy_ring | 妖精の輪 | 1 | uncommon | HP -8 でレリック（コモン） ／ ゴールド +35 ／ 立ち去る |
| woodcutter_hut | 木こりの小屋 | 1・2 | common | HP +18 ／ HP -5 でゴールド +45 ／ カード報酬 |
| underground_lake | 地底湖 | 2 | common | HP +14 ／ HP -10 でゴールド +70 ／ 立ち去る |
| abandoned_mine | 廃坑 | 2 | uncommon | HP -12 でレリック（レア） ／ ゴールド +40 ／ 立ち去る |
| catacombs | 地下墓地 | 2・3 | uncommon | 最大HP -4 でレリック（レア） ／ HP +10 ／ 立ち去る |
| lava_altar | 溶岩の祭壇 | 3 | uncommon | 最大HP -6 でレリック（エピック） ／ HP -15 でゴールド +110 ／ 立ち去る |
| caged_prisoner | 囚われの檻 | 3 | common | HP -10 でカード報酬 ／ HP +8・ゴールド +30 ／ 立ち去る |
| travelers_camp | 旅人の野営地 | 1・2・3 | common | HP +12 ／ カード報酬 ／ ゴールド +30 |
| fortune_teller | 占い師の天幕 | 1・2・3 | rare | 50G 払ってレリック（レア） ／ 最大HP +3 ／ 立ち去る |

2026-11-28 に 10 件追加（forest_shrine 以下）。背景は PixelLab で 480×320 のドット絵として直接生成した
（指示文: `art-work/events/gen-events.mjs`、書き方: [art/backgrounds-prompts.md](art/backgrounds-prompts.md)）。

## 幕ごとの数

| 幕 | イベント数 |
|---|---|
| 1 | 10 |
| 2 | 13 |
| 3 | 10 |
