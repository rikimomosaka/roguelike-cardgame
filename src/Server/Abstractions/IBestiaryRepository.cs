using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.History;

namespace RoguelikeCardGame.Server.Abstractions;

public interface IBestiaryRepository
{
    Task<BestiaryState> LoadAsync(string accountId, CancellationToken ct);
    Task SaveAsync(string accountId, BestiaryState state, CancellationToken ct);
    Task MergeAsync(string accountId, RunHistoryRecord record, CancellationToken ct);
}
