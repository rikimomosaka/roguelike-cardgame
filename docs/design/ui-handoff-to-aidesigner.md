# UI 引き継ぎ仕様書 — aidesigner 向け

> **目的:** Phase 2〜8 で作り込んだデバッグ UI を、aidesigner で一括リデザインするための「何を守り、何を任せるか」を明文化する。

**対象:** `src/Client/src/screens/**` の各画面 UI（バックエンド / ゲームロジック / データモデルは変更対象外）。

**現状:**
- 全画面の HTML モックアップが [`.superpowers/brainstorm/14705-1776939312/content/`](../../.superpowers/brainstorm/14705-1776939312/content/) に揃っている。
- モックアップは「**情報設計・インタラクション設計が確定**、見た目は今後刷新」という段階。
- aidesigner にはこれらモックアップを参照 → 統一された最終デザインを出力してもらう。

---

## 1. 画面一覧（canonical モックアップ）

| # | 画面 | Canonical ファイル | 対応 Phase | プレビュー URL |
|---|------|-------------------|-----------|---------------|
| 1 | ログイン | `login-v4.html` | P2 | http://localhost:56791/files/login-v4.html |
| 2 | メインメニュー | `main-menu-v3.html` | P2 | http://localhost:56791/files/main-menu-v3.html |
| 3 | 設定 | `settings-v2.html` | P2 | http://localhost:56791/files/settings-v2.html |
| 4 | 実績（カード図鑑） | `archives-cards-v12.html` **★canonical card visual** | P8 | http://localhost:56791/files/archives-cards-v12.html |
| 5 | 実績（レリック図鑑） | `archives-relics-v1.html` | P8 | http://localhost:56791/files/archives-relics-v1.html |
| 6 | 実績（ポーション図鑑） | `archives-potions-v1.html` | P8 | http://localhost:56791/files/archives-potions-v1.html |
| 7 | 実績（モンスター図鑑） | `archives-enemies-v2.html` | P8 | http://localhost:56791/files/archives-enemies-v2.html |
| 8 | 実績（プレイ履歴） | `archives-history-v5.html` | P8 | http://localhost:56791/files/archives-history-v5.html |
| 9 | マップ画面 | `map-screen-v4.html` | P3/P4 | http://localhost:56791/files/map-screen-v4.html |
| 10 | バトル画面 | `battle-v10.html` | P5 | http://localhost:56791/files/battle-v10.html |
| 11 | 報酬ポップアップ | `reward-v3.html` | P5 | http://localhost:56791/files/reward-v3.html |
| 12 | 商人ポップアップ | `merchant-v1.html` | P6 | http://localhost:56791/files/merchant-v1.html |
| 13 | イベント選択肢 | `event-v1.html` | P6 | http://localhost:56791/files/event-v1.html |
| 14 | 焚き火（休憩所） | `rest-v1.html` | P6 | http://localhost:56791/files/rest-v1.html |
| 15 | ACT 開始レリック選択 | `act-start-relic-v1.html` | P7 | http://localhost:56791/files/act-start-relic-v1.html |
| 16 | インゲームメニュー（ESC） | `ingame-menu-v1.html` | — | http://localhost:56791/files/ingame-menu-v1.html |
| 17 | ラン結果画面 | `run-result-v8.html` | P7 | http://localhost:56791/files/run-result-v8.html |

**参考**: `mood.html` / `pixel-font.html` — トーン＆タイポの素材集。

旧バージョン（v1〜v11 等）は **無視** してよい。canonical のみ参照すること。

---

## 2. 共通デザイン言語

### 2.1 コア原則

- **ダーク＋ゴールド**: ベース `#0a0604`（ほぼ黒）、テキスト主体 `#e6c18a`〜`#c9985a`、アクセント `#ffd54f`（金）。
- **ドット / ピクセル感**: ベクタ的な滑らかさではなく、文字はドット調フォント、装飾はピクセル的グラデ＋スキャンライン。
- **羊皮紙 / 古文書メタファー**: 枠線は `#5a3a1a`〜`#8a6a3a`（焦げ茶）、背景グラデは茶系。

### 2.2 フォント

| 用途 | フォント | サイズ目安 | 備考 |
|------|---------|-----------|------|
| **日本語・長文テキスト** | `'DotGothic16'` | 12〜16px | ドット系和文、本文・タイトル両方 |
| **英数字・ラベル・ラベルキャップス** | `'Pixelify Sans'` | 10〜14px | 数値・ショートラベル・ACT 表記・SE/BGM 等 |

**禁止**: Cinzel 等セリフ系は廃止済み。すべてドット系で統一する。

### 2.3 レアリティ色（全画面共通）

| レアリティ | 主色 | 枠線 | 背景 rgba |
|-----------|-----|------|-----------|
| COMMON (c) | `#eaeaea`（白銀） | `#8a6a3a`（共通茶） or `#8a8a8a` | `rgba(234,234,234,0.08)` |
| RARE (r) | `#9ecbff`（淡青） | `#5a7a9a` | `rgba(158,203,255,0.08)` |
| EPIC (e) | `#e4a8ff`（淡紫） | `#8a5a9a` | `rgba(228,168,255,0.08)` |
| LEGENDARY (l) | `#ffe58a`（黄金） | `#a88a2a` | `rgba(255,229,138,0.1)` |

**これは絶対に維持**。カード・レリック・ポーション・モンスター難度など、レアリティを持つ全要素で共通使用する。

### 2.4 状態色

| 状態 | 色 | 用途 |
|------|-----|------|
| HP / 獲得系 | `#6adf89`（緑） | 回復、プラス効果、✓ |
| HP 減 / 危険 / danger | `#ff9a8a`〜`#d96153`（赤系） | ダメージ、破壊、放棄、リスクタグ |
| ゴールド | `#ffd54f`（金） | ゴールド増加、価格、コスト |
| ブロック / 情報 | `#9ecbff`（青） | ブロック、セーブ、中立アイコン |
| 除去 / 清浄 | `#9ad0d0`（青緑） | カード除去、浄化 |
| 呪い / レリック枠 | `#a068c4`〜`#e4a8ff`（紫） | 呪いカード、レリック枠 |

### 2.5 レイアウト

- **ゲーム画面は 16:9**: `aspect-ratio:16/9;max-width:1200px;margin:0 auto` の「仮想スクリーン」内で完結させる。
- モックアップではこの仮想スクリーンの外に説明用の H2 / subtitle / チェックポイントを書いているが、**実装時はこの外枠チェック用テキストは不要**。
- ポップアップは仮想スクリーン内に重ねる（物理的な OS ウィンドウではない）。

---

## 3. 再利用コンポーネント（横断）

aidesigner には「これは 1 つのコンポーネントに統一してよい」として扱ってほしいもの。

### 3.1 カード絵柄 ★最重要

- **canonical = `archives-cards-v12.html` のカード見た目**
- 要素: 左上のコスト円 / 名前（上帯） / イラスト枠（正方形・スキャンライン） / 下に TYPE ラベル
- タイプ別グラデ（背景）:
  - `type--attack`: 赤 `#6a2a1a → #3a1a10`
  - `type--skill`: 緑 `#2a4a3a → #10201a`
  - `type--power`: 紫 `#4a2a5a → #1a1025`
- レアリティは名前色・コスト枠色・イラスト枠色に反映。
- **使用箇所**: 図鑑、バトル手札、報酬ピッカー、商人、焚き火（強化/除去ピッカー）、ランリザルト。
- **サイズ違い**: 実寸 104〜180px 幅で縮小 / 拡大する。基本レイアウト比率は保つ。

### 3.2 ツールチップ

- `position:fixed;z-index:10000`（**モーダルよりも上**。Battle v10 で修正済み）
- 構造: ヘッダー（名前 ＋ レアリティタグ） / 本文（説明）
- カーソル追従 + 画面端で自動反転

### 3.3 ポップアップモーダルパターン

現在の 6 画面（reward / merchant / event / rest / act-start-relic / ingame-menu）は共通:

```
.xx-stage (position:relative, aspect-ratio:16/9)
  ├ <iframe> (背景：凍結した元画面、pointer-events:none)
  ├ .xx-dim (inset:0, z:10, radial-gradient + blur)
  └ .xx-popup (z:20, centered, 620〜820px width)
       ├ __head (タイトル＋必要なら情報バー)
       ├ __body (overflow-y:auto)
       └ __foot (閉じる / 確定 等のボタン)
```

- **ピッカーサブポップアップ** (reward のカード選択、merchant の除去、rest の強化/除去) は z:30 で追加。
- **確認ダイアログ** (ingame-menu の放棄確認) は z:40 で最上位。

### 3.4 タイル行コンポーネント（リスト項目）

レリック / ポーション / サービス / イベント選択肢など、「アイコン＋名前＋説明＋右 CTA/価格」の横並び行。

- 左: 38×38px のアイコン枠
- 中: 名前（15px）＋ 説明（12px の灰色）
- 右: CTA or 価格 or タグ
- hover: `translateX(3px)` + `border-color:#c9985a` + 淡い発光

### 3.5 ボタン

- **主要 CTA**（確定、先へ進む、立ち去る等）: 金枠 `#c9985a` / 金文字 `#e8c87a` → hover で `#ffd54f` + グロー
- **副次的**（キャンセル、CLOSE、SKIP）: 茶色地 / 灰文字 `#8a6a3a` → hover で赤寄りに
- **危険操作**（放棄、破壊）: 赤枠 `#7a2a1a` / 赤文字 `#ff9a8a`

### 3.6 スライダー / トグル（設定系）

- スライダー: 1 本線（高さ 4px）、thumb は 10×16px の金色矩形
- トグル: 18×18px のドット風チェックボックス、チェック時は緑 ✓

---

## 4. インタラクションパターン

| パターン | 使用箇所 | 備考 |
|---------|----------|------|
| 上バーレリックのドラッグスクロール | バトル画面 | `mousedown`+`mousemove` で横スクロール、bar 全体は overflow 隠す |
| モーダル内スクロール | reward / merchant / event / rest の body | `overflow-y:auto`、細いスクロールバー |
| 選択ハイライト | act-start-relic のレリック 3 択 | 金枠 + 発光 + 上部 ◆ マーカー |
| 確認ダイアログ | ランを放棄 | **必須**、キャンセル優先配置 |
| ESC 階層クローズ | ingame-menu | 確認 → 設定 → メニューの順に 1 層ずつ閉じる |
| SOLD / CLAIMED スタンプ | merchant / reward | 斜め赤スタンプ or ✓ グレーアウト |

---

## 5. 画面ごとの「守ってほしい情報構造」

aidesigner には見た目の刷新を任せるが、**情報の要素と優先順位**は保ってほしい。

### 5.1 バトル画面 (`battle-v10.html`)

- 上バー: レリック帯（ドラッグスクロール） / MAP / MENU
- 左: HP / ブロック / パワー / ステータス効果
- 中央: 敵スロット（最大 5） / 意図アイコン
- 右下: 山札 / 捨札 / 除外の 3 パイル（クリックで一覧）
- 下: 手札 / エナジー（左下 64×64 円、数字のみ） / ターン終了
- **維持必須**: エナジー円の中は数字のみ（ラベル不要）、パイルラベルは DotGothic16。

### 5.2 報酬 (`reward-v3.html`)

- 簡素なヘッダー「報酬」のみ（敵名・階層は表示しない）
- タイル縦並び: ゴールド / ポーション / レリック / カード
- CTA は日本語（入手 / 選ぶ / 閉じる / 放棄）
- カード選択はサブピッカーで中央 3 枚

### 5.3 商人 (`merchant-v1.html`)

- ヘッダー右側に所持ゴールド
- セクション: CARDS（5 枚） / RELICS / POTIONS / SERVICE（除去）
- 所持金不足は価格赤＋ `is-locked`
- 除去はサブピッカーで現在のデッキから選ぶ

### 5.4 焚き火 (`rest-v1.html`)

- 基本 2 択: **休息**（HP +30%）/ **鍛える**（カード強化）
- 条件付き選択肢（**清める** = カード除去など）は**条件を満たさない限り非表示**とする
- カード選択はモード別色分け（強化=金、除去=青緑）で色と「+」バッジ挙動が違う

### 5.5 ACT 開始レリック (`act-start-relic-v1.html`)

- ACT バッジ + タイトル + サブ「1 つを選べ」
- レリック 3 枚横並び、各カード: アイコン 96px / 名前 / レアリティタグ / 説明 / フレーバー
- **放棄不可**: SKIP ボタンは出さない
- 選択 → 確定で閉鎖

### 5.6 インゲームメニュー (`ingame-menu-v1.html`)

- 4 項目固定: 続ける / 設定 / 保存して終了 / ランを放棄
- 各項目にホットキー（ESC/S/Q/X）表示
- 放棄のみ赤・確認ダイアログ必須

### 5.7 実績（図鑑）5 タブ

- タブ順: カード / レリック / ポーション / モンスター / 履歴
- 発見済みは詳細表示、未発見は `???` マスキング（アイコンは影のみ）
- カードタブはカード絵柄のグリッド、レリック/ポーションはタイル、モンスターはスプライト＋難度帯。
- 履歴タブは 1 ランごとに「結果 / Act / 所要時間 / デッキ 4 セット展開可能」の行アイテム。

---

## 6. aidesigner への要望

### 6.1 守ってほしいもの（制約）

1. **ダーク羊皮紙＋金 のトーン**と**ドット/ピクセルの質感**。
2. **DotGothic16 + Pixelify Sans** のタイポ（他は導入しない）。
3. **レアリティ 4 色**（上記 2.3）の意味割り当て。
4. **カード絵柄の構造**（コスト円 / 名前 / 正方形イラスト枠 / TYPE ラベル）。
5. **ポップアップ階層**（iframe 背景 → dim → popup → picker → confirm の z-index 構造）。
6. **情報要素と優先順位**（§5 の各画面）。
7. **16:9 仮想スクリーン**（1200px max-width、内側完結）。
8. **すべての CTA は日本語**（CLAIM→入手 等、reward v3 で確定した訳語を継承）。

### 6.2 任せる（自由に再デザインしてよい）

- 余白 / パディング / 角丸 / 境界線の微調整。
- ヘッダー装飾、アニメーション追加（ただしドット感は維持）。
- アイコンの差し替え（`◈ ✦ ◉ ◆ ⚗ ✂` 等はあくまで placeholder）。
- 効果音を示唆するエフェクト。
- 複数画面で共通化できる要素のコンポーネント化（CSS 変数 / 共通クラスの切り出し）。

### 6.3 してほしくないもの

- 明るい背景 / 白基調 / 彩度の高いパステルカラー。
- セリフ体・手書き体フォントの追加。
- 情報密度の大幅削減（現状の情報はゲームロジックが要求する）。
- 選択肢を「モーダルではなく画面遷移」に変えること（報酬は popup 維持、§5.2）。
- 商人の「カード 5 枚」のような数量変更（ロジック側に合わせる）。

---

## 7. 技術制約

- **ターゲット**: React 19 + TypeScript + Vite。CSS Modules / styled-components / Tailwind のいずれでも可。
- **画像リソース**: 現時点では全て CSS / Unicode 記号 / グラデで描画している。aidesigner が生成する画像リソースがある場合はピクセルアート寄り（pixel rendering: `image-rendering:pixelated` 推奨）で。
- **アクセシビリティ**: 最低限のキーボード操作とフォーカスリングは維持（ESC で閉じる、Tab 遷移）。
- **i18n**: 現時点は日本語固定。将来的に英語化するかは未定。

---

## 8. 成果物（aidesigner に期待するアウトプット）

1. **統一されたデザイントークン**（色 / タイポ / 余白 / 影 / アニメーションの変数定義）。
2. **共通コンポーネント**（Card / Tile / Popup / Tooltip / Button / Slider / Toggle）の React 実装または CSS モジュール。
3. **各画面の最終デザイン**: §1 の 17 画面分。
4. **差分 HTML or Figma のリファレンス**（任意、レビューしやすい形）。

---

## 9. 参考

- ゲーム全体仕様: [`docs/game_design_spec.md`](../game_design_spec.md)
- 実装計画: [`docs/superpowers/plans/2026-04-20-roadmap.md`](../superpowers/plans/2026-04-20-roadmap.md)
- モックアップ格納先: [`.superpowers/brainstorm/14705-1776939312/content/`](../../.superpowers/brainstorm/14705-1776939312/content/)
- プレビューサーバ: `http://localhost:56791/files/<name>.html`
