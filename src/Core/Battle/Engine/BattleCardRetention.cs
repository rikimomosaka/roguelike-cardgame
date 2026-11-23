using System;
using System.Linq;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Defines the single retention rule used by every participant at turn end.
/// </summary>
internal static class BattleCardRetention
{
    public static bool ShouldRetain(
        BattleCardInstance card,
        CardDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(definition);

        var effects = card.IsUpgraded && definition.UpgradedEffects is not null
            ? definition.UpgradedEffects
            : definition.Effects;
        return effects.Any(effect => effect.Action == "retainSelf")
            || (definition.EffectiveKeywords(card.IsUpgraded)?.Contains("wait") ?? false);
    }
}
