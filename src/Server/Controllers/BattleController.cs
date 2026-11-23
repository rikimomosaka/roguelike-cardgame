using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Random;
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
        var (startedState, startEvents) = BattleEngine.Start(
            run, run.ActiveBattle.EncounterId, rng, _data);
        var initialCpu = BattleCpuPlanningCoordinator.PlanUntilReady(
            startedState, rng, _data);
        var state = initialCpu.State;
        var events = CombineEvents(
            startEvents,
            initialCpu.Actions.SelectMany(action => action.Events));

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
        string startCommandId = NewCommandId("start");
        var journal = BattleActionJournal.Empty;
        // 敵の計画行動（ターン開始時、味方CPUより先）も再生できるよう記録する。
        var enemyDrafts = BattleActionJournalComposer.EnemyPlanning(
            startedState, _data, startCommandId);
        if (enemyDrafts.Count > 0)
            journal = journal.AppendSuccessfulCommands(enemyDrafts);
        var cpuDrafts = BattleActionJournalComposer.CpuPlanning(
            startedState, initialCpu, _data, startCommandId);
        if (cpuDrafts.Count > 0)
            journal = journal.AppendSuccessfulCommands(cpuDrafts);
        _sessions.Set(accountId, new BattleSession(state, rng)
        {
            ActionJournal = journal,
        });

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

    /// <summary>Returns an append-only replay feed filtered for one participant/actor.</summary>
    [HttpGet("replay")]
    public async Task<IActionResult> GetReplay(
        [FromQuery] int? turn,
        [FromQuery] string? participantId,
        [FromQuery] string? actorInstanceId,
        [FromQuery] long afterSequence = 0,
        [FromQuery] long? throughSequence = null,
        CancellationToken ct = default)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;
        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: "battle session does not exist");

        try
        {
            return Ok(BattleReplayDtoMapper.ToDto(
                session.ActionJournal,
                new BattleActionFilter(
                    Turn: turn,
                    ParticipantId: participantId,
                    ActorInstanceId: actorInstanceId,
                    AfterSequence: afterSequence,
                    ThroughSequence: throughSequence)));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ex.Message);
        }
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
            var before = session.State;
            string participantId = ResolveParticipantId(before, body.ParticipantId);
            string commandFingerprint = CreateCommandFingerprint(
                before, participantId, body.ActorInstanceId, body.ExpectedRevision,
                "playCard",
                ("handIndex", body.HandIndex),
                ("targetEnemyIndex", body.TargetEnemyIndex),
                ("targetAllyIndex", body.TargetAllyIndex),
                ("cardInstanceId", body.CardInstanceId));
            if (ValidateCommandEnvelope(
                    session,
                    participantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "playCard",
                    commandFingerprint,
                    stateOnlyResponse: false,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            if (RejectIfPending(session, participantId) is { } pendingResponse)
                return pendingResponse;
            BattleParticipantAccess.TryGet(before, participantId, out var participant);
            if (body.CardInstanceId is not null)
            {
                string? actualCardInstanceId = body.HandIndex >= 0
                    && body.HandIndex < participant.Hand.Length
                    ? participant.Hand[body.HandIndex].InstanceId
                    : null;
                if (!string.Equals(
                        body.CardInstanceId,
                        actualCardInstanceId,
                        StringComparison.Ordinal))
                    return StaleCommandTarget(
                        body.CardInstanceId, actualCardInstanceId);
            }
            var (commandState, commandEvents) = string.IsNullOrWhiteSpace(body.ParticipantId)
                ? BattleEngine.PlayCard(
                    before, body.HandIndex,
                    body.TargetEnemyIndex, body.TargetAllyIndex,
                    session.Rng, _data)
                : BattleEngine.PlayCard(
                    before, participantId, body.HandIndex,
                    body.TargetEnemyIndex, body.TargetAllyIndex,
                    session.Rng, _data);
            var humanDraft = BattleActionJournalComposer.CardPlay(
                before, commandState, participantId, body.HandIndex,
                commandEvents, _data, commandId);
            var completed = CompleteExternalCommand(
                session, before, commandState, participantId,
                commandEvents, humanDraft, commandId, commandFingerprint);
            _sessions.Set(accountId, completed.Session);
            return Ok(BattleStateDtoMapper.ToActionResponse(
                completed.Session.State, completed.Events, _data));
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// Phase 10.5.M2-Choose: choose modal の選択結果で pending を resolve する。
    /// session.Rng を再利用し決定論を維持する。BattleEngine.ResolveCardChoice を呼び、
    /// 残り effect を適用 → 必要に応じて再 pause、または FinalizeCardPlay まで進める。
    /// 選択数 / 候補不一致 / pending 未設定などはすべて 400 に変換 (broad catch)。
    /// </summary>
    [HttpPost("resolve-card-choice")]
    public async Task<IActionResult> ResolveCardChoice(
        [FromBody] ResolveCardChoiceRequestDto body, CancellationToken ct)
    {
        if (body is null) return BadRequest();
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");

        try
        {
            var before = session.State;
            string participantId = ResolveParticipantId(before, body.ParticipantId);
            string commandFingerprint = CreateCommandFingerprint(
                before, participantId, body.ActorInstanceId, body.ExpectedRevision,
                "resolveCardChoice",
                ("pendingCardInstanceId", body.PendingCardInstanceId),
                ("selectedInstanceIds", BattleCommandFingerprint.NormalizeUnorderedIds(
                    body.SelectedInstanceIds)));
            if (ValidateCommandEnvelope(
                    session,
                    participantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "resolveCardChoice",
                    commandFingerprint,
                    stateOnlyResponse: false,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            BattleParticipantAccess.TryGet(before, participantId, out var participant);
            string? actualPendingCardInstanceId = participant.PendingCardPlay?.CardInstanceId;
            if (body.PendingCardInstanceId is not null
                && !string.Equals(
                    body.PendingCardInstanceId,
                    actualPendingCardInstanceId,
                    StringComparison.Ordinal))
            {
                return StaleCommandTarget(
                    body.PendingCardInstanceId, actualPendingCardInstanceId);
            }
            var selected = (body.SelectedInstanceIds ?? Array.Empty<string>())
                .ToImmutableArray();
            var (commandState, commandEvents) = string.IsNullOrWhiteSpace(body.ParticipantId)
                ? BattleEngine.ResolveCardChoice(
                    before, selected, session.Rng, _data)
                : BattleEngine.ResolveCardChoice(
                    before, participantId, selected, session.Rng, _data);
            var draft = BattleActionJournalComposer.Command(
                before, commandState, "resolveCardChoice", participantId,
                commandEvents, commandId,
                selectedInstanceIds: selected,
                note: actualPendingCardInstanceId,
                catalog: _data);
            var completed = CompleteExternalCommand(
                session, before, commandState, participantId,
                commandEvents, draft, commandId, commandFingerprint);
            _sessions.Set(accountId, completed.Session);
            return Ok(BattleStateDtoMapper.ToActionResponse(
                completed.Session.State, completed.Events, _data));
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
    public async Task<IActionResult> EndTurn(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        BattleCommandRequestDto? body,
        CancellationToken ct)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");
        if (RejectIfPending(session) is { } pendingResp) return pendingResp;

        var run = await _saves.TryLoadAsync(accountId, ct);
        if (run is null)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "進行中のランがありません。");

        try
        {
            // Keep the legacy route retry-safe. A body-less retry could arrive
            // after resolution and accidentally Ready the next turn.
            if (body is null
                || string.IsNullOrWhiteSpace(body.CommandId)
                || body.ExpectedRevision is null)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "end-turn requires commandId and expectedRevision; use set-ready",
                    type: "/errors/battle-command-envelope-required");
            }
            var primary = BattleParticipantAccess.GetPrimary(session.State);
            string participantId = string.IsNullOrWhiteSpace(body.ParticipantId)
                ? primary.ParticipantId
                : body.ParticipantId;
            if (!string.Equals(
                    participantId,
                    primary.ParticipantId,
                    StringComparison.Ordinal))
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "legacy end-turn can only Ready the primary participant");
            }
            RequireHumanParticipant(session.State, participantId);
            string commandFingerprint = CreateCommandFingerprint(
                session.State,
                participantId,
                body.ActorInstanceId,
                body.ExpectedRevision,
                "ready",
                ("isReady", true));
            if (ValidateCommandEnvelope(
                    session,
                    participantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "ready",
                    commandFingerprint,
                    stateOnlyResponse: false,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            return ExecuteReadyChange(
                accountId,
                session,
                participantId,
                isReady: true,
                commandId,
                commandFingerprint);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// Reversibly changes one participant's Ready state. The supplied revision
    /// prevents a stale browser from overwriting actions appended by another
    /// participant. Core performs the all-ready transition atomically.
    /// </summary>
    [HttpPost("set-ready")]
    public async Task<IActionResult> SetReady(
        [FromBody] SetBattleParticipantReadyRequestDto body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ParticipantId))
            return BadRequest();
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;
        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "battle session does not exist");
        try
        {
            RequireHumanParticipant(session.State, body.ParticipantId);
            string commandFingerprint = CreateCommandFingerprint(
                session.State, body.ParticipantId, body.ActorInstanceId,
                body.ExpectedRevision, "ready", ("isReady", body.IsReady));
            if (ValidateCommandEnvelope(
                    session,
                    body.ParticipantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "ready",
                    commandFingerprint,
                    stateOnlyResponse: false,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            return ExecuteReadyChange(
                accountId, session, body.ParticipantId, body.IsReady,
                commandId, commandFingerprint);
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// コンボゲージを消費してタクティカルスキルを使用する。
    /// 使用可否（ゲージ量、phase、outcome、保留中選択）は Core が検証する。
    /// </summary>
    [HttpPost("use-tactical-skill")]
    public async Task<IActionResult> UseTacticalSkill(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        BattleCommandRequestDto? body,
        CancellationToken ct)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");

        try
        {
            body ??= new BattleCommandRequestDto();
            var before = session.State;
            string participantId = ResolveParticipantId(before, body.ParticipantId);
            string commandFingerprint = CreateCommandFingerprint(
                before, participantId, body.ActorInstanceId, body.ExpectedRevision,
                "useCharacterSkill", ("skillId", BattleEngine.TacticalSkillId));
            if (ValidateCommandEnvelope(
                    session,
                    participantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "useCharacterSkill",
                    commandFingerprint,
                    stateOnlyResponse: false,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            var (commandState, commandEvents) = string.IsNullOrWhiteSpace(body.ParticipantId)
                ? BattleEngine.UseCharacterSkill(
                    before, BattleEngine.TacticalSkillId, _data, session.Rng)
                : BattleEngine.UseCharacterSkill(
                    before, participantId, BattleEngine.TacticalSkillId, _data, session.Rng);
            var draft = BattleActionJournalComposer.CharacterSkill(
                before, commandState, participantId,
                BattleEngine.TacticalSkillId, commandEvents, _data, commandId);
            var completed = CompleteExternalCommand(
                session, before, commandState, participantId,
                commandEvents, draft, commandId, commandFingerprint);
            _sessions.Set(accountId, completed.Session);
            return Ok(BattleStateDtoMapper.ToActionResponse(
                completed.Session.State, completed.Events, _data));
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    public sealed record UseCharacterSkillRequestDto(
        string SkillId,
        string? ParticipantId = null,
        string? ActorInstanceId = null,
        string? CommandId = null,
        long? ExpectedRevision = null);

    /// <summary>Uses the skill id defined by the active character catalog entry.</summary>
    [HttpPost("use-character-skill")]
    public async Task<IActionResult> UseCharacterSkill(
        [FromBody] UseCharacterSkillRequestDto body, CancellationToken ct)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;
        if (body is null || string.IsNullOrWhiteSpace(body.SkillId))
            return BadRequest();
        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "battle session does not exist");

        try
        {
            var before = session.State;
            string participantId = ResolveParticipantId(before, body.ParticipantId);
            string commandFingerprint = CreateCommandFingerprint(
                before, participantId, body.ActorInstanceId, body.ExpectedRevision,
                "useCharacterSkill", ("skillId", body.SkillId));
            if (ValidateCommandEnvelope(
                    session,
                    participantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "useCharacterSkill",
                    commandFingerprint,
                    stateOnlyResponse: false,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            var (commandState, commandEvents) = string.IsNullOrWhiteSpace(body.ParticipantId)
                ? BattleEngine.UseCharacterSkill(
                    before, body.SkillId, _data, session.Rng)
                : BattleEngine.UseCharacterSkill(
                    before, participantId, body.SkillId, _data, session.Rng);
            var draft = BattleActionJournalComposer.CharacterSkill(
                before, commandState, participantId,
                body.SkillId, commandEvents, _data, commandId);
            var completed = CompleteExternalCommand(
                session, before, commandState, participantId,
                commandEvents, draft, commandId, commandFingerprint);
            _sessions.Set(accountId, completed.Session);
            return Ok(BattleStateDtoMapper.ToActionResponse(
                completed.Session.State, completed.Events, _data));
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                      or ArgumentException
                                      or IndexOutOfRangeException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
    }

    /// <summary>
    /// コンボゲージを消費してアルティメットスキルを使用する。
    /// 使用可否（ゲージ量、phase、outcome、保留中選択）は Core が検証する。
    /// </summary>
    [HttpPost("use-ultimate-skill")]
    public async Task<IActionResult> UseUltimateSkill(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        BattleCommandRequestDto? body,
        CancellationToken ct)
    {
        var (accountId, err) = await ResolveAccountAsync(ct);
        if (err is not null) return err;

        if (!_sessions.TryGet(accountId, out var session))
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘セッションが存在しません。");

        try
        {
            body ??= new BattleCommandRequestDto();
            var before = session.State;
            string participantId = ResolveParticipantId(before, body.ParticipantId);
            string commandFingerprint = CreateCommandFingerprint(
                before, participantId, body.ActorInstanceId, body.ExpectedRevision,
                "useCharacterSkill", ("skillId", BattleEngine.UltimateSkillId));
            if (ValidateCommandEnvelope(
                    session,
                    participantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "useCharacterSkill",
                    commandFingerprint,
                    stateOnlyResponse: false,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            var (commandState, commandEvents) = string.IsNullOrWhiteSpace(body.ParticipantId)
                ? BattleEngine.UseCharacterSkill(
                    before, BattleEngine.UltimateSkillId, _data, session.Rng)
                : BattleEngine.UseCharacterSkill(
                    before, participantId, BattleEngine.UltimateSkillId, _data, session.Rng);
            var draft = BattleActionJournalComposer.CharacterSkill(
                before, commandState, participantId,
                BattleEngine.UltimateSkillId, commandEvents, _data, commandId);
            var completed = CompleteExternalCommand(
                session, before, commandState, participantId,
                commandEvents, draft, commandId, commandFingerprint);
            _sessions.Set(accountId, completed.Session);
            return Ok(BattleStateDtoMapper.ToActionResponse(
                completed.Session.State, completed.Events, _data));
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
            var before = session.State;
            string participantId = ResolveParticipantId(before, body.ParticipantId);
            string commandFingerprint = CreateCommandFingerprint(
                before, participantId, body.ActorInstanceId, body.ExpectedRevision,
                "setTarget",
                ("side", side.ToString()),
                ("slotIndex", body.SlotIndex));
            if (ValidateCommandEnvelope(
                    session,
                    participantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "setTarget",
                    commandFingerprint,
                    stateOnlyResponse: true,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            if (RejectIfPending(session, participantId) is { } pendingResponse)
                return pendingResponse;
            var commandState = string.IsNullOrWhiteSpace(body.ParticipantId)
                ? BattleEngine.SetTarget(before, side, body.SlotIndex)
                : BattleEngine.SetTarget(
                    before, participantId, side, body.SlotIndex);
            var draft = BattleActionJournalComposer.Command(
                before, commandState, "setTarget", participantId,
                Array.Empty<BattleEvent>(), commandId,
                note: $"{side}:{body.SlotIndex}",
                catalog: _data);
            var completed = CompleteExternalCommand(
                session, before, commandState, participantId,
                Array.Empty<BattleEvent>(), draft, commandId, commandFingerprint);
            _sessions.Set(accountId, completed.Session);
            return Ok(BattleStateDtoMapper.ToDto(completed.Session.State, _data));
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
            var before = session.State;
            string participantId = ResolveParticipantId(before, body.ParticipantId);
            string commandFingerprint = CreateCommandFingerprint(
                before, participantId, body.ActorInstanceId, body.ExpectedRevision,
                "usePotion",
                ("potionIndex", body.PotionIndex),
                ("targetEnemyIndex", body.TargetEnemyIndex),
                ("targetAllyIndex", body.TargetAllyIndex),
                ("potionId", body.PotionId));
            if (ValidateCommandEnvelope(
                    session,
                    participantId,
                    body.ActorInstanceId,
                    body.CommandId,
                    body.ExpectedRevision,
                    commandKind: "usePotion",
                    commandFingerprint,
                    stateOnlyResponse: false,
                    out string commandId) is { } envelopeResponse)
                return envelopeResponse;
            if (RejectIfPending(session, participantId) is { } pendingResponse)
                return pendingResponse;
            BattleParticipantAccess.TryGet(before, participantId, out var participant);
            string? actualPotionId = body.PotionIndex >= 0
                && body.PotionIndex < participant.Potions.Length
                ? participant.Potions[body.PotionIndex]
                : null;
            if (body.PotionId is not null
                && !string.Equals(body.PotionId, actualPotionId, StringComparison.Ordinal))
                return StaleCommandTarget(body.PotionId, actualPotionId);
            var (commandState, commandEvents) = string.IsNullOrWhiteSpace(body.ParticipantId)
                ? BattleEngine.UsePotion(
                    before, body.PotionIndex,
                    body.TargetEnemyIndex, body.TargetAllyIndex,
                    session.Rng, _data)
                : BattleEngine.UsePotion(
                    before, participantId, body.PotionIndex,
                    body.TargetEnemyIndex, body.TargetAllyIndex,
                    session.Rng, _data);
            var draft = BattleActionJournalComposer.Potion(
                before, commandState, participantId, body.PotionIndex,
                commandEvents, _data, commandId);
            var completed = CompleteExternalCommand(
                session, before, commandState, participantId,
                commandEvents, draft, commandId, commandFingerprint);
            _sessions.Set(accountId, completed.Session);
            return Ok(BattleStateDtoMapper.ToActionResponse(
                completed.Session.State, completed.Events, _data));
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
    /// ActiveBattle を null にして永続化し、成功後に session を削除する。
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
        if (RejectIfPending(session) is { } pendingResp) return pendingResp;

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

        IActionResult response;
        if (summary.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory)
            response = await HandleVictoryAsync(accountId, afterFinalize, run, ct);
        else
            response = await HandleDefeatAsync(accountId, afterFinalize, ct);

        // 永続化が失敗・キャンセルされた場合は Resolved session を残し、再試行可能にする。
        _sessions.Remove(accountId);
        return response;
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

        var rewardRng = new SystemRng(RunRngSeed.Derive(
            beforeRun.RngSeed,
            beforeRun.CurrentAct,
            beforeRun.CurrentNodeId,
            RunRngPurpose.BattleReward));

        // ボス かつ 最終アクト → クリア処理
        if (isBoss && afterFinalize.CurrentAct == RunConstants.MaxAct)
        {
            var finished = ActTransition.FinishRun(afterFinalize, RunProgress.Cleared);
            var clearMap = _runStart.RehydrateMap(finished.RngSeed, finished.CurrentAct);
            var rec = RunHistoryBuilder.From(accountId, finished, clearMap,
                RunProgress.Cleared);
            rec = await _history.AppendAsync(accountId, rec, ct);
            await _bestiary.MergeAsync(accountId, rec, ct);
            await _saves.DeleteAsync(accountId, ct);
            return Ok(RunSnapshotDtoMapper.ToResultDto(rec));
        }

        RunState updated;
        if (isBoss)
        {
            // ボス かつ 非最終アクト → BossRewardFlow.Resolve で報酬生成 + OnRewardGenerated 発火
            updated = BossRewardFlow.Resolve(afterFinalize, _data, rewardRng);
            if (updated.ActiveReward is null)
                throw new InvalidOperationException(
                    $"BossRewardFlow.Resolve returned null reward for non-final act {afterFinalize.CurrentAct}.");
            updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        }
        else
        {
            // 通常エンカウンター
            // Phase 10.6.B フォローアップ: cardExclusions に s.Relics (relic IDs) を渡していた
            // バグ修正。card IDs を期待する引数なので relic IDs では一致せず exclusion 機能無効
            // だった。STS 慣例どおり battle reward は deck 重複も許容するので Empty を渡す
            // (EventResolver は別途 deck IDs を渡して event 報酬では dupe 防止する設計)。
            var (reward, newRng) = RewardGenerator.Generate(
                new RewardContext.FromEnemy(enc.Pool),
                afterFinalize.RewardRngState,
                ImmutableArray<string>.Empty,
                _data.RewardTables.TryGetValue($"act{beforeRun.CurrentAct}", out var tbl)
                    ? tbl : _data.RewardTables["act1"],
                _data, rewardRng, afterFinalize);
            updated = RewardActions.AssignReward(afterFinalize, reward, newRng, _data)
                with { SavedAtUtc = DateTimeOffset.UtcNow };
            // Phase 8: プレイヤーに提示されたカード選択肢を SeenCardBaseIds に追加。
            if (updated.ActiveReward!.CardChoices.Length > 0)
                updated = BestiaryTracker.NoteCardsSeen(updated, updated.ActiveReward.CardChoices);
        }
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
            RunProgress.GameOver);
        rec = await _history.AppendAsync(accountId, rec, ct);
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
    /// 戦闘用 RNG。同じ run / act / node から同じ shuffle を再現できるようにする。
    /// 注意: この関数は Start でしか呼ばない。PlayCard / EndTurn 等は session.Rng を
    /// 再利用する (spec §4-1 / Issue 1 review)。
    /// </summary>
    private static IRng MakeBattleRng(RunState run) =>
        new SystemRng(RunRngSeed.Derive(
            run.RngSeed, run.CurrentAct, run.CurrentNodeId, RunRngPurpose.Battle));

    private CompletedExternalCommand CompleteExternalCommand(
        BattleSession session,
        BattleState before,
        BattleState commandState,
        string actingParticipantId,
        IReadOnlyList<BattleEvent> commandEvents,
        BattleActionDraft commandDraft,
        string commandId,
        string commandFingerprint)
    {
        // A resolving effect choice is the only legal command while the
        // queued-turn state machine is paused. Do not initialize, reopen, or
        // plan any CPU participant until the final choice resumes PlayerInput.
        var planning = commandState.PendingTurnResolution is not null
            || commandState.Phase is BattlePhase.PlayerAttacking
                or BattlePhase.EnemyAttacking
            ? new BattleCpuPlanningResult(
                commandState,
                ImmutableArray<CpuCommittedAction>.Empty,
                ImmutableArray<CpuPlanningStop>.Empty,
                ImmutableArray<string>.Empty)
            {
                StateBeforePlanning = commandState,
            }
            : BattleCpuPlanningCoordinator.PlanAfterCommittedAction(
                before, commandState, actingParticipantId, session.Rng, _data);
        var events = CombineEvents(
            commandEvents,
            planning.Actions.SelectMany(action => action.Events));
        var reopenedByCpuAction = planning.Actions
            .SelectMany(action => action.ReopenedParticipantIds)
            .ToHashSet(StringComparer.Ordinal);
        var stateBeforeCpuPlanning = planning.StateBeforePlanning ?? commandState;
        var drafts = new List<BattleActionDraft>
        {
            commandDraft with
            {
                FeedChanges = planning.ReopenedParticipantIds
                    .Where(id => !reopenedByCpuAction.Contains(id))
                    .Select(id => new BattleReplayFeedChange(
                        id, IsReady: false, IsComplete: null))
                    .ToArray(),
                // Participant initialization and Ready reopens occur before the
                // first CPU decision. Keep that causal boundary on the human
                // command so the following CPU snapshot starts continuously.
                StateAfter = BattleStateDtoMapper.ToDto(
                    stateBeforeCpuPlanning, _data),
            },
        };
        drafts.AddRange(BattleActionJournalComposer.CpuPlanning(
            stateBeforeCpuPlanning, planning, _data, commandId));
        var journal = session.ActionJournal.AppendSuccessfulCommands(drafts);
        return new CompletedExternalCommand(
            session with
            {
                State = planning.State,
                ActionJournal = journal,
                CommandReceipts = BattleCommandReceiptStore.Add(
                    session.CommandReceipts, commandId, commandFingerprint),
            },
            events);
    }

    private IActionResult ExecuteReadyChange(
        string accountId,
        BattleSession session,
        string participantId,
        bool isReady,
        string? requestedCommandId = null,
        string? commandFingerprint = null)
    {
        var before = session.State;
        string commandId = requestedCommandId ?? NewCommandId("ready");
        var journalDrafts = new List<BattleActionDraft>();
        var transition = BattleEngine.SetParticipantReadyDetailed(
            before, participantId, isReady, session.Rng, _data);
        var readyInput = transition.PreReadyCpuPlanning?.State ?? before;
        var after = transition.State;
        var afterReady = transition.StateAfterReady;
        var afterResolution =
            transition.StateAfterResolutionBeforeNextTurnPlanning;

        if (transition.PreReadyCpuPlanning is { } preReadyPlanning)
        {
            journalDrafts.AddRange(BattleActionJournalComposer.CpuPlanning(
                before, preReadyPlanning, _data, $"{commandId}:pre-ready"));
        }

        long readyRevisionAfter = Math.Min(
            afterReady.ActionRevision,
            readyInput.ActionRevision >= long.MaxValue
                ? long.MaxValue
                : Math.Max(0, readyInput.ActionRevision) + 1);
        journalDrafts.Add(BattleActionJournalComposer.Command(
            readyInput,
            afterReady,
            "ready",
            participantId,
            transition.ReadyEvents,
            commandId,
            feedChanges:
            [
                new BattleReplayFeedChange(
                    participantId, IsReady: isReady, IsComplete: null),
            ],
            revisionBefore: readyInput.ActionRevision,
            revisionAfter: readyRevisionAfter,
            catalog: _data));

        if (transition.DidResolve)
        {
            var completeFeeds = BattleParticipantAccess
                .EffectiveParticipants(readyInput)
                .Select(participant => new BattleReplayFeedChange(
                    participant.ParticipantId,
                    IsReady: true,
                    IsComplete: true))
                .ToArray();
            journalDrafts.Add(BattleActionJournalComposer.Command(
                afterReady,
                afterResolution,
                "resolveTurn",
                participantId: null,
                events: transition.ResolutionEvents,
                commandId: $"{commandId}:resolution",
                feedChanges: completeFeeds,
                isResolution: true,
                note: "all participants ready",
                revisionBefore: readyRevisionAfter,
                revisionAfter: readyRevisionAfter,
                catalog: _data));
        }

        if (afterResolution.Turn > before.Turn)
        {
            // 次ターン開始時に敵が計画したカード使用（味方CPU計画より先）。
            journalDrafts.AddRange(BattleActionJournalComposer.EnemyPlanning(
                afterResolution, _data, $"{commandId}:next-turn"));
        }

        if (transition.NextTurnCpuPlanning is { } nextTurnPlanning)
        {
            journalDrafts.AddRange(BattleActionJournalComposer.CpuPlanning(
                afterResolution,
                nextTurnPlanning,
                _data,
                $"{commandId}:next-turn"));
        }

        var newSession = session with
        {
            State = after,
            ActionJournal = session.ActionJournal
                .AppendSuccessfulCommands(journalDrafts),
            CommandReceipts = commandFingerprint is null
                ? session.CommandReceipts
                : BattleCommandReceiptStore.Add(
                    session.CommandReceipts, commandId, commandFingerprint),
        };
        _sessions.Set(accountId, newSession);
        return Ok(BattleStateDtoMapper.ToActionResponse(
            after, transition.Events, _data));
    }

    private static string ResolveParticipantId(
        BattleState state,
        string? requestedParticipantId)
    {
        string participantId = string.IsNullOrWhiteSpace(requestedParticipantId)
            ? BattleParticipantAccess.GetPrimary(state).ParticipantId
            : requestedParticipantId;
        RequireHumanParticipant(state, participantId);
        return participantId;
    }

    /// <summary>
    /// Validates one client command envelope before any Core/RNG mutation.
    /// A successful duplicate is answered from the latest authoritative state
    /// without executing the command again. Missing metadata remains accepted
    /// for legacy solo clients.
    /// </summary>
    private IActionResult? ValidateCommandEnvelope(
        BattleSession session,
        string participantId,
        string? actorInstanceId,
        string? requestedCommandId,
        long? expectedRevision,
        string commandKind,
        string commandFingerprint,
        bool stateOnlyResponse,
        out string commandId)
    {
        commandId = requestedCommandId is null
            ? NewCommandId(commandKind)
            : requestedCommandId;

        if (string.IsNullOrWhiteSpace(commandId)
            || commandId.Length > 128
            || commandId.Any(char.IsControl))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "battle commandId must be 1-128 printable characters",
                type: "/errors/battle-command-id-invalid");
        }

        if (session.CommandReceipts.TryGetValue(commandId, out var duplicate))
        {
            if (!string.Equals(
                    duplicate.PayloadFingerprint,
                    commandFingerprint,
                    StringComparison.Ordinal))
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "battle commandId was already used by another command",
                    detail: $"commandId={commandId}",
                    type: "/errors/battle-command-id-conflict");
            }

            return stateOnlyResponse
                ? Ok(BattleStateDtoMapper.ToDto(session.State, _data))
                : Ok(BattleStateDtoMapper.ToActionResponse(
                    session.State, Array.Empty<BattleEvent>(), _data));
        }

        if (expectedRevision is { } expected
            && expected != session.State.ActionRevision)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "battle action revision is stale",
                detail: $"expected={expected}; actual={session.State.ActionRevision}",
                type: "/errors/battle-revision-conflict");
        }

        if (!BattleParticipantAccess.TryGet(
                session.State, participantId, out var participant))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Battle participant '{participantId}' does not exist.");
        }

        if (actorInstanceId is not null
            && !string.Equals(
                actorInstanceId,
                participant.ActorInstanceId,
                StringComparison.Ordinal))
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "battle actor identity is stale",
                detail: $"expected={participant.ActorInstanceId}; actual={actorInstanceId}",
                type: "/errors/battle-actor-conflict");
        }

        return null;
    }

    private static string CreateCommandFingerprint(
        BattleState state,
        string participantId,
        string? requestedActorInstanceId,
        long? expectedRevision,
        string commandKind,
        params (string Name, object? Value)[] payload)
    {
        string? actorInstanceId = requestedActorInstanceId;
        if (actorInstanceId is null
            && BattleParticipantAccess.TryGet(state, participantId, out var participant))
            actorInstanceId = participant.ActorInstanceId;

        var fields = new (string Name, object? Value)[payload.Length + 3];
        fields[0] = ("participantId", participantId);
        fields[1] = ("actorInstanceId", actorInstanceId);
        fields[2] = ("expectedRevision", expectedRevision);
        Array.Copy(payload, 0, fields, 3, payload.Length);
        return BattleCommandFingerprint.Create(commandKind, fields);
    }

    private IActionResult StaleCommandTarget(
        string expected,
        string? actual) => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "battle command target is stale",
        detail: $"expected={expected}; actual={actual ?? "<missing>"}",
        type: "/errors/battle-command-target-conflict");

    private static void RequireHumanParticipant(
        BattleState state,
        string participantId)
    {
        if (!BattleParticipantAccess.TryGet(
                state, participantId, out var participant))
        {
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        }
        if (participant.ControllerKind != BattleParticipantControllerKind.Human)
        {
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' is CPU-controlled.");
        }
    }

    private static IReadOnlyList<BattleEvent> CombineEvents(
        IEnumerable<BattleEvent> first,
        IEnumerable<BattleEvent> second)
    {
        var combined = new List<BattleEvent>();
        foreach (var battleEvent in first.Concat(second))
            combined.Add(battleEvent with { Order = combined.Count });
        return combined;
    }

    private static string NewCommandId(string kind) =>
        $"{kind}:{Guid.NewGuid():N}";

    private sealed record CompletedExternalCommand(
        BattleSession Session,
        IReadOnlyList<BattleEvent> Events);

    /// <summary>
    /// Phase 10.5.M2-Choose: PendingCardPlay (choose modal 待ち) 中は
    /// 他 player action を全て reject する共通 guard。409 Conflict + locale-friendly error。
    /// PlayCard / EndTurn / SetTarget / UsePotion / Finalize の各 endpoint で
    /// session 取得直後に呼ぶ。ResolveCardChoice はこの guard を適用しない (それ自体が unlock 操作)。
    /// </summary>
    private IActionResult? RejectIfPending(
        BattleSession session,
        string? participantId = null)
    {
        PendingCardPlay? pending;
        if (string.IsNullOrWhiteSpace(participantId))
        {
            pending = session.State.PendingCardPlay;
        }
        else if (!BattleParticipantAccess.TryGet(
                     session.State, participantId, out var participant))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"Battle participant '{participantId}' does not exist.");
        }
        else
        {
            pending = participant.PendingCardPlay;
            // The root remains the compatibility mirror for the primary.
            // Older persisted snapshots and maintenance/test tooling can
            // update that mirror directly, so never let a pending primary
            // choice bypass the HTTP guard because the participant copy is
            // momentarily stale.
            if (pending is null
                && string.Equals(
                    participantId,
                    BattleParticipantAccess.GetPrimary(session.State).ParticipantId,
                    StringComparison.Ordinal))
                pending = session.State.PendingCardPlay;
        }

        if (pending is not null)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "保留中のカード選択を先に解決してください。",
                detail: "Resolve the pending card choice before performing other actions.",
                type: "/errors/card-choice-pending");
        return null;
    }
}
