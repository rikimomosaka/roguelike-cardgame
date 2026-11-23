using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Cards;
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
        // プレイ可能キャラクターの初期デッキは開始時点から既知。
        // それ以外は図鑑で発見済みになった召喚カードから味方を開示する。
        var knownCardIds = state.DiscoveredCardBaseIds
            .Union(_data.Characters.Values.SelectMany(character => character.Deck));
        var discoveredUnitIds = knownCardIds
            .Select(id => _data.Cards.TryGetValue(id, out var card) ? card : null)
            .Where(card => card is not null)
            .SelectMany(card => card!.Effects
                .Concat(card.UpgradedEffects ?? Array.Empty<CardEffect>()))
            .Where(effect => effect.Action == "summon" && !string.IsNullOrWhiteSpace(effect.UnitId))
            .Select(effect => effect.UnitId!)
            .ToHashSet(StringComparer.Ordinal);

        var players = _data.Characters.Values
            .OrderBy(character => character.Id, StringComparer.Ordinal)
            .Select(character => new BestiaryCharacterDto(
                character.Id,
                character.Name,
                Kind: "player",
                character.MaxHp,
                ImageId: character.Id == "default" ? "player_stand" : null,
                character.HeightTier,
                LifetimeTurns: null,
                IsDiscovered: true,
                Description: string.IsNullOrWhiteSpace(character.Description)
                    ? "まだ見ぬ力を求め、危険な塔へ足を踏み入れた冒険者。"
                    : character.Description!,
                TraitDescription: string.IsNullOrWhiteSpace(character.TraitDescription)
                    ? "特性は未実装です。"
                    : character.TraitDescription!,
                Actions: character.EffectiveSkills.Select(skill =>
                    new BestiaryCharacterActionDto(
                        Category: skill.Id == "tactical" ? "戦技"
                            : skill.Id == "ultimate" ? "奥義" : "スキル",
                        skill.Name,
                        CardTextFormatter.FormatUsableEffects(
                            skill.Effects, skill.Keywords, CardActorContext.Empty,
                            skill.EffectiveUsesPerTurn),
                        skill.Cost.Energy,
                        skill.Cost.ComboGauge)).ToArray(),
                SummonCards: Array.Empty<BestiarySummonCardDto>(),
                CharacterClass: character.CharacterClass.ToString()))
            .ToArray();

        var allies = (_data.Units?.Values ?? Array.Empty<UnitDefinition>())
            .OrderBy(unit => unit.Id, StringComparer.Ordinal)
            .Select(unit => new BestiaryCharacterDto(
                unit.Id,
                unit.Name,
                Kind: "ally",
                unit.Hp,
                unit.ImageId,
                unit.HeightTier,
                unit.LifetimeTurns,
                IsDiscovered: discoveredUnitIds.Contains(unit.Id),
                Description: string.Empty,
                TraitDescription: unit.TraitDescription ?? string.Empty,
                Actions: BuildAllyActions(unit),
                SummonCards: _data.Cards.Values
                    .Where(card => card.Effects
                        .Concat(card.UpgradedEffects ?? Array.Empty<CardEffect>())
                        .Any(effect => effect.Action == "summon" && effect.UnitId == unit.Id))
                    .OrderBy(card => card.Id, StringComparer.Ordinal)
                    .Select(card => new BestiarySummonCardDto(
                        card.Id,
                        card.DisplayName ?? card.Name,
                        (int)card.Rarity,
                        card.CardType.ToString(),
                        card.Cost,
                        CardTextFormatter.Format(card, upgraded: false),
                        card.IsUpgradable
                            ? CardTextFormatter.Format(card, upgraded: true)
                            : null,
                        card.PoolClass?.ToString()))
                    .ToArray()))
            .ToArray();

        var dto = new BestiaryDto(
            SchemaVersion: state.SchemaVersion,
            DiscoveredCardBaseIds: Sorted(state.DiscoveredCardBaseIds),
            DiscoveredRelicIds: Sorted(state.DiscoveredRelicIds),
            DiscoveredPotionIds: Sorted(state.DiscoveredPotionIds),
            EncounteredEnemyIds: Sorted(state.EncounteredEnemyIds),
            // CPU-only token cards describe an ally's private action deck and
            // belong on that character's entry, not in the collectible-card
            // completion list.
            AllKnownCardBaseIds: _data.Cards.Values
                .Where(card => card.Rarity != CardRarity.Token)
                .Select(card => card.Id)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray(),
            AllKnownRelicIds: _data.Relics.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            AllKnownPotionIds: _data.Potions.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            AllKnownEnemyIds: _data.Enemies.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            PlayerCharacters: players,
            AllyCharacters: allies);
        return Ok(dto);
    }

    private IReadOnlyList<BestiaryCharacterActionDto> BuildAllyActions(
        UnitDefinition unit)
    {
        if (!unit.IsCardControlled)
        {
            return unit.Moves.Select((move, index) =>
                new BestiaryCharacterActionDto(
                    Category: "毎ターンの行動",
                    Name: unit.Moves.Count == 1 ? "行動" : $"行動{index + 1}",
                    Description: CardTextFormatter.FormatEffects(move.Effects),
                    EnergyCost: 0,
                    ComboGaugeCost: 0)).ToArray();
        }

        var skills = unit.EffectiveSkills.Select(skill =>
            new BestiaryCharacterActionDto(
                Category: skill.Id == "tactical" ? "戦技"
                    : skill.Id == "ultimate" ? "奥義" : "スキル",
                skill.Name,
                CardTextFormatter.FormatUsableEffects(
                    skill.Effects, skill.Keywords, CardActorContext.Empty,
                    skill.EffectiveUsesPerTurn),
                skill.Cost.Energy,
                skill.Cost.ComboGauge));
        var cards = unit.EffectiveDeck
            .Select(entry =>
            {
                var card = _data.Cards[entry.CardId];
                string displayName = card.DisplayName ?? card.Name;
                return new BestiaryCharacterActionDto(
                    Category: "行動カード",
                    Name: entry.Count > 1
                        ? $"{displayName} ×{entry.Count}"
                        : displayName,
                    Description: CardTextFormatter.Format(card, entry.Upgraded),
                    EnergyCost: (entry.Upgraded
                        ? card.UpgradedCost ?? card.Cost
                        : card.Cost) ?? 0,
                    ComboGaugeCost: card.ComboGaugeCost);
            });
        return skills.Concat(cards).ToArray();
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
