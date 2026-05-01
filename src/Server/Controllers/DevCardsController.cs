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
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

/// <summary>
/// 開発者専用 dev menu の cards CRUD API (Phase 10.5.I 読み取り + 10.5.J 編集)。
/// Development 環境でのみ機能し、それ以外では 404。
/// </summary>
[ApiController]
[Route("api/dev")]
public sealed class DevCardsController : ControllerBase
{
    private const string CardsResourcePrefix = "RoguelikeCardGame.Core.Data.Cards.";
    private static readonly Regex VersionPattern = new(@"^v(\d+)$", RegexOptions.Compiled);

    private readonly IWebHostEnvironment _env;
    private readonly DevCardWriter _writer;
    private readonly DataCatalogProvider _provider;

    public DevCardsController(
        IWebHostEnvironment env,
        DevCardWriter writer,
        DataCatalogProvider provider)
    {
        _env = env;
        _writer = writer;
        _provider = provider;
    }

    /// <summary>
    /// 全 card の versioned 構造を一覧で返す。各 version の spec は raw JSON 文字列。
    /// 本番環境では 404。base (embedded resource) と override (disk) をマージして返す。
    /// </summary>
    [HttpGet("cards")]
    public IActionResult GetCards()
    {
        if (!_env.IsDevelopment()) return NotFound();

        var asm = typeof(DataCatalog).Assembly;
        var result = new List<DevCardDto>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        // 1) manifest 由来の base カード (+ override マージ)
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(CardsResourcePrefix) || !name.EndsWith(".json")) continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var baseJson = reader.ReadToEnd();

            string mergedJson = baseJson;
            string? cardId = null;
            try
            {
                using var baseDoc = JsonDocument.Parse(baseJson);
                if (baseDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    baseDoc.RootElement.TryGetProperty("id", out var idEl) &&
                    idEl.ValueKind == JsonValueKind.String)
                {
                    cardId = idEl.GetString();
                    if (!string.IsNullOrEmpty(cardId))
                    {
                        var ovr = _writer.ReadOverride(cardId);
                        if (!string.IsNullOrEmpty(ovr))
                        {
                            try { mergedJson = CardOverrideMerger.Merge(baseJson, ovr); }
                            catch { mergedJson = baseJson; }
                        }
                    }
                }
            }
            catch (JsonException) { /* skip malformed base */ }

            try
            {
                var dto = ParseDevCardJson(mergedJson);
                if (dto is not null)
                {
                    result.Add(dto);
                    if (!string.IsNullOrEmpty(cardId)) seenIds.Add(cardId);
                }
            }
            catch (JsonException)
            {
                // skip malformed entries
            }
        }

        // 2) override-only カード (manifest に対応 base が無い ID — Phase 10.5.K 新規作成カード)
        foreach (var ovrId in _writer.ListOverrideIds())
        {
            if (seenIds.Contains(ovrId)) continue;
            var ovrJson = _writer.ReadOverride(ovrId);
            if (string.IsNullOrEmpty(ovrJson)) continue;
            try
            {
                var dto = ParseDevCardJson(ovrJson);
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
    /// POST /api/dev/cards
    /// 新規カードを override 層に作成 (Phase 10.5.K)。
    /// id validation: <c>^[a-z][a-z0-9_]*$</c>。base + override どちらかに同 id があれば 409。
    /// templateCardId 指定時は当該カードの merged active spec を v1 にコピー、未指定時は default spec
    /// (rarity=1 / cardType=Skill / cost=1 / effects=[]) で v1 作成。
    /// </summary>
    [HttpPost("cards")]
    public IActionResult NewCard([FromBody] NewCardRequest? body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (body is null) return BadRequest(new { error = "body is required." });

        // id validation
        if (string.IsNullOrEmpty(body.Id) ||
            !Regex.IsMatch(body.Id, @"^[a-z][a-z0-9_]*$"))
            return BadRequest(new { error = "Invalid id: must match ^[a-z][a-z0-9_]*$" });

        if (string.IsNullOrEmpty(body.Name))
            return BadRequest(new { error = "name is required." });

        // 既存 base (manifest or disk) / override に同 id が無いか
        var existingBaseManifest = ReadBaseFromManifest(body.Id);
        var existingBaseDisk = _writer.ReadBase(body.Id);
        var existingOverride = _writer.ReadOverride(body.Id);
        if (existingBaseManifest is not null || existingBaseDisk is not null || existingOverride is not null)
            return Conflict(new { error = $"card '{body.Id}' already exists" });

        // template clone or default
        JsonNode specNode;
        if (!string.IsNullOrEmpty(body.TemplateCardId))
        {
            // template の base は manifest 経由 (組み込みカードを引くため)、override は disk
            var tmplBase = _writer.ReadBase(body.TemplateCardId) ?? ReadBaseFromManifest(body.TemplateCardId);
            var tmplOverride = _writer.ReadOverride(body.TemplateCardId);
            if (tmplBase is null && tmplOverride is null)
                return BadRequest(new { error = $"template card '{body.TemplateCardId}' not found" });

            // merged JSON を取得 (両方あれば override で base を上書き)
            string mergedJson;
            if (tmplBase is not null && tmplOverride is not null)
                mergedJson = CardOverrideMerger.Merge(tmplBase, tmplOverride);
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
            // default minimal spec (Skill / cost 1 / effects=[])
            specNode = new JsonObject
            {
                ["rarity"] = 1,
                ["cardType"] = "Skill",
                ["cost"] = 1,
                ["effects"] = new JsonArray(),
            };
        }

        // 新規 versioned JSON 構築
        var newCardObj = new JsonObject
        {
            ["id"] = body.Id,
            ["name"] = body.Name,
            ["displayName"] = body.DisplayName,  // null の場合も明示書込
            ["activeVersion"] = "v1",
            ["versions"] = new JsonArray
            {
                new JsonObject
                {
                    ["version"] = "v1",
                    ["createdAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["label"] = body.TemplateCardId is { Length: > 0 }
                        ? $"clone of {body.TemplateCardId}"
                        : "new",
                    ["spec"] = specNode,
                },
            },
        };

        var json = newCardObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _writer.WriteOverride(body.Id, json);
        _provider.Rebuild();

        return Ok(new { id = body.Id });
    }

    /// <summary>
    /// POST /api/dev/cards/{id}/versions
    /// override に新 version を追加 (id は base + override から最大 v 番号 +1 で自動採番)。
    /// 初 save (override 未存在) なら activeVersion を新 version に設定。
    /// </summary>
    [HttpPost("cards/{id}/versions")]
    public IActionResult SaveVersion(string id, [FromBody] SaveCardVersionRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");
        if (body is null) return BadRequest("body is required.");

        var baseJson = ReadBaseFromManifest(id);
        if (baseJson is null) return NotFound($"card '{id}' not found in base.");

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
    /// PATCH /api/dev/cards/{id}/active
    /// override の activeVersion を上書き。override が無ければ新規作成 (versions は空のまま)。
    /// 指定 version が base + override に存在しなければ 400。
    /// </summary>
    [HttpPatch("cards/{id}/active")]
    public IActionResult SwitchActive(string id, [FromBody] SwitchActiveVersionRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");
        if (body is null || string.IsNullOrEmpty(body.Version))
            return BadRequest("version is required.");

        var baseJson = ReadBaseFromManifest(id);
        if (baseJson is null) return NotFound($"card '{id}' not found in base.");

        var existingOverride = _writer.ReadOverride(id);
        var allVersions = CollectAllVersions(baseJson, existingOverride);
        if (!allVersions.Contains(body.Version))
            return BadRequest($"version '{body.Version}' does not exist for card '{id}'.");

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
    /// DELETE /api/dev/cards/{id}/versions/{version}
    /// override から指定 version を削除。active は削除不可で 400。
    /// 削除後 versions が空になっても override file は残す (activeVersion 情報のため)。
    /// 指定 version が override に存在しなければ 404。
    /// </summary>
    [HttpDelete("cards/{id}/versions/{version}")]
    public IActionResult DeleteVersion(string id, string version)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");
        if (string.IsNullOrEmpty(version)) return BadRequest("version is required.");

        var existingOverride = _writer.ReadOverride(id);
        if (string.IsNullOrEmpty(existingOverride))
            return NotFound($"no override exists for card '{id}'.");

        var overrideObj = JsonNode.Parse(existingOverride)?.AsObject();
        if (overrideObj is null)
            return BadRequest("override JSON is malformed.");

        // 現在の active を解決 (override 優先、未指定なら base)
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
            return NotFound($"version '{version}' not in override of card '{id}'.");

        overrideObj["versions"] = versionsArr;
        var json = overrideObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        _writer.WriteOverride(id, json);
        _provider.Rebuild();

        return Ok(new { deletedVersion = version });
    }

    /// <summary>
    /// POST /api/dev/cards/{id}/promote
    /// override の version を base JSON に転記、override から削除。
    /// base は backup を取ってから上書き。override の versions が空になれば override file ごと削除。
    /// makeActiveOnBase=true なら base.activeVersion も更新。
    /// </summary>
    [HttpPost("cards/{id}/promote")]
    public IActionResult Promote(string id, [FromBody] PromoteCardVersionRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (string.IsNullOrEmpty(id)) return BadRequest("id is required.");
        if (body is null || string.IsNullOrEmpty(body.Version))
            return BadRequest("version is required.");

        var existingOverride = _writer.ReadOverride(id);
        if (string.IsNullOrEmpty(existingOverride))
            return NotFound($"no override exists for card '{id}'.");

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

        // base 読み出し (disk 経由 — promote 後は base を上書きするため manifest だけだと不整合)
        var baseJsonOnDisk = _writer.ReadBase(id);
        if (baseJsonOnDisk is null)
        {
            // disk に無ければ manifest から fallback
            baseJsonOnDisk = ReadBaseFromManifest(id);
        }
        if (baseJsonOnDisk is null) return NotFound($"card '{id}' not found in base.");

        var baseObj = JsonNode.Parse(baseJsonOnDisk)?.AsObject();
        if (baseObj is null) return BadRequest("base JSON is malformed.");

        var baseVersionsArr = baseObj["versions"] as JsonArray ?? new JsonArray();
        // 同 version が base にあれば置換、無ければ追加
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

        // override から該当 version を削除
        versionsArr.RemoveAt(targetIdx);
        // versions が空 + activeVersion も無いなら override file ごと削除
        var remainingActive = overrideObj["activeVersion"]?.GetValue<string>();
        if (versionsArr.Count == 0 && string.IsNullOrEmpty(remainingActive))
        {
            _writer.DeleteOverride(id);
        }
        else if (versionsArr.Count == 0)
        {
            // versions 空、active 残り → override file は残す
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

    private static DevCardDto? ParseDevCardJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("id", out var idEl)) return null;
        if (!root.TryGetProperty("name", out var nameEl)) return null;
        if (!root.TryGetProperty("activeVersion", out var avEl)) return null;
        if (!root.TryGetProperty("versions", out var vsEl) || vsEl.ValueKind != JsonValueKind.Array)
            return null;

        var versions = new List<DevCardVersionDto>();
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
            versions.Add(new DevCardVersionDto(ver, createdAt, label, spec));
        }

        string? displayName = root.TryGetProperty("displayName", out var dnEl)
            && dnEl.ValueKind == JsonValueKind.String
            ? dnEl.GetString()
            : null;

        return new DevCardDto(
            idEl.GetString() ?? string.Empty,
            nameEl.GetString() ?? string.Empty,
            displayName,
            avEl.GetString() ?? string.Empty,
            versions);
    }

    /// <summary>base カード JSON を embedded resource (manifest) から読む。</summary>
    private static string? ReadBaseFromManifest(string id)
    {
        var asm = typeof(DataCatalog).Assembly;
        var resName = CardsResourcePrefix + id + ".json";
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>base + override の versions[] から <c>v\d+</c> 形式の最大番号を返す。無ければ 0。</summary>
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

    /// <summary>override JSON を parse、無ければ id だけ入った最小 object を作る。</summary>
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
