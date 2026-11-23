using System;
using System.Text.Json;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Settings;

/// <summary>An error raised when persisted audio settings cannot be validated.</summary>
public sealed class AudioSettingsSerializerException : Exception
{
    public AudioSettingsSerializerException(string message) : base(message) { }
    public AudioSettingsSerializerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Strict serialization and migration for per-account audio settings.</summary>
public static class AudioSettingsSerializer
{
    private const int LegacySchemaVersion = 1;

    private sealed record AudioSettingsV1(
        int? SchemaVersion,
        int? Master,
        int? Bgm,
        int? Se,
        int? Ambient);

    private sealed record AudioSettingsV2(
        int? SchemaVersion,
        int? Master,
        int? Bgm,
        int? Se,
        int? Voice);

    public static string Serialize(AudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validated = AudioSettings.Create(settings.Master, settings.Bgm, settings.Se, settings.Voice);
        return JsonSerializer.Serialize(validated, JsonOptions.Default);
    }

    public static AudioSettings Deserialize(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new AudioSettingsSerializerException("AudioSettings JSON must be an object.");

            if (!document.RootElement.TryGetProperty("schemaVersion", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var schemaVersion))
            {
                throw new AudioSettingsSerializerException(
                    "AudioSettings schemaVersion is required and must be an integer.");
            }

            return schemaVersion switch
            {
                LegacySchemaVersion => DeserializeV1(json),
                AudioSettings.CurrentSchemaVersion => DeserializeV2(json),
                _ => throw new AudioSettingsSerializerException(
                    $"Unsupported schemaVersion: {schemaVersion} (current: {AudioSettings.CurrentSchemaVersion})"),
            };
        }
        catch (JsonException ex)
        {
            throw new AudioSettingsSerializerException("Failed to parse AudioSettings JSON.", ex);
        }
    }

    private static AudioSettings DeserializeV1(string json)
    {
        var legacy = JsonSerializer.Deserialize<AudioSettingsV1>(json, JsonOptions.Default)
            ?? throw new AudioSettingsSerializerException("AudioSettings JSON resolved to null.");
        Require(legacy.SchemaVersion, "schemaVersion");
        var master = Require(legacy.Master, "master");
        var bgm = Require(legacy.Bgm, "bgm");
        var se = Require(legacy.Se, "se");
        _ = Require(legacy.Ambient, "ambient");

        // Ambient never had a playback bus. Preserve the established channels and
        // initialize the newly introduced voice bus to its documented default.
        return CreateValidated(master, bgm, se, AudioSettings.Default.Voice);
    }

    private static AudioSettings DeserializeV2(string json)
    {
        var current = JsonSerializer.Deserialize<AudioSettingsV2>(json, JsonOptions.Default)
            ?? throw new AudioSettingsSerializerException("AudioSettings JSON resolved to null.");
        Require(current.SchemaVersion, "schemaVersion");
        return CreateValidated(
            Require(current.Master, "master"),
            Require(current.Bgm, "bgm"),
            Require(current.Se, "se"),
            Require(current.Voice, "voice"));
    }

    private static int Require(int? value, string propertyName)
    {
        if (value is null)
            throw new AudioSettingsSerializerException(
                $"AudioSettings property '{propertyName}' is required.");
        return value.Value;
    }

    private static AudioSettings CreateValidated(int master, int bgm, int se, int voice)
    {
        try
        {
            return AudioSettings.Create(master, bgm, se, voice);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new AudioSettingsSerializerException(
                "AudioSettings contains a value outside 0..100.", ex);
        }
    }
}
