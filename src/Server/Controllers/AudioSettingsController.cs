using System;
using System.Text.Json.Serialization;
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

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AudioSettingsDto(
        [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] int Master,
        [property: JsonRequired] int Bgm,
        [property: JsonRequired] int Se,
        [property: JsonRequired] int Voice);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _settings.GetOrDefaultAsync(accountId, ct);
        return Ok(new AudioSettingsDto(s.SchemaVersion, s.Master, s.Bgm, s.Se, s.Voice));
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] AudioSettingsDto dto, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (dto is null) return BadRequest(new { error = "body required" });
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        if (dto.SchemaVersion != AudioSettings.CurrentSchemaVersion)
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Unsupported schemaVersion: {dto.SchemaVersion} (current: {AudioSettings.CurrentSchemaVersion})");

        AudioSettings settings;
        try
        {
            settings = AudioSettings.Create(dto.Master, dto.Bgm, dto.Se, dto.Voice);
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
