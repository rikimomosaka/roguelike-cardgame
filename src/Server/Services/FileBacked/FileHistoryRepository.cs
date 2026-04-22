using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Json;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/history/{accountId}/{timestamp}_{runId}.json</c> にランの履歴を追記する。</summary>
public sealed class FileHistoryRepository : IHistoryRepository
{
    private readonly string _root;

    public FileHistoryRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory 未設定", nameof(options));
        _root = Path.Combine(root, "history");
    }

    public async Task AppendAsync(string accountId, RunHistoryRecord record, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ArgumentNullException.ThrowIfNull(record);
        var dir = Path.Combine(_root, accountId);
        Directory.CreateDirectory(dir);
        var stamp = record.EndedAtUtc.ToString("yyyyMMddTHHmmssfffZ");
        var fileName = $"{stamp}_{record.RunId}.json";
        var path = Path.Combine(dir, fileName);
        var json = JsonSerializer.Serialize(record, JsonOptions.Default);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
    }

    public async Task<IReadOnlyList<RunHistoryRecord>> ListAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var dir = Path.Combine(_root, accountId);
        if (!Directory.Exists(dir)) return Array.Empty<RunHistoryRecord>();
        var list = new List<RunHistoryRecord>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            var rec = JsonSerializer.Deserialize<RunHistoryRecord>(json, JsonOptions.Default);
            if (rec is not null) list.Add(rec);
        }
        list.Sort((a, b) => b.EndedAtUtc.CompareTo(a.EndedAtUtc));
        return list;
    }
}
