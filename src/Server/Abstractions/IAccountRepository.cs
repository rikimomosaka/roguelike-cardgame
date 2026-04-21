using System;
using System.Threading;
using System.Threading.Tasks;

namespace RoguelikeCardGame.Server.Abstractions;

/// <summary>アカウントメタデータの永続化。</summary>
/// <remarks>Server 専用。VR 移植時は UdonSharp の PlayerData API に置き換える。</remarks>
public interface IAccountRepository
{
    Task<bool> ExistsAsync(string accountId, CancellationToken ct);

    /// <exception cref="AccountAlreadyExistsException">既存 ID を再作成しようとした。</exception>
    Task CreateAsync(string accountId, DateTimeOffset nowUtc, CancellationToken ct);

    /// <returns>未登録の場合は <c>null</c>。</returns>
    Task<Account?> GetAsync(string accountId, CancellationToken ct);
}
