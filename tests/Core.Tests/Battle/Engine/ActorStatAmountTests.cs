using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// 攻撃力・守備力（筋力・敏捷とは別の能力値）と amountScalePercent（倍率）。
/// docs/current-enemy-card-ai-spec.md §8。
/// </summary>
public class ActorStatAmountTests
{
    private static IRng Rng() => new FakeRng(new int[50], new double[0]);

    [Theory]
    [InlineData(6, null, 6)]
    [InlineData(6, 150, 9)]
    [InlineData(5, 160, 8)]
    [InlineData(7, 150, 10)] // 10.5 → 切り捨て
    [InlineData(0, 150, 0)]
    public void Scale_floors_the_percentage(int value, int? percent, int expected)
        => Assert.Equal(expected, AmountSourceEvaluator.Scale(value, percent));

    [Fact]
    public void Parser_reads_scale_and_requires_amount_source()
    {
        var effect = CardEffectParser.ParseEffect(System.Text.Json.JsonDocument.Parse(
            """{"action":"attack","scope":"all","side":"enemy","amount":0,"amountSource":"selfAttack","amountScalePercent":150}""")
            .RootElement, message => new System.InvalidOperationException(message));
        Assert.Equal(150, effect.AmountScalePercent);

        Assert.ThrowsAny<System.Exception>(() => CardEffectParser.ParseEffect(System.Text.Json.JsonDocument.Parse(
            """{"action":"attack","scope":"all","side":"enemy","amount":3,"amountScalePercent":150}""")
            .RootElement, message => new System.InvalidOperationException(message)));
    }

    [Fact]
    public void Heavy_attack_card_queues_attack_power_times_one_and_half()
    {
        var heavy = new CardDefinition("heavy", "強攻撃", null, CardRarity.Token, CardType.Attack,
            Cost: 2, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 0,
                    AmountSource: "selfAttack", AmountScalePercent: 150),
            },
            UpgradedEffects: null, Keywords: null);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { heavy });
        var hero = BattleFixtures.Hero() with { AttackPower = 6 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            hand: ImmutableArray.Create(BattleFixtures.MakeBattleCard("heavy", "h1")));

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, null, Rng(), catalog);

        Assert.Equal(9, after.Allies[0].AttackAll.Sum);
        Assert.Equal(1, after.Energy);
    }

    [Fact]
    public void Card_text_shows_concrete_stat_value_when_actor_is_known()
    {
        var effect = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0,
            AmountSource: "selfAttack", AmountScalePercent: 150);
        var card = new CardDefinition("c", "c", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null, Effects: new[] { effect },
            UpgradedEffects: null, Keywords: null);

        var unknown = CardTextFormatter.Format(card, upgraded: false);
        var known = CardTextFormatter.Format(card, upgraded: false,
            new CardActorContext(Strength: 2, Weak: 0, Dexterity: 0) { AttackPower = 6 });

        Assert.Contains("[V:X|自身の攻撃力×1.5]", unknown);
        // 値（floor(6 * 1.5) + 筋力2）と、ポップアップで示す参照元ラベルを両方持つ。
        Assert.Contains("[R:11|up:自身の攻撃力×1.5]", known);

        var noModifier = CardTextFormatter.Format(card, upgraded: false,
            new CardActorContext(Strength: 0, Weak: 0, Dexterity: 0) { AttackPower = 6 });
        Assert.Contains("[R:9|:自身の攻撃力×1.5]", noModifier);
    }

    [Fact]
    public void Embedded_characters_and_starter_cards_use_attack_and_defense()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        foreach (var character in catalog.Characters.Values)
        {
            Assert.InRange(character.Attack, 4, 7);
            Assert.InRange(character.Defense, 4, 7);
        }
        Assert.Equal("selfAttack", catalog.Cards["basic_attack"].Effects[0].AmountSource);
        Assert.Equal("selfDefense", catalog.Cards["basic_defense"].Effects[0].AmountSource);
        Assert.Equal(150, catalog.Cards["enemy_common_heavy_attack"].Effects[0].AmountScalePercent);
        Assert.Equal(2, catalog.Cards["enemy_common_heavy_attack"].Cost);
    }
}
