using System.Linq;
using RoguelikeCardGame.Core.Battle.Statuses;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Statuses;

public class StatusDefinitionTests
{
    [Fact] public void All_contains_supported_statuses()
    {
        Assert.Equal(11, StatusDefinition.All.Count);
        var ids = StatusDefinition.All.Select(s => s.Id).ToHashSet();
        Assert.Contains("strength",   ids);
        Assert.Contains("dexterity",  ids);
        Assert.Contains("omnistrike", ids);
        Assert.Contains("vulnerable", ids);
        Assert.Contains("weak",       ids);
        Assert.Contains("poison",     ids);
        Assert.Contains("barrier",    ids);
        Assert.Contains("lifesteal",  ids);
        Assert.Contains("reflect",    ids);
        Assert.Contains("handCostReduction", ids);
        Assert.Contains("handCostIncrease", ids);
    }

    [Fact] public void Strength_is_permanent_buff()
    {
        var s = StatusDefinition.Get("strength");
        Assert.Equal(StatusKind.Buff, s.Kind);
        Assert.True(s.IsPermanent);
        Assert.Equal(StatusTickDirection.None, s.TickDirection);
    }

    [Fact] public void Dexterity_is_permanent_buff()
    {
        var s = StatusDefinition.Get("dexterity");
        Assert.Equal(StatusKind.Buff, s.Kind);
        Assert.True(s.IsPermanent);
        Assert.Equal(StatusTickDirection.None, s.TickDirection);
    }

    [Fact] public void Omnistrike_is_decrementing_buff()
    {
        var s = StatusDefinition.Get("omnistrike");
        Assert.Equal(StatusKind.Buff, s.Kind);
        Assert.False(s.IsPermanent);
        Assert.Equal(StatusTickDirection.Decrement, s.TickDirection);
    }

    [Fact] public void Vulnerable_is_decrementing_debuff()
    {
        var s = StatusDefinition.Get("vulnerable");
        Assert.Equal(StatusKind.Debuff, s.Kind);
        Assert.False(s.IsPermanent);
        Assert.Equal(StatusTickDirection.Decrement, s.TickDirection);
    }

    [Fact] public void Weak_is_decrementing_debuff()
    {
        var s = StatusDefinition.Get("weak");
        Assert.Equal(StatusKind.Debuff, s.Kind);
        Assert.False(s.IsPermanent);
        Assert.Equal(StatusTickDirection.Decrement, s.TickDirection);
    }

    [Fact] public void Poison_is_non_countdown_debuff()
    {
        // Phase 10.5.M6.5: poison は SideStatusCountdown 対象から外され、
        // TurnStartProcessor.ApplyPoisonTick 内で「ダメージ後に -1」される。
        var s = StatusDefinition.Get("poison");
        Assert.Equal(StatusKind.Debuff, s.Kind);
        Assert.False(s.IsPermanent);
        Assert.Equal(StatusTickDirection.None, s.TickDirection);
    }

    [Fact] public void Get_unknown_throws()
    {
        Assert.Throws<System.InvalidOperationException>(() => StatusDefinition.Get("unknown"));
    }

    [Theory]
    [InlineData("handCostReduction", StatusKind.Buff)]
    [InlineData("handCostIncrease", StatusKind.Debuff)]
    public void Hand_cost_statuses_are_permanent_and_do_not_count_down(
        string id, StatusKind expectedKind)
    {
        var status = StatusDefinition.Get(id);

        Assert.Equal(expectedKind, status.Kind);
        Assert.True(status.IsPermanent);
        Assert.Equal(StatusTickDirection.None, status.TickDirection);
    }
}
