using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル全体の不変状態。
/// 親 spec §3-1 参照。
/// 10.2.D で SummonHeld / PowerCards を追加（フィールド順は最終形に揃った）。
/// </summary>
public sealed record BattleState(
    int Turn,
    BattlePhase Phase,
    BattleOutcome Outcome,
    ImmutableArray<CombatActor> Allies,
    ImmutableArray<CombatActor> Enemies,
    int? TargetAllyIndex,
    int? TargetEnemyIndex,
    int Energy,
    int EnergyMax,
    ImmutableArray<BattleCardInstance> DrawPile,
    ImmutableArray<BattleCardInstance> Hand,
    ImmutableArray<BattleCardInstance> DiscardPile,
    ImmutableArray<BattleCardInstance> ExhaustPile,
    ImmutableArray<BattleCardInstance> SummonHeld,    // 10.2.D
    ImmutableArray<BattleCardInstance> PowerCards,    // 10.2.D
    int ComboCount,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    string EncounterId);
