using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/history")]
public sealed class HistoryController : ControllerBase
{
    private readonly IAccountRepository _accounts;
    private readonly IHistoryRepository _history;

    public HistoryController(IAccountRepository accounts, IHistoryRepository history)
    {
        _accounts = accounts;
        _history = history;
    }

    /// <summary>アカウントの全ラン履歴を返す（新しい順）。</summary>
    [HttpGet("")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!TryAcc(out var acc, out var err)) return err!;
        if (!await _accounts.ExistsAsync(acc, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "アカウントなし");

        var list = await _history.ListAsync(acc, ct);
        var dtos = new List<RunResultDto>(list.Count);
        foreach (var rec in list) dtos.Add(RunSnapshotDtoMapper.ToResultDto(rec));
        return Ok(dtos);
    }

    /// <summary>アカウントの直近ランの結果を返す。履歴がなければ 204。</summary>
    [HttpGet("last-result")]
    public async Task<IActionResult> LastResult(CancellationToken ct)
    {
        if (!TryAcc(out var acc, out var err)) return err!;
        if (!await _accounts.ExistsAsync(acc, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "アカウントなし");

        var list = await _history.ListAsync(acc, ct);
        if (list.Count == 0) return NoContent();
        return Ok(RunSnapshotDtoMapper.ToResultDto(list[0]));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private bool TryAcc(out string id, out IActionResult? err)
    {
        id = string.Empty;
        err = null;

        if (!Request.Headers.TryGetValue(RunsController.AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            err = Problem(statusCode: StatusCodes.Status400BadRequest, title: "account header missing");
            return false;
        }

        id = raw.ToString();
        try { AccountIdValidator.Validate(id); }
        catch (System.ArgumentException ex)
        {
            err = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            return false;
        }

        return true;
    }
}
