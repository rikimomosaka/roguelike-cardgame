using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;
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
                existing.State, Array.Empty<BattleEvent>(), _data));
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

        return Ok(BattleStateDtoMapper.ToActionResponse(state, events, _data));
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

        return Ok(BattleStateDtoMapper.ToDto(session.State, _data));
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
            return Ok(BattleStateDtoMapper.ToActionResponse(newState, events, _data));
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
            return Ok(BattleStateDtoMapper.ToActionResponse(newState, events, _data));
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
            return Ok(BattleStateDtoMapper.ToDto(newState, _data));
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
            return Ok(BattleStateDtoMapper.ToActionResponse(newState, events, _data));
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// spec §2-8 / §4-6: 戦闘終了確定。BattleEngine.Finalize 呼出 → Victory なら Reward
    /// 生成 (boss + last act は Cleared)、Defeat なら GameOver 履歴記録 + save 削除。
    /// session を必ず削除し、ActiveBattle も null にしてから分岐する。
    /// Victory/Defeat の経路は <see cref="RunsController.PostBattleWin"/> + Abandon を流用。
    /// </summary>
    [HttpPost("finalize")]
    public async Task<IActionResult> Finalize(CancellationToken ct)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");

        if (session.State.Phase != BattlePhase.Resolved)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘がまだ終了していません。");

        var run = await _saves.TryLoadAsync(accountId, ct);
        if (run is null)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "進行中のランがありません。");

        var (afterFinalize, summary) = BattleEngine.Finalize(session.State, run);
        // BattleEngine.Finalize 内で ActiveBattle=null は既に設定済だが念のため明示。
        afterFinalize = afterFinalize with { ActiveBattle = null };
        _sessions.Remove(accountId);

        if (summary.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory)
            return await HandleVictoryAsync(accountId, afterFinalize, run, ct);
        else
            return await HandleDefeatAsync(accountId, afterFinalize, ct);
    }

    /// <summary>
    /// Victory 分岐。ボス + 最終 act → Cleared 履歴記録、それ以外 → Reward 生成。
    /// 既存 <see cref="RunsController.PostBattleWin"/> の Victory 経路をコピー流用。
    /// </summary>
    private async Task<IActionResult> HandleVictoryAsync(
        string accountId, RunState afterFinalize, RunState beforeRun, CancellationToken ct)
    {
        // beforeRun.ActiveBattle は Finalize 前の RunState から取得 (encounter 解決用)。
        var enc = _data.Encounters[beforeRun.ActiveBattle!.EncounterId];
        bool isBoss = enc.Pool.Tier == EnemyTier.Boss;

        var rewardRng = new SystemRng(unchecked(
            (int)beforeRun.RngSeed ^ (int)beforeRun.PlaySeconds ^ 0x5EED));

        // ボス かつ 最終アクト → クリア処理
        if (isBoss && afterFinalize.CurrentAct == RunConstants.MaxAct)
        {
            var finished = ActTransition.FinishRun(afterFinalize, RunProgress.Cleared);
            var clearMap = _runStart.RehydrateMap(finished.RngSeed, finished.CurrentAct);
            var rec = RunHistoryBuilder.From(accountId, finished, clearMap,
                finished.VisitedNodeIds.Length, RunProgress.Cleared);
            await _history.AppendAsync(accountId, rec, ct);
            await _bestiary.MergeAsync(accountId, rec, ct);
            await _saves.DeleteAsync(accountId, ct);
            return Ok(RunSnapshotDtoMapper.ToResultDto(rec));
        }

        RewardRngState newRng;
        RewardState reward;
        if (isBoss)
        {
            // ボス かつ 非最終アクト → BossReward フラグ付き報酬
            var r = BossRewardFlow.GenerateBossReward(afterFinalize, _data, rewardRng)
                ?? throw new InvalidOperationException(
                    $"BossRewardFlow returned null for non-final act {afterFinalize.CurrentAct}.");
            reward = r;
            newRng = afterFinalize.RewardRngState;  // BossRewardFlow は RewardRngState を更新しない
        }
        else
        {
            // 通常エンカウンター
            var (r, nr) = RewardGenerator.Generate(
                new RewardContext.FromEnemy(enc.Pool),
                afterFinalize.RewardRngState,
                ImmutableArray.CreateRange(beforeRun.Relics),
                _data.RewardTables.TryGetValue($"act{beforeRun.CurrentAct}", out var tbl)
                    ? tbl : _data.RewardTables["act1"],
                _data, rewardRng);
            reward = r; newRng = nr;
        }

        var updated = afterFinalize with
        {
            ActiveReward = reward,
            RewardRngState = newRng,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        updated = NonBattleRelicEffects.ApplyOnRewardGenerated(updated, _data);
        // Phase 8: プレイヤーに提示されたカード選択肢を SeenCardBaseIds に追加。
        // ボス報酬は CardChoices が空のため、ガードで no-op 化する。
        if (reward.CardChoices.Length > 0)
            updated = BestiaryTracker.NoteCardsSeen(updated, reward.CardChoices);
        await _saves.SaveAsync(accountId, updated, ct);
        var winMap = _runStart.RehydrateMap(updated.RngSeed, updated.CurrentAct);
        return Ok(RunSnapshotDtoMapper.From(updated, winMap, _data));
    }

    /// <summary>
    /// Defeat 分岐。GameOver で履歴記録 + save 削除 + ResultDto 返却。
    /// 既存 <see cref="RunsController.PostAbandon"/> 同等の経路。
    /// </summary>
    private async Task<IActionResult> HandleDefeatAsync(
        string accountId, RunState afterFinalize, CancellationToken ct)
    {
        var defeatedMap = _runStart.RehydrateMap(afterFinalize.RngSeed, afterFinalize.CurrentAct);
        var rec = RunHistoryBuilder.From(accountId, afterFinalize, defeatedMap,
            afterFinalize.VisitedNodeIds.Length, RunProgress.GameOver);
        await _history.AppendAsync(accountId, rec, ct);
        await _bestiary.MergeAsync(accountId, rec, ct);
        await _saves.DeleteAsync(accountId, ct);
        return Ok(RunSnapshotDtoMapper.ToResultDto(rec));
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
