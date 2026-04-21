using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Server.Abstractions;

/// <summary>ラン状態の永続化。Phase 2 ではソロの単一スロットのみ。</summary>
/// <remarks>Server 専用。VR 移植時は UdonSharp の PlayerData API に置き換える。</remarks>
public interface ISaveRepository
{
    Task SaveAsync(string accountId, RunState state, CancellationToken ct);

    /// <returns>未保存の場合は <c>null</c>。</returns>
    Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct);

    Task DeleteAsync(string accountId, CancellationToken ct);
}
