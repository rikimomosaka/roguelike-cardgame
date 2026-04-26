using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineStartTests
{
    private static RunState MakeRun(params string[] deck)
    {
        // hero hp=70 / max=70 のシンプルなラン
        var deckArr = deck.Select(id => new CardInstance(id, false)).ToImmutableArray();
        return new RunState(
            SchemaVersion: RunState.CurrentSchemaVersion,
            CurrentAct: 1, CurrentNodeId: 0,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
            CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
            Deck: deckArr, Potions: ImmutableArray<string>.Empty, PotionSlotCount: 0,
            ActiveBattle: null, ActiveReward: null,
            EncounterQueueWeak: ImmutableArray<string>.Empty,
            EncounterQueueStrong: ImmutableArray<string>.Empty,
            EncounterQueueElite: ImmutableArray<string>.Empty,
            EncounterQueueBoss: ImmutableArray<string>.Empty,
            RewardRngState: new RoguelikeCardGame.Core.Rewards.RewardRngState(0, 0),
            ActiveMerchant: null, ActiveEvent: null,
            ActiveRestPending: false, ActiveRestCompleted: false,
            Relics: System.Array.Empty<string>(),
            PlaySeconds: 0, RngSeed: 1,
            SavedAtUtc: System.DateTimeOffset.UtcNow,
            Progress: RunProgress.InProgress,
            RunId: "run1", ActiveActStartRelicChoice: null,
            SeenCardBaseIds: ImmutableArray<string>.Empty,
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<RoguelikeCardGame.Core.Run.JourneyEntry>.Empty);
    }

    private static IRng Rng() => new FakeRng(new int[200], new double[0]);

    [Fact] public void Builds_hero_at_slot_zero_with_run_hp()
    {
        var run = MakeRun("strike", "defend");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal("hero", s.Allies[0].DefinitionId);
        Assert.Equal(0, s.Allies[0].SlotIndex);
        Assert.Equal(70, s.Allies[0].CurrentHp);
        Assert.Equal(70, s.Allies[0].MaxHp);
    }

    [Fact] public void Builds_enemies_from_encounter()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Single(s.Enemies);
        Assert.Equal("goblin", s.Enemies[0].DefinitionId);
        Assert.Equal("swing", s.Enemies[0].CurrentMoveId);
    }

    [Fact] public void Copies_deck_and_draws_five()
    {
        var run = MakeRun("strike", "strike", "strike", "strike", "strike", "strike", "strike", "defend");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(5, s.Hand.Length);
        Assert.Equal(3, s.DrawPile.Length); // 8 - 5
    }

    [Fact] public void Initial_state_is_PlayerInput_pending_turn1()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(BattlePhase.PlayerInput, s.Phase);
        Assert.Equal(BattleOutcome.Pending, s.Outcome);
        // 注: Start で Turn=0 で初期化 → TurnStartProcessor が +1 して Turn=1
        Assert.Equal(1, s.Turn);
    }

    [Fact] public void Initial_target_indices_are_zero()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(0, s.TargetAllyIndex);
        Assert.Equal(0, s.TargetEnemyIndex);
    }

    [Fact] public void Energy_initial_is_three()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(3, s.Energy);
        Assert.Equal(3, s.EnergyMax);
    }

    [Fact] public void EncounterId_set_correctly()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal("enc_test", s.EncounterId);
    }

    [Fact] public void Start_initializes_combo_fields_to_default()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Equal(0, s.ComboCount);
        Assert.Null(s.LastPlayedOrigCost);
        Assert.False(s.NextCardComboFreePass);
    }

    [Fact] public void Start_initializes_summon_held_and_power_cards_to_empty()
    {
        var run = MakeRun("strike");
        var cat = BattleFixtures.MinimalCatalog();
        var s = BattleEngine.Start(run, "enc_test", Rng(), cat);
        Assert.Empty(s.SummonHeld);
        Assert.Empty(s.PowerCards);
    }
}
