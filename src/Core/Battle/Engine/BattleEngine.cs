using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// バトルエンジンの公開ファサード。`Start` / `PlayCard` / `EndTurn` / `Finalize` を提供。
/// 親 spec §3-§10 参照。
/// </summary>
public static partial class BattleEngine
{
    public const int InitialEnergy = 3;

    public static (BattleState, IReadOnlyList<BattleEvent>) Start(
        RunState run, string encounterId, IRng rng, DataCatalog catalog) =>
        Start(run, encounterId, rng, catalog, out _);

    /// <summary>
    /// 戦闘開始。preOpeningState には、先制行動を使う直前（手札が配られ、プレイヤーの操作の直前）の状態が入る。
    /// 先制行動を誰も使わなかった時は null。画面はこの状態を先に出し、先制行動の演出の後で最終状態に切り替える。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) Start(
        RunState run, string encounterId, IRng rng, DataCatalog catalog, out BattleState? preOpeningState)
    {
        preOpeningState = null;
        if (!catalog.TryGetEncounter(encounterId, out var encounter))
            throw new System.InvalidOperationException($"encounter '{encounterId}' not found in catalog");

        // 1. 主人公 CombatActor 生成
        var hero = new CombatActor(
            InstanceId: "hero_inst", DefinitionId: "hero",
            Side: ActorSide.Ally, SlotIndex: 0,
            CurrentHp: run.CurrentHp, MaxHp: run.MaxHp,
            Block: BlockPool.Empty,
            AttackSingle: AttackPool.Empty,
            AttackRandom: AttackPool.Empty,
            AttackAll: AttackPool.Empty,
            Statuses: ImmutableDictionary<string, int>.Empty,
            CurrentMoveId: null,
            RemainingLifetimeTurns: null,                  // 10.2.D
            AssociatedSummonHeldInstanceId: null)          // 10.2.D
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = "hero_inst",
            // 基礎攻撃力・守備力はキャラクター定義の初期値（永続補正は今後追加）。
            AttackPower = catalog.TryGetCharacter(run.CharacterId, out var heroCharacter)
                ? heroCharacter.Attack : 0,
            DefensePower = heroCharacter?.Defense ?? 0,
        };

        // 2. 敵 CombatActor 生成
        var enemiesBuilder = ImmutableArray.CreateBuilder<CombatActor>();
        for (int i = 0; i < encounter.EnemyIds.Count; i++)
        {
            var eid = encounter.EnemyIds[i];
            if (!catalog.TryGetEnemy(eid, out var def))
                throw new System.InvalidOperationException($"enemy '{eid}' not found in catalog");
            enemiesBuilder.Add(new CombatActor(
                InstanceId: $"enemy_inst_{i}", DefinitionId: eid,
                Side: ActorSide.Enemy, SlotIndex: i,
                CurrentHp: def.Hp, MaxHp: def.Hp,
                Block: BlockPool.Empty,
                AttackSingle: AttackPool.Empty,
                AttackRandom: AttackPool.Empty,
                AttackAll: AttackPool.Empty,
                Statuses: ImmutableDictionary<string, int>.Empty,
                CurrentMoveId: def.IsCardControlled ? null : def.InitialMoveId,
                RemainingLifetimeTurns: null,                  // 10.2.D
                AssociatedSummonHeldInstanceId: null)          // 10.2.D
            {
                AttackPower = def.Attack,
                DefensePower = def.Defense,
            });
        }

        // 3. Deck コピー & シャッフル → 山札
        var deckCards = run.Deck
            .Select((c, idx) => new BattleCardInstance($"card_inst_{idx}", c.Id, c.Upgraded, null))
            .ToArray();
        ShuffleInPlace(deckCards, rng);
        var drawPile = deckCards.ToImmutableArray();

        // 4. 初期 BattleState（Turn=0、TurnStartProcessor で +1 して Turn=1 へ）
        // Phase 10.6.B: 所持 relic からターン開始時エナジー追加量 / DrawPerTurn を snapshot。
        // BattleState の EnergyMax は互換名であり、現在は保有上限を意味しない。
        int baseEnergyPerTurn = InitialEnergy;
        int baseDrawPerTurn = TurnStartProcessor.BaseDrawPerTurn;
        if (catalog.TryGetCharacter(run.CharacterId, out var character))
        {
            baseEnergyPerTurn = character.EnergyPerTurn;
            baseDrawPerTurn = character.DrawPerTurn;
        }

        int energyPerTurnFinal = Relics.PassiveModifiers.ApplyEnergyPerTurnBonus(
            baseEnergyPerTurn, run, catalog);
        int drawPerTurnFinal = Relics.PassiveModifiers.ApplyCardsDrawnPerTurnBonus(
            baseDrawPerTurn, run, catalog);
        var initial = new BattleState(
            Turn: 0,
            Phase: BattlePhase.PlayerInput,
            Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: enemiesBuilder.ToImmutable(),
            TargetAllyIndex: 0,
            TargetEnemyIndex: enemiesBuilder.Count > 0 ? 0 : (int?)null,
            Energy: 0, EnergyMax: energyPerTurnFinal,
            DrawPile: drawPile,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,    // 10.2.D
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,    // 10.2.D
            ComboCount: 0,                        // 10.2.C
            LastPlayedOrigCost: null,             // 10.2.C
            NextCardComboFreePass: false,         // 10.2.C
            OwnedRelicIds: run.Relics.ToImmutableArray(),   // 10.2.E
            Potions: run.Potions,                           // 10.2.E (already ImmutableArray<string>)
            EncounterId: encounterId,
            WildUsedInCurrentCombo: false,
            DrawPerTurn: drawPerTurnFinal)
        {
            MaxComboThisTurn = 0,
            ComboGauge = 0,
            UsedCharacterSkillIdsThisTurn = ImmutableHashSet<string>.Empty,
            CharacterSkillUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            CharacterTraitUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            CardUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            CharacterId = run.CharacterId,
        };

        var events = new List<BattleEvent>();
        int order = 0;

        // BattleStart event 発火
        events.Add(new BattleEvent(
            BattleEventKind.BattleStart, Order: order++,
            Note: encounterId));

        // 5. ターン 1 開始処理（5 ドロー、Energy=0+3、TurnStart イベント発火）
        var (afterTurnStart, evsTurnStart) = TurnStartProcessor.Process(initial, rng, catalog);
        foreach (var ev in evsTurnStart) { events.Add(ev with { Order = order++ }); }

        // 10.2.E 追加: OnBattleStart レリック発動 (TurnStart 後)
        // Phase 10.5.L1.5: relic-level Trigger 廃止、文字列指定に変更。
        var afterBattleStart = afterTurnStart;
        if (afterTurnStart.Outcome ==
            RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
        {
            var (afterRelic, evsBattleStart) = RelicTriggerProcessor.Fire(
                afterTurnStart, "OnBattleStart",
                catalog, rng, orderStart: order);
            afterBattleStart = afterRelic;
            foreach (var ev in evsBattleStart)
                events.Add(ev with { Order = order++ });

            var (afterStartOutcome, battleEndEvents) =
                BattleOutcomeResolver.ApplyIfTerminal(afterBattleStart, order);
            afterBattleStart = afterStartOutcome;
            foreach (var battleEvent in battleEndEvents)
                events.Add(battleEvent with { Order = order++ });
        }

        // Promote the completed solo start snapshot only after TurnStart and
        // OnBattleStart effects. Until all processors are participant-aware,
        // this guarantees that the explicit primary and its legacy root mirror
        // begin with the same shuffled zones, energy, combo, and statuses.
        var withPrimaryParticipant = BattleParticipantAccess.InitializePrimary(
            afterBattleStart,
            BattleParticipantAccess.DefaultPrimaryParticipantId,
            BattleParticipantControllerKind.Human);
        if (withPrimaryParticipant.Outcome ==
            RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
        {
            // 先制行動: ターン開始の処理と戦闘開始時のレリックがすべて終わった後、プレイヤーの操作の直前に、
            // 敵 → 味方の順で使う（2026-11-28）。使ったカードはどの山にも入らない。
            var beforeOpening = withPrimaryParticipant;
            int eventsBeforeOpening = events.Count;
            withPrimaryParticipant = EnemyOpeningActions.Apply(
                withPrimaryParticipant, events, ref order, rng, catalog);
            if (events.Count > eventsBeforeOpening)
                preOpeningState = beforeOpening;
            if (!withPrimaryParticipant.Participants.IsDefaultOrEmpty)
            {
                withPrimaryParticipant = BattleParticipantAccess.CommitRootToParticipant(
                    withPrimaryParticipant, withPrimaryParticipant.Participants[0].ParticipantId);
            }
        }
        if (withPrimaryParticipant.Outcome !=
            RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
        {
            withPrimaryParticipant = BattleParticipantTurnLifecycle
                .ApplyTerminalReset(withPrimaryParticipant);
        }
        else
        {
            // カード制御の敵は第 1 ターン開始処理の後、プレイヤー行動前に行動を予約する。
            // 敵の計画は予測のみなので、ライブのイベント列には流さない（演出は行動再生で確認する）。
            var (afterEnemyPlan, _) = EnemyCardPlanner.StartTurnAndPlan(
                withPrimaryParticipant, rng, catalog);
            withPrimaryParticipant = afterEnemyPlan;
        }

        return (withPrimaryParticipant, events);
    }

    private static void ShuffleInPlace(BattleCardInstance[] arr, IRng rng)
    {
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }
}
