using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// A deterministic, side-effect-free card selector for CPU participants.
/// It deliberately chooses one command at a time; the coordinator must apply
/// that command to the authoritative state before asking for another one.
/// </summary>
public static class CpuCardPlayPolicy
{
    public static CpuCardPlayDecision? ChooseNext(
        BattleState state,
        string participantId,
        DataCatalog catalog,
        bool includeReadyParticipant = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        ArgumentNullException.ThrowIfNull(catalog);

        if (state.Phase != BattlePhase.PlayerInput
            || state.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending
            || !BattleParticipantAccess.TryGet(state, participantId, out var participant)
            || participant.ControllerKind != BattleParticipantControllerKind.Cpu
            || (participant.IsReady && !includeReadyParticipant)
            || participant.PendingCardPlay is not null)
            return null;

        var actor = state.Allies.FirstOrDefault(candidate =>
            string.Equals(candidate.InstanceId, participant.ActorInstanceId, StringComparison.Ordinal));
        if (actor is null || !actor.IsAlive)
            return null;

        if (!catalog.TryGetUnit(participant.CharacterId, out var unit)
            || !unit.IsCardControlled
            || !string.Equals(
                unit.AiProfileId, UnitAiProfileIds.LowCostCombo, StringComparison.Ordinal))
            return null;

        var projected = BattleParticipantAccess.ProjectToRoot(state, participantId);
        int? allyTarget = IndexOfActor(projected.Allies, actor.InstanceId);
        return ChooseFromProjected(
            projected,
            participant.ParticipantId,
            actor,
            BuildPriorities(unit.EffectiveDeck),
            catalog,
            selectTargets: true,
            allyTarget);
    }

    /// <summary>
    /// カード制御の敵用。敵参加者を root へ投影した state を受け取り、味方CPUと
    /// 同じ候補比較で次の 1 枚を選ぶ。敵の単体攻撃対象は実行時に現行ルールで決めるため、
    /// 対象 index は返さない。
    /// </summary>
    internal static CpuCardPlayDecision? ChooseNextForEnemy(
        BattleState projected,
        BattleParticipantState participant,
        CombatActor actor,
        EnemyDefinition enemy,
        DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(projected);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(enemy);
        ArgumentNullException.ThrowIfNull(catalog);

        if (projected.Phase != BattlePhase.PlayerInput
            || projected.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending
            || !actor.IsAlive
            || !enemy.IsCardControlled
            || !string.Equals(
                enemy.AiProfileId, UnitAiProfileIds.LowCostCombo, StringComparison.Ordinal))
            return null;

        return ChooseFromProjected(
            projected,
            participant.ParticipantId,
            actor,
            BuildPriorities(enemy.EffectiveDeck),
            catalog,
            selectTargets: false,
            allyTarget: null);
    }

    private static Dictionary<string, int> BuildPriorities(
        IReadOnlyList<UnitDeckEntry> deck) =>
        deck
            .GroupBy(entry => entry.CardId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(entry => entry.Priority),
                StringComparer.Ordinal);

    private static CpuCardPlayDecision? ChooseFromProjected(
        BattleState projected,
        string participantId,
        CombatActor actor,
        IReadOnlyDictionary<string, int> priorities,
        DataCatalog catalog,
        bool selectTargets,
        int? allyTarget)
    {
        CpuCardPlayDecision? best = null;
        for (int handIndex = 0; handIndex < projected.Hand.Length; handIndex++)
        {
            var card = projected.Hand[handIndex];
            if (!catalog.TryGetCard(card.CardDefinitionId, out var definition))
                continue;

            int usesPerTurn = definition.EffectiveUsesPerTurn(card.IsUpgraded);
            if (CardUsageTracker.HasReachedLimit(projected, definition.Id, usesPerTurn))
                continue;
            if (BattleCardCostCalculator.GetPlayRestrictionReason(
                    projected, actor, definition) is not null)
                continue;
            if (definition.ComboGaugeCost < 0
                || projected.ComboGauge < definition.ComboGaugeCost)
                continue;

            var cost = BattleCardCostCalculator.EvaluateForHand(
                projected, card, definition, actor);
            if (cost.PayableCost is not { } payableCost
                || projected.Energy < payableCost)
                continue;

            int priority = priorities.GetValueOrDefault(definition.Id, 0);
            int? enemyTarget = selectTargets
                ? SelectEnemyTargetIndex(
                    projected, actor, definition, card.IsUpgraded)
                : null;
            int continuationTier = cost.ComboContinues
                ? cost.ComboReductionEligible ? 0 : 1
                : 2;
            var candidate = new CpuCardPlayDecision(
                participantId,
                actor.InstanceId,
                card.InstanceId,
                definition.Id,
                handIndex,
                enemyTarget,
                allyTarget,
                payableCost,
                cost.ComboContinues,
                cost.ComboReductionEligible,
                priority,
                continuationTier,
                BuildReason(continuationTier, payableCost, priority, handIndex));

            if (best is null || Compare(candidate, best) < 0)
                best = candidate;
        }

        return best;
    }

    private static int Compare(CpuCardPlayDecision left, CpuCardPlayDecision right)
    {
        int result = left.ContinuationTier.CompareTo(right.ContinuationTier);
        if (result != 0) return result;
        result = left.PayableCost.CompareTo(right.PayableCost);
        if (result != 0) return result;
        result = right.UnitPriority.CompareTo(left.UnitPriority);
        if (result != 0) return result;
        return left.HandIndex.CompareTo(right.HandIndex);
    }

    private static string BuildReason(
        int tier, int payableCost, int priority, int handIndex)
    {
        string category = tier switch
        {
            0 => "combo-discounted continuation",
            1 => "combo continuation",
            _ => "combo breaker",
        };
        return $"{category}; payable={payableCost}; priority={priority}; hand={handIndex}";
    }

    private static int? FirstLivingIndex(
        System.Collections.Immutable.ImmutableArray<CombatActor> actors)
    {
        for (int i = 0; i < actors.Length; i++)
        {
            if (actors[i].IsAlive)
                return i;
        }
        return null;
    }

    private static int? SelectEnemyTargetIndex(
        BattleState state,
        CombatActor actor,
        CardDefinition definition,
        bool upgraded)
    {
        var effects = upgraded && definition.UpgradedEffects is not null
            ? definition.UpgradedEffects
            : definition.Effects;
        var singleAttacks = effects
            .Where(effect => effect.Action == "attack"
                && effect.Scope == EffectScope.Single)
            .ToArray();

        if (singleAttacks.Length == 0)
            return FirstLivingIndex(state.Enemies);

        return state.Enemies
            .Select((enemy, index) => new
            {
                Enemy = enemy,
                Index = index,
                Defeated = WouldDefeat(actor, enemy, singleAttacks),
            })
            .Where(candidate => candidate.Enemy.IsAlive)
            .OrderByDescending(candidate => candidate.Defeated)
            .ThenBy(candidate => candidate.Enemy.CurrentHp)
            .ThenBy(candidate => candidate.Enemy.SlotIndex)
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => (int?)candidate.Index)
            .FirstOrDefault();
    }

    private static bool WouldDefeat(
        CombatActor actor,
        CombatActor target,
        IReadOnlyList<CardEffect> singleAttacks)
    {
        var simulated = target;
        foreach (var effect in singleAttacks)
        {
            int amount = string.IsNullOrEmpty(effect.AmountSource)
                ? effect.Amount
                : AmountSourceEvaluator.Scale(
                    AmountSourceEvaluator.IsActorStatSource(effect.AmountSource)
                        ? (effect.AmountSource == "selfAttack" ? actor.AttackPower : actor.DefensePower)
                        : effect.Amount,
                    effect.AmountScalePercent);
            (simulated, _, _) = DealDamageHelper.Apply(
                actor, simulated, amount, "cpu-target-preview", 0);
            if (!simulated.IsAlive)
                return true;
        }

        return false;
    }

    private static int? IndexOfActor(
        System.Collections.Immutable.ImmutableArray<CombatActor> actors,
        string actorInstanceId)
    {
        for (int i = 0; i < actors.Length; i++)
        {
            if (string.Equals(
                    actors[i].InstanceId, actorInstanceId, StringComparison.Ordinal))
                return i;
        }
        return null;
    }
}

public sealed record CpuCardPlayDecision(
    string ParticipantId,
    string ActorInstanceId,
    string CardInstanceId,
    string CardDefinitionId,
    int HandIndex,
    int? TargetEnemyIndex,
    int? TargetAllyIndex,
    int PayableCost,
    bool ComboContinues,
    bool ComboReductionEligible,
    int UnitPriority,
    int ContinuationTier,
    string DebugReason);
