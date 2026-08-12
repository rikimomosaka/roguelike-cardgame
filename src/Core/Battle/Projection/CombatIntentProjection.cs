namespace RoguelikeCardGame.Core.Battle.Projection;

/// <summary>
/// Immutable, UI-independent projection of one actor's currently announced action.
/// Attack totals remain split by scope so clients do not have to reconstruct rules.
/// </summary>
public sealed record CombatIntentProjection(
    int? AttackSingle,
    int? AttackRandom,
    int? AttackAll,
    int? AttackHits,
    int? AttackSingleHits,
    int? AttackRandomHits,
    int? AttackAllHits,
    int? Block,
    bool HasBuff,
    bool HasDebuff,
    bool HasHeal);
