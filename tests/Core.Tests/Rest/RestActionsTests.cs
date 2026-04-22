using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Rest;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rest;

public class RestActionsTests
{
    private static DataCatalog Catalog() => EmbeddedDataLoader.LoadCatalog();

    private static RunState PendingRunAt(int currentHp, int maxHp,
        ImmutableArray<CardInstance>? deck = null,
        ImmutableArray<string>? relics = null)
    {
        var catalog = Catalog();
        var s = RunState.NewSoloRun(
            catalog,
            rngSeed: 1,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero));
        return s with
        {
            CurrentHp = currentHp,
            MaxHp = maxHp,
            ActiveRestPending = true,
            Deck = deck ?? s.Deck,
            Relics = relics?.ToArray() ?? Array.Empty<string>(),
        };
    }

    [Fact]
    public void Heal_HealsCeilThirtyPercent_AndClearsPending()
    {
        var s = PendingRunAt(currentHp: 30, maxHp: 80);
        var s1 = RestActions.Heal(s, Catalog());
        // ceil(80 * 0.30) = ceil(24) = 24
        Assert.Equal(30 + 24, s1.CurrentHp);
        Assert.False(s1.ActiveRestPending);
    }

    [Fact]
    public void Heal_CapsAtMaxHp()
    {
        var s = PendingRunAt(currentHp: 70, maxHp: 80);
        var s1 = RestActions.Heal(s, Catalog());
        Assert.Equal(80, s1.CurrentHp);
        Assert.False(s1.ActiveRestPending);
    }

    [Fact]
    public void Heal_WithWarmBlanket_AddsPassiveBonus()
    {
        var s = PendingRunAt(currentHp: 30, maxHp: 80,
            relics: ImmutableArray.Create("warm_blanket"));
        var s1 = RestActions.Heal(s, Catalog());
        // ceil(80 * 0.30) = 24, + 10 = 34
        Assert.Equal(30 + 34, s1.CurrentHp);
    }

    [Fact]
    public void Heal_WithoutPending_Throws()
    {
        var s = PendingRunAt(20, 80) with { ActiveRestPending = false };
        Assert.Throws<InvalidOperationException>(() => RestActions.Heal(s, Catalog()));
    }

    [Fact]
    public void UpgradeCard_UpgradesDeckIndex_AndClearsPending()
    {
        var catalog = Catalog();
        // 強化可能なカード (例: "strike") を 1 枚持つデッキを作る
        var deck = ImmutableArray.Create(
            new CardInstance("strike", Upgraded: false),
            new CardInstance("defend", Upgraded: false));
        var s = PendingRunAt(80, 80, deck: deck);
        var s1 = RestActions.UpgradeCard(s, deckIndex: 0, catalog);
        Assert.True(s1.Deck[0].Upgraded);
        Assert.False(s1.Deck[1].Upgraded);
        Assert.False(s1.ActiveRestPending);
    }

    [Fact]
    public void UpgradeCard_AlreadyUpgraded_Throws()
    {
        var deck = ImmutableArray.Create(new CardInstance("strike", Upgraded: true));
        var s = PendingRunAt(80, 80, deck: deck);
        Assert.Throws<InvalidOperationException>(() =>
            RestActions.UpgradeCard(s, 0, Catalog()));
    }

    [Fact]
    public void UpgradeCard_IndexOutOfRange_Throws()
    {
        var deck = ImmutableArray.Create(new CardInstance("strike", Upgraded: false));
        var s = PendingRunAt(80, 80, deck: deck);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RestActions.UpgradeCard(s, 5, Catalog()));
    }

    [Fact]
    public void UpgradeCard_WithoutPending_Throws()
    {
        var deck = ImmutableArray.Create(new CardInstance("strike", Upgraded: false));
        var s = PendingRunAt(80, 80, deck: deck) with { ActiveRestPending = false };
        Assert.Throws<InvalidOperationException>(() =>
            RestActions.UpgradeCard(s, 0, Catalog()));
    }
}
