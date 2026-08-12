using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Json;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/history/{accountId}/{timestamp}_{runId}.json</c> にランの履歴を追記する。</summary>
public sealed class FileHistoryRepository : IHistoryRepository
{
    private readonly string _root;
    private readonly ILogger<FileHistoryRepository> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _appendLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public FileHistoryRepository(IOptions<DataStorageOptions> options, ILogger<FileHistoryRepository>? logger = null)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory 未設定", nameof(options));
        _root = Path.Combine(root, "history");
        _logger = logger ?? NullLogger<FileHistoryRepository>.Instance;
    }

    public async Task<RunHistoryRecord> AppendAsync(
        string accountId, RunHistoryRecord record, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ArgumentNullException.ThrowIfNull(record);
        var gate = _appendLocks.GetOrAdd(
            accountId, static _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));
        await gate.WaitAsync(ct);
        try
        {
            var dir = Path.Combine(_root, accountId);
            Directory.CreateDirectory(dir);

            // A run is a single history fact. Finalize/abandon may be retried after a
            // later persistence step fails, but must not append the same RunId twice.
            var existing = await FindRunIdAsync(dir, record.RunId, ct);
            if (existing is not null) return existing;

            var stamp = record.EndedAtUtc.ToString("yyyyMMddTHHmmssfffZ");
            var fileName = $"{stamp}_{record.RunId}.json";
            var path = Path.Combine(dir, fileName);
            var json = JsonSerializer.Serialize(record, JsonOptions.Default);
            await WriteAtomicAsync(path, json, ct);
            return record;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<RunHistoryRecord?> FindRunIdAsync(
        string directory, string runId, CancellationToken ct)
    {
        string suffix = $"_{runId}.json";
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            string fileName = Path.GetFileName(path);
            if (!fileName.EndsWith(suffix, StringComparison.Ordinal)
                && !fileName.Equals($"{runId}.json", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
                var existing = DeserializeRecord(json);
                if (existing is not null
                    && string.Equals(existing.RunId, runId, StringComparison.Ordinal))
                {
                    return existing;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException
                                           or FormatException or InvalidOperationException)
            {
                // An old partial/corrupt file is not a committed history fact. Leave it
                // for ListAsync diagnostics and allow a valid retry file to be written.
            }
        }
        return null;
    }

    private static async Task WriteAtomicAsync(
        string finalPath, string contents, CancellationToken ct)
    {
        string tempPath = $"{finalPath}.tmp.{Guid.NewGuid():N}";
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
                await writer.WriteAsync(contents.AsMemory(), ct);
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
            TryDeleteTemp(tempPath);
        }
    }

    private static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public async Task<IReadOnlyList<RunHistoryRecord>> ListAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var dir = Path.Combine(_root, accountId);
        if (!Directory.Exists(dir)) return Array.Empty<RunHistoryRecord>();
        var list = new List<RunHistoryRecord>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
                var rec = DeserializeRecord(json);
                if (rec is not null) list.Add(rec);
                else _logger.LogWarning("history file {Path} skipped: unknown or unsupported schema", path);
            }
            catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "history file {Path} skipped: {Message}", path, ex.Message);
            }
        }
        list.Sort((a, b) => b.EndedAtUtc.CompareTo(a.EndedAtUtc));
        return list;
    }

    private static RunHistoryRecord? DeserializeRecord(string json)
    {
        JsonNode? node = JsonNode.Parse(json);
        if (node is not JsonObject obj) return null;
        int version = obj["schemaVersion"]?.GetValue<int>() ?? 1;
        if (version == 1) { obj = MigrateV1ToV2(obj); version = 2; }
        if (version == 2) { obj = MigrateV2ToV3(obj); version = 3; }
        if (version != RunHistoryRecord.CurrentSchemaVersion) return null;
        return JsonSerializer.Deserialize<RunHistoryRecord>(obj.ToJsonString(), JsonOptions.Default);
    }

    private static JsonObject MigrateV1ToV2(JsonObject obj)
    {
        obj["seenCardBaseIds"] = new JsonArray();
        obj["acquiredRelicIds"] = new JsonArray();
        obj["acquiredPotionIds"] = new JsonArray();
        obj["encounteredEnemyIds"] = new JsonArray();
        obj["schemaVersion"] = 2;
        return obj;
    }

    private static JsonObject MigrateV2ToV3(JsonObject obj)
    {
        obj["journeyLog"] = new JsonArray();
        obj["schemaVersion"] = RunHistoryRecord.CurrentSchemaVersion;
        return obj;
    }
}
