using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// /api/dev/cards で返す read-only な card 概要 DTO。Phase 10.5.I。
/// versioned JSON の中身をほぼそのまま返し、spec 部分は raw JSON 文字列で保持して
/// UI 側で構造を保ったまま表示できるようにする。
/// </summary>
public sealed record DevCardDto(
    string Id,
    string Name,
    string? DisplayName,
    string ActiveVersion,
    IReadOnlyList<DevCardVersionDto> Versions);

/// <summary>
/// Card の各 version エントリ。spec は JSON 文字列のまま (UI 側で表示)。
/// </summary>
public sealed record DevCardVersionDto(
    string Version,
    string? CreatedAt,
    string? Label,
    string Spec);

// ---- Phase 10.5.J mutation request DTOs ----

/// <summary>POST /api/dev/cards/{id}/versions のリクエストボディ。</summary>
public sealed record SaveCardVersionRequest(string? Label, JsonElement Spec);

/// <summary>PATCH /api/dev/cards/{id}/active のリクエストボディ。</summary>
public sealed record SwitchActiveVersionRequest(string Version);

/// <summary>POST /api/dev/cards/{id}/promote のリクエストボディ。</summary>
public sealed record PromoteCardVersionRequest(string Version, bool MakeActiveOnBase = false);

// ---- Phase 10.5.K new card creation request DTO ----

/// <summary>POST /api/dev/cards のリクエストボディ。新規カード作成。</summary>
public sealed record NewCardRequest(
    string Id,
    string Name,
    string? DisplayName = null,
    string? TemplateCardId = null);
