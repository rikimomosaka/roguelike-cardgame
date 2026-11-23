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
        string? UpgradedDescription,
        int ComboGaugeCost = 0,
        int UsesPerTurn = 0,
        int? UpgradedUsesPerTurn = null,
        IReadOnlyList<string>? PoolKinds = null,
        string? PoolClass = null,
        IReadOnlyList<string>? EligibleCharacterIds = null);

    public sealed record PotionCatalogEntryDto(
        string Id,
        string Name,
        int Rarity,
        bool UsableOutsideBattle,
        string Description,
        int UsesPerTurn = 0);

    public sealed record EnemyCatalogEntryDto(
        string Id,
        string Name,
        string ImageId,
        int Hp,
        string InitialMoveId,
        int HeightTier,
        int Attack = 0,
        int Defense = 0,
        int InitialEnergy = 0,
        int EnergyPerTurn = 0,
        int DrawPerTurn = 0,
        int HandLimit = 0,
        IReadOnlyList<UnitDeckEntryDto>? Deck = null,
        IReadOnlyList<string>? OpeningActions = null);

    public sealed record UnitCatalogEntryDto(
        string Id,
        string Name,
        string ImageId,
        int Hp,
        string InitialMoveId,
        int? LifetimeTurns,
        int HeightTier,
        IReadOnlyList<UnitDeckEntryDto>? Deck = null,
        int InitialEnergy = 0,
        int EnergyPerTurn = 0,
        int DrawPerTurn = 0,
        int HandLimit = 0,
        string? AiProfileId = null,
        string? TraitDescription = null,
        IReadOnlyList<CharacterSkillCatalogEntryDto>? Skills = null,
        IReadOnlyList<string>? OwnedRelicIds = null,
        IReadOnlyList<string>? Potions = null);

    public sealed record UnitDeckEntryDto(
        string CardId,
        int Count,
        bool Upgraded,
        int Priority);

    public sealed record CharacterCatalogEntryDto(
        string Id,
        string Name,
        int MaxHp,
        int StartingGold,
        int PotionSlotCount,
        int HeightTier,
        IReadOnlyList<CharacterSkillCatalogEntryDto>? Skills = null,
        string CharacterClass = "Striker",
        IReadOnlyList<string>? Deck = null,
        string? Description = null,
        string? TraitDescription = null,
        int EnergyPerTurn = 3,
        int DrawPerTurn = 5,
        string? TraitName = null,
        string? TraitEffects = null);

    public sealed record CharacterSkillCatalogEntryDto(
        string Id,
        string Name,
        int EnergyCost,
        int ComboGaugeCost,
        bool OncePerTurn,
        string Description,
        bool RequiresCardsInHand,
        int UsesPerTurn = 0);

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
                CardTextFormatter.Format(def, upgraded: false),
                def.IsUpgradable ? CardTextFormatter.Format(def, upgraded: true) : null,
                def.ComboGaugeCost,
                def.UsesPerTurn,
                def.UpgradedUsesPerTurn,
                def.EffectivePoolKinds.Select(kind => kind.ToString()).ToArray(),
                def.PoolClass?.ToString(),
                def.EffectiveEligibleCharacterIds.ToArray());
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
                DescribePotionEffects(def),
                def.UsesPerTurn);
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
                def.Id, def.Name, def.ImageId, def.Hp, def.InitialMoveId, def.HeightTier,
                Attack: def.Attack,
                Defense: def.Defense,
                InitialEnergy: def.InitialEnergy,
                EnergyPerTurn: def.EnergyPerTurn,
                DrawPerTurn: def.DrawPerTurn,
                HandLimit: def.HandLimit,
                Deck: def.IsCardControlled
                    ? def.EffectiveDeck.Select(entry => new UnitDeckEntryDto(
                        entry.CardId, entry.Count, entry.Upgraded, entry.Priority)).ToArray()
                    : null,
                OpeningActions: def.EffectiveOpeningActions.ToArray());
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
                def.Id,
                def.Name,
                def.ImageId,
                def.Hp,
                def.InitialMoveId,
                def.LifetimeTurns,
                def.HeightTier,
                def.IsCardControlled
                    ? def.EffectiveDeck.Select(entry => new UnitDeckEntryDto(
                        entry.CardId,
                        entry.Count,
                        entry.Upgraded,
                        entry.Priority)).ToArray()
                    : null,
                def.InitialEnergy,
                def.EnergyPerTurn,
                def.DrawPerTurn,
                def.HandLimit,
                def.AiProfileId,
                def.TraitDescription,
                def.EffectiveSkills.Select(skill => new CharacterSkillCatalogEntryDto(
                    skill.Id,
                    skill.Name,
                    skill.Cost.Energy,
                    skill.Cost.ComboGauge,
                    skill.OncePerTurn,
                    // Keep catalog descriptions static. The battle DTO owns the live
                    // per-turn counter, so embedding an initial (0/N) header here would
                    // become stale and be rendered twice by the combat controls.
                    CardTextFormatter.FormatEffectsWithKeywords(
                        skill.Effects,
                        skill.Keywords,
                        CardActorContext.Empty),
                    skill.RequiresCardsInHand,
                    skill.EffectiveUsesPerTurn)).ToArray(),
                def.EffectiveOwnedRelicIds.ToArray(),
                def.EffectivePotions.ToArray());
        }
        return Ok(result);
    }

    [HttpGet("characters")]
    public IActionResult GetCharacters()
    {
        var result = new Dictionary<string, CharacterCatalogEntryDto>(_data.Characters.Count);
        foreach (var (id, def) in _data.Characters)
        {
            result[id] = new CharacterCatalogEntryDto(
                def.Id, def.Name, def.MaxHp, def.StartingGold, def.PotionSlotCount, def.HeightTier,
                def.EffectiveSkills.Select(skill => new CharacterSkillCatalogEntryDto(
                    skill.Id,
                    skill.Name,
                    skill.Cost.Energy,
                    skill.Cost.ComboGauge,
                    skill.OncePerTurn,
                    CardTextFormatter.FormatEffectsWithKeywords(
                        skill.Effects, skill.Keywords, CardActorContext.Empty),
                    skill.RequiresCardsInHand,
                    skill.EffectiveUsesPerTurn)).ToList(),
                def.CharacterClass.ToString(),
                def.Deck.ToArray(),
                def.Description,
                def.TraitDescription,
                def.EnergyPerTurn,
                def.DrawPerTurn,
                def.Trait?.Name,
                FormatCharacterTraitEffects(def.Trait));
        }
        return Ok(result);
    }

    [HttpGet("relics")]
    public IActionResult GetRelics()
    {
        var list = _data.Relics.Values
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r =>
            {
                var manual = r.Description ?? string.Empty;
                var auto = r.Effects is { Count: > 0 } ? CardTextFormatter.FormatEffects(r.Effects) : string.Empty;
                string combined =
                    manual.Length > 0 && auto.Length > 0 ? auto + "\n" + manual :
                    auto.Length > 0 ? auto :
                    manual.Length > 0 ? manual : r.Name;
                return new RelicDto(
                    Id: r.Id,
                    Name: r.Name,
                    Description: combined,
                    EffectText: auto,
                    Flavor: manual,
                    Rarity: r.Rarity.ToString());
            })
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

    private static string DescribePotionEffects(Core.Potions.PotionDefinition def)
    {
        var prefix = def.IsUsableOutsideBattle ? "" : "[戦闘中] ";
        return prefix + CardTextFormatter.FormatUsableEffects(
            def.Effects, null, CardActorContext.Empty, def.UsesPerTurn);
    }

    /// <summary>
    /// Projects a passive trait through the same marker-bearing effect formatter as cards and
    /// skills. Trigger metadata is kept here because it belongs to CharacterTraitDefinition,
    /// not to an individual CardEffect.
    /// </summary>
    private static string? FormatCharacterTraitEffects(CharacterTraitDefinition? trait)
    {
        if (trait is null) return null;

        var triggerText = trait.Trigger switch
        {
            CharacterTraitTrigger.OnTurnStart => "自身のターン開始時、",
            CharacterTraitTrigger.OnComboReached =>
                $"自身の最大コンボが[N:{trait.Threshold}]以上になった時、",
            CharacterTraitTrigger.OnContingencyTriggered =>
                "自身の除外状態の[K:contingency]が発動した時、",
            CharacterTraitTrigger.OnResonanceConsumed =>
                "自身の[S:resonance]が発動した時、",
            _ => string.Empty,
        };
        var effectText = CardTextFormatter.FormatEffectsWithKeywords(
            trait.Effects,
            keywords: null,
            CardActorContext.Empty);
        var text = triggerText + effectText;

        return trait.UsesPerTurn > 0
            ? $"{text}\n1ターンに[N:{trait.UsesPerTurn}]度だけ発動する。"
            : text;
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
        EventEffect.GainCard(var cardId) => $"gain card {cardId}",
        _ => "(effect)",
    };
}
