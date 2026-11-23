using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Battle.Definitions.Loaders;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Controllers;

[ApiController]
[Route("api/dev/content")]
public sealed class DevContentController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly DevContentService _content;

    public DevContentController(IWebHostEnvironment env, DevContentService content)
    {
        _env = env;
        _content = content;
    }

    [HttpGet("kinds")]
    public IActionResult GetKinds()
    {
        if (!IsAuthoringEnvironment()) return NotFound();
        return Ok(new
        {
            readOnly = !_env.IsDevelopment(),
            kinds = DevContentService.Kinds,
            enums = new
            {
                cardTypes = new[] { "Attack", "Skill", "Power", "Curse", "Status", "Unit" },
                characterClasses = ContentAuthoringCatalog.CharacterClasses,
                cardPoolClasses = ContentAuthoringCatalog.CardPoolClasses,
                cardPoolKinds = ContentAuthoringCatalog.CardPoolKinds,
                cardAbilities = ContentAuthoringCatalog.CardAbilityTypes,
                contingencyTriggerTimings = ContentAuthoringCatalog.ContingencyTriggerTimings,
                characterTraitTriggers = ContentAuthoringCatalog.CharacterTraitTriggers,
                rarities = new[] { 0, 1, 2, 3, 4, 5 },
                scopes = new[] { "self", "single", "random", "all" },
                sides = new[] { "enemy", "ally" },
                selects = new[] { "random", "choose", "all" },
                piles = new[] { "hand", "draw", "discard", "exhaust" },
                tiers = new[] { "Weak", "Strong", "Elite", "Boss" },
                triggers = ContentAuthoringCatalog.Triggers,
                actions = ContentAuthoringCatalog.EffectActions,
                amountSources = ContentAuthoringCatalog.AmountSources,
                eventEffectTypes = ContentAuthoringCatalog.EventEffectTypes,
                eventConditionTypes = ContentAuthoringCatalog.EventConditionTypes,
                eventRarities = new[] { "common", "uncommon", "rare" },
                tileKinds = Enum.GetNames<TileKind>(),
                optionLabels = ContentAuthoringCatalog.DisplayNames,
                statuses = StatusDefinition.All.Select(status => new
                {
                    status.Id,
                    name = BattleStatusTextCatalog.Get(status.Id).Name,
                    description = BattleStatusTextCatalog.Get(status.Id).Description,
                    kind = status.Kind.ToString(),
                    status.IsPermanent,
                    tickDirection = status.TickDirection.ToString(),
                }),
                keywords = CardKeywords.All.Values,
            },
        });
    }

    [HttpGet("{kind}")]
    public IActionResult List(string kind)
    {
        if (!IsAuthoringEnvironment()) return NotFound();
        if (kind == "statusDefinitions")
            return Ok(StatusDefinition.All.Select(status => new
            {
                id = status.Id,
                name = BattleStatusTextCatalog.Get(status.Id).Name,
                document = new
                {
                    status.Id,
                    name = BattleStatusTextCatalog.Get(status.Id).Name,
                    description = BattleStatusTextCatalog.Get(status.Id).Description,
                    kind = status.Kind.ToString(),
                    status.IsPermanent,
                    tickDirection = status.TickDirection.ToString(),
                },
            }));
        if (kind == "effectDefinitions")
            return Ok(ContentAuthoringCatalog.EffectActions.Select(action => new { id = action, name = action, document = new { action } }));
        try { return Ok(_content.List(kind)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
    }

    [HttpGet("{kind}/{id}")]
    public IActionResult Get(string kind, string id)
    {
        if (!IsAuthoringEnvironment()) return NotFound();
        try { return Ok(_content.Get(kind, id)); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{kind}/{id}/validate")]
    public IActionResult Validate(string kind, string id, [FromBody] SaveContentRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        try
        {
            _content.Validate(kind, id, body.Document);
            return Ok(new { valid = true });
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
        { return BadRequest(new { valid = false, error = ex.Message }); }
    }

    [HttpPut("{kind}/{id}")]
    public IActionResult Save(string kind, string id, [FromBody] SaveContentRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        try { return Ok(_content.Save(kind, id, body.Document, body.Revision)); }
        catch (DevContentConflictException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{kind}/{id}/standard-v1")]
    public IActionResult SaveAsStandardV1(string kind, string id, [FromBody] SaveContentRequest body)
    {
        if (!_env.IsDevelopment()) return NotFound();
        try { return Ok(_content.SaveActiveVersionAsStandardV1(kind, id, body.Document, body.Revision)); }
        catch (DevContentConflictException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{kind}/{id}")]
    public IActionResult Delete(string kind, string id, [FromQuery] string? revision = null)
    {
        if (!_env.IsDevelopment()) return NotFound();
        try
        {
            _content.DeleteOverride(kind, id, revision);
            return NoContent();
        }
        catch (DevContentConflictException ex) { return Conflict(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("translate")]
    public IActionResult Translate([FromBody] TranslateEffectsRequest body)
    {
        if (!IsAuthoringEnvironment()) return NotFound();
        try
        {
            if (body.Effects.ValueKind != JsonValueKind.Array)
                return BadRequest(new { error = "effects must be an array." });
            var effects = body.Effects.EnumerateArray()
                .Select(effect => CardEffectParser.ParseEffect(
                    effect,
                    message => new InvalidDataException(message)))
                .ToArray();
            return Ok(new { description = CardTextFormatter.FormatEffects(effects) });
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
        { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("preview")]
    public IActionResult Preview([FromBody] PreviewContentRequest body)
    {
        if (!IsAuthoringEnvironment()) return NotFound();
        try
        {
            var json = body.Document.GetRawText();
            object result = body.Kind switch
            {
                "cards" => PreviewCard(json),
                "relics" => PreviewRelic(json),
                "potions" => PreviewPotion(json),
                "characters" => PreviewCharacter(json),
                "enemies" => PreviewMoves(EnemyJsonLoader.Parse(json).Moves),
                "units" => PreviewMoves(UnitJsonLoader.Parse(json).Moves),
                "encounters" => PreviewEncounter(json),
                "events" => PreviewEvent(json),
                "rewardTables" => PreviewRewardTable(json),
                _ => new { description = string.Empty, upgradedDescription = (string?)null },
            };
            return Ok(result);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
        { return BadRequest(new { error = ex.Message }); }
    }

    public sealed record SaveContentRequest(JsonElement Document, string? Revision = null);
    public sealed record TranslateEffectsRequest(JsonElement Effects);
    public sealed record PreviewContentRequest(string Kind, JsonElement Document);

    private bool IsAuthoringEnvironment()
        => _env.IsDevelopment() || _env.IsEnvironment("E2E");

    private static object PreviewCard(string json)
    {
        var card = CardJsonLoader.Parse(json);
        return new
        {
            description = CardTextFormatter.Format(card, upgraded: false),
            upgradedDescription = card.IsUpgradable ? CardTextFormatter.Format(card, upgraded: true) : null,
            classification = FormatCardClassification(card),
        };
    }

    private static object PreviewRelic(string json)
    {
        var relic = RelicJsonLoader.Parse(json);
        return new
        {
            description = CardTextFormatter.FormatEffects(relic.Effects),
            upgradedDescription = (string?)null,
            flavor = relic.Description,
        };
    }

    private static object PreviewPotion(string json)
    {
        var potion = PotionJsonLoader.Parse(json);
        return new
        {
            description = CardTextFormatter.FormatUsableEffects(
                potion.Effects, null, CardActorContext.Empty, potion.UsesPerTurn),
            upgradedDescription = (string?)null,
        };
    }

    private static object PreviewCharacter(string json)
    {
        var character = CharacterJsonLoader.Parse(json);
        var lines = character.EffectiveSkills.Select(skill =>
        {
            var effects = CardTextFormatter.FormatUsableEffects(
                skill.Effects, skill.Keywords, CardActorContext.Empty,
                skill.EffectiveUsesPerTurn);
            return $"{skill.Name}\n{effects}";
        });
        return new
        {
            description = string.Join("\n\n", lines),
            upgradedDescription = (string?)null,
            classification = $"クラス: {DisplayName("characterClasses", character.CharacterClass.ToString())}",
            initialDeck = character.Deck,
            character.EnergyPerTurn,
            character.DrawPerTurn,
            traitName = character.Trait?.Name,
            traitDescription = character.Trait?.Description ?? character.TraitDescription,
            traitTrigger = character.Trait is null
                ? null
                : DisplayName("characterTraitTriggers", character.Trait.Trigger.ToString()),
            traitEffects = character.Trait is null
                ? null
                : CardTextFormatter.FormatEffects(character.Trait.Effects),
        };
    }

    private static string FormatCardClassification(CardDefinition card)
    {
        var uses = card.EffectivePoolKinds.Count == 0
            ? "通常プール外"
            : string.Join("・", card.EffectivePoolKinds.Select(kind =>
                DisplayName("cardPoolKinds", kind.ToString())));
        var pool = card.PoolClass is null
            ? "未設定"
            : DisplayName("cardPoolClasses", card.PoolClass.Value.ToString());
        var eligible = card.EffectiveEligibleCharacterIds.Count == 0
            ? "同クラスの全キャラクター"
            : string.Join("、", card.EffectiveEligibleCharacterIds);
        return $"用途: {uses} / 所属: {pool} / 取得可能: {eligible}";
    }

    private static string DisplayName(string category, string value) =>
        ContentAuthoringCatalog.DisplayNames.TryGetValue($"{category}:{value}", out var name)
            ? name
            : value;

    private static object PreviewMoves(IReadOnlyList<RoguelikeCardGame.Core.Battle.Definitions.MoveDefinition> moves)
    {
        var lines = moves.Select(move =>
            $"{move.Id} [{move.Kind}]\n{CardTextFormatter.FormatEffects(move.Effects)}");
        return new { description = string.Join("\n\n", lines), upgradedDescription = (string?)null };
    }

    private static object PreviewEncounter(string json)
    {
        var encounter = EncounterJsonLoader.Parse(json);
        var enemies = string.Join(" / ", encounter.EnemyIds);
        return new
        {
            description = $"Act {encounter.Pool.Act} / {encounter.Pool.Tier}\n出現: {enemies}",
            upgradedDescription = (string?)null,
        };
    }

    private static object PreviewEvent(string json)
    {
        var definition = EventJsonLoader.Parse(json);
        var choices = definition.Choices.Select((choice, index) =>
        {
            var effects = choice.Effects.IsEmpty
                ? "効果なし"
                : string.Join(" / ", choice.Effects.Select(FormatEventEffect));
            return $"{index + 1}. {choice.Label}\n   {effects}\n   → {choice.ResultMessage}";
        });
        return new
        {
            description = $"{definition.StartMessage}\n\n{string.Join("\n\n", choices)}",
            upgradedDescription = (string?)null,
        };
    }

    private static string FormatEventEffect(EventEffect effect) => effect switch
    {
        EventEffect.GainGold value => $"ゴールド+{value.Amount}",
        EventEffect.PayGold value => $"ゴールドを{value.Amount}支払う",
        EventEffect.Heal value => $"HPを{value.Amount}回復",
        EventEffect.TakeDamage value => $"HPに{value.Amount}ダメージ",
        EventEffect.GainMaxHp value => $"最大HP+{value.Amount}",
        EventEffect.LoseMaxHp value => $"最大HP-{value.Amount}",
        EventEffect.GainRelicRandom value => $"{value.Rarity}レリックを獲得",
        EventEffect.GrantCardReward => "カード報酬を獲得",
        EventEffect.GainCard value => $"カード「{value.CardId}」を獲得",
        _ => effect.GetType().Name,
    };

    private static object PreviewRewardTable(string json)
    {
        var table = RewardTableJsonLoader.Parse(json);
        var pools = table.Pools.OrderBy(pair => pair.Key).Select(pair =>
            $"{pair.Key}: Gold {pair.Value.GoldMin}～{pair.Value.GoldMax} / " +
            $"Potion {pair.Value.PotionBasePercent}% / " +
            $"C{pair.Value.CommonPercent} R{pair.Value.RarePercent} E{pair.Value.EpicPercent}");
        return new
        {
            description = string.Join("\n", pools),
            upgradedDescription = (string?)null,
        };
    }

}
