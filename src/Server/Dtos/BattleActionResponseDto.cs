using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleActionResponseDto(
    BattleStateDto State,
    IReadOnlyList<BattleEventStepDto> Steps)
{
    /// <summary>戦闘開始の応答だけ: 初期行動を使う直前の状態（初期行動が無い時は null）。画面はこれを先に出す。</summary>
    public BattleStateDto? InitialState { get; init; }
}
