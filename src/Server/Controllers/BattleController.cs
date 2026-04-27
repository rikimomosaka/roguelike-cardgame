using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

/// <summary>
/// Phase 10.3-MVP: in-memory <see cref="BattleSessionStore"/> 上で BattleEngine を駆動する戦闘 API。
/// spec §2-2 / §4 / §6-2 参照。Tasks 5-10 で PlayCard / EndTurn / SetTarget / UsePotion / Finalize を追加予定。
/// </summary>
[ApiController]
[Route("api/v1/runs/current/battle")]
public sealed class BattleController : ControllerBase
{
    public const string AccountHeader = "X-Account-Id";

    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly DataCatalog _data;
    private readonly BattleSessionStore _sessions;
    private readonly RunStartService _runStart;
    private readonly IHistoryRepository _history;
    private readonly IBestiaryRepository _bestiary;

    public BattleController(
        IAccountRepository accounts,
        ISaveRepository saves,
        DataCatalog data,
        BattleSessionStore sessions,
        RunStartService runStart,
        IHistoryRepository history,
        IBestiaryRepository bestiary)
    {
        _accounts = accounts;
        _saves = saves;
        _data = data;
        _sessions = sessions;
        _runStart = runStart;
        _history = history;
        _bestiary = bestiary;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: $"アカウントが見つかりません: {accountId}");

        var run = await _saves.TryLoadAsync(accountId, ct);
        if (run is null || run.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "進行中のランがありません。");
        if (run.ActiveBattle is null)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "進行中の戦闘がありません。");

        // 冪等性: 既存セッションがあれば events 空でそのまま返す (リロード対応)
        if (_sessions.TryGet(accountId, out var existing))
        {
            return Ok(BattleStateDtoMapper.ToActionResponse(
                existing, Array.Empty<BattleEvent>()));
        }

        var rng = MakeBattleRng(run);
        var (state, events) = BattleEngine.Start(run, run.ActiveBattle.EncounterId, rng, _data);

        // F1 (spec §6-2): 図鑑に新敵を記録。BattlePlaceholder.Start 経路で既に
        // 記録済の場合でも NoteEnemiesEncountered は集合の和なので冪等。
        // セッション登録より前に save するのは、SaveAsync が失敗した場合に
        // _sessions.Set を実行しないことで「冪等分岐に入って図鑑記録だけ抜ける」
        // 状態を防ぐため。再試行で完全フローが回る。
        if (_data.TryGetEncounter(run.ActiveBattle.EncounterId, out var encounter))
        {
            var noted = BestiaryTracker.NoteEnemiesEncountered(run, encounter.EnemyIds);
            if (!ReferenceEquals(noted, run))
                await _saves.SaveAsync(accountId, noted, ct);
        }

        _sessions.Set(accountId, state);

        return Ok(BattleStateDtoMapper.ToActionResponse(state, events));
    }

    /// <summary>
    /// spec §2-3: リロード時の State 復元用。session がなければ 404
    /// (Client は POST /start で再開始する)。
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: $"アカウントが見つかりません: {accountId}");

        if (!_sessions.TryGet(accountId, out var state))
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "戦闘セッションが存在しません。");

        return Ok(BattleStateDtoMapper.ToDto(state));
    }

    private bool TryGetAccountId(out string accountId, out IActionResult? err)
    {
        accountId = string.Empty;
        err = null;
        if (!Request.Headers.TryGetValue(AccountHeader, out var v) || string.IsNullOrWhiteSpace(v))
        {
            err = Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"ヘッダ {AccountHeader} が必要です。");
            return false;
        }
        var candidate = v.ToString();
        try { AccountIdValidator.Validate(candidate); }
        catch (ArgumentException ex)
        {
            err = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            return false;
        }
        accountId = candidate;
        return true;
    }

    /// <summary>
    /// 戦闘用 RNG。run の RngSeed / PlaySeconds / 戦闘マジック (0xBA771E) を XOR して
    /// 同じ run / 同じ進行から同じ shuffle を再現できるようにする。
    /// </summary>
    private static IRng MakeBattleRng(RunState run) =>
        new SystemRng(unchecked((int)run.RngSeed ^ (int)run.PlaySeconds ^ 0xBA771E));
}
