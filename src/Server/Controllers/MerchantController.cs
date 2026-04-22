using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/merchant")]
public sealed class MerchantController : ControllerBase
{
    private readonly IAccountRepository _accounts;
    private readonly ISaveRepository _saves;
    private readonly RunStartService _runStart;
    private readonly DataCatalog _data;

    public MerchantController(IAccountRepository accounts, ISaveRepository saves, RunStartService runStart, DataCatalog data)
    {
        _accounts = accounts;
        _saves = saves;
        _runStart = runStart;
        _data = data;
    }

    [HttpGet("inventory")]
    public async Task<IActionResult> GetInventory(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (s.ActiveMerchant is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "商人マスにいません。");

        return Ok(MerchantInventoryDto.From(s.ActiveMerchant));
    }

    [HttpPost("buy")]
    public async Task<IActionResult> PostBuy([FromBody] MerchantBuyRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (s.ActiveMerchant is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "商人マスにいません。");

        RunState updated;
        try
        {
            updated = body.Kind switch
            {
                "card" => MerchantActions.BuyCard(s, body.Id, _data),
                "relic" => MerchantActions.BuyRelic(s, body.Id, _data),
                "potion" => MerchantActions.BuyPotion(s, body.Id, _data),
                _ => throw new ArgumentException($"unknown kind \"{body.Kind}\"", nameof(body)),
            };
        }
        catch (ArgumentException ex) when (ex.Message.Contains("not in inventory") || ex.Message.Contains("unknown"))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not enough gold"))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);
        var map = _runStart.RehydrateMap(updated.RngSeed);
        return Ok(RunSnapshotDtoMapper.From(updated, map, _data));
    }

    [HttpPost("discard")]
    public async Task<IActionResult> PostDiscard([FromBody] MerchantDiscardRequestDto body, CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (body is null) return BadRequest();
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (s.ActiveMerchant is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "商人マスにいません。");

        RunState updated;
        try
        {
            updated = MerchantActions.DiscardCard(s, body.DeckIndex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Not enough gold"))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);
        var map = _runStart.RehydrateMap(updated.RngSeed);
        return Ok(RunSnapshotDtoMapper.From(updated, map, _data));
    }

    [HttpPost("leave")]
    public async Task<IActionResult> PostLeave(CancellationToken ct)
    {
        if (!TryGetAccountId(out var accountId, out var err)) return err!;
        if (!await _accounts.ExistsAsync(accountId, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"アカウントが見つかりません: {accountId}");

        var s = await _saves.TryLoadAsync(accountId, ct);
        if (s is null || s.Progress != RunProgress.InProgress)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "進行中のランがありません。");
        if (s.ActiveMerchant is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "商人マスにいません。");

        RunState updated;
        try
        {
            updated = MerchantActions.Leave(s);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }

        updated = updated with { SavedAtUtc = DateTimeOffset.UtcNow };
        await _saves.SaveAsync(accountId, updated, ct);
        var map = _runStart.RehydrateMap(updated.RngSeed);
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
