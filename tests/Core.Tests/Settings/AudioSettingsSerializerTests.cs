// tests/Core.Tests/Settings/AudioSettingsSerializerTests.cs
using RoguelikeCardGame.Core.Settings;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Settings;

public class AudioSettingsSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesAllValues()
    {
        var original = AudioSettings.Create(master: 10, bgm: 20, se: 30, ambient: 40);
        var json = AudioSettingsSerializer.Serialize(original);
        var restored = AudioSettingsSerializer.Deserialize(json);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Serialize_UsesCamelCaseFieldNames()
    {
        var json = AudioSettingsSerializer.Serialize(AudioSettings.Default);
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"master\":80", json);
        Assert.Contains("\"bgm\":70", json);
        Assert.Contains("\"se\":80", json);
        Assert.Contains("\"ambient\":60", json);
    }

    [Fact]
    public void Deserialize_UnknownField_Throws()
    {
        var json = "{\"schemaVersion\":1,\"master\":80,\"bgm\":70,\"se\":80,\"ambient\":60,\"extra\":1}";
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_WrongSchemaVersion_Throws()
    {
        var json = "{\"schemaVersion\":999,\"master\":80,\"bgm\":70,\"se\":80,\"ambient\":60}";
        var ex = Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Fact]
    public void Deserialize_OutOfRangeValue_Throws()
    {
        var json = "{\"schemaVersion\":1,\"master\":101,\"bgm\":0,\"se\":0,\"ambient\":0}";
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_InvalidJson_Throws()
    {
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize("not json"));
    }

    [Fact]
    public void Deserialize_NullLiteral_Throws()
    {
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize("null"));
    }
}
