using RoguelikeCardGame.Core.Battle.Events;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Events;

public class BattleEventEmissionTests
{
    [Fact] public void Default_optional_fields_are_null()
    {
        var ev = new BattleEvent(BattleEventKind.TurnStart, Order: 0);
        Assert.Null(ev.CasterInstanceId);
        Assert.Null(ev.TargetInstanceId);
        Assert.Null(ev.Amount);
        Assert.Null(ev.CardId);
        Assert.Null(ev.Note);
    }

    [Fact] public void All_fields_assignable()
    {
        var ev = new BattleEvent(
            BattleEventKind.DealDamage, Order: 3,
            CasterInstanceId: "hero1", TargetInstanceId: "goblin1",
            Amount: 5, CardId: "strike", Note: "single");
        Assert.Equal(BattleEventKind.DealDamage, ev.Kind);
        Assert.Equal(3, ev.Order);
        Assert.Equal("hero1", ev.CasterInstanceId);
        Assert.Equal("goblin1", ev.TargetInstanceId);
        Assert.Equal(5, ev.Amount);
        Assert.Equal("strike", ev.CardId);
        Assert.Equal("single", ev.Note);
    }

    [Fact] public void Record_equality_holds()
    {
        var a = new BattleEvent(BattleEventKind.PlayCard, 0, CardId: "strike");
        var b = new BattleEvent(BattleEventKind.PlayCard, 0, CardId: "strike");
        Assert.Equal(a, b);
    }
}
