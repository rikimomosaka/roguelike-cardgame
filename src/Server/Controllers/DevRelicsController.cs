using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

/// <summary>
/// 開発者専用 dev menu の relics CRUD API (Phase 10.5.L1)。
/// Card editor (10.5.J/K/M) を relic に mirror。Development 環境でのみ機能し、それ以外は 404。
/// </summary>
[ApiController]
[Route("api/dev")]
public sealed class DevRelicsController : ControllerBase
{
    private const string RelicsResourcePrefix = "RoguelikeCardGame.Core.Data.Relics.";
    private static readonly Regex VersionPattern = new(@"^v(\d+)$", RegexOptions.Compiled);

    private readonly IWebHostEnvironment _env;
    private readonly DevRelicWriter _writer;
    private readonly DataCatalogProvider _provider;

    public DevRelicsController(
        IWebHostEnvironment env,
        DevRelicWriter writer,
        DataCatalogProvider provider)
    {
        _env = env;
        _writer = writer;
        _provider = provider;
    }

    /// <summary>
    /// 全 relic の versioned 構造を一覧で返す。各 version の spec は raw JSON 文字列。
    /// 本番環境では 404。base (embedded resource) と override (disk) をマージして返す。
    /// </summary>
    [HttpGet("relics")]
    public IActionResult GetRelics()
    {
        if (!_env.IsDevelopment()) return NotFound();

        var asm = typeof(DataCatalog).Assembly;
        var result = new List<DevRelicDto>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        // 1) manifest 由来の base relic (+ override マージ)
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(RelicsResourcePrefix) || !name.EndsWith(".json")) continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var baseJson = reader.ReadToEnd();

            string mergedJson = baseJson;
            string? relicId = null;
            try
            {
                using var baseDoc = JsonDocument.Parse(baseJson);
                if (baseDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    baseDoc.RootElement.TryGetProperty("id", out var idEl) &&
                    idEl.ValueKind == JsonValueKind.String)
                {
                    relicId = idEl.GetString();
                    if (!string.IsNullOrEmpty(relicId))
                    {
                        var ovr = _writer.ReadOverride(relicId);
                        if (!string.IsNullOrEmpty(ovr))
                        {
                            try { mergedJson = RelicOverrideMerger.Merge(baseJson, ovr); }
                            catch { mergedJson = baseJson; }
                        }
                    }
                }
            }
            catch (JsonException) { /* skip malformed base */ }

            try
            {
                var dto = ParseDevRelicJson(mergedJson);
                if (dto is not null)
                {
                    result.Add(dto);
                    if (!string.IsNullOrEmpty(relicId)) seenIds.Add(relicId);
                }
            }
            catch (JsonException) { /* skip malformed entries */ }
        }

        // 2) override-only relic (manifest に対応 base が無い ID — 新規作成 relic)
        foreach (var ovrId in _writer.ListOverrideIds())
        {
            if (seenIds.Contains(ovrId)) continue;
            var ovrJson = _writer.ReadOverride(ovrId);
            if (string.IsNullOrEmpty(ovrJson)) continue;
            try
            {
                var dto = ParseDevRelicJson(ovrJson);
                if (dto is not null)
                {
                    result.Add(dto);
                    seenIds.Add(ovrId);
                }
            }
            catch (JsonException) { /* skip malformed */ }
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return Ok(result);
    }

    /// <summary>
    /// POST /api/dev/relics
    /// 新規 relic を override 層に作成。
    /// id validation: <c>^[a-z][a-z0-9_]*$</c>。base + override どちらかに同 id があれば 409。
    /// </summary>
    [HttpPost("relics")]
    public IActionResult NewRelic([FromBody] NewRelicRequest? body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (body is null) return BadRequest(new { error = "body is required." });

        if (string.IsNullOrEmpty(body.Id) ||
            !Regex.IsMatch(body.Id, @"^[a-z][a-z0-9_]*$"))
            return BadRequest(new { error = "Invalid id: must match ^[a-z][a-z0-9_]*$" });

        if (string.IsNullOrEmpty(body.Name))
            return BadRequest(new { error = "name is required." });

        var existingBaseManifest = ReadBaseFromManifest(body.Id);
        var existingBaseDisk = _writer.ReadBase(body.Id);
        var existingOverride = _writer.ReadOverride(body.Id);
        if (existingBaseManifest is not null || existingBaseDisk is not null || existingOverride is not null)
            return Conflict(new { error = $"relic '{body.Id}' already exists" });

        // template clone or default
        JsonNode specNode;
        if (!string.IsNullOrEmpty(body.TemplateRelicId))
        {
            var tmplBase = _writer.ReadBase(body.TemplateRelicId) ?? ReadBaseFromManifest(body.TemplateRelicId);
            var tmplOverride = _writer.ReadOverride(body.TemplateRelicId);
            if (tmplBase is null && tmplOverride is null)
                return BadRequest(new { error = $"template relic '{body.TemplateRelicId}' not found" });

            string mergedJson;
            if (tmplBase is not null && tmplOverride is not null)
                mergedJson = RelicOverrideMerger.Merge(tmplBase, tmplOverride);
            else if (tmplOverride is not null)
                mergedJson = tmplOverride;
            else
                mergedJson = tmplBase!;

            using var tmplDoc = JsonDocument.Parse(mergedJson);
            var tmplRoot = tmplDoc.RootElement;
            if (!tmplRoot.TryGetProperty("activeVersion", out var avEl) ||
                avEl.ValueKind != JsonValueKind.String)
                return BadRequest(new { error = "template activeVersion missing" });
            var activeVer = avEl.GetString();
            if (string.IsNullOrEmpty(activeVer))
                return BadRequest(new { error = "template activeVersion missing" });
            if (!tmplRoot.TryGetProperty("versions", out var vsEl) ||
                vsEl.ValueKind != JsonValueKind.Array)
                return BadRequest(new { error = "template versions missing" });

            string? matchedSpecRaw = null;
            foreach (var v in vsEl.EnumerateArray())
            {
                if (v.TryGetProperty("version", out var verEl) &&
                    verEl.ValueKind == JsonValueKind.String &&
                    verEl.GetString() == activeVer &&
                    v.TryGetProperty("spec", out var sEl))
                {
                    matchedSpecRaw = sEl.GetRawText();
                    break;
                }
            }
            if (matchedSpecRaw is null)
                return BadRequest(new { error = "template active spec not found" });
            specNode = JsonNode.Parse(matchedSpecRaw)
                ?? throw new InvalidOperationException("template spec parse failed");
        }
        else
        {
            // default minimal spec (rarity=1 / effects=[])
            // Phase 10.5.L1.5: relic-level trigger フィールド廃止。
            specNode = new JsonObject
            {
                ["rarity"] = 1,
                ["description"] = "",
                ["effects"] = new JsonArray(),
                ["implemented"] = true,
            };
        }

        var newRelicObj = new JsonObject
        {
            ["id"] = body.Id,
            ["name"] = body.Name,
            ["displayName"] = body.DisplayName,
            ["activeVersion"] = "v1",
            ["versions"] = new JsonArray
            {
                new JsonObject
                {
                    ["version"] = "v1",
                    ["createdAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["label"] = body.TemplateRelicId is { Length: > 0 }
                        ? $"clone of {body.TemplateRelicId}"
                        : "new",
                    ["spec"] = specNode,
                },
            },
        };

        var json = newRelicObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _writer.WriteOverride(body.Id, json);
        _provider.Rebuild();

        return Ok(new { id = body.Id });
    }

    /// <summary>
    /// POST /api/dev/relics/{id}/versions
    /// override に新 version を追加。
    /// </summary>
    [HttpPost("relics/{id}/versions")]
    public IActionResult SaveVersion(string id, [FromBody] SaveRelicVersionRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");
        if (body is null) return BadRequest("body is required.");

        var baseJson = ReadBaseFromManifest(id);
        if (baseJson is null) return NotFound($"relic '{id}' not found in base.");

        var existingOverride = _writer.ReadOverride(id);
        bool isFirstOverride = string.IsNullOrEmpty(existingOverride);

        var nextN = ScanMaxVersionNumber(baseJson, existingOverride) + 1;
        var newVer = $"v{nextN}";

        var overrideObj = ParseOrCreateOverride(existingOverride, id);
        var versionsArr = overrideObj["versions"] as JsonArray ?? new JsonArray();
        overrideObj["versions"] = versionsArr;

        var newEntry = new JsonObject
        {
            ["version"] = newVer,
            ["createdAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["label"] = body.Label,
            ["spec"] = JsonNode.Parse(body.Spec.GetRawText()),
        };
        versionsArr.Add(newEntry);

        if (isFirstOverride || string.IsNullOrEmpty(overrideObj["activeVersion"]?.GetValue<string>()))
        {
            overrideObj["activeVersion"] = newVer;
        }

        var json = overrideObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _writer.WriteOverride(id, json);
        _provider.Rebuild();

        return Ok(new { newVersion = newVer });
    }

    /// <summary>
    /// PATCH /api/dev/relics/{id}/active
    /// override の activeVersion を上書き。
    /// </summary>
    [HttpPatch("relics/{id}/active")]
    public IActionResult SwitchActive(string id, [FromBody] SwitchActiveRelicVersionRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");
        if (body is null || string.IsNullOrEmpty(body.Version))
            return BadRequest("version is required.");

        var baseJson = ReadBaseFromManifest(id);
        if (baseJson is null) return NotFound($"relic '{id}' not found in base.");

        var existingOverride = _writer.ReadOverride(id);
        var allVersions = CollectAllVersions(baseJson, existingOverride);
        if (!allVersions.Contains(body.Version))
            return BadRequest($"version '{body.Version}' does not exist for relic '{id}'.");

        var overrideObj = ParseOrCreateOverride(existingOverride, id);
        overrideObj["activeVersion"] = body.Version;
        if (overrideObj["versions"] is null)
            overrideObj["versions"] = new JsonArray();

        var json = overrideObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _writer.WriteOverride(id, json);
        _provider.Rebuild();

        return Ok(new { activeVersion = body.Version });
    }

    /// <summary>
    /// DELETE /api/dev/relics/{id}/versions/{version}
    /// override から指定 version を削除。active は削除不可。
    /// </summary>
    [HttpDelete("relics/{id}/versions/{version}")]
    public IActionResult DeleteVersion(string id, string version)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");
        if (string.IsNullOrEmpty(version)) return BadRequest("version is required.");

        var existingOverride = _writer.ReadOverride(id);
        if (string.IsNullOrEmpty(existingOverride))
            return NotFound($"no override exists for relic '{id}'.");

        var overrideObj = JsonNode.Parse(existingOverride)?.AsObject();
        if (overrideObj is null)
            return BadRequest("override JSON is malformed.");

        string currentActive = overrideObj["activeVersion"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrEmpty(currentActive))
        {
            var baseJson = ReadBaseFromManifest(id);
            if (baseJson is not null)
            {
                using var baseDoc = JsonDocument.Parse(baseJson);
                if (baseDoc.RootElement.TryGetProperty("activeVersion", out var avEl) &&
                    avEl.ValueKind == JsonValueKind.String)
                {
                    currentActive = avEl.GetString() ?? string.Empty;
                }
            }
        }

        if (version == currentActive)
            return BadRequest($"cannot delete active version '{version}'. switch active first.");

        var versionsArr = overrideObj["versions"] as JsonArray ?? new JsonArray();
        bool removed = false;
        for (int i = versionsArr.Count - 1; i >= 0; i--)
        {
            var entry = versionsArr[i];
            if (entry is null) continue;
            var v = entry["version"]?.GetValue<string>();
            if (v == version)
            {
                versionsArr.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed)
            return NotFound($"version '{version}' not in override of relic '{id}'.");

        overrideObj["versions"] = versionsArr;
        var json = overrideObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _writer.WriteOverride(id, json);
        _provider.Rebuild();

        return Ok(new { deletedVersion = version });
    }

    /// <summary>
    /// POST /api/dev/relics/preview
    /// Relic は description 手書きが基本なので、spec.description が non-empty ならそれを返す。
    /// 空なら effects から CardTextFormatter.FormatEffects を呼んで自動生成 (簡易)。
    /// </summary>
    [HttpPost("relics/preview")]
    public IActionResult Preview([FromBody] PreviewRelicRequest? body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (body is null) return BadRequest(new { error = "body is required." });

        try
        {
            var spec = body.Spec;
            if (spec.ValueKind != JsonValueKind.Object)
                return BadRequest(new { error = "spec must be an object." });

            // M5: 手動 description (フレーバーテキスト) と effects 自動文章化 を結合。
            //  両方ある場合: "{manual}\n{autoEffects}" (フレーバー → 機械的説明 の順)
            //  片方だけならそれを単独で返す
            string manual = "";
            if (spec.TryGetProperty("description", out var descEl) &&
                descEl.ValueKind == JsonValueKind.String)
            {
                manual = descEl.GetString() ?? "";
            }

            string autoText = "";
            if (spec.TryGetProperty("effects", out var effEl) &&
                effEl.ValueKind == JsonValueKind.Array)
            {
                var effects = new List<CardEffect>();
                foreach (var e in effEl.EnumerateArray())
                {
                    var parsed = CardEffectParser.ParseEffect(e, msg =>
                        new RelicJsonException(msg));
                    effects.Add(parsed);
                }
                if (effects.Count > 0)
                    autoText = CardTextFormatter.FormatEffects(effects);
            }

            // M5/M6: Client が層別レイアウト (effect 先頭、点線、flavor 小さめ) で描画
            //  できるよう flavor / effectText を分離して返す。
            //  description は後方互換のため "{auto}\n{manual}" で結合 (effect 上、flavor 下)。
            string combined;
            if (manual.Length > 0 && autoText.Length > 0)
                combined = autoText + "\n" + manual;
            else if (manual.Length > 0)
                combined = manual;
            else
                combined = autoText;

            return Ok(new
            {
                description = combined,
                flavor = manual,
                effectText = autoText,
            });
        }
        catch (RelicJsonException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// DELETE /api/dev/relics/{id}?alsoBase=bool
    /// override file を削除。alsoBase=true なら base file も backup を取って削除。
    /// </summary>
    [HttpDelete("relics/{id}")]
    public IActionResult DeleteRelic(string id, [FromQuery] bool alsoBase = false)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");

        var hasOverride = !string.IsNullOrEmpty(_writer.ReadOverride(id));
        var hasBaseDisk = _writer.ReadBase(id) is not null;
        var hasBaseManifest = ReadBaseFromManifest(id) is not null;
        var hasBase = hasBaseDisk || hasBaseManifest;
        if (!hasOverride && !hasBase) return NotFound(new { error = $"relic '{id}' not found" });

        if (hasOverride) _writer.DeleteOverride(id);
        if (alsoBase && hasBaseDisk)
        {
            _writer.DeleteBaseWithBackup(id);
        }
        _provider.Rebuild();
        return Ok(new { deleted = id, alsoBase });
    }

    /// <summary>
    /// POST /api/dev/relics/{id}/promote
    /// override の version を base JSON に転記、override から削除。
    /// </summary>
    [HttpPost("relics/{id}/promote")]
    public IActionResult Promote(string id, [FromBody] PromoteRelicVersionRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");
        if (body is null || string.IsNullOrEmpty(body.Version))
            return BadRequest("version is required.");

        var existingOverride = _writer.ReadOverride(id);
        if (string.IsNullOrEmpty(existingOverride))
            return NotFound($"no override exists for relic '{id}'.");

        var overrideObj = JsonNode.Parse(existingOverride)?.AsObject();
        if (overrideObj is null) return BadRequest("override JSON is malformed.");

        var versionsArr = overrideObj["versions"] as JsonArray;
        if (versionsArr is null)
            return NotFound($"version '{body.Version}' not found in override.");

        JsonNode? targetEntry = null;
        int targetIdx = -1;
        for (int i = 0; i < versionsArr.Count; i++)
        {
            var entry = versionsArr[i];
            if (entry?["version"]?.GetValue<string>() == body.Version)
            {
                targetEntry = entry;
                targetIdx = i;
                break;
            }
        }
        if (targetEntry is null)
            return NotFound($"version '{body.Version}' not found in override.");

        var baseJsonOnDisk = _writer.ReadBase(id) ?? ReadBaseFromManifest(id);
        if (baseJsonOnDisk is null) return NotFound($"relic '{id}' not found in base.");

        var baseObj = JsonNode.Parse(baseJsonOnDisk)?.AsObject();
        if (baseObj is null) return BadRequest("base JSON is malformed.");

        var baseVersionsArr = baseObj["versions"] as JsonArray ?? new JsonArray();
        bool replaced = false;
        for (int i = 0; i < baseVersionsArr.Count; i++)
        {
            var entry = baseVersionsArr[i];
            if (entry?["version"]?.GetValue<string>() == body.Version)
            {
                baseVersionsArr[i] = targetEntry.DeepClone();
                replaced = true;
                break;
            }
        }
        if (!replaced)
            baseVersionsArr.Add(targetEntry.DeepClone());
        baseObj["versions"] = baseVersionsArr;

        if (body.MakeActiveOnBase)
        {
            baseObj["activeVersion"] = body.Version;
        }

        var newBaseJson = baseObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _writer.WriteBaseWithBackup(id, newBaseJson);

        versionsArr.RemoveAt(targetIdx);
        var remainingActive = overrideObj["activeVersion"]?.GetValue<string>();
        if (versionsArr.Count == 0 && string.IsNullOrEmpty(remainingActive))
        {
            _writer.DeleteOverride(id);
        }
        else if (versionsArr.Count == 0)
        {
            overrideObj["versions"] = versionsArr;
            _writer.WriteOverride(id, overrideObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            overrideObj["versions"] = versionsArr;
            _writer.WriteOverride(id, overrideObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        _provider.Rebuild();
        return Ok(new { promotedVersion = body.Version });
    }

    // ---- internal helpers ----

    private static DevRelicDto? ParseDevRelicJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("id", out var idEl)) return null;
        if (!root.TryGetProperty("name", out var nameEl)) return null;
        if (!root.TryGetProperty("activeVersion", out var avEl)) return null;
        if (!root.TryGetProperty("versions", out var vsEl) || vsEl.ValueKind != JsonValueKind.Array)
            return null;

        var versions = new List<DevRelicVersionDto>();
        foreach (var v in vsEl.EnumerateArray())
        {
            string ver = v.TryGetProperty("version", out var vEl) && vEl.ValueKind == JsonValueKind.String
                ? vEl.GetString() ?? string.Empty
                : string.Empty;
            string? createdAt = v.TryGetProperty("createdAt", out var cEl) && cEl.ValueKind == JsonValueKind.String
                ? cEl.GetString()
                : null;
            string? label = v.TryGetProperty("label", out var lEl) && lEl.ValueKind == JsonValueKind.String
                ? lEl.GetString()
                : null;
            string spec = v.TryGetProperty("spec", out var sEl) ? sEl.GetRawText() : "{}";
            versions.Add(new DevRelicVersionDto(ver, createdAt, label, spec));
        }

        string? displayName = root.TryGetProperty("displayName", out var dnEl)
            && dnEl.ValueKind == JsonValueKind.String
            ? dnEl.GetString()
            : null;

        return new DevRelicDto(
            idEl.GetString() ?? string.Empty,
            nameEl.GetString() ?? string.Empty,
            displayName,
            avEl.GetString() ?? string.Empty,
            versions);
    }

    /// <summary>base relic JSON を embedded resource (manifest) から読む。</summary>
    private static string? ReadBaseFromManifest(string id)
    {
        var asm = typeof(DataCatalog).Assembly;
        var resName = RelicsResourcePrefix + id + ".json";
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int ScanMaxVersionNumber(string baseJson, string? overrideJson)
    {
        int max = 0;
        ScanVersionsInJson(baseJson, ref max);
        if (!string.IsNullOrEmpty(overrideJson))
            ScanVersionsInJson(overrideJson, ref max);
        return max;
    }

    private static void ScanVersionsInJson(string json, ref int max)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            if (!doc.RootElement.TryGetProperty("versions", out var vsEl) ||
                vsEl.ValueKind != JsonValueKind.Array) return;
            foreach (var v in vsEl.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object) continue;
                if (!v.TryGetProperty("version", out var verEl)) continue;
                if (verEl.ValueKind != JsonValueKind.String) continue;
                var s = verEl.GetString();
                if (s is null) continue;
                var m = VersionPattern.Match(s);
                if (!m.Success) continue;
                if (int.TryParse(m.Groups[1].Value, out var n) && n > max)
                    max = n;
            }
        }
        catch (JsonException) { /* skip */ }
    }

    private static HashSet<string> CollectAllVersions(string baseJson, string? overrideJson)
    {
        var set = new HashSet<string>();
        AddVersions(baseJson, set);
        if (!string.IsNullOrEmpty(overrideJson))
            AddVersions(overrideJson, set);
        return set;
    }

    private static void AddVersions(string json, HashSet<string> set)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            if (!doc.RootElement.TryGetProperty("versions", out var vsEl) ||
                vsEl.ValueKind != JsonValueKind.Array) return;
            foreach (var v in vsEl.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object) continue;
                if (!v.TryGetProperty("version", out var verEl)) continue;
                if (verEl.ValueKind != JsonValueKind.String) continue;
                var s = verEl.GetString();
                if (!string.IsNullOrEmpty(s)) set.Add(s);
            }
        }
        catch (JsonException) { /* skip */ }
    }

    private static JsonObject ParseOrCreateOverride(string? json, string id)
    {
        if (!string.IsNullOrEmpty(json))
        {
            var node = JsonNode.Parse(json)?.AsObject();
            if (node is not null)
            {
                if (node["id"] is null) node["id"] = id;
                return node;
            }
        }
        return new JsonObject { ["id"] = id, ["versions"] = new JsonArray() };
    }
}
