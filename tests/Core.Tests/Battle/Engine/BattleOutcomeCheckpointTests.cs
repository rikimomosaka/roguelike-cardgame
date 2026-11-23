using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class BattleOutcomeCheckpointTests
{
    private static IRng Rng() => new FakeRng([], []);

    [Fact]
    public void Lethal_self_damage_during_participant_card_play_resolves_defeat_immediately()
    {
        var definition = SelfDamageCard("fatal-choice", amount: 5);
        var card = BattleFixtures.MakeBattleCard(definition.Id, "fatal-card");
        var hero = BattleFixtures.Hero(currentHp: 1, maxHp: 20);
        var state = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                allies: ImmutableArray.Create(hero),
                hand: ImmutableArray.Create(card),
                energy: 0));
        string participantId = BattleParticipantAccess.GetPrimary(state).ParticipantId;

        var (next, events) = BattleEngine.PlayCard(
            state, participantId, handIndex: 0,
            targetEnemyIndex: 0, targetAllyIndex: 0,
            Rng(), BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.False(next.Allies[0].IsAlive);
        Assert.True(BattleParticipantAccess.GetPrimary(next).IsReady);
        Assert.Null(next.PendingCardPlay);
        Assert.Null(BattleParticipantAccess.GetPrimary(next).PendingCardPlay);
        var battleEnd = Assert.Single(events, e => e.Kind == BattleEventKind.BattleEnd);
        Assert.Equal(events.Count - 1, battleEnd.Order);
    }

    [Fact]
    public void Revive_during_self_damage_keeps_battle_pending()
    {
        var definition = SelfDamageCard("revived-choice", amount: 5);
        var card = BattleFixtures.MakeBattleCard(definition.Id, "revived-card");
        var hero = BattleFixtures.Hero(currentHp: 1, maxHp: 20) with
        {
            Statuses = BattleFixtures.Hero().Statuses
                .SetItem(BattleStatusIds.Revive, 4),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            hand: ImmutableArray.Create(card),
            energy: 0);

        var (next, events) = BattleEngine.PlayCard(
            state, handIndex: 0,
            targetEnemyIndex: 0, targetAllyIndex: 0,
            Rng(), BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(BattleOutcome.Pending, next.Outcome);
        Assert.Equal(BattlePhase.PlayerInput, next.Phase);
        Assert.Equal(4, next.Allies[0].CurrentHp);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.BattleEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lethal_OnTurnStart_relic_or_power_resolves_defeat(
        bool usePower)
    {
        var fatalEffect = new CardEffect(
            "selfDamage", EffectScope.Self, null, 5,
            Trigger: "OnTurnStart");
        var fatalPower = new CardDefinition(
            Id: "fatal-start-power",
            Name: "fatal-start-power",
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Power,
            Cost: 0,
            UpgradedCost: null,
            Effects: [fatalEffect],
            UpgradedEffects: null,
            Keywords: null);
        var fatalRelic = BattleFixtures.Relic(
            "fatal-start-relic", "OnTurnStart", true,
            new CardEffect("selfDamage", EffectScope.Self, null, 5));
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(currentHp: 1, maxHp: 20)]) with
        {
            OwnedRelicIds = usePower ? [] : [fatalRelic.Id],
            PowerCards = usePower
                ? [BattleFixtures.MakeBattleCard(fatalPower.Id, "fatal-power-instance")]
                : [],
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [fatalPower], relics: [fatalRelic]);

        var (next, events) = TurnStartProcessor.Process(state, Rng(), catalog);

        Assert.Equal(BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.False(next.Allies[0].IsAlive);
        Assert.DoesNotContain(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.TurnStart);
        Assert.Equal(
            events.Count - 1,
            Assert.Single(events, battleEvent =>
                battleEvent.Kind == BattleEventKind.BattleEnd).Order);
    }

    [Fact]
    public void Revive_during_OnTurnStart_trigger_keeps_battle_pending()
    {
        var fatalRelic = BattleFixtures.Relic(
            "revived-start-relic", "OnTurnStart", true,
            new CardEffect("selfDamage", EffectScope.Self, null, 5));
        var hero = BattleFixtures.Hero(currentHp: 1, maxHp: 20) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Revive, 4),
        };
        var idleEnemy = BattleFixtures.Goblin() with { CurrentMoveId = null };
        var state = BattleFixtures.MinimalState(
            allies: [hero], enemies: [idleEnemy]) with
        {
            OwnedRelicIds = [fatalRelic.Id],
        };

        var (next, events) = TurnStartProcessor.Process(
            state, Rng(),
            BattleFixtures.MinimalCatalog(relics: [fatalRelic]));

        Assert.Equal(BattleOutcome.Pending, next.Outcome);
        Assert.Equal(BattlePhase.PlayerInput, next.Phase);
        Assert.Equal(4, next.Allies[0].CurrentHp);
        Assert.Contains(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.TurnStart);
        Assert.DoesNotContain(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.BattleEnd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lethal_OnTurnEnd_trigger_resolves_before_next_turn(
        bool hasRevive)
    {
        var fatalRelic = BattleFixtures.Relic(
            "fatal-turn-end", "OnTurnEnd", true,
            new CardEffect("selfDamage", EffectScope.Self, null, 5));
        var hero = BattleFixtures.Hero(currentHp: 1, maxHp: 20);
        if (hasRevive)
        {
            hero = hero with
            {
                Statuses = ImmutableDictionary<string, int>.Empty
                    .Add(BattleStatusIds.Revive, 4),
            };
        }
        var idleEnemy = BattleFixtures.Goblin() with { CurrentMoveId = null };
        var state = BattleFixtures.MinimalState(
            allies: [hero], enemies: [idleEnemy]) with
        {
            OwnedRelicIds = [fatalRelic.Id],
        };

        var (next, events) = BattleEngine.EndTurn(
            state, Rng(),
            BattleFixtures.MinimalCatalog(relics: [fatalRelic]));

        if (hasRevive)
        {
            Assert.Equal(BattleOutcome.Pending, next.Outcome);
            Assert.Equal(BattlePhase.PlayerInput, next.Phase);
            Assert.Equal(4, next.Allies[0].CurrentHp);
            Assert.Contains(events, battleEvent =>
                battleEvent.Kind == BattleEventKind.TurnStart);
            Assert.DoesNotContain(events, battleEvent =>
                battleEvent.Kind == BattleEventKind.BattleEnd);
        }
        else
        {
            Assert.Equal(BattleOutcome.Defeat, next.Outcome);
            Assert.Equal(BattlePhase.Resolved, next.Phase);
            Assert.DoesNotContain(events, battleEvent =>
                battleEvent.Kind == BattleEventKind.TurnStart);
            Assert.Equal(
                events.Count - 1,
                Assert.Single(events, battleEvent =>
                    battleEvent.Kind == BattleEventKind.BattleEnd).Order);
        }
    }

    [Fact]
    public void Mutual_knockout_is_defeat_even_when_the_last_enemy_also_dies()
    {
        var hero = BattleFixtures.Hero(currentHp: 1, maxHp: 20) with
        {
            AttackSingle = AttackPool.Empty.Add(1),
        };
        var enemy = BattleFixtures.Goblin(hp: 1) with
        {
            Statuses = BattleFixtures.Goblin().Statuses
                .SetItem(BattleStatusIds.Reflect, 1),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(enemy),
            draw: Enumerable.Range(0, 5)
                .Select(index => BattleFixtures.MakeBattleCard(
                    "defend", $"draw-{index}"))
                .ToImmutableArray());

        var (next, events) = BattleEngine.EndTurn(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.False(next.Allies[0].IsAlive);
        Assert.False(next.Enemies[0].IsAlive);
        Assert.Equal(BattleOutcome.Defeat, next.Outcome);
        Assert.Equal("Defeat", Assert.Single(
            events, e => e.Kind == BattleEventKind.BattleEnd).Note);
    }

    [Fact]
    public void Planning_command_removes_a_dead_summon_returns_its_held_card_and_reuses_the_slot()
    {
        var selfDamage = SelfDamageCard("fragile-action", amount: 5);
        var replacementUnit = BattleFixtures.MinionDef("replacement");
        var summonReplacement = new CardDefinition(
            Id: "summon-replacement",
            Name: "summon-replacement",
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Unit,
            Cost: 0,
            UpgradedCost: null,
            Effects: [new CardEffect(
                "summon", EffectScope.Self, null, 0,
                UnitId: replacementUnit.Id)],
            UpgradedEffects: null,
            Keywords: null);
        var held = BattleFixtures.MakeBattleCard(
            "summon-fragile", "held-fragile");
        var fragile = BattleFixtures.SummonActor(
            "fragile-instance", "fragile", slotIndex: 1,
            hp: 1, associatedCardId: held.InstanceId) with
        {
            OwnerActorInstanceId = "hero_inst",
            OwnerPlayerSlot = 0,
        };
        var heroCard = BattleFixtures.MakeBattleCard(
            summonReplacement.Id, "summon-replacement-instance");
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), fragile],
            hand: [heroCard],
            energy: 0) with
        {
            SummonHeld = [held],
        };
        state = BattleParticipantAccess.InitializePrimary(state);
        var primary = BattleParticipantAccess.GetPrimary(state);
        var cpu = primary with
        {
            ParticipantId = "cpu:fragile-instance",
            ActorInstanceId = fragile.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            CharacterId = fragile.DefinitionId,
            TargetAllyIndex = 1,
            Energy = 0,
            DrawPile = [],
            Hand = [BattleFixtures.MakeBattleCard(
                selfDamage.Id, "fragile-action-instance")],
            DiscardPile = [],
            ExhaustPile = [],
            SummonHeld = [],
            PowerCards = [],
            IsReady = false,
        };
        state = BattleParticipantAccess.Add(state, cpu);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [selfDamage, summonReplacement],
            units: [replacementUnit]);

        (state, _) = BattleEngine.PlayCard(
            state, cpu.ParticipantId, handIndex: 0,
            targetEnemyIndex: 0, targetAllyIndex: 1,
            Rng(), catalog);

        Assert.DoesNotContain(state.Allies, actor =>
            actor.InstanceId == fragile.InstanceId);
        Assert.DoesNotContain(state.Participants, participant =>
            participant.ParticipantId == cpu.ParticipantId);
        var ownerAfterDeath = BattleParticipantAccess.GetPrimary(state);
        Assert.Empty(ownerAfterDeath.SummonHeld);
        Assert.Equal(held.InstanceId, Assert.Single(
            ownerAfterDeath.DiscardPile).InstanceId);

        (state, _) = BattleEngine.PlayCard(
            state, primary.ParticipantId, handIndex: 0,
            targetEnemyIndex: 0, targetAllyIndex: 0,
            new FakeRng([0], []), catalog);

        var replacement = Assert.Single(state.Allies, actor =>
            actor.DefinitionId == replacementUnit.Id);
        Assert.Equal(1, replacement.SlotIndex);
    }

    private static CardDefinition SelfDamageCard(string id, int amount) => new(
        Id: id,
        Name: id,
        DisplayName: null,
        Rarity: CardRarity.Common,
        CardType: CardType.Skill,
        Cost: 0,
        UpgradedCost: null,
        Effects: [new CardEffect("selfDamage", EffectScope.Self, null, amount)],
        UpgradedEffects: null,
        Keywords: null);
}
