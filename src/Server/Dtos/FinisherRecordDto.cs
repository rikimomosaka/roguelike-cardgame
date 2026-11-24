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
    bool? IsPlannedAfterimageIntentAvailable = null)
{
    /// <summary>
    /// 終撃によって保存された記録なら true。全クラス共通の「最後のアタック」保存は false で、
    /// プレイ履歴の残光表示（ボタンの「残光 N」・番号・説明文）は true の時だけ出す。
    /// </summary>
    public bool SavedByFinisher { get; init; }
}

/// <summary>
/// One saved card face. PlaySequence is the stable per-play UI key and retains
/// the original combo order even if identical card instances are represented.
/// </summary>
public sealed record FinisherRecordCardDto(
    long PlaySequence,
    BattleReplayCardDto Card);
