using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Data;

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
            effectActions = ContentAuthoringCatalog.EffectActions,
            cardAbilities = ContentAuthoringCatalog.CardAbilityTypes,
            contingencyTriggerTimings = ContentAuthoringCatalog.ContingencyTriggerTimings,
            effectScopes = new[] { "self", "single", "random", "all" },
            effectSides = new[] { "enemy", "ally" },
            piles = new[] { "hand", "draw", "discard", "exhaust" },
            selectModes = new[] { "random", "choose", "all" },
            // Phase 10.5.L1.5: relic と power を統一した trigger リスト (18 値)。
            // engine 発火が実装済みのもの (12) と spec 列挙のみ (6: 10.6 で実装) を含む。
            triggers = ContentAuthoringCatalog.Triggers,
            amountSources = ContentAuthoringCatalog.AmountSources,
            keywords = CardKeywords.All.Values
                .Select(k => new { id = k.Id, name = k.Name, description = k.Description })
                .ToArray(),
            statuses = StatusDefinition.All.Select(status => new
            {
                id = status.Id,
                jp = BattleStatusTextCatalog.Get(status.Id).Name,
                kind = status.Kind.ToString(),
            }),
        });
    }
}
