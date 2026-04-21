# Phase 2 — メニュー／ログイン／設定 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ログイン→メインメニュー→音量設定の UI と、それを支える Server API／リポジトリ抽象化を TDD で構築する。

**Architecture:** Core 層に純粋な `AudioSettings` ドメインと共通 `JsonOptions` を追加し、Server 層でリポジトリ interface 経由のファイル永続化＋薄い MVC コントローラを提供、Client は React + Context + 手書き fetch で画面 3 種を実装する。将来の Postgres / VRChat 移植差し替えを阻害しない。

**Tech Stack:** C# .NET 10 / ASP.NET Core 10 / xUnit / React 19 + TypeScript / Vite 8 / Vitest + @testing-library/react + jsdom

**前提タグ:** `phase1-complete`（commit `42f2074`）

**参照仕様:** [docs/superpowers/specs/2026-04-20-phase02-menu-login-settings-design.md](../specs/2026-04-20-phase02-menu-login-settings-design.md)

---

## タスク一覧（22 タスク）

- **Group A — Core 基盤（3）**
  - Task 1: `JsonOptions` 抽出 + `RunStateSerializer` リファクタ
  - Task 2: `AudioSettings` レコード
  - Task 3: `AudioSettingsSerializer`
- **Group B — Server 下準備（4）**
  - Task 4: `Program.cs` サンプル削除 + ポート固定 + `partial class Program`
  - Task 5: `AccountIdValidator`
  - Task 6: `DataStorageOptions` + `appsettings.json DataStorage` 節
  - Task 7: `Abstractions/`（Account・例外・3 interface）
- **Group C — ファイル実装（3）**
  - Task 8: `FileAccountRepository`
  - Task 9: `FileAudioSettingsRepository`
  - Task 10: `SaveRepository` → `FileSaveRepository` 移行
- **Group D — API パイプライン（4）**
  - Task 11: `Program.cs` 配線（MVC + CORS + DI + `Cors:AllowedOrigins`）
  - Task 12: `AccountsController`
  - Task 13: `AudioSettingsController`
  - Task 14: `RunsController`
- **Group E — Client 基盤（3）**
  - Task 15: Vitest 導入 + `vite.config.ts` proxy + テスト環境
  - Task 16: `api/client.ts` + `ApiError`
  - Task 17: API モジュール群 + プリミティブコンポーネント
- **Group F — Client 画面とステート（5）**
  - Task 18: `AccountContext`
  - Task 19: `LoginScreen`
  - Task 20: `useAudioSettings` フック
  - Task 21: `MainMenuScreen` + `SettingsScreen`
  - Task 22: `App.tsx` 画面ルーティング + `main.tsx` ラップ + `phase2-complete` タグ

各タスクは「失敗テストを書く → 失敗を確認 → 最小実装 → 成功を確認 → コミット」の TDD ループに則る。構成ファイル／リネームのタスクは同等の「検証コマンドを走らせて期待の状態であることを確認」で代替する。

---

## Group A — Core 基盤

### Task 1: `JsonOptions` 抽出 + `RunStateSerializer` リファクタ

**目的:** 後続 `AudioSettingsSerializer` と `RunStateSerializer` が同一の `JsonSerializerOptions` を共有するための下地。挙動は変えない。

**Files:**
- Create: `src/Core/Json/JsonOptions.cs`
- Modify: `src/Core/Run/RunStateSerializer.cs:17-23`（`private static readonly JsonSerializerOptions Options` を削除して `JsonOptions.Default` 参照に変更）
- Create: `tests/Core.Tests/Json/JsonOptionsTests.cs`

- [ ] **Step 1: `JsonOptionsTests.cs` を作成して失敗テストを書く**

```csharp
// tests/Core.Tests/Json/JsonOptionsTests.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using RoguelikeCardGame.Core.Json;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Json;

public class JsonOptionsTests
{
    [Fact]
    public void Default_UsesCamelCasePolicy()
    {
        Assert.Same(JsonNamingPolicy.CamelCase, JsonOptions.Default.PropertyNamingPolicy);
    }

    [Fact]
    public void Default_DisallowsUnmappedMembers()
    {
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, JsonOptions.Default.UnmappedMemberHandling);
    }

    [Fact]
    public void Default_WritesCompactJson()
    {
        Assert.False(JsonOptions.Default.WriteIndented);
    }

    [Fact]
    public void Default_IncludesStringEnumConverter()
    {
        Assert.Contains(JsonOptions.Default.Converters, c => c is JsonStringEnumConverter);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter FullyQualifiedName~JsonOptionsTests`
Expected: コンパイルエラー「型または名前空間の名前 `JsonOptions` が見つかりません」。

- [ ] **Step 3: `JsonOptions.cs` を実装**

```csharp
// src/Core/Json/JsonOptions.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoguelikeCardGame.Core.Json;

/// <summary>
/// Core 層で共有する <see cref="JsonSerializerOptions"/>。
/// </summary>
/// <remarks>
/// VRChat (Udon#) 移植時は System.Text.Json が使えないため、この静的プロパティごと削除し、
/// 各シリアライザが手書きの JSON 変換に置き換わる想定。
/// </remarks>
public static class JsonOptions
{
    public static JsonSerializerOptions Default { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
```

- [ ] **Step 4: `JsonOptionsTests` が通ることを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter FullyQualifiedName~JsonOptionsTests`
Expected: `Passed!  - Failed: 0, Passed: 4`

- [ ] **Step 5: `RunStateSerializer.cs` を `JsonOptions.Default` 参照に書き換え**

```csharp
// src/Core/Run/RunStateSerializer.cs の冒頭 using に追加
using RoguelikeCardGame.Core.Json;
```

`RunStateSerializer` クラスを以下に置き換え（`Options` フィールドを削除、`Serialize`/`Deserialize` は `JsonOptions.Default` を使用）:

```csharp
public static class RunStateSerializer
{
    public static string Serialize(RunState state)
    {
        return JsonSerializer.Serialize(state, JsonOptions.Default);
    }

    public static RunState Deserialize(string json)
    {
        RunState? state;
        try
        {
            state = JsonSerializer.Deserialize<RunState>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex);
        }

        if (state is null)
            throw new RunStateSerializerException("RunState JSON が null として解釈されました。");

        if (state.SchemaVersion != RunState.CurrentSchemaVersion)
            throw new RunStateSerializerException(
                $"未対応の schemaVersion: {state.SchemaVersion} (対応: {RunState.CurrentSchemaVersion})");

        return state;
    }
}
```

併せて `using System.Text.Json.Serialization;` は `RunStateSerializer.cs` では使わなくなるので削除。

- [ ] **Step 6: 既存 `RunStateSerializer` テストが依然全通することを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj`
Expected: `Passed!  - Failed: 0` （既存の `RunStateSerializer` ケース全 + 新規 4 ケース）

- [ ] **Step 7: コミット**

```bash
git add src/Core/Json/JsonOptions.cs src/Core/Run/RunStateSerializer.cs tests/Core.Tests/Json/JsonOptionsTests.cs
git commit -m "feat(core): extract JsonOptions.Default for reuse across serializers"
```

---

### Task 2: `AudioSettings` レコード

**目的:** 音量設定ドメインの不変データと範囲検証ファクトリ。

**Files:**
- Create: `src/Core/Settings/AudioSettings.cs`
- Create: `tests/Core.Tests/Settings/AudioSettingsTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
// tests/Core.Tests/Settings/AudioSettingsTests.cs
using System;
using RoguelikeCardGame.Core.Settings;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Settings;

public class AudioSettingsTests
{
    [Fact]
    public void Default_UsesExpectedInitialValues()
    {
        var d = AudioSettings.Default;
        Assert.Equal(AudioSettings.CurrentSchemaVersion, d.SchemaVersion);
        Assert.Equal(80, d.Master);
        Assert.Equal(70, d.Bgm);
        Assert.Equal(80, d.Se);
        Assert.Equal(60, d.Ambient);
    }

    [Fact]
    public void Create_WithValidValues_ReturnsSettings()
    {
        var s = AudioSettings.Create(master: 100, bgm: 0, se: 50, ambient: 25);
        Assert.Equal(AudioSettings.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Equal(100, s.Master);
        Assert.Equal(0, s.Bgm);
        Assert.Equal(50, s.Se);
        Assert.Equal(25, s.Ambient);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(101, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 101, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 101, 0)]
    [InlineData(0, 0, 0, -1)]
    [InlineData(0, 0, 0, 101)]
    public void Create_WithOutOfRange_Throws(int master, int bgm, int se, int ambient)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioSettings.Create(master, bgm, se, ambient));
    }

    [Fact]
    public void Create_AtBoundaries_Succeeds()
    {
        var lo = AudioSettings.Create(0, 0, 0, 0);
        var hi = AudioSettings.Create(100, 100, 100, 100);
        Assert.Equal(0, lo.Master);
        Assert.Equal(100, hi.Ambient);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter FullyQualifiedName~AudioSettingsTests`
Expected: コンパイルエラー「`AudioSettings` が見つかりません」。

- [ ] **Step 3: `AudioSettings.cs` を実装**

```csharp
// src/Core/Settings/AudioSettings.cs
using System;

namespace RoguelikeCardGame.Core.Settings;

/// <summary>
/// プレイヤーごとの音量設定。値は 0–100（検証済み）。
/// </summary>
/// <remarks>
/// VRChat (Udon#) 移植時は record → sealed class に変換し、
/// 各フィールドは PlayerData の個別 key（例 "audio.master"）に分解する想定。
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
    public static AudioSettings Create(int master, int bgm, int se, int ambient)
    {
        ValidateRange(master, nameof(master));
        ValidateRange(bgm, nameof(bgm));
        ValidateRange(se, nameof(se));
        ValidateRange(ambient, nameof(ambient));
        return new AudioSettings(CurrentSchemaVersion, master, bgm, se, ambient);
    }

    private static void ValidateRange(int value, string paramName)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(paramName, value, "値は 0–100 の範囲内である必要があります。");
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter FullyQualifiedName~AudioSettingsTests`
Expected: `Passed!  - Failed: 0, Passed: 11` (Default 1 + Create 有効 1 + Theory 8 + 境界 1)

- [ ] **Step 5: コミット**

```bash
git add src/Core/Settings/AudioSettings.cs tests/Core.Tests/Settings/AudioSettingsTests.cs
git commit -m "feat(core): add AudioSettings record with range-validated factory"
```

---

### Task 3: `AudioSettingsSerializer`

**目的:** `AudioSettings` ⇔ JSON 変換、`schemaVersion` とメンバ整合性検証。

**Files:**
- Create: `src/Core/Settings/AudioSettingsSerializer.cs`
- Create: `tests/Core.Tests/Settings/AudioSettingsSerializerTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
// tests/Core.Tests/Settings/AudioSettingsSerializerTests.cs
using RoguelikeCardGame.Core.Settings;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Settings;

public class AudioSettingsSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesAllValues()
    {
        var original = AudioSettings.Create(master: 10, bgm: 20, se: 30, ambient: 40);
        var json = AudioSettingsSerializer.Serialize(original);
        var restored = AudioSettingsSerializer.Deserialize(json);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Serialize_UsesCamelCaseFieldNames()
    {
        var json = AudioSettingsSerializer.Serialize(AudioSettings.Default);
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"master\":80", json);
        Assert.Contains("\"bgm\":70", json);
        Assert.Contains("\"se\":80", json);
        Assert.Contains("\"ambient\":60", json);
    }

    [Fact]
    public void Deserialize_UnknownField_Throws()
    {
        var json = "{\"schemaVersion\":1,\"master\":80,\"bgm\":70,\"se\":80,\"ambient\":60,\"extra\":1}";
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_WrongSchemaVersion_Throws()
    {
        var json = "{\"schemaVersion\":999,\"master\":80,\"bgm\":70,\"se\":80,\"ambient\":60}";
        var ex = Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Fact]
    public void Deserialize_OutOfRangeValue_Throws()
    {
        var json = "{\"schemaVersion\":1,\"master\":101,\"bgm\":0,\"se\":0,\"ambient\":0}";
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_InvalidJson_Throws()
    {
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize("not json"));
    }

    [Fact]
    public void Deserialize_NullLiteral_Throws()
    {
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize("null"));
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter FullyQualifiedName~AudioSettingsSerializerTests`
Expected: コンパイルエラー「`AudioSettingsSerializer` が見つかりません」。

- [ ] **Step 3: `AudioSettingsSerializer.cs` を実装**

```csharp
// src/Core/Settings/AudioSettingsSerializer.cs
using System;
using System.Text.Json;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Settings;

/// <summary>AudioSettings JSON のパース失敗を表す例外。</summary>
/// <remarks>VR 移植時はこの例外クラスを UdonSharpBehaviour のエラーフラグ文字列に置換する想定。</remarks>
public sealed class AudioSettingsSerializerException : Exception
{
    public AudioSettingsSerializerException(string message) : base(message) { }
    public AudioSettingsSerializerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>AudioSettings ⇔ JSON 文字列の変換。ファイル I/O は Server 側の Repository が担当。</summary>
public static class AudioSettingsSerializer
{
    public static string Serialize(AudioSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonOptions.Default);
    }

    public static AudioSettings Deserialize(string json)
    {
        AudioSettings? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize<AudioSettings>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new AudioSettingsSerializerException("AudioSettings JSON のパースに失敗しました。", ex);
        }

        if (deserialized is null)
            throw new AudioSettingsSerializerException("AudioSettings JSON が null として解釈されました。");

        if (deserialized.SchemaVersion != AudioSettings.CurrentSchemaVersion)
            throw new AudioSettingsSerializerException(
                $"未対応の schemaVersion: {deserialized.SchemaVersion} (対応: {AudioSettings.CurrentSchemaVersion})");

        try
        {
            return AudioSettings.Create(deserialized.Master, deserialized.Bgm, deserialized.Se, deserialized.Ambient);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new AudioSettingsSerializerException("AudioSettings の値が許容範囲外です。", ex);
        }
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj --filter FullyQualifiedName~AudioSettingsSerializerTests`
Expected: `Passed!  - Failed: 0, Passed: 7`

- [ ] **Step 5: Core 全テストで回帰がないことを確認**

Run: `dotnet test tests/Core.Tests/Core.Tests.csproj`
Expected: `Failed: 0`（Phase 1 既存 + Task 1–3 新規すべて通る）

- [ ] **Step 6: コミット**

```bash
git add src/Core/Settings/AudioSettingsSerializer.cs tests/Core.Tests/Settings/AudioSettingsSerializerTests.cs
git commit -m "feat(core): add AudioSettingsSerializer with schema + range validation"
```

---

## Group B — Server 下準備

### Task 4: `Program.cs` のサンプル削除 + ポート固定 + `partial class Program`

**目的:** 以降のコントローラ配線と `WebApplicationFactory<Program>` 利用の下地。まだ MVC 等は入れず、最小構成で動く状態に戻す。

**Files:**
- Modify: `src/Server/Program.cs`（`WeatherForecast` 関連を全削除、`public partial class Program {}` を追加）
- Modify: `src/Server/Properties/launchSettings.json`（`http` プロファイルを `http://localhost:5114` に固定、`https` プロファイル削除）

- [ ] **Step 1: `src/Server/Program.cs` を次の内容に置き換える**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program { }
```

- [ ] **Step 2: `launchSettings.json` を書き換える**

```json
{
  "$schema": "https://json.schemastore.org/launchsettings.json",
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5114",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

（`https` プロファイルと `IIS Express` 節は削除。Phase 2 は HTTP のみ使用。）

- [ ] **Step 3: ビルドして起動できることを確認**

Run: `dotnet build src/Server/Server.csproj`
Expected: `Build succeeded` / `0 Error(s)`

Run（別ターミナル想定、手動確認）: `dotnet run --project src/Server/Server.csproj`
Expected: コンソールに `Now listening on: http://localhost:5114` が出て `Ctrl+C` で終了できる。

- [ ] **Step 4: 既存の `SaveRepositoryTests` が依然通ることを確認（回帰防止）**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj`
Expected: `Failed: 0`

- [ ] **Step 5: コミット**

```bash
git add src/Server/Program.cs src/Server/Properties/launchSettings.json
git commit -m "chore(server): drop weather sample, pin dev port 5114, expose Program for test host"
```

---

### Task 5: `AccountIdValidator`

**目的:** 3 リポジトリ + コントローラで共有する accountId 検証ロジックを 1 箇所に集約。

**Files:**
- Create: `src/Server/Services/AccountIdValidator.cs`
- Create: `tests/Server.Tests/Services/AccountIdValidatorTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
// tests/Server.Tests/Services/AccountIdValidatorTests.cs
using System;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class AccountIdValidatorTests
{
    [Theory]
    [InlineData("player-001")]
    [InlineData("ABC_123")]
    [InlineData("日本語id")]
    [InlineData("a")]
    public void Validate_ValidId_DoesNotThrow(string id)
    {
        AccountIdValidator.Validate(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    [InlineData("../escape")]
    [InlineData("with:colon")]
    [InlineData("with*star")]
    [InlineData("with?question")]
    [InlineData("with|pipe")]
    [InlineData("with\"quote")]
    public void Validate_InvalidId_ThrowsArgumentException(string id)
    {
        Assert.Throws<ArgumentException>(() => AccountIdValidator.Validate(id));
    }

    [Fact]
    public void Validate_NullId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AccountIdValidator.Validate(null!));
    }

    [Fact]
    public void Validate_WithControlCharacter_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AccountIdValidator.Validate("tab\there"));
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~AccountIdValidatorTests`
Expected: コンパイルエラー「`AccountIdValidator` が見つかりません」。

- [ ] **Step 3: `AccountIdValidator.cs` を実装**

```csharp
// src/Server/Services/AccountIdValidator.cs
using System;
using System.IO;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// accountId がファイル名・HTTP ルート両面で安全な文字列であることを検証する共通ユーティリティ。
/// </summary>
public static class AccountIdValidator
{
    public static void Validate(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("accountId は空にできません。", nameof(accountId));

        if (accountId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(
                $"accountId にファイル名として使えない文字が含まれています: {accountId}",
                nameof(accountId));

        if (accountId.Contains('/') || accountId.Contains('\\'))
            throw new ArgumentException(
                $"accountId にパス区切り文字が含まれています: {accountId}",
                nameof(accountId));
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~AccountIdValidatorTests`
Expected: `Passed!  - Failed: 0, Passed: 16` (Theory の有効 4 + 無効 10 + null 1 + 制御文字 1)

- [ ] **Step 5: コミット**

```bash
git add src/Server/Services/AccountIdValidator.cs tests/Server.Tests/Services/AccountIdValidatorTests.cs
git commit -m "feat(server): add shared AccountIdValidator"
```

---

### Task 6: `DataStorageOptions` + `appsettings.json` DataStorage セクション

**目的:** ファイルリポジトリのルートディレクトリを `IConfiguration` 経由で注入できるようにする。

**Files:**
- Create: `src/Server/Services/DataStorageOptions.cs`
- Modify: `src/Server/appsettings.json`
- Modify: `src/Server/appsettings.Development.json`（存在すれば同じ扱い、無ければ作成不要）

- [ ] **Step 1: `DataStorageOptions.cs` を作成**

```csharp
// src/Server/Services/DataStorageOptions.cs
namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> の
/// <c>DataStorage</c> セクションにバインドされる設定 POCO。
/// </summary>
public sealed class DataStorageOptions
{
    public const string SectionName = "DataStorage";

    /// <summary>ファイル代理 DB のルートディレクトリ（相対 or 絶対パス）。</summary>
    public string RootDirectory { get; set; } = "./data-local";
}
```

- [ ] **Step 2: `appsettings.json` に `DataStorage` セクションを追加**

`src/Server/appsettings.json` を次の内容に置き換え:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "DataStorage": {
    "RootDirectory": "./data-local"
  }
}
```

- [ ] **Step 3: ビルドが通ることを確認**

Run: `dotnet build src/Server/Server.csproj`
Expected: `Build succeeded` / `0 Error(s)`

- [ ] **Step 4: `.gitignore` に `data-local` を追加**

`.gitignore` の末尾に以下を追加（既存行は保持）:

```
# Phase 2 file-backed stand-in database
/src/Server/data-local/
/src/Server/bin/**/data-local/
```

- [ ] **Step 5: コミット**

```bash
git add src/Server/Services/DataStorageOptions.cs src/Server/appsettings.json .gitignore
git commit -m "feat(server): add DataStorageOptions bound to appsettings DataStorage section"
```

---

### Task 7: `Abstractions/` — Account / 例外 / 3 interface

**目的:** リポジトリ interface 群と DTO `Account`、専用例外を 1 つのタスクで揃える（いずれも内容が短いので 1 コミット）。

**Files:**
- Create: `src/Server/Abstractions/Account.cs`
- Create: `src/Server/Abstractions/AccountAlreadyExistsException.cs`
- Create: `src/Server/Abstractions/IAccountRepository.cs`
- Create: `src/Server/Abstractions/IAudioSettingsRepository.cs`
- Create: `src/Server/Abstractions/ISaveRepository.cs`

テストはこのタスクでは書かない（interface のみなので実装タスク側でテスト）。コンパイルが通り、ビルド警告が出ないことが合格条件。

- [ ] **Step 1: `Account.cs` を作成**

```csharp
// src/Server/Abstractions/Account.cs
using System;

namespace RoguelikeCardGame.Server.Abstractions;

/// <summary>アカウントメタデータ（ID と作成日時）。</summary>
/// <remarks>Server 専用。VR 移植時は PlayerData の読み出し結果に置き換える。</remarks>
public sealed record Account(string Id, DateTimeOffset CreatedAt);
```

- [ ] **Step 2: `AccountAlreadyExistsException.cs` を作成**

```csharp
// src/Server/Abstractions/AccountAlreadyExistsException.cs
using System;

namespace RoguelikeCardGame.Server.Abstractions;

public sealed class AccountAlreadyExistsException : Exception
{
    public AccountAlreadyExistsException(string accountId)
        : base($"アカウント ID はすでに存在します: {accountId}")
    {
        AccountId = accountId;
    }

    public string AccountId { get; }
}
```

- [ ] **Step 3: `IAccountRepository.cs` を作成**

```csharp
// src/Server/Abstractions/IAccountRepository.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RoguelikeCardGame.Server.Abstractions;

/// <summary>アカウントメタデータの永続化。</summary>
/// <remarks>Server 専用。VR 移植時は UdonSharp の PlayerData API に置き換える。</remarks>
public interface IAccountRepository
{
    Task<bool> ExistsAsync(string accountId, CancellationToken ct);

    /// <exception cref="AccountAlreadyExistsException">既存 ID を再作成しようとした。</exception>
    Task CreateAsync(string accountId, DateTimeOffset nowUtc, CancellationToken ct);

    /// <returns>未登録の場合は <c>null</c>。</returns>
    Task<Account?> GetAsync(string accountId, CancellationToken ct);
}
```

- [ ] **Step 4: `IAudioSettingsRepository.cs` を作成**

```csharp
// src/Server/Abstractions/IAudioSettingsRepository.cs
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Settings;

namespace RoguelikeCardGame.Server.Abstractions;

/// <summary>アカウント別の音量設定を永続化する。</summary>
/// <remarks>Server 専用。VR 移植時は UdonSharp の PlayerData API に置き換える。</remarks>
public interface IAudioSettingsRepository
{
    /// <summary>保存が無い場合は <see cref="AudioSettings.Default"/> を返す。</summary>
    Task<AudioSettings> GetOrDefaultAsync(string accountId, CancellationToken ct);

    Task UpsertAsync(string accountId, AudioSettings settings, CancellationToken ct);
}
```

- [ ] **Step 5: `ISaveRepository.cs` を作成**

```csharp
// src/Server/Abstractions/ISaveRepository.cs
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Server.Abstractions;

/// <summary>ラン状態の永続化。Phase 2 ではソロの単一スロットのみ。</summary>
/// <remarks>Server 専用。VR 移植時は UdonSharp の PlayerData API に置き換える。</remarks>
public interface ISaveRepository
{
    Task SaveAsync(string accountId, RunState state, CancellationToken ct);

    /// <returns>未保存の場合は <c>null</c>。</returns>
    Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct);

    Task DeleteAsync(string accountId, CancellationToken ct);
}
```

- [ ] **Step 6: ビルドと既存テストの通過を確認**

Run: `dotnet build`
Expected: `0 Error(s), 0 Warning(s)`

Run: `dotnet test`
Expected: `Failed: 0`

- [ ] **Step 7: コミット**

```bash
git add src/Server/Abstractions/
git commit -m "feat(server): introduce repository abstractions for accounts/audio/saves"
```

---

## Group C — ファイル実装

### Task 8: `FileAccountRepository`

**Files:**
- Create: `src/Server/Services/FileBacked/FileAccountRepository.cs`
- Create: `tests/Server.Tests/Services/FileAccountRepositoryTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
// tests/Server.Tests/Services/FileAccountRepositoryTests.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class FileAccountRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileAccountRepository _repo;
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    public FileAccountRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rcg-account-tests-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DataStorageOptions { RootDirectory = _tempRoot });
        _repo = new FileAccountRepository(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseForMissingAccount()
    {
        Assert.False(await _repo.ExistsAsync("never", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ThenExists_ReturnsTrue()
    {
        await _repo.CreateAsync("alice", FixedNow, CancellationToken.None);
        Assert.True(await _repo.ExistsAsync("alice", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Twice_ThrowsAccountAlreadyExists()
    {
        await _repo.CreateAsync("dup", FixedNow, CancellationToken.None);
        await Assert.ThrowsAsync<AccountAlreadyExistsException>(() =>
            _repo.CreateAsync("dup", FixedNow, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_Missing_ReturnsNull()
    {
        Assert.Null(await _repo.GetAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_AfterCreate_ReturnsAccountWithCreatedAt()
    {
        await _repo.CreateAsync("bob", FixedNow, CancellationToken.None);
        var got = await _repo.GetAsync("bob", CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal("bob", got!.Id);
        Assert.Equal(FixedNow, got.CreatedAt);
    }

    [Fact]
    public async Task CreateAsync_WritesUnderAccountsSubdir()
    {
        await _repo.CreateAsync("carol", FixedNow, CancellationToken.None);
        var expected = Path.Combine(_tempRoot, "accounts", "carol.json");
        Assert.True(File.Exists(expected));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    public async Task InvalidId_Throws(string bad)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.CreateAsync(bad, FixedNow, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.ExistsAsync(bad, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.GetAsync(bad, CancellationToken.None));
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~FileAccountRepositoryTests`
Expected: コンパイルエラー「`FileAccountRepository` が見つかりません」。

- [ ] **Step 3: `FileAccountRepository.cs` を実装**

```csharp
// src/Server/Services/FileBacked/FileAccountRepository.cs
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Json;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/accounts/{accountId}.json</c> にアカウントメタデータを永続化する。</summary>
public sealed class FileAccountRepository : IAccountRepository
{
    private readonly string _dir;

    public FileAccountRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory が未設定です。", nameof(options));
        _dir = Path.Combine(root, "accounts");
        Directory.CreateDirectory(_dir);
    }

    public Task<bool> ExistsAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        return Task.FromResult(File.Exists(PathFor(accountId)));
    }

    public async Task CreateAsync(string accountId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (File.Exists(path))
            throw new AccountAlreadyExistsException(accountId);

        var account = new Account(accountId, nowUtc);
        var json = JsonSerializer.Serialize(account, JsonOptions.Default);
        await WriteAtomicAsync(path, json, ct);
    }

    public async Task<Account?> GetAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return JsonSerializer.Deserialize<Account>(json, JsonOptions.Default);
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");

    private static async Task WriteAtomicAsync(string finalPath, string contents, CancellationToken ct)
    {
        var tmp = finalPath + ".tmp";
        await File.WriteAllTextAsync(tmp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, finalPath, overwrite: true);
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~FileAccountRepositoryTests`
Expected: `Failed: 0, Passed: 10` (6 単独 + Theory 4)

- [ ] **Step 5: コミット**

```bash
git add src/Server/Services/FileBacked/FileAccountRepository.cs tests/Server.Tests/Services/FileAccountRepositoryTests.cs
git commit -m "feat(server): add FileAccountRepository under accounts/ subdir"
```

---

### Task 9: `FileAudioSettingsRepository`

**Files:**
- Create: `src/Server/Services/FileBacked/FileAudioSettingsRepository.cs`
- Create: `tests/Server.Tests/Services/FileAudioSettingsRepositoryTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
// tests/Server.Tests/Services/FileAudioSettingsRepositoryTests.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Settings;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class FileAudioSettingsRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileAudioSettingsRepository _repo;

    public FileAudioSettingsRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rcg-audio-tests-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DataStorageOptions { RootDirectory = _tempRoot });
        _repo = new FileAudioSettingsRepository(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task GetOrDefault_NoFile_ReturnsDefault()
    {
        var got = await _repo.GetOrDefaultAsync("new-player", CancellationToken.None);
        Assert.Equal(AudioSettings.Default, got);
    }

    [Fact]
    public async Task Upsert_ThenGet_ReturnsPersistedValue()
    {
        var custom = AudioSettings.Create(master: 10, bgm: 20, se: 30, ambient: 40);
        await _repo.UpsertAsync("alice", custom, CancellationToken.None);
        var got = await _repo.GetOrDefaultAsync("alice", CancellationToken.None);
        Assert.Equal(custom, got);
    }

    [Fact]
    public async Task Upsert_OverwritesPreviousValue()
    {
        await _repo.UpsertAsync("bob", AudioSettings.Create(1, 1, 1, 1), CancellationToken.None);
        await _repo.UpsertAsync("bob", AudioSettings.Create(99, 99, 99, 99), CancellationToken.None);
        var got = await _repo.GetOrDefaultAsync("bob", CancellationToken.None);
        Assert.Equal(99, got.Master);
    }

    [Fact]
    public async Task Upsert_IsIsolatedPerAccount()
    {
        await _repo.UpsertAsync("a", AudioSettings.Create(10, 10, 10, 10), CancellationToken.None);
        await _repo.UpsertAsync("b", AudioSettings.Create(90, 90, 90, 90), CancellationToken.None);
        var a = await _repo.GetOrDefaultAsync("a", CancellationToken.None);
        var b = await _repo.GetOrDefaultAsync("b", CancellationToken.None);
        Assert.Equal(10, a.Master);
        Assert.Equal(90, b.Master);
    }

    [Fact]
    public async Task Upsert_WritesUnderAudioSettingsSubdir()
    {
        await _repo.UpsertAsync("carol", AudioSettings.Default, CancellationToken.None);
        var expected = Path.Combine(_tempRoot, "audio_settings", "carol.json");
        Assert.True(File.Exists(expected));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has/slash")]
    public async Task InvalidId_Throws(string bad)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.GetOrDefaultAsync(bad, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.UpsertAsync(bad, AudioSettings.Default, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrDefault_AfterCorruptedFile_FallsBackToDefault()
    {
        var subdir = Path.Combine(_tempRoot, "audio_settings");
        Directory.CreateDirectory(subdir);
        await File.WriteAllTextAsync(Path.Combine(subdir, "corrupt.json"), "not-json");

        // 仕様上は破損ファイル → Default ではなく例外、が将来の堅牢化候補。
        // Phase 2 では破損時も Default を返して運用継続する (ユーザ体験優先)。
        var got = await _repo.GetOrDefaultAsync("corrupt", CancellationToken.None);
        Assert.Equal(AudioSettings.Default, got);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~FileAudioSettingsRepositoryTests`
Expected: コンパイルエラー「`FileAudioSettingsRepository` が見つかりません」。

- [ ] **Step 3: `FileAudioSettingsRepository.cs` を実装**

```csharp
// src/Server/Services/FileBacked/FileAudioSettingsRepository.cs
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Settings;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/audio_settings/{accountId}.json</c> に音量設定を永続化する。</summary>
public sealed class FileAudioSettingsRepository : IAudioSettingsRepository
{
    private readonly string _dir;

    public FileAudioSettingsRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory が未設定です。", nameof(options));
        _dir = Path.Combine(root, "audio_settings");
        Directory.CreateDirectory(_dir);
    }

    public async Task<AudioSettings> GetOrDefaultAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return AudioSettings.Default;

        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            return AudioSettingsSerializer.Deserialize(json);
        }
        catch (AudioSettingsSerializerException)
        {
            // 破損ファイルは運用継続のため Default にフォールバック。
            return AudioSettings.Default;
        }
    }

    public async Task UpsertAsync(string accountId, AudioSettings settings, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        // 値域検証は Create 経由で強制（呼び出し側が不正値を渡した場合のガード）。
        var validated = AudioSettings.Create(settings.Master, settings.Bgm, settings.Se, settings.Ambient);
        var json = AudioSettingsSerializer.Serialize(validated);

        var final = PathFor(accountId);
        var tmp = final + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, final, overwrite: true);
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~FileAudioSettingsRepositoryTests`
Expected: `Failed: 0, Passed: 10` (単独 6 + Theory 3 + 破損 1)

- [ ] **Step 5: コミット**

```bash
git add src/Server/Services/FileBacked/FileAudioSettingsRepository.cs tests/Server.Tests/Services/FileAudioSettingsRepositoryTests.cs
git commit -m "feat(server): add FileAudioSettingsRepository with default fallback on corruption"
```

---

### Task 10: `SaveRepository` → `FileSaveRepository` 移行

**目的:** Phase 1 の同期実装を `ISaveRepository` 実装 + 非同期 + 新パスに移す。既存 11 テストは async 書き換えで全維持、加えて `.tmp` 残存無しを検証。

**Files:**
- Delete: `src/Server/Services/SaveRepository.cs`
- Delete: `tests/Server.Tests/SaveRepositoryTests.cs`
- Create: `src/Server/Services/FileBacked/FileSaveRepository.cs`
- Create: `tests/Server.Tests/Services/FileSaveRepositoryTests.cs`

- [ ] **Step 1: 新テスト `FileSaveRepositoryTests.cs` を作成**

```csharp
// tests/Server.Tests/Services/FileSaveRepositoryTests.cs
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class FileSaveRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DataCatalog _catalog = EmbeddedDataLoader.LoadCatalog();
    private readonly FileSaveRepository _repo;
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    public FileSaveRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rcg-save-tests-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DataStorageOptions { RootDirectory = _tempRoot });
        _repo = new FileSaveRepository(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private RunState FreshRun(ulong seed = 42UL) =>
        RunState.NewSoloRun(_catalog, rngSeed: seed, nowUtc: FixedNow);

    [Fact]
    public async Task Save_CreatesFileUnderSavesSubdir()
    {
        await _repo.SaveAsync("player-001", FreshRun(), CancellationToken.None);
        var expected = Path.Combine(_tempRoot, "saves", "player-001.json");
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public async Task TryLoad_AfterSave_ReturnsEquivalentState()
    {
        var original = FreshRun(seed: 777UL);
        await _repo.SaveAsync("player-002", original, CancellationToken.None);

        var restored = await _repo.TryLoadAsync("player-002", CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(original.RngSeed, restored!.RngSeed);
        Assert.Equal(original.MaxHp, restored.MaxHp);
        Assert.Equal(original.Deck, restored.Deck);
    }

    [Fact]
    public async Task TryLoad_MissingAccount_ReturnsNull()
    {
        Assert.Null(await _repo.TryLoadAsync("never-saved", CancellationToken.None));
    }

    [Fact]
    public async Task Save_OverwritesExistingFile()
    {
        await _repo.SaveAsync("p", FreshRun(seed: 1UL), CancellationToken.None);
        await _repo.SaveAsync("p", FreshRun(seed: 2UL), CancellationToken.None);

        var state = await _repo.TryLoadAsync("p", CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(2UL, state!.RngSeed);
    }

    [Fact]
    public async Task Delete_ExistingAccount_RemovesFile()
    {
        await _repo.SaveAsync("to-delete", FreshRun(), CancellationToken.None);
        var path = Path.Combine(_tempRoot, "saves", "to-delete.json");
        Assert.True(File.Exists(path));

        await _repo.DeleteAsync("to-delete", CancellationToken.None);

        Assert.False(File.Exists(path));
        Assert.Null(await _repo.TryLoadAsync("to-delete", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_MissingAccount_IsNoOp()
    {
        await _repo.DeleteAsync("never-existed", CancellationToken.None);
    }

    [Fact]
    public async Task Save_DoesNotLeaveTmpFile()
    {
        await _repo.SaveAsync("tidy", FreshRun(), CancellationToken.None);
        var tmp = Path.Combine(_tempRoot, "saves", "tidy.json.tmp");
        Assert.False(File.Exists(tmp));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escape")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    public async Task InvalidAccountId_Throws(string bad)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.SaveAsync(bad, FreshRun(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.TryLoadAsync(bad, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.DeleteAsync(bad, CancellationToken.None));
    }
}
```

- [ ] **Step 2: 旧 `SaveRepositoryTests.cs` を削除（新テストで置き換わる）**

```bash
rm tests/Server.Tests/SaveRepositoryTests.cs
```

- [ ] **Step 3: テストが失敗することを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~FileSaveRepositoryTests`
Expected: コンパイルエラー「`FileSaveRepository` が見つかりません」。

- [ ] **Step 4: `FileSaveRepository.cs` を実装**

```csharp
// src/Server/Services/FileBacked/FileSaveRepository.cs
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/saves/{accountId}.json</c> にソロランを永続化する。</summary>
public sealed class FileSaveRepository : ISaveRepository
{
    private readonly string _dir;

    public FileSaveRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory が未設定です。", nameof(options));
        _dir = Path.Combine(root, "saves");
        Directory.CreateDirectory(_dir);
    }

    public async Task SaveAsync(string accountId, RunState state, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        if (state is null) throw new ArgumentNullException(nameof(state));

        var json = RunStateSerializer.Serialize(state);
        var final = PathFor(accountId);
        var tmp = final + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, final, overwrite: true);
    }

    public async Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return RunStateSerializer.Deserialize(json);
    }

    public Task DeleteAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");
}
```

- [ ] **Step 5: 旧 `src/Server/Services/SaveRepository.cs` を削除**

```bash
rm src/Server/Services/SaveRepository.cs
```

- [ ] **Step 6: Server 全テストが通ることを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj`
Expected: `Failed: 0` (Task 5/8/9 + FileSaveRepository 新規 12 ケース)

- [ ] **Step 7: コミット**

```bash
git add src/Server/Services/FileBacked/FileSaveRepository.cs src/Server/Services/SaveRepository.cs tests/Server.Tests/Services/FileSaveRepositoryTests.cs tests/Server.Tests/SaveRepositoryTests.cs
git commit -m "refactor(server): migrate SaveRepository to ISaveRepository/FileSaveRepository (async, saves/ subdir)"
```

---

## Group D — API パイプライン

### Task 11: `Program.cs` 配線（MVC + CORS + DI + `Cors:AllowedOrigins`）

**目的:** コントローラが動くように ASP.NET Core のパイプラインを組む。CORS は appsettings 経由。

**Files:**
- Modify: `src/Server/Program.cs`
- Modify: `src/Server/appsettings.json`（`Cors` セクション追加）
- Create: `src/Server/appsettings.Development.json`（存在しない場合のみ）

- [ ] **Step 1: `src/Server/appsettings.json` に `Cors` セクションを追加**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "DataStorage": {
    "RootDirectory": "./data-local"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173"
    ]
  }
}
```

- [ ] **Step 2: `src/Server/Program.cs` を以下に置き換え**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.Configure<DataStorageOptions>(
    builder.Configuration.GetSection(DataStorageOptions.SectionName));

builder.Services.AddSingleton<IAccountRepository, FileAccountRepository>();
builder.Services.AddSingleton<IAudioSettingsRepository, FileAudioSettingsRepository>();
builder.Services.AddSingleton<ISaveRepository, FileSaveRepository>();

const string CorsPolicyName = "ClientCors";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicyName);
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapControllers();

app.Run();

public partial class Program { }
```

- [ ] **Step 3: ビルドが通ることを確認**

Run: `dotnet build src/Server/Server.csproj`
Expected: `0 Error(s)`

- [ ] **Step 4: 既存テストが依然通ることを確認**

Run: `dotnet test`
Expected: `Failed: 0`

- [ ] **Step 5: コミット**

```bash
git add src/Server/Program.cs src/Server/appsettings.json
git commit -m "feat(server): wire MVC, CORS (appsettings-driven), DI for repositories"
```

---

### Task 12: `AccountsController`

**目的:** `POST /api/v1/accounts` と `GET /api/v1/accounts/{id}`。

**Files:**
- Create: `src/Server/Controllers/AccountsController.cs`
- Create: `tests/Server.Tests/Controllers/AccountsControllerTests.cs`

- [ ] **Step 1: `Server.Tests.csproj` に `WebApplicationFactory` 依存を追加**

`tests/Server.Tests/Server.Tests.csproj` の `ItemGroup` (PackageReference 群) に次を追加:

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
```

- [ ] **Step 2: 失敗テスト `AccountsControllerTests.cs` を書く**

```csharp
// tests/Server.Tests/Controllers/AccountsControllerTests.cs
using System;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class AccountsControllerTests : IClassFixture<TempDataFactory>, IDisposable
{
    private readonly TempDataFactory _factory;

    public AccountsControllerTests(TempDataFactory factory) => _factory = factory;

    public void Dispose() => _factory.ResetData();

    [Fact]
    public async Task Post_NewId_Returns201WithBody()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = "new-user" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(body);
        Assert.Equal("new-user", body!.Id);
    }

    [Fact]
    public async Task Post_DuplicateId_Returns409()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = "dup" });
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = "dup" });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has/slash")]
    public async Task Post_InvalidId_Returns400(string bad)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = bad });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Get_Existing_Returns200()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = "alice" });

        var res = await client.GetAsync("/api/v1/accounts/alice");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.Equal("alice", body!.Id);
    }

    [Fact]
    public async Task Get_Missing_Returns404()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/accounts/nope");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Theory]
    [InlineData("has%2Fslash")] // URL-encoded '/'
    [InlineData("%20")]          // URL-encoded space
    public async Task Get_InvalidId_Returns400(string badEncoded)
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync($"/api/v1/accounts/{badEncoded}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private sealed record AccountResponse(string Id, DateTimeOffset CreatedAt);
}

/// <summary>テスト間で独立した data ディレクトリを持つ Program 用 factory。</summary>
public sealed class TempDataFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "rcg-integration-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>("DataStorage:RootDirectory", _dataRoot),
            });
        });
    }

    public void ResetData()
    {
        if (Directory.Exists(_dataRoot)) Directory.Delete(_dataRoot, recursive: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) ResetData();
        base.Dispose(disposing);
    }
}
```

- [ ] **Step 3: テストが失敗することを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~AccountsControllerTests`
Expected: コンパイルエラー or 404 群（`/api/v1/accounts` ルート未実装）。

- [ ] **Step 4: `AccountsController.cs` を実装**

```csharp
// src/Server/Controllers/AccountsController.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly IAccountRepository _accounts;

    public AccountsController(IAccountRepository accounts) => _accounts = accounts;

    public sealed record CreateAccountRequest([Required] string AccountId);

    public sealed record AccountResponse(string Id, DateTimeOffset CreatedAt);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { error = "body required" });

        try
        {
            AccountIdValidator.Validate(request.AccountId);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            await _accounts.CreateAsync(request.AccountId, now, ct);
            return CreatedAtAction(
                nameof(Get),
                new { id = request.AccountId },
                new AccountResponse(request.AccountId, now));
        }
        catch (AccountAlreadyExistsException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        try
        {
            AccountIdValidator.Validate(id);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        var account = await _accounts.GetAsync(id, ct);
        if (account is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {id}");

        return Ok(new AccountResponse(account.Id, account.CreatedAt));
    }
}
```

- [ ] **Step 5: テストが通ることを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~AccountsControllerTests`
Expected: `Failed: 0, Passed: 9` (Post 正常 1 + 重複 1 + 無効 Theory 3 + Get 正常 1 + 未登録 1 + 無効 Theory 2)

- [ ] **Step 6: コミット**

```bash
git add src/Server/Controllers/AccountsController.cs tests/Server.Tests/Controllers/AccountsControllerTests.cs tests/Server.Tests/Server.Tests.csproj
git commit -m "feat(server): AccountsController (POST create / GET by id)"
```

---

### Task 13: `AudioSettingsController`

**目的:** `GET /api/v1/audio-settings` / `PUT /api/v1/audio-settings`。`X-Account-Id` ヘッダ必須。

**Files:**
- Create: `src/Server/Controllers/AudioSettingsController.cs`
- Create: `tests/Server.Tests/Controllers/AudioSettingsControllerTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
// tests/Server.Tests/Controllers/AudioSettingsControllerTests.cs
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class AudioSettingsControllerTests : IClassFixture<TempDataFactory>, IDisposable
{
    private readonly TempDataFactory _factory;

    public AudioSettingsControllerTests(TempDataFactory factory) => _factory = factory;

    public void Dispose() => _factory.ResetData();

    private sealed record AudioDto(int SchemaVersion, int Master, int Bgm, int Se, int Ambient);

    private async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
            res.EnsureSuccessStatusCode();
    }

    private static HttpClient WithAccount(HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        client.DefaultRequestHeaders.Add("X-Account-Id", id);
        return client;
    }

    [Fact]
    public async Task Get_MissingHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/audio-settings");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Get_AccountMissing_Returns404()
    {
        var client = WithAccount(_factory.CreateClient(), "ghost");
        var res = await client.GetAsync("/api/v1/audio-settings");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Get_NewAccount_ReturnsDefault()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "alice");
        WithAccount(client, "alice");

        var res = await client.GetAsync("/api/v1/audio-settings");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AudioDto>();
        Assert.NotNull(dto);
        Assert.Equal(80, dto!.Master);
        Assert.Equal(70, dto.Bgm);
        Assert.Equal(80, dto.Se);
        Assert.Equal(60, dto.Ambient);
    }

    [Fact]
    public async Task Put_ValidValues_Returns204_AndPersists()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "bob");
        WithAccount(client, "bob");

        var put = await client.PutAsJsonAsync("/api/v1/audio-settings",
            new AudioDto(1, Master: 10, Bgm: 20, Se: 30, Ambient: 40));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var got = await client.GetFromJsonAsync<AudioDto>("/api/v1/audio-settings");
        Assert.Equal(10, got!.Master);
    }

    [Fact]
    public async Task Put_OutOfRange_Returns400()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "carol");
        WithAccount(client, "carol");

        var res = await client.PutAsJsonAsync("/api/v1/audio-settings",
            new AudioDto(1, Master: 200, Bgm: 0, Se: 0, Ambient: 0));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Put_AccountMissing_Returns404()
    {
        var client = WithAccount(_factory.CreateClient(), "ghost");
        var res = await client.PutAsJsonAsync("/api/v1/audio-settings",
            new AudioDto(1, 50, 50, 50, 50));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~AudioSettingsControllerTests`
Expected: `Failed` (ルート未実装で 404 が返り続ける)

- [ ] **Step 3: `AudioSettingsController.cs` を実装**

```csharp
// src/Server/Controllers/AudioSettingsController.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Settings;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/audio-settings")]
public sealed class AudioSettingsController : ControllerBase
{
    public const string AccountHeader = "X-Account-Id";

    private readonly IAccountRepository _accounts;
    private readonly IAudioSettingsRepository _settings;

    public AudioSettingsController(IAccountRepository accounts, IAudioSettingsRepository settings)
    {
        _accounts = accounts;
        _settings = settings;
    }

    public sealed record AudioSettingsDto(int SchemaVersion, int Master, int Bgm, int Se, int Ambient);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _settings.GetOrDefaultAsync(accountId, ct);
        return Ok(new AudioSettingsDto(s.SchemaVersion, s.Master, s.Bgm, s.Se, s.Ambient));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] AudioSettingsDto dto, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (dto is null) return BadRequest(new { error = "body required" });
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        AudioSettings settings;
        try
        {
            settings = AudioSettings.Create(dto.Master, dto.Bgm, dto.Se, dto.Ambient);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        await _settings.UpsertAsync(accountId, settings, ct);
        return NoContent();
    }

    private bool TryGetAccountId(out string accountId, out IActionResult? error)
    {
        accountId = string.Empty;
        error = null;

        if (!Request.Headers.TryGetValue(AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            error = Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"ヘッダ {AccountHeader} が必要です。");
            return false;
        }

        var candidate = raw.ToString();
        try
        {
            AccountIdValidator.Validate(candidate);
        }
        catch (ArgumentException ex)
        {
            error = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            return false;
        }

        accountId = candidate;
        return true;
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~AudioSettingsControllerTests`
Expected: `Failed: 0, Passed: 6`

- [ ] **Step 5: コミット**

```bash
git add src/Server/Controllers/AudioSettingsController.cs tests/Server.Tests/Controllers/AudioSettingsControllerTests.cs
git commit -m "feat(server): AudioSettingsController (GET/PUT with X-Account-Id header)"
```

---

### Task 14: `RunsController`

**目的:** `GET /api/v1/runs/latest`。アカウント存在チェック + ラン未保存時 204。

**Files:**
- Create: `src/Server/Controllers/RunsController.cs`
- Create: `tests/Server.Tests/Controllers/RunsControllerTests.cs`

- [ ] **Step 1: 失敗テストを書く**

```csharp
// tests/Server.Tests/Controllers/RunsControllerTests.cs
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class RunsControllerTests : IClassFixture<TempDataFactory>, IDisposable
{
    private readonly TempDataFactory _factory;

    public RunsControllerTests(TempDataFactory factory) => _factory = factory;

    public void Dispose() => _factory.ResetData();

    private static HttpClient WithAccount(HttpClient client, string id)
    {
        client.DefaultRequestHeaders.Remove("X-Account-Id");
        client.DefaultRequestHeaders.Add("X-Account-Id", id);
        return client;
    }

    private async Task EnsureAccountAsync(HttpClient client, string id)
    {
        var res = await client.PostAsJsonAsync("/api/v1/accounts", new { accountId = id });
        if (res.StatusCode != HttpStatusCode.Created && res.StatusCode != HttpStatusCode.Conflict)
            res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_AccountMissing_Returns404()
    {
        var client = WithAccount(_factory.CreateClient(), "ghost");
        var res = await client.GetAsync("/api/v1/runs/latest");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Get_AccountExistsNoRun_Returns204()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "alice");
        WithAccount(client, "alice");

        var res = await client.GetAsync("/api/v1/runs/latest");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task Get_AccountWithSavedRun_Returns200WithState()
    {
        var client = _factory.CreateClient();
        await EnsureAccountAsync(client, "bob");

        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = EmbeddedDataLoader.LoadCatalog();
            var save = scope.ServiceProvider.GetRequiredService<ISaveRepository>();
            var run = RunState.NewSoloRun(catalog, rngSeed: 777UL, nowUtc: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
            await save.SaveAsync("bob", run, CancellationToken.None);
        }

        WithAccount(client, "bob");
        var res = await client.GetAsync("/api/v1/runs/latest");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"rngSeed\":777", body);
    }

    [Fact]
    public async Task Get_NoHeader_Returns400()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/runs/latest");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~RunsControllerTests`
Expected: `Failed`（ルート未実装）

- [ ] **Step 3: `RunsController.cs` を実装**

```csharp
// src/Server/Controllers/RunsController.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/runs")]
public sealed class RunsController : ControllerBase
{
    public const string AccountHeader = "X-Account-Id";

    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;

    public RunsController(IAccountRepository accounts, ISaveRepository saves)
    {
        _accounts = accounts;
        _saves = saves;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;

        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null) return NoContent();
        return Ok(state);
    }

    private bool TryGetAccountId(out string accountId, out IActionResult? error)
    {
        accountId = string.Empty;
        error = null;

        if (!Request.Headers.TryGetValue(AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            error = Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"ヘッダ {AccountHeader} が必要です。");
            return false;
        }

        var candidate = raw.ToString();
        try
        {
            AccountIdValidator.Validate(candidate);
        }
        catch (ArgumentException ex)
        {
            error = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            return false;
        }

        accountId = candidate;
        return true;
    }
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `dotnet test tests/Server.Tests/Server.Tests.csproj --filter FullyQualifiedName~RunsControllerTests`
Expected: `Failed: 0, Passed: 4`

- [ ] **Step 5: Server 全テスト回帰確認**

Run: `dotnet test`
Expected: `Failed: 0`

- [ ] **Step 6: コミット**

```bash
git add src/Server/Controllers/RunsController.cs tests/Server.Tests/Controllers/RunsControllerTests.cs
git commit -m "feat(server): RunsController GET /runs/latest"
```

---

## Group E — Client 基盤

### Task 15: Vitest 導入 + Vite proxy + テスト環境

**目的:** Client 側テスト基盤と Server プロキシを用意。UI タスクの前に全て揃える。

**Files:**
- Modify: `src/Client/package.json`
- Modify: `src/Client/vite.config.ts`
- Create: `src/Client/vitest.config.ts`
- Create: `src/Client/src/test/setup.ts`
- Modify: `src/Client/tsconfig.app.json`（`types` に `vitest/globals` を追加）

- [ ] **Step 1: devDependencies を追加**

作業ディレクトリ: `src/Client/`

Run: `cd src/Client && npm install -D vitest @vitest/ui @testing-library/react @testing-library/jest-dom jsdom @types/node`
Expected: インストール成功、`package.json` の `devDependencies` に 6 パッケージが追加される。

- [ ] **Step 2: `package.json` の `scripts` に `test` を追加**

`src/Client/package.json` の `scripts` セクションを次のようにする（既存 dev/build/preview はそのまま、`test` と `test:run` を追加）:

```json
"scripts": {
  "dev": "vite",
  "build": "tsc -b && vite build",
  "lint": "eslint .",
  "preview": "vite preview",
  "test": "vitest",
  "test:run": "vitest run"
}
```

- [ ] **Step 3: `src/Client/vite.config.ts` を書き換える**

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5114',
        changeOrigin: true,
      },
    },
  },
})
```

- [ ] **Step 4: `src/Client/vitest.config.ts` を作成**

```ts
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
```

- [ ] **Step 5: `src/Client/src/test/setup.ts` を作成**

```ts
import '@testing-library/jest-dom/vitest'
```

- [ ] **Step 6: `src/Client/tsconfig.app.json` の `compilerOptions.types` に vitest/globals を追加**

現行の `tsconfig.app.json` を開き、`compilerOptions` に `"types": ["vitest/globals"]` を追加（既存の `types` 指定がある場合は末尾に追記）。

- [ ] **Step 7: ダミーテストで動作確認**

`src/Client/src/test/smoke.test.ts` を作成:

```ts
import { describe, expect, it } from 'vitest'

describe('vitest smoke', () => {
  it('adds numbers', () => {
    expect(1 + 1).toBe(2)
  })
})
```

Run: `cd src/Client && npm run test:run`
Expected: `1 passed`

- [ ] **Step 8: スモークテストを削除（役目終わり）**

```bash
rm src/Client/src/test/smoke.test.ts
```

- [ ] **Step 9: ビルドが通ることを確認**

Run: `cd src/Client && npm run build`
Expected: エラーなくビルド成功。

- [ ] **Step 10: コミット**

```bash
git add src/Client/package.json src/Client/package-lock.json src/Client/vite.config.ts src/Client/vitest.config.ts src/Client/src/test/setup.ts src/Client/tsconfig.app.json
git commit -m "chore(client): install Vitest + testing-library, wire Vite dev proxy to Server :5114"
```

---

### Task 16: `api/client.ts` + `ApiError`

**目的:** すべての API 呼び出しが通る fetch ラッパと `ApiError` クラス。

**Files:**
- Create: `src/Client/src/api/client.ts`
- Create: `src/Client/src/api/client.test.ts`

- [ ] **Step 1: 失敗テストを書く**

```ts
// src/Client/src/api/client.test.ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiRequest } from './client'

describe('apiRequest', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('prefixes /api/v1 and returns parsed JSON', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ hello: 'world' }), { status: 200 }))
    const result = await apiRequest<{ hello: string }>('GET', '/ping')
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/ping', expect.objectContaining({ method: 'GET' }))
    expect(result).toEqual({ hello: 'world' })
  })

  it('adds X-Account-Id header when provided', async () => {
    fetchMock.mockResolvedValue(new Response('null', { status: 200 }))
    await apiRequest('GET', '/whatever', { accountId: 'alice' })
    const init = fetchMock.mock.calls[0][1] as RequestInit
    const headers = new Headers(init.headers)
    expect(headers.get('X-Account-Id')).toBe('alice')
  })

  it('omits X-Account-Id when not provided', async () => {
    fetchMock.mockResolvedValue(new Response('null', { status: 200 }))
    await apiRequest('GET', '/whatever')
    const init = fetchMock.mock.calls[0][1] as RequestInit
    const headers = new Headers(init.headers)
    expect(headers.get('X-Account-Id')).toBeNull()
  })

  it('serializes body as JSON with Content-Type', async () => {
    fetchMock.mockResolvedValue(new Response('null', { status: 200 }))
    await apiRequest('POST', '/create', { body: { accountId: 'x' } })
    const init = fetchMock.mock.calls[0][1] as RequestInit
    const headers = new Headers(init.headers)
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(init.body).toBe(JSON.stringify({ accountId: 'x' }))
  })

  it('returns undefined on 204 No Content', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    const result = await apiRequest('PUT', '/void')
    expect(result).toBeUndefined()
  })

  it('throws ApiError carrying status and body on non-2xx', async () => {
    fetchMock.mockResolvedValue(new Response('boom', { status: 409 }))
    await expect(apiRequest('POST', '/dup')).rejects.toMatchObject({
      status: 409,
      body: 'boom',
    })
  })

  it('ApiError is instance of Error', async () => {
    fetchMock.mockResolvedValue(new Response('x', { status: 500 }))
    try {
      await apiRequest('GET', '/')
      expect.fail('should have thrown')
    } catch (e) {
      expect(e).toBeInstanceOf(ApiError)
      expect(e).toBeInstanceOf(Error)
    }
  })
})
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `cd src/Client && npm run test:run -- --run api/client.test.ts`
Expected: モジュール解決エラー（`./client` が見つからない）。

- [ ] **Step 3: `client.ts` を実装**

```ts
// src/Client/src/api/client.ts
export class ApiError extends Error {
  readonly status: number
  readonly body: string

  constructor(status: number, body: string) {
    super(`HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

export type ApiRequestOptions = {
  accountId?: string
  body?: unknown
}

export async function apiRequest<T>(
  method: string,
  path: string,
  opts?: ApiRequestOptions,
): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (opts?.accountId) {
    headers['X-Account-Id'] = opts.accountId
  }

  const response = await fetch(`/api/v1${path}`, {
    method,
    headers,
    body: opts?.body !== undefined ? JSON.stringify(opts.body) : undefined,
  })

  if (!response.ok) {
    const text = await response.text()
    throw new ApiError(response.status, text)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `cd src/Client && npm run test:run -- --run api/client.test.ts`
Expected: `7 passed`

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/api/client.ts src/Client/src/api/client.test.ts
git commit -m "feat(client): add apiRequest wrapper and ApiError"
```

---

### Task 17: API モジュール群 + プリミティブコンポーネント

**目的:** `types.ts` / `accounts.ts` / `audioSettings.ts` / `runs.ts` と `components/Button.tsx` / `Slider.tsx`。UI タスクで必要になる部品をまとめて配置。

**Files:**
- Create: `src/Client/src/api/types.ts`
- Create: `src/Client/src/api/accounts.ts`
- Create: `src/Client/src/api/audioSettings.ts`
- Create: `src/Client/src/api/runs.ts`
- Create: `src/Client/src/components/Button.tsx`
- Create: `src/Client/src/components/Slider.tsx`

※ これらは薄いラッパ・プリミティブ UI なので、Unit テストは後続タスク（LoginScreen / SettingsScreen）に統合して書く。

- [ ] **Step 1: `types.ts` を作成**

```ts
// src/Client/src/api/types.ts
export type AccountDto = {
  id: string
  createdAt: string
}

export type AudioSettingsDto = {
  schemaVersion: number
  master: number
  bgm: number
  se: number
  ambient: number
}

export type RunStateDto = {
  schemaVersion: number
  currentAct: number
  currentTileIndex: number
  currentHp: number
  maxHp: number
  gold: number
  deck: string[]
  relics: string[]
  potions: string[]
  playSeconds: number
  rngSeed: number
  savedAtUtc: string
  progress: 'InProgress' | 'Completed' | 'Abandoned'
}
```

- [ ] **Step 2: `accounts.ts` を作成**

```ts
// src/Client/src/api/accounts.ts
import { apiRequest } from './client'
import type { AccountDto } from './types'

export function createAccount(accountId: string): Promise<AccountDto> {
  return apiRequest<AccountDto>('POST', '/accounts', { body: { accountId } })
}

export function getAccount(accountId: string): Promise<AccountDto> {
  return apiRequest<AccountDto>('GET', `/accounts/${encodeURIComponent(accountId)}`)
}
```

- [ ] **Step 3: `audioSettings.ts` を作成**

```ts
// src/Client/src/api/audioSettings.ts
import { apiRequest } from './client'
import type { AudioSettingsDto } from './types'

export function getAudioSettings(accountId: string): Promise<AudioSettingsDto> {
  return apiRequest<AudioSettingsDto>('GET', '/audio-settings', { accountId })
}

export function putAudioSettings(
  accountId: string,
  settings: AudioSettingsDto,
): Promise<void> {
  return apiRequest<void>('PUT', '/audio-settings', { accountId, body: settings })
}
```

- [ ] **Step 4: `runs.ts` を作成**

```ts
// src/Client/src/api/runs.ts
import { ApiError, apiRequest } from './client'
import type { RunStateDto } from './types'

export async function getLatestRun(accountId: string): Promise<RunStateDto | null> {
  try {
    const result = await apiRequest<RunStateDto | undefined>('GET', '/runs/latest', { accountId })
    return result ?? null
  } catch (err) {
    if (err instanceof ApiError && err.status === 204) {
      return null
    }
    throw err
  }
}
```

- [ ] **Step 5: `components/Button.tsx` を作成**

```tsx
// src/Client/src/components/Button.tsx
import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Props = ButtonHTMLAttributes<HTMLButtonElement> & {
  children: ReactNode
  variant?: 'primary' | 'secondary' | 'danger'
}

export function Button({ children, variant = 'primary', ...rest }: Props) {
  return (
    <button className={`btn btn--${variant}`} {...rest}>
      {children}
    </button>
  )
}
```

- [ ] **Step 6: `components/Slider.tsx` を作成**

```tsx
// src/Client/src/components/Slider.tsx
type Props = {
  label: string
  value: number
  onChange: (next: number) => void
  min?: number
  max?: number
}

export function Slider({ label, value, onChange, min = 0, max = 100 }: Props) {
  return (
    <label className="slider">
      <span className="slider__label">{label}</span>
      <input
        type="range"
        min={min}
        max={max}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
      />
      <span className="slider__value">{value}</span>
    </label>
  )
}
```

- [ ] **Step 7: 型チェックが通ることを確認**

Run: `cd src/Client && npx tsc -b`
Expected: エラーなし。

- [ ] **Step 8: コミット**

```bash
git add src/Client/src/api/types.ts src/Client/src/api/accounts.ts src/Client/src/api/audioSettings.ts src/Client/src/api/runs.ts src/Client/src/components/Button.tsx src/Client/src/components/Slider.tsx
git commit -m "feat(client): add typed API modules and Button/Slider primitives"
```

---

## Group F — Client 画面とステート

### Task 18: `AccountContext`

**目的:** `accountId` をグローバル状態として提供、`localStorage` を使うラッパ。

**Files:**
- Create: `src/Client/src/context/AccountContext.tsx`
- Create: `src/Client/src/context/AccountContext.test.tsx`

- [ ] **Step 1: 失敗テストを書く**

```tsx
// src/Client/src/context/AccountContext.test.tsx
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AccountProvider, useAccount } from './AccountContext'
import type { ReactNode } from 'react'

function wrapper({ children }: { children: ReactNode }) {
  return <AccountProvider>{children}</AccountProvider>
}

describe('AccountContext', () => {
  beforeEach(() => {
    localStorage.clear()
  })
  afterEach(() => {
    localStorage.clear()
  })

  it('starts with accountId = null when localStorage empty', () => {
    const { result } = renderHook(() => useAccount(), { wrapper })
    expect(result.current.accountId).toBeNull()
  })

  it('hydrates from localStorage on mount', () => {
    localStorage.setItem('rcg.accountId', 'alice')
    const { result } = renderHook(() => useAccount(), { wrapper })
    expect(result.current.accountId).toBe('alice')
  })

  it('login() sets state and writes localStorage', () => {
    const { result } = renderHook(() => useAccount(), { wrapper })
    act(() => {
      result.current.login('bob')
    })
    expect(result.current.accountId).toBe('bob')
    expect(localStorage.getItem('rcg.accountId')).toBe('bob')
  })

  it('logout() clears state and localStorage', () => {
    localStorage.setItem('rcg.accountId', 'carol')
    const { result } = renderHook(() => useAccount(), { wrapper })
    act(() => {
      result.current.logout()
    })
    expect(result.current.accountId).toBeNull()
    expect(localStorage.getItem('rcg.accountId')).toBeNull()
  })

  it('throws outside provider', () => {
    expect(() => renderHook(() => useAccount())).toThrow(/AccountProvider/)
  })
})
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `cd src/Client && npm run test:run -- --run context/AccountContext.test.tsx`
Expected: モジュール解決エラー。

- [ ] **Step 3: `AccountContext.tsx` を実装**

```tsx
// src/Client/src/context/AccountContext.tsx
import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'

const STORAGE_KEY = 'rcg.accountId'

type AccountContextValue = {
  accountId: string | null
  login: (id: string) => void
  logout: () => void
}

const AccountContext = createContext<AccountContextValue | null>(null)

export function AccountProvider({ children }: { children: ReactNode }) {
  const [accountId, setAccountId] = useState<string | null>(null)

  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) setAccountId(stored)
  }, [])

  const login = useCallback((id: string) => {
    localStorage.setItem(STORAGE_KEY, id)
    setAccountId(id)
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY)
    setAccountId(null)
  }, [])

  return (
    <AccountContext.Provider value={{ accountId, login, logout }}>
      {children}
    </AccountContext.Provider>
  )
}

export function useAccount(): AccountContextValue {
  const ctx = useContext(AccountContext)
  if (!ctx) throw new Error('useAccount must be used within AccountProvider')
  return ctx
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `cd src/Client && npm run test:run -- --run context/AccountContext.test.tsx`
Expected: `5 passed`

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/context/AccountContext.tsx src/Client/src/context/AccountContext.test.tsx
git commit -m "feat(client): add AccountContext with localStorage hydration"
```

---

### Task 19: `LoginScreen`

**目的:** 2 タブ（新規作成／既存 ID）のログイン画面。成功で `login()` を呼ぶ。

**Files:**
- Create: `src/Client/src/screens/LoginScreen.tsx`
- Create: `src/Client/src/screens/LoginScreen.test.tsx`

- [ ] **Step 1: 失敗テストを書く**

```tsx
// src/Client/src/screens/LoginScreen.test.tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountProvider } from '../context/AccountContext'
import { LoginScreen } from './LoginScreen'

const onLoggedIn = vi.fn()

function renderScreen() {
  return render(
    <AccountProvider>
      <LoginScreen onLoggedIn={onLoggedIn} />
    </AccountProvider>,
  )
}

describe('LoginScreen', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    localStorage.clear()
    onLoggedIn.mockClear()
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders both tabs', () => {
    renderScreen()
    expect(screen.getByRole('tab', { name: '新規作成' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: '既存 ID で続行' })).toBeInTheDocument()
  })

  it('rejects empty id before sending request', () => {
    renderScreen()
    fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(screen.getByText(/入力してください/i)).toBeInTheDocument()
  })

  it('creates new account on success', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ id: 'alice', createdAt: '2026-04-20T12:00:00Z' }), { status: 201 }),
    )
    renderScreen()
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'alice' } })
    fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }))

    await waitFor(() => expect(onLoggedIn).toHaveBeenCalledWith('alice'))
    expect(localStorage.getItem('rcg.accountId')).toBe('alice')
  })

  it('shows conflict message on 409', async () => {
    fetchMock.mockResolvedValue(new Response('dup', { status: 409 }))
    renderScreen()
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'dup' } })
    fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }))

    await waitFor(() =>
      expect(screen.getByText(/すでに使われています/)).toBeInTheDocument(),
    )
    expect(onLoggedIn).not.toHaveBeenCalled()
  })

  it('logs in existing account when GET returns 200', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ id: 'bob', createdAt: '2026-04-20T12:00:00Z' }), { status: 200 }),
    )
    renderScreen()
    fireEvent.click(screen.getByRole('tab', { name: '既存 ID で続行' }))
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'bob' } })
    fireEvent.click(screen.getByRole('button', { name: 'ログイン' }))

    await waitFor(() => expect(onLoggedIn).toHaveBeenCalledWith('bob'))
  })

  it('shows not-found message on 404 existing flow', async () => {
    fetchMock.mockResolvedValue(new Response('missing', { status: 404 }))
    renderScreen()
    fireEvent.click(screen.getByRole('tab', { name: '既存 ID で続行' }))
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'ghost' } })
    fireEvent.click(screen.getByRole('button', { name: 'ログイン' }))

    await waitFor(() =>
      expect(screen.getByText(/登録されていません/)).toBeInTheDocument(),
    )
  })

  it('rejects id with slash in client-side validation', () => {
    renderScreen()
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'bad/id' } })
    fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(screen.getByText(/使用できない文字/)).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `cd src/Client && npm run test:run -- --run screens/LoginScreen.test.tsx`
Expected: モジュール解決エラー。

- [ ] **Step 3: `LoginScreen.tsx` を実装**

```tsx
// src/Client/src/screens/LoginScreen.tsx
import { useState } from 'react'
import { createAccount, getAccount } from '../api/accounts'
import { ApiError } from '../api/client'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'

type Tab = 'new' | 'existing'

type Props = {
  onLoggedIn: (accountId: string) => void
}

const ID_PATTERN = /^[^/\\]{1,32}$/

function validateClientSide(id: string): string | null {
  if (!id.trim()) return 'アカウント ID を入力してください。'
  if (id.length > 32) return 'アカウント ID は 32 文字以内で入力してください。'
  if (!ID_PATTERN.test(id)) return 'アカウント ID に使用できない文字が含まれています。'
  return null
}

export function LoginScreen({ onLoggedIn }: Props) {
  const { login } = useAccount()
  const [tab, setTab] = useState<Tab>('new')
  const [id, setId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function handleSubmit() {
    setError(null)
    const clientError = validateClientSide(id)
    if (clientError) {
      setError(clientError)
      return
    }

    setPending(true)
    try {
      if (tab === 'new') {
        await createAccount(id)
      } else {
        await getAccount(id)
      }
      login(id)
      onLoggedIn(id)
    } catch (e) {
      if (e instanceof ApiError) {
        if (tab === 'new' && e.status === 409)
          setError('その ID はすでに使われています。')
        else if (tab === 'existing' && e.status === 404)
          setError('その ID は登録されていません。')
        else setError(`エラーが発生しました (HTTP ${e.status})`)
      } else {
        setError('ネットワークエラーが発生しました。')
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <main className="login-screen">
      <h1>Roguelike Card Game</h1>
      <div role="tablist" className="login-tabs">
        <button
          role="tab"
          aria-selected={tab === 'new'}
          onClick={() => { setTab('new'); setError(null) }}
        >
          新規作成
        </button>
        <button
          role="tab"
          aria-selected={tab === 'existing'}
          onClick={() => { setTab('existing'); setError(null) }}
        >
          既存 ID で続行
        </button>
      </div>
      <label>
        アカウント ID
        <input
          type="text"
          value={id}
          onChange={(e) => setId(e.target.value)}
          maxLength={32}
          disabled={pending}
        />
      </label>
      {error && <p role="alert" className="login-error">{error}</p>}
      <Button onClick={handleSubmit} disabled={pending}>
        {tab === 'new' ? 'アカウント作成' : 'ログイン'}
      </Button>
    </main>
  )
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `cd src/Client && npm run test:run -- --run screens/LoginScreen.test.tsx`
Expected: `7 passed`

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/screens/LoginScreen.tsx src/Client/src/screens/LoginScreen.test.tsx
git commit -m "feat(client): LoginScreen with 2-tab new/existing flow"
```

---

### Task 20: `useAudioSettings` フック

**目的:** 設定画面用の取得・楽観更新・500ms デバウンス PUT をまとめた hook。

**Files:**
- Create: `src/Client/src/hooks/useAudioSettings.ts`
- Create: `src/Client/src/hooks/useAudioSettings.test.ts`

- [ ] **Step 1: 失敗テストを書く**

```ts
// src/Client/src/hooks/useAudioSettings.test.ts
import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAudioSettings } from './useAudioSettings'

const DEFAULT_SETTINGS = {
  schemaVersion: 1,
  master: 80,
  bgm: 70,
  se: 80,
  ambient: 60,
}

describe('useAudioSettings', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('fetches settings on mount', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(DEFAULT_SETTINGS), { status: 200 }))
    const { result } = renderHook(() => useAudioSettings('alice'))

    await waitFor(() => expect(result.current.settings).not.toBeNull())
    expect(result.current.settings?.master).toBe(80)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/audio-settings',
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('update() applies optimistic change and PUTs after 500ms', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(DEFAULT_SETTINGS), { status: 200 }),
    )
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    const { result } = renderHook(() => useAudioSettings('alice'))
    await waitFor(() => expect(result.current.settings).not.toBeNull())

    act(() => {
      result.current.update({ master: 10 })
    })
    expect(result.current.settings?.master).toBe(10)
    expect(fetchMock).toHaveBeenCalledTimes(1)

    await act(async () => {
      vi.advanceTimersByTime(500)
    })

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const putCall = fetchMock.mock.calls[1][1] as RequestInit
    expect(putCall.method).toBe('PUT')
  })

  it('debounces multiple rapid updates into single PUT', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(DEFAULT_SETTINGS), { status: 200 }),
    )
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    const { result } = renderHook(() => useAudioSettings('alice'))
    await waitFor(() => expect(result.current.settings).not.toBeNull())

    act(() => {
      result.current.update({ master: 10 })
    })
    act(() => {
      vi.advanceTimersByTime(200)
    })
    act(() => {
      result.current.update({ master: 20 })
    })
    act(() => {
      vi.advanceTimersByTime(200)
    })
    act(() => {
      result.current.update({ master: 30 })
    })
    await act(async () => {
      vi.advanceTimersByTime(500)
    })

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const putBody = JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string)
    expect(putBody.master).toBe(30)
  })

  it('sets saveStatus to error when PUT fails', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(DEFAULT_SETTINGS), { status: 200 }),
    )
    fetchMock.mockResolvedValueOnce(new Response('boom', { status: 500 }))

    const { result } = renderHook(() => useAudioSettings('alice'))
    await waitFor(() => expect(result.current.settings).not.toBeNull())

    act(() => {
      result.current.update({ master: 10 })
    })
    await act(async () => {
      vi.advanceTimersByTime(500)
    })

    await waitFor(() => expect(result.current.saveStatus).toBe('error'))
  })
})
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `cd src/Client && npm run test:run -- --run hooks/useAudioSettings.test.ts`
Expected: モジュール解決エラー。

- [ ] **Step 3: `useAudioSettings.ts` を実装**

```ts
// src/Client/src/hooks/useAudioSettings.ts
import { useCallback, useEffect, useRef, useState } from 'react'
import { getAudioSettings, putAudioSettings } from '../api/audioSettings'
import type { AudioSettingsDto } from '../api/types'

type SaveStatus = 'idle' | 'saving' | 'saved' | 'error'

const DEBOUNCE_MS = 500

export function useAudioSettings(accountId: string): {
  settings: AudioSettingsDto | null
  update: (patch: Partial<Omit<AudioSettingsDto, 'schemaVersion'>>) => void
  saveStatus: SaveStatus
} {
  const [settings, setSettings] = useState<AudioSettingsDto | null>(null)
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle')
  const timerRef = useRef<number | null>(null)
  const pendingRef = useRef<AudioSettingsDto | null>(null)

  useEffect(() => {
    let cancelled = false
    getAudioSettings(accountId)
      .then((s) => { if (!cancelled) setSettings(s) })
      .catch(() => { if (!cancelled) setSaveStatus('error') })
    return () => { cancelled = true }
  }, [accountId])

  const flush = useCallback(async () => {
    const next = pendingRef.current
    if (!next) return
    pendingRef.current = null
    setSaveStatus('saving')
    try {
      await putAudioSettings(accountId, next)
      setSaveStatus('saved')
    } catch {
      setSaveStatus('error')
    }
  }, [accountId])

  const update = useCallback(
    (patch: Partial<Omit<AudioSettingsDto, 'schemaVersion'>>) => {
      setSettings((prev) => {
        if (!prev) return prev
        const next = { ...prev, ...patch }
        pendingRef.current = next
        if (timerRef.current !== null) window.clearTimeout(timerRef.current)
        timerRef.current = window.setTimeout(() => { void flush() }, DEBOUNCE_MS)
        return next
      })
    },
    [flush],
  )

  useEffect(() => {
    return () => {
      if (timerRef.current !== null) window.clearTimeout(timerRef.current)
    }
  }, [])

  return { settings, update, saveStatus }
}
```

- [ ] **Step 4: テストが通ることを確認**

Run: `cd src/Client && npm run test:run -- --run hooks/useAudioSettings.test.ts`
Expected: `4 passed`

- [ ] **Step 5: コミット**

```bash
git add src/Client/src/hooks/useAudioSettings.ts src/Client/src/hooks/useAudioSettings.test.ts
git commit -m "feat(client): add useAudioSettings hook with 500ms debounce"
```

---

### Task 21: `MainMenuScreen` + `SettingsScreen`

**目的:** 2 画面のレンダリングと画面内のコールバックを配線。「続きから」判定のための runs/latest 呼び出し込み。

**Files:**
- Create: `src/Client/src/screens/MainMenuScreen.tsx`
- Create: `src/Client/src/screens/SettingsScreen.tsx`
- Create: `src/Client/src/screens/MainMenuScreen.test.tsx`

- [ ] **Step 1: `MainMenuScreen.test.tsx` を書く（失敗テスト）**

```tsx
// src/Client/src/screens/MainMenuScreen.test.tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountProvider } from '../context/AccountContext'
import { MainMenuScreen } from './MainMenuScreen'

function renderScreen(handlers: { onOpenSettings?: () => void; onLogout?: () => void } = {}) {
  localStorage.setItem('rcg.accountId', 'alice')
  return render(
    <AccountProvider>
      <MainMenuScreen
        onOpenSettings={handlers.onOpenSettings ?? (() => {})}
        onLogout={handlers.onLogout ?? (() => {})}
      />
    </AccountProvider>,
  )
}

describe('MainMenuScreen', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    localStorage.clear()
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('renders 5 menu buttons and current account id', async () => {
    renderScreen()
    expect(screen.getByText('alice')).toBeInTheDocument()
    for (const label of ['シングルプレイ', 'マルチプレイ', '設定', '実績', '終了']) {
      expect(screen.getByRole('button', { name: label })).toBeInTheDocument()
    }
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
  })

  it('calls onOpenSettings when settings button clicked', () => {
    const onOpenSettings = vi.fn()
    renderScreen({ onOpenSettings })
    fireEvent.click(screen.getByRole('button', { name: '設定' }))
    expect(onOpenSettings).toHaveBeenCalled()
  })

  it('shows coming-soon dialog for multiplayer / achievements', async () => {
    renderScreen()
    fireEvent.click(screen.getByRole('button', { name: 'マルチプレイ' }))
    expect(await screen.findByText(/準備中/)).toBeInTheDocument()
  })

  it('calls onLogout from logout button', () => {
    const onLogout = vi.fn()
    renderScreen({ onLogout })
    fireEvent.click(screen.getByRole('button', { name: 'ログアウト' }))
    expect(onLogout).toHaveBeenCalled()
  })
})
```

- [ ] **Step 2: テストが失敗することを確認**

Run: `cd src/Client && npm run test:run -- --run screens/MainMenuScreen.test.tsx`
Expected: モジュール解決エラー。

- [ ] **Step 3: `MainMenuScreen.tsx` を実装**

```tsx
// src/Client/src/screens/MainMenuScreen.tsx
import { useEffect, useState } from 'react'
import { getLatestRun } from '../api/runs'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'

type Props = {
  onOpenSettings: () => void
  onLogout: () => void
}

type ComingSoonKind = 'single' | 'multi' | 'achievements' | 'quit' | null

export function MainMenuScreen({ onOpenSettings, onLogout }: Props) {
  const { accountId } = useAccount()
  const [hasRun, setHasRun] = useState<boolean>(false)
  const [dialog, setDialog] = useState<ComingSoonKind>(null)

  useEffect(() => {
    if (!accountId) return
    let cancelled = false
    getLatestRun(accountId)
      .then((run) => { if (!cancelled) setHasRun(run !== null) })
      .catch(() => { /* ignore: UI に hasRun=false のまま */ })
    return () => { cancelled = true }
  }, [accountId])

  function showSoon(kind: ComingSoonKind) {
    setDialog(kind)
  }

  return (
    <main className="main-menu">
      <header className="main-menu__header">
        <span className="main-menu__account">{accountId}</span>
        <button className="btn btn--secondary" onClick={onLogout}>ログアウト</button>
      </header>

      <nav className="main-menu__buttons">
        <Button onClick={() => showSoon('single')}>シングルプレイ</Button>
        <Button onClick={() => showSoon('multi')}>マルチプレイ</Button>
        <Button onClick={onOpenSettings}>設定</Button>
        <Button onClick={() => showSoon('achievements')}>実績</Button>
        <Button variant="danger" onClick={() => showSoon('quit')}>終了</Button>
      </nav>

      {hasRun && <p className="main-menu__badge">保存済みラン有り</p>}

      {dialog && (
        <div role="dialog" aria-label="準備中" className="main-menu__dialog">
          <p>準備中です。</p>
          {dialog === 'quit' && <p>このタブを閉じてください。</p>}
          <Button variant="secondary" onClick={() => setDialog(null)}>閉じる</Button>
        </div>
      )}
    </main>
  )
}
```

- [ ] **Step 4: `SettingsScreen.tsx` を実装（テストは useAudioSettings で担保済みなので追加 unit test は不要）**

```tsx
// src/Client/src/screens/SettingsScreen.tsx
import { Button } from '../components/Button'
import { Slider } from '../components/Slider'
import { useAccount } from '../context/AccountContext'
import { useAudioSettings } from '../hooks/useAudioSettings'

type Props = {
  onBack: () => void
}

export function SettingsScreen({ onBack }: Props) {
  const { accountId } = useAccount()
  if (!accountId) {
    return (
      <main className="settings-screen">
        <p>ログインが必要です。</p>
        <Button onClick={onBack}>戻る</Button>
      </main>
    )
  }
  const { settings, update, saveStatus } = useAudioSettings(accountId)

  return (
    <main className="settings-screen">
      <header><h2>設定</h2></header>
      {settings === null ? (
        <p>読み込み中…</p>
      ) : (
        <div className="settings-screen__sliders">
          <Slider label="Master" value={settings.master} onChange={(v) => update({ master: v })} />
          <Slider label="BGM" value={settings.bgm} onChange={(v) => update({ bgm: v })} />
          <Slider label="SE" value={settings.se} onChange={(v) => update({ se: v })} />
          <Slider label="Ambient" value={settings.ambient} onChange={(v) => update({ ambient: v })} />
        </div>
      )}
      <footer className="settings-screen__footer">
        <span aria-live="polite">
          {saveStatus === 'saving' && '保存中…'}
          {saveStatus === 'saved' && '保存済み ✓'}
          {saveStatus === 'error' && '保存に失敗しました'}
        </span>
        <Button onClick={onBack}>メニューへ戻る</Button>
      </footer>
    </main>
  )
}
```

- [ ] **Step 5: `MainMenuScreen` テストが通ることを確認**

Run: `cd src/Client && npm run test:run -- --run screens/MainMenuScreen.test.tsx`
Expected: `4 passed`

- [ ] **Step 6: Client 全テストが通ることを確認**

Run: `cd src/Client && npm run test:run`
Expected: 全テスト `passed`。

- [ ] **Step 7: コミット**

```bash
git add src/Client/src/screens/MainMenuScreen.tsx src/Client/src/screens/MainMenuScreen.test.tsx src/Client/src/screens/SettingsScreen.tsx
git commit -m "feat(client): MainMenuScreen and SettingsScreen"
```

---

### Task 22: `App.tsx` 画面ルーティング + `main.tsx` ラップ + CSS + 手動確認 + `phase2-complete` タグ

**目的:** 画面遷移の土管と起動時の `localStorage` ブートストラップを配線し、手動確認を通して Phase 2 を締める。

**Files:**
- Modify: `src/Client/src/App.tsx`
- Modify: `src/Client/src/main.tsx`
- Modify: `src/Client/src/App.css`
- Modify: `src/Client/src/index.css`

- [ ] **Step 1: `App.tsx` を書き換える**

```tsx
// src/Client/src/App.tsx
import { useEffect, useState } from 'react'
import { getAccount } from './api/accounts'
import { ApiError } from './api/client'
import { Button } from './components/Button'
import { useAccount } from './context/AccountContext'
import { LoginScreen } from './screens/LoginScreen'
import { MainMenuScreen } from './screens/MainMenuScreen'
import { SettingsScreen } from './screens/SettingsScreen'

type Screen =
  | { kind: 'bootstrapping' }
  | { kind: 'login' }
  | { kind: 'main-menu' }
  | { kind: 'settings' }
  | { kind: 'bootstrap-error'; message: string }

export default function App() {
  const { accountId, logout } = useAccount()
  const [screen, setScreen] = useState<Screen>({ kind: 'bootstrapping' })

  useEffect(() => {
    let cancelled = false
    async function bootstrap() {
      if (!accountId) {
        if (!cancelled) setScreen({ kind: 'login' })
        return
      }
      try {
        await getAccount(accountId)
        if (!cancelled) setScreen({ kind: 'main-menu' })
      } catch (e) {
        if (cancelled) return
        if (e instanceof ApiError && e.status === 404) {
          logout()
          setScreen({ kind: 'login' })
        } else {
          setScreen({ kind: 'bootstrap-error', message: 'サーバに接続できませんでした。' })
        }
      }
    }
    void bootstrap()
    return () => { cancelled = true }
  }, [accountId, logout])

  if (screen.kind === 'bootstrapping') {
    return <main className="bootstrap"><p>起動中…</p></main>
  }
  if (screen.kind === 'bootstrap-error') {
    return (
      <main className="bootstrap-error">
        <p>{screen.message}</p>
        <Button onClick={() => setScreen({ kind: 'bootstrapping' })}>再試行</Button>
      </main>
    )
  }
  if (screen.kind === 'login') {
    return <LoginScreen onLoggedIn={() => setScreen({ kind: 'main-menu' })} />
  }
  if (screen.kind === 'main-menu') {
    return (
      <MainMenuScreen
        onOpenSettings={() => setScreen({ kind: 'settings' })}
        onLogout={() => { logout(); setScreen({ kind: 'login' }) }}
      />
    )
  }
  return <SettingsScreen onBack={() => setScreen({ kind: 'main-menu' })} />
}
```

- [ ] **Step 2: `main.tsx` を `AccountProvider` でラップ**

```tsx
// src/Client/src/main.tsx
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { AccountProvider } from './context/AccountContext'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AccountProvider>
      <App />
    </AccountProvider>
  </StrictMode>,
)
```

- [ ] **Step 3: `App.css` をダークテーマの最小スタイルに置き換える**

```css
/* src/Client/src/App.css */
:root {
  color-scheme: dark;
  --bg: #14171a;
  --fg: #e8ecef;
  --accent: #7aa2f7;
  --muted: #6b7280;
  --danger: #e06c75;
  font-family: system-ui, sans-serif;
}

body {
  margin: 0;
  background: var(--bg);
  color: var(--fg);
  min-height: 100vh;
}

main {
  max-width: 720px;
  margin: 0 auto;
  padding: 2rem 1rem;
}

.btn {
  display: inline-block;
  padding: 0.6rem 1.2rem;
  border: 1px solid var(--accent);
  background: transparent;
  color: var(--fg);
  border-radius: 0.4rem;
  cursor: pointer;
  font-size: 1rem;
}
.btn:hover { background: rgba(122, 162, 247, 0.1); }
.btn:disabled { opacity: 0.5; cursor: not-allowed; }
.btn--secondary { border-color: var(--muted); }
.btn--danger { border-color: var(--danger); color: var(--danger); }

.login-screen h1 { text-align: center; }
.login-tabs { display: flex; gap: 0.5rem; margin: 1rem 0; }
.login-tabs button {
  flex: 1; padding: 0.5rem; background: transparent;
  color: var(--fg); border: 1px solid var(--muted); border-radius: 0.4rem; cursor: pointer;
}
.login-tabs button[aria-selected='true'] { border-color: var(--accent); }
.login-screen label { display: block; margin: 1rem 0 0.25rem; }
.login-screen input {
  width: 100%; padding: 0.5rem; background: #1f2429; color: var(--fg);
  border: 1px solid var(--muted); border-radius: 0.3rem;
}
.login-error { color: var(--danger); margin: 0.5rem 0; }

.main-menu__header { display: flex; justify-content: space-between; margin-bottom: 1.5rem; }
.main-menu__account { font-weight: bold; }
.main-menu__buttons { display: grid; grid-template-columns: 1fr; gap: 0.75rem; max-width: 320px; margin: 0 auto; }
.main-menu__badge { text-align: center; margin-top: 1rem; color: var(--accent); }
.main-menu__dialog {
  margin-top: 1.5rem; padding: 1rem; background: #1f2429;
  border: 1px solid var(--muted); border-radius: 0.4rem;
}

.settings-screen__sliders { display: grid; gap: 1rem; margin: 1.5rem 0; }
.slider { display: grid; grid-template-columns: 6rem 1fr 3rem; align-items: center; gap: 1rem; }
.slider__value { text-align: right; color: var(--accent); }

.settings-screen__footer { display: flex; justify-content: space-between; align-items: center; }

.bootstrap, .bootstrap-error { text-align: center; padding-top: 4rem; }
```

- [ ] **Step 4: `index.css` を整理（空にするか最小リセットのみ）**

```css
/* src/Client/src/index.css */
* { box-sizing: border-box; }
```

そして `main.tsx` の `import './index.css'` を `import './index.css'; import './App.css'` に拡張:

```tsx
import './index.css'
import './App.css'
```

（既に `App.tsx` から `App.css` を import している場合は不要。上記は main.tsx から両方取り込む形で統一する。）

- [ ] **Step 5: 型チェック・ビルド・テスト全件クリアを確認**

Run: `cd src/Client && npm run build`
Expected: エラーなく `dist/` に成果物が出る。

Run: `cd src/Client && npm run test:run`
Expected: 全テスト passed。

Run: `dotnet build -warnaserror`
Expected: `0 Error(s) 0 Warning(s)` 相当。

Run: `dotnet test`
Expected: `Failed: 0`

- [ ] **Step 6: 手動動作確認を実施**

別ターミナルで Server 起動: `dotnet run --project src/Server/Server.csproj`
別ターミナルで Client 起動: `cd src/Client && npm run dev`

ブラウザで `http://localhost:5173` を開き、仕様書 §6.4 の 10 項目を順に確認:

1. [ ] ログイン画面 → 新規作成タブ → ID 入力 → メインメニュー遷移
2. [ ] 同じ ID で再度新規作成 → `409` メッセージ
3. [ ] 既存タブに未登録 ID → `404` メッセージ
4. [ ] 設定画面 → 4 スライダ → リロードで値維持
5. [ ] 別ブラウザ（シークレット等）で同 ID ログイン → 同じ値が見える
6. [ ] 「続きから」バッジは Phase 2 では非表示（ラン未保存）
7. [ ] 「ログアウト」でログイン画面に戻り `localStorage` がクリアされている
8. [ ] 「終了」で「準備中・このタブを閉じてください」表示
9. [ ] Server 停止中のリロード → ネットワークエラー画面 + 再試行ボタン
10. [ ] 停止した Server を起動後、再試行ボタンでメインメニューまで復帰できる

すべてパスすれば次へ。問題があれば該当タスクに戻って修正 → 再確認。

- [ ] **Step 7: コミット**

```bash
git add src/Client/src/App.tsx src/Client/src/main.tsx src/Client/src/App.css src/Client/src/index.css
git commit -m "feat(client): wire screen routing, localStorage bootstrap, dark theme styles"
```

- [ ] **Step 8: `phase2-complete` タグを打つ**

```bash
git tag -a phase2-complete -m "Phase 2: menu / login / settings UI + server API"
git log --oneline -n 1
```

Expected: 現在の HEAD にタグが付く。`git tag --list phase2-complete` で確認。

---

## 実装上の注意

1. **1 タスク 1 コミット主義**: 各タスク末尾のコミットは必ず単体で緑（`dotnet build -warnaserror` / `dotnet test` / `npm run test:run` 全通過）であること。
2. **Core に Server 依存を足さない**: Task 2/3 で `using Microsoft.*` や `using System.Threading.Tasks` を Core に入れない（テストコードは OK）。
3. **Postgres への移行互換性**: リポジトリ interface 側のシグネチャを変えずに FileBacked を差し替えできれば合格。interface に `ValueTask` や実装依存の型を混ぜない。
4. **VRChat 移植コメント**: Core 層の新規ファイル（Task 1/2/3）は XML doc に `<remarks>VRChat (Udon#) 移植時は …</remarks>` を必ず含める（Task 1–3 の実装コードには既に入っている）。
5. **appsettings の秘匿情報**: 現状機密なし。将来 OAuth 導入時は `appsettings.json` に実秘密を置かず環境変数化。
6. **Windows / Unix パス**: `Path.Combine` に統一。`/` 直書き禁止。

---

## 完了判定チェックリスト

- [ ] Group A 完了（JsonOptions / AudioSettings / Serializer）
- [ ] Group B 完了（Program 骨格 / AccountIdValidator / DataStorageOptions / Abstractions）
- [ ] Group C 完了（3 File repositories + SaveRepository 移行）
- [ ] Group D 完了（4 controllers、全件 integration test 緑）
- [ ] Group E 完了（Vitest 設定・apiRequest・API module・primitive components）
- [ ] Group F 完了（AccountContext・LoginScreen・useAudioSettings・MainMenu/Settings・App ルーティング）
- [ ] `dotnet build -warnaserror` エラー 0 / 警告 0
- [ ] `dotnet test` 失敗 0
- [ ] `cd src/Client && npm run build` エラー 0
- [ ] `cd src/Client && npm run test:run` 失敗 0
- [ ] 手動確認 10 項目すべてパス
- [ ] `phase2-complete` タグが現 HEAD に存在
