# Roguelike Card Game

Slay the Spire風のソロ協力両対応ローグライクカードゲーム（PvE）。

## ビルド・実行

```bash
# バックエンド（.NET 10）
dotnet build
dotnet run --project src/Server

# フロントエンド（React + Vite）
cd src/Client && npm run dev

# テスト
dotnet test
```

### テスト実行の注意

- フロントのテスト（`npx vitest run` / `npx playwright test`）は **大文字ドライブのパス（`C:\...`）から**実行する。
  小文字の `c:\...` を作業ディレクトリにすると vitest が二重ロードされ、全ファイルが
  `Cannot read properties of undefined (reading 'config')` で失敗する（コードの問題ではない）。
- 開発サーバーと Server.Tests は `data-local/dev-overrides/`（git 管理外）の上書きデータを読み込む。
  上書きはエントリ全体を置き換えるので、定義に項目を追加したら上書きファイル側も更新する。

## 技術スタック

- **Core**: C# .NET 10 クラスライブラリ（純粋ゲームロジック、将来Udon#移植対象）
- **Server**: ASP.NET Core 10 Web API + SignalR（リアルタイム同期）
- **Client**: React 19 + TypeScript + Vite
- **テスト**: xUnit

## プロジェクト構成

```
roguelike-cardgame/
├── src/
│   ├── Core/              # 純粋ゲームロジック（UI・通信に依存しない）
│   │   ├── Cards/         # カード定義・効果
│   │   ├── Battle/        # バトルシステム（フェーズ進行、ダメージ計算）
│   │   ├── Map/           # マップ・階層進行
│   │   ├── Player/        # プレイヤー状態（HP、デッキ、ゴールド等）
│   │   └── Enemy/         # 敵定義・AI行動
│   ├── Server/            # ASP.NET Core API + SignalR ハブ
│   │   ├── Hubs/          # SignalR ハブ（リアルタイム通信）
│   │   └── Services/      # ルーム管理、ゲームセッション等
│   └── Client/            # React + TypeScript（Vite）
├── tests/
│   ├── Core.Tests/        # ゲームロジックのユニットテスト
│   └── Server.Tests/      # APIテスト
└── docs/                  # 仕様書
```

## 設計方針

- **Core は完全に独立**: Server/Client への依存なし。将来の Udon# 移植を見据え、純粋なC#ロジックのみ
- **Server は薄く**: Core のロジックを呼び出すだけ。ゲームルールは Core に集約
- **テストファースト**: Core のロジックは xUnit でテスト可能な設計

## 進行中の仕様変更（必読）

- **敵AIのカード化・キャラクター能力値システム**（2026-11-23 開始、一部実装）:
  - 実装済みの現行仕様: [docs/current-enemy-card-ai-spec.md](docs/current-enemy-card-ai-spec.md)
  - 全体方針・残り段階・未決事項: [docs/proposed-enemy-card-ai-and-character-stats.md](docs/proposed-enemy-card-ai-and-character-stats.md)
  - 敵34体は全てカード制御（`deck`）。デッキは旧Moveから自動生成した初期値で、手動調整する前提。敵用カードは `Data/Cards/enemy_*.json`（Token）。
  - 敵のカード選択は既存 `CpuCardPlayPolicy` を共通利用し、敵専用AIを新設・複製しない。
  - 敵の行動予測: ターン開始時に敵の行動を一度実際に行い、戦場効果は反映せず `PlannedEffects` に記録して頭上に表示。敵行動時はその記録を「非攻撃 → 攻撃」の順に再現実行する。ドロー等の手札系だけ予約時に即時。プレイヤー側の予約時即時処理は変更しない。
  - 攻撃力・守備力は筋力・敏捷とは別の能力値（敵・味方・プレイヤーが初期値 `attack`/`defense` を持ち、図鑑に載せる）。カードは `amountSource: "selfAttack"/"selfDefense"` で参照し、そこに筋力・敏捷が加わる。敵の予約では実行時の値を使う。
  - 倍率は `amountScalePercent`（例: 強攻撃=150）。初期デッキの「攻撃の基本」「防御の基本」も能力値参照。
  - 敵の計画行動は味方CPUと同じ行動記録としてジャーナルに残り、敵の「行動」ボタンから再生できる（開発確認用）。
  - 行動力（エナジー）に上限は設けない。敵の単体攻撃対象は現行ルール（最小Slot）。
  - 先制行動（`openingActions`）は通常デッキと独立し、最初の手札を配った直後・他の全処理の前に即時解決する。
  - 提案書 §5 の未決事項は、ユーザーの回答なしに仕様化しない。実装した部分は `current-*` 仕様書へ移す。
- **位相（静・動）・双極・カードの後付け能力**（2026-11-26 実装）: [docs/current-phase-spec.md](docs/current-phase-spec.md)。
  位相を踏み倒す効果（動のまま非アタックを使える等）は作らない。
- **キャラクター・カードの現行一覧**: [docs/current-characters-and-class-cards.md](docs/current-characters-and-class-cards.md)（組込データから作成）。
  キーワード能力（乗勢・終撃・再演・伏策など）は [docs/current-class-card-abilities-spec.md](docs/current-class-card-abilities-spec.md)。
- **ストライカーのキャラ構成・カードプール**（2026-11-27 相談開始、未承認）: [docs/proposed-striker-roster.md](docs/proposed-striker-roster.md)。カードは当面設計だけ。
- **エナジー**: ターン開始時は規定値まで回復（足し算ではない、2026-11-27〜）。[docs/current-energy-spec.md](docs/current-energy-spec.md)。
- `docs/current-*.md` が現行仕様の正本。`proposed-*.md` は未承認・未実装の提案。
  初期設計や実装済みの提案は `docs/archive/` に移してある（経緯の記録。現行仕様としては読まない）。

## コーディング規約

- C# は record を積極利用（不変データ）
- 日本語コメントOK
- Core 内では ASP.NET Core やネットワーク関連の using 禁止
