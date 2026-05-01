# Phase 10.5.H — Versioned JSON Schema 移行 + Override Merge 実装

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Card JSON を `{ id, name, displayName, activeVersion, versions[] }` 形式に移行する。CardJsonLoader が **flat (旧) と versioned (新) 両形式に対応**することで段階的に移行可能。一括移行スクリプトで全 35 card JSON を変換。Server 側で **DEV モード時のみ** `data-local/dev-overrides/cards/*.json` を読んで base と version-merge する仕組みを追加。

**Architecture:** Core 側は純関数のまま (file I/O なし)。`CardJsonLoader.Parse(json)` を versioned 検出 (`versions` キーあり) で分岐。`CardOverrideMerger.Merge(baseJson, overrideJson) → mergedJson` 純関数を追加。Server 側 (`EmbeddedDataLoader` 〜 `DataCatalog.LoadFromStrings` 間に挟む形で) DEV 環境のみ disk から override JSON を読み、merger で union → 結果を LoadFromStrings に渡す。

**Tech Stack:** C# .NET 10 + System.Text.Json、xUnit、PowerShell or .csx (移行スクリプト用)。Client 変更なし。

**Spec:** `docs/superpowers/specs/2026-05-01-phase10-5-design.md` §3, §4

**スコープ外:**
- relic / potion / enemy / unit JSON の versioned 化 → 10.5.L
- FileSystemWatcher による hot reload → 10.5.I / 10.5.J で dev menu 経由で実装
- Override の "promote to source" UI → 10.5.J

---

## Versioned JSON Schema (確定)

```json
{
  "id": "strike",
  "name": "ストライク",
  "displayName": null,
  "activeVersion": "v1",
  "versions": [
    {
      "version": "v1",
      "createdAt": "2026-05-01T00:00:00Z",
      "label": "original",
      "spec": {
        "rarity": 1,
        "cardType": "Attack",
        "cost": 1,
        "upgradedCost": null,
        "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 6 }],
        "upgradedEffects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 9 }],
        "description": null,
        "upgradedDescription": null,
        "keywords": null,
        "upgradedKeywords": null
      }
    }
  ]
}
```

トップ階層: `id`, `name`, `displayName`, `activeVersion`, `versions[]`
各 version: `version` (識別子)、`createdAt` (ISO8601)、`label` (任意)、`spec` (旧 flat JSON の rarity/cost/effects/etc 一式)

---

## File Structure

| ファイル | 種別 | 責務 |
|---|---|---|
| `src/Core/Cards/CardJsonLoader.cs` | Modify | versioned/flat 両対応に拡張 |
| `src/Core/Cards/CardOverrideMerger.cs` | Create | 純関数: baseJson + overrideJson → mergedJson (versions union, activeVersion 上書き) |
| `tests/Core.Tests/Cards/CardJsonLoaderTests.cs` | Modify | versioned 形式読込テスト追加 (flat 既存テストは継続緑) |
| `tests/Core.Tests/Cards/CardOverrideMergerTests.cs` | Create | merge ルールの単体テスト |
| `src/Core/Data/Cards/*.json` | Modify (35 ファイル) | 移行スクリプトで一括 versioned 化 |
| `tools/migrate-cards-to-versioned.csx` (or .ps1) | Create | 一括移行スクリプト (一回限り実行) |
| `src/Server/Services/DevOverrideLoader.cs` | Create | DEV モード限定で `data-local/dev-overrides/cards/` を読んで merger に流す |
| `src/Server/Program.cs` | Modify | `AddSingleton<DataCatalog>` で env が Development なら DevOverrideLoader 経由 |
| `tests/Server.Tests/Services/DevOverrideLoaderTests.cs` | Create | 一時ディレクトリで override 読込検証 |
| `.gitignore` | Modify | `data-local/dev-overrides/` を追加 |
| `data-local/dev-overrides/cards/.gitkeep` | Create | dir 作成のため空 placeholder (gitignored で .gitkeep 自体も無視) — ※実際は不要、未存在で OK |

---

## Conventions

- **TDD strictly.**
- **Build clean.**
- **後方互換維持.** 旧 flat JSON 形式は引き続き Parse できること (既存 35 card JSON が移行前の状態でも全テスト緑)。移行スクリプト実行後も flat → versioned 変換結果が同じ CardDefinition を生成することを spot check。
- **Core は file I/O 禁止.** 全 disk 読み込みは Server 側で行い、Core には string で渡す。
- **DEV ガード.** override 読込は `IWebHostEnvironment.IsDevelopment()` でのみ実行。本番では override dir が存在しないし、存在しても読まない。

---

## Task 1: CardJsonLoader を versioned/flat 両対応に拡張 (TDD)

**Files:**
- Modify: `src/Core/Cards/CardJsonLoader.cs`
- Modify: `tests/Core.Tests/Cards/CardJsonLoaderTests.cs`

### Step 1.1: テスト

- [ ] versioned 形式の card JSON が正しくパースされるテスト追加:

```csharp
[Fact]
public void Parses_versioned_card_format()
{
    var json = """
    {
      "id": "strike",
      "name": "ストライク",
      "displayName": null,
      "activeVersion": "v1",
      "versions": [
        {
          "version": "v1",
          "createdAt": "2026-05-01T00:00:00Z",
          "label": "original",
          "spec": {
            "rarity": 1,
            "cardType": "Attack",
            "cost": 1,
            "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 6 }]
          }
        }
      ]
    }
    """;
    var def = CardJsonLoader.Parse(json);
    Assert.Equal("strike", def.Id);
    Assert.Equal("ストライク", def.Name);
    Assert.Equal(CardRarity.Common, def.Rarity);
    Assert.Equal(CardType.Attack, def.CardType);
    Assert.Equal(1, def.Cost);
    Assert.Single(def.Effects);
}

[Fact]
public void Versioned_with_multiple_versions_picks_active()
{
    var json = """
    {
      "id": "strike",
      "name": "ストライク",
      "activeVersion": "v2",
      "versions": [
        { "version": "v1", "spec": { "rarity": 1, "cardType": "Attack", "cost": 1, "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 6 }] } },
        { "version": "v2", "spec": { "rarity": 1, "cardType": "Attack", "cost": 0, "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 8 }] } }
      ]
    }
    """;
    var def = CardJsonLoader.Parse(json);
    Assert.Equal(0, def.Cost);  // v2 の値
    Assert.Equal(8, def.Effects[0].Amount);
}

[Fact]
public void Versioned_unknown_active_version_throws()
{
    var json = """
    {
      "id": "x", "name": "x",
      "activeVersion": "v99",
      "versions": [{ "version": "v1", "spec": { "rarity": 1, "cardType": "Attack", "cost": 1, "effects": [] } }]
    }
    """;
    Assert.Throws<CardJsonLoaderException>(() => CardJsonLoader.Parse(json));
}

[Fact]
public void Flat_format_still_parses_for_backward_compat()
{
    // 既存 35 card JSON は flat 形式のままなので、これが緑のままであることが必須
    var json = """
    {
      "id": "strike",
      "name": "ストライク",
      "rarity": 1,
      "cardType": "Attack",
      "cost": 1,
      "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 6 }]
    }
    """;
    var def = CardJsonLoader.Parse(json);
    Assert.Equal("strike", def.Id);
    Assert.Equal(1, def.Cost);
}
```

`CardJsonLoaderException` クラス名は既存規約に従う (なければ `InvalidOperationException` 等)。

### Step 1.2: 実装

- [ ] `CardJsonLoader.Parse(json)` を versioned 検出ロジックに拡張:

```csharp
public static CardDefinition Parse(string json)
{
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    // 共通フィールド
    string id = ReadStringOrThrow(root, "id");
    string name = ReadStringOrThrow(root, "name");
    string? displayName = TryGetString(root, "displayName");

    // versioned 検出: versions プロパティがあれば versioned
    if (root.TryGetProperty("versions", out var versionsEl) &&
        versionsEl.ValueKind == JsonValueKind.Array)
    {
        return ParseVersioned(id, name, displayName, root, versionsEl);
    }

    // flat (legacy)
    return ParseFlat(id, name, displayName, root);
}

private static CardDefinition ParseVersioned(
    string id, string name, string? displayName,
    JsonElement root, JsonElement versionsEl)
{
    string activeVersion = ReadStringOrThrow(root, "activeVersion");

    JsonElement? activeSpec = null;
    foreach (var v in versionsEl.EnumerateArray())
    {
        if (!v.TryGetProperty("version", out var verEl)) continue;
        if (verEl.GetString() != activeVersion) continue;
        if (!v.TryGetProperty("spec", out var specEl)) continue;
        activeSpec = specEl;
        break;
    }
    if (activeSpec is null)
        throw new CardJsonLoaderException(
            $"activeVersion '{activeVersion}' not found in versions[] for card '{id}'");

    return ParseSpec(id, name, displayName, activeSpec.Value);
}

private static CardDefinition ParseFlat(
    string id, string name, string? displayName, JsonElement root)
{
    // 既存 flat ロジックを spec として走らせる (root 自体が spec)
    return ParseSpec(id, name, displayName, root);
}

private static CardDefinition ParseSpec(
    string id, string name, string? displayName, JsonElement spec)
{
    // 既存 Parse 内のロジックを spec から読む形に切り出した版
    // (rarity / cardType / cost / effects / upgradedEffects / description / keywords ...)
    // ...
}
```

`ParseSpec` には現状の Parse 関数中身 (id/name 抽出後のロジック) を機械的に移す。

- [ ] テスト全件緑、既存 flat テスト全件緑のまま。

---

## Task 2: CardOverrideMerger 純関数を新設 (TDD)

**Files:**
- Create: `src/Core/Cards/CardOverrideMerger.cs`
- Create: `tests/Core.Tests/Cards/CardOverrideMergerTests.cs`

### Step 2.1: テスト

- [ ] マージ規則:
  - base.versions ∪ override.versions (同 version 識別子があれば override 優先)
  - override.activeVersion が指定されていれば override 値で base.activeVersion を上書き
  - override に id 等のメタが無くても OK (base 側のを使う)
  - id mismatch ならエラー

```csharp
[Fact]
public void Merge_unions_versions_arrays()
{
    var baseJson = """
    {
      "id": "x", "name": "x", "activeVersion": "v1",
      "versions": [{ "version": "v1", "spec": { ... } }]
    }
    """;
    var overrideJson = """
    {
      "id": "x", "activeVersion": "v2",
      "versions": [{ "version": "v2", "spec": { ... } }]
    }
    """;
    var merged = CardOverrideMerger.Merge(baseJson, overrideJson);
    using var doc = JsonDocument.Parse(merged);
    var versions = doc.RootElement.GetProperty("versions").EnumerateArray().ToList();
    Assert.Equal(2, versions.Count);
    Assert.Equal("v2", doc.RootElement.GetProperty("activeVersion").GetString());
}

[Fact]
public void Merge_override_version_replaces_base_version_with_same_id()
{
    // base v1 vs override v1 (異なる spec) → override 優先
    // ...
}

[Fact]
public void Merge_id_mismatch_throws()
{
    // ...
}

[Fact]
public void Merge_override_without_activeVersion_keeps_base()
{
    // ...
}
```

### Step 2.2: 実装

- [ ] `CardOverrideMerger.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoguelikeCardGame.Core.Cards;

public static class CardOverrideMerger
{
    public static string Merge(string baseJson, string overrideJson)
    {
        var baseNode = JsonNode.Parse(baseJson)?.AsObject()
            ?? throw new CardJsonLoaderException("base JSON must be object");
        var overrideNode = JsonNode.Parse(overrideJson)?.AsObject()
            ?? throw new CardJsonLoaderException("override JSON must be object");

        var baseId = baseNode["id"]?.GetValue<string>();
        var overrideId = overrideNode["id"]?.GetValue<string>();
        if (overrideId is not null && baseId != overrideId)
            throw new CardJsonLoaderException(
                $"override id '{overrideId}' does not match base id '{baseId}'");

        // versions union
        var baseVersions = baseNode["versions"]?.AsArray() ?? new JsonArray();
        var overrideVersions = overrideNode["versions"]?.AsArray() ?? new JsonArray();
        var merged = new JsonArray();
        var overrideIds = new HashSet<string>();

        foreach (var v in overrideVersions)
        {
            if (v is null) continue;
            var verId = v["version"]?.GetValue<string>();
            if (verId is null) continue;
            overrideIds.Add(verId);
        }

        // base version: override に同 id があれば skip
        foreach (var v in baseVersions)
        {
            if (v is null) continue;
            var verId = v["version"]?.GetValue<string>();
            if (verId is not null && overrideIds.Contains(verId)) continue;
            merged.Add(v.DeepClone());
        }
        // override version: 全部追加
        foreach (var v in overrideVersions)
        {
            if (v is null) continue;
            merged.Add(v.DeepClone());
        }

        baseNode["versions"] = merged;

        // activeVersion override
        var overrideActive = overrideNode["activeVersion"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(overrideActive))
        {
            baseNode["activeVersion"] = overrideActive;
        }

        return baseNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
```

- [ ] テスト全件緑。

---

## Task 3: 一括移行スクリプト

**Files:**
- Create: `tools/migrate-cards-to-versioned.csx` (.NET script、`dotnet script` で実行) or `tools/migrate-cards-to-versioned.ps1`

### Step 3.1: スクリプト作成

- [ ] PowerShell 案 (より身近):

```powershell
# tools/migrate-cards-to-versioned.ps1
# 一回限り実行: src/Core/Data/Cards/*.json を versioned 形式に変換
$ErrorActionPreference = 'Stop'
$cardsDir = Join-Path $PSScriptRoot '..\src\Core\Data\Cards'
$now = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ssZ')

Get-ChildItem $cardsDir -Filter '*.json' | ForEach-Object {
    $json = Get-Content $_.FullName -Raw | ConvertFrom-Json -AsHashtable
    if ($json.ContainsKey('versions')) {
        Write-Host "Skipping (already versioned): $($_.Name)"
        return
    }
    # spec = root から id/name/displayName を除いた残り
    $spec = @{}
    foreach ($key in $json.Keys) {
        if ($key -in 'id','name','displayName') { continue }
        $spec[$key] = $json[$key]
    }
    $versioned = [ordered]@{
        id = $json['id']
        name = $json['name']
        displayName = if ($json.ContainsKey('displayName')) { $json['displayName'] } else { $null }
        activeVersion = 'v1'
        versions = @(
            [ordered]@{
                version = 'v1'
                createdAt = $now
                label = 'original'
                spec = $spec
            }
        )
    }
    $output = $versioned | ConvertTo-Json -Depth 20
    Set-Content -Path $_.FullName -Value $output -Encoding UTF8
    Write-Host "Migrated: $($_.Name)"
}
Write-Host 'Done.'
```

### Step 3.2: 実行 + diff 確認

- [ ] PowerShell で実行: `pwsh tools/migrate-cards-to-versioned.ps1`
- [ ] `git diff src/Core/Data/Cards/` で全 35 ファイルが versioned 化されたことを目視確認
- [ ] `dotnet test` を実行して **既存テスト全件緑のまま** であることを確認 (後方互換 + 新 loader が両形式読めるため)

---

## Task 4: Server 側 DevOverrideLoader 実装 (TDD)

**Files:**
- Create: `src/Server/Services/DevOverrideLoader.cs`
- Modify: `src/Server/Program.cs` (DI 登録)
- Create: `tests/Server.Tests/Services/DevOverrideLoaderTests.cs`

### Step 4.1: テスト

```csharp
public class DevOverrideLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public DevOverrideLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dev-override-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "cards"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void NonExistent_dir_returns_empty_dict()
    {
        var result = DevOverrideLoader.LoadCards("c:\\nonexistent");
        Assert.Empty(result);
    }

    [Fact]
    public void Reads_card_override_jsons()
    {
        var path = Path.Combine(_tempDir, "cards", "strike.json");
        File.WriteAllText(path, """{ "id": "strike", "activeVersion": "v2", "versions": [] }""");

        var result = DevOverrideLoader.LoadCards(_tempDir);

        Assert.Single(result);
        Assert.True(result.ContainsKey("strike"));
    }
}
```

### Step 4.2: 実装

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// DEV ビルド時のみ data-local/dev-overrides/cards/*.json を読んで
/// id → JSON 文字列の辞書を返す。
/// </summary>
public static class DevOverrideLoader
{
    public static IReadOnlyDictionary<string, string> LoadCards(string overrideRoot)
    {
        var result = new Dictionary<string, string>();
        var cardsDir = Path.Combine(overrideRoot, "cards");
        if (!Directory.Exists(cardsDir)) return result;

        foreach (var path in Directory.EnumerateFiles(cardsDir, "*.json"))
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch { continue; }  // 読めないファイルは skip
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrEmpty(id)) continue;
                result[id] = json;
            }
            catch { continue; }
        }
        return result;
    }
}
```

### Step 4.3: Program.cs で merge 経路 wire-up

- [ ] `src/Server/Program.cs` の `AddSingleton<DataCatalog>` を以下に変更:

```csharp
builder.Services.AddSingleton<DataCatalog>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    if (!env.IsDevelopment())
        return EmbeddedDataLoader.LoadCatalog();

    // DEV: data-local/dev-overrides/cards/ から override を読んで base と merge
    var overrideRoot = Path.Combine(env.ContentRootPath, "..", "..", "data-local", "dev-overrides");
    var overrides = DevOverrideLoader.LoadCards(overrideRoot);
    if (overrides.Count == 0) return EmbeddedDataLoader.LoadCatalog();

    return EmbeddedDataLoader.LoadCatalogWithOverrides(overrides);
});
```

- [ ] `EmbeddedDataLoader.LoadCatalogWithOverrides(IReadOnlyDictionary<string, string> cardOverrides)` を新設:

```csharp
public static DataCatalog LoadCatalogWithOverrides(IReadOnlyDictionary<string, string> cardOverrides)
{
    var asm = typeof(EmbeddedDataLoader).Assembly;
    var baseCards = ReadAllWithPrefix(asm, CardsPrefix).ToList();

    if (cardOverrides.Count == 0)
        return DataCatalog.LoadFromStrings(/* 既存パラメータ */);

    var merged = new List<string>(baseCards.Count);
    foreach (var json in baseCards)
    {
        // base から id を抽出して override 候補を探す
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (id is not null && cardOverrides.TryGetValue(id, out var ovr))
        {
            merged.Add(CardOverrideMerger.Merge(json, ovr));
        }
        else
        {
            merged.Add(json);
        }
    }

    string? merchantPricesJson = ReadSingle(asm, MerchantPricesResourceName);
    return DataCatalog.LoadFromStrings(
        cards: merged,
        relics: ReadAllWithPrefix(asm, RelicsPrefix),
        // ... 残り embedded 経路をそのまま
    );
}
```

(現実装と同じ呼出を維持して cards 配列だけ merged で差し替える)

- [ ] `dotnet test` 全件緑、`dotnet run --project src/Server` で起動できることを sanity 確認 (override 無しの状態)。

---

## Task 5: .gitignore + dir 設定

**Files:**
- Modify: `.gitignore`

- [ ] `.gitignore` に追加:

```
# Phase 10.5.H: 開発者ローカル override (machine-local)
data-local/dev-overrides/
```

(`data-local/` 自体は他の Server-side persistence で既に gitignore 済かどうか確認、なければそれも追加)

- [ ] dir は実際には作成不要 (override 使わない開発者は dir 無し、必要な開発者が手動で `mkdir`)。

---

## Task 6: Self-review + 1 commit + push

### 1. Spec coverage

- [ ] versioned JSON schema 実装、loader 両形式対応 ✓
- [ ] 一括移行スクリプトで 35 card JSON 変換完了 ✓
- [ ] CardOverrideMerger 純関数で merge ルール実装 ✓
- [ ] Server DEV モードで data-local/dev-overrides/cards/*.json を読んで base と merge ✓
- [ ] gitignore で override dir を除外 ✓

### 2. Build / test final

- [ ] `dotnet build` 警告 0 / エラー 0
- [ ] `dotnet test` 全件緑 (Core 1138 + 新 ~10 / Server 200 + 新 ~3)
- [ ] `npx tsc --noEmit` パス
- [ ] `npx vitest run` 全件緑 (155)
- [ ] `npm run build` (Client) エラーなし
- [ ] `dotnet run --project src/Server` で起動 (5 秒以内に listen 開始) — sanity 確認 (override 無し)

### 3. Commit + push

- [ ] 1 commit (`feat(core/server): versioned card JSON + dev override merger (Phase 10.5.H)`)
- [ ] origin master へ push
- [ ] **migration 結果も同 commit**: 35 card JSON ファイルの versioned 化 + loader/merger 新規 + Server wire-up を一括 commit

---

## 完了条件

- [ ] CardJsonLoader が versioned/flat 両形式を解釈
- [ ] CardOverrideMerger 純関数が unit test 含めて完成
- [ ] 35 card JSON が versioned 形式に migrate 済 (git diff で全ファイル変更)
- [ ] Server DEV mode で override を読み込み base と merge する経路が動作 (空 override で起動可、override ありでテスト緑)
- [ ] .gitignore で `data-local/dev-overrides/` 除外
- [ ] commit + push 済み

## 今回スコープ外

- relic / potion / enemy / unit JSON の versioned 化 → 10.5.L
- FileSystemWatcher hot reload → 10.5.I/J
- override の "promote" 機能 → 10.5.J
- Token rarity カードの override 経由配信 (機構は同じだが運用は別)

## ロールバック

問題あれば:
1. Server.Program.cs の DataCatalog DI を `EmbeddedDataLoader.LoadCatalog()` 直呼びに戻す → override は無視されるが catalog 動作は変化なし
2. 移行 JSON 変換の revert は `git checkout HEAD~ -- src/Core/Data/Cards/` で flat 形式に戻る、loader が両対応なのでどちらでも動く

## 関連ドキュメント

- 設計: [`2026-05-01-phase10-5-design.md`](../specs/2026-05-01-phase10-5-design.md) §3, §4
- 直前 sub-phase: [`2026-05-01-phase10-5G-token-rarity.md`](2026-05-01-phase10-5G-token-rarity.md)
