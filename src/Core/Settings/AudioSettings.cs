// src/Core/Settings/AudioSettings.cs
using System;

namespace RoguelikeCardGame.Core.Settings;

/// <summary>
/// プレイヤーごとの音量設定。値は 0–100（検証済み）。
/// </summary>
/// <remarks>
/// VRChat (Udon#) 移植時は record → sealed class に変換し、
/// 各フィールドは PlayerData の個別 key（例 "audio.master"）に分解する想定。
/// </remarks>
public sealed record AudioSettings(
    int SchemaVersion,
    int Master,
    int Bgm,
    int Se,
    int Ambient)
{
    public const int CurrentSchemaVersion = 1;

    public static AudioSettings Default =>
        new(CurrentSchemaVersion, Master: 80, Bgm: 70, Se: 80, Ambient: 60);

    /// <summary>0–100 の範囲外を拒否する検証付きファクトリ。</summary>
    public static AudioSettings Create(int master, int bgm, int se, int ambient)
    {
        ValidateRange(master, nameof(master));
        ValidateRange(bgm, nameof(bgm));
        ValidateRange(se, nameof(se));
        ValidateRange(ambient, nameof(ambient));
        return new AudioSettings(CurrentSchemaVersion, master, bgm, se, ambient);
    }

    private static void ValidateRange(int value, string paramName)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(paramName, value, "値は 0–100 の範囲内である必要があります。");
    }
}
