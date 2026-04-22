using System.Text.Json.Serialization;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Events;

/// <summary>イベント選択肢が適用する効果。タグ付き union（record hierarchy）。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(GainGold), typeDiscriminator: "gainGold")]
[JsonDerivedType(typeof(PayGold), typeDiscriminator: "payGold")]
[JsonDerivedType(typeof(Heal), typeDiscriminator: "heal")]
[JsonDerivedType(typeof(TakeDamage), typeDiscriminator: "takeDamage")]
[JsonDerivedType(typeof(GainMaxHp), typeDiscriminator: "gainMaxHp")]
[JsonDerivedType(typeof(LoseMaxHp), typeDiscriminator: "loseMaxHp")]
[JsonDerivedType(typeof(GainRelicRandom), typeDiscriminator: "gainRelicRandom")]
[JsonDerivedType(typeof(GrantCardReward), typeDiscriminator: "grantCardReward")]
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
