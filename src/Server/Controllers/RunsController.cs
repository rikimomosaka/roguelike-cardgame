using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;
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
    private readonly DataCatalog _data;
    private readonly IHistoryRepository _history;
    private readonly IBestiaryRepository _bestiary;

    public RunsController(IAccountRepository accounts, ISaveRepository saves, RunStartService runStart, DataCatalog data, IHistoryRepository history, IBestiaryRepository bestiary)
    {
        _accounts = accounts;
        _saves = saves;
        _runStart = runStart;
        _data = data;
        _history = history;
        _bestiary = bestiary;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var state = await _saves.TryLoadAsync(accountId, ct);
        if (state is null || state.Progress != RunProgress.InProgress) return NoContent();

        var map = _runStart.RehydrateMap(state.RngSeed, state.CurrentAct);
        return Ok(RunSnapshotDtoMapper.From(state, map, _data));
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
        return Ok(RunSnapshotDtoMapper.From(state, map, _data));
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

        if (state.ActiveBattle is not null || state.ActiveReward is not null)
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "戦闘中または報酬未受取のため移動できません。");

        var map = _runStart.RehydrateMap(state.RngSeed, state.CurrentAct);
        RunState advanced;
        try
        {
            advanced = RunActions.SelectNextNode(state, map, body.NodeId);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }

        var node = map.GetNode(body.NodeId);
        var actualKind = advanced.UnknownResolutions.TryGetValue(node.Id, out var resolved) ? resolved : node.Kind;

        var effectRng = new SystemRng(unchecked((int)advanced.RngSeed ^ (node.Id * 31) ^ (int)advanced.PlaySeconds));
        advanced = NodeEffectResolver.Resolve(advanced, actualKind, node.Row, _data, effectRng);

        long elapsed = Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);
        advanced = advanced with
        {
            PlaySeconds = advanced.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        await _saves.SaveAsync(accountId, advanced, ct);
        return NoContent();
    }

    [HttpPost("current/battle/win")]
    public async Task<IActionResult> PostBattleWin([FromBody] BattleWinRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress || s.ActiveBattle is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中の戦闘がありません。");

        var afterWin = BattlePlaceholder.Win(s);
        var enc = _data.Encounters[afterWin.ActiveBattle!.EncounterId];
        bool isBoss = enc.Pool.Tier == EnemyTier.Boss;

        long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);

        // ボス かつ 最終アクト → クリア処理
        if (isBoss && afterWin.CurrentAct == RunConstants.MaxAct)
        {
            var finished = ActTransition.FinishRun(afterWin with
            {
                ActiveBattle = null,
                PlaySeconds = afterWin.PlaySeconds + elapsed,
            }, RunProgress.Cleared);
            var rec = RunHistoryBuilder.From(accountId, finished, finished.VisitedNodeIds.Length, RunProgress.Cleared);
            await _history.AppendAsync(accountId, rec, ct);
            await _bestiary.MergeAsync(accountId, rec, ct);
            await _saves.DeleteAsync(accountId, ct);
            return Ok(RunSnapshotDtoMapper.ToResultDto(rec));
        }

        var rewardRng = new SystemRng(unchecked((int)s.RngSeed ^ (int)s.PlaySeconds ^ 0x5EED));
        RewardRngState newRng;
        RewardState reward;

        if (isBoss)
        {
            // ボス かつ 非最終アクト → BossReward フラグ付き報酬
            var r = BossRewardFlow.GenerateBossReward(afterWin, _data, rewardRng)
                ?? throw new InvalidOperationException(
                    $"BossRewardFlow returned null for non-final act {afterWin.CurrentAct}.");
            reward = r;
            newRng = afterWin.RewardRngState;  // BossRewardFlow は RewardRngState を更新しない
        }
        else
        {
            // 通常エンカウンター
            var (r, nr) = RewardGenerator.Generate(
                new RewardContext.FromEnemy(enc.Pool),
                afterWin.RewardRngState,
                ImmutableArray.CreateRange(s.Relics),
                _data.RewardTables.TryGetValue($"act{s.CurrentAct}", out var tbl) ? tbl : _data.RewardTables["act1"],
                _data, rewardRng);
            reward = r; newRng = nr;
        }

        var updated = afterWin with
        {
            ActiveBattle = null,
            ActiveReward = reward,
            RewardRngState = newRng,
            PlaySeconds = afterWin.PlaySeconds + elapsed,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
        // Phase 8: プレイヤーに提示されたカード選択肢を SeenCardBaseIds に追加。
        // ボス報酬は CardChoices が空のため、ガードで no-op 化する。
        if (reward.CardChoices.Length > 0)
            updated = BestiaryTracker.NoteCardsSeen(updated, reward.CardChoices);
        await _saves.SaveAsync(accountId, updated, ct);
        var winMap = _runStart.RehydrateMap(updated.RngSeed, updated.CurrentAct);
        return Ok(RunSnapshotDtoMapper.From(updated, winMap, _data));
    }

    [HttpPost("current/reward/gold")]
    public async Task<IActionResult> PostRewardGold(CancellationToken ct)
        => await ApplyReward(s => RewardApplier.ApplyGold(s), ct);

    [HttpPost("current/reward/potion")]
    public async Task<IActionResult> PostRewardPotion(CancellationToken ct)
        => await ApplyReward(s => RewardApplier.ApplyPotion(s), ct);

    private async Task<IActionResult> ApplyReward(Func<RunState, RunState> action, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress || s.ActiveReward is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "報酬画面がありません。");

        RunState updated;
        try { updated = action(s); }
        catch (InvalidOperationException ex)
        { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message); }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);
        return NoContent();
    }

    [HttpPost("current/reward/card")]
    public async Task<IActionResult> PostRewardCard([FromBody] RewardCardRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();
        bool hasCard = !string.IsNullOrEmpty(body.CardId);
        bool skip = body.Skip == true;
        if (hasCard == skip)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "cardId と skip のうち片方のみ指定してください。");

        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress || s.ActiveReward is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "報酬画面がありません。");

        RunState updated;
        try
        {
            updated = skip ? RewardApplier.SkipCard(s) : RewardApplier.PickCard(s, body.CardId!);
        }
        catch (ArgumentException ex)
        { return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message); }
        catch (InvalidOperationException ex)
        { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message); }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);
        return NoContent();
    }

    [HttpPost("current/reward/proceed")]
    public async Task<IActionResult> PostRewardProceed([FromBody] RewardProceedRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress || s.ActiveReward is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "報酬画面がありません。");

        long elapsed = body is null ? 0 : Math.Clamp(body.ElapsedSeconds, 0, MaxElapsedSecondsPerRequest);

        RunState updated;
        if (s.ActiveReward.IsBossReward && s.CurrentAct < RunConstants.MaxAct)
        {
            // act 遷移: 新マップ生成 → Unknown 解決 → AdvanceAct
            int nextAct = s.CurrentAct + 1;
            var nextActSeed = unchecked((int)(uint)ActMapSeed.Derive(s.RngSeed, nextAct));
            var newMap = _runStart.RehydrateMap(s.RngSeed, nextAct);
            var newResolutions = _runStart.ResolveUnknownsForAct(newMap, s.RngSeed, nextAct);
            var advanceRng = new SystemRng(unchecked(nextActSeed ^ 0xAC70));
            updated = ActTransition.AdvanceAct(s, newMap, _data, advanceRng, newResolutions);
            // 新アクトの層開始レリック選択はスタートマスに入った時点で ActStartController.Enter が生成する。
            updated = updated with
            {
                PlaySeconds = updated.PlaySeconds + elapsed,
                SavedAtUtc = DateTimeOffset.UtcNow,
            };
        }
        else
        {
            try { updated = RewardApplier.Proceed(s); }
            catch (InvalidOperationException ex)
            { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message); }
            updated = updated with
            {
                PlaySeconds = updated.PlaySeconds + elapsed,
                SavedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        await _saves.SaveAsync(accountId, updated, ct);
        var proceedMap = _runStart.RehydrateMap(updated.RngSeed, updated.CurrentAct);
        return Ok(RunSnapshotDtoMapper.From(updated, proceedMap, _data));
    }

    [HttpPost("current/reward/claim-relic")]
    public async Task<IActionResult> PostRewardClaimRelic(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress || s.ActiveReward is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "報酬画面がありません。");
        if (s.ActiveReward.RelicId is null || s.ActiveReward.RelicClaimed)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "relic を受け取れません。");

        RunState updated;
        try { updated = RewardApplier.ClaimRelic(s, _data); }
        catch (InvalidOperationException ex)
        { return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message); }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);
        return NoContent();
    }

    [HttpPost("current/potion/discard")]
    public async Task<IActionResult> PostPotionDiscard([FromBody] PotionDiscardRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");

        RunState updated;
        try { updated = RewardApplier.DiscardPotion(s, body.SlotIndex); }
        catch (ArgumentException ex)
        { return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message); }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
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
        var finished = ActTransition.FinishRun(state with
        {
            PlaySeconds = state.PlaySeconds + elapsed,
        }, RunProgress.Abandoned);
        var rec = RunHistoryBuilder.From(accountId, finished, finished.VisitedNodeIds.Length, RunProgress.Abandoned);
        await _history.AppendAsync(accountId, rec, ct);
        await _bestiary.MergeAsync(accountId, rec, ct);
        await _saves.DeleteAsync(accountId, ct);
        return Ok(RunSnapshotDtoMapper.ToResultDto(rec));
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
