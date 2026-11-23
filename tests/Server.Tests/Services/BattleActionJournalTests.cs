using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Tests.Services;

public sealed class BattleActionJournalTests
{
    [Fact]
    public void Append_is_immutable_and_assigns_one_global_monotonic_sequence()
    {
        var mutableCostChanges = new List<BattleCardCostChangeSnapshot>
        {
            new("modifier", -1, "temperance", "節制"),
        };
        var mutableTargets = new List<string> { "enemy-1" };
        var first = Draft(
            commandId: "command-1",
            participantId: "player-1",
            actorInstanceId: "hero-1",
            card: new BattleCardDecisionSnapshot(
                CardInstanceId: "card-instance-1",
                DefinitionId: "strike",
                DisplayName: "ストライク",
                CardType: "attack",
                Rarity: "starter",
                IsUpgraded: false,
                OriginalCost: 1,
                CurrentCost: 0,
                PaidCost: 0,
                Description: "敵単体に6アタック。",
                CostChanges: mutableCostChanges),
            targets: mutableTargets);

        var afterFirst = BattleActionJournal.Empty.AppendSuccessfulCommand(first);
        var afterSecond = afterFirst.AppendSuccessfulCommand(Draft(
            commandId: "command-2",
            participantId: "cpu-wisp-1",
            actorInstanceId: "wisp-1",
            revisionBefore: 1,
            revisionAfter: 2));

        mutableCostChanges.Add(new("combo", -1, null, "コンボ"));
        mutableTargets.Add("enemy-2");

        Assert.Empty(BattleActionJournal.Empty.Entries);
        Assert.Single(afterFirst.Entries);
        Assert.Equal(1, afterFirst.Entries[0].Sequence);
        Assert.Equal(2, afterSecond.Entries[1].Sequence);
        Assert.Equal(2, afterSecond.LastSequence);
        Assert.Equal(3, afterSecond.NextSequence);
        Assert.Single(afterFirst.Entries[0].Card!.CostChanges!);
        Assert.Single(afterFirst.Entries[0].TargetActorInstanceIds);
    }

    [Fact]
    public void Query_filters_source_and_inbound_actions_by_turn_participant_actor_and_sequence()
    {
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommands(
        [
            Draft(
                commandId: "p1-attacks-p2",
                participantId: "p1",
                actorInstanceId: "actor-1",
                effects:
                [
                    new BattleActionEffectSnapshot(
                        "status", "actor-1", "p2", "actor-2", 1, "strength"),
                    new BattleActionEffectSnapshot(
                        "status", "actor-1", "p3", "actor-3", 2, "dexterity"),
                ],
                intents:
                [
                    new BattleActionIntentSnapshot(
                        "attack", "single", 6, 1, "p2", "actor-2"),
                ]),
            Draft(
                commandId: "p2-blocks",
                participantId: "p2",
                actorInstanceId: "actor-2",
                revisionBefore: 1,
                revisionAfter: 2),
            Draft(
                commandId: "next-turn",
                participantId: "p2",
                actorInstanceId: "actor-2",
                turn: 2,
                revisionBefore: 2,
                revisionAfter: 3),
        ]);

        Assert.Equal(
            ["p1-attacks-p2", "p2-blocks"],
            journal.Query(new BattleActionFilter(Turn: 1, ParticipantId: "p2"))
                .Select(entry => entry.CommandId));
        Assert.Equal(
            ["p1-attacks-p2", "p2-blocks", "next-turn"],
            journal.Query(new BattleActionFilter(ActorInstanceId: "actor-2"))
                .Select(entry => entry.CommandId));
        Assert.Equal(
            ["p2-blocks", "next-turn"],
            journal.Query(new BattleActionFilter(
                    ParticipantId: "p2",
                    AfterSequence: 1))
                .Select(entry => entry.CommandId));
        Assert.Single(journal.Query(new BattleActionFilter(
            ParticipantId: "p2",
            ActorInstanceId: "actor-2",
            ThroughSequence: 1)));
    }

    [Fact]
    public void Feed_changes_track_ready_unready_and_complete_without_mutating_old_journal()
    {
        var live = BattleActionJournal.Empty.AppendSuccessfulCommand(Draft(
            commandId: "play",
            participantId: "p1",
            actorInstanceId: "actor-1"));
        var ready = live.AppendSuccessfulCommand(Draft(
            commandId: "ready",
            participantId: "p1",
            actorInstanceId: "actor-1",
            kind: "readyChanged",
            revisionBefore: 1,
            revisionAfter: 2,
            feedChanges: [new BattleReplayFeedChange("p1", IsReady: true)]));
        var reopened = ready.AppendSuccessfulCommand(Draft(
            commandId: "new-legal-action",
            participantId: "p1",
            actorInstanceId: "actor-1",
            revisionBefore: 2,
            revisionAfter: 3,
            feedChanges: [new BattleReplayFeedChange("p1", IsReady: false)]));
        var complete = reopened.AppendSuccessfulCommand(Draft(
            commandId: "resolution-complete",
            participantId: null,
            actorInstanceId: null,
            kind: "turnCompleted",
            revisionBefore: 3,
            revisionAfter: 4,
            feedChanges:
            [
                new BattleReplayFeedChange("p1", IsComplete: true),
                new BattleReplayFeedChange("p2", IsComplete: true),
            ],
            isResolution: true));

        Assert.False(live.GetFeedState(1, "p1").IsReady);
        Assert.True(ready.GetFeedState(1, "p1").IsReady);
        Assert.False(reopened.GetFeedState(1, "p1").IsReady);
        Assert.True(complete.GetFeedState(1, "p1").IsReady);
        Assert.True(complete.GetFeedState(1, "p1").IsComplete);
        Assert.True(complete.GetFeedState(1, "p2").IsComplete);
        Assert.Equal(4, complete.GetFeedState(1, "p1").LastSequence);
    }

    [Fact]
    public void External_correlation_ids_can_repeat_but_internal_entry_ids_are_unique()
    {
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(Draft(
            commandId: "same-command",
            participantId: "p1",
            actorInstanceId: "actor-1"));

        var repeated = journal.AppendSuccessfulCommand(Draft(
                commandId: "same-command",
                participantId: "p1",
                actorInstanceId: "actor-1",
                revisionBefore: 1,
                revisionAfter: 2));

        Assert.Equal(2, repeated.Entries.Length);
        Assert.All(repeated.Entries, entry =>
            Assert.Equal("same-command", entry.CommandId));
        Assert.Equal(2, repeated.Entries.Select(entry => entry.EntryId).Distinct().Count());
        Assert.Equal("battle-action:1", repeated.Entries[0].EntryId);
        Assert.Equal("battle-action:2", repeated.Entries[1].EntryId);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BattleActionJournal.Empty.AppendSuccessfulCommand(Draft(
                commandId: "bad-revision",
                participantId: "p1",
                actorInstanceId: "actor-1",
                revisionBefore: 4,
                revisionAfter: 3)));
    }

    [Fact]
    public void Append_rejects_revision_regression_across_existing_and_batched_entries()
    {
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(Draft(
            commandId: "revision-4",
            participantId: "p1",
            actorInstanceId: "actor-1",
            revisionBefore: 3,
            revisionAfter: 4));

        Assert.Throws<InvalidOperationException>(() =>
            journal.AppendSuccessfulCommand(Draft(
                commandId: "revision-regressed",
                participantId: "p1",
                actorInstanceId: "actor-1",
                revisionBefore: 1,
                revisionAfter: 2)));
        Assert.Throws<InvalidOperationException>(() =>
            BattleActionJournal.Empty.AppendSuccessfulCommands(
            [
                Draft("batch-first", "p1", "actor-1", revisionBefore: 0,
                    revisionAfter: 2),
                Draft("batch-regressed", "p1", "actor-1", revisionBefore: 1,
                    revisionAfter: 3),
            ]));
    }

    [Fact]
    public void Query_includes_source_actor_and_all_audience_for_shared_resolution()
    {
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(Draft(
            commandId: "shared-resolution",
            participantId: "last-ready-human",
            actorInstanceId: "human-actor",
            kind: "resolveTurn",
            effects:
            [
                new BattleActionEffectSnapshot(
                    "DealDamage", "cpu-actor", null, "enemy-1", 7),
            ],
            audience: ["earlier-ready-human", "cpu-participant"],
            isResolution: true));

        Assert.Single(journal.Query(new BattleActionFilter(
            ParticipantId: "earlier-ready-human")));
        Assert.Single(journal.Query(new BattleActionFilter(
            ParticipantId: "cpu-participant",
            ActorInstanceId: "cpu-actor")));
        Assert.Empty(journal.Query(new BattleActionFilter(
            ParticipantId: "not-in-battle")));
    }

    [Fact]
    public void Query_does_not_expose_action_to_unrelated_participant()
    {
        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(Draft(
            commandId: "targeted-action",
            participantId: "source",
            actorInstanceId: "source-actor",
            effects:
            [
                new BattleActionEffectSnapshot(
                    "ApplyStatus", "source-actor", "target", "target-actor", 1),
            ],
            audience: ["target"]));

        Assert.Single(journal.Query(new BattleActionFilter(ParticipantId: "target")));
        Assert.Empty(journal.Query(new BattleActionFilter(ParticipantId: "unrelated")));
    }

    private static BattleActionDraft Draft(
        string commandId,
        string? participantId,
        string? actorInstanceId,
        int turn = 1,
        string kind = "cardPlayed",
        long revisionBefore = 0,
        long revisionAfter = 1,
        BattleCardDecisionSnapshot? card = null,
        IReadOnlyList<string>? targets = null,
        IReadOnlyList<BattleActionEffectSnapshot>? effects = null,
        IReadOnlyList<BattleActionIntentSnapshot>? intents = null,
        IReadOnlyList<string>? audience = null,
        IReadOnlyList<BattleReplayFeedChange>? feedChanges = null,
        bool isResolution = false) =>
        new(
            Turn: turn,
            CommandId: commandId,
            Kind: kind,
            ParticipantId: participantId,
            ActorInstanceId: actorInstanceId,
            RevisionBefore: revisionBefore,
            RevisionAfter: revisionAfter,
            Card: card,
            TargetActorInstanceIds: targets,
            AudienceParticipantIds: audience,
            Effects: effects,
            Intents: intents,
            FeedChanges: feedChanges,
            IsResolution: isResolution);
}
