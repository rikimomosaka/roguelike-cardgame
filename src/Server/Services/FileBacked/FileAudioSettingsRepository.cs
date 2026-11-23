using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Settings;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services.FileBacked;

/// <summary><c>{rootDir}/audio_settings/{accountId}.json</c> に音量設定を永続化する。</summary>
public sealed class FileAudioSettingsRepository : IAudioSettingsRepository
{
    private readonly string _dir;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _accountWriteLocks =
        new(StringComparer.Ordinal);

    public FileAudioSettingsRepository(IOptions<DataStorageOptions> options)
    {
        var root = options.Value.RootDirectory;
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("DataStorage:RootDirectory が未設定です。", nameof(options));
        _dir = Path.Combine(root, "audio_settings");
        Directory.CreateDirectory(_dir);
    }

    public async Task<AudioSettings> GetOrDefaultAsync(string accountId, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path)) return AudioSettings.Default;

        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            return AudioSettingsSerializer.Deserialize(json);
        }
        catch (AudioSettingsSerializerException)
        {
            // 破損ファイルは運用継続のため Default にフォールバック。
            return AudioSettings.Default;
        }
    }

    public async Task UpsertAsync(string accountId, AudioSettings settings, CancellationToken ct)
    {
        AccountIdValidator.Validate(accountId);
        Directory.CreateDirectory(_dir);
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        // 値域検証は Create 経由で強制（呼び出し側が不正値を渡した場合のガード）。
        var validated = AudioSettings.Create(settings.Master, settings.Bgm, settings.Se, settings.Voice);
        var json = AudioSettingsSerializer.Serialize(validated);

        var gate = _accountWriteLocks.GetOrAdd(accountId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        string? tmp = null;
        try
        {
            var final = PathFor(accountId);
            tmp = final + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(
                tmp,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                ct);
            await MoveIntoPlaceAsync(tmp, final, ct);
            tmp = null;
        }
        finally
        {
            if (tmp is not null)
            {
                try
                {
                    File.Delete(tmp);
                }
                catch (IOException)
                {
                    // Best-effort cleanup must not hide the original write failure.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same as above; the next write uses a different unique name.
                }
            }
            gate.Release();
        }
    }

    private static async Task MoveIntoPlaceAsync(
        string temporaryPath,
        string finalPath,
        CancellationToken ct)
    {
        const int maxAttempts = 6;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, finalPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                // Virus scanners and indexers can briefly hold the prior file
                // on Windows even though repository writers are serialized.
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts - 1)
            {
                // The same transient sharing violation is surfaced as either
                // IOException or UnauthorizedAccessException by Windows.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5 * (1 << attempt)), ct);
        }
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");
}
