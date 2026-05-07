using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// Phase 10.5.M2-Choose: PlayCard 中で choose effect に到達した時の pause 状態。
/// Resolve されるまで PlayCard は呼べない (validate で reject)。
/// BattleState は in-memory のみ (Server BattleSession.State) なので save schema 影響なし。
/// </summary>
public sealed record PendingCardPlay(
    string CardInstanceId,
    int EffectIndex,
    bool SummonSucceededBefore,
    PendingChoice Choice);

/// <summary>
/// Phase 10.5.M2-Choose: choose effect の選択候補と要件。
/// </summary>
public sealed record PendingChoice(
    string Action,
    string Pile,
    int Count,
    ImmutableArray<string> CandidateInstanceIds);
