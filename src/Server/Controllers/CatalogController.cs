using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/v1/catalog")]
public sealed class CatalogController : ControllerBase
{
    private readonly DataCatalog _data;

    public CatalogController(DataCatalog data) => _data = data;

    public sealed record CardCatalogEntryDto(
        string Id,
        string Name,
        string? DisplayName,
        int Rarity,
        string CardType,
        int? Cost);

    [HttpGet("cards")]
    public IActionResult GetCards()
    {
        var result = new Dictionary<string, CardCatalogEntryDto>(_data.Cards.Count);
        foreach (var (id, def) in _data.Cards)
        {
            result[id] = new CardCatalogEntryDto(
                def.Id,
                def.Name,
                def.DisplayName,
                (int)def.Rarity,
                def.CardType.ToString(),
                def.Cost);
        }
        return Ok(result);
    }
}
