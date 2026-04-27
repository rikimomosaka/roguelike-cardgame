using System.Collections.Concurrent;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// 戦闘中の BattleState を accountId 単位でメモリ保持する。
/// Phase 10.3-MVP 暫定: save に乗らない (リロードで戦闘進行リセット)。
/// Phase 10.5 で本格保存への移行を検討。
/// </summary>
public sealed class BattleSessionStore
{
    private readonly ConcurrentDictionary<string, BattleState> _sessions = new();

    public bool TryGet(string accountId, out BattleState state)
        => _sessions.TryGetValue(accountId, out state!);

    public void Set(string accountId, BattleState state)
        => _sessions[accountId] = state;

    public void Remove(string accountId)
        => _sessions.TryRemove(accountId, out _);
}
