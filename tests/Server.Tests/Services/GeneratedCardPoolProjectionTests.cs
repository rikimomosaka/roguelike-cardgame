using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Tests.Services;

public sealed class GeneratedCardPoolProjectionTests
{
    private static readonly DataCatalog BaseCatalog =
        EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void Battle_DTO_uses_the_instance_effective_pool_in_every_zone()
    {
        var catalog = CatalogWithPool(CardPoolClass.Guardian);
        var cards = Enumerable.Range(0, 6)
            .Select(index => new BattleCardInstance(
                $"generated-{index}",
                "basic_attack",
                IsUpgraded: false,
                CostOverride: null)
            {
                PoolClassOverride = CardPoolClass.Striker,
            })
            .ToArray();
        var state = State(
            hand: [cards[0]],
            draw: [cards[1]],
            discard: [cards[2]],
            exhaust: [cards[3]],
            summonHeld: [cards[4]],
            powers: [cards[5]]);

        var dto = BattleStateDtoMapper.ToDto(state, catalog);

        var rootCards = dto.Hand
            .Concat(dto.DrawPile)
            .Concat(dto.DiscardPile)
            .Concat(dto.ExhaustPile)
            .Concat(dto.SummonHeld)
            .Concat(dto.PowerCards)
            .ToArray();
        Assert.Equal(6, rootCards.Length);
        Assert.All(rootCards, card => Assert.Equal("Striker", card.PoolClass));

        var participant = Assert.Single(dto.Participants!);
        var participantCards = participant.Hand
            .Concat(participant.DrawPile)
            .Concat(participant.DiscardPile)
            .Concat(participant.ExhaustPile)
            .Concat(participant.SummonHeld)
            .Concat(participant.PowerCards)
            .ToArray();
        Assert.Equal(6, participantCards.Length);
        Assert.All(participantCards, card =>
            Assert.Equal("Striker", card.PoolClass));
    }

    [Fact]
    public void Unknown_definition_DTO_falls_back_to_universal()
    {
        var state = State(hand:
        [
            new BattleCardInstance(
                "unknown-generated", "unknown-definition", false, null),
        ]);

        var dto = BattleStateDtoMapper.ToDto(state, BaseCatalog);

        Assert.Equal("Universal", Assert.Single(dto.Hand).PoolClass);
    }

    [Fact]
    public void Finisher_DTO_uses_the_saved_snapshot_pool_not_current_catalog_pool()
    {
        var catalog = CatalogWithPool(CardPoolClass.Striker);
        var snapshot = Snapshot(CardPoolClass.Guardian);
        var state = State() with
        {
            LastFinisherRecord = [snapshot],
        };

        var dto = BattleStateDtoMapper.ToDto(state, catalog);

        var saved = Assert.Single(dto.FinisherRecord!.Cards).Card;
        Assert.Equal("Guardian", saved.PoolClass);
    }

    [Fact]
    public void Journal_and_replay_preserve_effective_and_reenacted_snapshot_pools()
    {
        var catalog = CatalogWithPool(CardPoolClass.Guardian);
        var playedCard = new BattleCardInstance(
            "journal-card",
            "basic_attack",
            IsUpgraded: false,
            CostOverride: null)
        {
            PoolClassOverride = CardPoolClass.Enhancer,
        };
        var before = State(hand: [playedCard]) with { ActionRevision = 4 };
        var after = State(discard: [playedCard]) with { ActionRevision = 5 };
        var participant = BattleParticipantAccess.GetPrimary(before);
        var snapshot = Snapshot(CardPoolClass.Tactician) with
        {
            CardInstanceId = "reenacted-card",
        };
        var events = new BattleEvent[]
        {
            new(
                BattleEventKind.PlayCard,
                Order: 0,
                CasterInstanceId: participant.ActorInstanceId,
                Amount: 1),
            new(
                BattleEventKind.ReenactCardEffects,
                Order: 1,
                CasterInstanceId: participant.ActorInstanceId,
                CardId: snapshot.CardDefinitionId,
                ReenactedCard: snapshot),
        };

        var draft = BattleActionJournalComposer.CardPlay(
            before,
            after,
            participant.ParticipantId,
            handIndex: 0,
            events,
            catalog,
            commandId: "pool-journal");

        Assert.Equal("Enhancer", draft.Card!.PoolClass);
        Assert.Equal(
            "Enhancer",
            Assert.Single(draft.StateBefore!.Hand).PoolClass);
        var reenacted = Assert.Single(draft.Effects!, effect =>
            effect.Kind == BattleEventKind.ReenactCardEffects.ToString());
        Assert.Equal("Tactician", reenacted.Card!.PoolClass);

        var journal = BattleActionJournal.Empty.AppendSuccessfulCommand(draft);
        var replay = BattleReplayDtoMapper.ToDto(
            journal,
            new BattleActionFilter(
                Turn: before.Turn,
                ParticipantId: participant.ParticipantId,
                ActorInstanceId: participant.ActorInstanceId));
        var replayEntry = Assert.Single(replay.Entries);
        Assert.Equal("Enhancer", replayEntry.Card!.PoolClass);
        Assert.Equal(
            "Tactician",
            Assert.Single(replayEntry.Effects, effect =>
                effect.Kind == BattleEventKind.ReenactCardEffects.ToString())
                .Card!.PoolClass);
    }

    private static DataCatalog CatalogWithPool(CardPoolClass poolClass)
    {
        var definition = BaseCatalog.Cards["basic_attack"] with
        {
            PoolClass = poolClass,
        };
        return BaseCatalog with
        {
            Cards = new Dictionary<string, CardDefinition>(BaseCatalog.Cards)
            {
                [definition.Id] = definition,
            },
        };
    }

    private static PlayedCardEffectSnapshot Snapshot(CardPoolClass poolClass) =>
        new(
            CardDefinitionId: "basic_attack",
            CardInstanceId: "snapshot-basic-attack",
            ActorInstanceId: "hero-1",
            CardType: CardType.Attack,
            OriginalCost: 1,
            PaidCost: 0,
            IsUpgraded: false,
            Keywords: [],
            Effects: BaseCatalog.Cards["basic_attack"].Effects.ToImmutableArray(),
            PlaySequence: 1)
        {
            PoolClass = poolClass,
        };

    private static BattleState State(
        ImmutableArray<BattleCardInstance> hand = default,
        ImmutableArray<BattleCardInstance> draw = default,
        ImmutableArray<BattleCardInstance> discard = default,
        ImmutableArray<BattleCardInstance> exhaust = default,
        ImmutableArray<BattleCardInstance> summonHeld = default,
        ImmutableArray<BattleCardInstance> powers = default) =>
        new(
            Turn: 1,
            Phase: BattlePhase.PlayerInput,
            Outcome: BattleOutcome.Pending,
            Allies: [Actor("hero-1", ActorSide.Ally, 0)],
            Enemies: [Actor("enemy-1", ActorSide.Enemy, 0)],
            TargetAllyIndex: 0,
            TargetEnemyIndex: 0,
            Energy: 3,
            EnergyMax: 3,
            DrawPile: EmptyIfDefault(draw),
            Hand: EmptyIfDefault(hand),
            DiscardPile: EmptyIfDefault(discard),
            ExhaustPile: EmptyIfDefault(exhaust),
            SummonHeld: EmptyIfDefault(summonHeld),
            PowerCards: EmptyIfDefault(powers),
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            OwnedRelicIds: [],
            Potions: [],
            EncounterId: "test");

    private static ImmutableArray<BattleCardInstance> EmptyIfDefault(
        ImmutableArray<BattleCardInstance> cards) =>
        cards.IsDefault ? [] : cards;

    private static CombatActor Actor(string id, ActorSide side, int slot) =>
        new(
            id,
            side == ActorSide.Ally ? "hero" : "enemy",
            side,
            slot,
            CurrentHp: 20,
            MaxHp: 20,
            BlockPool.Empty,
            AttackPool.Empty,
            AttackPool.Empty,
            AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty,
            CurrentMoveId: null,
            RemainingLifetimeTurns: null,
            AssociatedSummonHeldInstanceId: null);
}
