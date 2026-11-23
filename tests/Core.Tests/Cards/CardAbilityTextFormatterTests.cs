using System;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public sealed class CardAbilityTextFormatterTests
{
    [Fact]
    public void Momentum_formats_as_a_parameterized_effect_condition()
    {
        var card = Card(
            effects: new[]
            {
                new CardEffect(
                    "attack", EffectScope.Single, EffectSide.Enemy, 11,
                    MaxComboMin: 3),
            });

        Assert.Equal(
            "[K:momentum][N:3]：敵単体に[N:11]アタック。",
            CardTextFormatter.Format(card, upgraded: false));
    }

    [Fact]
    public void Finisher_is_rendered_in_the_priority_keyword_line_before_effects()
    {
        var card = Card(
            effects: new[]
            {
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 12),
            },
            abilities: new CardAbilityDefinition[] { new FinisherAbilityDefinition() });

        Assert.Equal(
            "[K:finisher]\n敵単体に[N:12]アタック。",
            CardTextFormatter.Format(card, upgraded: false));
    }

    [Theory]
    [MemberData(nameof(RepriseCases))]
    public void Reprise_filter_has_canonical_compact_text(
        RepriseCardFilter filter,
        string expected)
    {
        var card = Card(
            effects: Array.Empty<CardEffect>(),
            abilities: new CardAbilityDefinition[] { new RepriseAbilityDefinition(filter) });

        Assert.Equal(expected, CardTextFormatter.Format(card, upgraded: false));
    }

    public static TheoryData<RepriseCardFilter, string> RepriseCases => new()
    {
        {
            new RepriseCardFilter(
                CardTypes: new[] { CardType.Skill },
                MaxOriginalCost: 3),
            "[K:reprise]：コスト[N:3]以下のスキル。"
        },
        {
            new RepriseCardFilter(CardTypes: new[] { CardType.Attack }),
            "[K:reprise]：アタック。"
        },
        {
            new RepriseCardFilter(MinOriginalCost: 1, MaxOriginalCost: 1),
            "[K:reprise]：コスト[N:1]のカード。"
        },
        {
            new RepriseCardFilter(
                CardTypes: new[] { CardType.Attack, CardType.Skill }),
            "[K:reprise]：アタック・スキル。"
        },
        {
            new RepriseCardFilter(
                CardTypes: new[] { CardType.Attack, CardType.Skill },
                MinOriginalCost: 2),
            "[K:reprise]：コスト[N:2]以上のアタック・スキル。"
        },
        {
            new RepriseCardFilter(
                MinOriginalCost: 1,
                MaxOriginalCost: 3),
            "[K:reprise]：コスト[N:1]～[N:3]のカード。"
        },
        {
            new RepriseCardFilter(
                CardTypes: new[] { CardType.Skill },
                MaxOriginalCost: 3,
                MinPaidCost: 0,
                MaxPaidCost: 1),
            "[K:reprise]：コスト[N:3]以下のスキルかつ支払コスト[N:0]～[N:1]のカード。"
        },
    };

    [Theory]
    [InlineData(ContingencyTriggerTiming.TurnStart, null, "[K:contingency]：ターン開始時。")]
    [InlineData(ContingencyTriggerTiming.BeforeEnemyAttack, null, "[K:contingency]：相手のアタック時。")]
    [InlineData(ContingencyTriggerTiming.ComboReached, 3, "[K:contingency]：[N:3]コンボ達成時。")]
    public void Contingency_is_rendered_before_its_delayed_effects(
        ContingencyTriggerTiming timing,
        int? threshold,
        string expectedHeader)
    {
        var card = Card(
            effects: new[] { new CardEffect("block", EffectScope.Self, null, 5) },
            abilities: new CardAbilityDefinition[]
            {
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(timing, threshold)),
            });

        Assert.Equal(
            $"{expectedHeader}\n自身に[N:5]ブロック。",
            CardTextFormatter.Format(card, upgraded: false));
    }

    [Fact]
    public void Typed_ability_is_not_hidden_by_a_manual_description()
    {
        var card = Card(
            effects: Array.Empty<CardEffect>(),
            abilities: new CardAbilityDefinition[] { new FinisherAbilityDefinition() }) with
        {
            Description = "手書き効果。",
        };

        Assert.Equal(
            "[K:finisher]\n手書き効果。",
            CardTextFormatter.Format(card, upgraded: false));
    }

    [Fact]
    public void Card_sections_follow_the_canonical_display_order()
    {
        var card = Card(
            effects: new[]
            {
                // 入力はあえて乗勢を先にし、表示順が JSON 順に依存しないことも固定する。
                new CardEffect(
                    "attack", EffectScope.Single, EffectSide.Enemy, 9,
                    MaxComboMin: 4),
                new CardEffect("block", EffectScope.Self, null, 5),
            },
            abilities: new CardAbilityDefinition[]
            {
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.BeforeEnemyAttack)),
                new FinisherAbilityDefinition(),
            },
            keywords: new[] { "exhaust", "wild" },
            usesPerTurn: 1);

        Assert.Equal(
            """
            ターン中1度だけ使用可能(0/1)。
            [K:wild]/[K:finisher]/[K:exhaust]
            [K:contingency]：相手のアタック時。
            自身に[N:5]ブロック。
            [K:momentum][N:4]：敵単体に[N:9]アタック。
            """,
            CardTextFormatter.Format(card, upgraded: false));
    }

    [Fact]
    public void Upgraded_abilities_are_independent_from_normal_abilities()
    {
        var card = Card(
            effects: Array.Empty<CardEffect>(),
            abilities: new CardAbilityDefinition[] { new FinisherAbilityDefinition() }) with
        {
            UpgradedCost = 0,
            UpgradedAbilities = Array.Empty<CardAbilityDefinition>(),
        };

        Assert.Equal("[K:finisher]", CardTextFormatter.Format(card, upgraded: false));
        Assert.Equal(string.Empty, CardTextFormatter.Format(card, upgraded: true));
    }

    [Theory]
    [InlineData("momentum", "乗勢", "効果の実行時、自身のこのターンの最大コンボがX以上であれば適用される。")]
    [InlineData("finisher", "終撃", "効果の適用後、このカードを含むこのコンボ中の全てのアタックカードを残光としてこのターン中保存し、現在コンボを0にする。")]
    [InlineData("reprise", "再演", "再演条件を満たし、この戦闘中に最後に効果の解決を完了した自身のカード1枚の効果を再実行する。")]
    [InlineData("contingency", "伏策", "使用時は効果が実行されず、発動待ちとして除外する。伏策条件を満たした時に効果を実行し、捨札へ移動する。")]
    public void Ability_keyword_catalog_uses_the_confirmed_text(
        string id,
        string name,
        string description)
    {
        var metadata = Assert.IsType<CardKeywords.KeywordMeta>(CardKeywords.Get(id));

        Assert.Equal(name, metadata.Name);
        Assert.Equal(description, metadata.Description);
    }

    private static CardDefinition Card(
        CardEffect[] effects,
        CardAbilityDefinition[]? abilities = null,
        string[]? keywords = null,
        int usesPerTurn = 0) =>
        new(
            Id: "ability_card",
            Name: "Ability card",
            DisplayName: null,
            Rarity: CardRarity.Rare,
            CardType: CardType.Skill,
            Cost: 1,
            UpgradedCost: null,
            Effects: effects,
            UpgradedEffects: null,
            Keywords: keywords,
            UsesPerTurn: usesPerTurn,
            Abilities: abilities);
}
