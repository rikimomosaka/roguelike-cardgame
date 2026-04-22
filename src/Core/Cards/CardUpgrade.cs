using System;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Cards;

public static class CardUpgrade
{
    public static bool CanUpgrade(CardInstance ci, DataCatalog catalog)
    {
        if (ci.Upgraded) return false;
        if (!catalog.TryGetCard(ci.Id, out var def)) return false;
        return def.UpgradedEffects is not null;
    }

    public static CardInstance Upgrade(CardInstance ci)
    {
        if (ci.Upgraded) throw new InvalidOperationException($"Card {ci.Id} already upgraded");
        return ci with { Upgraded = true };
    }
}
