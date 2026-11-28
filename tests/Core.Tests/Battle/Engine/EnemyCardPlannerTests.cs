using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.Projection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// カード制御の敵（docs/proposed-enemy-card-ai-and-character-stats.md §2-2a）。
/// 予約時は予測のみ、敵行動時に「非攻撃 → 攻撃」の順で実行する。
/// </summary>
public class EnemyCardPlannerTests
{
    private const string EnemyId = "card_goblin";

    private static CardDefinition Card(string id, int cost, params CardEffect[] effects) =>
        new(id, id, null, CardRarity.Token, CardType.Skill,
            Cost: cost, UpgradedCost: null,
            Effects: effects, UpgradedEffects: null, Keywords: null);

    private static readonly CardDefinition Strike = Card("e_strike", 1,
        new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 5));
    private static readonly CardDefinition Guard = Card("e_guard", 1,
        new CardEffect("block", EffectScope.Self, null, 4));
    private static readonly CardDefinition Rage = Card("e_rage", 0,
        new CardEffect("buff", EffectScope.Self, null, 2, Name: "strength"));
    private static readonly CardDefinition Think = Card("e_think", 0,
        new CardEffect("drawCards", EffectScope.Self, null, 1));

    private static EnemyDefinition CardGoblin(int energyPerTurn = 2, int drawPerTurn = 3) =>
        new(EnemyId, "Card Goblin", "img", 30, new EnemyPool(1, EnemyTier.Weak),
            InitialMoveId: string.Empty,
            Moves: System.Array.Empty<MoveDefinition>(),
            Deck: new[]
            {
                new UnitDeckEntry("e_strike"),
                new UnitDeckEntry("e_guard"),
                new UnitDeckEntry("e_rage"),
                new UnitDeckEntry("e_think"),
            },
            InitialEnergy: 0,
            EnergyPerTurn: energyPerTurn,
            DrawPerTurn: drawPerTurn,
            HandLimit: 5,
            AiProfileId: UnitAiProfileIds.LowCostCombo);

    private static DataCatalog Catalog(EnemyDefinition? enemy = null) =>
        BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), Strike, Guard, Rage, Think },
            enemies: new[] { enemy ?? CardGoblin() },
            encounters: new[]
            {
                new EncounterDefinition("enc_card", new EnemyPool(1, EnemyTier.Weak), new[] { EnemyId }),
            });

    private static CombatActor EnemyActor() =>
        BattleFixtures.Goblin(hp: 30) with { DefinitionId = EnemyId, CurrentMoveId = null };

    /// <summary>山札の並びを固定した敵参加者を持つ state（シャッフルを経由しない）。</summary>
    private static BattleState StateWithEnemyDeck(params string[] drawPile)
    {
        var enemy = EnemyActor();
        var participant = new BattleParticipantState(
            ParticipantId: EnemyCardPlanner.ParticipantIdPrefix + enemy.InstanceId,
            ActorInstanceId: enemy.InstanceId,
            ControllerKind: BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot: null,
            CharacterId: EnemyId,
            TargetAllyIndex: null,
            TargetEnemyIndex: null,
            Energy: 0,
            EnergyPerTurn: 2,
            DrawPerTurn: 3,
            DrawPile: drawPile
                .Select((id, i) => BattleFixtures.MakeBattleCard(id, $"e_card_{i}"))
                .ToImmutableArray(),
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, MaxComboThisTurn: 0, ComboGauge: 0,
            LastPlayedOrigCost: null, NextCardComboFreePass: false,
            WildUsedInCurrentCombo: false,
            UsedCharacterSkillIdsThisTurn: ImmutableHashSet<string>.Empty,
            CharacterSkillUsesThisTurn: ImmutableDictionary<string, int>.Empty,
            CardUsesThisTurn: ImmutableDictionary<string, int>.Empty,
            CardUseLockedThisTurn: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            PotionUsesThisTurn: ImmutableDictionary<string, int>.Empty,
            PendingCardPlay: null,
            IsReady: true);
        return BattleFixtures.MinimalState(enemies: ImmutableArray.Create(enemy)) with
        {
            EnemyParticipants = ImmutableArray.Create(participant),
        };
    }

    private static IRng Rng() => new FakeRng(new int[200], new double[0]);

    private static BattleParticipantState Participant(BattleState state)
    {
        Assert.True(EnemyCardPlanner.TryGetParticipant(state, state.Enemies[0].InstanceId, out var p));
        return p;
    }

    [Fact]
    public void Planning_reserves_battlefield_effects_without_applying_them()
    {
        var state = StateWithEnemyDeck("e_rage", "e_strike", "e_guard");
        var catalog = Catalog();

        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(state, Rng(), catalog);

        var enemy = planned.Enemies[0];
        Assert.Equal(
            new[] { "buff", "attack", "block" },
            enemy.PlannedEffects.Select(p => p.Effect.Action).ToArray());
        // 予測のみ: バフ・ブロック・攻撃はまだ適用されていない。
        Assert.Equal(0, enemy.GetStatus("strength"));
        Assert.Equal(0, enemy.Block.Sum);
        Assert.Equal(70, planned.Allies[0].CurrentHp);
        // カード使用自体（コスト支払い・カード移動）は予約時に処理済み。
        var participant = Participant(planned);
        Assert.Empty(participant.Hand);
        Assert.Equal(3, participant.DiscardPile.Length);
        // 0コスト(筋力) → 1コスト(攻撃: コンボ軽減で0) → 1コスト(防御: 継続せず1) の支払い。
        Assert.Equal(1, participant.Energy);
        Assert.Equal(
            new[] { "e_rage", "e_strike", "e_guard" },
            participant.DiscardPile.Select(c => c.CardDefinitionId).ToArray());
        // プレイヤー側 root には影響しない。
        Assert.Equal(state.Energy, planned.Energy);
        Assert.Equal(state.Hand, planned.Hand);
    }

    [Fact]
    public void Intent_shows_planned_buff_block_and_attack_including_later_buff()
    {
        var catalog = Catalog();
        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(
            StateWithEnemyDeck("e_strike", "e_rage", "e_guard"), Rng(), catalog);

        var intent = CombatIntentProjector.Project(planned, planned.Enemies[0], catalog);

        Assert.NotNull(intent);
        // 攻撃の後に使ったバフも攻撃前に反映される予定として表示する。
        Assert.Equal(7, intent!.AttackAll);
        Assert.Equal(4, intent.Block);
        Assert.True(intent.HasBuff);
    }

    [Fact]
    public void Enemy_action_applies_non_attack_effects_before_attacks_and_clears_plan()
    {
        var catalog = Catalog();
        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(
            StateWithEnemyDeck("e_strike", "e_rage", "e_guard"), Rng(), catalog);

        var (after, _) = EnemyAttackingResolver.Resolve(
            planned with { Phase = BattlePhase.EnemyAttacking }, Rng(), catalog);

        var enemy = after.Enemies[0];
        Assert.Equal(70 - 7, after.Allies[0].CurrentHp);
        Assert.Equal(2, enemy.GetStatus("strength"));
        Assert.Equal(4, enemy.Block.Sum);
        Assert.Empty(enemy.PlannedEffects);
    }

    [Fact]
    public void One_draw_enemy_acts_twice_only_after_drawing_a_two_card_skill()
    {
        // 雑魚は 1 ドローで 1 行動。2 ドローのスキルを引いた時だけ、引いた 2 枚も使って 2 回行動する。
        var prepare = Card("e_prepare", 0,
            new CardEffect("draw", EffectScope.Self, null, 2)) with { UsesPerTurn = 1 };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), Strike, Guard, Rage, Think, prepare },
            enemies: new[] { CardGoblin(energyPerTurn: 2, drawPerTurn: 1) },
            encounters: new[]
            {
                new EncounterDefinition("enc_card", new EnemyPool(1, EnemyTier.Weak), new[] { EnemyId }),
            });
        BattleState WithDrawOne(BattleState state) => state with
        {
            EnemyParticipants = state.EnemyParticipants.SetItem(
                0, state.EnemyParticipants[0] with { DrawPerTurn = 1 }),
        };

        var (plain, _) = EnemyCardPlanner.StartTurnAndPlan(
            WithDrawOne(StateWithEnemyDeck("e_strike", "e_guard", "e_prepare")), Rng(), catalog);
        Assert.Equal(new[] { "attack" },
            plain.Enemies[0].PlannedEffects.Select(planned => planned.Effect.Action));

        var (doubled, _) = EnemyCardPlanner.StartTurnAndPlan(
            WithDrawOne(StateWithEnemyDeck("e_prepare", "e_strike", "e_guard")), Rng(), catalog);
        Assert.Equal(
            new[] { "attack", "block" },
            doubled.Enemies[0].PlannedEffects.Select(planned => planned.Effect.Action).OrderBy(x => x));
    }

    [Fact]
    public void Enemy_action_announces_each_card_before_its_effects()
    {
        var catalog = Catalog();
        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(
            StateWithEnemyDeck("e_strike", "e_rage", "e_guard"), Rng(), catalog);

        var (_, events) = EnemyAttackingResolver.Resolve(
            planned with { Phase = BattlePhase.EnemyAttacking }, Rng(), catalog);

        var announcements = events
            .Where(battleEvent => battleEvent.Kind == BattleEventKind.EnemyCardAction)
            .ToArray();
        // 3 枚のカードそれぞれの名前が演出用に告知され、効果より先に並ぶ。
        Assert.Equal(
            new[] { "e_guard", "e_rage", "e_strike" }.OrderBy(id => id),
            announcements.Select(battleEvent => battleEvent.CardId!).OrderBy(id => id));
        Assert.All(announcements, battleEvent =>
            Assert.Equal(planned.Enemies[0].InstanceId, battleEvent.CasterInstanceId));
        var firstAnnouncement = events.ToList().IndexOf(announcements[0]);
        var firstEffect = events.ToList().FindIndex(battleEvent =>
            battleEvent.Kind is BattleEventKind.GainBlock
                or BattleEventKind.ApplyStatus
                or BattleEventKind.AttackFire);
        Assert.True(firstAnnouncement < firstEffect);
    }

    [Fact]
    public void Draw_effects_resolve_at_planning_and_drawn_cards_can_be_planned()
    {
        var catalog = Catalog();
        var state = StateWithEnemyDeck("e_think", "e_strike", "e_strike");
        var participant = Participant(state) with { DrawPerTurn = 1, EnergyPerTurn = 1 };
        state = state with { EnemyParticipants = ImmutableArray.Create(participant) };

        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(state, Rng(), catalog);

        // ドローは予約せず即時処理され、引いた攻撃カードを同ターンに予約する。
        Assert.Equal(
            new[] { "attack" },
            planned.Enemies[0].PlannedEffects.Select(p => p.Effect.Action).ToArray());
        var after = Participant(planned);
        Assert.Single(after.DrawPile);
        // 0コスト → 1コストはコンボ継続の軽減で支払い0。
        Assert.Equal(1, after.Energy);
        Assert.Equal(2, after.ComboCount);
    }

    [Fact]
    public void Unspent_energy_above_the_per_turn_amount_is_kept_but_not_added_to()
    {
        var catalog = Catalog();
        var state = StateWithEnemyDeck("e_guard", "e_guard", "e_guard");
        var participant = Participant(state) with { Energy = 10, DrawPerTurn = 0 };
        state = state with { EnemyParticipants = ImmutableArray.Create(participant) };

        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(state, Rng(), catalog);

        // 規定値（2）以上残っているのでそのまま。上限は無い。
        Assert.Equal(10, Participant(planned).Energy);
    }

    [Fact]
    public void Battle_start_plans_and_end_turn_executes_then_replans()
    {
        var catalog = Catalog();
        var run = MakeRun("strike", "strike", "strike", "defend", "defend");

        var (started, _) = BattleEngine.Start(run, "enc_card", Rng(), catalog);

        Assert.NotEmpty(started.Enemies[0].PlannedEffects);
        Assert.Single(started.EnemyParticipants);
        // プレイヤーの root と味方参加者は敵の計画に影響されない。
        Assert.Equal(5, started.Hand.Length);
        Assert.Equal(started.Participants[0].Hand, started.Hand);

        var intent = CombatIntentProjector.Project(started, started.Enemies[0], catalog);
        Assert.NotNull(intent);

        var (afterTurn, _) = BattleEngine.EndTurn(started, Rng(), catalog);

        Assert.Equal(2, afterTurn.Turn);
        Assert.Equal(BattlePhase.PlayerInput, afterTurn.Phase);
        Assert.NotEmpty(afterTurn.Enemies[0].PlannedEffects);
    }

    private static readonly CardDefinition StatStrike = Card("e_stat_strike", 1,
        new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 0, AmountSource: "selfAttack"));
    private static readonly CardDefinition StatGuard = Card("e_stat_guard", 1,
        new CardEffect("block", EffectScope.Self, null, 0, AmountSource: "selfDefense"));
    private static readonly CardDefinition OpeningWard = Card("e_open_ward", 0,
        new CardEffect("buff", EffectScope.Self, null, 3, Name: "resistance"));

    private static DataCatalog StatCatalog(EnemyDefinition enemy) =>
        BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), Strike, Guard, Rage, Think,
                StatStrike, StatGuard, OpeningWard },
            enemies: new[] { enemy },
            encounters: new[]
            {
                new EncounterDefinition("enc_card", new EnemyPool(1, EnemyTier.Weak), new[] { EnemyId }),
            });

    [Fact]
    public void Attack_stat_reference_uses_the_value_at_execution_plus_strength()
    {
        var catalog = StatCatalog(CardGoblin());
        var state = StateWithEnemyDeck("e_stat_strike", "e_rage", "e_stat_guard");
        state = state with
        {
            Enemies = state.Enemies.SetItem(0, state.Enemies[0] with
            {
                AttackPower = 6,
                DefensePower = 3,
            }),
        };

        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(state, Rng(), catalog);
        // 予約時には数値を固定しない（AmountSource を保持）。
        Assert.Contains(planned.Enemies[0].PlannedEffects,
            p => p.Effect.AmountSource == "selfAttack");
        var intent = CombatIntentProjector.Project(planned, planned.Enemies[0], catalog);
        Assert.Equal(6 + 2, intent!.AttackAll);
        Assert.Equal(3, intent.Block);

        // 予約後に攻撃力が変動した場合、実行時の有効値を使う。
        var changed = planned with
        {
            Enemies = planned.Enemies.SetItem(0, planned.Enemies[0] with { AttackPower = 10 }),
        };
        var (after, _) = EnemyAttackingResolver.Resolve(
            changed with { Phase = BattlePhase.EnemyAttacking }, Rng(), catalog);

        Assert.Equal(70 - (10 + 2), after.Allies[0].CurrentHp);
        Assert.Equal(3, after.Enemies[0].Block.Sum);
    }

    [Fact]
    public void Zero_attack_stat_with_strength_deals_only_strength()
    {
        var catalog = StatCatalog(CardGoblin());
        var state = StateWithEnemyDeck("e_stat_strike", "e_rage", "e_guard");

        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(state, Rng(), catalog);
        var (after, _) = EnemyAttackingResolver.Resolve(
            planned with { Phase = BattlePhase.EnemyAttacking }, Rng(), catalog);

        Assert.Equal(70 - 2, after.Allies[0].CurrentHp);
    }

    [Fact]
    public void Battle_start_sets_enemy_stats_and_resolves_opening_actions_immediately()
    {
        var enemy = CardGoblin() with
        {
            Attack = 7,
            Defense = 4,
            OpeningActions = new[] { "e_open_ward" },
        };
        var catalog = StatCatalog(enemy);

        var (started, _) = BattleEngine.Start(
            MakeRun("strike", "strike", "strike", "defend", "defend"), "enc_card", Rng(), catalog);

        var actor = started.Enemies[0];
        Assert.Equal(7, actor.AttackPower);
        Assert.Equal(4, actor.DefensePower);
        // 先制行動は予約せず即時適用され、通常デッキには含まれない。
        Assert.Equal(3, actor.GetStatus("resistance"));
        var participant = Participant(started);
        Assert.DoesNotContain(
            participant.DrawPile.Concat(participant.Hand).Concat(participant.DiscardPile),
            card => card.CardDefinitionId == "e_open_ward");
    }

    [Fact]
    public void Zero_cost_draw_two_gives_one_energy_enemy_two_actions_through_the_combo_discount()
    {
        // 臨戦態勢（0 コスト・2 枚引く）→ 1 コスト（コンボで 0 支払い）→ 1 コスト（残りのエナジー 1）。
        // ドロー = エナジー = 1 の敵でも、臨戦態勢を引けば 2 回行動できる（2026-11-28 の行動デッキの原則）。
        var ready = Card("e_ready", 0, new CardEffect("draw", EffectScope.Self, null, 2));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), Strike, Guard, ready },
            enemies: new[] { CardGoblin(energyPerTurn: 1, drawPerTurn: 1) },
            encounters: new[] { new EncounterDefinition("enc_card", new EnemyPool(1, EnemyTier.Weak), new[] { EnemyId }) });
        var state = StateWithEnemyDeck("e_ready", "e_strike", "e_guard");
        var enemyParticipant = state.EnemyParticipants[0] with { EnergyPerTurn = 1, DrawPerTurn = 1 };
        state = state with { EnemyParticipants = state.EnemyParticipants.SetItem(0, enemyParticipant) };

        var (planned, _) = EnemyCardPlanner.StartTurnAndPlan(state, Rng(), catalog);

        Assert.Equal(new[] { "attack", "block" }, planned.Enemies[0].PlannedEffects
            .Where(effect => effect.Effect.Action is "attack" or "block")
            .Select(effect => effect.Effect.Action)
            .OrderBy(action => action));
    }

    [Fact]
    public void Opening_actions_can_attack_and_never_enter_any_pile()
    {
        // 先制行動は通常の敵の行動と同じ処理で実行するので、攻撃も使える（2026-11-28）。
        var enemy = CardGoblin() with { OpeningActions = new[] { "e_strike", "e_open_ward" } };
        var catalog = StatCatalog(enemy);

        var (started, events) = BattleEngine.Start(
            MakeRun("strike", "strike", "strike", "defend", "defend"), "enc_card", Rng(), catalog);

        Assert.Equal(70 - 5, started.Allies[0].CurrentHp);
        Assert.Equal(3, started.Enemies[0].GetStatus("resistance"));
        // 実行後も、1 ターン目の行動予約は別に作られる。
        Assert.NotEmpty(started.Enemies[0].PlannedEffects);
        Assert.Contains(events, e => e.Kind == BattleEventKind.EnemyCardAction && e.CardId == "e_strike");
        // 画面で中央にカードを出すための、写し付きの先制行動イベント。
        Assert.Equal(new[] { "e_strike", "e_open_ward" }, events
            .Where(e => e.Kind == BattleEventKind.OpeningAction && e.ReenactedCard is not null)
            .Select(e => e.CardId));
        var participant = Participant(started);
        Assert.DoesNotContain(
            participant.DrawPile.Concat(participant.Hand).Concat(participant.DiscardPile).Concat(participant.ExhaustPile),
            card => card.CardDefinitionId is "e_open_ward");
    }

    [Fact]
    public void Start_reports_the_state_right_before_opening_actions()
    {
        // 画面は先制行動の直前の状態を先に出し、演出の後で最終状態に切り替える（バフが演出より先に見えないように）。
        var enemy = CardGoblin() with { OpeningActions = new[] { "e_open_ward" } };
        var catalog = StatCatalog(enemy);

        var (started, _) = BattleEngine.Start(
            MakeRun("strike", "strike", "strike", "defend", "defend"), "enc_card", Rng(), catalog,
            out var preOpening);

        Assert.NotNull(preOpening);
        Assert.Equal(0, preOpening!.Enemies[0].GetStatus("resistance"));
        Assert.Equal(3, started.Enemies[0].GetStatus("resistance"));
        Assert.Equal(started.Hand.Select(c => c.InstanceId), preOpening.Hand.Select(c => c.InstanceId));

        var (_, _) = BattleEngine.Start(
            MakeRun("strike", "strike", "strike", "defend", "defend"), "enc_card", Rng(),
            StatCatalog(CardGoblin()), out var none);
        Assert.Null(none);
    }

    [Fact]
    public void Ally_opening_actions_run_after_enemy_openings_and_before_player_input()
    {
        var enemy = CardGoblin() with { OpeningActions = new[] { "e_open_ward" } };
        var baseCatalog = StatCatalog(enemy);
        var character = new CharacterDefinition(
            "default", "Hero", 70, 99, 3, new[] { "strike" },
            OpeningActions: new[] { "e_guard" });
        var catalog = baseCatalog with
        {
            Characters = new Dictionary<string, CharacterDefinition> { ["default"] = character },
        };

        var (started, events) = BattleEngine.Start(
            MakeRun("strike", "strike", "strike", "defend", "defend"), "enc_card", Rng(), catalog);

        Assert.Equal(BattlePhase.PlayerInput, started.Phase);
        Assert.Equal(4, started.Allies[0].Block.Sum);
        var eventList = events.ToList();
        var enemyOpening = eventList.FindIndex(e => e.Kind == BattleEventKind.ApplyStatus && e.Note == "resistance");
        var allyOpening = eventList.FindIndex(e => e.Kind == BattleEventKind.GainBlock
            && e.TargetInstanceId == started.Allies[0].InstanceId);
        Assert.True(enemyOpening >= 0 && allyOpening > enemyOpening);
        // 使ったカードはどの山にも入らない（デッキの strike/defend だけ）。
        Assert.DoesNotContain(
            started.DrawPile.Concat(started.Hand).Concat(started.DiscardPile).Concat(started.ExhaustPile),
            card => card.CardDefinitionId == "e_guard");
    }

    [Fact]
    public void Move_enemies_do_not_create_enemy_participants()
    {
        var (started, _) = BattleEngine.Start(
            MakeRun("strike"), "enc_test", Rng(), BattleFixtures.MinimalCatalog());

        Assert.True(started.EnemyParticipants.IsDefaultOrEmpty);
        Assert.Empty(started.Enemies[0].PlannedEffects);
        Assert.Equal("swing", started.Enemies[0].CurrentMoveId);
    }

    private static RunState MakeRun(params string[] deck) => new(
        SchemaVersion: RunState.CurrentSchemaVersion,
        CurrentAct: 1, CurrentNodeId: 0,
        VisitedNodeIds: ImmutableArray<int>.Empty,
        UnknownResolutions: ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
        CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
        Deck: deck.Select(id => new CardInstance(id, false)).ToImmutableArray(),
        Potions: ImmutableArray<string>.Empty, PotionSlotCount: 0,
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
        SavedAtUtc: System.DateTimeOffset.UnixEpoch,
        Progress: RunProgress.InProgress,
        RunId: "run1", ActiveActStartRelicChoice: null,
        SeenCardBaseIds: ImmutableArray<string>.Empty,
        AcquiredRelicIds: ImmutableArray<string>.Empty,
        AcquiredPotionIds: ImmutableArray<string>.Empty,
        EncounteredEnemyIds: ImmutableArray<string>.Empty,
        JourneyLog: ImmutableArray<RoguelikeCardGame.Core.Run.JourneyEntry>.Empty);
}
