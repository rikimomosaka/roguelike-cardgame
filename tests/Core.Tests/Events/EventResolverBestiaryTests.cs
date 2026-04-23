using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventResolverBestiaryTests
{
    private static readonly DataCatalog Cat = EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void GainRelicRandom_TracksRelic()
    {
        var rng = new SequentialRng(42UL);
        var s = TestRunStates.FreshDefault(Cat) with
        {
            ActiveEvent = new EventInstance(
                EventId: "evt",
                Choices: ImmutableArray.Create(new EventChoice(
                    Label: "c",
                    Condition: null,
                    Effects: ImmutableArray.Create<EventEffect>(
                        new EventEffect.GainRelicRandom(CardRarity.Common)))),
                ChosenIndex: null),
        };
        var after = EventResolver.ApplyChoice(s, 0, Cat, rng);
        Assert.NotEmpty(after.AcquiredRelicIds);
        Assert.Contains(after.AcquiredRelicIds[0], after.Relics);
    }

    [Fact]
    public void GrantCardReward_TracksCardChoices()
    {
        var rng = new SequentialRng(7UL);
        var s = TestRunStates.FreshDefault(Cat) with
        {
            ActiveEvent = new EventInstance(
                EventId: "evt",
                Choices: ImmutableArray.Create(new EventChoice(
                    Label: "c",
                    Condition: null,
                    Effects: ImmutableArray.Create<EventEffect>(new EventEffect.GrantCardReward()))),
                ChosenIndex: null),
        };
        var after = EventResolver.ApplyChoice(s, 0, Cat, rng);
        Assert.NotNull(after.ActiveReward);
        foreach (var cardId in after.ActiveReward!.CardChoices)
            Assert.Contains(cardId, after.SeenCardBaseIds);
    }
}
