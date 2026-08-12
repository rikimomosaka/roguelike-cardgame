using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Json;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/accounts/{accountId}.json</c> にアカウントメタデータを永続化する。</summary>
public sealed class FileAccountRepository : IAccountRepository
{
    private readonly string _dir;
    private readonly SemaphoreSlim _createGate = new(initialCount: 1, maxCount: 1);

    public FileAccountRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory が未設定です。", nameof(options));
        _dir = Path.Combine(root, "accounts");
        Directory.CreateDirectory(_dir);
    }

    public Task<bool> ExistsAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(FindCaseInsensitivePath(accountId) is not null);
    }

    public async Task CreateAsync(string accountId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        await _createGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_dir);
            if (FindCaseInsensitivePath(accountId) is not null)
                throw new AccountAlreadyExistsException(accountId);

            var path = PathFor(accountId);
            var account = new Account(accountId, nowUtc);
            var json = JsonSerializer.Serialize(account, JsonOptions.Default);
            await WriteAtomicCreateAsync(path, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _createGate.Release();
        }
    }

    public async Task<Account?> GetAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ct.ThrowIfCancellationRequested();
        var path = FindCaseInsensitivePath(accountId);
        if (path is null) return null;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<Account>(json, JsonOptions.Default);
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");

    private string? FindCaseInsensitivePath(string accountId)
    {
        if (!Directory.Exists(_dir)) return null;

        string? match = null;
        foreach (var path in Directory.EnumerateFiles(_dir, "*", SearchOption.TopDirectoryOnly))
        {
            if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    Path.GetFileNameWithoutExtension(path), accountId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null && !string.Equals(match, path, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Case-insensitive duplicate account files exist for accountId: {accountId}");
            }

            match = path;
        }

        return match;
    }

    private static async Task WriteAtomicCreateAsync(
        string finalPath, string contents, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Account path has no parent directory.");
        var tmp = Path.Combine(
            directory,
            $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                    tmp, contents,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct)
                .ConfigureAwait(false);
            // Never overwrite an account created after our existence check.
            File.Move(tmp, finalPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
