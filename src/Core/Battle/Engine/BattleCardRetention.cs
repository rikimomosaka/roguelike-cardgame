using System;
using System.Linq;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Defines the single retention rule used by every participant at turn end.
/// </summary>
public static class BattleCardRetention
{
    /// <param name="ownerPhase">カードの持ち主の位相。静なら双極付きのカードも残る。</param>
    public static bool ShouldRetain(
        BattleCardInstance card,
        CardDefinition definition,
        ActorPhase ownerPhase)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(definition);

        var effects = card.IsUpgraded && definition.UpgradedEffects is not null
            ? definition.UpgradedEffects
            : definition.Effects;
        if (ownerPhase == ActorPhase.Calm && card.KeywordCount(CardKeywords.Bipolar) > 0)
            return true;
        return effects.Any(effect => effect.Action == "retainSelf")
            || (definition.EffectiveKeywords(card.IsUpgraded)?.Contains("wait") ?? false);
    }
}
