using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// Card-play state owned by one independently controlled combat actor.
/// Combat statistics and statuses remain on <see cref="CombatActor"/>; this
/// record owns zones, resources, combo tracking, choices, and turn readiness.
/// </summary>
public sealed record BattleParticipantState(
    string ParticipantId,
    string ActorInstanceId,
    BattleParticipantControllerKind ControllerKind,
    int? OwnerPlayerSlot,
    string CharacterId,
    int? TargetAllyIndex,
    int? TargetEnemyIndex,
    int Energy,
    int EnergyPerTurn,
    int DrawPerTurn,
    ImmutableArray<BattleCardInstance> DrawPile,
    ImmutableArray<BattleCardInstance> Hand,
    ImmutableArray<BattleCardInstance> DiscardPile,
    ImmutableArray<BattleCardInstance> ExhaustPile,
    ImmutableArray<BattleCardInstance> SummonHeld,
    ImmutableArray<BattleCardInstance> PowerCards,
    int ComboCount,
    int MaxComboThisTurn,
    int ComboGauge,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    bool WildUsedInCurrentCombo,
    ImmutableHashSet<string> UsedCharacterSkillIdsThisTurn,
    ImmutableDictionary<string, int> CharacterSkillUsesThisTurn,
    ImmutableDictionary<string, int> CardUsesThisTurn,
    bool CardUseLockedThisTurn,
    ImmutableArray<string> OwnedRelicIds,
    ImmutableArray<string> Potions,
    ImmutableDictionary<string, int> PotionUsesThisTurn,
    PendingCardPlay? PendingCardPlay,
    bool IsReady)
{
    /// <summary>
    /// CPU card plays already committed in the current turn.  This is stored
    /// on the participant rather than in a coordinator invocation so the
    /// safety cap cannot be bypassed by repeatedly reopening CPU planning.
    /// Human participants leave this value at zero.
    /// </summary>
    public int CpuCardPlaysThisTurn { get; init; }

    /// <summary>
    /// Legal actions that existed when this CPU most recently became Ready.
    /// External changes reopen it only when they add a new key, so a CPU that
    /// stopped on a repeated state cannot be restarted by an unrelated command.
    /// </summary>
    public ImmutableHashSet<string> CpuLegalActionKeysWhenReadied { get; init; } =
        ImmutableHashSet<string>.Empty;

    /// <summary>Per-trait activation counts for the current turn.</summary>
    public ImmutableDictionary<string, int> CharacterTraitUsesThisTurn { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    /// <summary>Real cards played by this participant during the current turn.</summary>
    public ImmutableArray<PlayedCardEffectSnapshot> PlayedCardHistoryThisTurn { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    /// <summary>
    /// Effect resolutions eligible for Reprise. This is battle-persistent and
    /// therefore is not cleared at an ordinary turn boundary.
    /// </summary>
    public ImmutableArray<PlayedCardEffectSnapshot> RepriseEligibleHistory { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    /// <summary>Real cards in the currently connected combo, in play order.</summary>
    public ImmutableArray<PlayedCardEffectSnapshot> CurrentComboCardHistory { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    /// <summary>
    /// Attack-card snapshots saved by the most recent Finisher this turn.
    /// </summary>
    public ImmutableArray<PlayedCardEffectSnapshot> LastFinisherRecord { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    /// <summary>Contingency cards currently armed in the exhaust pile.</summary>
    public ImmutableArray<ArmedContingency> ArmedContingencies { get; init; } =
        ImmutableArray<ArmedContingency>.Empty;

    public long NextCardPlaySequence { get; init; }

    public long NextEffectResolutionSequence { get; init; }

    public long NextContingencyOrdinal { get; init; }

    /// <summary>
    /// Set at turn end when Combo Carryover preserved the active combo.  Turn
    /// start consumes this marker so arbitrary stale combo fields are not
    /// mistaken for a legitimate carryover.
    /// </summary>
    public bool ComboCarryoverPending { get; init; }
}
