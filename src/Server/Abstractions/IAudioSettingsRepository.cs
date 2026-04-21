using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Settings;

namespace RoguelikeCardGame.Server.Abstractions;

/// <summary>アカウント別の音量設定を永続化する。</summary>
/// <remarks>Server 専用。VR 移植時は UdonSharp の PlayerData API に置き換える。</remarks>
public interface IAudioSettingsRepository
{
    /// <summary>保存が無い場合は <see cref="AudioSettings.Default"/> を返す。</summary>
    Task<AudioSettings> GetOrDefaultAsync(string accountId, CancellationToken ct);

    Task UpsertAsync(string accountId, AudioSettings settings, CancellationToken ct);
}
