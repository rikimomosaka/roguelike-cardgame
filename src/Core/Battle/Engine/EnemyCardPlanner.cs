using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// カード制御の敵のターン開始処理と行動計画。
/// docs/proposed-enemy-card-ai-and-character-stats.md §2-2a:
/// - 敵はターン開始時（プレイヤー行動前）に手札からカードを選んで使用する。
/// - 選択は味方CPUと同じ <see cref="CpuCardPlayPolicy"/> の比較規則を共通利用する。
/// - カード使用は <see cref="BattleEngine"/> の通常カード処理（コスト・コンボ・使用回数・カード移動・
///   手札系即時効果）を共通利用し、戦場効果は予測として <see cref="CombatActor.PlannedEffects"/> へ予約する。
/// - 予約した戦場効果は敵の行動時（<see cref="EnemyAttackingResolver"/>）に実行する。
/// </summary>
internal static class EnemyCardPlanner
{
    /// <summary>1 体 1 ターンの安全上限（味方CPUと同値）。</summary>
    public const int MaxPlaysPerEnemy = BattleCpuPlanningCoordinator.MaxPlaysPerActor;

    public const string ParticipantIdPrefix = "enemy:";

    /// <summary>
    /// 敵カード使用時に適用せず予約する戦場効果。これ以外（ドロー・捨て・コスト変更・
    /// エナジー等の手札／資源系）は予約時に即時処理する。
    /// </summary>
    public static bool IsDeferredBattlefieldAction(string action) => action is
        "attack" or "block" or "buff" or "debuff" or "heal" or "selfDamage" or "summon";

    public static bool IsCardControlled(CombatActor actor, DataCatalog catalog) =>
        actor.Side == ActorSide.Enemy
        && catalog.TryGetEnemy(actor.DefinitionId, out var def)
        && def.IsCardControlled;

    /// <summary>
    /// 生存中のカード制御敵に参加者状態が無ければ作る（デッキ展開とシャッフルのみ。ドローは
    /// ターン開始処理で行う）。既存参加者はリセットしない。
    /// </summary>
    public static BattleState EnsureEnemyParticipants(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var result = state;
        foreach (var enemy in state.Enemies.OrderBy(actor => actor.SlotIndex))
        {
            if (!enemy.IsAlive) continue;
            if (!catalog.TryGetEnemy(enemy.DefinitionId, out var def) || !def.IsCardControlled)
                continue;
            if (TryGetParticipant(result, enemy.InstanceId, out _))
                continue;

            var cards = new List<BattleCardInstance>();
            int ordinal = 0;
            foreach (var entry in def.EffectiveDeck)
            {
                for (int copy = 0; copy < entry.Count; copy++)
                {
                    cards.Add(new BattleCardInstance(
                        $"{enemy.InstanceId}_card_{ordinal++}",
                        entry.CardId,
                        entry.Upgraded,
                        null));
                }
            }
            var deck = cards.ToArray();
            for (int i = deck.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            var participant = new BattleParticipantState(
                ParticipantId: ParticipantIdPrefix + enemy.InstanceId,
                ActorInstanceId: enemy.InstanceId,
                ControllerKind: BattleParticipantControllerKind.Cpu,
                OwnerPlayerSlot: null,
                CharacterId: def.Id,
                TargetAllyIndex: null,
                TargetEnemyIndex: null,
                Energy: Math.Max(0, def.InitialEnergy),
                EnergyPerTurn: Math.Max(0, def.EnergyPerTurn),
                DrawPerTurn: Math.Max(0, def.DrawPerTurn),
                DrawPile: deck.ToImmutableArray(),
                Hand: ImmutableArray<BattleCardInstance>.Empty,
                DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
                ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
                SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
                PowerCards: ImmutableArray<BattleCardInstance>.Empty,
                ComboCount: 0,
                MaxComboThisTurn: 0,
                ComboGauge: 0,
                LastPlayedOrigCost: null,
                NextCardComboFreePass: false,
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
            result = result with
            {
                EnemyParticipants = result.EnemyParticipants.IsDefault
                    ? ImmutableArray.Create(participant)
                    : result.EnemyParticipants.Add(participant),
            };
        }
        return result;
    }

    /// <summary>
    /// 全カード制御敵のターン開始処理（前ターン手札の整理・エナジー加算・ドロー）と計画を
    /// Slot 順に行う。戦闘決着済み・PlayerInput 以外では何もしない。
    /// </summary>
    public static (BattleState State, IReadOnlyList<BattleEvent> Events) StartTurnAndPlan(
        BattleState state, IRng rng, DataCatalog catalog)
    {
        var events = new List<BattleEvent>();
        if (state.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending
            || state.Phase != BattlePhase.PlayerInput)
            return (state, events);

        // 再生用記録は毎ターン作り直す（前後スナップショットへ前ターン分を入れ子にしない）。
        var result = EnsureEnemyParticipants(state, rng, catalog) with
        {
            EnemyPlanningActions = ImmutableArray<CpuCommittedAction>.Empty,
        };
        if (result.EnemyParticipants.IsDefaultOrEmpty)
            return (result, events);
        var actions = ImmutableArray.CreateBuilder<CpuCommittedAction>();

        var enemyIds = result.Enemies
            .OrderBy(actor => actor.SlotIndex)
            .Select(actor => actor.InstanceId)
            .ToArray();
        foreach (var enemyId in enemyIds)
        {
            var actor = FindEnemy(result, enemyId);
            if (actor is null || !actor.IsAlive) continue;
            if (!catalog.TryGetEnemy(actor.DefinitionId, out var def) || !def.IsCardControlled)
                continue;
            if (!TryGetParticipant(result, enemyId, out var participant))
                continue;

            result = StartTurn(result, participant, actor, def, rng, catalog, events);
            result = Plan(result, enemyId, def, rng, catalog, events, actions);
            if (result.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
                break;
        }
        return (result with { EnemyPlanningActions = actions.ToImmutable() }, events);
    }

    private static BattleState StartTurn(
        BattleState state,
        BattleParticipantState participant,
        CombatActor actor,
        EnemyDefinition def,
        IRng rng,
        DataCatalog catalog,
        List<BattleEvent> events)
    {
        var savedRoot = BattleParticipantAccess.CaptureRootResources(
            state, BattleParticipantAccess.GetPrimary(state));
        var projected = BattleParticipantAccess.ProjectDetachedToRoot(state, participant);

        // 前ターンの手札整理（待機以外は捨札へ）とターン限定状態のリセットは、
        // 味方参加者と同じターン終了規則を使う。敵の手札は敵ターン後に見えないため、
        // 次の計画直前にまとめて処理する。
        projected = BattleParticipantTurnLifecycle.ApplyTurnEndToProjectedRoot(
            projected, actor, catalog);
        projected = ComboTurnBoundaryRules.ResetAtTurnStart(projected);
        projected = projected with
        {
            Energy = EnergyMath.RefillTo(projected.Energy, projected.EnergyMax),
        };
        projected = DrawHelper.Draw(
            projected,
            projected.DrawPerTurn,
            rng,
            Math.Max(1, def.HandLimit),
            actor,
            out _,
            out int shuffled);
        if (shuffled > 0)
        {
            events.Add(new BattleEvent(
                BattleEventKind.Shuffle,
                Order: events.Count,
                CasterInstanceId: actor.InstanceId,
                TargetInstanceId: actor.InstanceId,
                Amount: shuffled));
        }

        var updated = BattleParticipantAccess.CaptureRootResources(projected, participant)
            with { CpuCardPlaysThisTurn = 0 };
        var restored = BattleParticipantAccess.RestoreRoot(projected, savedRoot);
        restored = ReplaceParticipant(restored, updated);
        // 予約は毎ターン作り直す（通常は実行時に空になっている）。
        return ReplaceEnemy(restored, actor.InstanceId, a => a with
        {
            PlannedEffects = ImmutableArray<PlannedCardEffect>.Empty,
        });
    }

    private static BattleState Plan(
        BattleState state,
        string enemyId,
        EnemyDefinition def,
        IRng rng,
        DataCatalog catalog,
        List<BattleEvent> events,
        ImmutableArray<CpuCommittedAction>.Builder actions)
    {
        var result = state;
        for (int plays = 0; plays < MaxPlaysPerEnemy; plays++)
        {
            var actor = FindEnemy(result, enemyId);
            if (actor is null || !actor.IsAlive) break;
            if (!TryGetParticipant(result, enemyId, out var participant)) break;

            var savedRoot = BattleParticipantAccess.CaptureRootResources(
                result, BattleParticipantAccess.GetPrimary(result));
            var projected = BattleParticipantAccess.ProjectDetachedToRoot(result, participant);
            var decision = CpuCardPlayPolicy.ChooseNextForEnemy(
                projected, participant, actor, def, catalog);
            if (decision is null) break;

            var stateBefore = result;
            var cardSnapshot = projected.Hand[decision.HandIndex];
            catalog.TryGetCard(cardSnapshot.CardDefinitionId, out var cardDefinition);
            var costBreakdown = cardDefinition is null
                ? null
                : BattleCardCostCalculator.EvaluateForHand(
                    projected, cardSnapshot, cardDefinition, actor);
            int usesBefore = projected.CardUsesThisTurn.GetValueOrDefault(decision.CardDefinitionId);

            var (afterPlay, playEvents) = BattleEngine.PlanEnemyCard(
                projected, enemyId, decision.HandIndex, rng, catalog);
            foreach (var ev in playEvents)
                events.Add(ev with { Order = events.Count });

            var updated = BattleParticipantAccess.CaptureRootResources(afterPlay, participant)
                with { CpuCardPlaysThisTurn = participant.CpuCardPlaysThisTurn + 1 };
            result = BattleParticipantAccess.RestoreRoot(afterPlay, savedRoot);
            result = ReplaceParticipant(result, updated);

            var actorAfter = FindEnemy(result, enemyId) ?? actor;
            actions.Add(new CpuCommittedAction(
                participant.ParticipantId,
                enemyId,
                decision.CardInstanceId,
                decision.CardDefinitionId,
                decision.PayableCost,
                TargetEnemyIndex: null,
                TargetAllyIndex: null,
                decision.DebugReason,
                playEvents.ToImmutableArray())
            {
                Turn = stateBefore.Turn,
                CardSnapshot = cardSnapshot,
                CostBreakdown = costBreakdown,
                UsesBefore = usesBefore,
                RevisionBefore = stateBefore.ActionRevision,
                RevisionAfter = result.ActionRevision,
                IntentDelta = BuildPlannedIntentDelta(stateBefore, actor, actorAfter),
                ActorContext = new CpuCommittedActorContext(
                    actor.GetStatus("strength"),
                    actor.GetStatus("weak"),
                    actor.GetStatus("dexterity"))
                {
                    AttackPower = actor.AttackPower,
                    DefensePower = actor.DefensePower,
                },
                StateBefore = stateBefore,
                StateAfter = result,
                PlanningOrdinal = actions.Count,
            });
        }
        return result;
    }

    /// <summary>
    /// このカード使用で新たに予約した攻撃・ブロックを、味方CPUの行動記録と同じ形で返す
    /// （行動再生の「予定」表示用。値は予約時点の能力値で評価）。
    /// </summary>
    private static CpuCommittedIntentDelta BuildPlannedIntentDelta(
        BattleState state, CombatActor before, CombatActor after)
    {
        var added = after.PlannedEffects.Skip(before.PlannedEffects.Length)
            .Select(planned => (planned.Source, Effect: EffectApplier.ResolveActorStatAtExecution(
                planned.Effect, state, after)))
            .ToArray();
        ImmutableArray<AttackHit> Hits(Cards.EffectScope scope) => added
            .Where(item => item.Effect.Action == "attack" && item.Effect.Scope == scope)
            .Select(item => new AttackHit(item.Effect.Amount, item.Source, DamageCauseChain.Empty))
            .ToImmutableArray();
        var blocks = added.Where(item => item.Effect.Action == "block").ToArray();
        return new CpuCommittedIntentDelta(
            Hits(Cards.EffectScope.Single),
            Hits(Cards.EffectScope.Random),
            Hits(Cards.EffectScope.All),
            BlockSumDelta: blocks.Sum(item => item.Effect.Amount),
            BlockAddCountDelta: blocks.Length);
    }

    public static bool TryGetParticipant(
        BattleState state, string enemyInstanceId, out BattleParticipantState participant)
    {
        if (!state.EnemyParticipants.IsDefaultOrEmpty)
        {
            foreach (var candidate in state.EnemyParticipants)
            {
                if (string.Equals(
                        candidate.ActorInstanceId, enemyInstanceId, StringComparison.Ordinal))
                {
                    participant = candidate;
                    return true;
                }
            }
        }
        participant = null!;
        return false;
    }

    private static BattleState ReplaceParticipant(
        BattleState state, BattleParticipantState participant)
    {
        for (int i = 0; i < state.EnemyParticipants.Length; i++)
        {
            if (string.Equals(
                    state.EnemyParticipants[i].ParticipantId,
                    participant.ParticipantId,
                    StringComparison.Ordinal))
            {
                return state with
                {
                    EnemyParticipants = state.EnemyParticipants.SetItem(i, participant),
                };
            }
        }
        throw new InvalidOperationException(
            $"Enemy participant '{participant.ParticipantId}' does not exist.");
    }

    private static CombatActor? FindEnemy(BattleState state, string instanceId)
    {
        foreach (var enemy in state.Enemies)
            if (string.Equals(enemy.InstanceId, instanceId, StringComparison.Ordinal))
                return enemy;
        return null;
    }

    private static BattleState ReplaceEnemy(
        BattleState state, string instanceId, Func<CombatActor, CombatActor> update)
    {
        for (int i = 0; i < state.Enemies.Length; i++)
        {
            if (string.Equals(state.Enemies[i].InstanceId, instanceId, StringComparison.Ordinal))
                return state with { Enemies = state.Enemies.SetItem(i, update(state.Enemies[i])) };
        }
        return state;
    }
}
