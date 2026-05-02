using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Server.Controllers;

/// <summary>
/// Phase 10.5.M — Dev menu のフォーム dropdown / multi-select に渡す enum 値リスト供給。
/// Core 側の追加 (status / keyword / action) が UI に自動反映される。Development でのみ有効。
/// </summary>
[ApiController]
[Route("api/dev")]
public sealed class DevMetaController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public DevMetaController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpGet("meta")]
    public IActionResult GetMeta()
    {
        if (!_env.IsDevelopment()) return NotFound();
        return Ok(new
        {
            cardTypes = new[] { "Attack", "Skill", "Power", "Curse", "Status", "Unit" },
            rarities = new object[]
            {
                new { value = 0, label = "Promo" },
                new { value = 1, label = "Common" },
                new { value = 2, label = "Rare" },
                new { value = 3, label = "Epic" },
                new { value = 4, label = "Legendary" },
                new { value = 5, label = "Token" },
            },
            // Phase 10.5.M3: drawCards / retainSelf / exhaustSelf を一覧から外す。
            //   - drawCards は formatter / engine で draw alias として動作維持
            //   - retainSelf / exhaustSelf は keyword "wait" / "exhaust" 化、formatter alias は残す
            effectActions = new[]
            {
                "attack", "block", "buff", "debuff", "heal", "draw", "discard",
                "gainEnergy", "gainMaxEnergy", "exhaustCard",
                "upgrade", "summon", "selfDamage", "addCard", "recoverFromDiscard",
            },
            effectScopes = new[] { "self", "single", "random", "all" },
            effectSides = new[] { "enemy", "ally" },
            piles = new[] { "hand", "draw", "discard", "exhaust" },
            selectModes = new[] { "random", "choose", "all" },
            triggers = new[] { "OnTurnStart", "OnTurnEnd", "OnPlayCard", "OnDamageReceived", "OnCombo" },
            // Phase 10.5.L1: Relic editor 用 trigger 列。Core/Relics/RelicTrigger.cs と同期。
            relicTriggers = new[]
            {
                "OnPickup", "Passive", "OnBattleStart", "OnBattleEnd",
                "OnMapTileResolved", "OnTurnStart", "OnTurnEnd",
                "OnCardPlay", "OnEnemyDeath",
            },
            amountSources = new[]
            {
                "handCount", "drawPileCount", "discardPileCount", "exhaustPileCount",
                "selfHp", "selfHpLost", "selfBlock", "comboCount", "energy", "powerCardCount",
            },
            keywords = CardKeywords.All.Values
                .Select(k => new { id = k.Id, name = k.Name, description = k.Description })
                .ToArray(),
            statuses = new object[]
            {
                new { id = "weak", jp = "脱力" },
                new { id = "vulnerable", jp = "脆弱" },
                new { id = "strength", jp = "筋力" },
                new { id = "dexterity", jp = "敏捷" },
                new { id = "poison", jp = "毒" },
                new { id = "omnistrike", jp = "拡散" },
            },
        });
    }
}
