namespace RoguelikeCardGame.Core.Enemy;

/// <summary>敵の強さ区分。</summary>
public enum EnemyTier
{
    Weak,
    Strong,
    Elite,
    Boss,
}

/// <summary>敵が出現するアクトと強さ区分の組み合わせ。</summary>
public sealed record EnemyPool(int Act, EnemyTier Tier);
