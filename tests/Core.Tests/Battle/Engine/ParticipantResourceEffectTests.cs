using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class ParticipantResourceEffectTests
{
    [Fact]
    public void Cpu_self_draw_uses_its_own_confusion_and_unit_hand_limit()
    {
        var setup = CreateState(
            cpuHand: [Card("held")],
            cpuDraw: [Card("d1"), Card("d2")],
            cpuStatuses: ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Confusion, 1),
            handLimit: 2);
        var projected = BattleParticipantAccess.ProjectToRoot(
            setup.State, setup.CpuParticipantId);

        var (afterEffect, events) = EffectApplier.Apply(
            projected,
            setup.Wisp,
            new CardEffect("draw", EffectScope.Self, null, 3),
            new FakeRng([3], []),
            setup.Catalog);
        var result = BattleParticipantAccess.CommitRootToParticipant(
            afterEffect, setup.CpuParticipantId);
        var cpu = Assert.Single(
            result.Participants,
            participant => participant.ParticipantId == setup.CpuParticipantId);

        Assert.Equal(["held", "d1"], cpu.Hand.Select(card => card.InstanceId));
        Assert.Equal("d2", Assert.Single(cpu.DrawPile).InstanceId);
        Assert.Equal(3, cpu.Hand[1].CostOverride);
        Assert.Empty(cpu.Hand[1].EffectiveCostModifiers);
        Assert.Equal(1, Assert.Single(events).Amount);
        Assert.Empty(result.Hand);
    }

    [Fact]
    public void Cpu_add_card_to_hand_uses_its_limit_and_confusion_owner()
    {
        var setup = CreateState(
            cpuHand: [Card("held")],
            cpuDraw: [],
            cpuStatuses: ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Confusion, 1),
            handLimit: 2);
        var projected = BattleParticipantAccess.ProjectToRoot(
            setup.State, setup.CpuParticipantId);

        var (afterEffect, _) = EffectApplier.Apply(
            projected,
            setup.Wisp,
            new CardEffect(
                "addCard", EffectScope.Self, null, 2,
                Pile: "hand", CardRefId: "strike"),
            new FakeRng([2], []),
            setup.Catalog);
        var result = BattleParticipantAccess.CommitRootToParticipant(
            afterEffect, setup.CpuParticipantId);
        var cpu = Assert.Single(
            result.Participants,
            participant => participant.ParticipantId == setup.CpuParticipantId);

        Assert.Equal(2, cpu.Hand.Length);
        Assert.Single(cpu.DiscardPile);
        Assert.Equal(2, cpu.Hand[1].CostOverride);
    }

    [Fact]
    public void Ally_resource_effects_mutate_only_the_target_participant()
    {
        var setup = CreateState(
            cpuHand: [Card("cpu-hand")],
            cpuDraw: [Card("cpu-draw")],
            cpuStatuses: ImmutableDictionary<string, int>.Empty,
            handLimit: 3);
        var state = setup.State with { TargetAllyIndex = 1 };
        state = BattleParticipantAccess.Replace(
            state,
            state.Participants[0] with { TargetAllyIndex = 1 });
        int primaryEnergy = state.Energy;

        (state, _) = EffectApplier.Apply(
            state,
            setup.Hero,
            new CardEffect(
                "draw", EffectScope.Single, EffectSide.Ally, 1),
            new FakeRng([], []),
            setup.Catalog);
        (state, _) = EffectApplier.Apply(
            state,
            setup.Hero,
            new CardEffect(
                "gainEnergy", EffectScope.Single, EffectSide.Ally, 2),
            new FakeRng([], []),
            setup.Catalog);
        (state, _) = EffectApplier.Apply(
            state,
            setup.Hero,
            new CardEffect(
                "modifyCardCost", EffectScope.Single, EffectSide.Ally, -1,
                Pile: "hand", Select: "all", CostDuration: "untilPlayed"),
            new FakeRng([], []),
            setup.Catalog);

        var cpu = Assert.Single(
            state.Participants,
            participant => participant.ParticipantId == setup.CpuParticipantId);
        Assert.Equal(["cpu-hand", "cpu-draw"],
            cpu.Hand.Select(card => card.InstanceId));
        Assert.Equal(2, cpu.Energy);
        Assert.All(cpu.Hand, card =>
            Assert.Equal(-1, Assert.Single(card.EffectiveCostModifiers).Amount));
        Assert.Equal(primaryEnergy, state.Energy);
        Assert.Empty(state.Hand);
    }

    [Theory]
    [InlineData("draw")]
    [InlineData("gainEnergy")]
    [InlineData("gainMaxEnergy")]
    [InlineData("modifyCardCost")]
    public void All_resource_effects_target_only_living_participant_actors(
        string action)
    {
        var setup = CreateState(
            cpuHand: [Card("cpu-hand")],
            cpuDraw: [Card("cpu-draw")],
            cpuStatuses: ImmutableDictionary<string, int>.Empty,
            handLimit: 3,
            heroHand: [Card("hero-hand")],
            heroDraw: [Card("hero-draw")],
            includeLegacySummon: true);

        var (result, _) = EffectApplier.Apply(
            setup.State,
            setup.Hero,
            ResourceEffect(action, EffectScope.All),
            new FakeRng([], []),
            setup.Catalog);

        AssertResourceChange(action, result, setup, heroChanged: true);
    }

    [Theory]
    [InlineData("draw")]
    [InlineData("gainEnergy")]
    [InlineData("gainMaxEnergy")]
    [InlineData("modifyCardCost")]
    public void Random_resource_effects_never_offer_a_legacy_summon(
        string action)
    {
        var setup = CreateState(
            cpuHand: [Card("cpu-hand")],
            cpuDraw: [Card("cpu-draw")],
            cpuStatuses: ImmutableDictionary<string, int>.Empty,
            handLimit: 3,
            heroHand: [Card("hero-hand")],
            heroDraw: [Card("hero-draw")],
            includeLegacySummon: true);
        var rng = new LastCandidateRng();

        var (result, _) = EffectApplier.Apply(
            setup.State,
            setup.Hero,
            ResourceEffect(action, EffectScope.Random),
            rng,
            setup.Catalog);

        Assert.Equal((0, 2), Assert.Single(rng.IntRanges));
        AssertResourceChange(action, result, setup, heroChanged: false);
    }

    [Theory]
    [InlineData("draw")]
    [InlineData("gainEnergy")]
    [InlineData("gainMaxEnergy")]
    [InlineData("modifyCardCost")]
    public void Single_resource_effect_explicitly_rejects_a_legacy_summon(
        string action)
    {
        var setup = CreateState(
            cpuHand: [Card("cpu-hand")],
            cpuDraw: [Card("cpu-draw")],
            cpuStatuses: ImmutableDictionary<string, int>.Empty,
            handLimit: 3,
            heroHand: [Card("hero-hand")],
            heroDraw: [Card("hero-draw")],
            includeLegacySummon: true);
        var state = setup.State with { TargetAllyIndex = 2 };
        state = BattleParticipantAccess.Replace(
            state,
            state.Participants[0] with { TargetAllyIndex = 2 });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EffectApplier.Apply(
                state,
                setup.Hero,
                ResourceEffect(action, EffectScope.Single),
                new FakeRng([], []),
                setup.Catalog));

        Assert.Contains("legacy-summon", exception.Message);
        Assert.Contains("does not own card resources", exception.Message);
    }

    private static CardEffect ResourceEffect(string action, EffectScope scope) =>
        action == "modifyCardCost"
            ? new CardEffect(
                action, scope, EffectSide.Ally, -1,
                Pile: "hand", Select: "all", CostDuration: "untilPlayed")
            : new CardEffect(action, scope, EffectSide.Ally, 1);

    private static void AssertResourceChange(
        string action,
        BattleState result,
        Setup setup,
        bool heroChanged)
    {
        var cpu = Assert.Single(
            result.Participants,
            participant => participant.ParticipantId == setup.CpuParticipantId);
        switch (action)
        {
            case "draw":
                Assert.Equal(
                    heroChanged ? ["hero-hand", "hero-draw"] : ["hero-hand"],
                    result.Hand.Select(card => card.InstanceId));
                Assert.Equal(
                    ["cpu-hand", "cpu-draw"],
                    cpu.Hand.Select(card => card.InstanceId));
                break;
            case "gainEnergy":
                Assert.Equal(heroChanged ? 4 : 3, result.Energy);
                Assert.Equal(1, cpu.Energy);
                break;
            case "gainMaxEnergy":
                Assert.Equal(heroChanged ? 4 : 3, result.EnergyMax);
                Assert.Equal(2, cpu.EnergyPerTurn);
                break;
            case "modifyCardCost":
                Assert.Equal(
                    heroChanged ? -1 : 0,
                    result.Hand[0].EffectiveCostModifiers.Sum(modifier => modifier.Amount));
                Assert.Equal(
                    -1,
                    cpu.Hand[0].EffectiveCostModifiers.Sum(modifier => modifier.Amount));
                break;
            default:
                throw new InvalidOperationException($"Unexpected action '{action}'");
        }

        Assert.Equal(3, result.Allies.Length);
        Assert.Contains(result.Allies, actor => actor.InstanceId == "legacy-summon");
        Assert.DoesNotContain(
            result.Participants,
            participant => participant.ActorInstanceId == "legacy-summon");
    }

    private static Setup CreateState(
        ImmutableArray<BattleCardInstance> cpuHand,
        ImmutableArray<BattleCardInstance> cpuDraw,
        ImmutableDictionary<string, int> cpuStatuses,
        int handLimit,
        ImmutableArray<BattleCardInstance>? heroHand = null,
        ImmutableArray<BattleCardInstance>? heroDraw = null,
        bool includeLegacySummon = false)
    {
        const string cpuParticipantId = "cpu:wisp";
        var hero = BattleFixtures.Hero();
        var wisp = BattleFixtures.SummonActor("wisp", "wisp", 1, hp: 30) with
        {
            Statuses = cpuStatuses,
        };
        var allies = ImmutableArray.Create(hero, wisp);
        if (includeLegacySummon)
        {
            allies = allies.Add(BattleFixtures.SummonActor(
                "legacy-summon", "legacy", 2, hp: 20, moveId: "wait"));
        }
        var state = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                allies: allies,
                hand: heroHand ?? [],
                draw: heroDraw ?? [],
                energy: 3));
        var cpu = state.Participants[0] with
        {
            ParticipantId = cpuParticipantId,
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            CharacterId = "wisp",
            TargetAllyIndex = 1,
            Energy = 0,
            EnergyPerTurn = 1,
            DrawPerTurn = 1,
            Hand = cpuHand,
            DrawPile = cpuDraw,
            DiscardPile = [],
            ExhaustPile = [],
            SummonHeld = [],
            PowerCards = [],
            OwnedRelicIds = [],
            Potions = [],
        };
        state = BattleParticipantAccess.Add(state, cpu);
        var unit = new UnitDefinition(
            "wisp", "Wisp", "wisp", 30, string.Empty, [],
            Deck: [new UnitDeckEntry("strike")],
            InitialEnergy: 0,
            EnergyPerTurn: 1,
            DrawPerTurn: 1,
            HandLimit: handLimit,
            AiProfileId: UnitAiProfileIds.LowCostCombo);
        return new Setup(
            state,
            BattleFixtures.MinimalCatalog(units: [unit]),
            hero,
            wisp,
            cpuParticipantId);
    }

    private static BattleCardInstance Card(string instanceId) =>
        new(instanceId, "strike", false, null);

    private sealed record Setup(
        BattleState State,
        DataCatalog Catalog,
        CombatActor Hero,
        CombatActor Wisp,
        string CpuParticipantId);

    private sealed class LastCandidateRng : IRng
    {
        public List<(int MinInclusive, int MaxExclusive)> IntRanges { get; } = [];

        public int NextInt(int minInclusive, int maxExclusive)
        {
            IntRanges.Add((minInclusive, maxExclusive));
            return maxExclusive - 1;
        }

        public double NextDouble() =>
            throw new InvalidOperationException("No double sample was expected");
    }
}
