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
        return Task.FromResult(File.Exists(PathFor(accountId)));
    }

    public async Task CreateAsync(string accountId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (File.Exists(path))
            throw new AccountAlreadyExistsException(accountId);

        var account = new Account(accountId, nowUtc);
        var json = JsonSerializer.Serialize(account, JsonOptions.Default);
        await WriteAtomicAsync(path, json, ct);
    }

    public async Task<Account?> GetAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return JsonSerializer.Deserialize<Account>(json, JsonOptions.Default);
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");

    private static async Task WriteAtomicAsync(string finalPath, string contents, CancellationToken ct)
    {
        var tmp = finalPath + ".tmp";
        await File.WriteAllTextAsync(tmp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, finalPath, overwrite: true);
    }
}
