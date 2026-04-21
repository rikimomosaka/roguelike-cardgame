using System;
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
        var validated = AudioSettings.Create(settings.Master, settings.Bgm, settings.Se, settings.Ambient);
        var json = AudioSettingsSerializer.Serialize(validated);

        var final = PathFor(accountId);
        var tmp = final + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        File.Move(tmp, final, overwrite: true);
    }

    private string PathFor(string accountId) => Path.Combine(_dir, accountId + ".json");
}
