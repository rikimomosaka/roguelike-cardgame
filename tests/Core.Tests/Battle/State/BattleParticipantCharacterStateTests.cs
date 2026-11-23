using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public sealed class BattleParticipantCharacterStateTests
{
    [Fact]
    public void Projection_and_commit_preserve_trait_usage_and_reprise_state_per_participant()
    {
        var heroOne = BattleFixtures.Hero() with
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = "hero_inst",
        };
        var heroTwo = BattleFixtures.Hero(slotIndex: 1) with
        {
            InstanceId = "hero_two",
            OwnerPlayerSlot = 1,
            OwnerActorInstanceId = "hero_two",
        };
        var root = EmptyState(heroOne, heroTwo);
        root = BattleParticipantAccess.InitializePrimary(root);

        var primarySnapshot = Snapshot("primary-card", 1);
        var primary = BattleParticipantAccess.GetPrimary(root) with
        {
            RepriseEligibleHistory = ImmutableArray.Create(primarySnapshot),
            NextEffectResolutionSequence = 2,
            CharacterTraitUsesThisTurn =
                ImmutableDictionary<string, int>.Empty.Add("primary-trait", 1),
        };
        root = BattleParticipantAccess.Replace(root, primary);

        var secondarySnapshot = Snapshot("secondary-card", 8);
        var secondary = primary with
        {
            ParticipantId = "player:1",
            ActorInstanceId = heroTwo.InstanceId,
            OwnerPlayerSlot = 1,
            CharacterId = "secondary",
            RepriseEligibleHistory = ImmutableArray.Create(secondarySnapshot),
            NextEffectResolutionSequence = 9,
            CharacterTraitUsesThisTurn =
                ImmutableDictionary<string, int>.Empty.Add("secondary-trait", 2),
        };
        root = BattleParticipantAccess.Add(root, secondary);

        var projected = BattleParticipantAccess.ProjectToRoot(root, "player:1");

        Assert.Equal(secondarySnapshot, Assert.Single(projected.RepriseEligibleHistory));
        Assert.Equal(9, projected.NextEffectResolutionSequence);
        Assert.Equal(2, projected.CharacterTraitUsesThisTurn["secondary-trait"]);

        projected = projected with
        {
            NextEffectResolutionSequence = 10,
            CharacterTraitUsesThisTurn =
                projected.CharacterTraitUsesThisTurn.SetItem("secondary-trait", 3),
        };
        var committed = BattleParticipantAccess.CommitRootToParticipant(
            projected, "player:1");

        Assert.Equal(2, committed.NextEffectResolutionSequence);
        Assert.Equal(1, committed.CharacterTraitUsesThisTurn["primary-trait"]);
        Assert.Equal(10, committed.Participants[1].NextEffectResolutionSequence);
        Assert.Equal(
            3,
            committed.Participants[1]
                .CharacterTraitUsesThisTurn["secondary-trait"]);
    }

    private static BattleState EmptyState(
        CombatActor heroOne,
        CombatActor heroTwo) => new(
        Turn: 1,
        Phase: BattlePhase.PlayerInput,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(heroOne, heroTwo),
        Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
        TargetAllyIndex: 0,
        TargetEnemyIndex: 0,
        Energy: 3,
        EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    private static PlayedCardEffectSnapshot Snapshot(string id, long sequence) =>
        new(
            CardDefinitionId: id,
            CardInstanceId: $"{id}-instance",
            ActorInstanceId: "hero_inst",
            CardType: CardType.Skill,
            OriginalCost: 1,
            PaidCost: 1,
            IsUpgraded: false,
            Keywords: ImmutableArray<string>.Empty,
            Effects: ImmutableArray<CardEffect>.Empty,
            PlaySequence: sequence);
}
