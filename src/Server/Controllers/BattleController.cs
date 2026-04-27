using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
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
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

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
                existing.State, Array.Empty<BattleEvent>()));
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

        // §4-1: state と rng を組で保持。以降の PlayCard / EndTurn は同じ rng instance を
        // 受け取り、決定性 (BattleDeterminismTests と同等) を維持する。
        _sessions.Set(accountId, new BattleSession(state, rng));

        return Ok(BattleStateDtoMapper.ToActionResponse(state, events));
    }

    /// <summary>
    /// spec §2-3: リロード時の State 復元用。session がなければ 404
    /// (Client は POST /start で再開始する)。
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "戦闘セッションが存在しません。");

        return Ok(BattleStateDtoMapper.ToDto(session.State));
    }

    /// <summary>
    /// spec §2-4 / §4-5: 手札からカードプレイ。BattleEngine.PlayCard 呼出、
    /// InvalidOperationException (cost 不足 / handIndex 範囲外 / Phase 不正) +
    /// IndexOutOfRangeException (target index 負値) +
    /// ArgumentException を 400 に変換。
    /// </summary>
    [HttpPost("play-card")]
    public async Task<IActionResult> PlayCard(
        [FromBody] PlayCardRequestDto body, CancellationToken ct)
    {
        if (body is null) return BadRequest();
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");

        try
        {
            var (newState, events) = BattleEngine.PlayCard(
                session.State, body.HandIndex, body.TargetEnemyIndex, body.TargetAllyIndex,
                session.Rng, _data);
            _sessions.Set(accountId, session with { State = newState });
            return Ok(BattleStateDtoMapper.ToActionResponse(newState, events));
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// spec §2-5: ターン終了。BattleEngine.EndTurn 呼出で
    /// PlayerAttacking → EnemyAttacking → 次 PlayerInput (または Resolved) まで進める。
    /// session.Rng を再利用し決定論を維持する (Task 6 follow-up)。
    /// </summary>
    [HttpPost("end-turn")]
    public async Task<IActionResult> EndTurn(CancellationToken ct)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");

        var run = await _saves.TryLoadAsync(accountId, ct);
        if (run is null)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "進行中のランがありません。");

        try
        {
            var (newState, events) = BattleEngine.EndTurn(session.State, session.Rng, _data);
            _sessions.Set(accountId, session with { State = newState });
            return Ok(BattleStateDtoMapper.ToActionResponse(newState, events));
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// spec §2-7: ターゲット切替。BattleEngine.SetTarget 呼出。events 発火なしのため、
    /// レスポンスは <see cref="BattleStateDto"/> のみ (steps 配列なし)。
    /// 不正な Side / 範囲外 / 死亡スロットなどはすべて 400 に変換 (broad catch)。
    /// </summary>
    [HttpPost("set-target")]
    public async Task<IActionResult> SetTarget(
        [FromBody] SetTargetRequestDto body, CancellationToken ct)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;
        if (body is null) return BadRequest();

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");

        if (!Enum.TryParse<ActorSide>(body.Side, out var side))
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: $"不正な Side: {body.Side}");

        try
        {
            var newState = BattleEngine.SetTarget(session.State, side, body.SlotIndex);
            _sessions.Set(accountId, session with { State = newState });
            return Ok(BattleStateDtoMapper.ToDto(newState));
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// spec §2-6: ポーション使用。BattleEngine.UsePotion 呼出。
    /// PlayerInput 限定 / 空 slot / 範囲外などはすべて 400 に変換 (broad catch)。
    /// session.Rng を再利用し決定論を維持する。
    /// </summary>
    [HttpPost("use-potion")]
    public async Task<IActionResult> UsePotion(
        [FromBody] UsePotionRequestDto body, CancellationToken ct)
    {
        if (body is null) return BadRequest();
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");

        try
        {
            var (newState, events) = BattleEngine.UsePotion(
                session.State, body.PotionIndex, body.TargetEnemyIndex, body.TargetAllyIndex,
                session.Rng, _data);
            _sessions.Set(accountId, session with { State = newState });
            return Ok(BattleStateDtoMapper.ToActionResponse(newState, events));
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// 共通 prologue: header から accountId を取得し、アカウント存在を確認する。
    /// 失敗時は <see cref="IActionResult"/> を返し、呼出側はそのまま return する。
    /// </summary>
    private async Task<(string accountId, IActionResult? error)> ResolveAccountAsync(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err))
            return (string.Empty, err);
        if (!await _accounts.ExistsAsync(accountId, ct))
            return (string.Empty, Problem(statusCode: StatusCodes.Status404NotFound,
                title: $"アカウントが見つかりません: {accountId}"));
        return (accountId, null);
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
    /// 注意: この関数は Start でしか呼ばない。PlayCard / EndTurn 等は session.Rng を
    /// 再利用する (spec §4-1 / Issue 1 review)。
    /// </summary>
    private static IRng MakeBattleRng(RunState run) =>
        new SystemRng(unchecked((int)run.RngSeed ^ (int)run.PlaySeconds ^ 0xBA771E));
}
