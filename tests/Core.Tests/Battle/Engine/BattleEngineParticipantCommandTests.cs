using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineParticipantCommandTests
{
    private const string CpuParticipantId = "cpu:wisp_inst";

    private static IRng Rng() => new FakeRng(new int[40], new double[0]);

    [Fact]
    public void Secondary_play_uses_its_actor_resources_and_preserves_primary_mirror()
    {
        var action = new CardDefinition(
            "wisp_action", "Wisp action", null,
            CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect(
                    "attack", EffectScope.Single, EffectSide.Enemy, 7),
                new CardEffect(
                    "block", EffectScope.Self, null, 4),
            },
            UpgradedEffects: null, Keywords: null,
            UsesPerTurn: 1);
        var cpuHand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard(action.Id, "wisp-action-1"));
        var (state, primaryBefore) = MakeParticipantBattle(cpuHand, energy: 2);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), action });

        var (next, events) = BattleEngine.PlayCard(
            state, CpuParticipantId, handIndex: 0,
            targetEnemyIndex: 0, targetAllyIndex: 1,
            Rng(), catalog);

        var cpu = next.Participants[1];
        var hero = next.Allies.Single(actor => actor.InstanceId == "hero_inst");
        var wisp = next.Allies.Single(actor => actor.InstanceId == "wisp_inst");
        Assert.Equal(1, cpu.Energy);
        Assert.Empty(cpu.Hand);
        Assert.Equal("wisp-action-1", Assert.Single(cpu.DiscardPile).InstanceId);
        Assert.Equal(1, cpu.ComboCount);
        Assert.Equal(1, cpu.CardUsesThisTurn[action.Id]);
        Assert.Equal(7, wisp.AttackSingle.Sum);
        Assert.Equal(4, wisp.Block.Sum);
        Assert.Equal(0, hero.AttackSingle.Sum);
        Assert.Equal(0, hero.Block.Sum);
        Assert.Contains(events, battleEvent =>
            battleEvent.CasterInstanceId == "wisp_inst");
        Assert.All(
            events.Where(battleEvent => battleEvent.Source is not null),
            battleEvent => Assert.Equal(
                "wisp_inst", battleEvent.Source!.ActorInstanceId));

        Assert.Equal(primaryBefore, next.Participants[0]);
        Assert.Equal(primaryBefore.Energy, next.Energy);
        Assert.Equal(primaryBefore.Hand, next.Hand);
        Assert.Equal(primaryBefore.TargetAllyIndex, next.TargetAllyIndex);
        Assert.Equal(1, next.ActionRevision);
    }

    [Fact]
    public void Secondary_choice_is_resolved_in_its_own_zones_by_its_actor()
    {
        var choose = new CardDefinition(
            "wisp_choose", "Wisp choose", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect(
                    "exhaustCard", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "choose"),
            },
            UpgradedEffects: null, Keywords: null);
        var cpuHand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "wisp-strike"),
            BattleFixtures.MakeBattleCard("defend", "wisp-defend"),
            BattleFixtures.MakeBattleCard(choose.Id, "wisp-choose"));
        var (state, primaryBefore) = MakeParticipantBattle(cpuHand, energy: 2);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), choose });

        var (paused, _) = BattleEngine.PlayCard(
            state, CpuParticipantId, handIndex: 2,
            targetEnemyIndex: 0, targetAllyIndex: 1,
            Rng(), catalog);

        Assert.NotNull(paused.Participants[1].PendingCardPlay);
        Assert.Null(paused.PendingCardPlay);
        Assert.Equal(1, paused.ActionRevision);

        var (resolved, events) = BattleEngine.ResolveCardChoice(
            paused, CpuParticipantId,
            ImmutableArray.Create("wisp-strike"), Rng(), catalog);

        var cpu = resolved.Participants[1];
        Assert.Null(cpu.PendingCardPlay);
        Assert.Contains(cpu.ExhaustPile, card => card.InstanceId == "wisp-strike");
        Assert.Contains(cpu.DiscardPile, card => card.InstanceId == "wisp-choose");
        Assert.Contains(cpu.Hand, card => card.InstanceId == "wisp-defend");
        Assert.DoesNotContain(cpu.Hand, card => card.InstanceId == "wisp-strike");
        Assert.All(
            events.Where(battleEvent => battleEvent.Source is not null),
            battleEvent => Assert.Equal(
                "wisp_inst", battleEvent.Source!.ActorInstanceId));
        Assert.Equal(primaryBefore, resolved.Participants[0]);
        Assert.Equal(primaryBefore.Hand, resolved.Hand);
        Assert.Null(resolved.PendingCardPlay);
        Assert.Equal(2, resolved.ActionRevision);
    }

    [Fact]
    public void Secondary_continuity_status_controls_its_own_combo_and_cost()
    {
        var expensive = new CardDefinition(
            "wisp_expensive", "Wisp expensive", null,
            CardRarity.Common, CardType.Skill,
            Cost: 2, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("block", EffectScope.Self, null, 2),
            },
            UpgradedEffects: null, Keywords: null);
        var wisp = BattleFixtures.WithStatus(
            OwnedWisp(), BattleStatusIds.Continuity, 1);
        var (state, _) = MakeParticipantBattle(
            ImmutableArray.Create(
                BattleFixtures.MakeBattleCard(expensive.Id, "wisp-expensive")),
            energy: 3,
            wisp: wisp);
        var cpu = state.Participants[1] with
        {
            ComboCount = 1,
            MaxComboThisTurn = 1,
            LastPlayedOrigCost = 0,
        };
        state = BattleParticipantAccess.Replace(state, cpu);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), expensive });

        var (next, _) = BattleEngine.PlayCard(
            state, CpuParticipantId, 0, 0, 1, Rng(), catalog);

        var nextCpu = next.Participants[1];
        var nextWisp = next.Allies.Single(actor => actor.InstanceId == "wisp_inst");
        Assert.Equal(2, nextCpu.Energy);
        Assert.Equal(2, nextCpu.ComboCount);
        Assert.Equal(0, nextWisp.GetStatus(BattleStatusIds.Continuity));
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Continuity));
    }

    [Fact]
    public void Secondary_target_change_updates_only_its_target_and_revision()
    {
        var (state, primaryBefore) = MakeParticipantBattle([], energy: 0);
        state = state with
        {
            Enemies = ImmutableArray.Create(
                BattleFixtures.Goblin(slotIndex: 0),
                BattleFixtures.Goblin(slotIndex: 1)),
        };

        var next = BattleEngine.SetTarget(
            state, CpuParticipantId, ActorSide.Enemy, slotIndex: 1);

        Assert.Equal(1, next.Participants[1].TargetEnemyIndex);
        Assert.Equal(primaryBefore.TargetEnemyIndex, next.TargetEnemyIndex);
        Assert.Equal(primaryBefore.TargetEnemyIndex, next.Participants[0].TargetEnemyIndex);
        Assert.Equal(1, next.ActionRevision);
    }

    [Fact]
    public void Secondary_potion_uses_its_inventory_actor_and_revision()
    {
        var potion = BattleFixtures.Potion(
            "wisp-potion",
            new CardEffect("block", EffectScope.Self, null, 3));
        var (state, primaryBefore) = MakeParticipantBattle([], energy: 0);
        var cpu = state.Participants[1] with
        {
            Potions = ImmutableArray.Create("wisp-potion"),
        };
        state = BattleParticipantAccess.Replace(state, cpu);
        var catalog = BattleFixtures.MinimalCatalog(
            potions: new[] { potion });

        var (next, events) = BattleEngine.UsePotion(
            state,
            CpuParticipantId,
            potionIndex: 0,
            targetEnemyIndex: 0,
            targetAllyIndex: 1,
            rng: Rng(),
            catalog: catalog);

        Assert.Equal("", next.Participants[1].Potions[0]);
        Assert.Equal(primaryBefore.Potions, next.Potions);
        Assert.Equal(3, next.Allies.Single(
            actor => actor.InstanceId == "wisp_inst").Block.Sum);
        Assert.Equal(0, next.Allies.Single(
            actor => actor.InstanceId == "hero_inst").Block.Sum);
        Assert.All(events, battleEvent =>
            Assert.Equal("wisp_inst", battleEvent.CasterInstanceId));
        Assert.Equal(1, next.ActionRevision);
    }

    [Fact]
    public void Secondary_character_skill_uses_its_resources_and_actor()
    {
        var skill = new CharacterSkillDefinition(
            "wisp-technique",
            "Wisp Technique",
            new ResourceCost(Energy: 1, ComboGauge: 1),
            OncePerTurn: true,
            Effects:
            [
                new CardEffect(
                    "buff", EffectScope.Self, null, 2, Name: "strength"),
            ]);
        var character = new CharacterDefinition(
            "wisp", "Wisp", 30, 0, 0, [], Skills: [skill]);
        var (state, primaryBefore) = MakeParticipantBattle([], energy: 2);
        var cpu = state.Participants[1] with { ComboGauge = 2 };
        state = BattleParticipantAccess.Replace(state, cpu);
        var catalog = BattleFixtures.MinimalCatalog() with
        {
            Characters = new System.Collections.Generic.Dictionary<
                string, CharacterDefinition>
            {
                [character.Id] = character,
            },
        };

        var (next, events) = BattleEngine.UseCharacterSkill(
            state, CpuParticipantId, skill.Id, catalog, Rng());

        Assert.Equal(1, next.Participants[1].Energy);
        Assert.Equal(1, next.Participants[1].ComboGauge);
        Assert.Equal(primaryBefore.Energy, next.Energy);
        Assert.Equal(primaryBefore.ComboGauge, next.ComboGauge);
        Assert.Equal(2, next.Allies.Single(
            actor => actor.InstanceId == "wisp_inst").GetStatus("strength"));
        Assert.Equal(0, next.Allies.Single(
            actor => actor.InstanceId == "hero_inst").GetStatus("strength"));
        Assert.Contains(events, battleEvent =>
            battleEvent.CasterInstanceId == "wisp_inst");
        Assert.Equal(1, next.ActionRevision);
    }

    [Fact]
    public void Legacy_and_primary_compatible_commands_increment_revision_safely()
    {
        var legacy = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "legacy-strike"))) with
        {
            ActionRevision = long.MaxValue,
        };
        var catalog = BattleFixtures.MinimalCatalog();

        var (legacyNext, _) = BattleEngine.PlayCard(
            legacy, 0, 0, 0, Rng(), catalog);

        Assert.Empty(legacyNext.Participants);
        Assert.Equal(long.MaxValue, legacyNext.ActionRevision);

        var negativeLegacy = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "negative-strike"))) with
        {
            ActionRevision = -5,
        };
        var (normalizedRevision, _) = BattleEngine.PlayCard(
            negativeLegacy, 0, 0, 0, Rng(), catalog);
        Assert.Equal(1, normalizedRevision.ActionRevision);

        var primaryState = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                hand: ImmutableArray.Create(
                    BattleFixtures.MakeBattleCard("strike", "primary-strike"))));
        var (primaryNext, _) = BattleEngine.PlayCard(
            primaryState, 0, 0, 0, Rng(), catalog);

        Assert.Empty(primaryNext.Hand);
        Assert.Empty(primaryNext.Participants[0].Hand);
        Assert.Equal(primaryNext.Hand, primaryNext.Participants[0].Hand);
        Assert.Equal(1, primaryNext.ActionRevision);
    }

    private static (BattleState State, BattleParticipantState Primary)
        MakeParticipantBattle(
            ImmutableArray<BattleCardInstance> cpuHand,
            int energy,
            CombatActor? wisp = null)
    {
        wisp ??= OwnedWisp();
        var primaryHand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("defend", "primary-defend"));
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero(), wisp),
            hand: primaryHand,
            energy: 3);
        state = BattleParticipantAccess.InitializePrimary(state);
        var primary = state.Participants[0];
        var secondary = primary with
        {
            ParticipantId = CpuParticipantId,
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = 0,
            CharacterId = wisp.DefinitionId,
            TargetAllyIndex = 1,
            TargetEnemyIndex = 0,
            Energy = energy,
            EnergyPerTurn = 2,
            DrawPerTurn = 2,
            DrawPile = ImmutableArray<BattleCardInstance>.Empty,
            Hand = cpuHand,
            DiscardPile = ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile = ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld = ImmutableArray<BattleCardInstance>.Empty,
            PowerCards = ImmutableArray<BattleCardInstance>.Empty,
            ComboCount = 0,
            MaxComboThisTurn = 0,
            ComboGauge = 0,
            LastPlayedOrigCost = null,
            NextCardComboFreePass = false,
            WildUsedInCurrentCombo = false,
            UsedCharacterSkillIdsThisTurn = ImmutableHashSet<string>.Empty,
            CharacterSkillUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            CardUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            CardUseLockedThisTurn = false,
            OwnedRelicIds = ImmutableArray<string>.Empty,
            Potions = ImmutableArray<string>.Empty,
            PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            PendingCardPlay = null,
            IsReady = false,
        };
        state = BattleParticipantAccess.Add(state, secondary);
        return (state, primary);
    }

    private static CombatActor OwnedWisp() =>
        BattleFixtures.SummonActor("wisp_inst", "wisp", slotIndex: 1) with
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = "hero_inst",
        };
}
