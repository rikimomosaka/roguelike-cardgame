using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Events;

/// <summary>イベント選択肢が適用する効果。タグ付き union（record hierarchy）。</summary>
public abstract record EventEffect
{
    public sealed record GainGold(int Amount) : EventEffect;
    public sealed record PayGold(int Amount) : EventEffect;
    public sealed record Heal(int Amount) : EventEffect;
    public sealed record TakeDamage(int Amount) : EventEffect;
    public sealed record GainMaxHp(int Amount) : EventEffect;
    public sealed record LoseMaxHp(int Amount) : EventEffect;
    public sealed record GainRelicRandom(CardRarity Rarity) : EventEffect;
    public sealed record GrantCardReward() : EventEffect;
}
