using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class CombatActorTests
{
    private static CombatActor MakeHero(int hp = 70) =>
        new("hero1", "hero", ActorSide.Ally, 0, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, null,
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);

    [Fact] public void IsAlive_true_when_hp_positive()
    {
        var a = MakeHero(70);
        Assert.True(a.IsAlive);
    }

    [Fact] public void IsAlive_false_when_hp_zero()
    {
        var a = MakeHero(70) with { CurrentHp = 0 };
        Assert.False(a.IsAlive);
    }

    [Fact] public void IsAlive_false_when_hp_negative()
    {
        var a = MakeHero(70) with { CurrentHp = -5 };
        Assert.False(a.IsAlive);
    }

    [Fact] public void Record_equality_holds()
    {
        Assert.Equal(MakeHero(70), MakeHero(70));
    }

    [Fact] public void GetStatus_returns_zero_for_unknown()
    {
        var a = MakeHero();
        Assert.Equal(0, a.GetStatus("strength"));
    }

    [Fact] public void GetStatus_returns_amount_when_present()
    {
        var statuses = ImmutableDictionary<string, int>.Empty.Add("strength", 3);
        var a = MakeHero() with { Statuses = statuses };
        Assert.Equal(3, a.GetStatus("strength"));
    }

    [Fact] public void StatusSources_defaults_empty_and_only_returns_source_for_live_status()
    {
        var actor = MakeHero();
        Assert.Empty(actor.StatusSources);
        Assert.Null(actor.GetStatusSource("poison"));

        var source = new CombatSource(
            CombatSourceKind.CardEffect, 0, actor.InstanceId, actor.InstanceId,
            DefinitionId: "poison_card");
        var staleOnly = actor with
        {
            StatusSources = ImmutableDictionary<string, CombatSource>.Empty
                .Add("poison", source),
        };
        Assert.Null(staleOnly.GetStatusSource("poison"));
    }

    [Fact] public void Legacy_json_without_new_attribution_fields_restores_safe_defaults()
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(MakeHero()))!.AsObject();
        json.Remove(nameof(CombatActor.StatusSources));
        json.Remove(nameof(CombatActor.OwnerPlayerSlot));
        json.Remove(nameof(CombatActor.OwnerActorInstanceId));

        var restored = JsonSerializer.Deserialize<CombatActor>(json.ToJsonString());

        Assert.NotNull(restored);
        Assert.Empty(restored.StatusSources);
        Assert.Null(restored.OwnerPlayerSlot);
        Assert.Null(restored.OwnerActorInstanceId);
    }

    [Fact] public void Record_inequality_when_statuses_differ()
    {
        var a = MakeHero();
        var b = MakeHero() with { Statuses = ImmutableDictionary<string, int>.Empty.Add("weak", 1) };
        Assert.NotEqual(a, b);
    }

    // === 10.2.D: Lifetime / AssociatedSummonHeldInstanceId ===

    [Fact] public void RemainingLifetimeTurns_null_means_permanent()
    {
        var hero = BattleFixtures.Hero();
        Assert.Null(hero.RemainingLifetimeTurns);
    }

    [Fact] public void AssociatedSummonHeldInstanceId_null_for_hero()
    {
        var hero = BattleFixtures.Hero();
        Assert.Null(hero.AssociatedSummonHeldInstanceId);
    }

    [Fact] public void Record_equality_distinguishes_lifetime_field()
    {
        var hero = BattleFixtures.Hero();
        var copy = hero with { RemainingLifetimeTurns = 3 };
        Assert.NotEqual(hero, copy);
    }

    [Fact] public void Record_equality_distinguishes_associated_summon_held()
    {
        var hero = BattleFixtures.Hero();
        var copy = hero with { AssociatedSummonHeldInstanceId = "card_x" };
        Assert.NotEqual(hero, copy);
    }
}
