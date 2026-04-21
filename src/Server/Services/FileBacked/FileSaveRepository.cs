using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

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
        if (state is null) throw new ArgumentNullException(nameof(state));
        Directory.CreateDirectory(_dir);

        var json = RunStateSerializer.Serialize(state);
        var final = PathFor(accountId);
        var tmp = final + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, final, overwrite: true);
    }

    public async Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        try
        {
            return RunStateSerializer.Deserialize(json);
        }
        catch (RunStateSerializerException)
        {
            // スキーマ不一致や破損セーブは「セーブ無し」扱いにして新規扱いで始められるようにする。
            return null;
        }
    }

    public Task DeleteAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");
}
