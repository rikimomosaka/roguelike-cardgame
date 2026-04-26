using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierTests
{
    private static BattleState BasicState() => new(
        Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero()),
        Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
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

    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    [Fact] public void Attack_single_adds_to_caster_AttackSingle()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(6, next.Allies[0].AttackSingle.Sum);
        Assert.Equal(1, next.Allies[0].AttackSingle.AddCount);
    }

    [Fact] public void Attack_random_adds_to_caster_AttackRandom()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 4);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(4, next.Allies[0].AttackRandom.Sum);
    }

    [Fact] public void Attack_all_adds_to_caster_AttackAll()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(3, next.Allies[0].AttackAll.Sum);
    }

    [Fact] public void Block_self_adds_to_caster_block()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("block", EffectScope.Self, null, 5);
        var (next, events) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(5, next.Allies[0].Block.Sum);
        Assert.Contains(events, e => e.Kind == BattleEventKind.GainBlock && e.Amount == 5);
    }

    [Fact] public void Unimplemented_action_is_noop()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, events) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        // 状態変化なし、イベント emission なし (10.2.A スコープ外)
        Assert.Equal(s, next);
        Assert.Empty(events);
    }
}
