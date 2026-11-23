using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>Turn-boundary rules owned by the Guardian status family.</summary>
internal static class GuardianTurnEndRules
{
    internal sealed record CountdownResult(
        CombatActor Actor,
        IReadOnlyList<BattleEvent> Events);

    /// <summary>
    /// Fortify keeps its fixed allowance first. Steadfast then keeps half,
    /// rounded down, of only the Block that Fortify would otherwise lose.
    /// </summary>
    public static CombatActor ApplyBlockCarry(CombatActor actor)
    {
        int displayedBlock = actor.Block.Display(
            actor.GetStatus(BattleStatusIds.Dexterity));
        int fortifyLimit = System.Math.Max(
            0, actor.GetStatus(BattleStatusIds.Fortify));
        int fortifyCarry = System.Math.Min(displayedBlock, fortifyLimit);
        int lostAfterFortify = displayedBlock - fortifyCarry;
        int steadfastCarry = actor.GetStatus(BattleStatusIds.Steadfast) > 0
            ? lostAfterFortify / 2
            : 0;
        int totalCarry = (int)System.Math.Min(
            int.MaxValue, (long)fortifyCarry + steadfastCarry);
        return actor with { Block = new BlockPool(totalCarry, 0) };
    }

    /// <summary>
    /// Applies duration countdown after every participant has committed its
    /// turn-end state. Afterglow's caller can suppress the entire group.
    /// </summary>
    public static CountdownResult ApplyCountdown(
        CombatActor actor,
        bool preserveCountdownBuffs,
        int orderStart = 0)
    {
        if (preserveCountdownBuffs)
            return new CountdownResult(actor, System.Array.Empty<BattleEvent>());

        var events = new List<BattleEvent>();
        int order = orderStart;
        var updated = actor;
        foreach (string statusId in CountdownStatusIds)
        {
            if (updated.GetStatus(statusId) <= 0) continue;

            var source = updated.GetStatusSource(statusId);
            bool removes = updated.GetStatus(statusId) == 1;
            updated = BattleStatusRules.Decrement(updated, statusId);
            if (removes)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus,
                    Order: order++,
                    TargetInstanceId: actor.InstanceId,
                    Note: statusId,
                    Source: source));
            }
        }

        return new CountdownResult(updated, events);
    }

    private static IReadOnlyList<string> CountdownStatusIds { get; } = new[]
    {
        BattleStatusIds.Steadfast,
        BattleStatusIds.Cover,
        BattleStatusIds.DefensiveStance,
    };
}
