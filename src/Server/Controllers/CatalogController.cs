using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Cards;
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
        int? UpgradedCost,
        bool Upgradable,
        string Description,
        string? UpgradedDescription);

    public sealed record PotionCatalogEntryDto(
        string Id,
        string Name,
        int Rarity,
        bool UsableOutsideBattle,
        string Description);

    public sealed record EnemyCatalogEntryDto(
        string Id,
        string Name,
        string ImageId,
        int Hp,
        string InitialMoveId);

    public sealed record UnitCatalogEntryDto(
        string Id,
        string Name,
        string ImageId,
        int Hp,
        string InitialMoveId,
        int? LifetimeTurns);

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
                def.UpgradedCost,
                def.IsUpgradable,
                DescribeEffects(def.Effects),
                def.UpgradedEffects is null ? null : DescribeEffects(def.UpgradedEffects));
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
                def.IsUsableOutsideBattle,
                DescribePotionEffects(def));
        }
        return Ok(result);
    }

    [HttpGet("enemies")]
    public IActionResult GetEnemies()
    {
        var result = new Dictionary<string, EnemyCatalogEntryDto>(_data.Enemies.Count);
        foreach (var (id, def) in _data.Enemies)
        {
            result[id] = new EnemyCatalogEntryDto(
                def.Id, def.Name, def.ImageId, def.Hp, def.InitialMoveId);
        }
        return Ok(result);
    }

    [HttpGet("units")]
    public IActionResult GetUnits()
    {
        if (_data.Units is null) return Ok(new Dictionary<string, UnitCatalogEntryDto>());
        var result = new Dictionary<string, UnitCatalogEntryDto>(_data.Units.Count);
        foreach (var (id, def) in _data.Units)
        {
            result[id] = new UnitCatalogEntryDto(
                def.Id, def.Name, def.ImageId, def.Hp, def.InitialMoveId, def.LifetimeTurns);
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
                Description: string.IsNullOrEmpty(r.Description) ? r.Name : r.Description,
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
                StartMessage: e.StartMessage,
                Tiers: e.Tiers.IsDefault
                    ? System.Array.Empty<int>()
                    : (IReadOnlyList<int>)e.Tiers.ToArray(),
                Rarity: e.Rarity.ToString(),
                ConditionSummary: e.Condition switch
                {
                    EventCondition.MinGold(var g) => $"requires {g} gold",
                    EventCondition.MinHp(var h) => $"requires {h} HP",
                    null => null,
                    _ => "requires condition",
                },
                Choices: e.Choices.Select(c => new EventChoiceDto(
                    Label: c.Label,
                    ConditionSummary: c.Condition switch
                    {
                        EventCondition.MinGold(var g) => $"requires {g} gold",
                        EventCondition.MinHp(var h) => $"requires {h} HP",
                        null => null,
                        _ => "requires condition",
                    },
                    EffectSummaries: c.Effects.Select(EffectLabel).ToList(),
                    ResultMessage: c.ResultMessage))
                .ToList()))
            .ToList();
        return Ok(list);
    }

    private static string DescribeEffects(IReadOnlyList<CardEffect> effects)
    {
        if (effects.Count == 0) return string.Empty;
        return string.Join(" / ", effects.Select(CardEffectLabel));
    }

    private static string DescribePotionEffects(Core.Potions.PotionDefinition def)
    {
        var prefix = def.IsUsableOutsideBattle ? "" : "[戦闘中] ";
        return prefix + DescribeEffects(def.Effects);
    }

    private static string CardEffectLabel(CardEffect e) => e.Action switch
    {
        "attack" => $"{e.Amount} ダメージ",
        "block" => $"ブロック +{e.Amount}",
        "gainMaxHp" => $"最大HP +{e.Amount}",
        "gainGold" => $"+{e.Amount} ゴールド",
        "restHealBonus" => $"休憩時の回復 +{e.Amount}",
        _ => $"(未実装: {e.Action})",
    };

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
