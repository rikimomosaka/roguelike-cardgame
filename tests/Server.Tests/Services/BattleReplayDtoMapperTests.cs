using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Dtos;

namespace RoguelikeCardGame.Server.Tests.Services;

public sealed class BattleReplayDtoMapperTests
{
    [Fact]
    public void Decision_snapshots_project_the_viewed_participant_and_keep_compact_source_names()
    {
        var before = ReplayState(
            sourceEnergy: 2, targetEnergy: 4,
            sourceHandIds: ["spark", "guard"],
            sourceCombo: 0, sourceAttack: 0, targetHp: 30);
        var after = ReplayState(
            sourceEnergy: 1, targetEnergy: 5,
            sourceHandIds: ["guard"],
            sourceCombo: 1, sourceAttack: 3, targetHp: 29);
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(
            new BattleActionDraft(
                Turn: 4,
                CommandId: "cpu-spark",
                Kind: "playCard",
                ParticipantId: "cpu-source",
                ActorInstanceId: "wisp-source",
                RevisionBefore: 10,
                RevisionAfter: 11,
                Effects:
                [
                    new BattleActionEffectSnapshot(
                        "gainEnergy", "wisp-source", "cpu-target", "wisp-target", 1),
                ],
                StateBefore: before,
                StateAfter: after,
                Source: new BattleActionSourceSnapshot(
                    "wisp-source", "青いウィスプ", "card", "wisp_spark", "火花")));

        var sourceFeed = BattleReplayDtoMapper.ToDto(
            journal,
            new BattleActionFilter(4, "cpu-source", "wisp-source"));
        var source = Assert.Single(sourceFeed.Entries);
        var sourceParticipantBefore = Assert.Single(
            source.StateBefore!.Participants!, participant =>
                participant.ParticipantId == "cpu-source");
        var sourceParticipantAfter = Assert.Single(
            source.StateAfter!.Participants!, participant =>
                participant.ParticipantId == "cpu-source");
        Assert.Equal(2, sourceParticipantBefore.Energy);
        Assert.Equal(["spark", "guard"],
            sourceParticipantBefore.Hand.Select(card => card.InstanceId));
        Assert.Equal(1, sourceParticipantAfter.Energy);
        Assert.Equal(["guard"],
            sourceParticipantAfter.Hand.Select(card => card.InstanceId));
        Assert.Equal(0, sourceParticipantBefore.ComboCount);
        Assert.Equal(1, sourceParticipantAfter.ComboCount);
        Assert.Equal(0, Assert.Single(source.StateBefore.Allies, actor =>
            actor.InstanceId == "wisp-source").AttackSingleDisplay);
        Assert.Equal(3, Assert.Single(source.StateAfter.Allies, actor =>
            actor.InstanceId == "wisp-source").AttackSingleDisplay);

        var targetFeed = BattleReplayDtoMapper.ToDto(
            journal,
            new BattleActionFilter(4, "cpu-target", "wisp-target"));
        var inbound = Assert.Single(targetFeed.Entries);
        Assert.Equal("externalEffect", inbound.ViewKind);
        Assert.Equal(4, Assert.Single(
            inbound.StateBefore!.Participants!, participant =>
                participant.ParticipantId == "cpu-target").Energy);
        Assert.Equal(5, Assert.Single(
            inbound.StateAfter!.Participants!, participant =>
                participant.ParticipantId == "cpu-target").Energy);
        Assert.Equal(30, Assert.Single(inbound.StateBefore.Allies, actor =>
            actor.InstanceId == "wisp-target").CurrentHp);
        Assert.Equal(29, Assert.Single(inbound.StateAfter.Allies, actor =>
            actor.InstanceId == "wisp-target").CurrentHp);
        Assert.Equal("青いウィスプ", inbound.Source!.ActorDisplayName);
        Assert.Equal("火花", inbound.Source.ActionDisplayName);
        Assert.Null(inbound.Card);
        Assert.Null(inbound.Ability);
    }

    [Fact]
    public void Source_feed_has_full_card_while_target_feed_has_only_its_compact_effect()
    {
        var card = new BattleCardDecisionSnapshot(
            CardInstanceId: "instance-1",
            DefinitionId: "support-card",
            DisplayName: "応援",
            CardType: "skill",
            Rarity: "common",
            IsUpgraded: true,
            OriginalCost: 2,
            CurrentCost: 1,
            PaidCost: 1,
            Description: "味方単体に筋力1を付与。",
            CostChanges:
            [
                new BattleCardCostChangeSnapshot(
                    "status", -1, "temperance", "節制"),
            ]);
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(new BattleActionDraft(
            Turn: 3,
            CommandId: "support-command",
            Kind: "cardPlayed",
            ParticipantId: "source-player",
            ActorInstanceId: "source-actor",
            RevisionBefore: 8,
            RevisionAfter: 9,
            Card: card,
            TargetActorInstanceIds: ["target-actor", "other-actor"],
            SelectedInstanceIds: ["selected-instance"],
            Ability: new BattleAbilityDecisionSnapshot(
                "skill-1", "Skill One", "characterSkill", "Gain block.",
                1, 2, 0, 1),
            Effects:
            [
                new BattleActionEffectSnapshot(
                    "status", "source-actor", "target-player", "target-actor",
                    1, "strength", "筋力1を付与。"),
                new BattleActionEffectSnapshot(
                    "status", "source-actor", "other-player", "other-actor",
                    1, "dexterity", "敏捷1を付与。"),
            ]));

        var source = BattleReplayDtoMapper.ToDto(journal, new BattleActionFilter(
            Turn: 3,
            ParticipantId: "source-player"));
        var target = BattleReplayDtoMapper.ToDto(journal, new BattleActionFilter(
            Turn: 3,
            ParticipantId: "target-player",
            ActorInstanceId: "target-actor"));

        var sourceEntry = Assert.Single(source.Entries);
        Assert.Equal("action", sourceEntry.ViewKind);
        Assert.Equal("応援", sourceEntry.Card!.DisplayName);
        Assert.Equal(2, sourceEntry.Card.OriginalCost);
        Assert.Equal(1, sourceEntry.Card.CurrentCost);
        Assert.Equal(1, sourceEntry.Card.PaidCost);
        Assert.Single(sourceEntry.Card.CostChanges);
        Assert.Equal(2, sourceEntry.Effects.Count);
        Assert.Equal(["selected-instance"], sourceEntry.SelectedInstanceIds);
        Assert.Equal("Skill One", sourceEntry.Ability!.DisplayName);

        var inbound = Assert.Single(target.Entries);
        Assert.Equal("externalEffect", inbound.ViewKind);
        Assert.Null(inbound.Card);
        Assert.Empty(inbound.SelectedInstanceIds);
        Assert.Null(inbound.Ability);
        var effect = Assert.Single(inbound.Effects);
        Assert.Equal("target-actor", effect.TargetActorInstanceId);
        Assert.Equal("strength", effect.StatusId);
    }

    [Fact]
    public void Reenact_effect_projects_its_own_card_snapshot_for_replay_animation()
    {
        var reenactedCard = new BattleCardDecisionSnapshot(
            CardInstanceId: "source-card-1",
            DefinitionId: "source-card",
            DisplayName: "再演元",
            CardType: "Attack",
            Rarity: "Rare",
            IsUpgraded: true,
            OriginalCost: 2,
            CurrentCost: 2,
            PaidCost: 0,
            Description: "敵単体に12アタック。",
            PoolClass: "Striker");
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(
            new BattleActionDraft(
                Turn: 2,
                CommandId: "reenact-marker",
                Kind: "resolveTurn",
                ParticipantId: "source-player",
                ActorInstanceId: "source-actor",
                RevisionBefore: 3,
                RevisionAfter: 3,
                Effects:
                [
                    new BattleActionEffectSnapshot(
                        Kind: "ReenactCardEffects",
                        SourceActorInstanceId: "source-actor",
                        TargetParticipantId: null,
                        TargetActorInstanceId: null,
                        CardDefinitionId: reenactedCard.DefinitionId,
                        CardInstanceId: reenactedCard.CardInstanceId,
                        Card: reenactedCard),
                ]));

        var dto = BattleReplayDtoMapper.ToDto(
            journal,
            new BattleActionFilter(
                Turn: 2,
                ParticipantId: "source-player",
                ActorInstanceId: "source-actor"));

        var effect = Assert.Single(Assert.Single(dto.Entries).Effects);
        Assert.Equal("source-card", effect.CardDefinitionId);
        Assert.Equal("source-card-1", effect.CardInstanceId);
        Assert.Equal("source-card-1", effect.Card!.CardInstanceId);
        Assert.True(effect.Card.IsUpgraded);
        Assert.Equal("Striker", effect.Card.PoolClass);
    }

    [Fact]
    public void Ready_and_complete_states_are_exposed_for_polling_and_sequence_cursor()
    {
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommands(
        [
            new BattleActionDraft(
                1, "p1-ready", "readyChanged", "p1", "a1", 0, 1,
                FeedChanges: [new BattleReplayFeedChange("p1", IsReady: true)]),
            new BattleActionDraft(
                1, "p2-ready", "readyChanged", "p2", "a2", 1, 2,
                FeedChanges: [new BattleReplayFeedChange("p2", IsReady: true)]),
            new BattleActionDraft(
                1, "resolved", "turnCompleted", null, null, 2, 3,
                AudienceParticipantIds: ["p1", "p2"],
                FeedChanges:
                [
                    new BattleReplayFeedChange("p1", IsComplete: true),
                    new BattleReplayFeedChange("p2", IsComplete: true),
                ],
                IsResolution: true),
        ]);

        var dto = BattleReplayDtoMapper.ToDto(journal, new BattleActionFilter(
            Turn: 1,
            ParticipantId: "p1",
            AfterSequence: 1));

        Assert.Equal(1, dto.FromSequenceExclusive);
        Assert.Equal(3, dto.LatestSequence);
        Assert.Equal(3, dto.ReturnedThroughSequence);
        Assert.Equal("complete", dto.FeedState);
        Assert.True(dto.IsReady);
        Assert.True(dto.IsComplete);
        var feed = Assert.Single(dto.ParticipantFeeds);
        Assert.Equal("p1", feed.ParticipantId);
        Assert.Equal("complete", feed.State);
        Assert.Equal([3L], dto.Entries.Select(entry => entry.Sequence));
        Assert.Equal("resolution", dto.Entries[^1].ViewKind);
    }

    [Fact]
    public void Unknown_selected_feed_defaults_to_live()
    {
        var dto = BattleReplayDtoMapper.ToDto(
            BattleActionJournal.Empty,
            new BattleActionFilter(Turn: 1, ParticipantId: "not-yet-joined"));

        Assert.Equal("live", dto.FeedState);
        Assert.False(dto.IsReady);
        Assert.False(dto.IsComplete);
        Assert.Empty(dto.ParticipantFeeds);
        Assert.Empty(dto.Entries);
    }

    [Fact]
    public void Shared_resolution_is_a_full_view_for_every_participant_and_source_actor()
    {
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(
            new BattleActionDraft(
                Turn: 4,
                CommandId: "resolve-turn",
                Kind: "resolveTurn",
                ParticipantId: "last-ready-human",
                ActorInstanceId: "human-actor",
                RevisionBefore: 20,
                RevisionAfter: 21,
                AudienceParticipantIds:
                    ["last-ready-human", "earlier-human", "cpu-participant"],
                Effects:
                [
                    new BattleActionEffectSnapshot(
                        "DealDamage", "cpu-actor", null, "enemy-1",
                        Amount: 12,
                        ActualHpLoss: 9,
                        BlockedAmount: 3),
                    new BattleActionEffectSnapshot(
                        "DealDamage", "human-actor", null, "enemy-2",
                        Amount: 4),
                ],
                IsResolution: true));

        var cpuReplay = BattleReplayDtoMapper.ToDto(
            journal,
            new BattleActionFilter(
                Turn: 4,
                ParticipantId: "cpu-participant",
                ActorInstanceId: "cpu-actor"));

        var entry = Assert.Single(cpuReplay.Entries);
        Assert.Equal("resolution", entry.ViewKind);
        Assert.Equal(2, entry.Effects.Count);
        Assert.Contains(entry.Effects, effect =>
            effect.SourceActorInstanceId == "cpu-actor"
            && effect.Amount == 12
            && effect.ActualHpLoss == 9
            && effect.BlockedAmount == 3);
    }

    [Fact]
    public void Feed_only_audience_does_not_receive_an_empty_external_effect()
    {
        var card = new BattleCardDecisionSnapshot(
            "card-1", "strike", "Strike", "Attack", "Starter", false,
            1, 1, 1, "Deal damage.");
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(
            new BattleActionDraft(
                Turn: 1,
                CommandId: "reopen-ready-cpu",
                Kind: "playCard",
                ParticipantId: "human",
                ActorInstanceId: "human-actor",
                RevisionBefore: 0,
                RevisionAfter: 1,
                Card: card,
                FeedChanges:
                [
                    new BattleReplayFeedChange("cpu", IsReady: false),
                ]));

        var replay = BattleReplayDtoMapper.ToDto(
            journal,
            new BattleActionFilter(ParticipantId: "cpu"));

        var entry = Assert.Single(replay.Entries);
        Assert.Equal("feedState", entry.ViewKind);
        Assert.Null(entry.Card);
        Assert.Empty(entry.Effects);
        Assert.Empty(entry.Intents);
    }

    [Fact]
    public void Replay_entry_preserves_authoritative_post_play_combo_count()
    {
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(
            new BattleActionDraft(
                Turn: 2,
                CommandId: "combo-card",
                Kind: "playCard",
                ParticipantId: "source-player",
                ActorInstanceId: "source-actor",
                RevisionBefore: 4,
                RevisionAfter: 5,
                ComboCountAfter: 15));

        var replay = BattleReplayDtoMapper.ToDto(
            journal,
            new BattleActionFilter(ParticipantId: "source-player"));

        Assert.Equal(15, Assert.Single(replay.Entries).ComboCountAfter);
    }

    private static BattleStateDto ReplayState(
        int sourceEnergy,
        int targetEnergy,
        IReadOnlyList<string> sourceHandIds,
        int sourceCombo,
        int sourceAttack,
        int targetHp)
    {
        var sourceActor = ReplayActor("wisp-source", 1, 30, sourceAttack);
        var targetActor = ReplayActor("wisp-target", 2, targetHp, 0);
        var source = ReplayParticipant(
            "cpu-source", sourceActor.InstanceId, sourceEnergy, sourceHandIds, sourceCombo);
        var target = ReplayParticipant(
            "cpu-target", targetActor.InstanceId, targetEnergy, [], 0);
        return new BattleStateDto(
            Turn: 4,
            Phase: "PlayerInput",
            Outcome: "Pending",
            Allies: [sourceActor, targetActor],
            Enemies: [],
            TargetAllyIndex: 0,
            TargetEnemyIndex: null,
            Energy: sourceEnergy,
            EnergyPerTurn: 1,
            DrawPile: [],
            Hand: source.Hand,
            DiscardPile: [],
            ExhaustPile: [],
            SummonHeld: [],
            PowerCards: [],
            ComboCount: sourceCombo,
            MaxComboThisTurn: sourceCombo,
            ComboGauge: 0,
            UsedCharacterSkillIdsThisTurn: [],
            CharacterSkillUsesThisTurn: new Dictionary<string, int>(),
            LastPlayedOrigCost: sourceCombo == 0 ? null : 1,
            NextCardComboFreePass: false,
            OwnedRelicIds: [],
            Potions: [],
            PotionUsesThisTurn: new Dictionary<string, int>(),
            EncounterId: "replay-test",
            Participants: [source, target]);
    }

    private static BattleParticipantDto ReplayParticipant(
        string participantId,
        string actorInstanceId,
        int energy,
        IReadOnlyList<string> handIds,
        int combo)
    {
        var hand = handIds.Select(id => new BattleCardInstanceDto(
            id, "wisp_spark", false, null, 1, 1)).ToList();
        return new BattleParticipantDto(
            ParticipantId: participantId,
            ActorInstanceId: actorInstanceId,
            ControllerKind: "Cpu",
            OwnerPlayerSlot: 0,
            CharacterId: "wisp",
            TargetAllyIndex: 0,
            TargetEnemyIndex: null,
            Energy: energy,
            EnergyPerTurn: 1,
            DrawPerTurn: 3,
            DrawPile: [],
            Hand: hand,
            DiscardPile: [],
            ExhaustPile: [],
            SummonHeld: [],
            PowerCards: [],
            ComboCount: combo,
            MaxComboThisTurn: combo,
            ComboGauge: 0,
            LastPlayedOrigCost: combo == 0 ? null : 1,
            NextCardComboFreePass: false,
            WildUsedInCurrentCombo: false,
            UsedCharacterSkillIdsThisTurn: [],
            CharacterSkillUsesThisTurn: new Dictionary<string, int>(),
            CardUsesThisTurn: new Dictionary<string, int>(),
            CardUseLockedThisTurn: false,
            OwnedRelicIds: [],
            Potions: [],
            PotionUsesThisTurn: new Dictionary<string, int>(),
            PendingCardPlay: null,
            CharacterSkillsRestricted: false,
            CpuCardPlaysThisTurn: combo,
            IsReady: false);
    }

    private static CombatActorDto ReplayActor(
        string instanceId,
        int slotIndex,
        int currentHp,
        int attackSingle) => new(
            InstanceId: instanceId,
            DefinitionId: "wisp",
            Side: "Ally",
            SlotIndex: slotIndex,
            CurrentHp: currentHp,
            MaxHp: 30,
            BlockDisplay: 0,
            AttackSingleDisplay: attackSingle,
            AttackRandomDisplay: 0,
            AttackAllDisplay: 0,
            Statuses: new Dictionary<string, int>(),
            CurrentMoveId: null,
            RemainingLifetimeTurns: null,
            AssociatedSummonHeldInstanceId: null,
            Intent: new IntentDto(
                attackSingle == 0 ? null : attackSingle,
                null,
                null,
                attackSingle == 0 ? null : 1,
                attackSingle == 0 ? null : 1,
                null,
                null,
                null,
                false,
                false,
                false));
}
