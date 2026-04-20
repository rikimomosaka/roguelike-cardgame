namespace RoguelikeCardGame.Core.Cards;

/// <summary>カード効果の基底。Type は JSON の "type" フィールドに対応。</summary>
public abstract record CardEffect(string Type);

/// <summary>ターゲットにダメージを与える。</summary>
public sealed record DamageEffect(int Amount) : CardEffect("damage");

/// <summary>自分にブロックを得る。</summary>
public sealed record GainBlockEffect(int Amount) : CardEffect("gainBlock");

/// <summary>未知／将来拡張の効果。Type 文字列のみ保持する。</summary>
public sealed record UnknownEffect(string TypeName) : CardEffect(TypeName);
