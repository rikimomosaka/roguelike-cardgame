using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
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
        if (actor.DefinitionId == "hero")
            return ProjectQueuedHeroAttacks(actor);

        if (actor.CurrentMoveId is null)
            return null;

        var move = FindCurrentMove(actor, catalog);
        return move is null ? null : ProjectMove(state, actor, move);
    }

    private static CombatIntentProjection? ProjectQueuedHeroAttacks(CombatActor actor)
    {
        int strength = actor.GetStatus("strength");
        int weak = actor.GetStatus("weak");
        int single = actor.AttackSingle.Display(strength, weak);
        int random = actor.AttackRandom.Display(strength, weak);
        int all = actor.AttackAll.Display(strength, weak);
        int hits = actor.AttackSingle.AddCount
            + actor.AttackRandom.AddCount
            + actor.AttackAll.AddCount;

        if (hits == 0)
            return null;

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

        return new CombatIntentProjection(
            AttackSingle: actor.AttackSingle.AddCount > 0 ? single : null,
            AttackRandom: actor.AttackRandom.AddCount > 0 ? random : null,
            AttackAll: actor.AttackAll.AddCount > 0 ? all : null,
            AttackHits: hits,
            AttackSingleHits: actor.AttackSingle.AddCount > 0
                ? actor.AttackSingle.AddCount
                : null,
            AttackRandomHits: actor.AttackRandom.AddCount > 0
                ? actor.AttackRandom.AddCount
                : null,
            AttackAllHits: actor.AttackAll.AddCount > 0
                ? actor.AttackAll.AddCount
                : null,
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

        // Effects resolve in declaration order. A deterministic self status change
        // therefore changes the preview of only the attacks that follow it.
        foreach (var effect in move.Effects)
        {
            switch (effect.Action)
            {
                case "attack":
                    int adjusted = AttackDamageMath.AdjustForAttacker(
                        effect.Amount, strength, weak);
                    switch (effect.Scope)
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
                        state, actor, effect, ref strength, ref weak);
                    break;

                case "debuff":
                    hasDebuff = true;
                    ApplyDeterministicAttackerStatus(
                        state, actor, effect, ref strength, ref weak);
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

    private static void ApplyDeterministicAttackerStatus(
        BattleState state,
        CombatActor actor,
        CardEffect effect,
        ref int strength,
        ref int weak)
    {
        if (!DefinitelyTargetsActor(state, actor, effect))
            return;

        if (effect.Name == "strength")
        {
            strength = unchecked(strength + effect.Amount);
        }
        else if (effect.Name == "weak")
        {
            int updated = unchecked(weak + effect.Amount);
            weak = updated > 0 ? updated : 0;
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
