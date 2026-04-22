using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
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

    /// <summary>
    /// スタートマスを踏んだ時点で呼ぶ。まだ選択肢が未生成であれば、現アクトの
    /// ActStartRelicPool から 3 択を決定的に生成して ActiveActStartRelicChoice に設定する。
    /// 既に選択肢が存在する／スタートマスを既に通過済みの場合は 409。
    /// </summary>
    [HttpPost("enter")]
    public async Task<IActionResult> Enter(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (s.ActiveActStartRelicChoice is not null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "既に層開始レリック選択中です。");

        var map = _runStart.RehydrateMap(s.RngSeed, s.CurrentAct);
        if (s.CurrentNodeId != map.StartNodeId)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "スタートマスにいません。");
        if (s.VisitedNodeIds.Contains(s.CurrentNodeId))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "スタートマスは通過済みです。");

        // (rngSeed, act) から決定的にレリック選択 RNG を派生させる
        var derivedSeed = unchecked((int)(uint)ActMapSeed.Derive(s.RngSeed, s.CurrentAct));
        var rng = new SystemRng(unchecked(derivedSeed ^ 0x5ECF));
        RunState updated;
        try
        {
            var choice = ActStartActions.GenerateChoices(s, s.CurrentAct, _data, rng);
            updated = s with
            {
                ActiveActStartRelicChoice = choice,
                SavedAtUtc = DateTimeOffset.UtcNow,
            };
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        await _saves.SaveAsync(accountId, updated, ct);
        return Ok(RunSnapshotDtoMapper.From(updated, map, _data));
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
