using System;
using System.IO;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// accountId がファイル名・HTTP ルート両面で安全な文字列であることを検証する共通ユーティリティ。
/// </summary>
public static class AccountIdValidator
{
    private static readonly char[] PortableInvalidFileNameChars =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly string[] WindowsDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    public static void Validate(string accountId)
    {
        // Path.GetInvalidFileNameChars differs by OS. Enforce one portable contract so an ID
        // accepted on Linux cannot become an invalid path or a filesystem alias on Windows.
        if (accountId is not null && IsNonPortableFileName(accountId))
        {
            throw new ArgumentException(
                $"accountId is not a portable file name: {accountId}", nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("accountId は空にできません。", nameof(accountId));

        if (accountId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(
                $"accountId にファイル名として使えない文字が含まれています: {accountId}",
                nameof(accountId));

        if (accountId.Contains('/') || accountId.Contains('\\'))
            throw new ArgumentException(
                $"accountId にパス区切り文字が含まれています: {accountId}",
                nameof(accountId));
    }

    private static bool IsNonPortableFileName(string accountId)
    {
        if (accountId.IndexOfAny(PortableInvalidFileNameChars) >= 0
            || accountId.EndsWith(' ')
            || accountId.EndsWith('.'))
        {
            return true;
        }

        foreach (var ch in accountId)
        {
            if (char.IsControl(ch)) return true;
        }

        var firstNamePart = accountId.Split('.')[0];
        return Array.Exists(
            WindowsDeviceNames,
            name => string.Equals(name, firstNamePart, StringComparison.OrdinalIgnoreCase));
    }
}
