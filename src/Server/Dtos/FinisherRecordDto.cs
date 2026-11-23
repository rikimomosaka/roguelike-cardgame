namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// Attack cards captured by the most recent Finisher and the deterministic
/// attacker-side intent that Afterimage would replay from them now.
/// </summary>
public sealed record FinisherRecordDto(
    int AfterimageStacks,
    IReadOnlyList<FinisherRecordCardDto> Cards,
    IntentDto? PlannedAfterimageIntent,
    /// <summary>
    /// null when Afterimage is not currently applicable; true when the preview
    /// is deterministic (including a known zero attacks); false when it is not.
    /// </summary>
    bool? IsPlannedAfterimageIntentAvailable = null);

/// <summary>
/// One saved card face. PlaySequence is the stable per-play UI key and retains
/// the original combo order even if identical card instances are represented.
/// </summary>
public sealed record FinisherRecordCardDto(
    long PlaySequence,
    BattleReplayCardDto Card);
