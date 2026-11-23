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

## コーディング規約

- C# は record を積極利用（不変データ）
- 日本語コメントOK
- Core 内では ASP.NET Core やネットワーク関連の using 禁止
