using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardKeywordsTests
{
    [Fact]
    public void Wild_keyword_has_jp_name_and_desc()
    {
        var meta = CardKeywords.Get("wild");
        Assert.NotNull(meta);
        Assert.Equal("ワイルド", meta!.Name);
        Assert.Contains("敵", meta.Description);
    }

    [Fact]
    public void Superwild_keyword_has_jp_name()
    {
        var meta = CardKeywords.Get("superwild");
        Assert.NotNull(meta);
        Assert.Equal("スーパーワイルド", meta!.Name);
    }

    [Fact]
    public void Unknown_keyword_returns_null()
    {
        Assert.Null(CardKeywords.Get("nonexistent"));
    }

    [Fact]
    public void All_exposes_known_keywords()
    {
        var all = CardKeywords.All;
        Assert.Contains("wild", all.Keys);
        Assert.Contains("superwild", all.Keys);
    }
}
