using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.History;

namespace RoguelikeCardGame.Server.Abstractions;

public interface IHistoryRepository
{
    Task AppendAsync(string accountId, RunHistoryRecord record, CancellationToken ct);
    Task<IReadOnlyList<RunHistoryRecord>> ListAsync(string accountId, CancellationToken ct);
}
