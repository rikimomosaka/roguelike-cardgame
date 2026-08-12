using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Events;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class AttackPoolTests
{
    [Fact] public void Empty_has_zero_sum_and_count()
    {
        var p = AttackPool.Empty;
        Assert.Equal(0, p.Sum);
        Assert.Equal(0, p.AddCount);
        Assert.Equal(0, p.RawTotal);
    }

    [Fact] public void Add_increments_sum_and_addcount()
    {
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(8, p.Sum);
        Assert.Equal(2, p.AddCount);
        Assert.Equal(8, p.RawTotal);
        Assert.Equal(new[] { 5, 3 }, p.HitAmounts);
    }

    [Fact] public void RawTotal_equals_Sum()
    {
        var p = AttackPool.Empty.Add(7).Add(0).Add(11);
        Assert.Equal(p.Sum, p.RawTotal);
    }

    [Fact] public void Add_zero_still_increments_addcount()
    {
        // 力バフ遡及計算 (10.2.B) で AddCount × strength が乗るため、amount=0 でも AddCount は +1
        var p = AttackPool.Empty.Add(0);
        Assert.Equal(0, p.Sum);
        Assert.Equal(1, p.AddCount);
    }

    [Fact] public void Legacy_unattributed_hit_has_no_player_owner_slot()
    {
        var hit = Assert.Single(AttackPool.Empty.Add(5).Hits);

        Assert.Equal(CombatSourceKind.Unattributed, hit.Source.Kind);
        Assert.Null(hit.Source.OwnerSlot);
    }

    [Fact] public void Display_no_status_returns_sum()
    {
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(8, p.Display(strength: 0, weak: 0));
    }

    [Fact] public void Display_strength_adds_per_addcount()
    {
        // Sum=8, AddCount=2, strength=3 → 8 + 2*3 = 14
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(14, p.Display(strength: 3, weak: 0));
    }

    [Fact] public void Display_weak_applies_three_quarters_floor()
    {
        // Sum=10, AddCount=1, strength=0, weak=1 → floor(10 * 0.75) = 7
        var p = AttackPool.Empty.Add(10);
        Assert.Equal(7, p.Display(strength: 0, weak: 1));
    }

    [Fact] public void Display_weak_with_strength_is_calculated_per_hit()
    {
        // (5+3)*0.75=6, (3+3)*0.75=4。合計14へ一度だけ掛けた10ではなく、6+4。
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(10, p.Display(strength: 3, weak: 1));
    }

    [Fact] public void Display_weak_rounds_each_hit_before_summing()
    {
        // floor(5*0.75) + floor(5*0.75) = 3+3。合計10から計算した7にはしない。
        var p = AttackPool.Empty.Add(5).Add(5);
        Assert.Equal(6, p.Display(strength: 0, weak: 1));
    }

    [Fact] public void Display_clamps_each_negative_hit_to_zero()
    {
        var p = AttackPool.Empty.Add(-5).Add(4);
        Assert.Equal(4, p.Display(strength: 0, weak: 0));
    }

    [Fact] public void Display_zero_when_empty()
    {
        Assert.Equal(0, AttackPool.Empty.Display(strength: 5, weak: 0));
    }

    [Fact] public void Operator_plus_sums_both_fields()
    {
        var a = AttackPool.Empty.Add(5).Add(3);   // Sum=8, AddCount=2
        var b = AttackPool.Empty.Add(2);          // Sum=2, AddCount=1
        var c = a + b;                            // Sum=10, AddCount=3
        Assert.Equal(10, c.Sum);
        Assert.Equal(3, c.AddCount);
        Assert.Equal(new[] { 5, 3, 2 }, c.HitAmounts);
    }

    [Fact] public void Operator_plus_with_empty_returns_other()
    {
        var a = AttackPool.Empty.Add(5);
        var c = a + AttackPool.Empty;
        Assert.Equal(a, c);
    }

    [Fact] public void Constructor_preserves_explicit_hit_order()
    {
        var p = new AttackPool(ImmutableArray.Create(2, 8, 3));

        Assert.Equal(new[] { 2, 8, 3 }, p.HitAmounts);
        Assert.Equal(13, p.Sum);
        Assert.Equal(3, p.AddCount);
    }

    [Fact] public void Pools_with_same_hit_values_have_value_equality()
    {
        var a = AttackPool.Empty.Add(2).Add(8);
        var b = AttackPool.Empty.Add(2).Add(8);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact] public void Json_round_trip_preserves_hit_order()
    {
        var original = AttackPool.Empty.Add(2).Add(8).Add(3);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AttackPool>(json);

        Assert.Equal(original, restored);
        Assert.Equal(new[] { 2, 8, 3 }, restored.HitAmounts);
    }

    [Fact] public void Legacy_sum_count_json_is_rejected_instead_of_becoming_empty()
    {
        const string legacyJson = "{\"Sum\":8,\"AddCount\":2}";

        Assert.Throws<System.ArgumentException>(() =>
            JsonSerializer.Deserialize<AttackPool>(legacyJson));
    }
}
