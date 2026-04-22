using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Rest;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/rest")]
public sealed class RestController : ControllerBase
{
    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly RunStartService _runStart;
    private readonly DataCatalog _data;

    public RestController(IAccountRepository accounts, ISaveRepository saves, RunStartService runStart, DataCatalog data)
    {
        _accounts = accounts;
        _saves = saves;
        _runStart = runStart;
        _data = data;
    }

    [HttpPost("heal")]
    public async Task<IActionResult> PostHeal(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (!s.ActiveRestPending)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Rest is not pending");

        RunState updated;
        try
        {
            updated = RestActions.Heal(s, _data);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);
        var map = _runStart.RehydrateMap(updated.RngSeed, updated.CurrentAct);
        return Ok(RunSnapshotDtoMapper.From(updated, map, _data));
    }

    [HttpPost("upgrade")]
    public async Task<IActionResult> PostUpgrade([FromBody] RestUpgradeRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (!s.ActiveRestPending)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Rest is not pending");

        RunState updated;
        try
        {
            updated = RestActions.UpgradeCard(s, body.DeckIndex, _data);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);
        var map = _runStart.RehydrateMap(updated.RngSeed, updated.CurrentAct);
        return Ok(RunSnapshotDtoMapper.From(updated, map, _data));
    }

    private bool TryGetAccountId(out string accountId, out IActionResult? error)
    {
        accountId = string.Empty;
        error = null;
        if (!Request.Headers.TryGetValue(RunsController.AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            error = Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"ヘッダ {RunsController.AccountHeader} が必要です。");
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
