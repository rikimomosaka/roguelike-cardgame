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

    private static CardDefinition MakeAttackDef(
        int amount,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<string>? upgradedKeywords = null)
        => new(
            Id: "x", Name: "x", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { E("attack", EffectScope.Single, EffectSide.Enemy, amount) },
            UpgradedEffects: null, Keywords: keywords, UpgradedKeywords: upgradedKeywords,
            Description: null, UpgradedDescription: null);

    // --- Attack ---

    [Fact]
    public void Attack_single_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) });
        Assert.Equal("敵 1 体に [N:6] ダメージ。", s);
    }

    [Fact]
    public void Attack_random_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.Random, EffectSide.Enemy, 5) });
        Assert.Equal("敵ランダム 1 体に [N:5] ダメージ。", s);
    }

    [Fact]
    public void Attack_all_enemies()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.All, EffectSide.Enemy, 8) });
        Assert.Equal("敵全体に [N:8] ダメージ。", s);
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
        Assert.Equal("敵 1 体に [N:5] ダメージ × [N:3] 回。", s);
    }

    // --- Block ---

    [Fact]
    public void Block_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.Self, null, 5) });
        Assert.Equal("ブロック [N:5] を得る。", s);
    }

    [Fact]
    public void Block_ally_single()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.Single, EffectSide.Ally, 3) });
        Assert.Equal("味方 1 体にブロック [N:3]。", s);
    }

    [Fact]
    public void Block_ally_all()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.All, EffectSide.Ally, 4) });
        Assert.Equal("味方全体にブロック [N:4]。", s);
    }

    // --- Draw / Discard ---

    [Fact]
    public void Draw_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("draw", EffectScope.Self, null, 2) });
        Assert.Equal("カードを [N:2] 枚引く。", s);
    }

    [Fact]
    public void Discard_self_default_random()
    {
        // Select なしは旧仕様 (ランダム) の文言を維持
        var s = CardTextFormatter.FormatEffects(new[] { E("discard", EffectScope.Self, null, 1) });
        Assert.Equal("手札 [N:1] 枚を捨てる。", s);
    }

    [Fact]
    public void Discard_with_select_choose()
    {
        var e = new CardEffect("discard", EffectScope.Self, null, 1, Select: "choose");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("手札を選んで [N:1] 枚捨てる。", s);
    }

    [Fact]
    public void Discard_with_select_random_explicit()
    {
        var e = new CardEffect("discard", EffectScope.Self, null, 1, Select: "random");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("手札からランダムに [N:1] 枚捨てる。", s);
    }

    // --- Buff / Debuff (status with 「を付与」 suffix) ---

    [Fact]
    public void Buff_self_strength()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("buff", EffectScope.Self, null, 1, "strength") });
        Assert.Equal("自身に 筋力 [N:1] を付与。", s);
    }

    [Fact]
    public void Debuff_weak_single_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("debuff", EffectScope.Single, EffectSide.Enemy, 1, "weak") });
        Assert.Equal("敵 1 体に 脱力 [N:1] を付与。", s);
    }

    [Fact]
    public void Debuff_vulnerable_all_enemies()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("debuff", EffectScope.All, EffectSide.Enemy, 2, "vulnerable") });
        Assert.Equal("敵全体に 脆弱 [N:2] を付与。", s);
    }

    [Fact]
    public void Buff_ally_all_dexterity()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("buff", EffectScope.All, EffectSide.Ally, 1, "dexterity") });
        Assert.Equal("味方全体に 敏捷 [N:1] を付与。", s);
    }

    // --- Heal ---

    [Fact]
    public void Heal_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("heal", EffectScope.Self, null, 5) });
        Assert.Equal("HP を [N:5] 回復。", s);
    }

    [Fact]
    public void Heal_ally_single()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("heal", EffectScope.Single, EffectSide.Ally, 3) });
        Assert.Equal("味方 1 体の HP を [N:3] 回復。", s);
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
        Assert.Equal("手札 [N:1] 枚を除外。", s);
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
        Assert.Equal("エナジー +[N:1]。", s);
    }

    [Fact]
    public void Upgrade()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("upgrade", EffectScope.Self, null, 1) });
        Assert.Equal("カード [N:1] 枚を強化。", s);
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
        Assert.Equal("敵 1 体に [N:6] ダメージ。\nブロック [N:3] を得る。", s);
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
        Assert.Equal("敵 1 体に [N:6] ダメージ。", CardTextFormatter.Format(def, upgraded: false));
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
        Assert.Equal("敵 1 体に [N:9] ダメージ。", CardTextFormatter.Format(def, upgraded: true));
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
        Assert.Equal("敵 1 体に [N:6] ダメージ。", CardTextFormatter.Format(def, upgraded: false));
    }

    // --- 10.5.B: Keyword lines ---

    [Fact]
    public void Keywords_render_as_separate_lines_at_top()
    {
        var def = MakeAttackDef(amount: 5, keywords: new[] { "wild" });
        var s = CardTextFormatter.Format(def, upgraded: false);
        Assert.Equal("[K:wild]\n敵 1 体に [N:5] ダメージ。", s);
    }

    [Fact]
    public void Multiple_keywords_each_on_own_line()
    {
        var def = MakeAttackDef(amount: 5, keywords: new[] { "wild", "superwild" });
        var s = CardTextFormatter.Format(def, upgraded: false);
        Assert.Equal("[K:wild]\n[K:superwild]\n敵 1 体に [N:5] ダメージ。", s);
    }

    [Fact]
    public void Upgraded_keywords_used_when_upgraded()
    {
        var def = MakeAttackDef(amount: 5,
            keywords: new[] { "wild" },
            upgradedKeywords: new[] { "superwild" });
        var s = CardTextFormatter.Format(def, upgraded: true);
        Assert.StartsWith("[K:superwild]\n", s);
    }

    [Fact]
    public void No_keyword_line_when_keywords_empty()
    {
        var def = MakeAttackDef(amount: 5, keywords: null);
        var s = CardTextFormatter.Format(def, upgraded: false);
        Assert.Equal("敵 1 体に [N:5] ダメージ。", s);
    }

    // --- 10.5.B: New action specs ---

    [Fact]
    public void SelfDamage_emits_jp_text()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("selfDamage", EffectScope.Self, null, 3) });
        Assert.Equal("自身のHPを-[N:3]。", s);
    }

    [Fact]
    public void AddCard_to_hand()
    {
        var e = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "hand", CardRefId: "strike");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("[C:strike] を手札に [N:1] 枚加える。", s);
    }

    [Fact]
    public void AddCard_to_drawpile()
    {
        var e = new CardEffect("addCard", EffectScope.Self, null, 2, Pile: "draw", CardRefId: "burn");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("[C:burn] を山札に [N:2] 枚加える。", s);
    }

    [Fact]
    public void AddCard_to_discard()
    {
        var e = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "discard", CardRefId: "wound");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("[C:wound] を捨札に [N:1] 枚加える。", s);
    }

    [Fact]
    public void RecoverFromDiscard_random_to_hand()
    {
        var e = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 2, Pile: "hand", Select: "random");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("捨札からランダムに [N:2] 枚、手札に戻す。", s);
    }

    [Fact]
    public void RecoverFromDiscard_choose_to_exhaust()
    {
        var e = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 1, Pile: "exhaust", Select: "choose");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("捨札から選んで [N:1] 枚、除外する。", s);
    }

    [Fact]
    public void GainMaxEnergy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("gainMaxEnergy", EffectScope.Self, null, 1) });
        Assert.Equal("エナジー上限を+[N:1]する。", s);
    }

    // --- 10.5.B: Power trigger marker ---

    [Fact]
    public void Power_trigger_emits_marker_prefix()
    {
        var e = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("[T:OnTurnStart]の度にカードを [N:1] 枚引く。", s);
    }

    [Fact]
    public void Power_trigger_with_repeat_count_groups_correctly()
    {
        // 同一 Trigger + 同一 spec が連続したら × N 回
        var e = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart");
        var s = CardTextFormatter.FormatEffects(new[] { e, e });
        Assert.Equal("[T:OnTurnStart]の度にカードを [N:1] 枚引く × [N:2] 回。", s);
    }

    [Fact]
    public void Different_trigger_breaks_grouping()
    {
        var a = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart");
        var b = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnPlayCard");
        var s = CardTextFormatter.FormatEffects(new[] { a, b });
        Assert.Equal("[T:OnTurnStart]の度にカードを [N:1] 枚引く。\n[T:OnPlayCard]の度にカードを [N:1] 枚引く。", s);
    }

    // --- 10.5.B: AmountSource (Variable X) marker ---

    [Fact]
    public void AmountSource_handCount_emits_X_marker()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0, AmountSource: "handCount");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("敵 1 体に [V:X|手札の数] ダメージ。", s);
    }

    [Fact]
    public void AmountSource_drawPileCount_block()
    {
        var e = new CardEffect("block", EffectScope.Self, null, 0, AmountSource: "drawPileCount");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("ブロック [V:X|山札の数] を得る。", s);
    }

    [Fact]
    public void AmountSource_unknown_passes_raw()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0, AmountSource: "weirdSrc");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("敵 1 体に [V:X|weirdSrc] ダメージ。", s);
    }

    // --- 10.5.C: CardActorContext (up/down marker) ---

    [Fact]
    public void Attack_with_strength_emits_up_marker()
    {
        var def = MakeAttackDef(amount: 5);
        var ctx = new CardActorContext(Strength: 2, Weak: 0, Dexterity: 0);
        var s = CardTextFormatter.Format(def, upgraded: false, ctx);
        Assert.Equal("敵 1 体に [N:7|up] ダメージ。", s);
    }

    [Fact]
    public void Attack_with_weak_emits_down_marker()
    {
        var def = MakeAttackDef(amount: 5);
        var ctx = new CardActorContext(Strength: 0, Weak: 1, Dexterity: 0);
        // 5 * 0.75 = 3.75 → floor 3
        var s = CardTextFormatter.Format(def, upgraded: false, ctx);
        Assert.Equal("敵 1 体に [N:3|down] ダメージ。", s);
    }

    [Fact]
    public void Attack_unchanged_emits_no_modifier()
    {
        var def = MakeAttackDef(amount: 5);
        var s = CardTextFormatter.Format(def, upgraded: false, CardActorContext.Empty);
        Assert.Equal("敵 1 体に [N:5] ダメージ。", s);
    }

    [Fact]
    public void Block_with_dexterity_emits_up()
    {
        var def = new CardDefinition(
            Id: "x", Name: "x", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { E("block", EffectScope.Self, null, 5) },
            UpgradedEffects: null, Keywords: null, UpgradedKeywords: null,
            Description: null, UpgradedDescription: null);
        var ctx = new CardActorContext(Strength: 0, Weak: 0, Dexterity: 3);
        var s = CardTextFormatter.Format(def, upgraded: false, ctx);
        Assert.Equal("ブロック [N:8|up] を得る。", s);
    }

    [Fact]
    public void Strength_after_weak_uses_floor()
    {
        // (5 + 2) * 0.75 = 5.25 → 5。base 5 と等しいので無修飾。
        var def = MakeAttackDef(amount: 5);
        var ctx = new CardActorContext(Strength: 2, Weak: 1, Dexterity: 0);
        var s = CardTextFormatter.Format(def, upgraded: false, ctx);
        Assert.Equal("敵 1 体に [N:5] ダメージ。", s);
    }

    [Fact]
    public void Format_overload_without_context_keeps_existing_behavior()
    {
        // 既存 Format(def, upgraded) は無 context として動く (CardActorContext.Empty 経由)。
        var def = MakeAttackDef(amount: 5);
        var s = CardTextFormatter.Format(def, upgraded: false);
        Assert.Equal("敵 1 体に [N:5] ダメージ。", s);
    }

    [Fact]
    public void FormatEffects_context_overload_applies_adjustment()
    {
        var ctx = new CardActorContext(Strength: 3, Weak: 0, Dexterity: 0);
        var s = CardTextFormatter.FormatEffects(
            new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) }, ctx);
        Assert.Equal("敵 1 体に [N:9|up] ダメージ。", s);
    }

    [Fact]
    public void Block_context_only_dexterity_does_not_affect_attack_in_same_card()
    {
        // attack は dexterity を見ず、block は strength/weak を見ない。
        var def = new CardDefinition(
            Id: "x", Name: "x", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[]
            {
                E("attack", EffectScope.Single, EffectSide.Enemy, 6),
                E("block", EffectScope.Self, null, 5),
            },
            UpgradedEffects: null, Keywords: null, UpgradedKeywords: null,
            Description: null, UpgradedDescription: null);
        var ctx = new CardActorContext(Strength: 0, Weak: 0, Dexterity: 2);
        var s = CardTextFormatter.Format(def, upgraded: false, ctx);
        Assert.Equal("敵 1 体に [N:6] ダメージ。\nブロック [N:7|up] を得る。", s);
    }

    [Fact]
    public void AmountSource_with_context_keeps_variable_marker()
    {
        // Variable X を使う effect は context があっても [V:X|...] を維持 (10.5.D で別途扱う)。
        var ctx = new CardActorContext(Strength: 5, Weak: 0, Dexterity: 0);
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0, AmountSource: "handCount");
        var s = CardTextFormatter.FormatEffects(new[] { e }, ctx);
        Assert.Equal("敵 1 体に [V:X|手札の数] ダメージ。", s);
    }
}
