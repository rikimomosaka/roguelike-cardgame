using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/runs")]
public sealed class RunsController : ControllerBase
{
    public const string AccountHeader = "X-Account-Id";
    private const long MaxElapsedSecondsPerRequest = 86400L;

    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly RunStartService _runStart;

    public RunsController(IAccountRepository accounts, ISaveRepository saves, RunStartService runStart)
    {
        _accounts = accounts;
        _saves = saves;
        _runStart = runStart;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress) return NoContent();

        var map = _runStart.RehydrateMap(state.RngSeed);
        return Ok(new RunSnapshotDto(state, MapDtoMapper.From(map)));
    }

    [HttpPost("new")]
    public async Task<IActionResult> PostNew([FromQuery] bool force, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var existing = await _saves.TryLoadAsync(accountId, ct);
        if (!force && existing is not null && existing.Progress == RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがあります。force=true で上書き可能。");

        var (state, map) = await _runStart.StartAsync(accountId, ct);
        return Ok(new RunSnapshotDto(state, MapDtoMapper.From(map)));
    }

    [HttpPost("current/move")]
    public async Task<IActionResult> PostMove([FromBody] MoveRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");

        var map = _runStart.RehydrateMap(state.RngSeed);
        RunState updated;
        try
        {
            updated = RunActions.SelectNextNode(state, map, body.NodeId);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        long elapsed = Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
        updated = updated with
        {
            PlaySeconds = state.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        await _saves.SaveAsync(accountId, updated, ct);
        return NoContent();
    }

    [HttpPost("current/abandon")]
    public async Task<IActionResult> PostAbandon([FromBody] HeartbeatRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");

        long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
        var updated = state with
        {
            Progress = RunProgress.Abandoned,
            PlaySeconds = state.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        await _saves.SaveAsync(accountId, updated, ct);
        return NoContent();
    }

    [HttpPost("current/heartbeat")]
    public async Task<IActionResult> PostHeartbeat([FromBody] HeartbeatRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");

        long elapsed = Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
        var updated = state with
        {
            PlaySeconds = state.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        await _saves.SaveAsync(accountId, updated, ct);
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
        try { AccountIdValidator.Validate(candidate); }
        catch (ArgumentException ex)
        {
            error = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            return false;
        }
        accountId = candidate;
        return true;
    }
}
