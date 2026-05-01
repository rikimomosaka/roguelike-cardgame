using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardTextFormatterTests
{
    private static CardEffect E(
        string action,
        EffectScope scope,
        EffectSide? side,
        int amount,
        string? name = null,
        string? unitId = null)
        => new(action, scope, side, amount, name, unitId);

    // --- Attack ---

    [Fact]
    public void Attack_single_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) });
        Assert.Equal("敵 1 体に 6 ダメージ。", s);
    }

    [Fact]
    public void Attack_random_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.Random, EffectSide.Enemy, 5) });
        Assert.Equal("敵ランダム 1 体に 5 ダメージ。", s);
    }

    [Fact]
    public void Attack_all_enemies()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.All, EffectSide.Enemy, 8) });
        Assert.Equal("敵全体に 8 ダメージ。", s);
    }

    [Fact]
    public void Attack_repeated_collapses_to_x_n()
    {
        var s = CardTextFormatter.FormatEffects(new[]
        {
            E("attack", EffectScope.Single, EffectSide.Enemy, 5),
            E("attack", EffectScope.Single, EffectSide.Enemy, 5),
            E("attack", EffectScope.Single, EffectSide.Enemy, 5),
        });
        Assert.Equal("敵 1 体に 5 ダメージ × 3 回。", s);
    }

    // --- Block ---

    [Fact]
    public void Block_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.Self, null, 5) });
        Assert.Equal("ブロック 5 を得る。", s);
    }

    [Fact]
    public void Block_ally_single()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.Single, EffectSide.Ally, 3) });
        Assert.Equal("味方 1 体にブロック 3。", s);
    }

    [Fact]
    public void Block_ally_all()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.All, EffectSide.Ally, 4) });
        Assert.Equal("味方全体にブロック 4。", s);
    }

    // --- Draw / Discard ---

    [Fact]
    public void Draw_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("draw", EffectScope.Self, null, 2) });
        Assert.Equal("カードを 2 枚引く。", s);
    }

    [Fact]
    public void Discard_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("discard", EffectScope.Self, null, 1) });
        Assert.Equal("手札 1 枚を捨てる。", s);
    }

    // --- Buff / Debuff (status) ---

    [Fact]
    public void Buff_self_strength()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("buff", EffectScope.Self, null, 1, "strength") });
        Assert.Equal("自身に 筋力 1。", s);
    }

    [Fact]
    public void Debuff_weak_single_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("debuff", EffectScope.Single, EffectSide.Enemy, 1, "weak") });
        Assert.Equal("敵 1 体に 脱力 1。", s);
    }

    [Fact]
    public void Debuff_vulnerable_all_enemies()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("debuff", EffectScope.All, EffectSide.Enemy, 2, "vulnerable") });
        Assert.Equal("敵全体に 脆弱 2。", s);
    }

    [Fact]
    public void Buff_ally_all_dexterity()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("buff", EffectScope.All, EffectSide.Ally, 1, "dexterity") });
        Assert.Equal("味方全体に 敏捷 1。", s);
    }

    // --- Heal ---

    [Fact]
    public void Heal_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("heal", EffectScope.Self, null, 5) });
        Assert.Equal("HP を 5 回復。", s);
    }

    [Fact]
    public void Heal_ally_single()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("heal", EffectScope.Single, EffectSide.Ally, 3) });
        Assert.Equal("味方 1 体の HP を 3 回復。", s);
    }

    // --- Summon ---

    [Fact]
    public void Summon_unit()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("summon", EffectScope.Self, null, 0, name: null, unitId: "wisp") });
        Assert.Equal("wisp を召喚。", s);
    }

    // --- Pile ops ---

    [Fact]
    public void ExhaustCard()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("exhaustCard", EffectScope.Self, null, 1) });
        Assert.Equal("手札 1 枚を除外。", s);
    }

    [Fact]
    public void ExhaustSelf()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("exhaustSelf", EffectScope.Self, null, 0) });
        Assert.Equal("このカードを除外。", s);
    }

    [Fact]
    public void RetainSelf()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("retainSelf", EffectScope.Self, null, 0) });
        Assert.Equal("このカードを次ターンに持ち越す。", s);
    }

    [Fact]
    public void GainEnergy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("gainEnergy", EffectScope.Self, null, 1) });
        Assert.Equal("エナジー +1。", s);
    }

    [Fact]
    public void Upgrade()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("upgrade", EffectScope.Self, null, 1) });
        Assert.Equal("カード 1 枚を強化。", s);
    }

    // --- Concatenation ---

    [Fact]
    public void Multiple_distinct_effects_join_with_newline()
    {
        var s = CardTextFormatter.FormatEffects(new[]
        {
            E("attack", EffectScope.Single, EffectSide.Enemy, 6),
            E("block", EffectScope.Self, null, 3),
        });
        Assert.Equal("敵 1 体に 6 ダメージ。\nブロック 3 を得る。", s);
    }

    [Fact]
    public void Empty_effects_returns_empty_string()
    {
        var s = CardTextFormatter.FormatEffects(System.Array.Empty<CardEffect>());
        Assert.Equal(string.Empty, s);
    }

    // --- Format(def, upgraded) ---

    [Fact]
    public void Format_uses_override_when_description_set()
    {
        var def = new CardDefinition(
            Id: "x", Name: "x", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: null, Keywords: null, UpgradedKeywords: null,
            Description: "手書き", UpgradedDescription: null);
        Assert.Equal("手書き", CardTextFormatter.Format(def, upgraded: false));
    }

    [Fact]
    public void Format_falls_back_to_effects_when_override_null()
    {
        var def = new CardDefinition(
            Id: "x", Name: "x", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: null, Keywords: null, UpgradedKeywords: null,
            Description: null, UpgradedDescription: null);
        Assert.Equal("敵 1 体に 6 ダメージ。", CardTextFormatter.Format(def, upgraded: false));
    }

    [Fact]
    public void Format_upgraded_uses_upgraded_effects()
    {
        var def = new CardDefinition(
            Id: "x", Name: "x", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 9) },
            Keywords: null, UpgradedKeywords: null,
            Description: null, UpgradedDescription: null);
        Assert.Equal("敵 1 体に 9 ダメージ。", CardTextFormatter.Format(def, upgraded: true));
    }

    [Fact]
    public void Format_upgraded_uses_override_when_set()
    {
        var def = new CardDefinition(
            Id: "x", Name: "x", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 9) },
            Keywords: null, UpgradedKeywords: null,
            Description: null, UpgradedDescription: "強化手書き");
        Assert.Equal("強化手書き", CardTextFormatter.Format(def, upgraded: true));
    }

    [Fact]
    public void Format_whitespace_only_override_falls_back_to_formatter()
    {
        var def = new CardDefinition(
            Id: "x", Name: "x", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: null, Keywords: null, UpgradedKeywords: null,
            Description: "   ", UpgradedDescription: null);
        Assert.Equal("敵 1 体に 6 ダメージ。", CardTextFormatter.Format(def, upgraded: false));
    }
}
