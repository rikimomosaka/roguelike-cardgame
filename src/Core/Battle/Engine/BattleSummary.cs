using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 戦闘終了時に <see cref="BattleEngine.Finalize"/> が返すサマリ。
/// 親 spec §10-2 参照。
/// 10.2.E で ConsumedPotionIds / RunSideOperations が追加予定。
/// </summary>
public sealed record BattleSummary(
    int FinalHeroHp,
    BattleOutcome Outcome,
    string EncounterId);
