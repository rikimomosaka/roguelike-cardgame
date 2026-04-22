using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/debug")]
public sealed class DebugController : ControllerBase
{
    private readonly IHostEnvironment _env;
    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly IHistoryRepository _history;
    private readonly RunStartService _runStart;
    private readonly DataCatalog _data;

    public DebugController(
        IHostEnvironment env, IAccountRepository a, ISaveRepository s,
        IHistoryRepository h, RunStartService rs, DataCatalog d)
    { _env = env; _accounts = a; _saves = s; _history = h; _runStart = rs; _data = d; }

    [HttpPost("damage")]
    public async Task<IActionResult> Damage([FromBody] DebugDamageRequestDto body, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        if (body is null || body.Amount <= 0) return BadRequest();
        if (!TryAcc(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "アカウントなし");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランなし");

        var damaged = DebugActions.ApplyDamage(s, body.Amount);
        if (damaged.CurrentHp <= 0)
        {
            var finished = ActTransition.FinishRun(damaged, RunProgress.GameOver);
            var rec = RunHistoryBuilder.From(accountId, finished, finished.VisitedNodeIds.Length, RunProgress.GameOver);
            await _history.AppendAsync(accountId, rec, ct);
            await _saves.DeleteAsync(accountId, ct);
            return Ok(RunSnapshotDtoMapper.ToResultDto(rec));
        }

        damaged = damaged with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, damaged, ct);
        var map = _runStart.RehydrateMap(damaged.RngSeed, damaged.CurrentAct);
        return Ok(RunSnapshotDtoMapper.From(damaged, map, _data));
    }

    private bool TryAcc(out string id, out IActionResult? err)
    {
        id = string.Empty; err = null;
        if (!Request.Headers.TryGetValue(RunsController.AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        { err = Problem(statusCode: 400, title: "account header missing"); return false; }
        id = raw.ToString();
        try { AccountIdValidator.Validate(id); }
        catch (ArgumentException ex) { err = Problem(statusCode: 400, title: ex.Message); return false; }
        return true;
    }
}
