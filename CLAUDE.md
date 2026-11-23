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

- **敵AIのカード化・キャラクター能力値システム**（2026-11-23 提案、未実装）:
  [docs/proposed-enemy-card-ai-and-character-stats.md](docs/proposed-enemy-card-ai-and-character-stats.md)
  - 敵の固定Move方式を、CPU味方と共通のカード使用・行動予約方式へ移行する方針。
  - 敵のカード選択は既存 `CpuCardPlayPolicy` / `BattleCpuPlanningCoordinator` を共通利用し、敵専用AIを新設・複製しない。
  - 敵の通常戦闘中のバフ・デバフだけ予約時に反映せず、実行時に反映する。プレイヤー側の予約時即時処理は変更しない。
  - 能力値参照効果は実行時の有効値を参照する（既存 `AmountSource` は使用時固定なので流用不可）。
  - 先制行動は通常デッキと独立したプールで、登場時に即時解決する。
  - 同文書 §5 の未決事項は、ユーザーの回答なしに仕様化しない。実装した部分は `current-*` 仕様書へ移す。
- `docs/current-*.md` が現行仕様の正本。`proposed-*.md` は未承認・未実装の提案。

## コーディング規約

- C# は record を積極利用（不変データ）
- 日本語コメントOK
- Core 内では ASP.NET Core やネットワーク関連の using 禁止
