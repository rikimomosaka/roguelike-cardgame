using System;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class AccountIdValidatorTests
{
    [Theory]
    [InlineData("player-001")]
    [InlineData("ABC_123")]
    [InlineData("日本語id")]
    [InlineData("a")]
    public void Validate_ValidId_DoesNotThrow(string id)
    {
        AccountIdValidator.Validate(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    [InlineData("../escape")]
    [InlineData("with:colon")]
    [InlineData("with*star")]
    [InlineData("with?question")]
    [InlineData("with|pipe")]
    [InlineData("with\"quote")]
    public void Validate_InvalidId_ThrowsArgumentException(string id)
    {
        Assert.Throws<ArgumentException>(() => AccountIdValidator.Validate(id));
    }

    [Fact]
    public void Validate_NullId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AccountIdValidator.Validate(null!));
    }

    [Fact]
    public void Validate_WithControlCharacter_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AccountIdValidator.Validate("tab\there"));
    }
}
