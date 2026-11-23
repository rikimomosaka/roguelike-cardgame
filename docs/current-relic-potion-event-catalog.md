# レリック・ポーション・ランダムイベント追加仕様

この文書は、2026-08-17 時点で追加・補完したコンテンツの初期バランスを記録する。
数値はデータ管理画面から調整可能で、ID と画像ファイル名は一致させる。

## 既存レリックの効果補完

| ID | 発動条件 | 効果 |
|---|---|---|
| `bell_earring` | ターン開始時 | 自身にブロック4 |
| `big_bag` | 常時 | ターン開始時の手札枚数+1 |
| `bone_earring` | 戦闘開始時 | 自身にバリア1 |
| `burning_blood` | 戦闘開始時 | 自身に再生2 |
| `gauntlet` | 戦闘開始時 | 自身に筋力2 |
| `magic_pouch` | 常時 | カード報酬のリロール回数+1 |
| `mana_tarot` | 常時 | ターン開始時のエナジー回復量+1。 |
| `nice_acorn` | 取得時 | 最大HP+4 |
| `nyango_bell` | アタック命中時 | 20%の確率で脆弱1 |
| `ritual_chalice` | 敵撃破時 | HP3回復 |
| `skull_fish` | 戦闘開始時 | 自身に元気1 |
| `skull_mushroom` | 戦闘開始時 | 敵全体に毒2 |

`honeycomb_stone` は説明と実効果を一致させ、戦闘開始時のバリアを2に修正した。

## 新規レリック

| ID | レア度 | 効果 |
|---|---|---|
| `iron_feather` | Common | 戦闘開始時に敏捷1 |
| `ember_hourglass` | Rare | ターン開始時に元気1 |
| `moonlit_mirror` | Rare | 戦闘開始時に反射2・バリア1 |
| `scholar_lens` | Common | カード報酬の選択肢+1 |
| `merchant_scale` | Common | ショップ価格-15% |
| `bloodstone_ring` | Rare | 敵撃破時にHP3回復 |
| `storm_drum` | Epic | 戦闘開始時に追撃2 |
| `golden_sprout` | Common | カードをデッキへ加えた時に5ゴールド獲得 |

## 新規ポーション

| ID | レア度 | 効果 |
|---|---|---|
| `barrier_potion` | Rare | 自身にバリア1 |
| `regeneration_potion` | Common | 自身に再生5 |
| `resistance_potion` | Common | 自身に耐性2 |
| `reflection_potion` | Rare | 自身に反射4 |
| `frenzy_potion` | Rare | 自身に筋力2・元気1 |
| `insight_potion` | Common | 自身に洞察2 |
| `mastery_potion` | Rare | 自身に熟練2 |
| `revival_potion` | Epic | 自身に復活25 |

## 新規ランダムイベント

| ID | 出現階層 | 概要 |
|---|---|---|
| `forgotten_forge` | Act 1–2 | 最大HPを4失ってカード報酬を得る、80ゴールドを回収する、立ち去る |
| `moonlit_garden` | Act 1–3 | HP20回復、HP8を失って最大HP+6、立ち去る |
| `sealed_vault` | Act 2–3 | HP15を失うか120ゴールドを払い、Rareレリックを得る、立ち去る |
| `lost_caravan` | Act 1–2 | 45ゴールド、HP6を失ってカード報酬、75ゴールドでCommonレリック、立ち去る |

選択肢の実行可否は既存の `minHp` / `minGold` 条件で判定する。イベント画像は
`src/Client/public/backgrounds/events/<event-id>.png` に配置する。

## 画像アトラス

新規レリック8種とポーション8種は、1枚の4×4透過PNG
`src/Client/public/icons/atlases/relic_potion_atlas_01.png` として生成した。
各セルを行優先で機械分割し、180×180 RGBAの個別PNGとして各アイコンフォルダーへ配置する。

| 行 | 左から右 |
|---|---|
| 1 | iron feather / ember hourglass / moonlit mirror / scholar lens |
| 2 | merchant scale / bloodstone ring / storm drum / golden sprout |
| 3 | barrier / regeneration / resistance / reflection potion |
| 4 | frenzy / insight / mastery / revival potion |
