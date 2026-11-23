using System.Collections.Immutable;
using System.Text.Json;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Tests.Services;

public sealed class BattleActionJournalComposerTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void CardPlay_uses_post_command_explicit_target_and_only_new_intent()
    {
        var card = new BattleCardInstance("card-1", "strike", false, null);
        var beforeActor = Actor("hero-1", ActorSide.Ally, 0) with
        {
            AttackSingle = AttackPool.Empty.Add(2),
        };
        var afterActor = beforeActor with
        {
            AttackSingle = beforeActor.AttackSingle.Add(
                new AttackHit(5, CombatSource.Unattributed, DamageCauseChain.Empty)
                {
                    TargetInstanceId = "enemy-2",
                }),
        };
        var before = State(
            [beforeActor],
            [Actor("enemy-1", ActorSide.Enemy, 0), Actor("enemy-2", ActorSide.Enemy, 1)],
            [Participant("p1", "hero-1", card, targetEnemyIndex: 0)]) with
        {
            ActionRevision = 3,
        };
        var after = before with
        {
            Allies = [afterActor],
            Participants =
            [
                Participant("p1", "hero-1", handCard: null, targetEnemyIndex: 1),
            ],
            ActionRevision = 4,
        };

        var draft = BattleActionJournalComposer.CardPlay(
            before,
            after,
            "p1",
            0,
            [new BattleEvent(
                BattleEventKind.PlayCard,
                0,
                "hero-1",
                Amount: 1,
                ComboCountAfter: 4)],
            Catalog,
            "human-card");

        Assert.Equal(4, draft.ComboCountAfter);
        Assert.Equal(["enemy-2"], draft.TargetActorInstanceIds);
        var intent = Assert.Single(draft.Intents!);
        Assert.Equal(5, intent.Amount);
        Assert.Equal("enemy-2", intent.TargetActorInstanceId);
        Assert.Empty(draft.AudienceParticipantIds!);
    }

    [Fact]
    public void CpuPlanning_uses_each_committed_decision_snapshot_not_final_state()
    {
        var finalActor = Actor("cpu-actor", ActorSide.Ally, 0) with
        {
            AttackSingle = AttackPool.Empty.Add(99),
        };
        var finalState = State(
            [finalActor],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [Participant("cpu", "cpu-actor", handCard: null, targetEnemyIndex: 0,
                controller: BattleParticipantControllerKind.Cpu)]) with
        {
            ActionRevision = 8,
        };
        var card = new BattleCardInstance("cpu-card", "strike", true, null);
        var breakdown = new CardCostBreakdown(1, 0, 0, false)
        {
            Changes =
            [
                new CardCostChangeStep(CardCostChangeKind.Modifier, -1, "setup"),
            ],
        };
        var hit = new AttackHit(
            7, CombatSource.Unattributed, DamageCauseChain.Empty)
        {
            TargetInstanceId = "enemy-1",
        };
        var decisionBefore = State(
            [Actor("cpu-actor", ActorSide.Ally, 0)],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [Participant("cpu", "cpu-actor", card, targetEnemyIndex: 0,
                controller: BattleParticipantControllerKind.Cpu) with { Energy = 1 }])
            with { ActionRevision = 6 };
        var decisionAfter = decisionBefore with
        {
            Allies =
            [
                decisionBefore.Allies[0] with
                {
                    AttackSingle = AttackPool.Empty.Add(hit),
                },
            ],
            Participants =
            [
                Participant("cpu", "cpu-actor", handCard: null, targetEnemyIndex: 0,
                    controller: BattleParticipantControllerKind.Cpu) with { Energy = 0 },
            ],
            ActionRevision = 7,
        };
        var action = new CpuCommittedAction(
            "cpu", "cpu-actor", "cpu-card", "strike", 0,
            0, null, "test",
            [new BattleEvent(
                BattleEventKind.PlayCard,
                Order: 0,
                CasterInstanceId: "cpu-actor",
                CardId: "strike",
                ComboCountAfter: 7)])
        {
            CardSnapshot = card,
            CostBreakdown = breakdown,
            UsesBefore = 1,
            RevisionBefore = 6,
            RevisionAfter = 7,
            TargetActorInstanceIds = ["enemy-1"],
            SelectedInstanceIds = ["selected-card"],
            IntentDelta = new CpuCommittedIntentDelta([hit], [], [], 0, 0),
            ActorContext = new CpuCommittedActorContext(3, 0, 0),
            StateBefore = decisionBefore,
            StateAfter = decisionAfter,
        };
        var planning = new BattleCpuPlanningResult(
            finalState, [action], [], []);

        var draft = Assert.Single(BattleActionJournalComposer.CpuPlanning(
            finalState with { ActionRevision = 6 },
            planning,
            Catalog,
            "cpu-plan"));

        Assert.Equal(6, draft.RevisionBefore);
        Assert.Equal(7, draft.RevisionAfter);
        Assert.True(draft.Card!.IsUpgraded);
        Assert.Equal(0, draft.Card.CurrentCost);
        Assert.Single(draft.Card.CostChanges!);
        Assert.Equal(["selected-card"], draft.SelectedInstanceIds);
        var intent = Assert.Single(draft.Intents!);
        Assert.Equal(7, intent.Amount);
        Assert.Equal(1, intent.Hits);
        Assert.Equal("enemy-1", intent.TargetActorInstanceId);
        Assert.Equal(1, draft.StateBefore!.Participants![0].Energy);
        Assert.Single(draft.StateBefore.Participants[0].Hand);
        Assert.Equal(0, draft.StateAfter!.Participants![0].Energy);
        Assert.Empty(draft.StateAfter.Participants[0].Hand);
        Assert.Equal(7, draft.StateAfter.Allies[0].AttackSingleDisplay);
        Assert.Equal(7, draft.ComboCountAfter);
        Assert.Equal("cpu-actor", draft.Source!.ActorInstanceId);
        Assert.Equal(draft.Card.DisplayName, draft.Source.ActionDisplayName);
    }

    [Fact]
    public void Skill_and_potion_capture_display_and_usage_at_decision_time()
    {
        var hero = Actor("hero-1", ActorSide.Ally, 0);
        var participant = Participant(
            "p1", "hero-1", handCard: null, targetEnemyIndex: 0) with
        {
            CharacterSkillUsesThisTurn =
                ImmutableDictionary<string, int>.Empty.Add("tactical", 1),
            Potions = ["energy_potion"],
            PotionUsesThisTurn =
                ImmutableDictionary<string, int>.Empty.Add("energy_potion", 1),
        };
        var before = State(
            [hero], [Actor("enemy-1", ActorSide.Enemy, 0)], [participant]);
        var after = before with { ActionRevision = 1 };

        var skill = BattleActionJournalComposer.CharacterSkill(
            before, after, "p1", "tactical", [], Catalog, "skill-command");
        var potion = BattleActionJournalComposer.Potion(
            before, after, "p1", 0, [], Catalog, "potion-command");

        Assert.Equal("characterSkill", skill.Ability!.Kind);
        Assert.Equal("tactical", skill.Ability.DefinitionId);
        Assert.Equal(1, skill.Ability.UsesBefore);
        Assert.NotEmpty(skill.Ability.Description);
        Assert.Equal("potion", potion.Ability!.Kind);
        Assert.Equal("energy_potion", potion.Ability.DefinitionId);
        Assert.Equal(1, potion.Ability.UsesBefore);
        Assert.NotEmpty(potion.Ability.Description);
    }

    [Fact]
    public void Cpu_reopen_feed_change_is_attached_to_the_causal_cpu_action()
    {
        var human = Participant(
            "human", "hero-1", handCard: null, targetEnemyIndex: 0) with
        {
            IsReady = true,
        };
        var cpu = Participant(
            "cpu", "cpu-actor", handCard: null, targetEnemyIndex: 0,
            controller: BattleParticipantControllerKind.Cpu);
        var before = State(
            [Actor("hero-1", ActorSide.Ally, 0), Actor("cpu-actor", ActorSide.Ally, 1)],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [human, cpu]);
        var after = BattleParticipantAccess.Replace(
            before with { ActionRevision = 1 },
            human with { IsReady = false, Energy = 4 });
        var action = new CpuCommittedAction(
            cpu.ParticipantId, cpu.ActorInstanceId,
            "cpu-card", "strike", 0,
            TargetEnemyIndex: 0, TargetAllyIndex: 1,
            DebugReason: "test", Events: [])
        {
            CardSnapshot = new BattleCardInstance("cpu-card", "strike", false, null),
            RevisionBefore = 0,
            RevisionAfter = 1,
            StateBefore = before,
            StateAfter = after,
            ReopenedParticipantIds = [human.ParticipantId],
        };
        var planning = new BattleCpuPlanningResult(after, [action], [], []);

        var draft = Assert.Single(BattleActionJournalComposer.CpuPlanning(
            before, planning, Catalog, "causal"));

        var feed = Assert.Single(draft.FeedChanges!);
        Assert.Equal(human.ParticipantId, feed.ParticipantId);
        Assert.False(feed.IsReady);
        Assert.True(draft.StateBefore!.Participants!.Single(participant =>
            participant.ParticipantId == human.ParticipantId).IsReady);
        Assert.False(draft.StateAfter!.Participants!.Single(participant =>
            participant.ParticipantId == human.ParticipantId).IsReady);
    }

    [Fact]
    public void CpuPlanning_merges_ready_stops_and_actions_by_planning_ordinal()
    {
        var firstCpu = Participant(
            "cpu:first", "cpu-first-actor", handCard: null, targetEnemyIndex: 0,
            controller: BattleParticipantControllerKind.Cpu);
        var card = new BattleCardInstance("cpu-card", "strike", false, null);
        var secondCpu = Participant(
            "cpu:second", "cpu-second-actor", card, targetEnemyIndex: 0,
            controller: BattleParticipantControllerKind.Cpu);
        var initial = State(
            [
                Actor(firstCpu.ActorInstanceId, ActorSide.Ally, 0),
                Actor(secondCpu.ActorInstanceId, ActorSide.Ally, 1),
            ],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [firstCpu, secondCpu]);
        var afterFirstReady = BattleParticipantAccess.Replace(
            initial, firstCpu with { IsReady = true });
        var afterAction = BattleParticipantAccess.Replace(
            afterFirstReady with { ActionRevision = 1 },
            secondCpu with { Hand = [] });
        var final = BattleParticipantAccess.Replace(
            afterAction, secondCpu with { Hand = [], IsReady = true });
        var action = new CpuCommittedAction(
            secondCpu.ParticipantId,
            secondCpu.ActorInstanceId,
            card.InstanceId,
            card.CardDefinitionId,
            PaidEnergy: 1,
            TargetEnemyIndex: 0,
            TargetAllyIndex: 1,
            DebugReason: "test",
            Events: [])
        {
            CardSnapshot = card,
            RevisionBefore = 0,
            RevisionAfter = 1,
            StateBefore = afterFirstReady,
            StateAfter = afterAction,
            PlanningOrdinal = 1,
        };
        var firstStop = new CpuPlanningStop(
            firstCpu.ParticipantId, CpuPlanningStopReason.NoLegalAction, 0)
        {
            StateBefore = initial,
            StateAfter = afterFirstReady,
            PlanningOrdinal = 0,
        };
        var secondStop = new CpuPlanningStop(
            secondCpu.ParticipantId, CpuPlanningStopReason.NoLegalAction, 1)
        {
            StateBefore = afterAction,
            StateAfter = final,
            PlanningOrdinal = 2,
        };
        var planning = new BattleCpuPlanningResult(
            final, [action], [firstStop, secondStop], [])
        {
            StateBeforePlanning = initial,
        };

        var drafts = BattleActionJournalComposer.CpuPlanning(
            initial, planning, Catalog, "interleaved");
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommands(drafts);

        Assert.Equal(["ready", "playCard", "ready"],
            drafts.Select(draft => draft.Kind));
        Assert.Equal(["ready", "playCard", "ready"],
            journal.Entries.Select(entry => entry.Kind));
        Assert.Equal(
            [firstCpu.ParticipantId, secondCpu.ParticipantId, secondCpu.ParticipantId],
            drafts.Select(draft => draft.ParticipantId));
        Assert.Equal(
            JsonSerializer.Serialize(drafts[0].StateAfter),
            JsonSerializer.Serialize(drafts[1].StateBefore));
        Assert.Equal(
            JsonSerializer.Serialize(drafts[1].StateAfter),
            JsonSerializer.Serialize(drafts[2].StateBefore));
    }

    [Fact]
    public void CpuPlanning_completes_the_feed_when_a_summon_was_removed_during_planning()
    {
        var human = Participant(
            "human", "hero-actor", handCard: null,
            targetEnemyIndex: 0);
        var cpu = Participant(
            "cpu:departed", "departed-actor", handCard: null,
            targetEnemyIndex: 0,
            controller: BattleParticipantControllerKind.Cpu);
        var before = State(
            [
                Actor(human.ActorInstanceId, ActorSide.Ally, 0),
                Actor(cpu.ActorInstanceId, ActorSide.Ally, 1),
            ],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [human, cpu]);
        var after = before with
        {
            Allies = [before.Allies[0]],
            Participants = [human],
        };
        var stop = new CpuPlanningStop(
            cpu.ParticipantId,
            CpuPlanningStopReason.ActorUnavailable,
            PlaysCommitted: 1)
        {
            StateBefore = after,
            StateAfter = after,
            PlanningOrdinal = 0,
        };
        var planning = new BattleCpuPlanningResult(after, [], [stop], [])
        {
            StateBeforePlanning = before,
        };

        var draft = Assert.Single(BattleActionJournalComposer.CpuPlanning(
            before, planning, Catalog, "departed"));

        var feed = Assert.Single(draft.FeedChanges!);
        Assert.Equal(cpu.ParticipantId, feed.ParticipantId);
        Assert.True(feed.IsReady);
        Assert.True(feed.IsComplete);
    }

    [Fact]
    public void Unit_skill_uses_the_same_decision_snapshot_format_as_player_skill()
    {
        var skill = new CharacterSkillDefinition(
            "wisp-art", "燐火", new ResourceCost(Energy: 1, ComboGauge: 2),
            OncePerTurn: false,
            Effects: [new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength")],
            UsesPerTurn: 2);
        UnitDefinition unit = Catalog.Units!["wisp"] with { Skills = [skill] };
        var units = Catalog.Units.ToDictionary(pair => pair.Key, pair => pair.Value);
        units[unit.Id] = unit;
        var catalog = Catalog with { Units = units };
        var participant = Participant(
            "cpu:wisp", "wisp-actor", handCard: null, targetEnemyIndex: 0,
            controller: BattleParticipantControllerKind.Cpu) with
        {
            CharacterId = unit.Id,
            CharacterSkillUsesThisTurn =
                ImmutableDictionary<string, int>.Empty.Add(skill.Id, 1),
        };
        var before = State(
            [Actor("wisp-actor", ActorSide.Ally, 0)],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [participant]);

        var draft = BattleActionJournalComposer.CharacterSkill(
            before, before with { ActionRevision = 1 }, participant.ParticipantId,
            skill.Id, [], catalog, "unit-skill");

        Assert.Equal("characterSkill", draft.Ability!.Kind);
        Assert.Equal(skill.Id, draft.Ability.DefinitionId);
        Assert.Equal(skill.Name, draft.Ability.DisplayName);
        Assert.Equal(1, draft.Ability.UsesBefore);
        Assert.Equal(2, draft.Ability.UsesPerTurn);
        Assert.Contains("[S:strength]", draft.Ability.Description);
    }

    [Fact]
    public void Reenact_effect_uses_played_snapshot_even_when_physical_card_changed_or_disappeared()
    {
        var exactCard = new BattleCardInstance(
            "reenacted-basic-attack", "basic_attack", IsUpgraded: true, CostOverride: null);
        var participant = Participant(
            "p1", "hero-1", handCard: null, targetEnemyIndex: 0) with
        {
            DiscardPile = [exactCard],
        };
        var state = State(
            [Actor("hero-1", ActorSide.Ally, 0)],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [participant]);
        CombatSource Source(string instanceId) => new(
            CombatSourceKind.CardEffect,
            OwnerSlot: 0,
            OwnerInstanceId: "hero-1",
            ActorInstanceId: "hero-1",
            DefinitionId: "basic_attack",
            InstanceId: instanceId,
            Scope: "reenact");
        PlayedCardEffectSnapshot Snapshot(string instanceId, bool upgraded) => new(
            CardDefinitionId: "basic_attack",
            CardInstanceId: instanceId,
            ActorInstanceId: "hero-1",
            CardType: CardType.Attack,
            OriginalCost: 1,
            PaidCost: 1,
            IsUpgraded: upgraded,
            Keywords: ImmutableArray<string>.Empty,
            Effects: (upgraded
                    && Catalog.Cards["basic_attack"].UpgradedEffects is { } upgradedEffects
                        ? upgradedEffects
                        : Catalog.Cards["basic_attack"].Effects)
                .ToImmutableArray(),
            PlaySequence: upgraded ? 2 : 1);
        var events = new[]
        {
            // The actual play was unupgraded. A later upgrade of the physical
            // instance must not alter the copied card face.
            new BattleEvent(
                BattleEventKind.ReenactCardEffects,
                Order: 0,
                CasterInstanceId: "hero-1",
                CardId: "basic_attack",
                Source: Source(exactCard.InstanceId),
                ReenactedCard: Snapshot(exactCard.InstanceId, upgraded: false)),
            // The actual play was upgraded. Its physical instance may disappear
            // from every pile without losing the copied card face.
            new BattleEvent(
                BattleEventKind.ReenactCardEffects,
                Order: 1,
                CasterInstanceId: "hero-1",
                CardId: "basic_attack",
                Source: Source("missing-reenacted-basic-attack"),
                ReenactedCard: Snapshot(
                    "missing-reenacted-basic-attack", upgraded: true)),
        };

        var draft = BattleActionJournalComposer.Command(
            state,
            state,
            kind: "resolveTurn",
            participantId: participant.ParticipantId,
            events: events,
            commandId: "reenact",
            catalog: Catalog);

        Assert.Collection(
            draft.Effects!,
            exact =>
            {
                Assert.Equal("basic_attack", exact.CardDefinitionId);
                Assert.Equal(exactCard.InstanceId, exact.CardInstanceId);
                Assert.Equal(exactCard.InstanceId, exact.Card!.CardInstanceId);
                Assert.False(exact.Card.IsUpgraded);
                Assert.Equal("Universal", exact.Card.PoolClass);
                Assert.Equal(0, exact.Card.PaidCost);
            },
            fallback =>
            {
                Assert.Equal("basic_attack", fallback.CardDefinitionId);
                Assert.Equal("missing-reenacted-basic-attack", fallback.CardInstanceId);
                Assert.Equal("missing-reenacted-basic-attack", fallback.Card!.CardInstanceId);
                Assert.True(fallback.Card.IsUpgraded);
                Assert.Equal("Universal", fallback.Card.PoolClass);
            });
    }

    [Theory]
    [InlineData(BattleEventKind.ContingencyTriggered)]
    [InlineData(BattleEventKind.ContingencyResolved)]
    public void Contingency_boundary_exposes_its_exact_card_snapshot_for_animation(
        BattleEventKind eventKind)
    {
        var participant = Participant(
            "p1", "hero-1", handCard: null, targetEnemyIndex: 0);
        var state = State(
            [Actor("hero-1", ActorSide.Ally, 0)],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [participant]);
        var snapshot = new PlayedCardEffectSnapshot(
            CardDefinitionId: "basic_attack",
            CardInstanceId: "armed-basic-attack",
            ActorInstanceId: "hero-1",
            CardType: CardType.Attack,
            OriginalCost: 1,
            PaidCost: 0,
            IsUpgraded: true,
            Keywords: ImmutableArray<string>.Empty,
            Effects: Catalog.Cards["basic_attack"].UpgradedEffects!
                .ToImmutableArray(),
            PlaySequence: 7);
        var battleEvent = new BattleEvent(
            eventKind,
            Order: 0,
            CasterInstanceId: "hero-1",
            CardId: "basic_attack",
            ReenactedCard: snapshot);

        var draft = BattleActionJournalComposer.Command(
            state,
            state,
            kind: "resolveTurn",
            participantId: participant.ParticipantId,
            events: [battleEvent],
            commandId: "contingency",
            catalog: Catalog);

        var effect = Assert.Single(draft.Effects!);
        Assert.Equal(eventKind.ToString(), effect.Kind);
        Assert.Equal("armed-basic-attack", effect.CardInstanceId);
        Assert.Equal("armed-basic-attack", effect.Card!.CardInstanceId);
        Assert.True(effect.Card.IsUpgraded);
    }

    [Fact]
    public void Resolution_damage_amount_is_the_actual_hp_loss()
    {
        var before = State(
            [Actor("hero-1", ActorSide.Ally, 0)],
            [Actor("enemy-1", ActorSide.Enemy, 0)],
            [Participant("p1", "hero-1", handCard: null, targetEnemyIndex: 0)]);
        var damage = new BattleEvent(
            BattleEventKind.DealDamage,
            Order: 0,
            CasterInstanceId: "hero-1",
            TargetInstanceId: "enemy-1",
            Amount: 12,
            BlockedAmount: 3,
            ActualHpLoss: 9);

        var draft = BattleActionJournalComposer.Command(
            before,
            before,
            "resolveTurn",
            participantId: null,
            events: [damage],
            commandId: "resolution",
            isResolution: true);

        var effect = Assert.Single(draft.Effects!);
        Assert.Equal(9, effect.Amount);
        Assert.Equal(9, effect.ActualHpLoss);
        Assert.Equal(3, effect.BlockedAmount);
    }

    [Fact]
    public void Resolution_keeps_removed_summon_in_its_inbound_replay_feed()
    {
        var hero = Actor("hero-1", ActorSide.Ally, 0);
        var summon = Actor("summon-1", ActorSide.Ally, 1);
        var enemy = Actor("enemy-1", ActorSide.Enemy, 0);
        var human = Participant(
            "p1", hero.InstanceId, handCard: null, targetEnemyIndex: 0);
        var summonedParticipant = Participant(
            "cpu:summon-1", summon.InstanceId, handCard: null, targetEnemyIndex: 0,
            controller: BattleParticipantControllerKind.Cpu);
        var before = State([hero, summon], [enemy], [human, summonedParticipant]);
        var after = before with
        {
            Allies = [hero],
            Participants = [human],
            ActionRevision = 1,
        };
        var damage = new BattleEvent(
            BattleEventKind.DealDamage,
            Order: 0,
            CasterInstanceId: enemy.InstanceId,
            TargetInstanceId: summon.InstanceId,
            Amount: summon.CurrentHp,
            ActualHpLoss: summon.CurrentHp);
        var death = new BattleEvent(
            BattleEventKind.ActorDeath,
            Order: 1,
            CasterInstanceId: enemy.InstanceId,
            TargetInstanceId: summon.InstanceId);

        var draft = BattleActionJournalComposer.Command(
            before,
            after,
            "resolveTurn",
            participantId: null,
            events: [damage, death],
            commandId: "resolution",
            isResolution: true,
            catalog: Catalog);
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(draft);

        Assert.All(draft.Effects!, effect =>
            Assert.Equal(summonedParticipant.ParticipantId, effect.TargetParticipantId));
        var replayEntry = Assert.Single(journal.Query(new BattleActionFilter(
            ParticipantId: summonedParticipant.ParticipantId)));
        Assert.Equal(2, replayEntry.Effects.Length);
        Assert.Contains(replayEntry.Effects,
            effect => effect.Kind == BattleEventKind.ActorDeath.ToString());
    }

    private static BattleState State(
        ImmutableArray<CombatActor> allies,
        ImmutableArray<CombatActor> enemies,
        ImmutableArray<BattleParticipantState> participants) =>
        new(
            Turn: 1,
            Phase: BattlePhase.PlayerInput,
            Outcome: BattleOutcome.Pending,
            Allies: allies,
            Enemies: enemies,
            TargetAllyIndex: 0,
            TargetEnemyIndex: 0,
            Energy: 3,
            EnergyMax: 3,
            DrawPile: [],
            Hand: participants.IsDefaultOrEmpty ? [] : participants[0].Hand,
            DiscardPile: [],
            ExhaustPile: [],
            SummonHeld: [],
            PowerCards: [],
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            OwnedRelicIds: [],
            Potions: [],
            EncounterId: "test")
        {
            Participants = participants,
        };

    private static CombatActor Actor(
        string id,
        ActorSide side,
        int slot) =>
        new(
            id,
            id,
            side,
            slot,
            20,
            20,
            BlockPool.Empty,
            AttackPool.Empty,
            AttackPool.Empty,
            AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty,
            null,
            null,
            null);

    private static BattleParticipantState Participant(
        string participantId,
        string actorId,
        BattleCardInstance? handCard,
        int targetEnemyIndex,
        BattleParticipantControllerKind controller = BattleParticipantControllerKind.Human) =>
        new(
            ParticipantId: participantId,
            ActorInstanceId: actorId,
            ControllerKind: controller,
            OwnerPlayerSlot: 0,
            CharacterId: "default",
            TargetAllyIndex: 0,
            TargetEnemyIndex: targetEnemyIndex,
            Energy: 3,
            EnergyPerTurn: 3,
            DrawPerTurn: 5,
            DrawPile: [],
            Hand: handCard is null ? [] : [handCard],
            DiscardPile: [],
            ExhaustPile: [],
            SummonHeld: [],
            PowerCards: [],
            ComboCount: 0,
            MaxComboThisTurn: 0,
            ComboGauge: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            WildUsedInCurrentCombo: false,
            UsedCharacterSkillIdsThisTurn: [],
            CharacterSkillUsesThisTurn: ImmutableDictionary<string, int>.Empty,
            CardUsesThisTurn: ImmutableDictionary<string, int>.Empty,
            CardUseLockedThisTurn: false,
            OwnedRelicIds: [],
            Potions: [],
            PotionUsesThisTurn: ImmutableDictionary<string, int>.Empty,
            PendingCardPlay: null,
            IsReady: false);
}
