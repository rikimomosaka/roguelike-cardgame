using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// Server-owned receipt for one externally supplied idempotency key.  The key is
/// deliberately not a replay journal entry id: a client is free to choose any
/// printable command id without being able to collide with journal internals.
/// </summary>
public sealed record BattleCommandReceipt(
    string ExternalCommandId,
    string PayloadFingerprint);

internal static class BattleCommandFingerprint
{
    /// <summary>
    /// Produces a stable fingerprint from a normalized command payload. Field
    /// ordering at the call site is irrelevant; collection ordering remains
    /// significant unless the caller explicitly normalizes that collection.
    /// </summary>
    public static string Create(
        string commandKind,
        params (string Name, object? Value)[] fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandKind);
        ArgumentNullException.ThrowIfNull(fields);

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["commandKind"] = commandKind,
        };
        foreach (var (name, value) in fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!payload.TryAdd(name, value))
                throw new ArgumentException($"Duplicate command field '{name}'.", nameof(fields));
        }

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToHexString(SHA256.HashData(json));
    }

    public static string[] NormalizeUnorderedIds(IEnumerable<string>? ids) =>
        (ids ?? [])
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
}

internal static class BattleCommandReceiptStore
{
    public static ImmutableDictionary<string, BattleCommandReceipt> Empty { get; } =
        ImmutableDictionary.Create<string, BattleCommandReceipt>(StringComparer.Ordinal);

    public static ImmutableDictionary<string, BattleCommandReceipt> Add(
        ImmutableDictionary<string, BattleCommandReceipt>? receipts,
        string externalCommandId,
        string payloadFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalCommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadFingerprint);
        receipts ??= Empty;

        if (receipts.TryGetValue(externalCommandId, out var existing))
        {
            if (string.Equals(
                    existing.PayloadFingerprint,
                    payloadFingerprint,
                    StringComparison.Ordinal))
                return receipts;

            throw new InvalidOperationException(
                $"External battle command '{externalCommandId}' already has a different receipt.");
        }

        return receipts.Add(
            externalCommandId,
            new BattleCommandReceipt(externalCommandId, payloadFingerprint));
    }
}
