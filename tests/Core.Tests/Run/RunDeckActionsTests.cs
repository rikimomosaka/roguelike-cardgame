using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunDeckActionsTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Sample() =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 5, 3, 0, 0, 0, System.TimeSpan.Zero));

    // BuildCatalogWithFakeRelic helper をローカルコピー (NonBattleRelicEffectsTests.cs と同じパターン)
    // T9 で TestHelpers/ に集約予定
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

        var orig = BaseCatalog;
        var relics = orig.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return orig with { Relics = relics };
    }

    [Fact]
    public void AddCardToDeck_AppendsCardInstance()
    {
        var s0 = Sample();
        int origDeckLen = s0.Deck.Length;

        var s1 = RunDeckActions.AddCardToDeck(s0, "strike", BaseCatalog);

        Assert.Equal(origDeckLen + 1, s1.Deck.Length);
        Assert.Equal("strike", s1.Deck[^1].Id);
        Assert.False(s1.Deck[^1].Upgraded);
    }

    [Fact]
    public void AddCardToDeck_FiresOnCardAddedToDeckTrigger()
    {
        var fake = BuildCatalogWithFakeRelic(
            id: "card_collector",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 5, Trigger: "OnCardAddedToDeck") });
        var s0 = Sample() with {
            Gold = 10,
            Relics = new List<string> { "card_collector" }
        };

        var s1 = RunDeckActions.AddCardToDeck(s0, "strike", fake);

        Assert.Equal(15, s1.Gold);
    }

    [Fact]
    public void AddCardToDeck_UnknownCardId_Throws()
    {
        var s0 = Sample();
        Assert.Throws<System.ArgumentException>(() =>
            RunDeckActions.AddCardToDeck(s0, "no_such_card", BaseCatalog));
    }

    [Fact]
    public void AddCardToDeck_NullState_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            RunDeckActions.AddCardToDeck(null!, "strike", BaseCatalog));
    }

    [Fact]
    public void AddCardToDeck_NullCardId_Throws()
    {
        var s0 = Sample();
        Assert.Throws<System.ArgumentNullException>(() =>
            RunDeckActions.AddCardToDeck(s0, null!, BaseCatalog));
    }

    [Fact]
    public void AddCardToDeck_NullCatalog_Throws()
    {
        var s0 = Sample();
        Assert.Throws<System.ArgumentNullException>(() =>
            RunDeckActions.AddCardToDeck(s0, "strike", null!));
    }
}
