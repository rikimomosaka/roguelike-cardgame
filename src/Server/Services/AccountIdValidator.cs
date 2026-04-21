using System;
using System.IO;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// accountId がファイル名・HTTP ルート両面で安全な文字列であることを検証する共通ユーティリティ。
/// </summary>
public static class AccountIdValidator
{
    public static void Validate(string accountId)
    {
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
}
