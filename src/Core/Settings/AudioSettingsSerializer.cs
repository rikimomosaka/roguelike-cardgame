// src/Core/Settings/AudioSettingsSerializer.cs
using System;
using System.Text.Json;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Settings;

/// <summary>AudioSettings JSON のパース失敗を表す例外。</summary>
/// <remarks>VR 移植時はこの例外クラスを UdonSharpBehaviour のエラーフラグ文字列に置換する想定。</remarks>
public sealed class AudioSettingsSerializerException : Exception
{
    public AudioSettingsSerializerException(string message) : base(message) { }
    public AudioSettingsSerializerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>AudioSettings ⇔ JSON 文字列の変換。ファイル I/O は Server 側の Repository が担当。</summary>
public static class AudioSettingsSerializer
{
    public static string Serialize(AudioSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonOptions.Default);
    }

    public static AudioSettings Deserialize(string json)
    {
        AudioSettings? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize<AudioSettings>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new AudioSettingsSerializerException("AudioSettings JSON のパースに失敗しました。", ex);
        }

        if (deserialized is null)
            throw new AudioSettingsSerializerException("AudioSettings JSON が null として解釈されました。");

        if (deserialized.SchemaVersion != AudioSettings.CurrentSchemaVersion)
            throw new AudioSettingsSerializerException(
                $"未対応の schemaVersion: {deserialized.SchemaVersion} (対応: {AudioSettings.CurrentSchemaVersion})");

        try
        {
            return AudioSettings.Create(deserialized.Master, deserialized.Bgm, deserialized.Se, deserialized.Ambient);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new AudioSettingsSerializerException("AudioSettings の値が許容範囲外です。", ex);
        }
    }
}
