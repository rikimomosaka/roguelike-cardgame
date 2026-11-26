using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>カード使用時の位相の判定（効果の前）と双極のエナジー。</summary>
public sealed class PhasePlayCardTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    private static CardDefinition Def(string id, CardType type, int cost, params CardEffect[] effects) =>
        new(id, id, null, CardRarity.Common, type, Cost: cost, UpgradedCost: null,
            Effects: effects, UpgradedEffects: null, Keywords: null);

    private static readonly CardDefinition CalmBlade = Def("calm_blade", CardType.Attack, 1,
        new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 7),
        new CardEffect("setPhase", EffectScope.Self, null, 0, Name: "calm"));
    private static readonly CardDefinition MotionForm = Def("motion_form", CardType.Skill, 1,
        new CardEffect("setPhase", EffectScope.Self, null, 0, Name: "motion"));
    private static readonly CardDefinition FlowingStance = Def("flowing_stance", CardType.Skill, 0,
        new CardEffect("invertPhase", EffectScope.Self, null, 0));
    private static readonly CardDefinition ReturnEdge = Def("return_edge", CardType.Attack, 1,
        new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6),
        new CardEffect("invertPhase", EffectScope.Self, null, 0));
    private static readonly CardDefinition Honing = Def("honing", CardType.Skill, 0,
        new CardEffect("augmentCards", EffectScope.Self, null, 1, Name: "bipolar",
            Pile: "hand", Select: "choose", CardCount: 1, CardTypeFilter: "Attack"));

    private static DataCatalog Catalog() => BattleFixtures.MinimalCatalog(cards: new[]
    {
        BattleFixtures.Strike(), BattleFixtures.Defend(), CalmBlade, MotionForm, Honing, FlowingStance, ReturnEdge,
    });

    private static BattleCardInstance Card(string defId, string instId) => new(instId, defId, false, null);

    private static BattleState WithHero(ActorPhase phase, params BattleCardInstance[] hand) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = phase }),
            hand: ImmutableArray.Create(hand));

    private static string[] PhaseNotes(System.Collections.Generic.IEnumerable<BattleEvent> events) =>
        events.Where(e => e.Kind == BattleEventKind.PhaseChanged).Select(e => e.Note!).ToArray();

    [Fact]
    public void CalmHero_PlaysAttackThatSetsCalm_KeepsTheNewCalm()
    {
        // 判定で静は消える予定になるが、効果で静を得直したので消えない。
        var (after, events) = BattleEngine.PlayCard(
            WithHero(ActorPhase.Calm, Card("calm_blade", "c1")), 0, 0, 0, Rng(), Catalog());

        Assert.Equal(ActorPhase.Calm, after.Allies[0].Phase);
        Assert.False(after.Allies[0].PhaseExitPending);
        Assert.Empty(PhaseNotes(events));
    }

    [Fact]
    public void MotionHero_PlaysInvertSkill_EndsCalm()
    {
        var (after, events) = BattleEngine.PlayCard(
            WithHero(ActorPhase.Motion, Card("flowing_stance", "f1")), 0, 0, 0, Rng(), Catalog());

        Assert.Equal(ActorPhase.Calm, after.Allies[0].Phase);
        Assert.Equal(new[] { "motion->calm:effect" }, PhaseNotes(events));
    }

    [Fact]
    public void CalmHero_PlaysInvertAttack_EndsMotion()
    {
        var (after, _) = BattleEngine.PlayCard(
            WithHero(ActorPhase.Calm, Card("return_edge", "r1")), 0, 0, 0, Rng(), Catalog());

        Assert.Equal(ActorPhase.Motion, after.Allies[0].Phase);
    }

    [Fact]
    public void CalmHero_PlaysPlainAttack_LosesCalmAfterEffects()
    {
        var (after, events) = BattleEngine.PlayCard(
            WithHero(ActorPhase.Calm, Card("strike", "s1")), 0, 0, 0, Rng(), Catalog());

        Assert.Equal(ActorPhase.None, after.Allies[0].Phase);
        var phaseEvent = events.Single(e => e.Kind == BattleEventKind.PhaseChanged);
        Assert.Equal("calm->none:used", phaseEvent.Note);
        // 消えるのは効果（アタックの予約）の後。
        var attackIndex = events.ToList().FindIndex(e => e.Kind == BattleEventKind.GainAttack);
        Assert.True(attackIndex >= 0 && attackIndex < events.ToList().IndexOf(phaseEvent));
    }

    [Fact]
    public void NoneHero_PlaysMotionSkill_EndsMotion()
    {
        var (after, events) = BattleEngine.PlayCard(
            WithHero(ActorPhase.None, Card("motion_form", "m1")), 0, 0, 0, Rng(), Catalog());

        Assert.Equal(ActorPhase.Motion, after.Allies[0].Phase);
        Assert.Equal(new[] { "none->motion:effect" }, PhaseNotes(events));
    }

    [Fact]
    public void MotionHero_PlaysSkill_BecomesNone()
    {
        var (after, events) = BattleEngine.PlayCard(
            WithHero(ActorPhase.Motion, Card("defend", "d1")), 0, 0, 0, Rng(), Catalog());

        Assert.Equal(ActorPhase.None, after.Allies[0].Phase);
        Assert.Equal(new[] { "motion->none:used" }, PhaseNotes(events));
    }

    [Fact]
    public void MotionHero_PlaysBipolar2Attack_GainsTwoEnergy_AndClearsBipolar()
    {
        var state = WithHero(ActorPhase.Motion, Card("strike", "s1").AddKeyword("bipolar", 2)) with { Energy = 3 };

        var (after, events) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), Catalog());

        Assert.Equal(3 - 1 + 2, after.Energy);
        Assert.Equal(ActorPhase.Motion, after.Allies[0].Phase);
        var played = Assert.Single(after.DiscardPile);
        Assert.Equal(0, played.KeywordCount("bipolar"));
        Assert.Contains(events, e => e.Note == "bipolar" && e.Amount == 2);
    }

    [Fact]
    public void CalmHero_PlaysBipolarAttack_NoEnergy_KeepsBipolar_AndBecomesNone()
    {
        var state = WithHero(ActorPhase.Calm, Card("strike", "s1").AddKeyword("bipolar", 1)) with { Energy = 3 };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), Catalog());

        Assert.Equal(2, after.Energy);
        Assert.Equal(ActorPhase.None, after.Allies[0].Phase);
        Assert.Equal(1, Assert.Single(after.DiscardPile).KeywordCount("bipolar"));
    }

    [Fact]
    public void ChooseResume_DoesNotJudgeTwice()
    {
        var state = WithHero(ActorPhase.Motion,
            Card("honing", "h1"), Card("strike", "s1"), Card("strike", "s2"));

        var (paused, pauseEvents) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), Catalog());
        Assert.NotNull(paused.PendingCardPlay);
        // 選択待ちの間は効果処理の途中なので、まだ消えない。
        Assert.Equal(ActorPhase.Motion, paused.Allies[0].Phase);

        var (resolved, resumeEvents) = BattleEngine.ResolveCardChoice(
            paused, ImmutableArray.Create("s1"), Rng(), Catalog());

        Assert.Equal(ActorPhase.None, resolved.Allies[0].Phase);
        Assert.Equal(new[] { "motion->none:used" }, PhaseNotes(pauseEvents.Concat(resumeEvents)));
    }

    [Fact]
    public void CardControlledEnemy_PhaseJudgedAtReplay_NotAtPlanning()
    {
        const string enemyId = "phase_goblin";
        var enemyStrike = Def("e_phase_strike", CardType.Attack, 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 3));
        var enemyDef = new EnemyDefinition(enemyId, "Phase Goblin", "img", 30, new EnemyPool(1, EnemyTier.Weak),
            InitialMoveId: string.Empty,
            Moves: System.Array.Empty<MoveDefinition>(),
            Deck: new[] { new UnitDeckEntry("e_phase_strike") },
            InitialEnergy: 0, EnergyPerTurn: 1, DrawPerTurn: 1, HandLimit: 5,
            AiProfileId: UnitAiProfileIds.LowCostCombo);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), enemyStrike },
            enemies: new[] { enemyDef },
            encounters: new[] { new EncounterDefinition("enc_phase", new EnemyPool(1, EnemyTier.Weak), new[] { enemyId }) });
        var source = new CombatSource(CombatSourceKind.CardEffect, null, "goblin_inst_0", "goblin_inst_0",
            DefinitionId: "e_phase_strike", InstanceId: "e_card_0");
        var enemy = BattleFixtures.Goblin(hp: 30) with
        {
            DefinitionId = enemyId,
            CurrentMoveId = null,
            Phase = ActorPhase.Calm,
            PlannedEffects = ImmutableArray.Create(
                new PlannedCardEffect(enemyStrike.Effects[0], source)),
        };
        var state = BattleFixtures.MinimalState(enemies: ImmutableArray.Create(enemy))
            with { Phase = BattlePhase.EnemyAttacking };

        Assert.Equal(ActorPhase.Calm, state.Enemies[0].Phase);
        var (after, events) = EnemyAttackingResolver.Resolve(state, Rng(), catalog);

        Assert.Equal(ActorPhase.None, after.Enemies[0].Phase);
        Assert.Equal(new[] { "calm->none:used" }, PhaseNotes(events));
    }
}
