using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.Projection;

/// <summary>
/// Pure projection of combat intent from an immutable battle snapshot.
/// This is game logic and deliberately lives in Core rather than a transport layer.
/// </summary>
public static class CombatIntentProjector
{
    public static CombatIntentProjection? Project(
        BattleState state,
        CombatActor actor,
        DataCatalog catalog)
    {
        var afterimage = AfterimageIntentProjector.Project(state, actor);
        if (UsesQueuedCardAttacks(actor, catalog))
            return Merge(
                ProjectQueuedCardAttacks(actor),
                afterimage);

        // カード制御の敵: 予約済み戦場効果を実行順（非攻撃 → 攻撃）で予告する。
        // ブロック・バフ・デバフは予定として表示するだけで、まだ適用されていない。
        if (actor.Side == ActorSide.Enemy && !actor.PlannedEffects.IsDefaultOrEmpty)
        {
            var ordered = actor.PlannedEffects
                .Where(planned => planned.Effect.Action != "attack")
                .Concat(actor.PlannedEffects
                    .Where(planned => planned.Effect.Action == "attack"))
                .Select(planned => EffectApplier.ResolveActorStatAtExecution(
                    planned.Effect, state, actor))
                .ToArray();
            var planned = new MoveDefinition(
                "planned", MoveKind.Unknown, ordered, NextMoveId: "planned");
            return Merge(ProjectMove(state, actor, planned), afterimage);
        }

        if (actor.CurrentMoveId is null)
            return afterimage;

        var move = FindCurrentMove(actor, catalog);
        return move is null
            ? afterimage
            : Merge(ProjectMove(state, actor, move), afterimage);
    }

    private static CombatIntentProjection? Merge(
        CombatIntentProjection? first,
        CombatIntentProjection? second)
    {
        if (first is null) return second;
        if (second is null) return first;

        return new CombatIntentProjection(
            AttackSingle: AddNullable(first.AttackSingle, second.AttackSingle),
            AttackRandom: AddNullable(first.AttackRandom, second.AttackRandom),
            AttackAll: AddNullable(first.AttackAll, second.AttackAll),
            AttackHits: AddNullable(first.AttackHits, second.AttackHits),
            AttackSingleHits: AddNullable(
                first.AttackSingleHits, second.AttackSingleHits),
            AttackRandomHits: AddNullable(
                first.AttackRandomHits, second.AttackRandomHits),
            AttackAllHits: AddNullable(
                first.AttackAllHits, second.AttackAllHits),
            Block: AddNullable(first.Block, second.Block),
            HasBuff: first.HasBuff || second.HasBuff,
            HasDebuff: first.HasDebuff || second.HasDebuff,
            HasHeal: first.HasHeal || second.HasHeal);
    }

    private static int? AddNullable(int? left, int? right) =>
        left is null
            ? right
            : right is null
                ? left
                : unchecked(left.Value + right.Value);

    private static bool UsesQueuedCardAttacks(
        CombatActor actor,
        DataCatalog catalog)
    {
        if (actor.DefinitionId == "hero")
            return true;

        return actor.Side == ActorSide.Ally
            && catalog.TryGetUnit(actor.DefinitionId, out var unit)
            && unit.IsCardControlled;
    }

    private static CombatIntentProjection? ProjectQueuedCardAttacks(CombatActor actor)
    {
        int strength = actor.GetStatus("strength");
        int weak = actor.GetStatus("weak");
        int vigor = actor.GetStatus("vigor");
        int single = Display(actor.AttackSingle, strength, weak, vigor);
        int random = Display(actor.AttackRandom, strength, weak, vigor);
        int all = Display(actor.AttackAll, strength, weak, vigor);
        int singleHits = actor.AttackSingle.AddCount;
        int randomHits = actor.AttackRandom.AddCount;
        int allHits = actor.AttackAll.AddCount;
        int hits = singleHits + randomHits + allHits;

        if (hits == 0)
            return null;

        int pursuit = actor.GetStatus("pursuit");
        if (pursuit > 0)
        {
            all = unchecked(all + AttackDamageMath.AdjustForAttacker(
                pursuit, strength, weak, vigor));
            allHits++;
            hits++;
        }

        if (actor.GetStatus("omnistrike") > 0)
        {
            return new CombatIntentProjection(
                AttackSingle: null,
                AttackRandom: null,
                AttackAll: unchecked(single + random + all),
                AttackHits: hits,
                AttackSingleHits: null,
                AttackRandomHits: null,
                AttackAllHits: hits,
                Block: null,
                HasBuff: false,
                HasDebuff: false,
                HasHeal: false);
        }

        if (actor.GetStatus(BattleStatusIds.Disruption) > 0)
        {
            return new CombatIntentProjection(
                AttackSingle: null,
                AttackRandom: unchecked(single + random + all),
                AttackAll: null,
                AttackHits: hits,
                AttackSingleHits: null,
                AttackRandomHits: hits,
                AttackAllHits: null,
                Block: null,
                HasBuff: false,
                HasDebuff: false,
                HasHeal: false);
        }

        return new CombatIntentProjection(
            AttackSingle: singleHits > 0 ? single : null,
            AttackRandom: randomHits > 0 ? random : null,
            AttackAll: allHits > 0 ? all : null,
            AttackHits: hits,
            AttackSingleHits: singleHits > 0 ? singleHits : null,
            AttackRandomHits: randomHits > 0 ? randomHits : null,
            AttackAllHits: allHits > 0 ? allHits : null,
            Block: null,
            HasBuff: false,
            HasDebuff: false,
            HasHeal: false);
    }

    private static MoveDefinition? FindCurrentMove(
        CombatActor actor,
        DataCatalog catalog)
    {
        IReadOnlyList<MoveDefinition>? moves = null;
        if (actor.Side == ActorSide.Enemy
            && catalog.TryGetEnemy(actor.DefinitionId, out var enemy))
        {
            moves = enemy.Moves;
        }
        else if (actor.Side == ActorSide.Ally
            && catalog.TryGetUnit(actor.DefinitionId, out var unit))
        {
            moves = unit.Moves;
        }

        return moves?.FirstOrDefault(move => move.Id == actor.CurrentMoveId);
    }

    private static CombatIntentProjection? ProjectMove(
        BattleState state,
        CombatActor actor,
        MoveDefinition move)
    {
        int strength = actor.GetStatus("strength");
        int weak = actor.GetStatus("weak");
        int vigor = actor.GetStatus("vigor");
        int omnistrike = actor.GetStatus(BattleStatusIds.Omnistrike);
        int disruption = actor.GetStatus(BattleStatusIds.Disruption);
        int attackSingle = 0;
        int attackRandom = 0;
        int attackAll = 0;
        int singleHits = 0;
        int randomHits = 0;
        int allHits = 0;
        int block = 0;
        bool hasBuff = false;
        bool hasDebuff = false;
        bool hasHeal = false;

        int pursuit = actor.GetStatus("pursuit");
        if (pursuit > 0 && move.Effects.Any(effect => effect.Action == "attack"))
        {
            int adjustedPursuit = AttackDamageMath.AdjustForAttacker(
                pursuit, strength, weak, vigor);
            switch (AttackScopeRules.Resolve(
                EffectScope.All, omnistrike, disruption))
            {
                case EffectScope.Random:
                    attackRandom = adjustedPursuit;
                    randomHits = 1;
                    break;
                default:
                    attackAll = adjustedPursuit;
                    allHits = 1;
                    break;
            }
        }

        // Effects resolve in declaration order. A deterministic self status change
        // therefore changes the preview of only the attacks that follow it.
        foreach (var effect in move.Effects)
        {
            switch (effect.Action)
            {
                case "attack":
                    int adjusted = AttackDamageMath.AdjustForAttacker(
                        effect.Amount, strength, weak, vigor);
                    switch (AttackScopeRules.Resolve(
                        effect.Scope, omnistrike, disruption))
                    {
                        case EffectScope.Single:
                            attackSingle = unchecked(attackSingle + adjusted);
                            singleHits++;
                            break;
                        case EffectScope.Random:
                            attackRandom = unchecked(attackRandom + adjusted);
                            randomHits++;
                            break;
                        case EffectScope.All:
                            attackAll = unchecked(attackAll + adjusted);
                            allHits++;
                            break;
                    }
                    break;

                case "block":
                    block = unchecked(block + effect.Amount);
                    break;

                case "buff":
                    hasBuff = true;
                    ApplyDeterministicAttackerStatus(
                        state, actor, effect,
                        ref strength, ref weak, ref vigor,
                        ref omnistrike, ref disruption);
                    break;

                case "debuff":
                    hasDebuff = true;
                    ApplyDeterministicAttackerStatus(
                        state, actor, effect,
                        ref strength, ref weak, ref vigor,
                        ref omnistrike, ref disruption);
                    break;

                case "heal":
                    hasHeal = true;
                    break;
            }
        }

        int totalHits = singleHits + randomHits + allHits;
        if (totalHits == 0 && block == 0 && !hasBuff && !hasDebuff && !hasHeal)
            return null;

        return new CombatIntentProjection(
            AttackSingle: singleHits > 0 ? attackSingle : null,
            AttackRandom: randomHits > 0 ? attackRandom : null,
            AttackAll: allHits > 0 ? attackAll : null,
            AttackHits: totalHits > 0 ? totalHits : null,
            AttackSingleHits: singleHits > 0 ? singleHits : null,
            AttackRandomHits: randomHits > 0 ? randomHits : null,
            AttackAllHits: allHits > 0 ? allHits : null,
            Block: block > 0 ? block : null,
            HasBuff: hasBuff,
            HasDebuff: hasDebuff,
            HasHeal: hasHeal);
    }

    private static int Display(AttackPool pool, int strength, int weak, int vigor)
    {
        int sum = 0;
        foreach (int amount in pool.HitAmounts)
            sum = unchecked(sum + AttackDamageMath.AdjustForAttacker(
                amount, strength, weak, vigor));
        return sum;
    }

    private static void ApplyDeterministicAttackerStatus(
        BattleState state,
        CombatActor actor,
        CardEffect effect,
        ref int strength,
        ref int weak,
        ref int vigor,
        ref int omnistrike,
        ref int disruption)
    {
        if (!DefinitelyTargetsActor(state, actor, effect))
            return;

        foreach (var change in effect.EffectiveStatusChanges())
        {
            if (change.Name == "strength")
            {
                strength = unchecked(strength + change.Amount);
            }
            else if (change.Name == "weak")
            {
                int updated = unchecked(weak + change.Amount);
                weak = updated > 0 ? updated : 0;
            }
            else if (change.Name == "vigor")
            {
                int updated = unchecked(vigor + change.Amount);
                vigor = updated > 0 ? updated : 0;
            }
            else if (change.Name == BattleStatusIds.Omnistrike)
            {
                int updated = unchecked(omnistrike + change.Amount);
                omnistrike = updated > 0 ? updated : 0;
            }
            else if (change.Name == BattleStatusIds.Disruption)
            {
                int updated = unchecked(disruption + change.Amount);
                disruption = updated > 0 ? updated : 0;
            }
        }
    }

    /// <summary>
    /// Random status effects are included only when the actor is the sole possible
    /// target. This keeps the projection deterministic and does not consume RNG.
    /// </summary>
    private static bool DefinitelyTargetsActor(
        BattleState state,
        CombatActor actor,
        CardEffect effect)
    {
        if (effect.Scope == EffectScope.Self)
            return true;
        if (effect.Side != EffectSide.Ally)
            return false;

        var ownSide = actor.Side == ActorSide.Ally ? state.Allies : state.Enemies;
        return effect.Scope switch
        {
            EffectScope.All => actor.IsAlive,
            EffectScope.Random => ownSide.Count(candidate => candidate.IsAlive) == 1
                && actor.IsAlive,
            EffectScope.Single => SelectedOwnSideActor(state, actor.Side)?.InstanceId
                == actor.InstanceId,
            _ => false,
        };
    }

    private static CombatActor? SelectedOwnSideActor(
        BattleState state,
        ActorSide side)
    {
        if (side == ActorSide.Ally)
        {
            return state.TargetAllyIndex is { } index
                && index >= 0
                && index < state.Allies.Length
                    ? state.Allies[index]
                    : null;
        }

        return state.TargetEnemyIndex is { } enemyIndex
            && enemyIndex >= 0
            && enemyIndex < state.Enemies.Length
                ? state.Enemies[enemyIndex]
                : null;
    }
}
