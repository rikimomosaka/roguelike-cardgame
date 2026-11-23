using System.Linq;
using RoguelikeCardGame.Core.Battle.Statuses;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Statuses;

public class StatusDefinitionTests
{
    [Fact] public void All_contains_supported_statuses()
    {
        Assert.Equal(41, StatusDefinition.All.Count);
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
        Assert.Contains(BattleStatusIds.Vigor, ids);
        Assert.Contains(BattleStatusIds.Pursuit, ids);
        Assert.Contains(BattleStatusIds.Regeneration, ids);
        Assert.Contains(BattleStatusIds.Fortify, ids);
        Assert.Contains(BattleStatusIds.Resistance, ids);
        Assert.Contains(BattleStatusIds.Revive, ids);
        Assert.Contains(BattleStatusIds.Growth, ids);
        Assert.Contains(BattleStatusIds.Insight, ids);
        Assert.Contains(BattleStatusIds.Haste, ids);
        Assert.Contains(BattleStatusIds.Continuity, ids);
        Assert.Contains(BattleStatusIds.Mastery, ids);
        Assert.Contains(BattleStatusIds.Afterimage, ids);
        Assert.Contains(BattleStatusIds.Steadfast, ids);
        Assert.Contains(BattleStatusIds.Cover, ids);
        Assert.Contains(BattleStatusIds.DefensiveStance, ids);
        Assert.Contains(BattleStatusIds.ComboCarryover, ids);
        Assert.Contains(BattleStatusIds.Afterglow, ids);
        Assert.Contains(BattleStatusIds.Resonance, ids);
        Assert.Contains(BattleStatusIds.VirulentPoison, ids);
        Assert.Contains(BattleStatusIds.Stagnation, ids);
        Assert.Contains(BattleStatusIds.Burn, ids);
        Assert.Contains(BattleStatusIds.Confusion, ids);
        Assert.Contains(BattleStatusIds.Curse, ids);
        Assert.Contains(BattleStatusIds.Bind, ids);
        Assert.Contains(BattleStatusIds.Silence, ids);
        Assert.Contains(BattleStatusIds.Seal, ids);
        Assert.Contains(BattleStatusIds.Exhaustion, ids);
        Assert.Contains(BattleStatusIds.Distress, ids);
        Assert.Contains(BattleStatusIds.Scorch, ids);
        Assert.Contains(BattleStatusIds.Disruption, ids);
        Assert.True(ids.SetEquals(BattleStatusTextCatalog.All.Keys));
        Assert.All(BattleStatusTextCatalog.All.Values, text =>
        {
            Assert.False(string.IsNullOrWhiteSpace(text.Name));
            Assert.False(string.IsNullOrWhiteSpace(text.Description));
        });
    }

    [Theory]
    [InlineData(BattleStatusIds.Afterimage)]
    [InlineData(BattleStatusIds.Steadfast)]
    [InlineData(BattleStatusIds.Cover)]
    [InlineData(BattleStatusIds.DefensiveStance)]
    [InlineData(BattleStatusIds.ComboCarryover)]
    [InlineData(BattleStatusIds.Afterglow)]
    [InlineData(BattleStatusIds.Resonance)]
    public void New_class_buffs_use_explicit_lifecycle_hooks(string id)
    {
        var status = StatusDefinition.Get(id);

        Assert.Equal(StatusKind.Buff, status.Kind);
        Assert.False(status.IsPermanent);
        Assert.Equal(StatusTickDirection.None, status.TickDirection);
    }

    [Fact]
    public void Disruption_is_a_decrementing_debuff()
    {
        var status = StatusDefinition.Get(BattleStatusIds.Disruption);

        Assert.Equal(StatusKind.Debuff, status.Kind);
        Assert.False(status.IsPermanent);
        Assert.Equal(StatusTickDirection.Decrement, status.TickDirection);
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
