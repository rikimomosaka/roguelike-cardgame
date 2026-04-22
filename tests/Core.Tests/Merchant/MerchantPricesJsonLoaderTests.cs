using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Merchant;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Merchant;

public class MerchantPricesJsonLoaderTests
{
    private const string SampleJson = """
    {
      "cards":   { "Common": 50, "Rare": 80, "Epic": 150 },
      "relics":  { "Common": 150, "Rare": 250, "Epic": 350 },
      "potions": { "Common": 50, "Rare": 75, "Epic": 100 },
      "discardSlotPrice": 75
    }
    """;

    [Fact]
    public void Parse_Valid_ReturnsPrices()
    {
        var p = MerchantPricesJsonLoader.Parse(SampleJson);
        Assert.Equal(50, p.Cards[CardRarity.Common]);
        Assert.Equal(80, p.Cards[CardRarity.Rare]);
        Assert.Equal(150, p.Cards[CardRarity.Epic]);
        Assert.Equal(150, p.Relics[CardRarity.Common]);
        Assert.Equal(75, p.DiscardSlotPrice);
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<MerchantPricesJsonException>(() => MerchantPricesJsonLoader.Parse("{"));
    }

    [Fact]
    public void Parse_MissingRarity_Throws()
    {
        var bad = """{"cards":{"Common":50},"relics":{"Common":150,"Rare":250,"Epic":350},"potions":{"Common":50,"Rare":75,"Epic":100},"discardSlotPrice":75}""";
        Assert.Throws<MerchantPricesJsonException>(() => MerchantPricesJsonLoader.Parse(bad));
    }
}
