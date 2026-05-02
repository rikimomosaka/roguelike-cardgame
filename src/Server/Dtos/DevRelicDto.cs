using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// /api/dev/relics で返す read-only な relic 概要 DTO。Phase 10.5.L1。
/// versioned JSON の中身をほぼそのまま返し、spec 部分は raw JSON 文字列で保持して
/// UI 側で構造を保ったまま表示できるようにする。
/// </summary>
public sealed record DevRelicDto(
    string Id,
    string Name,
    string? DisplayName,
    string ActiveVersion,
    IReadOnlyList<DevRelicVersionDto> Versions);

/// <summary>
/// Relic の各 version エントリ。spec は JSON 文字列のまま (UI 側で表示)。
/// </summary>
public sealed record DevRelicVersionDto(
    string Version,
    string? CreatedAt,
    string? Label,
    string Spec);

// ---- mutation request DTOs ----

/// <summary>POST /api/dev/relics/{id}/versions のリクエストボディ。</summary>
public sealed record SaveRelicVersionRequest(string? Label, JsonElement Spec);

/// <summary>PATCH /api/dev/relics/{id}/active のリクエストボディ。</summary>
public sealed record SwitchActiveRelicVersionRequest(string Version);

/// <summary>POST /api/dev/relics/{id}/promote のリクエストボディ。</summary>
public sealed record PromoteRelicVersionRequest(string Version, bool MakeActiveOnBase = false);

/// <summary>POST /api/dev/relics のリクエストボディ。新規 relic 作成。</summary>
public sealed record NewRelicRequest(
    string Id,
    string Name,
    string? DisplayName = null,
    string? TemplateRelicId = null);

/// <summary>POST /api/dev/relics/preview のリクエストボディ。</summary>
public sealed record PreviewRelicRequest(JsonElement Spec);
