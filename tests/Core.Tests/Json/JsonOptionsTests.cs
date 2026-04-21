// tests/Core.Tests/Json/JsonOptionsTests.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using RoguelikeCardGame.Core.Json;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Json;

public class JsonOptionsTests
{
    [Fact]
    public void Default_UsesCamelCasePolicy()
    {
        Assert.Same(JsonNamingPolicy.CamelCase, JsonOptions.Default.PropertyNamingPolicy);
    }

    [Fact]
    public void Default_DisallowsUnmappedMembers()
    {
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, JsonOptions.Default.UnmappedMemberHandling);
    }

    [Fact]
    public void Default_WritesCompactJson()
    {
        Assert.False(JsonOptions.Default.WriteIndented);
    }

    [Fact]
    public void Default_IncludesStringEnumConverter()
    {
        Assert.Contains(JsonOptions.Default.Converters, c => c is JsonStringEnumConverter);
    }
}
