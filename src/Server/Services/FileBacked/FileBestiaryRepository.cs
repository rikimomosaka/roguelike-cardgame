using System;
using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

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
        var sem = GetLock(accountId);
        await sem.WaitAsync(ct);
        try
        {
            await SaveCoreAsync(accountId, state, ct);
        }
        finally
        {
            sem.Release();
        }
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
            await SaveCoreAsync(accountId, merged, ct);
        }
        finally { sem.Release(); }
    }

    private string PathFor(string accountId) => Path.Combine(_root, accountId + ".json");

    private async Task SaveCoreAsync(
        string accountId, BestiaryState state, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var json = BestiaryStateSerializer.Serialize(state);
        var finalPath = PathFor(accountId);
        var tempPath = $"{finalPath}.tmp.{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(
                stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096, leaveOpen: true))
            {
                await writer.WriteAsync(json.AsMemory(), ct);
                await writer.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(finalPath))
                File.Replace(tempPath, finalPath, destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            else
                File.Move(tempPath, finalPath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static SemaphoreSlim GetLock(string accountId) =>
        _locks.GetOrAdd(
            accountId, static _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
}
