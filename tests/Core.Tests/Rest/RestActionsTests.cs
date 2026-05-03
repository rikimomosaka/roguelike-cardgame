using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rest;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rest;

public class RestActionsTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static DataCatalog Catalog() => BaseCatalog;

    /// <summary>
    /// フェイクレリックを注入した DataCatalog を返すローカルヘルパ (T9 で TestHelpers/ に集約予定)。
    /// </summary>
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

        var relics = BaseCatalog.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return BaseCatalog with { Relics = relics };
    }

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
    public void Heal_HealsCeilThirtyPercent_AndSetsCompleted()
    {
        var s = PendingRunAt(currentHp: 30, maxHp: 80);
        var s1 = RestActions.Heal(s, Catalog());
        // ceil(80 * 0.30) = ceil(24) = 24
        Assert.Equal(30 + 24, s1.CurrentHp);
        Assert.True(s1.ActiveRestPending);   // Pending は残る
        Assert.True(s1.ActiveRestCompleted); // Completed が立つ
    }

    [Fact]
    public void Heal_CapsAtMaxHp()
    {
        var s = PendingRunAt(currentHp: 70, maxHp: 80);
        var s1 = RestActions.Heal(s, Catalog());
        Assert.Equal(80, s1.CurrentHp);
        Assert.True(s1.ActiveRestPending);
        Assert.True(s1.ActiveRestCompleted);
    }

    [Fact]
    public void Heal_TwiceThrows()
    {
        var s = PendingRunAt(currentHp: 30, maxHp: 80);
        var s1 = RestActions.Heal(s, Catalog());
        Assert.Throws<InvalidOperationException>(() => RestActions.Heal(s1, Catalog()));
    }

    [Fact]
    public void Heal_ThenUpgrade_Throws()
    {
        var deck = ImmutableArray.Create(new CardInstance("strike", Upgraded: false));
        var s = PendingRunAt(30, 80, deck: deck);
        var s1 = RestActions.Heal(s, Catalog());
        Assert.Throws<InvalidOperationException>(() =>
            RestActions.UpgradeCard(s1, 0, Catalog()));
    }

    [Fact]
    public void Heal_WithWarmBlanket_BaseHealOnly()
    {
        // Phase 10.5.L1.5: warm_blanket の base effects=[] (リセット済み)。
        // Passive RestHealBonus が発火しないことを検証 (= base heal だけ)。
        // ceil(80 * 0.30) = 24
        var s = PendingRunAt(currentHp: 30, maxHp: 80,
            relics: ImmutableArray.Create("warm_blanket"));
        var s1 = RestActions.Heal(s, Catalog());
        Assert.Equal(30 + 24, s1.CurrentHp);
    }

    [Fact]
    public void Heal_WithoutPending_Throws()
    {
        var s = PendingRunAt(20, 80) with { ActiveRestPending = false };
        Assert.Throws<InvalidOperationException>(() => RestActions.Heal(s, Catalog()));
    }

    [Fact]
    public void UpgradeCard_UpgradesDeckIndex_AndSetsCompleted()
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
        Assert.True(s1.ActiveRestPending);   // Pending は残る
        Assert.True(s1.ActiveRestCompleted); // Completed が立つ
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

    // Phase 10.6.A Task 4: OnRest は Heal 専用、UpgradeCard では発火しない

    [Fact]
    public void Heal_WithOnRestRelic_FiresGainMaxHpAfterHealing()
    {
        // base 30% of 80 = ceil(24) = 24 heal → 50+24=74. Then OnRest fires +1/+1 → 75/81.
        var fake = BuildCatalogWithFakeRelic(
            id: "rest_grower",
            effects: new[] { new CardEffect(
                "gainMaxHp", EffectScope.Self, null, 1, Trigger: "OnRest") });
        var s0 = PendingRunAt(currentHp: 50, maxHp: 80,
            relics: ImmutableArray.Create("rest_grower")) with { };
        // PendingRunAt uses fake catalog; switch to fake catalog by reconstructing
        var s0f = s0; // state is correct; catalog passed to Heal is the fake one

        var s1 = RestActions.Heal(s0f, fake);

        Assert.True(s1.ActiveRestCompleted);
        Assert.Equal(81, s1.MaxHp);
        Assert.Equal(75, s1.CurrentHp);
    }

    [Fact]
    public void UpgradeCard_DoesNotFireOnRestTrigger()
    {
        // OnRest は Heal 専用。UpgradeCard で発火しないことを確認。
        var fake = BuildCatalogWithFakeRelic(
            id: "rest_grower_upgrade",
            effects: new[] { new CardEffect(
                "gainMaxHp", EffectScope.Self, null, 99, Trigger: "OnRest") });
        var deck = ImmutableArray.Create(new CardInstance("strike", Upgraded: false));
        var s0 = PendingRunAt(80, 80, deck: deck,
            relics: ImmutableArray.Create("rest_grower_upgrade"));
        int origMaxHp = s0.MaxHp;

        var s1 = RestActions.UpgradeCard(s0, deckIndex: 0, fake);

        Assert.True(s1.ActiveRestCompleted);
        Assert.Equal(origMaxHp, s1.MaxHp); // OnRest は Heal 専用、UpgradeCard では発火しない
    }
}
