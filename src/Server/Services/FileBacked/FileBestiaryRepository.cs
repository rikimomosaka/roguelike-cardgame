using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/bestiary/{accountId}.json</c> にアカウント単位 Bestiary を保存。</summary>
public sealed class FileBestiaryRepository : IBestiaryRepository
{
    private readonly string _root;
    private static readonly System.Collections.Generic.Dictionary<string, SemaphoreSlim> _locks = new();

    public FileBestiaryRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory 未設定", nameof(options));
        _root = Path.Combine(root, "bestiary");
    }

    public async Task<BestiaryState> LoadAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return BestiaryState.Empty;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return BestiaryStateSerializer.Deserialize(json);
    }

    public async Task SaveAsync(string accountId, BestiaryState state, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(_root);
        var json = BestiaryStateSerializer.Serialize(state);
        await File.WriteAllTextAsync(PathFor(accountId), json, new UTF8Encoding(false), ct);
    }

    public async Task MergeAsync(string accountId, RunHistoryRecord record, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ArgumentNullException.ThrowIfNull(record);
        var sem = GetLock(accountId);
        await sem.WaitAsync(ct);
        try
        {
            var current = await LoadAsync(accountId, ct);
            var merged = BestiaryUpdater.Merge(current, record);
            await SaveAsync(accountId, merged, ct);
        }
        finally { sem.Release(); }
    }

    private string PathFor(string accountId) => Path.Combine(_root, accountId + ".json");

    private static SemaphoreSlim GetLock(string accountId)
    {
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out var s))
                _locks[accountId] = s = new SemaphoreSlim(1, 1);
            return s;
        }
    }
}
