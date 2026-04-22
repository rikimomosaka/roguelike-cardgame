using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Server.Dtos;

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
        int? Cost,
        bool Upgradable);

    public sealed record PotionCatalogEntryDto(
        string Id,
        string Name,
        int Rarity,
        bool UsableInBattle,
        bool UsableOutOfBattle);

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
                def.Cost,
                def.UpgradedEffects is not null);
        }
        return Ok(result);
    }

    [HttpGet("potions")]
    public IActionResult GetPotions()
    {
        var result = new Dictionary<string, PotionCatalogEntryDto>(_data.Potions.Count);
        foreach (var (id, def) in _data.Potions)
        {
            result[id] = new PotionCatalogEntryDto(
                def.Id,
                def.Name,
                (int)def.Rarity,
                def.UsableInBattle,
                def.UsableOutOfBattle);
        }
        return Ok(result);
    }

    [HttpGet("relics")]
    public IActionResult GetRelics()
    {
        var list = _data.Relics.Values
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new RelicDto(
                Id: r.Id,
                Name: r.Name,
                Description: r.Name,
                Rarity: r.Rarity.ToString(),
                Trigger: r.Trigger.ToString()))
            .ToList();
        return Ok(list);
    }

    [HttpGet("events")]
    public IActionResult GetEvents()
    {
        var list = _data.Events.Values
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new EventDto(
                Id: e.Id,
                Name: e.Name,
                Description: e.Description,
                Choices: e.Choices.Select(c => new EventChoiceDto(
                    Label: c.Label,
                    ConditionSummary: c.Condition switch
                    {
                        EventCondition.MinGold(var g) => $"requires {g} gold",
                        EventCondition.MinHp(var h) => $"requires {h} HP",
                        null => null,
                        _ => "requires condition",
                    },
                    EffectSummaries: c.Effects.Select(EffectLabel).ToList()))
                .ToList()))
            .ToList();
        return Ok(list);
    }

    private static string EffectLabel(EventEffect e) => e switch
    {
        EventEffect.GainGold(var n) => $"+{n} gold",
        EventEffect.PayGold(var n) => $"-{n} gold",
        EventEffect.Heal(var n) => $"+{n} HP",
        EventEffect.TakeDamage(var n) => $"-{n} HP",
        EventEffect.GainMaxHp(var n) => $"+{n} max HP",
        EventEffect.LoseMaxHp(var n) => $"-{n} max HP",
        EventEffect.GainRelicRandom(var rarity) => $"random {rarity} relic",
        EventEffect.GrantCardReward => "card reward (3 choices)",
        _ => "(effect)",
    };
}
