# Phase 2 — メニュー／ログイン／設定 設計書

**日付:** 2026-04-20
**スコープ:** ゲーム起動からメインメニューまでの UI、音量設定、ログイン／新規作成、最新ラン参照。
**前提タグ:** `phase1-complete`。

---

## 1. スコープとゴール

**含む**
- ログイン画面（「新規作成」「既存 ID で続行」の 2 タブ）
- メインメニュー画面（シングル／マルチ／設定／実績／終了の 5 ボタン）
- 設定画面（Master / BGM / SE / Ambient の 4 スライダ、0–100）
- 音量設定のサーバ永続化（アカウント別）
- 最新ラン情報の参照 API（メインメニュー「続きから」表示判定用）
- Client↔Server を通すための Vite dev proxy と最小 CORS 設定
- Phase 1 の `SaveRepository` をインターフェース化・非同期化する手戻りリファクタ

**含まない（後続フェーズ）**
- マルチプレイ同期（Phase 9）
- 実績画面の中身（Phase 8。ボタンは置くがクリックで「準備中」ダイアログ）
- マップ・バトル（Phase 3 以降）
- パスワード／OAuth 認証（将来差し替え前提のスタブのみ）
- 実際の音声再生（設定値を保存するだけ）

**運用上の最終到達点**
- ブラウザ版 → Railway + Postgres で公開
- 将来 VRChat ワールドへ移植（Udon#）

---

## 2. ドメインモデル（`src/Core/`）

### 2.1 `AudioSettings`

```csharp
namespace RoguelikeCardGame.Core.Settings;

/// <summary>
/// プレイヤーごとの音量設定。値は 0–100（クランプ済）。
/// </summary>
/// <remarks>
/// VRChat 移植時は record → sealed class、各フィールドは UdonSharp で
/// PlayerData の個別 key（例 "audio.master"）に分解する想定。
/// </remarks>
public sealed record AudioSettings(
    int SchemaVersion,
    int Master,
    int Bgm,
    int Se,
    int Ambient)
{
    public const int CurrentSchemaVersion = 1;

    public static AudioSettings Default =>
        new(CurrentSchemaVersion, Master: 80, Bgm: 70, Se: 80, Ambient: 60);

    /// <summary>0–100 の範囲外を拒否する検証付きファクトリ。</summary>
    public static AudioSettings Create(int master, int bgm, int se, int ambient);
}
```

**検証ルール** (`Create`)
- 各値が `0 <= v <= 100` を外れたら `ArgumentOutOfRangeException`。
- Ctor は「そのまま保存」用途なのでクランプも検証もしない（Deserialize 後の整合は Serializer の責務）。

### 2.2 `AudioSettingsSerializer`

```csharp
public sealed class AudioSettingsSerializerException : Exception { /* 標準 2 ctor */ }

public static class AudioSettingsSerializer
{
    public static string Serialize(AudioSettings settings);
    public static AudioSettings Deserialize(string json);
}
```

- `Phase 1 RunStateSerializer` と同じ `JsonSerializerOptions` を共有するため、`src/Core/Json/JsonOptions.cs` に共通プロパティ `Default` を切り出す：
  - `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
  - `WriteIndented = false`
  - `JsonStringEnumConverter`
  - `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`
- `Deserialize` の手順：
  1. `JsonSerializer.Deserialize<AudioSettings>(json, JsonOptions.Default)` を実行（失敗で `AudioSettingsSerializerException`）。
  2. `null` なら `AudioSettingsSerializerException`。
  3. `schemaVersion != CurrentSchemaVersion` で `AudioSettingsSerializerException`。
  4. `AudioSettings.Create(d.Master, d.Bgm, d.Se, d.Ambient)` を呼び直して範囲検証を走らせ、その結果（`SchemaVersion` は揃える）を返す。値が 0–100 外なら `AudioSettings.Create` が `ArgumentOutOfRangeException` を投げるので、`Deserialize` 内で捕捉し `AudioSettingsSerializerException` にラップ。

### 2.3 既存 `RunStateSerializer` の共通化

- `src/Core/Run/RunStateSerializer.cs` の `private static readonly JsonSerializerOptions Options` を削除し、`JsonOptions.Default` を参照するように書き換え。挙動不変、テスト全件維持。

### 2.4 Core は Udon 移植を意識した書きぶりを維持

- 新規追加物に `async/await` を入れない。
- LINQ の深いチェーンを避ける。
- 新規のジェネリクスを最小限に。
- `string[]` ベースで動くパス（公開 API は `IReadOnlyList<string>` で OK、実装は配列コピー）。
- 新規 Core ファイルの先頭 XML doc に VR 移植ノート（recordの扱い／例外の扱い）を添える。

---

## 3. リポジトリ抽象化（Server 専用）

### 3.1 抽象化の位置

- インターフェースは **Server 層のみ** に置く（`src/Server/Abstractions/`）。Core には入れない。
- Udon# はインターフェースを扱えないため、これらは VR に移植しない。VR 版は `UdonSharpBehaviour` が直接 `PlayerData` を叩く別実装になる。
- 各インターフェースの XML doc に `<remarks>Server 専用。VR 移植時は UdonSharp の PlayerData API に置き換える。</remarks>` を付記。

### 3.2 インターフェース

```csharp
namespace RoguelikeCardGame.Server.Abstractions;

public sealed record Account(string Id, DateTimeOffset CreatedAt);

public interface IAccountRepository
{
    Task<bool> ExistsAsync(string accountId, CancellationToken ct);
    Task CreateAsync(string accountId, DateTimeOffset nowUtc, CancellationToken ct);
    Task<Account?> GetAsync(string accountId, CancellationToken ct);
}

public interface IAudioSettingsRepository
{
    Task<AudioSettings> GetOrDefaultAsync(string accountId, CancellationToken ct);
    Task UpsertAsync(string accountId, AudioSettings settings, CancellationToken ct);
}

public interface ISaveRepository
{
    Task SaveAsync(string accountId, RunState state, CancellationToken ct);
    Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct);
    Task DeleteAsync(string accountId, CancellationToken ct);
}

public sealed class AccountAlreadyExistsException : Exception
{
    public AccountAlreadyExistsException(string accountId)
        : base($"アカウント ID はすでに存在します: {accountId}") { }
}
```

### 3.3 例外契約

| 条件 | 例外 |
|---|---|
| アカウント ID が空白 / 制御文字 / `/` / `\` / `Path.GetInvalidFileNameChars` を含む | `ArgumentException` |
| `CreateAsync` で ID が既存 | `AccountAlreadyExistsException` |
| `GetAsync` で ID が未登録 | 例外ではなく `null` を返す |
| `UpsertAsync` の `settings` 値が 0–100 外 | `ArgumentOutOfRangeException`（`AudioSettings.Create` が投げる） |

### 3.4 ファイル実装

`src/Server/Services/FileBacked/` に配置：

| 実装 | パス |
|---|---|
| `FileAccountRepository` | `{dataDir}/accounts/{accountId}.json` |
| `FileAudioSettingsRepository` | `{dataDir}/audio_settings/{accountId}.json` |
| `FileSaveRepository` | `{dataDir}/saves/{accountId}.json` |

**共通ユーティリティ** `src/Server/Services/AccountIdValidator.cs`：
- 静的メソッド `Validate(string accountId)` が null / 空白 / `/` / `\` / `Path.GetInvalidFileNameChars` を拒否し `ArgumentException` を投げる。
- 3 実装はすべてこの検証を通す。

**書き込み手順**（全ファイル共通）：
1. `{path}.tmp` に UTF-8 (BOM なし) で JSON を書く。
2. `File.Move(tmp, final, overwrite: true)` でアトミック差し替え。

**制限事項**（受容）：
- `CreateAsync` の「存在チェック → 作成」はアトミックではない（ファイル代理 DB の制約）。Postgres 移行時は `UNIQUE` 制約で強化。
- ファイル排他ロック無し。単一プロセス想定（開発マシン／Railway 1 インスタンス）。並列書き込みが来ても `File.Move` の原子性でデータ破損は防げる。

### 3.5 Phase 1 手戻り

1. `src/Server/Services/SaveRepository.cs` → `src/Server/Services/FileBacked/FileSaveRepository.cs` に改名・移動。
2. `ISaveRepository` 実装、`async Task` シグネチャ化。`File.WriteAllTextAsync` / `File.ReadAllTextAsync` 使用。
3. パスを `{root}/{id}.json` → `{root}/saves/{id}.json` に変更。
4. `tests/Server.Tests/SaveRepositoryTests.cs` を `async` 書き換え、新パス対応。既存 11 ケース全維持。

### 3.6 DI 登録

`src/Server/Program.cs` に：

```csharp
builder.Services.Configure<DataStorageOptions>(
    builder.Configuration.GetSection("DataStorage"));

builder.Services.AddSingleton<IAccountRepository, FileAccountRepository>();
builder.Services.AddSingleton<IAudioSettingsRepository, FileAudioSettingsRepository>();
builder.Services.AddSingleton<ISaveRepository, FileSaveRepository>();
```

`appsettings.json`：

```json
{
  "DataStorage": {
    "RootDirectory": "./data-local"
  }
}
```

`DataStorageOptions` は `RootDirectory` 1 プロパティの POCO。

---

## 4. HTTP API（Server）

### 4.1 共通方針

- ベース URL: `/api/v1/...`
- レスポンス形式: JSON、camelCase、`JsonStringEnumConverter` 有効。
- エラー: RFC 7807 Problem Details（ASP.NET Core 標準 `ProblemDetails`）。
- 認証ヘッダ: `X-Account-Id: <id>`（Phase 2 は単なる識別、将来の認証層差し替え前提）。
- CORS:
  - 設定は `appsettings.json` の `Cors:AllowedOrigins`（string 配列）で制御。ASP.NET Core の `IConfiguration` バインディング経由なので環境変数（`Cors__AllowedOrigins__0` 等）でも上書き可能。
  - 開発既定: `http://localhost:5173`（Vite dev server）を許可。
  - 本番: Railway ドメインのみ（デプロイ時に `appsettings.Production.json` or 環境変数で上書き）。
- `Program.cs` の `WeatherForecast` サンプルは Phase 2 最初のタスクで削除。
- 末尾に `public partial class Program {}` を追加し `WebApplicationFactory<Program>` から参照可能にする。

### 4.2 エンドポイント

| Method | Path | 要求 | 成功 | 失敗 |
|---|---|---|---|---|
| `POST` | `/api/v1/accounts` | body `{accountId: string}` | `201 Created`、body `{id, createdAt}` | `400` 不正 ID / `409` 既存 |
| `GET` | `/api/v1/accounts/{id}` | — | `200`、body `{id, createdAt}` | `400` 不正 ID / `404` 未登録 |
| `GET` | `/api/v1/audio-settings` | header `X-Account-Id` | `200`、body `AudioSettings` JSON | `400` / `404` アカウント不在 |
| `PUT` | `/api/v1/audio-settings` | header + body `AudioSettings` | `204 No Content` | `400` 範囲外 or 不正 ID / `404` |
| `GET` | `/api/v1/runs/latest` | header `X-Account-Id` | `200`、body `RunState` JSON | `204` ラン無し / `400` / `404` アカウント不在 |

### 4.3 Controller 配置

`src/Server/Controllers/`：

- `AccountsController`
- `AudioSettingsController`
- `RunsController`

各コントローラは対応する Repository インターフェースを DI で受け取る薄いラッパに保つ。ビジネスロジックは置かない。

### 4.4 リクエストバリデーション

- `X-Account-Id` ヘッダ必須のエンドポイントは、ヘッダ欠落で `400`。
- Body バリデーションは `System.ComponentModel.DataAnnotations` は使わず、Controller 内で `AudioSettings.Create` を呼んで範囲チェックに任せる（DRY）。

---

## 5. Client アーキテクチャ

### 5.1 画面遷移

React Router は導入しない。`App.tsx` に Discriminated Union：

```ts
type Screen = { kind: 'login' } | { kind: 'main-menu' } | { kind: 'settings' };
```

### 5.2 ディレクトリ構成（`src/Client/src/`）

```
api/
  client.ts            ── fetch ラッパ、X-Account-Id 自動付与、ApiError 型
  accounts.ts
  audioSettings.ts
  runs.ts
  types.ts             ── DTO 型を手書き
context/
  AccountContext.tsx
hooks/
  useAudioSettings.ts
screens/
  LoginScreen.tsx
  MainMenuScreen.tsx
  SettingsScreen.tsx
components/
  Button.tsx
  Slider.tsx
App.tsx, main.tsx, index.css
```

### 5.3 セッション管理

- `AccountContext` が `accountId: string | null` と `login(id)` / `logout()` を提供。
- `login` 成功時に `localStorage['rcg.accountId']` に書き込み。
- アプリ起動時（`App.tsx` の `useEffect`）に `localStorage` から読み戻し → `GET /api/v1/accounts/{id}` で存在確認：
  - 存在 → `main-menu`
  - 未登録（`404`）→ `localStorage` を削除して `login`
  - ネットワークエラー → エラー画面表示、リトライボタン。

### 5.4 ログイン画面

- 2 タブ: 「新規作成」「既存 ID で続行」
- 共通入力: アカウント ID（テキスト）
- 新規: `POST /api/v1/accounts` → `201` で `login(id)` と画面遷移、`409` なら「その ID はすでに使われています」。
- 既存: `GET /api/v1/accounts/{id}` → `200` で `login(id)` と画面遷移、`404` なら「その ID は登録されていません」。
- 両タブ共通のフロントエンド ID 検証: 空白禁止、`/` `\` 禁止、32 文字以内。サーバ側検証と一致。

### 5.5 メインメニュー

5 ボタン：
- 「シングルプレイ」: Phase 2 では「続きから」「新しいラン」の 2 次画面に遷移するモックのみ（押しても「準備中」ダイアログ）。
- 「マルチプレイ」: 「準備中」ダイアログ。
- 「設定」: 設定画面へ。
- 「実績」: 「準備中」ダイアログ。
- 「終了」: 確認モーダル → 「このタブを閉じてください」テキスト表示（`window.close()` はサイレント失敗するため）。

`useEffect` で `GET /api/v1/runs/latest` を呼んで結果を `hasRun: boolean` 状態に保持（「続きから」は Phase 3+ で使う土台）。

ヘッダー右上に現在の `accountId` と「ログアウト」ボタン。

### 5.6 設定画面

- 4 つの `Slider`（0–100、`<input type="range">`）。
- 初期値: 起動時に `GET /api/v1/audio-settings` で取得、`useAudioSettings` フックが供給。
- 変更時: 楽観更新（UI 即時反映）→ 500ms デバウンス → `PUT /api/v1/audio-settings`。
- 保存中インジケータ（右下に「保存中…」テキスト、成功後「保存済み ✓」に 2 秒表示）。

### 5.7 `useAudioSettings` フック

```ts
function useAudioSettings(accountId: string): {
  settings: AudioSettings | null;
  update: (patch: Partial<AudioSettings>) => void;
  saveStatus: 'idle' | 'saving' | 'saved' | 'error';
}
```

- 内部で 500ms デバウンス（`setTimeout` ベース、`useRef` で timer id 保持）。
- 連続変更は最後の値 1 回だけ PUT。

### 5.8 API クライアント

```ts
// api/client.ts
export class ApiError extends Error {
  constructor(public status: number, public body: string) { super(`HTTP ${status}`); }
}

export async function apiRequest<T>(
  method: string,
  path: string,
  opts?: { accountId?: string; body?: unknown }
): Promise<T> {
  const res = await fetch(`/api/v1${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(opts?.accountId ? { 'X-Account-Id': opts.accountId } : {}),
    },
    body: opts?.body !== undefined ? JSON.stringify(opts.body) : undefined,
  });
  if (!res.ok) throw new ApiError(res.status, await res.text());
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}
```

### 5.9 Dev サーバ / Vite proxy

- Phase 2 の最初の Server 側タスクで `src/Server/Properties/launchSettings.json` の `applicationUrl` を `http://localhost:5114` に固定する（HTTPS プロファイルは dev では使わず、代わりにこの HTTP URL 1 本に統一）。
- `src/Client/vite.config.ts` に同じポートを指すプロキシを設定：

```ts
server: {
  proxy: {
    '/api': {
      target: 'http://localhost:5114',
      changeOrigin: true,
    },
  },
},
```

- Client は `/api/v1/...` を直接叩けば dev proxy が Server へ転送。CORS 設定は本番のみ必要。
- 本番ビルド時は `VITE_API_BASE_URL` 環境変数で本番 Server 直接指定（Phase 2 では開発のみサポート、本番デプロイはロードマップ範囲外）。

### 5.10 スタイリング

- プレーン CSS ファイル、画面ごと 1 ファイル。
- CSS フレームワーク（Tailwind 等）は導入しない。
- ダークテーマ前提の配色（Slay the Spire 系が暗め UI のため違和感が少ない）。具体カラーは実装時に決定。

### 5.11 状態管理ライブラリ

- Zustand / Redux / Jotai は導入しない。`useState` + `useContext` + `useReducer` のみ。

### 5.12 VR 移植時のアンカー

- `onExit: () => void` のように副作用をハンドラ差し替え可能にしておく（VR 版は VRChat SDK で扱う）。
- API クライアント (`apiRequest`) は「ストレージアクセス関数」として抽象化可能な形に保つ。VR 移植時は `vrcPlayerDataRequest` に丸ごと差し替え。

---

## 6. テスト戦略

### 6.1 Core.Tests（xUnit、既存プロジェクト）

| テストクラス | 内容 |
|---|---|
| `AudioSettingsTests` | `AudioSettings.Default` の値、`AudioSettings.Create` が 0–100 外で例外 |
| `AudioSettingsSerializerTests` | 往復、schemaVersion ミスマッチで例外、未知フィールドで例外、値範囲外の JSON で例外 |
| `JsonOptionsTests`（新規） | `JsonOptions.Default` が期待する設定（camelCase、string enum、unmapped disallow）を持つことを固定 |

### 6.2 Server.Tests（xUnit、既存プロジェクト）

| テストクラス | 内容 |
|---|---|
| `FileAccountRepositoryTests` | Create→Exists、二重 Create で例外、不正 ID で `ArgumentException`、`GetAsync` 未登録で null |
| `FileAudioSettingsRepositoryTests` | 未保存で `Default` 返却、Upsert→Get 往復、別アカウント独立、範囲外値の Upsert で例外 |
| `FileSaveRepositoryTests` | 既存 11 ケースを非同期・新パス対応で維持、`.tmp` 残存無しを追加検証 |
| `AccountsControllerTests` | `WebApplicationFactory<Program>` 経由で POST 201/409/400、GET 200/404/400 |
| `AudioSettingsControllerTests` | GET 200/404、PUT 204、PUT 400（範囲外）、ヘッダ欠落 400 |
| `RunsControllerTests` | GET 200（保存済み）、204（ラン無し）、404（アカウント無し） |
| `AccountIdValidatorTests` | 境界ケース集（空白、制御文字、`/`、`\`、長すぎる ID 等） |

### 6.3 Client.Tests（新規 Vitest）

**依存追加** (`package.json` devDependencies):

- `vitest`
- `@testing-library/react`
- `@testing-library/jest-dom`
- `jsdom`

**テストクラス**:

| ファイル | 内容 |
|---|---|
| `api/client.test.ts` | `X-Account-Id` 自動付与、`ApiError` の `status` 保持、`204` 時に `undefined`、body 自動 stringify |
| `hooks/useAudioSettings.test.ts` | 初回 GET、変更後 500ms で PUT、連続変更が 1 回にデバウンス（fake timers）、エラー時 `saveStatus = 'error'` |
| `context/AccountContext.test.tsx` | `login` で `localStorage` に書く、`logout` で削除、存在しない ID での再ログイン試行時の挙動 |

### 6.4 手動動作確認

1. `dotnet run --project src/Server` + `cd src/Client && npm run dev` で起動。
2. ログイン画面 → 新規作成タブで任意 ID → メインメニュー遷移。
3. 同じ ID で再度新規作成 → `409` メッセージ表示。
4. 既存タブに存在しない ID → `404` メッセージ表示。
5. 設定画面 → 4 スライダ調整 → ブラウザリロード → 値が維持される。
6. 別ブラウザ / シークレットで同 ID ログイン → 同じ値が見える（Server 側保存確認）。
7. 「続きから」は Phase 2 時点では非活性（ラン未保存のため）。
8. 「ログアウト」で `localStorage` クリア、ログイン画面へ。
9. 「終了」で確認モーダル表示。
10. Server 停止中に操作 → ネットワークエラー画面 / リトライボタンが機能する。

### 6.5 Done 判定

1. `dotnet build -warnaserror` クリーン。
2. `dotnet test` 全件成功。
3. `cd src/Client && npm run build` クリーン（TypeScript strict、ESLint エラー 0）。
4. `cd src/Client && npm run test` 全件成功。
5. 手動チェックリスト 10 項目すべて OK。
6. 新規 Core ファイルに VR 移植ノートコメントが付与されている。
7. `phase2-complete` タグが新規コミット列の末尾に付与されている。

---

## 7. 既知の制限と将来課題

| 項目 | 現状 | 将来フェーズでの対応 |
|---|---|---|
| `CreateAccount` の非アトミック性 | ファイル代理 DB の制約として受容 | Postgres 移行時に `UNIQUE` 制約で強化 |
| パスワード無し認証 | ヘッダに ID を載せるだけ | 認証フェーズ（ロードマップ外）で OAuth / パスワードを追加 |
| VR 移植時の翻訳コスト | record / 例外 / `System.Text.Json` を使用 | VR 移植フェーズで機械的翻訳 |
| OpenAPI 自動生成 | 未使用、DTO は手書き | Client 規模が大きくなった段階で `openapi-typescript-codegen` 等を検討 |
| 実際の音声再生 | 未実装、設定値の保存のみ | 後続フェーズで音源アセット導入後 |

---

## 8. Phase 2 成果物まとめ

**Core 追加**
- `src/Core/Settings/AudioSettings.cs`
- `src/Core/Settings/AudioSettingsSerializer.cs`
- `src/Core/Json/JsonOptions.cs`（共通化）

**Core 変更**
- `src/Core/Run/RunStateSerializer.cs`（`JsonOptions.Default` を参照）

**Server 追加**
- `src/Server/Abstractions/IAccountRepository.cs`
- `src/Server/Abstractions/IAudioSettingsRepository.cs`
- `src/Server/Abstractions/ISaveRepository.cs`
- `src/Server/Abstractions/Account.cs`
- `src/Server/Abstractions/AccountAlreadyExistsException.cs`
- `src/Server/Services/AccountIdValidator.cs`
- `src/Server/Services/FileBacked/FileAccountRepository.cs`
- `src/Server/Services/FileBacked/FileAudioSettingsRepository.cs`
- `src/Server/Services/FileBacked/FileSaveRepository.cs`（旧 `SaveRepository` の後継）
- `src/Server/Services/DataStorageOptions.cs`
- `src/Server/Controllers/AccountsController.cs`
- `src/Server/Controllers/AudioSettingsController.cs`
- `src/Server/Controllers/RunsController.cs`

**Server 変更**
- `src/Server/Program.cs`（`WeatherForecast` 削除、DI 登録、CORS、`public partial class Program {}` 追加）
- `src/Server/appsettings.json`（`DataStorage` セクション追加）
- `src/Server/Properties/launchSettings.json`（ポート固定）

**Server 削除**
- `src/Server/Services/SaveRepository.cs`（`FileBacked/FileSaveRepository.cs` に統合）

**Client 追加**
- `src/Client/src/api/client.ts`, `accounts.ts`, `audioSettings.ts`, `runs.ts`, `types.ts`
- `src/Client/src/context/AccountContext.tsx`
- `src/Client/src/hooks/useAudioSettings.ts`
- `src/Client/src/screens/LoginScreen.tsx`, `MainMenuScreen.tsx`, `SettingsScreen.tsx`
- `src/Client/src/components/Button.tsx`, `Slider.tsx`
- `src/Client/vite.config.ts`（proxy 設定）
- `src/Client/vitest.config.ts`
- `src/Client/package.json`（Vitest 関連 devDependencies 追加）

**Client 変更**
- `src/Client/src/App.tsx`（画面切替ロジック）
- `src/Client/src/main.tsx`（`AccountContext.Provider` でラップ）
- `src/Client/src/index.css`, `App.css`（整理）

**テスト追加**
- `tests/Core.Tests/Settings/AudioSettingsTests.cs`
- `tests/Core.Tests/Settings/AudioSettingsSerializerTests.cs`
- `tests/Core.Tests/Json/JsonOptionsTests.cs`
- `tests/Server.Tests/Services/FileAccountRepositoryTests.cs`
- `tests/Server.Tests/Services/FileAudioSettingsRepositoryTests.cs`
- `tests/Server.Tests/Services/AccountIdValidatorTests.cs`
- `tests/Server.Tests/Controllers/AccountsControllerTests.cs`
- `tests/Server.Tests/Controllers/AudioSettingsControllerTests.cs`
- `tests/Server.Tests/Controllers/RunsControllerTests.cs`
- `src/Client/src/api/client.test.ts`
- `src/Client/src/hooks/useAudioSettings.test.ts`
- `src/Client/src/context/AccountContext.test.tsx`

**テスト変更**
- `tests/Server.Tests/SaveRepositoryTests.cs` → `tests/Server.Tests/Services/FileSaveRepositoryTests.cs`（非同期化、新パス）

---
