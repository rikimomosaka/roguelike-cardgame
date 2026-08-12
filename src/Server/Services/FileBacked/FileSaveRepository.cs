using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

public sealed class FileSaveRepositoryException : IOException
{
    public FileSaveRepositoryException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary><c>{rootDir}/saves/{accountId}.json</c> にソロランを永続化する。</summary>
public sealed class FileSaveRepository : ISaveRepository
{
    private readonly string _dir;

    public FileSaveRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory が未設定です。", nameof(options));
        _dir = Path.Combine(root, "saves");
        Directory.CreateDirectory(_dir);
    }

    public async Task SaveAsync(string accountId, RunState state, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        ArgumentNullException.ThrowIfNull(state);

        // Serialize performs RunState.Validate before any file is touched.
        var json = RunStateSerializer.Serialize(state);
        var final = PathFor(accountId);
        var backup = BackupPathFor(accountId);
        try
        {
            Directory.CreateDirectory(_dir);
            await WriteAtomicAsync(final, backup, json, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileSaveRepositoryException(
                $"Failed to save run data for account '{accountId}'.", ex);
        }
    }

    public async Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var primaryPath = PathFor(accountId);
        var backupPath = BackupPathFor(accountId);
        bool primaryExists = File.Exists(primaryPath);
        bool backupExists = File.Exists(backupPath);
        if (!primaryExists && !backupExists) return null;

        try
        {
            var primary = await TryReadValidAsync(primaryPath, ct);
            if (primary is not null) return primary;

            var backup = await TryReadValidAsync(backupPath, ct);
            if (backup is null)
            {
                throw new InvalidDataException(
                    $"Run save data for account '{accountId}' exists but neither primary nor backup is valid.");
            }

            // Heal the primary without replacing the known-good backup with the
            // corrupt primary which caused this recovery.
            await RestorePrimaryAsync(primaryPath, RunStateSerializer.Serialize(backup), ct);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new FileSaveRepositoryException(
                $"Failed to load run data for account '{accountId}'.", ex);
        }
    }

    public Task DeleteAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        try
        {
            var path = PathFor(accountId);
            if (File.Exists(path)) File.Delete(path);
            var backup = BackupPathFor(accountId);
            if (File.Exists(backup)) File.Delete(backup);
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileSaveRepositoryException(
                $"Failed to delete run data for account '{accountId}'.", ex);
        }
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");
    private string BackupPathFor(string accountId) => PathFor(accountId) + ".bak";

    private static async Task<RunState?> TryReadValidAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        try
        {
            return RunStateSerializer.Deserialize(json);
        }
        catch (RunStateSerializerException)
        {
            return null;
        }
    }

    private static async Task WriteAtomicAsync(
        string finalPath,
        string backupPath,
        string json,
        CancellationToken ct)
    {
        var tempPath = UniqueTempPath(finalPath);
        try
        {
            await WriteDurableUtf8Async(tempPath, json, ct);
            if (File.Exists(finalPath))
                File.Replace(tempPath, finalPath, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, finalPath);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    private static async Task RestorePrimaryAsync(
        string finalPath,
        string json,
        CancellationToken ct)
    {
        var tempPath = UniqueTempPath(finalPath);
        try
        {
            await WriteDurableUtf8Async(tempPath, json, ct);
            if (File.Exists(finalPath))
                File.Replace(tempPath, finalPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, finalPath);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    private static async Task WriteDurableUtf8Async(
        string path,
        string contents,
        CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true);
        await writer.WriteAsync(contents.AsMemory(), ct);
        await writer.FlushAsync(ct);
        stream.Flush(flushToDisk: true);
    }

    private static string UniqueTempPath(string finalPath)
        => $"{finalPath}.tmp.{Guid.NewGuid():N}";

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
