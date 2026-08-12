namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// The provisional owner of battle-wide solo resources such as the shared hand.
/// Explicit per-player hand ownership can replace this query when co-op ownership is modeled.
/// </summary>
public sealed record ProvisionalSoloHeroOwner(CombatActor Hero, int AllyIndex);

/// <summary>
/// Resolves the living hero with the lowest slot. Equal slots retain ally-array order.
/// </summary>
public static class ProvisionalSoloHeroOwnerResolver
{
    public static ProvisionalSoloHeroOwner? Resolve(BattleState state)
    {
        int ownerIndex = -1;
        int lowestSlot = int.MaxValue;

        for (int i = 0; i < state.Allies.Length; i++)
        {
            var ally = state.Allies[i];
            if (ally.DefinitionId == "hero" && ally.IsAlive &&
                (ownerIndex < 0 || ally.SlotIndex < lowestSlot))
            {
                ownerIndex = i;
                lowestSlot = ally.SlotIndex;
            }
        }

        return ownerIndex < 0
            ? null
            : new ProvisionalSoloHeroOwner(state.Allies[ownerIndex], ownerIndex);
    }
}
