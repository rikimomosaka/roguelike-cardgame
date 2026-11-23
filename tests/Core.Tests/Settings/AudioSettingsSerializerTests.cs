// tests/Core.Tests/Settings/AudioSettingsSerializerTests.cs
using RoguelikeCardGame.Core.Settings;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Settings;

public class AudioSettingsSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesAllValues()
    {
        var original = AudioSettings.Create(master: 10, bgm: 20, se: 30, voice: 40);
        var json = AudioSettingsSerializer.Serialize(original);
        var restored = AudioSettingsSerializer.Deserialize(json);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Serialize_UsesCamelCaseFieldNames()
    {
        var json = AudioSettingsSerializer.Serialize(AudioSettings.Default);
        Assert.Contains("\"schemaVersion\":2", json);
        Assert.Contains("\"master\":80", json);
        Assert.Contains("\"bgm\":70", json);
        Assert.Contains("\"se\":80", json);
        Assert.Contains("\"voice\":80", json);
    }

    [Fact]
    public void Deserialize_UnknownField_Throws()
    {
        var json = "{\"schemaVersion\":2,\"master\":80,\"bgm\":70,\"se\":80,\"voice\":80,\"extra\":1}";
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_WrongSchemaVersion_Throws()
    {
        var json = "{\"schemaVersion\":999,\"master\":80,\"bgm\":70,\"se\":80,\"voice\":80}";
        var ex = Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Fact]
    public void Deserialize_OutOfRangeValue_Throws()
    {
        var json = "{\"schemaVersion\":2,\"master\":101,\"bgm\":0,\"se\":0,\"voice\":0}";
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

    [Fact]
    public void Deserialize_V1_MigratesEstablishedChannelsAndDefaultsVoice()
    {
        var json = "{\"schemaVersion\":1,\"master\":11,\"bgm\":22,\"se\":33,\"ambient\":44}";

        var migrated = AudioSettingsSerializer.Deserialize(json);

        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Equal(11, migrated.Master);
        Assert.Equal(22, migrated.Bgm);
        Assert.Equal(33, migrated.Se);
        Assert.Equal(80, migrated.Voice);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"master\":80,\"bgm\":70,\"se\":80}")]
    [InlineData("{\"schemaVersion\":1,\"master\":80,\"bgm\":70,\"se\":80}")]
    public void Deserialize_MissingRequiredField_Throws(string json)
    {
        Assert.Throws<AudioSettingsSerializerException>(() => AudioSettingsSerializer.Deserialize(json));
    }
}
