namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>敵が出現するアクトと強さ区分の組み合わせ。</summary>
public sealed record EnemyPool(int Act, EnemyTier Tier);
