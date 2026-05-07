namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// Phase 10.5.M2-Choose: choose modal 待ち状態 (PendingCardPlay) の DTO。
/// Server BattleSession 内の <see cref="Core.Battle.State.PendingCardPlay"/> を Client に伝達する。
/// </summary>
public sealed record PendingCardPlayDto(
    string CardInstanceId,
    int EffectIndex,
    PendingChoiceDto Choice);

/// <summary>
/// Phase 10.5.M2-Choose: choose effect の選択候補と要件 DTO。
/// </summary>
public sealed record PendingChoiceDto(
    string Action,
    string Pile,
    int Count,
    string[] CandidateInstanceIds);

/// <summary>
/// Phase 10.5.M2-Choose: POST /battle/resolve-card-choice の request body。
/// </summary>
public sealed record ResolveCardChoiceRequestDto(
    string[] SelectedInstanceIds);
