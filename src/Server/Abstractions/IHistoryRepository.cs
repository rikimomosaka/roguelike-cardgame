using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.History;

namespace RoguelikeCardGame.Server.Abstractions;

public interface IHistoryRepository
{
    /// <summary>
    /// Appends a run once and returns the canonical persisted record. A retry with the
    /// same RunId returns the first record, including its original EndedAtUtc.
    /// </summary>
    Task<RunHistoryRecord> AppendAsync(
        string accountId, RunHistoryRecord record, CancellationToken ct);
    Task<IReadOnlyList<RunHistoryRecord>> ListAsync(string accountId, CancellationToken ct);
}
