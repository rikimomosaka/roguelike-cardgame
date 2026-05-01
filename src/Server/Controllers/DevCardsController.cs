using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Dtos;

namespace RoguelikeCardGame.Server.Controllers;

/// <summary>
/// 開発者専用 dev menu の cards 読み取り API (Phase 10.5.I)。
/// Development 環境でのみ 200 を返し、それ以外では 404。
/// 編集系は Phase 10.5.J で実装する。
/// </summary>
[ApiController]
[Route("api/dev")]
public sealed class DevCardsController : ControllerBase
{
    private const string CardsResourcePrefix = "RoguelikeCardGame.Core.Data.Cards.";

    private readonly IWebHostEnvironment _env;

    public DevCardsController(IWebHostEnvironment env)
    {
        _env = env;
    }

    /// <summary>
    /// 全 card の versioned 構造を一覧で返す。各 version の spec は raw JSON 文字列。
    /// 本番環境では 404。
    /// </summary>
    [HttpGet("cards")]
    public IActionResult GetCards()
    {
        if (!_env.IsDevelopment()) return NotFound();

        var asm = typeof(DataCatalog).Assembly;
        var result = new List<DevCardDto>();
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(CardsResourcePrefix) || !name.EndsWith(".json")) continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            try
            {
                var dto = ParseDevCardJson(json);
                if (dto is not null) result.Add(dto);
            }
            catch (JsonException)
            {
                // skip malformed entries
            }
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return Ok(result);
    }

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
            // spec はそのまま生 JSON 文字列で渡す (UI で構造表示するため)
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
}
