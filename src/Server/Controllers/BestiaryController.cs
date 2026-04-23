using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/bestiary")]
public sealed class BestiaryController : ControllerBase
{
    private readonly IAccountRepository _accounts;
    private readonly IBestiaryRepository _bestiary;
    private readonly DataCatalog _data;

    public BestiaryController(IAccountRepository accounts, IBestiaryRepository bestiary, DataCatalog data)
    {
        _accounts = accounts;
        _bestiary = bestiary;
        _data = data;
    }

    [HttpGet("")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryAcc(out var acc, out var err)) return err!;
        if (!await _accounts.ExistsAsync(acc, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "アカウントなし");

        var state = await _bestiary.LoadAsync(acc, ct);
        var dto = new BestiaryDto(
            SchemaVersion: state.SchemaVersion,
            DiscoveredCardBaseIds: Sorted(state.DiscoveredCardBaseIds),
            DiscoveredRelicIds: Sorted(state.DiscoveredRelicIds),
            DiscoveredPotionIds: Sorted(state.DiscoveredPotionIds),
            EncounteredEnemyIds: Sorted(state.EncounteredEnemyIds),
            AllKnownCardBaseIds: _data.Cards.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            AllKnownRelicIds: _data.Relics.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            AllKnownPotionIds: _data.Potions.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            AllKnownEnemyIds: _data.Enemies.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        return Ok(dto);
    }

    private static IReadOnlyList<string> Sorted(System.Collections.Immutable.ImmutableHashSet<string> set)
        => set.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    private bool TryAcc(out string id, out IActionResult? err)
    {
        id = string.Empty; err = null;
        if (!Request.Headers.TryGetValue(RunsController.AccountHeader, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            err = Problem(statusCode: StatusCodes.Status400BadRequest, title: "account header missing");
            return false;
        }
        id = raw.ToString();
        try { AccountIdValidator.Validate(id); }
        catch (ArgumentException ex)
        {
            err = Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            return false;
        }
        return true;
    }
}
