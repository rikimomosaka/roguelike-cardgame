using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class BossRewardFlowTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void GenerateBossReward_NonFinalAct_ReturnsRewardWithIsBossRewardTrue(int act)
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentAct = act };
        var r = BossRewardFlow.GenerateBossReward(s, cat, new SystemRng(1));
        Assert.NotNull(r);
        Assert.True(r!.IsBossReward);
    }

    [Fact]
    public void GenerateBossReward_FinalAct_ReturnsNull()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentAct = RunConstants.MaxAct };
        var r = BossRewardFlow.GenerateBossReward(s, cat, new SystemRng(1));
        Assert.Null(r);
    }

    [Fact]
    public void Resolve_NonFinalAct_FiresOnRewardGeneratedRelicTrigger()
    {
        // Arrange: fake relic that grants 9 gold OnRewardGenerated
        var fake = BuildCatalogWithFakeRelic(
            id: "boss_lucky",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 9, Trigger: "OnRewardGenerated") });
        var s0 = TestRunStates.FreshDefault(fake) with
        {
            Gold = 50,
            CurrentAct = 1,
            Relics = new List<string> { "boss_lucky" },
        };

        // Act
        var s1 = BossRewardFlow.Resolve(s0, fake, new SystemRng(1));

        // Assert: ActiveReward is set AND gold increased by 9
        Assert.NotNull(s1.ActiveReward);
        Assert.True(s1.ActiveReward!.IsBossReward);
        Assert.Equal(59, s1.Gold);
    }

    private static DataCatalog BuildCatalogWithFakeRelic(
        string id,
        IReadOnlyList<CardEffect> effects,
        bool implemented = true)
    {
        var fake = new RelicDefinition(
            Id: id,
            Name: $"fake_{id}",
            Rarity: CardRarity.Common,
            Effects: effects,
            Description: "",
            Implemented: implemented);

        var orig = EmbeddedDataLoader.LoadCatalog();
        var relics = orig.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return orig with { Relics = relics };
    }
}
