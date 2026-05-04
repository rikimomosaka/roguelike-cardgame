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
        Assert.Equal("敵単体に[N:6]アタック。", s);
    }

    [Fact]
    public void Attack_random_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.Random, EffectSide.Enemy, 5) });
        Assert.Equal("ランダムな敵に[N:5]アタック。", s);
    }

    [Fact]
    public void Attack_all_enemies()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("attack", EffectScope.All, EffectSide.Enemy, 8) });
        Assert.Equal("敵全体に[N:8]アタック。", s);
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
        Assert.Equal("敵単体に[N:5]アタック×[N:3]。", s);
    }

    // --- Block ---

    [Fact]
    public void Block_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.Self, null, 5) });
        Assert.Equal("自身に[N:5]ブロック。", s);
    }

    [Fact]
    public void Block_ally_single()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.Single, EffectSide.Ally, 3) });
        Assert.Equal("味方単体に[N:3]ブロック。", s);
    }

    [Fact]
    public void Block_ally_all()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("block", EffectScope.All, EffectSide.Ally, 4) });
        Assert.Equal("味方全体に[N:4]ブロック。", s);
    }

    // --- Draw / Discard ---

    [Fact]
    public void Draw_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("draw", EffectScope.Self, null, 2) });
        Assert.Equal("[N:2]枚引く。", s);
    }

    [Fact]
    public void Discard_self_default_random()
    {
        // Select なしは旧仕様 (ランダム) の文言を維持
        var s = CardTextFormatter.FormatEffects(new[] { E("discard", EffectScope.Self, null, 1) });
        Assert.Equal("手札[N:1]枚を捨てる。", s);
    }

    [Fact]
    public void Discard_with_select_choose()
    {
        var e = new CardEffect("discard", EffectScope.Self, null, 1, Select: "choose");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("手札を選んで[N:1]枚捨てる。", s);
    }

    [Fact]
    public void Discard_with_select_random_explicit()
    {
        var e = new CardEffect("discard", EffectScope.Self, null, 1, Select: "random");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("手札からランダムに[N:1]枚捨てる。", s);
    }

    // --- Buff / Debuff (status with 「を付与」 suffix) ---

    [Fact]
    public void Buff_self_strength()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("buff", EffectScope.Self, null, 1, "strength") });
        Assert.Equal("自身に[S:strength][N:1]を付与。", s);
    }

    [Fact]
    public void Debuff_weak_single_enemy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("debuff", EffectScope.Single, EffectSide.Enemy, 1, "weak") });
        Assert.Equal("敵単体に[S:weak][N:1]を付与。", s);
    }

    [Fact]
    public void Debuff_vulnerable_all_enemies()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("debuff", EffectScope.All, EffectSide.Enemy, 2, "vulnerable") });
        Assert.Equal("敵全体に[S:vulnerable][N:2]を付与。", s);
    }

    [Fact]
    public void Buff_ally_all_dexterity()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("buff", EffectScope.All, EffectSide.Ally, 1, "dexterity") });
        Assert.Equal("味方全体に[S:dexterity][N:1]を付与。", s);
    }

    // --- Heal ---

    [Fact]
    public void Heal_self()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("heal", EffectScope.Self, null, 5) });
        Assert.Equal("自身にHPを[N:5]回復。", s);
    }

    [Fact]
    public void Heal_ally_single()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("heal", EffectScope.Single, EffectSide.Ally, 3) });
        Assert.Equal("味方単体にHPを[N:3]回復。", s);
    }

    // --- Summon ---

    [Fact]
    public void Summon_unit()
    {
        // Phase 10.5.M6.7: unit ID は marker [U:id]、"召喚" は keyword [K:summon]。
        var s = CardTextFormatter.FormatEffects(new[] { E("summon", EffectScope.Self, null, 0, name: null, unitId: "wisp") });
        Assert.Equal("[U:wisp]を[K:summon]。", s);
    }

    // --- Pile ops ---

    [Fact]
    public void ExhaustCard_default_choose()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("exhaustCard", EffectScope.Self, null, 1) });
        Assert.Equal("手札を[N:1]枚選んで除外。", s);
    }

    [Fact]
    public void ExhaustCard_random()
    {
        var e = new CardEffect("exhaustCard", EffectScope.Self, null, 2, Pile: "discard", Select: "random");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("捨札を[N:2]枚ランダムに除外。", s);
    }

    [Fact]
    public void ExhaustCard_all_ignores_amount()
    {
        var e = new CardEffect("exhaustCard", EffectScope.Self, null, 99, Pile: "draw", Select: "all");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("山札を全て除外。", s);
    }

    [Fact]
    public void ExhaustSelf_emits_exhaust_keyword_marker()
    {
        // Phase 10.5.M3: exhaustSelf は keyword 化された。後方互換のため [K:exhaust] を emit。
        var s = CardTextFormatter.FormatEffects(new[] { E("exhaustSelf", EffectScope.Self, null, 0) });
        Assert.Equal("[K:exhaust]。", s);
    }

    [Fact]
    public void RetainSelf_emits_wait_keyword_marker()
    {
        // Phase 10.5.M2: retainSelf は keyword 化されたが、後方互換のため [K:wait] を emit する。
        var s = CardTextFormatter.FormatEffects(new[] { E("retainSelf", EffectScope.Self, null, 0) });
        Assert.Equal("[K:wait]。", s);
    }

    [Fact]
    public void GainEnergy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("gainEnergy", EffectScope.Self, null, 1) });
        Assert.Equal("エナジー+[N:1]。", s);
    }

    [Fact]
    public void Upgrade_default_choose()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("upgrade", EffectScope.Self, null, 1) });
        Assert.Equal("手札を[N:1]枚選んで強化。", s);
    }

    [Fact]
    public void Upgrade_random_in_drawPile()
    {
        var e = new CardEffect("upgrade", EffectScope.Self, null, 2, Pile: "draw", Select: "random");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("山札を[N:2]枚ランダムに強化。", s);
    }

    [Fact]
    public void Upgrade_all_in_hand()
    {
        var e = new CardEffect("upgrade", EffectScope.Self, null, 99, Pile: "hand", Select: "all");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("手札を全て強化。", s);
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
        Assert.Equal("敵単体に[N:6]アタック。\n自身に[N:3]ブロック。", s);
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
        Assert.Equal("敵単体に[N:6]アタック。", CardTextFormatter.Format(def, upgraded: false));
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
        Assert.Equal("敵単体に[N:9]アタック。", CardTextFormatter.Format(def, upgraded: true));
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
        Assert.Equal("敵単体に[N:6]アタック。", CardTextFormatter.Format(def, upgraded: false));
    }

    // --- 10.5.B: Keyword lines ---

    [Fact]
    public void Keywords_render_as_separate_lines_at_top()
    {
        var def = MakeAttackDef(amount: 5, keywords: new[] { "wild" });
        var s = CardTextFormatter.Format(def, upgraded: false);
        Assert.Equal("[K:wild]\n敵単体に[N:5]アタック。", s);
    }

    [Fact]
    public void Multiple_keywords_join_with_slash_on_one_line()
    {
        // Phase 10.5.M3: 複数キーワードは "/" 区切りで 1 行にまとめる。
        var def = MakeAttackDef(amount: 5, keywords: new[] { "wild", "superwild" });
        var s = CardTextFormatter.Format(def, upgraded: false);
        Assert.Equal("[K:wild]/[K:superwild]\n敵単体に[N:5]アタック。", s);
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
        Assert.Equal("敵単体に[N:5]アタック。", s);
    }

    // --- 10.5.B: New action specs ---

    [Fact]
    public void SelfDamage_emits_jp_text()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("selfDamage", EffectScope.Self, null, 3) });
        Assert.Equal("自身に[N:3]ダメージ。", s);
    }

    [Fact]
    public void AddCard_to_hand()
    {
        var e = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "hand", CardRefId: "strike");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("[C:strike]を手札に[N:1]枚加える。", s);
    }

    [Fact]
    public void AddCard_to_drawpile()
    {
        var e = new CardEffect("addCard", EffectScope.Self, null, 2, Pile: "draw", CardRefId: "burn");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("[C:burn]を山札に[N:2]枚加える。", s);
    }

    [Fact]
    public void AddCard_to_discard()
    {
        var e = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "discard", CardRefId: "wound");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("[C:wound]を捨札に[N:1]枚加える。", s);
    }

    [Fact]
    public void RecoverFromDiscard_random_to_hand()
    {
        var e = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 2, Pile: "hand", Select: "random");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("捨札からランダムに[N:2]枚、手札に戻す。", s);
    }

    [Fact]
    public void RecoverFromDiscard_choose_to_exhaust()
    {
        var e = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 1, Pile: "exhaust", Select: "choose");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("捨札から選んで[N:1]枚、除外する。", s);
    }

    [Fact]
    public void GainMaxEnergy()
    {
        var s = CardTextFormatter.FormatEffects(new[] { E("gainMaxEnergy", EffectScope.Self, null, 1) });
        Assert.Equal("エナジー上限+[N:1]。", s);
    }

    // --- 10.5.B: Power trigger marker ---

    [Fact]
    public void Power_trigger_emits_marker_prefix()
    {
        var e = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("[T:OnTurnStart]の度に[N:1]枚引く。", s);
    }

    [Fact]
    public void Power_trigger_with_repeat_count_groups_correctly()
    {
        // 同一 Trigger + 同一 spec が連続したら × N 回
        var e = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart");
        var s = CardTextFormatter.FormatEffects(new[] { e, e });
        Assert.Equal("[T:OnTurnStart]の度に[N:1]枚引く×[N:2]。", s);
    }

    [Fact]
    public void Different_trigger_breaks_grouping()
    {
        var a = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart");
        var b = new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnPlayCard");
        var s = CardTextFormatter.FormatEffects(new[] { a, b });
        Assert.Equal("[T:OnTurnStart]の度に[N:1]枚引く。\n[T:OnPlayCard]の度に[N:1]枚引く。", s);
    }

    // --- 10.5.B: AmountSource (Variable X) marker ---

    [Fact]
    public void AmountSource_handCount_emits_X_marker()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0, AmountSource: "handCount");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("敵単体に[V:X|手札の数]アタック。", s);
    }

    [Fact]
    public void AmountSource_drawPileCount_block()
    {
        var e = new CardEffect("block", EffectScope.Self, null, 0, AmountSource: "drawPileCount");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("自身に[V:X|山札の数]ブロック。", s);
    }

    [Fact]
    public void AmountSource_unknown_passes_raw()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0, AmountSource: "weirdSrc");
        var s = CardTextFormatter.FormatEffects(new[] { e });
        Assert.Equal("敵単体に[V:X|weirdSrc]アタック。", s);
    }

    // --- 10.5.C: CardActorContext (up/down marker) ---

    [Fact]
    public void Attack_with_strength_emits_up_marker()
    {
        var def = MakeAttackDef(amount: 5);
        var ctx = new CardActorContext(Strength: 2, Weak: 0, Dexterity: 0);
        var s = CardTextFormatter.Format(def, upgraded: false, ctx);
        Assert.Equal("敵単体に[N:7|up]アタック。", s);
    }

    [Fact]
    public void Attack_with_weak_emits_down_marker()
    {
        var def = MakeAttackDef(amount: 5);
        var ctx = new CardActorContext(Strength: 0, Weak: 1, Dexterity: 0);
        // 5 * 0.75 = 3.75 → floor 3
        var s = CardTextFormatter.Format(def, upgraded: false, ctx);
        Assert.Equal("敵単体に[N:3|down]アタック。", s);
    }

    [Fact]
    public void Attack_unchanged_emits_no_modifier()
    {
        var def = MakeAttackDef(amount: 5);
        var s = CardTextFormatter.Format(def, upgraded: false, CardActorContext.Empty);
        Assert.Equal("敵単体に[N:5]アタック。", s);
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
        Assert.Equal("自身に[N:8|up]ブロック。", s);
    }

    [Fact]
    public void Strength_after_weak_uses_floor()
    {
        // (5 + 2) * 0.75 = 5.25 → 5。base 5 と等しいので無修飾。
        var def = MakeAttackDef(amount: 5);
        var ctx = new CardActorContext(Strength: 2, Weak: 1, Dexterity: 0);
        var s = CardTextFormatter.Format(def, upgraded: false, ctx);
        Assert.Equal("敵単体に[N:5]アタック。", s);
    }

    [Fact]
    public void Format_overload_without_context_keeps_existing_behavior()
    {
        // 既存 Format(def, upgraded) は無 context として動く (CardActorContext.Empty 経由)。
        var def = MakeAttackDef(amount: 5);
        var s = CardTextFormatter.Format(def, upgraded: false);
        Assert.Equal("敵単体に[N:5]アタック。", s);
    }

    [Fact]
    public void FormatEffects_context_overload_applies_adjustment()
    {
        var ctx = new CardActorContext(Strength: 3, Weak: 0, Dexterity: 0);
        var s = CardTextFormatter.FormatEffects(
            new[] { E("attack", EffectScope.Single, EffectSide.Enemy, 6) }, ctx);
        Assert.Equal("敵単体に[N:9|up]アタック。", s);
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
        Assert.Equal("敵単体に[N:6]アタック。\n自身に[N:7|up]ブロック。", s);
    }

    [Fact]
    public void AmountSource_with_context_keeps_variable_marker()
    {
        // Variable X を使う effect は context があっても [V:X|...] を維持 (10.5.D で別途扱う)。
        var ctx = new CardActorContext(Strength: 5, Weak: 0, Dexterity: 0);
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0, AmountSource: "handCount");
        var s = CardTextFormatter.FormatEffects(new[] { e }, ctx);
        Assert.Equal("敵単体に[V:X|手札の数]アタック。", s);
    }

    // --- Phase 10.6.B: Passive trigger action descriptions ---

    [Fact]
    public void FormatEffects_Passive_EnergyPerTurnBonus_RendersJapaneseText()
    {
        var effects = new[] {
            new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("エナジー最大値 +[N:1]", text);
    }

    [Fact]
    public void FormatEffects_Passive_CardsDrawnPerTurnBonus_RendersText()
    {
        var effects = new[] {
            new CardEffect("cardsDrawnPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("ターン開始時の手札枚数 +[N:1]", text);
    }

    [Fact]
    public void FormatEffects_Passive_GoldRewardMultiplier_Positive()
    {
        var effects = new[] {
            new CardEffect("goldRewardMultiplier", EffectScope.Self, null, 50, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("戦闘ゴールド報酬 +[N:50]%", text);
    }

    [Fact]
    public void FormatEffects_Passive_GoldRewardMultiplier_Negative()
    {
        var effects = new[] {
            new CardEffect("goldRewardMultiplier", EffectScope.Self, null, -20, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("戦闘ゴールド報酬 -[N:20]%", text);
    }

    [Fact]
    public void FormatEffects_Passive_ShopPriceMultiplier_Negative()
    {
        var effects = new[] {
            new CardEffect("shopPriceMultiplier", EffectScope.Self, null, -20, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("ショップ価格 -[N:20]%", text);
    }

    [Fact]
    public void FormatEffects_Passive_RewardCardChoicesBonus()
    {
        var effects = new[] {
            new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, 1, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("カード報酬選択肢 +[N:1] 枚", text);
    }

    [Fact]
    public void FormatEffects_Passive_RewardRerollAvailable()
    {
        var effects = new[] {
            new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("カード報酬を [N:1] 回リロール可能", text);
    }

    [Fact]
    public void FormatEffects_Passive_UnknownEnemyWeightDelta()
    {
        var effects = new[] {
            new CardEffect("unknownEnemyWeightDelta", EffectScope.Self, null, 5, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("ハテナマスの敵戦闘出現率 +[N:5]", text);
    }

    [Fact]
    public void FormatEffects_Passive_UnknownTreasureWeightDelta_Negative()
    {
        var effects = new[] {
            new CardEffect("unknownTreasureWeightDelta", EffectScope.Self, null, -3, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("ハテナマスの宝箱出現率 -[N:3]", text);
    }

    [Fact]
    public void FormatEffects_Passive_RestHealBonus()
    {
        var effects = new[] {
            new CardEffect("restHealBonus", EffectScope.Self, null, 5, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.Contains("休憩所での回復 +[N:5]", text);
    }

    [Fact]
    public void FormatEffects_Passive_NoTriggerPrefix()
    {
        // Passive trigger には trigger プレフィックスが付かない
        var effects = new[] {
            new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive")
        };
        var text = CardTextFormatter.FormatEffects(effects);
        Assert.DoesNotContain("バトル開始時", text);
    }
}
