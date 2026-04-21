using System;

namespace RoguelikeCardGame.Server.Abstractions;

public sealed class AccountAlreadyExistsException : Exception
{
    public AccountAlreadyExistsException(string accountId)
        : base($"アカウント ID はすでに存在します: {accountId}")
    {
        AccountId = accountId;
    }

    public string AccountId { get; }
}
