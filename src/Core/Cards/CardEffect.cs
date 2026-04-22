namespace RoguelikeCardGame.Core.Cards;

/// <summary>カード効果の基底。Type は JSON の "type" フィールドに対応。</summary>
public abstract record CardEffect(string Type);

/// <summary>ターゲットにダメージを与える。</summary>
public sealed record DamageEffect(int Amount) : CardEffect("damage");

/// <summary>自分にブロックを得る。</summary>
public sealed record GainBlockEffect(int Amount) : CardEffect("gainBlock");

/// <summary>最大 HP を恒久加算する（レリック OnPickup 用）。</summary>
public sealed record GainMaxHpEffect(int Amount) : CardEffect("gainMaxHp");

/// <summary>Gold を加算する（レリック OnPickup / OnMapTileResolved 用）。</summary>
public sealed record GainGoldEffect(int Amount) : CardEffect("gainGold");

/// <summary>Rest 時の回復量へ +Amount（レリック Passive 用）。</summary>
public sealed record RestHealBonusEffect(int Amount) : CardEffect("restHealBonus");

/// <summary>未知／将来拡張の効果。Type 文字列のみ保持する。</summary>
public sealed record UnknownEffect(string Type) : CardEffect(Type);
