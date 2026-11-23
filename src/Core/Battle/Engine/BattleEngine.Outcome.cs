using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// Planning commands can deal immediate self damage.  Resolve the battle
    /// after the participant projection has been committed, then append the
    /// terminal event without changing the already-established event order.
    /// </summary>
    private static (BattleState State, IReadOnlyList<BattleEvent> Events)
        ApplyOutcomeAfterPlanningCommand(
            BattleState state,
            IReadOnlyList<BattleEvent> commandEvents)
    {
        // A planning command can kill a summoned participant immediately
        // (self damage, a reaction, and similar effects).  Remove that actor at
        // the command commit boundary so its held card is returned and its
        // battlefield slot can be reused by a later command in the same
        // planning phase.  SummonCleanup currently emits no additional event;
        // keep the mutable list/order here so that remains safe if it does in
        // the future.
        var cleanedEvents = commandEvents.ToList();
        int cleanupOrder = cleanedEvents.Count;
        state = SummonCleanup.Apply(state, cleanedEvents, ref cleanupOrder);
        commandEvents = cleanedEvents;

        var (terminal, terminalEvents) = BattleOutcomeResolver.ApplyIfTerminal(
            state, commandEvents.Count);
        if (terminalEvents.Count == 0)
            return (state, commandEvents);

        var combined = ImmutableArray.CreateBuilder<BattleEvent>(
            commandEvents.Count + terminalEvents.Count);
        combined.AddRange(commandEvents);
        combined.AddRange(terminalEvents);
        return (terminal, combined.ToImmutable());
    }
}
