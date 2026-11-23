using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Server.Services;

public sealed record DevContentKind(
    string Id,
    string Label,
    string Group,
    string SourceDirectory,
    string OverrideDirectory,
    bool CanCreate = true,
    bool CanDelete = true,
    bool SupportsGamePreview = false,
    string? Limitation = null);

public sealed record DevContentSummary(
    string Id,
    string Name,
    bool HasBase,
    bool HasOverride,
    string Revision,
    string? PoolClass = null,
    string? CharacterClass = null);

public sealed record DevContentDocument(
    string Kind,
    string Id,
    string Name,
    JsonNode Document,
    bool HasBase,
    bool HasOverride,
    string Revision,
    IReadOnlyList<string> ReferencedBy);

public sealed class DevContentConflictException : Exception
{
    public DevContentConflictException(string message) : base(message) { }
}

/// <summary>
/// File-backed authoring facade used only by the development workbench. Documents are kept as
/// JSON nodes so fields unknown to the current UI survive round-trips.
/// </summary>
public sealed class DevContentService
{
    private static readonly Regex SafeId = new("^[a-z][a-z0-9_-]*$", RegexOptions.Compiled);
    private readonly object _mutationGate = new();
    private readonly string _repoRoot;
    private readonly DataCatalogProvider _catalogProvider;

    public DevContentService(IWebHostEnvironment env, DataCatalogProvider catalogProvider)
    {
        _repoRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));
        _catalogProvider = catalogProvider;
    }

    public static IReadOnlyList<DevContentKind> Kinds { get; } = new[]
    {
        new DevContentKind("cards", "カード", "プレイヤー", "src/Core/Data/Cards", "cards", SupportsGamePreview: true),
        new DevContentKind("potions", "ポーション", "プレイヤー", "src/Core/Data/Potions", "potions", SupportsGamePreview: true),
        new DevContentKind("relics", "レリック", "プレイヤー", "src/Core/Data/Relics", "relics", SupportsGamePreview: true),
        new DevContentKind("characters", "キャラクター", "登場キャラクター", "src/Core/Data/Characters", "characters", SupportsGamePreview: true),
        new DevContentKind("enemies", "敵キャラクター", "登場キャラクター", "src/Core/Data/Enemies", "enemies", SupportsGamePreview: true),
        new DevContentKind("units", "召喚キャラクター", "登場キャラクター", "src/Core/Data/Units", "units", SupportsGamePreview: true),
        new DevContentKind("encounters", "敵編成", "進行・プール", "src/Core/Data/Encounters", "encounters", SupportsGamePreview: true),
        new DevContentKind("events", "イベント", "進行・プール", "src/Core/Data/Events", "events", SupportsGamePreview: true),
        new DevContentKind("rewardTables", "各層の報酬プール", "進行・プール", "src/Core/Data/RewardTable", "reward-tables", SupportsGamePreview: true),
        new DevContentKind("actStartRelicPools", "各層の開始レリック", "進行・プール", "src/Core/Data/RelicsActStart", "act-start-relic-pools"),
        new DevContentKind("merchantPrices", "商店価格", "進行・プール", "src/Core/Data", "merchant-prices", CanCreate: false, CanDelete: false),
        new DevContentKind("mapConfigs", "マップ生成", "進行・プール", "src/Core/Map/Config", "map-configs", CanCreate: false, CanDelete: false),
        new DevContentKind("statusDefinitions", "状態変化", "ルール定義", "", "", CanCreate: false, CanDelete: false,
            Limitation: "現在はCoreの戦闘ルールと密結合しているため、この画面では参照のみです。JSON化は次段階で実装します。"),
        new DevContentKind("effectDefinitions", "効果・自動翻訳", "ルール定義", "", "", CanCreate: false, CanDelete: false,
            Limitation: "効果の利用先と自動翻訳を確認できます。新しいactionの追加はCore実装を伴うため参照のみです。"),
    };

    public IReadOnlyList<DevContentSummary> List(string kind)
    {
        var info = RequireWritableBackedKind(kind);
        var baseDocuments = ReadBaseDocuments(info);
        var overrides = DevOverrideLoader.Load(_catalogProvider.OverrideRoot, info.OverrideDirectory);
        var ids = baseDocuments.Keys.Concat(overrides.Keys).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
        return ids.Select(id =>
        {
            baseDocuments.TryGetValue(id, out var baseJson);
            overrides.TryGetValue(id, out var overrideJson);
            var current = Merge(info.Id, baseJson, overrideJson)
                ?? throw new InvalidDataException($"Content '{kind}/{id}' has no document.");
            return new DevContentSummary(
                id,
                ReadName(current, id),
                baseJson is not null,
                overrideJson is not null,
                Revision(current),
                ReadOptionalString(current, "poolClass"),
                ReadOptionalString(current, "characterClass"));
        }).ToArray();
    }

    public DevContentDocument Get(string kind, string id)
    {
        var info = RequireWritableBackedKind(kind);
        var baseDocuments = ReadBaseDocuments(info);
        var overrides = DevOverrideLoader.Load(_catalogProvider.OverrideRoot, info.OverrideDirectory);
        baseDocuments.TryGetValue(id, out var baseJson);
        overrides.TryGetValue(id, out var overrideJson);
        var current = Merge(info.Id, baseJson, overrideJson)
            ?? throw new KeyNotFoundException($"Content '{kind}/{id}' was not found.");
        var node = JsonNode.Parse(current)
            ?? throw new InvalidDataException($"Content '{kind}/{id}' is null JSON.");
        return new DevContentDocument(
            kind,
            id,
            ReadName(current, id),
            node,
            baseJson is not null,
            overrideJson is not null,
            Revision(current),
            FindReferences(kind, id));
    }

    public DevContentDocument Save(string kind, string id, JsonElement document, string? expectedRevision)
    {
        var info = RequireWritableBackedKind(kind);
        ValidateRouteId(info, id);
        var node = JsonNode.Parse(document.GetRawText()) as JsonObject
            ?? throw new InvalidDataException("The document must be a JSON object.");
        ValidateDocumentIdentity(info.Id, id, node);
        var normalized = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        lock (_mutationGate)
        {
            DevContentDocument? current = null;
            try { current = Get(kind, id); }
            catch (KeyNotFoundException) { }
            if (expectedRevision is not null && current?.Revision != expectedRevision)
                throw new DevContentConflictException("別の変更が先に保存されています。再読み込みしてからやり直してください。");

            // Build and validate the complete candidate catalog before touching disk.
            _catalogProvider.ValidateCandidate(kind, id, normalized);
            var writer = new DevCardWriter(_catalogProvider.OverrideRoot, subDir: info.OverrideDirectory);
            writer.WriteOverride(id, normalized);
            _catalogProvider.Rebuild();
        }
        return Get(kind, id);
    }

    public DevContentDocument SaveActiveVersionAsStandardV1(
        string kind,
        string id,
        JsonElement document,
        string? expectedRevision)
    {
        if (kind is not ("cards" or "relics"))
            throw new InvalidOperationException("標準v1への整理はカードとレリックだけが対象です。");
        var info = RequireWritableBackedKind(kind);
        ValidateRouteId(info, id);
        var submitted = JsonNode.Parse(document.GetRawText()) as JsonObject
            ?? throw new InvalidDataException("The document must be a JSON object.");
        ValidateDocumentIdentity(info.Id, id, submitted);
        var normalizedNode = NormalizeActiveVersionAsV1(submitted);
        var normalized = normalizedNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        lock (_mutationGate)
        {
            var current = Get(kind, id);
            if (expectedRevision is not null && current.Revision != expectedRevision)
                throw new DevContentConflictException("別の変更が先に保存されています。再読み込みしてからやり直してください。");

            // Validate the complete final document before modifying the tracked source file.
            _catalogProvider.ValidateCandidate(kind, id, normalized);
            var sourceDirectory = Path.Combine(
                _repoRoot,
                info.SourceDirectory.Replace('/', Path.DirectorySeparatorChar));
            var backupRoot = Path.Combine(_repoRoot, "data-local", "backups");
            var writer = new DevCardWriter(
                _catalogProvider.OverrideRoot,
                baseDir: sourceDirectory,
                backupRoot: backupRoot,
                subDir: info.OverrideDirectory);
            writer.WriteBaseWithBackup(id, normalized);
            writer.DeleteOverride(id);
            _catalogProvider.RegisterAuthoringBase(kind, id, normalized);
        }
        return Get(kind, id);
    }

    public void DeleteOverride(string kind, string id, string? expectedRevision)
    {
        var info = RequireWritableBackedKind(kind);
        if (!info.CanDelete) throw new InvalidOperationException($"'{kind}' cannot be deleted.");
        lock (_mutationGate)
        {
            var current = Get(kind, id);
            if (!current.HasOverride) throw new KeyNotFoundException($"Override '{kind}/{id}' was not found.");
            if (expectedRevision is not null && current.Revision != expectedRevision)
                throw new DevContentConflictException("別の変更が先に保存されています。再読み込みしてからやり直してください。");

            _catalogProvider.ValidateCandidate(kind, id, json: null);
            var writer = new DevCardWriter(_catalogProvider.OverrideRoot, subDir: info.OverrideDirectory);
            writer.DeleteOverride(id);
            _catalogProvider.Rebuild();
        }
    }

    public void Validate(string kind, string id, JsonElement document)
    {
        var info = RequireWritableBackedKind(kind);
        ValidateRouteId(info, id);
        var node = JsonNode.Parse(document.GetRawText()) as JsonObject
            ?? throw new InvalidDataException("The document must be a JSON object.");
        ValidateDocumentIdentity(info.Id, id, node);
        _catalogProvider.ValidateCandidate(kind, id, node.ToJsonString());
    }

    private IReadOnlyList<string> FindReferences(string selectedKind, string selectedId)
    {
        var result = new List<string>();
        foreach (var kind in Kinds.Where(kind => !string.IsNullOrEmpty(kind.SourceDirectory)))
        {
            // 種別ごとに基礎データと override を 1 回だけ読む（文書ごとの再読込は O(n²) になる）。
            var info = RequireWritableBackedKind(kind.Id);
            var bases = ReadBaseDocuments(info);
            var overrides = DevOverrideLoader.Load(_catalogProvider.OverrideRoot, info.OverrideDirectory);
            foreach (var summary in List(kind.Id))
            {
                if (kind.Id == selectedKind && summary.Id == selectedId) continue;
                bases.TryGetValue(summary.Id, out var baseJson);
                overrides.TryGetValue(summary.Id, out var overrideJson);
                var current = Merge(info.Id, baseJson, overrideJson);
                if (current is null) continue;
                var document = JsonNode.Parse(current);
                if (document is not null && ContainsString(document, selectedId))
                    result.Add($"{kind.Label}: {summary.Name} ({summary.Id})");
            }
        }
        return result.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private Dictionary<string, string> ReadBaseDocuments(DevContentKind info)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var directory = Path.Combine(_repoRoot, info.SourceDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (info.Id == "merchantPrices")
        {
            var path = Path.Combine(directory, "merchant-prices.json");
            if (File.Exists(path)) result["merchant-prices"] = File.ReadAllText(path);
            return result;
        }
        if (!Directory.Exists(directory)) return result;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var json = File.ReadAllText(path);
            var id = ExtractId(info.Id, json, Path.GetFileNameWithoutExtension(path));
            if (id is not null) result[id] = json;
        }
        return result;
    }

    private static string? ExtractId(string kind, string json, string fallback)
    {
        if (kind == "merchantPrices") return "merchant-prices";
        if (kind == "mapConfigs") return fallback;
        using var doc = JsonDocument.Parse(json);
        if (kind == "actStartRelicPools" && doc.RootElement.TryGetProperty("act", out var act) && act.TryGetInt32(out var value))
            return $"act{value}";
        return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }

    private static string? Merge(string kind, string? baseJson, string? overrideJson)
    {
        if (baseJson is null) return overrideJson;
        if (overrideJson is null) return baseJson;
        return kind switch
        {
            "cards" => CardOverrideMerger.Merge(baseJson, overrideJson),
            "relics" => RelicOverrideMerger.Merge(baseJson, overrideJson),
            _ => overrideJson,
        };
    }

    private static string ReadName(string json, string fallback)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("displayName", out var display) && display.ValueKind == JsonValueKind.String)
            return display.GetString() ?? fallback;
        if (doc.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            return name.GetString() ?? fallback;
        return fallback;
    }

    private static string? ReadOptionalString(string json, string propertyName)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static void ValidateRouteId(DevContentKind info, string id)
    {
        if (!SafeId.IsMatch(id)) throw new InvalidDataException("IDは英小文字から始まる英小文字・数字・_・-で指定してください。");
        if (!info.CanCreate)
        {
            var expected = info.Id == "merchantPrices" ? "merchant-prices" : "map-act1";
            if (id != expected) throw new InvalidDataException($"'{info.Id}' のIDは '{expected}' 固定です。");
        }
    }

    private static void ValidateDocumentIdentity(string kind, string id, JsonObject document)
    {
        if (kind == "actStartRelicPools")
        {
            var act = document["act"]?.GetValue<int>()
                ?? throw new InvalidDataException("act is required.");
            if (id != $"act{act}") throw new InvalidDataException("URLのIDとactが一致しません。");
            return;
        }
        if (kind is "merchantPrices" or "mapConfigs") return;
        var documentId = document["id"]?.GetValue<string>();
        if (documentId != id) throw new InvalidDataException("URLのIDとdocument.idが一致しません。");
    }

    private static JsonObject NormalizeActiveVersionAsV1(JsonObject document)
    {
        var activeVersion = document["activeVersion"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(activeVersion))
            throw new InvalidDataException("使用中バージョンが設定されていません。");
        var versions = document["versions"] as JsonArray
            ?? throw new InvalidDataException("versions must be an array.");
        var active = versions
            .OfType<JsonObject>()
            .FirstOrDefault(version => version["version"]?.GetValue<string>() == activeVersion)
            ?? throw new InvalidDataException($"使用中バージョン '{activeVersion}' が versions にありません。");
        var spec = active["spec"] as JsonObject
            ?? throw new InvalidDataException("使用中バージョンの spec がありません。");

        var normalized = (JsonObject)document.DeepClone();
        normalized["activeVersion"] = "v1";
        normalized["versions"] = new JsonArray
        {
            new JsonObject
            {
                ["version"] = "v1",
                ["spec"] = spec.DeepClone(),
            },
        };
        return normalized;
    }

    private static bool ContainsString(JsonNode? node, string value)
    {
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text)) return text == value;
        if (node is JsonArray array) return array.Any(child => ContainsString(child, value));
        if (node is JsonObject obj) return obj.Any(pair => ContainsString(pair.Value, value));
        return false;
    }

    private static string Revision(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant()[..16];

    private static DevContentKind RequireWritableBackedKind(string kind)
        => Kinds.FirstOrDefault(item => item.Id == kind && !string.IsNullOrEmpty(item.SourceDirectory))
           ?? throw new KeyNotFoundException($"Content kind '{kind}' was not found.");
}
