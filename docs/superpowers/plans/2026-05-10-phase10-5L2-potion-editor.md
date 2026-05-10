# Phase 10.5.L2 Potion Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase 10.5.L1 (relic editor) の機能を potion に再現。dev menu から既存 7 ポーション編集 / 新規 potion 作成 / formatter live preview / version 管理 / dev-override fallback を提供。

**Architecture:** L1 を 1:1 mirror — `PotionDefinition` に description / displayName field を追加し、`PotionJsonLoader` を versioning 対応に拡張、新規 `PotionOverrideMerger` を追加、Server に `DevPotionsController` 7 endpoints + `DevPotionWriter` を実装、Client に `DevPotionsScreen` + `PotionSpecForm` を新規作成。`EffectListEditor` / `EffectEditor` / `FormatterPreview` 等の共通コンポーネントは再利用。`EffectEditor` に `battleOnly` トグルを always-on 追加。

**Tech Stack:** C# .NET 10、ASP.NET Core 10、xUnit、React 19 + TypeScript + Vite、Vitest + React Testing Library。

**Spec reference:** [docs/superpowers/specs/2026-05-10-phase10-5L2-potion-editor-design.md](docs/superpowers/specs/2026-05-10-phase10-5L2-potion-editor-design.md)

---

## File Structure

**Create:**
- `src/Core/Potions/PotionOverrideMerger.cs` — base + override JSON マージ純関数 (`RelicOverrideMerger` mirror)
- `src/Server/Controllers/DevPotionsController.cs` — 7 endpoints (`DevRelicsController` mirror)
- `src/Server/Dtos/DevPotionDto.cs` — `DevPotionDto` / `DevPotionVersionDto` / `NewPotionRequest` / `SavePotionVersionRequest` / `SwitchActivePotionVersionRequest` / `PreviewPotionRequest`
- `src/Client/src/screens/DevPotionsScreen.tsx` — 画面シェル (左 30% list / 右 70% form)
- `src/Client/src/screens/DevPotionsScreen.css` — screen styles
- `src/Client/src/screens/dev/PotionSpecForm.tsx` — potion 固有 form
- `src/Client/src/screens/dev/PotionSpecForm.test.tsx` — form unit tests
- `tests/Core.Tests/Potions/PotionJsonLoaderVersioningTests.cs` — 新 schema + 後方互換テスト
- `tests/Core.Tests/Potions/PotionOverrideMergerTests.cs` — マージ純関数テスト
- `tests/Server.Tests/Controllers/DevPotionsControllerTests.cs` — 7 endpoints + error paths

**Modify:**
- `src/Core/Potions/PotionDefinition.cs` — `DisplayName?` / `Description?` field 追加
- `src/Core/Potions/PotionJsonLoader.cs` — versioning 対応 (`ResolveActiveSpec` 追加)
- `src/Core/Data/DevOverrideLoader.cs` — `LoadPotions(...)` 追加
- `src/Core/Data/EmbeddedDataLoader.cs` — `LoadCatalogWithOverrides(...)` に potionOverrides 引数追加
- `src/Server/Services/DevCardWriter.cs` — 末尾に `DevPotionWriter` 薄ラッパクラス追加
- `src/Server/Services/DataCatalogProvider.cs` — potion override 経路を追加
- `src/Server/Program.cs` — `DevPotionWriter` を DI 登録
- `src/Client/src/api/dev.ts` — potion 用 7 endpoint 関数追加
- `src/Client/src/api/types.ts` — `DevPotionDto` / `DevPotionVersionDto` 等の DTO 型追加
- `src/Client/src/screens/dev/DevSpecTypes.ts` — `PotionSpec` / `PotionVersionSpec` 型 + 変換関数追加
- `src/Client/src/screens/dev/EffectEditor.tsx` — `battleOnly` トグルを always-on 表示
- `src/Client/src/screens/dev/EffectEditor.test.tsx` — battleOnly トグル UI テスト追加
- `src/Client/src/screens/DevHomeScreen.tsx` — 「Potions」ナビ追加
- `src/Client/src/App.tsx` (or routing 設定) — `/dev/potions` route 追加

---

## Task 1: PotionDefinition 拡張 + PotionJsonLoader versioning 対応 (TDD)

**Files:**
- Modify: `src/Core/Potions/PotionDefinition.cs`
- Modify: `src/Core/Potions/PotionJsonLoader.cs`
- Create: `tests/Core.Tests/Potions/PotionJsonLoaderVersioningTests.cs`

**Goal:** `PotionDefinition` に `DisplayName?` / `Description?` 追加、`PotionJsonLoader` を versioning 形式 (`activeVersion` + `versions[]`) に対応させる。後方互換: 既存 flat 形式 (`{id, name, rarity, effects}`) は引き続きパースできる。

- [ ] **Step 1.1: PotionJsonLoader 失敗テストを書く (TDD)**

`tests/Core.Tests/Potions/PotionJsonLoaderVersioningTests.cs` を新規作成:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Potions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Potions;

public class PotionJsonLoaderVersioningTests
{
    [Fact]
    public void Parse_flat_legacy_json_succeeds_as_version1()
    {
        // 既存 7 ポーションと同じ flat 形式
        var json = """
        {
            "id": "fire_potion",
            "name": "ファイアポーション",
            "rarity": 1,
            "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 20, "battleOnly": true }]
        }
        """;
        var def = PotionJsonLoader.Parse(json);
        Assert.Equal("fire_potion", def.Id);
        Assert.Equal("ファイアポーション", def.Name);
        Assert.Null(def.DisplayName);
        Assert.Null(def.Description);
        Assert.Equal(CardRarity.Uncommon, def.Rarity);
        Assert.Single(def.Effects);
    }

    [Fact]
    public void Parse_versioned_json_uses_activeVersion()
    {
        var json = """
        {
            "id": "fire_potion",
            "name": "ファイアポーション",
            "displayName": null,
            "activeVersion": "v2",
            "versions": [
                { "version": "v1", "spec": { "rarity": 1, "effects": [], "description": "古い" } },
                { "version": "v2", "spec": { "rarity": 2, "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 30, "battleOnly": true }], "description": "新しい" } }
            ]
        }
        """;
        var def = PotionJsonLoader.Parse(json);
        Assert.Equal(CardRarity.Rare, def.Rarity);  // v2 の rarity=2
        Assert.Equal("新しい", def.Description);
        Assert.Single(def.Effects);
    }

    [Fact]
    public void Parse_versioned_json_with_displayName_populates_field()
    {
        var json = """
        {
            "id": "p1",
            "name": "Potion1",
            "displayName": "別名",
            "activeVersion": "v1",
            "versions": [{ "version": "v1", "spec": { "rarity": 0, "effects": [] } }]
        }
        """;
        var def = PotionJsonLoader.Parse(json);
        Assert.Equal("別名", def.DisplayName);
    }

    [Fact]
    public void Parse_versioned_json_with_missing_active_throws()
    {
        var json = """
        {
            "id": "p1", "name": "Potion1",
            "activeVersion": "v999",
            "versions": [{ "version": "v1", "spec": { "rarity": 0, "effects": [] } }]
        }
        """;
        Assert.Throws<PotionJsonException>(() => PotionJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_versioned_json_with_invalid_version_spec_throws()
    {
        var json = """
        {
            "id": "p1", "name": "Potion1",
            "activeVersion": "v1",
            "versions": [{ "version": "v1", "spec": "not_an_object" }]
        }
        """;
        Assert.Throws<PotionJsonException>(() => PotionJsonLoader.Parse(json));
    }
}
```

- [ ] **Step 1.2: テストが失敗することを確認 (compile error 想定: PotionDefinition に DisplayName / Description が無いため)**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~PotionJsonLoaderVersioning" --logger "console;verbosity=minimal"
```

Expected: コンパイルエラー (`PotionDefinition` に `DisplayName` / `Description` プロパティが無い)。

- [ ] **Step 1.3: PotionDefinition に field 追加**

`src/Core/Potions/PotionDefinition.cs` を以下に置換:

```csharp
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Potions;

/// <summary>ポーションのマスター定義。</summary>
public sealed record PotionDefinition(
    string Id,
    string Name,
    string? DisplayName,
    CardRarity Rarity,
    IReadOnlyList<CardEffect> Effects,
    string? Description = null)
{
    /// <summary>
    /// 戦闘外で使用可能か。effects のいずれかが BattleOnly=false なら true。
    /// 全 effect が BattleOnly=true なら false（マップ画面でグレーアウト）。
    /// </summary>
    public bool IsUsableOutsideBattle => Effects.Any(e => !e.BattleOnly);
}
```

(`DisplayName` を `Name` の直後 / `Rarity` の直前に挿入。`Description` は末尾 default null。)

- [ ] **Step 1.4: PotionJsonLoader を versioning 対応**

`src/Core/Potions/PotionJsonLoader.cs` を以下に置換:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Potions;

/// <summary>ポーション JSON のパース失敗を表す例外。</summary>
public sealed class PotionJsonException : Exception
{
    public PotionJsonException(string message) : base(message) { }
    public PotionJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>ポーション JSON 文字列を PotionDefinition に変換する純粋関数群。</summary>
public static class PotionJsonLoader
{
    public static PotionDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new PotionJsonException("ポーション JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);
                var displayName = root.TryGetProperty("displayName", out var dnEl) && dnEl.ValueKind == JsonValueKind.String
                    ? dnEl.GetString()
                    : null;

                // Phase 10.5.L2: versioned 検出 — versions が配列なら versioned、それ以外 flat (legacy)。
                if (root.TryGetProperty("versions", out var versionsEl) &&
                    versionsEl.ValueKind == JsonValueKind.Array)
                {
                    var activeSpec = ResolveActiveSpec(root, versionsEl, id);
                    return ParseSpec(id, name, displayName, activeSpec);
                }

                // flat (legacy): root 自体を spec として扱う。
                return ParseSpec(id, name, displayName, root);
            }
            catch (PotionJsonException) { throw; }
            catch (Exception ex) when (ex is not PotionJsonException)
            {
                var where = id is null ? "(potion id unknown)" : $"(potion id={id})";
                throw new PotionJsonException($"ポーション JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static JsonElement ResolveActiveSpec(JsonElement root, JsonElement versionsEl, string id)
    {
        var activeVersion = GetRequiredString(root, "activeVersion", id);
        foreach (var v in versionsEl.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Object) continue;
            if (!v.TryGetProperty("version", out var verEl) || verEl.ValueKind != JsonValueKind.String) continue;
            if (verEl.GetString() != activeVersion) continue;
            if (!v.TryGetProperty("spec", out var specEl) || specEl.ValueKind != JsonValueKind.Object)
                throw new PotionJsonException(
                    $"version '{activeVersion}' の spec が object ではありません (potion id={id})。");
            return specEl;
        }
        throw new PotionJsonException(
            $"activeVersion '{activeVersion}' が versions[] に見つかりません (potion id={id})。");
    }

    private static PotionDefinition ParseSpec(string id, string name, string? displayName, JsonElement spec)
    {
        var rawRarity = GetRequiredInt(spec, "rarity", id);
        if (!Enum.IsDefined(typeof(CardRarity), rawRarity))
            throw new PotionJsonException($"rarity の値 {rawRarity} は無効です (potion id={id})。");
        var rarity = (CardRarity)rawRarity;

        var effects = ParseEffects(spec, "effects", id);

        var description = spec.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
            ? descEl.GetString()
            : null;

        return new PotionDefinition(id, name, displayName, rarity, effects, description);
    }

    private static IReadOnlyList<CardEffect> ParseEffects(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<CardEffect>();

        var list = new List<CardEffect>();
        foreach (var el in arr.EnumerateArray())
            list.Add(ParseEffect(el, id));
        return list;
    }

    private static CardEffect ParseEffect(JsonElement el, string? id)
    {
        var ctx = id is null ? "" : $" (potion id={id})";
        return CardEffectParser.ParseEffect(el, msg => new PotionJsonException($"{msg}{ctx}"));
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (potion id={id})";
            throw new PotionJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (potion id={id})";
            throw new PotionJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }
}
```

- [ ] **Step 1.5: テストが通ることを確認 + 既存全テスト通過確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --logger "console;verbosity=minimal"
```

Expected: 既存 1263 + 新 5 = 1268 PASS, 0 failures。既存 7 ポーション JSON は flat 形式なので legacy 経路で読まれて regression なし。

- [ ] **Step 1.6: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Core/Potions/PotionDefinition.cs src/Core/Potions/PotionJsonLoader.cs tests/Core.Tests/Potions/PotionJsonLoaderVersioningTests.cs
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(potions): versioning support + DisplayName/Description fields (Phase 10.5.L2 T1)

- PotionDefinition に DisplayName? / Description? を追加 (relic と同等)
- PotionJsonLoader を versioning 対応 (activeVersion + versions[]、RelicJsonLoader と同パターン)
- 後方互換: 既存 flat 形式は引き続きパース可
- テスト 5 件 (legacy 経路 / versioned 経路 / displayName / 不正 activeVersion / 不正 spec)

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 2: PotionOverrideMerger 新規 (TDD)

**Files:**
- Create: `src/Core/Potions/PotionOverrideMerger.cs`
- Create: `tests/Core.Tests/Potions/PotionOverrideMergerTests.cs`

**Goal:** base + override JSON をマージする純関数を `RelicOverrideMerger` の mirror として作成。マージ規則: versions union (override 優先)、override.activeVersion 上書き、id mismatch で例外。

- [ ] **Step 2.1: 失敗テストを書く**

`tests/Core.Tests/Potions/PotionOverrideMergerTests.cs` を新規作成:

```csharp
using System.Text.Json;
using RoguelikeCardGame.Core.Potions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Potions;

public class PotionOverrideMergerTests
{
    [Fact]
    public void Merge_combines_base_and_override_versions()
    {
        var baseJson = """
        {
            "id": "p1", "name": "P1", "activeVersion": "v1",
            "versions": [{ "version": "v1", "spec": { "rarity": 0, "effects": [] } }]
        }
        """;
        var overrideJson = """
        {
            "id": "p1", "activeVersion": "v2",
            "versions": [{ "version": "v2", "spec": { "rarity": 1, "effects": [], "description": "new" } }]
        }
        """;
        var merged = PotionOverrideMerger.Merge(baseJson, overrideJson);
        using var doc = JsonDocument.Parse(merged);
        Assert.Equal("v2", doc.RootElement.GetProperty("activeVersion").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("versions").GetArrayLength());
    }

    [Fact]
    public void Merge_override_replaces_same_version_id()
    {
        var baseJson = """
        {
            "id": "p1", "name": "P1", "activeVersion": "v1",
            "versions": [{ "version": "v1", "spec": { "rarity": 0, "effects": [] } }]
        }
        """;
        var overrideJson = """
        {
            "id": "p1",
            "versions": [{ "version": "v1", "spec": { "rarity": 2, "effects": [] } }]
        }
        """;
        var merged = PotionOverrideMerger.Merge(baseJson, overrideJson);
        using var doc = JsonDocument.Parse(merged);
        var versions = doc.RootElement.GetProperty("versions");
        Assert.Equal(1, versions.GetArrayLength());
        Assert.Equal(2, versions[0].GetProperty("spec").GetProperty("rarity").GetInt32());
    }

    [Fact]
    public void Merge_id_mismatch_throws()
    {
        var baseJson = """{ "id": "a", "name": "A", "activeVersion": "v1", "versions": [] }""";
        var overrideJson = """{ "id": "b", "versions": [] }""";
        Assert.Throws<PotionJsonException>(() => PotionOverrideMerger.Merge(baseJson, overrideJson));
    }

    [Fact]
    public void Merge_override_without_activeVersion_keeps_base_active()
    {
        var baseJson = """
        {
            "id": "p1", "name": "P1", "activeVersion": "v1",
            "versions": [{ "version": "v1", "spec": { "rarity": 0, "effects": [] } }]
        }
        """;
        var overrideJson = """{ "id": "p1", "versions": [] }""";
        var merged = PotionOverrideMerger.Merge(baseJson, overrideJson);
        using var doc = JsonDocument.Parse(merged);
        Assert.Equal("v1", doc.RootElement.GetProperty("activeVersion").GetString());
    }
}
```

- [ ] **Step 2.2: テスト失敗確認**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --filter "FullyQualifiedName~PotionOverrideMerger" --logger "console;verbosity=minimal"
```

Expected: コンパイルエラー (`PotionOverrideMerger` 未定義)。

- [ ] **Step 2.3: PotionOverrideMerger を実装**

`src/Core/Potions/PotionOverrideMerger.cs` を新規作成:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoguelikeCardGame.Core.Potions;

/// <summary>
/// 開発者ローカル override JSON を base potion JSON にマージする純関数。
/// マージ規則:
///   - versions は union (override 優先で同 version 識別子重複時は override 採用)
///   - override.activeVersion が指定されていれば base.activeVersion を上書き
///   - id mismatch なら PotionJsonException を送出
///   - override に id 等のメタが無くても base 側を使う
/// 入出力は JSON 文字列。Core 層内なので file I/O は触れない。
/// Phase 10.5.L2 — RelicOverrideMerger の mirror。
/// </summary>
public static class PotionOverrideMerger
{
    public static string Merge(string baseJson, string overrideJson)
    {
        var baseNode = JsonNode.Parse(baseJson)?.AsObject()
            ?? throw new PotionJsonException("base JSON は object でなければなりません。");
        var overrideNode = JsonNode.Parse(overrideJson)?.AsObject()
            ?? throw new PotionJsonException("override JSON は object でなければなりません。");

        var baseId = baseNode["id"]?.GetValue<string>();
        var overrideId = overrideNode["id"]?.GetValue<string>();
        if (overrideId is not null && baseId is not null && baseId != overrideId)
            throw new PotionJsonException(
                $"override id '{overrideId}' は base id '{baseId}' と一致しません。");

        var baseVersions = baseNode["versions"] as JsonArray ?? new JsonArray();
        var overrideVersions = overrideNode["versions"] as JsonArray ?? new JsonArray();

        var overrideIds = new HashSet<string>();
        foreach (var v in overrideVersions)
        {
            if (v is null) continue;
            var verId = v["version"]?.GetValue<string>();
            if (verId is not null) overrideIds.Add(verId);
        }

        var merged = new JsonArray();
        foreach (var v in baseVersions)
        {
            if (v is null) continue;
            var verId = v["version"]?.GetValue<string>();
            if (verId is not null && overrideIds.Contains(verId)) continue;
            merged.Add(v.DeepClone());
        }
        foreach (var v in overrideVersions)
        {
            if (v is null) continue;
            merged.Add(v.DeepClone());
        }

        baseNode["versions"] = merged;

        var overrideActive = overrideNode["activeVersion"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(overrideActive))
        {
            baseNode["activeVersion"] = overrideActive;
        }

        return baseNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
```

- [ ] **Step 2.4: テスト通過確認 + 全 Core テスト通過**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --logger "console;verbosity=minimal"
```

Expected: 1268 + 4 = 1272 PASS, 0 failures。

- [ ] **Step 2.5: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Core/Potions/PotionOverrideMerger.cs tests/Core.Tests/Potions/PotionOverrideMergerTests.cs
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(potions): PotionOverrideMerger for base+override merge (Phase 10.5.L2 T2)

RelicOverrideMerger を mirror した純関数。versions union / activeVersion 上書き /
id mismatch 例外。テスト 4 件。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 3: DataCatalog の potion override 経路追加

**Files:**
- Modify: `src/Core/Data/DevOverrideLoader.cs`
- Modify: `src/Core/Data/EmbeddedDataLoader.cs`
- Modify: `src/Server/Services/DataCatalogProvider.cs`

**Goal:** dev-overrides フォルダから potion override を読み込み、`DataCatalog` ロード時に base + override をマージする経路を追加。

- [ ] **Step 3.1: DevOverrideLoader.LoadRelics の実装を読んで pattern を把握**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" grep -nE "LoadRelics|LoadCards" -- src/Core/Data/DevOverrideLoader.cs | head -5
```

Read `src/Core/Data/DevOverrideLoader.cs` 全体を確認。`LoadRelics(string overrideRoot)` を mirror する `LoadPotions(string overrideRoot)` を追加する。

- [ ] **Step 3.2: DevOverrideLoader.LoadPotions を追加**

`src/Core/Data/DevOverrideLoader.cs` の末尾 (LoadRelics の直後) に `LoadPotions` を追加。`LoadRelics` のロジックを copy + `relics` → `potions` 置換 + `RelicJsonLoader` → `PotionJsonLoader` + `RelicOverrideMerger` → `PotionOverrideMerger` にすること。

実装パターン (LoadRelics と同等):
```csharp
public static IReadOnlyDictionary<string, PotionDefinition> LoadPotions(string overrideRoot)
{
    var dir = Path.Combine(overrideRoot, "potions");
    if (!Directory.Exists(dir)) return new Dictionary<string, PotionDefinition>();

    var result = new Dictionary<string, PotionDefinition>();
    foreach (var file in Directory.GetFiles(dir, "*.json"))
    {
        var json = File.ReadAllText(file);
        try
        {
            var def = PotionJsonLoader.Parse(json);
            result[def.Id] = def;
        }
        catch (PotionJsonException)
        {
            // skip malformed override
        }
    }
    return result;
}
```

(ファイル先頭の using に `RoguelikeCardGame.Core.Potions;` が無ければ追加。`LoadRelics` の流儀を踏襲。)

- [ ] **Step 3.3: EmbeddedDataLoader.LoadCatalogWithOverrides を拡張**

`src/Core/Data/EmbeddedDataLoader.cs` の `LoadCatalogWithOverrides` シグネチャに `IReadOnlyDictionary<string, PotionDefinition>? potionOverrides = null` を追加 (default null で互換)。method 内部で base potions を読み込んだ後、potionOverrides の内容で `dict[id] = override` 形式で上書き。既存の cardOverrides / relicOverrides と同パターン。

- [ ] **Step 3.4: DataCatalogProvider.BuildCatalog に potion 経路を組込**

`src/Server/Services/DataCatalogProvider.cs` の `BuildCatalog()` を以下に置換:

```csharp
private DataCatalog BuildCatalog()
{
    if (!_env.IsDevelopment())
        return EmbeddedDataLoader.LoadCatalog();

    var overrideRoot = Path.Combine(_env.ContentRootPath, "..", "..", "data-local", "dev-overrides");
    var cardOverrides = DevOverrideLoader.LoadCards(overrideRoot);
    var relicOverrides = DevOverrideLoader.LoadRelics(overrideRoot);
    var potionOverrides = DevOverrideLoader.LoadPotions(overrideRoot);

    return cardOverrides.Count == 0 && relicOverrides.Count == 0 && potionOverrides.Count == 0
        ? EmbeddedDataLoader.LoadCatalog()
        : EmbeddedDataLoader.LoadCatalogWithOverrides(cardOverrides, relicOverrides, potionOverrides);
}
```

- [ ] **Step 3.5: 既存テスト通過確認 (regression なし)**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --logger "console;verbosity=minimal"
dotnet test tests/Server.Tests/Server.Tests.csproj -c Release --logger "console;verbosity=minimal" 2>&1 | tail -10
```

Expected: Core 1272 PASS, Server 289 + 2 skip PASS (どちらも regression なし)。dev-overrides/potions ディレクトリは無いので空 dict が返り、既存挙動と同じ。

- [ ] **Step 3.6: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Core/Data/DevOverrideLoader.cs src/Core/Data/EmbeddedDataLoader.cs src/Server/Services/DataCatalogProvider.cs
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(data): potion override loading path (Phase 10.5.L2 T3)

DevOverrideLoader.LoadPotions / EmbeddedDataLoader.LoadCatalogWithOverrides の
potionOverrides 引数 / DataCatalogProvider への組込で、dev-overrides/potions/ から
potion の override を読めるようにした。既存挙動への影響なし (空ディレクトリ → 空 dict)。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 4: Server DTOs + DevPotionWriter

**Files:**
- Create: `src/Server/Dtos/DevPotionDto.cs`
- Modify: `src/Server/Services/DevCardWriter.cs` (末尾に DevPotionWriter 追加)
- Modify: `src/Server/Program.cs` (DI 登録)

**Goal:** Server 側の DTO と writer を準備。Controller は次の Task 5 で実装。

- [ ] **Step 4.1: DevPotionDto + Request DTOs 作成**

`src/Server/Dtos/DevPotionDto.cs` を新規作成:

```csharp
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// /api/dev/potions で返す read-only な potion 概要 DTO。Phase 10.5.L2。
/// versioned JSON の中身をほぼそのまま返し、spec 部分は raw JSON 文字列で保持して
/// UI 側で構造を保ったまま表示できるようにする。
/// </summary>
public sealed record DevPotionDto(
    string Id,
    string Name,
    string? DisplayName,
    string ActiveVersion,
    IReadOnlyList<DevPotionVersionDto> Versions);

public sealed record DevPotionVersionDto(
    string Version,
    string? CreatedAt,
    string? Label,
    string Spec);

public sealed record SavePotionVersionRequest(string? Label, JsonElement Spec);

public sealed record SwitchActivePotionVersionRequest(string Version);

public sealed record NewPotionRequest(
    string Id,
    string Name,
    string? DisplayName = null,
    string? TemplatePotionId = null);

public sealed record PreviewPotionRequest(JsonElement Spec);
```

- [ ] **Step 4.2: DevPotionWriter を DevCardWriter.cs 末尾に追加**

`src/Server/Services/DevCardWriter.cs` の末尾 (`DevRelicWriter` クラスの直後) に追加:

```csharp
/// <summary>
/// Phase 10.5.L2: potion 用 writer (DevCardWriter の subDir="potions" instance)。
/// 単に DI 上で別 type として持ちたいだけなので、内部で DevCardWriter を委譲する薄ラッパ。
/// </summary>
public sealed class DevPotionWriter
{
    private readonly DevCardWriter _inner;

    public DevPotionWriter(string overrideRoot, string? baseDir = null, string? backupRoot = null)
    {
        _inner = new DevCardWriter(overrideRoot, baseDir, backupRoot, subDir: "potions");
    }

    public void WriteOverride(string id, string json) => _inner.WriteOverride(id, json);
    public string? ReadOverride(string id) => _inner.ReadOverride(id);
    public void DeleteOverride(string id) => _inner.DeleteOverride(id);
    public void WriteBaseWithBackup(string id, string json) => _inner.WriteBaseWithBackup(id, json);
    public string? ReadBase(string id) => _inner.ReadBase(id);
    public void DeleteBaseWithBackup(string id) => _inner.DeleteBaseWithBackup(id);
    public IReadOnlyList<string> ListOverrideIds() => _inner.ListOverrideIds();
}
```

- [ ] **Step 4.3: Program.cs に DI 登録**

`src/Server/Program.cs` で `DevRelicWriter` を DI 登録している場所を `git -C "c:/Users/Metaverse/projects/roguelike-cardgame" grep -nE "DevRelicWriter" -- src/Server/Program.cs` で見つけて、その直後に同じパターンで `DevPotionWriter` を登録:

```csharp
builder.Services.AddSingleton(_ => new DevPotionWriter(
    overrideRoot: Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data-local", "dev-overrides"),
    baseDir: null,    // potion は base 側を直接編集しない (override only)
    backupRoot: null));
```

(具体的な path / args は `DevRelicWriter` 登録箇所のコピーから potion 用に置換すれば良い。)

- [ ] **Step 4.4: ビルド通過確認**

```bash
dotnet build src/Server/Server.csproj -c Release
```

Expected: クリーンビルド (warning 0、error 0)。

- [ ] **Step 4.5: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Server/Dtos/DevPotionDto.cs src/Server/Services/DevCardWriter.cs src/Server/Program.cs
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(server): DevPotion DTOs + DevPotionWriter (Phase 10.5.L2 T4)

DevPotionDto / DevPotionVersionDto / NewPotionRequest / SavePotionVersionRequest /
SwitchActivePotionVersionRequest / PreviewPotionRequest を新規追加。
DevPotionWriter は DevCardWriter の subDir="potions" 薄ラッパ。Program.cs に DI 登録。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 5: DevPotionsController 実装 + テスト (TDD)

**Files:**
- Create: `src/Server/Controllers/DevPotionsController.cs`
- Create: `tests/Server.Tests/Controllers/DevPotionsControllerTests.cs`

**Goal:** 7 endpoints を `DevRelicsController` mirror で実装。potion は base 直接編集なし (override-only) なので `alsoBase` クエリは省略。

- [ ] **Step 5.1: 失敗テストを書く (DevRelicsControllerTests を雛型に)**

`tests/Server.Tests/Controllers/DevPotionsControllerTests.cs` を新規作成。`tests/Server.Tests/Controllers/DevRelicsControllerTests.cs` を参考に下記 7 件の happy path + 主要エラーパスを書く。`TempDataFactory` などの fixture pattern を踏襲。

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class DevPotionsControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;
    public DevPotionsControllerTests(TempDataFactory f) => _factory = f;

    [Fact]
    public async Task GetPotions_returns_at_least_baseline_seven_potions()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/dev/potions");
        res.EnsureSuccessStatusCode();
        var list = await res.Content.ReadFromJsonAsync<DevPotionDto[]>();
        Assert.NotNull(list);
        Assert.Contains(list!, p => p.Id == "fire_potion");
        Assert.Contains(list!, p => p.Id == "health_potion");
    }

    [Fact]
    public async Task NewPotion_creates_override_file()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        var body = new { id = "test_potion", name = "テストポーション" };
        var res = await client.PostAsJsonAsync("/api/dev/potions", body);
        res.EnsureSuccessStatusCode();

        // GET で再取得して確認
        var list = await client.GetFromJsonAsync<DevPotionDto[]>("/api/dev/potions");
        Assert.Contains(list!, p => p.Id == "test_potion");
    }

    [Fact]
    public async Task NewPotion_invalid_id_returns_400()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/dev/potions",
            new { id = "Invalid-ID", name = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task NewPotion_existing_id_returns_409()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/dev/potions",
            new { id = "fire_potion", name = "duplicate" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task SaveVersion_adds_new_version_to_override()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        var body = new
        {
            label = "test edit",
            spec = new
            {
                rarity = 1,
                effects = new[] { new { action = "attack", scope = "single", side = "enemy", amount = 30, battleOnly = true } },
                description = (string?)null,
            }
        };
        var res = await client.PostAsJsonAsync("/api/dev/potions/fire_potion/versions", body);
        res.EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<DevPotionDto[]>("/api/dev/potions");
        var fire = list!.First(p => p.Id == "fire_potion");
        Assert.True(fire.Versions.Count >= 2);  // base v1 + 新 v2
    }

    [Fact]
    public async Task PatchActive_switches_activeVersion()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        // 先に v2 を作る
        await client.PostAsJsonAsync("/api/dev/potions/fire_potion/versions",
            new { label = "v2", spec = new { rarity = 2, effects = new object[0] } });
        var res = await client.PatchAsJsonAsync("/api/dev/potions/fire_potion/active",
            new { version = "v2" });
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DeleteVersion_removes_non_active_version()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        // v2 を作って、active のまま v1 を消すのは v1 が active で base 側にあるので失敗パターン。
        // 代わりに v2 作成 → v2 active 切替 → v1 削除を試みる: v1 は base 側なので override から消すだけ (no-op パターン or 404)
        // 実際の挙動は relic と同じ。以下 happy path: v2 を作って、active を v2 に切替、v3 を作って、v3 削除。
        await client.PostAsJsonAsync("/api/dev/potions/fire_potion/versions",
            new { label = "v2", spec = new { rarity = 1, effects = new object[0] } });
        await client.PatchAsJsonAsync("/api/dev/potions/fire_potion/active", new { version = "v2" });
        await client.PostAsJsonAsync("/api/dev/potions/fire_potion/versions",
            new { label = "v3", spec = new { rarity = 1, effects = new object[0] } });
        var res = await client.DeleteAsync("/api/dev/potions/fire_potion/versions/v3");
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DeleteVersion_active_returns_400()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/dev/potions/fire_potion/versions",
            new { label = "v2", spec = new { rarity = 1, effects = new object[0] } });
        await client.PatchAsJsonAsync("/api/dev/potions/fire_potion/active", new { version = "v2" });
        var res = await client.DeleteAsync("/api/dev/potions/fire_potion/versions/v2");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Preview_returns_formatter_text()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        var body = new
        {
            spec = new
            {
                rarity = 1,
                effects = new[] { new { action = "attack", scope = "single", side = "enemy", amount = 25, battleOnly = true } },
                description = (string?)null,
            }
        };
        var res = await client.PostAsJsonAsync("/api/dev/potions/preview", body);
        res.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var desc = doc.RootElement.GetProperty("description").GetString();
        Assert.Contains("25", desc!);  // formatter 出力に effect amount が含まれる
    }

    [Fact]
    public async Task DeletePotion_removes_override()
    {
        _factory.ResetData();
        var client = _factory.CreateClient();
        // 新規 potion 作成
        await client.PostAsJsonAsync("/api/dev/potions",
            new { id = "del_test", name = "Del" });
        var res = await client.DeleteAsync("/api/dev/potions/del_test");
        res.EnsureSuccessStatusCode();

        // 削除後は一覧から消える
        var list = await client.GetFromJsonAsync<DevPotionDto[]>("/api/dev/potions");
        Assert.DoesNotContain(list!, p => p.Id == "del_test");
    }
}
```

- [ ] **Step 5.2: テスト失敗確認 (`DevPotionsController` 未実装)**

```bash
dotnet test tests/Server.Tests/Server.Tests.csproj -c Release --filter "FullyQualifiedName~DevPotionsController" --logger "console;verbosity=minimal"
```

Expected: 全 endpoint 404 (controller 未実装)、全テスト失敗。

- [ ] **Step 5.3: DevPotionsController を実装**

`src/Server/Controllers/DevPotionsController.cs` を新規作成。`src/Server/Controllers/DevRelicsController.cs` の全体構造を copy + 下記置換を行う:

- `Relic` → `Potion` (型名 / 変数名)
- `relic` → `potion` (パス / コメント / エラーメッセージ)
- `RelicJsonException` → `PotionJsonException`
- `RelicJsonLoader` → `PotionJsonLoader`
- `RelicOverrideMerger` → `PotionOverrideMerger`
- `DevRelicWriter` → `DevPotionWriter`
- `RelicsResourcePrefix = "RoguelikeCardGame.Core.Data.Relics."` → `PotionsResourcePrefix = "RoguelikeCardGame.Core.Data.Potions."`
- `DevRelicDto` → `DevPotionDto`
- `DevRelicVersionDto` → `DevPotionVersionDto`
- `[Route("api/dev")]` はそのまま (relic と同じ route base)
- `[HttpGet("relics")]` → `[HttpGet("potions")]` 等、全 `/relics` を `/potions` に置換

省略: relic にある `[HttpDelete("relics/{id}")]` の `alsoBase` クエリと `Promote` endpoint は potion でも同 pattern で残す (将来 base への昇格機能を使う可能性があるため、relic と挙動を揃えておく)。ただし potion 側は base directory がない場合は `alsoBase=true` を 400 で拒否するだけで OK (relic と同じ挙動)。

`displayName` field の扱い: `NewPotion` で `body.DisplayName` を JsonObject に格納、それ以外の version 操作では spec のみ触り displayName は触らない (relic と同じ)。

- [ ] **Step 5.4: テスト通過確認**

```bash
dotnet test tests/Server.Tests/Server.Tests.csproj -c Release --filter "FullyQualifiedName~DevPotionsController" --logger "console;verbosity=minimal"
```

Expected: 10 件 PASS (上記の test メソッド一覧と一致するように調整)。

- [ ] **Step 5.5: 全 Server テスト通過確認 (regression なし)**

```bash
dotnet test tests/Server.Tests/Server.Tests.csproj -c Release --logger "console;verbosity=minimal" 2>&1 | tail -10
```

Expected: 既存 289 + 新 ~10 = ~299 PASS, 0 failures。

- [ ] **Step 5.6: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Server/Controllers/DevPotionsController.cs tests/Server.Tests/Controllers/DevPotionsControllerTests.cs
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(server): DevPotionsController 7 endpoints (Phase 10.5.L2 T5)

DevRelicsController の構造を mirror。
- GET /api/dev/potions (一覧 + base/override マージ)
- POST /api/dev/potions (新規作成)
- POST /api/dev/potions/{id}/versions (version 追加)
- PATCH /api/dev/potions/{id}/active (activeVersion 切替)
- DELETE /api/dev/potions/{id}/versions/{version} (active 不可 / non-active OK)
- POST /api/dev/potions/preview (formatter live preview)
- DELETE /api/dev/potions/{id} (override 削除)

IsDevelopment() ガードで本番では 404。テスト 10 件 (happy + error path)。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 6: Client API client + DTO 型 + DevSpecTypes 拡張

**Files:**
- Modify: `src/Client/src/api/types.ts`
- Modify: `src/Client/src/api/dev.ts`
- Modify: `src/Client/src/screens/dev/DevSpecTypes.ts`

**Goal:** Client 側の TypeScript 型と API client 関数を準備。UI components は次の Task 7+ で実装。

- [ ] **Step 6.1: types.ts に DTO 型を追加**

`src/Client/src/api/types.ts` の relic 型 (`DevRelicDto` / `DevRelicVersionDto`) の直後に同パターンで追加:

```typescript
export type DevPotionDto = {
  id: string
  name: string
  displayName: string | null
  activeVersion: string
  versions: DevPotionVersionDto[]
}

export type DevPotionVersionDto = {
  version: string
  createdAt: string | null
  label: string | null
  spec: string  // raw JSON 文字列、UI 側で parse
}

export type NewPotionRequest = {
  id: string
  name: string
  displayName?: string | null
  templatePotionId?: string | null
}

export type SavePotionVersionRequest = {
  label: string | null
  spec: unknown  // JSON-serializable spec object
}

export type SwitchActivePotionVersionRequest = {
  version: string
}

export type PreviewPotionRequest = {
  spec: unknown
}

export type PreviewPotionResponse = {
  description: string
  flavor: string
  effectText: string
}
```

- [ ] **Step 6.2: dev.ts に API client 関数追加**

`src/Client/src/api/dev.ts` の relic 関数群 (`listDevRelics` / `newDevRelic` / etc.) の直後に同パターンで追加:

```typescript
export async function listDevPotions(): Promise<DevPotionDto[]> {
  return await apiRequest<DevPotionDto[]>('GET', '/dev/potions', {})
}

export async function newDevPotion(body: NewPotionRequest): Promise<{ id: string }> {
  return await apiRequest<{ id: string }>('POST', '/dev/potions', { body })
}

export async function savePotionVersion(id: string, body: SavePotionVersionRequest): Promise<{ newVersion: string }> {
  return await apiRequest<{ newVersion: string }>('POST', `/dev/potions/${encodeURIComponent(id)}/versions`, { body })
}

export async function switchActivePotionVersion(id: string, body: SwitchActivePotionVersionRequest): Promise<{ activeVersion: string }> {
  return await apiRequest<{ activeVersion: string }>('PATCH', `/dev/potions/${encodeURIComponent(id)}/active`, { body })
}

export async function deletePotionVersion(id: string, version: string): Promise<{ deletedVersion: string }> {
  return await apiRequest<{ deletedVersion: string }>('DELETE', `/dev/potions/${encodeURIComponent(id)}/versions/${encodeURIComponent(version)}`, {})
}

export async function previewPotion(body: PreviewPotionRequest): Promise<PreviewPotionResponse> {
  return await apiRequest<PreviewPotionResponse>('POST', '/dev/potions/preview', { body })
}

export async function deletePotion(id: string): Promise<void> {
  await apiRequest<void>('DELETE', `/dev/potions/${encodeURIComponent(id)}`, {})
}
```

(import 文に新 type を追加: `import type { DevPotionDto, NewPotionRequest, SavePotionVersionRequest, SwitchActivePotionVersionRequest, PreviewPotionRequest, PreviewPotionResponse } from './types'` — 既存 import 行と一緒にしても可。)

- [ ] **Step 6.3: DevSpecTypes.ts に PotionVersionSpec 型 + 変換関数追加**

`src/Client/src/screens/dev/DevSpecTypes.ts` の relic 用型 (`RelicVersionSpec` / `parseRelicSpec` / `relicSpecToJson` 等) の直後に同パターンで追加:

```typescript
export type PotionVersionSpec = {
  rarity: number               // 0..3 (Common/Uncommon/Rare/Epic に対応する CardRarity int)
  effects: EffectSpec[]
  description?: string | null  // null/省略時は formatter 自動生成
}

export function parsePotionSpec(rawJson: string): PotionVersionSpec {
  const r = JSON.parse(rawJson)
  return {
    rarity: typeof r.rarity === 'number' ? r.rarity : 0,
    effects: Array.isArray(r.effects) ? r.effects.map((e: unknown) => parseEffect(e)) : [],
    description: typeof r.description === 'string' ? r.description : null,
  }
}

export function potionSpecToJson(spec: PotionVersionSpec): unknown {
  const out: Record<string, unknown> = {
    rarity: spec.rarity,
    effects: spec.effects.map(effectToJson),
  }
  if (spec.description !== null && spec.description !== undefined && spec.description !== '') {
    out.description = spec.description
  }
  return out
}
```

(`EffectSpec` / `parseEffect` / `effectToJson` は既存。)

- [ ] **Step 6.4: tsc + 既存テスト通過確認**

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame/src/Client
npx tsc --noEmit
npm run test:run -- --no-file-parallelism 2>&1 | tail -10
```

Expected: tsc clean, 183 PASS (regression なし)。

- [ ] **Step 6.5: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Client/src/api/types.ts src/Client/src/api/dev.ts src/Client/src/screens/dev/DevSpecTypes.ts
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(client): potion DTO types + API client + DevSpecTypes (Phase 10.5.L2 T6)

types.ts に DevPotionDto / DevPotionVersionDto / NewPotionRequest /
SavePotionVersionRequest / SwitchActivePotionVersionRequest /
PreviewPotionRequest / PreviewPotionResponse を追加。

dev.ts に listDevPotions / newDevPotion / savePotionVersion /
switchActivePotionVersion / deletePotionVersion / previewPotion / deletePotion を追加。

DevSpecTypes.ts に PotionVersionSpec + parsePotionSpec + potionSpecToJson を追加。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 7: EffectEditor に battleOnly トグル追加 (TDD)

**Files:**
- Modify: `src/Client/src/screens/dev/EffectEditor.tsx`
- Modify: `src/Client/src/screens/dev/EffectEditor.test.tsx`

**Goal:** `EffectEditor` の UI に `battleOnly` checkbox を always-on で表示。型定義 (`DevSpecTypes.ts:15`) は既にある。

- [ ] **Step 7.1: 失敗テストを書く**

`src/Client/src/screens/dev/EffectEditor.test.tsx` に新 test を追加 (既存テストの末尾に append):

```typescript
import { describe, it, expect, vi } from 'vitest'
// ... 既存 imports

describe('EffectEditor battleOnly toggle', () => {
  it('renders battleOnly checkbox', () => {
    const onChange = vi.fn()
    render(<EffectEditor effect={{ action: 'attack', scope: 'single', side: 'enemy', amount: 6, battleOnly: false }} onChange={onChange} />)
    const checkbox = screen.getByLabelText(/戦闘中のみ/) as HTMLInputElement
    expect(checkbox).toBeTruthy()
    expect(checkbox.checked).toBe(false)
  })

  it('toggling checkbox calls onChange with new battleOnly', () => {
    const onChange = vi.fn()
    render(<EffectEditor effect={{ action: 'attack', scope: 'single', side: 'enemy', amount: 6, battleOnly: false }} onChange={onChange} />)
    const checkbox = screen.getByLabelText(/戦闘中のみ/) as HTMLInputElement
    fireEvent.click(checkbox)
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ battleOnly: true }))
  })
})
```

- [ ] **Step 7.2: テスト失敗確認**

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame/src/Client
npm run test:run -- --no-file-parallelism --testNamePattern "battleOnly" 2>&1 | tail -10
```

Expected: 2 tests fail (`getByLabelText(/戦闘中のみ/)` でラベル不在エラー)。

- [ ] **Step 7.3: EffectEditor に battleOnly checkbox を追加**

`src/Client/src/screens/dev/EffectEditor.tsx` の最下部の form 要素 (action / scope / side / amount 等の行) の直後に checkbox 行を追加:

```tsx
<div className="effect-editor__row">
  <label className="effect-editor__field">
    <input
      type="checkbox"
      checked={effect.battleOnly}
      onChange={(e) => onChange({ ...effect, battleOnly: e.target.checked })}
    />
    <span title="true=戦闘中のみ使用可能 / false=どこでも使える (potion 用、card/relic では engine 側で無視)">
      戦闘中のみ
    </span>
  </label>
</div>
```

- [ ] **Step 7.4: テスト通過確認**

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame/src/Client
npm run test:run -- --no-file-parallelism 2>&1 | tail -10
```

Expected: 183 + 2 = 185 PASS, 0 failures。tsc clean。

- [ ] **Step 7.5: 視覚確認 (任意推奨)**

`dev.bat` 起動 → 既存 `/dev/relics` で適当な relic を編集モードにし、effect 行に「戦闘中のみ」checkbox が表示されることを確認 (relic 用には engine 側で無視されるが、混在しても問題ないことを確認)。

- [ ] **Step 7.6: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Client/src/screens/dev/EffectEditor.tsx src/Client/src/screens/dev/EffectEditor.test.tsx
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(dev-editor): battleOnly toggle in EffectEditor (Phase 10.5.L2 T7)

Effect 行に「戦闘中のみ」 checkbox を always-on 表示。potion editor で必須、
card/relic では engine 側で無視 (混在しても害なし)。テスト 2 件追加。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 8: PotionSpecForm 実装 + テスト (TDD)

**Files:**
- Create: `src/Client/src/screens/dev/PotionSpecForm.tsx`
- Create: `src/Client/src/screens/dev/PotionSpecForm.test.tsx`

**Goal:** Potion 固有の編集フォーム。Fields: name / displayName / rarity / description override / effects (EffectListEditor 再利用)。Live preview パネル (formatter 結果 + IsUsableOutsideBattle 判定)。

- [ ] **Step 8.1: 失敗テストを書く**

`src/Client/src/screens/dev/PotionSpecForm.test.tsx` を新規作成:

```typescript
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { PotionSpecForm } from './PotionSpecForm'

describe('PotionSpecForm', () => {
  const baseProps = {
    name: 'テスト',
    displayName: null as string | null,
    spec: { rarity: 1, effects: [], description: null },
    onChange: vi.fn(),
  }

  it('renders name and rarity fields', () => {
    render(<PotionSpecForm {...baseProps} />)
    expect(screen.getByLabelText(/name/i)).toBeTruthy()
    expect(screen.getByLabelText(/rarity/i)).toBeTruthy()
  })

  it('typing into name calls onChange', () => {
    const onChange = vi.fn()
    render(<PotionSpecForm {...baseProps} onChange={onChange} />)
    const input = screen.getByLabelText(/name/i) as HTMLInputElement
    fireEvent.change(input, { target: { value: '新ポーション' } })
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ name: '新ポーション' }))
  })

  it('description override checkbox toggles textarea', () => {
    const onChange = vi.fn()
    render(<PotionSpecForm {...baseProps} onChange={onChange} />)
    const checkbox = screen.getByLabelText(/手動 override/i) as HTMLInputElement
    fireEvent.click(checkbox)
    // toggle 後 spec.description が null → 空文字 (override on で textarea 表示)
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      spec: expect.objectContaining({ description: '' }),
    }))
  })

  it('shows usability indicator (戦闘中のみ vs どこでも)', () => {
    const battleOnlyEffect = { action: 'attack', scope: 'single', side: 'enemy', amount: 20, battleOnly: true }
    render(<PotionSpecForm
      {...baseProps}
      spec={{ rarity: 1, effects: [battleOnlyEffect], description: null }}
    />)
    expect(screen.getByText(/戦闘中のみ/)).toBeTruthy()
  })

  it('shows どこでも when at least one effect is non-battleOnly', () => {
    const heal = { action: 'heal', scope: 'self', amount: 15, battleOnly: false }
    render(<PotionSpecForm
      {...baseProps}
      spec={{ rarity: 1, effects: [heal], description: null }}
    />)
    expect(screen.getByText(/どこでも/)).toBeTruthy()
  })
})
```

- [ ] **Step 8.2: テスト失敗確認**

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame/src/Client
npm run test:run -- --no-file-parallelism --testNamePattern "PotionSpecForm" 2>&1 | tail -10
```

Expected: 5 tests fail (component 未実装)。

- [ ] **Step 8.3: PotionSpecForm を実装**

`src/Client/src/screens/dev/PotionSpecForm.tsx` を新規作成。`RelicSpecForm.tsx` の構造を参考にして、potion 用に簡略化:

```tsx
import type { ChangeEvent } from 'react'
import { EffectListEditor } from './EffectListEditor'
import type { PotionVersionSpec } from './DevSpecTypes'

export type PotionFormState = {
  name: string
  displayName: string | null
  spec: PotionVersionSpec
}

type Props = PotionFormState & {
  onChange: (state: PotionFormState) => void
}

const RARITY_LABELS = ['Common', 'Uncommon', 'Rare', 'Epic']

export function PotionSpecForm(props: Props) {
  const { name, displayName, spec, onChange } = props
  const isUsableOutsideBattle = spec.effects.some((e) => !e.battleOnly)
  const hasDescOverride = spec.description !== null && spec.description !== undefined

  return (
    <div className="potion-spec-form">
      <label className="psf-row">
        <span>name</span>
        <input
          type="text"
          value={name}
          onChange={(e: ChangeEvent<HTMLInputElement>) =>
            onChange({ ...props, name: e.target.value })
          }
        />
      </label>

      <label className="psf-row">
        <span>displayName</span>
        <input
          type="text"
          value={displayName ?? ''}
          onChange={(e) =>
            onChange({ ...props, displayName: e.target.value === '' ? null : e.target.value })
          }
        />
      </label>

      <label className="psf-row">
        <span>rarity</span>
        <select
          value={spec.rarity}
          onChange={(e) =>
            onChange({ ...props, spec: { ...spec, rarity: Number(e.target.value) } })
          }
        >
          {RARITY_LABELS.map((label, idx) => (
            <option key={idx} value={idx}>{label}</option>
          ))}
        </select>
      </label>

      <div className="psf-row">
        <label>
          <input
            type="checkbox"
            checked={hasDescOverride}
            onChange={(e) =>
              onChange({
                ...props,
                spec: { ...spec, description: e.target.checked ? '' : null },
              })
            }
          />
          手動 override description
        </label>
        {hasDescOverride && (
          <textarea
            value={spec.description ?? ''}
            onChange={(e) =>
              onChange({ ...props, spec: { ...spec, description: e.target.value } })
            }
            rows={3}
          />
        )}
      </div>

      <div className="psf-section">
        <h4>Effects</h4>
        <EffectListEditor
          effects={spec.effects}
          onChange={(effects) => onChange({ ...props, spec: { ...spec, effects } })}
        />
      </div>

      <div className="psf-usability">
        使用可能タイミング:{' '}
        <strong>{isUsableOutsideBattle ? 'どこでも' : '戦闘中のみ'}</strong>
      </div>
    </div>
  )
}
```

(CSS class `psf-row` 等は最低限のもの。`DevPotionsScreen.css` で簡易スタイル付ける予定。)

- [ ] **Step 8.4: テスト通過確認**

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame/src/Client
npm run test:run -- --no-file-parallelism --testNamePattern "PotionSpecForm" 2>&1 | tail -10
```

Expected: 5 tests PASS。

- [ ] **Step 8.5: 全 client テスト通過確認**

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame/src/Client
npx tsc --noEmit
npm run test:run -- --no-file-parallelism 2>&1 | tail -10
```

Expected: 185 + 5 = 190 PASS, 0 failures。tsc clean。

- [ ] **Step 8.6: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Client/src/screens/dev/PotionSpecForm.tsx src/Client/src/screens/dev/PotionSpecForm.test.tsx
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(dev-editor): PotionSpecForm component (Phase 10.5.L2 T8)

Potion 固有 form。name / displayName / rarity / description override (checkbox + textarea) /
effects (EffectListEditor 再利用) / 使用可能タイミング表示 (IsUsableOutsideBattle 等価判定)。
テスト 5 件 (render / onChange / override toggle / usability indicator x2)。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 9: DevPotionsScreen 実装

**Files:**
- Create: `src/Client/src/screens/DevPotionsScreen.tsx`
- Create: `src/Client/src/screens/DevPotionsScreen.css`

**Goal:** Screen シェル (左 30% list / 右 70% form)。`DevRelicsScreen` の構造を mirror。version タブ / 保存ボタン / live preview / master 戻すボタンを実装。

- [ ] **Step 9.1: DevPotionsScreen.tsx を実装**

`src/Client/src/screens/DevPotionsScreen.tsx` を新規作成。`src/Client/src/screens/DevRelicsScreen.tsx` の全体構造を参照に、下記方針で書き換え:

- relic API → potion API (`listDevPotions` 等)
- `DevRelicDto` → `DevPotionDto`
- `RelicSpecForm` → `PotionSpecForm`
- `parseRelicSpec` → `parsePotionSpec`
- `relicSpecToJson` → `potionSpecToJson`
- formatter preview API: `previewRelic` → `previewPotion`
- スタイル class prefix: `dev-relics` → `dev-potions`
- 左サイドバー: relic 一覧 → potion 一覧 (master + override)
- 右パネル: 編集中 potion の version タブ + form + live preview + 「master に戻す」ボタン

State machine と debounce ロジックは relic と全く同じパターンで書く (300ms debounce で formatter preview の API call)。

`DevRelicsScreen` 全体を一度通読してから写経する形が現実的 (`DevRelicsScreen.tsx` は 566 行、構造を grep で先に把握すること)。

参考: `git -C "c:/Users/Metaverse/projects/roguelike-cardgame" grep -nE "^export|^function|useState|useEffect" -- src/Client/src/screens/DevRelicsScreen.tsx` で全体構造を把握できる。

- [ ] **Step 9.2: DevPotionsScreen.css を実装**

`src/Client/src/screens/DevPotionsScreen.css` を新規作成。`src/Client/src/screens/DevRelicsScreen.css` をコピーして class prefix を `dev-relics` → `dev-potions` に置換。スタイル変更は不要 (relic と完全に同じ視覚)。

- [ ] **Step 9.3: tsc + 既存テスト通過確認**

**Note (spec 訂正):** Spec Section 5.3 で `DevPotionsScreen.test.tsx` を新規作成と書いたが、L1 の `DevRelicsScreen` も単体テストを持たず (実機能は API endpoints test + 個別 sub-component test で十分カバー)、T9 でも screen 単体テストは作成しない。実機能の検証は T5 の Server endpoints test + T8 の PotionSpecForm test + T10 の manual integration check で網羅する。

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame/src/Client
npx tsc --noEmit
npm run test:run -- --no-file-parallelism 2>&1 | tail -10
```

Expected: tsc clean, 190 PASS (regression なし)。

- [ ] **Step 9.4: Commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Client/src/screens/DevPotionsScreen.tsx src/Client/src/screens/DevPotionsScreen.css
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(dev-screen): DevPotionsScreen (Phase 10.5.L2 T9)

DevRelicsScreen の構造を mirror した potion 編集画面。左 30% に potion 一覧
(master/override 区別表示)、右 70% に version タブ + PotionSpecForm + live preview
(300ms debounce) + 保存ボタン + 「master に戻す」ボタン。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

---

## Task 10: ナビ + ルート + 統合動作確認 + memory 更新

**Files:**
- Modify: `src/Client/src/screens/DevHomeScreen.tsx`
- Modify: `src/Client/src/App.tsx` (or routing 設定箇所)
- Modify: memory ファイル (`MEMORY.md` + `project_phase_status.md`)

**Goal:** DevHomeScreen に「Potions」ボタン追加、`/dev/potions` route 登録、最終統合確認、memory 更新。

- [ ] **Step 10.1: DevHomeScreen にナビ追加**

`src/Client/src/screens/DevHomeScreen.tsx` の `Cards` / `Relics` ボタンと並列で `Potions` ボタンを追加:

```tsx
<button onClick={() => navigate('/dev/potions')}>Potions</button>
```

(既存の relic ボタン箇所を `git grep -nE "Relics" src/Client/src/screens/DevHomeScreen.tsx` で見つけて隣に挿入。)

- [ ] **Step 10.2: App.tsx に route 登録**

`src/Client/src/App.tsx` の `/dev/relics` route の直後に以下を追加:

```tsx
{import.meta.env.DEV && (
  <Route path="/dev/potions" element={<DevPotionsScreen />} />
)}
```

(既存の `/dev/relics` 登録パターンを踏襲。`import { DevPotionsScreen } from './screens/DevPotionsScreen'` を import に追加。)

- [ ] **Step 10.3: tsc + 全テスト通過確認**

```bash
cd c:/Users/Metaverse/projects/roguelike-cardgame/src/Client
npx tsc --noEmit
npm run test:run -- --no-file-parallelism 2>&1 | tail -10
```

Expected: tsc clean, 190 PASS (regression なし)。

- [ ] **Step 10.4: Server + Core 全テスト通過確認 (final sweep)**

```bash
dotnet test tests/Core.Tests/Core.Tests.csproj --logger "console;verbosity=minimal" 2>&1 | tail -10
dotnet test tests/Server.Tests/Server.Tests.csproj -c Release --logger "console;verbosity=minimal" 2>&1 | tail -10
```

Expected: Core 1272 PASS, Server ~299 PASS (T1-T9 累計合計)。

- [ ] **Step 10.5: 動作確認 (manual、推奨)**

`dev.bat` 起動 → ブラウザで dev menu に行き「Potions」ボタンから `/dev/potions` に遷移:
- 一覧に既存 7 ポーション (`fire_potion`, `block_potion`, `health_potion`, `strength_potion`, `swift_potion`, `energy_potion`, `poison_potion`) が表示されることを確認
- `fire_potion` を選択 → form が「ファイアポーション / Common(rarity=1) / attack 20 effect / 戦闘中のみ」を表示
- effect の amount を 25 に変更 → live preview が「敵単体に[N:25]ダメージ」等に更新
- v2 として保存 → version タブに `v2` が現れる
- v2 を active に切替 → game runtime に反映 (戦闘で fire_potion 使用 → 25 ダメージ)
- 「master に戻す」 → override 削除 → 一覧から override 表示が消え master のみ
- 新規 potion 「test_potion」を作成 → reward / merchant に出現するか確認 (要 catalog reload — 自動 rebuild されるはず)
- production build (`cd src/Client && npm run build`) → dist 内の HTML/JS に "DevPotionsScreen" 文字列が含まれないことを確認 (`import.meta.env.DEV` で tree-shake)

- [ ] **Step 10.6: Memory 更新**

`C:\Users\Metaverse\.claude\projects\c--Users-Metaverse-projects-roguelike-cardgame\memory\MEMORY.md` の最初の行を更新:

```markdown
- [フェーズ進捗 (2026-05-10)](project_phase_status.md) — Phase 10.5.L2 (potion editor) 完了。次は 10.5.L3 / 10.5.L4 / 10.6.B-Reroll N回 / Phase 9
```

`C:\Users\Metaverse\.claude\projects\c--Users-Metaverse-projects-roguelike-cardgame\memory\project_phase_status.md` の `**Phase 10.5.M2-Choose**` の上に新エントリ:

```markdown
- **Phase 10.5.L2: Potion Editor** (T1〜T10 全完了、master 直接 commit + push、subagent-driven、2026-05-10)
  - T1: PotionDefinition に DisplayName? / Description? 追加 + PotionJsonLoader versioning 対応
  - T2: PotionOverrideMerger 新規 (RelicOverrideMerger mirror)
  - T3: DevOverrideLoader.LoadPotions / EmbeddedDataLoader / DataCatalogProvider に potion 経路追加
  - T4: DevPotionDto + DevPotionWriter + DI 登録
  - T5: DevPotionsController 7 endpoints (DevRelicsController mirror)
  - T6: Client DTO 型 + dev.ts API client + DevSpecTypes.ts に PotionVersionSpec
  - T7: EffectEditor に battleOnly トグル always-on 追加 (potion で必須、card/relic では engine が無視)
  - T8: PotionSpecForm 新規 (name / displayName / rarity / description override / effects / 使用可能タイミング表示)
  - T9: DevPotionsScreen 新規 (DevRelicsScreen mirror、左 30% list / 右 70% form)
  - T10: ナビ + route + 統合確認 + memory 更新
  - 既存 7 ポーション JSON は flat 形式のまま放置 (PotionJsonLoader が後方互換で吸収)
  - テスト: Core 1263 → 1272 (+9)、Server 289 → ~299 (+10)、Client 183 → 190 (+7)
```

(memory ファイルは git 管理外なので commit 不要。)

- [ ] **Step 10.7: Final commit + push**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" add src/Client/src/screens/DevHomeScreen.tsx src/Client/src/App.tsx
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" commit -m "$(cat <<'EOF'
feat(dev-nav): Potions ナビ + /dev/potions route (Phase 10.5.L2 T10)

DevHomeScreen に Potions ボタン追加、App.tsx に dev-only route 登録
(import.meta.env.DEV ガード)。Phase 10.5.L2 完了。

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" push origin master
```

- [ ] **Step 10.8: working tree clean 確認**

```bash
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" status --short
git -C "c:/Users/Metaverse/projects/roguelike-cardgame" log --oneline origin/master..HEAD
```

Expected: working tree clean (修正待ち無し)、未 push commit 無し (最後の push が反映済)。

---

## Out of Scope (本フェーズ非対応、将来検討)

Spec の Section 6 と同じ:
- Visual preview の高度版 (`PotionVisualPreview.tsx` 専用コンポーネント) — 既存 `PotionSlot` の流用のみ
- `Trigger` / `Keywords` / `VisualKey` field の追加 — 必要になったら別 phase
- Upgrade メカニズム (`UpgradedXxx` fields) — potion は upgrade 概念無し
- Generic 抽象化 (`DevSpecForm<T>`) — L3/L4 着手時に検討
- 既存 7 ポーション JSON の新 schema 移行 — 後方互換で吸収、移行不要
- Catalog hot-reload の改善 — 既存機構流用
- Description formatter の potion 専用拡張 — `CardTextFormatter` 流用、専用 marker 無し
- 多言語対応 — JP 固定継続
- Production 環境への dev menu 露出 — 三段ゲートで完全遮断、自動 e2e なし
