using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardEnumTests
{
    [Fact]
    public void CardRarity_HasFiveMembers_ZeroIsPromo()
    {
        Assert.Equal(0, (int)CardRarity.Promo);
        Assert.Equal(1, (int)CardRarity.Common);
        Assert.Equal(2, (int)CardRarity.Rare);
        Assert.Equal(3, (int)CardRarity.Epic);
        Assert.Equal(4, (int)CardRarity.Legendary);
    }

    [Fact]
    public void CardType_HasUnitAttackSkillPower()
    {
        var names = System.Enum.GetNames(typeof(CardType));
        Assert.Contains("Unit", names);
        Assert.Contains("Attack", names);
        Assert.Contains("Skill", names);
        Assert.Contains("Power", names);
        Assert.Equal(6, names.Length);
    }

    [Fact]
    public void CardType_includes_Status_value() =>
        Assert.True(System.Enum.IsDefined(typeof(CardType), CardType.Status));

    [Fact]
    public void CardType_includes_Curse_value() =>
        Assert.True(System.Enum.IsDefined(typeof(CardType), CardType.Curse));
}
