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
        bool Upgradable,
        string Description,
        string? UpgradedDescription);

    public sealed record PotionCatalogEntryDto(
        string Id,
        string Name,
        int Rarity,
        bool UsableInBattle,
        bool UsableOutOfBattle,
        string Description);

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
                def.UpgradedEffects is not null,
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
                def.UsableInBattle,
                def.UsableOutOfBattle,
                DescribePotionEffects(def));
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

    private static string DescribeEffects(IReadOnlyList<CardEffect> effects)
    {
        if (effects.Count == 0) return string.Empty;
        return string.Join(" / ", effects.Select(CardEffectLabel));
    }

    private static string DescribePotionEffects(Core.Potions.PotionDefinition def)
    {
        var scope = def.UsableInBattle && def.UsableOutOfBattle
            ? ""
            : def.UsableInBattle
                ? "[戦闘中] "
                : def.UsableOutOfBattle
                    ? "[戦闘外] "
                    : "";
        return scope + DescribeEffects(def.Effects);
    }

    private static string CardEffectLabel(CardEffect e) => e switch
    {
        DamageEffect d => $"{d.Amount} ダメージ",
        GainBlockEffect b => $"ブロック +{b.Amount}",
        GainMaxHpEffect m => $"最大HP +{m.Amount}",
        GainGoldEffect g => $"+{g.Amount} ゴールド",
        RestHealBonusEffect r => $"休憩時の回復 +{r.Amount}",
        UnknownEffect u => $"(未実装: {u.Type})",
        _ => "(効果)",
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
