// tests/Core.Tests/Settings/AudioSettingsTests.cs
using System;
using RoguelikeCardGame.Core.Settings;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Settings;

public class AudioSettingsTests
{
    [Fact]
    public void Default_UsesExpectedInitialValues()
    {
        var d = AudioSettings.Default;
        Assert.Equal(AudioSettings.CurrentSchemaVersion, d.SchemaVersion);
        Assert.Equal(80, d.Master);
        Assert.Equal(70, d.Bgm);
        Assert.Equal(80, d.Se);
        Assert.Equal(60, d.Ambient);
    }

    [Fact]
    public void Create_WithValidValues_ReturnsSettings()
    {
        var s = AudioSettings.Create(master: 100, bgm: 0, se: 50, ambient: 25);
        Assert.Equal(AudioSettings.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Equal(100, s.Master);
        Assert.Equal(0, s.Bgm);
        Assert.Equal(50, s.Se);
        Assert.Equal(25, s.Ambient);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, "master")]
    [InlineData(101, 0, 0, 0, "master")]
    [InlineData(0, -1, 0, 0, "bgm")]
    [InlineData(0, 101, 0, 0, "bgm")]
    [InlineData(0, 0, -1, 0, "se")]
    [InlineData(0, 0, 101, 0, "se")]
    [InlineData(0, 0, 0, -1, "ambient")]
    [InlineData(0, 0, 0, 101, "ambient")]
    public void Create_WithOutOfRange_Throws(int master, int bgm, int se, int ambient, string expectedParamName)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioSettings.Create(master, bgm, se, ambient));
        Assert.Equal(expectedParamName, ex.ParamName);
    }

    [Fact]
    public void Create_AtBoundaries_Succeeds()
    {
        var lo = AudioSettings.Create(0, 0, 0, 0);
        Assert.Equal(0, lo.Master);
        Assert.Equal(0, lo.Bgm);
        Assert.Equal(0, lo.Se);
        Assert.Equal(0, lo.Ambient);

        var hi = AudioSettings.Create(100, 100, 100, 100);
        Assert.Equal(100, hi.Master);
        Assert.Equal(100, hi.Bgm);
        Assert.Equal(100, hi.Se);
        Assert.Equal(100, hi.Ambient);
    }
}
