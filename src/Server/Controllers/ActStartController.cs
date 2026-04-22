using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/act-start")]
public sealed class ActStartController : ControllerBase
{
    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly RunStartService _runStart;
    private readonly DataCatalog _data;

    public ActStartController(IAccountRepository accounts, ISaveRepository saves, RunStartService runStart, DataCatalog data)
    {
        _accounts = accounts;
        _saves = saves;
        _runStart = runStart;
        _data = data;
    }

    [HttpPost("choose")]
    public async Task<IActionResult> Choose([FromBody] ActStartChooseRequestDto body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrEmpty(body.RelicId)) return BadRequest();
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (s.ActiveActStartRelicChoice is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "act-start relic 選択中ではありません。");

        RunState updated;
        try
        {
            updated = ActStartActions.ChooseRelic(s, body.RelicId, _data);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ex.Message);
        }

        if (!updated.VisitedNodeIds.Contains(updated.CurrentNodeId))
            updated = updated with { VisitedNodeIds = updated.VisitedNodeIds.Add(updated.CurrentNodeId) };
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
