using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 戦闘終了時に <see cref="BattleEngine.Finalize"/> が返すサマリ。
/// 親 spec §10-2 / 10.2.E spec §6 参照。
/// </summary>
public sealed record BattleSummary(
    int FinalHeroHp,
    RoguelikeCardGame.Core.Battle.State.BattleOutcome Outcome,
    string EncounterId,
    ImmutableArray<string> ConsumedPotionIds);    // 10.2.E
