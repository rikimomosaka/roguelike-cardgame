using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル全体の不変状態。
/// 親 spec §3-1 参照。
/// 10.2.C で ComboCount / LastPlayedOrigCost / NextCardComboFreePass を追加。
/// 10.2.D で SummonHeld / PowerCards が ExhaustPile の後に追加される予定（その時点でフィールド順を最終形に揃える）。
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
    int ComboCount,                       // 10.2.C: 現在のコンボ数 (0..N)
    int? LastPlayedOrigCost,              // 10.2.C: 直前に手打ちプレイしたカードの元コスト
    bool NextCardComboFreePass,           // 10.2.C: SuperWild 由来。次のカード 1 枚はコンボ条件 bypass
    string EncounterId);
