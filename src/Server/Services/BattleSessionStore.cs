using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// 戦闘セッション 1 件分の状態 + RNG。
/// IRng は session 単位で再利用必須 (spec §4-1)。複数の PlayCard / EndTurn 呼出で
/// RNG state を継承し、Core の <c>BattleDeterminismTests</c> が要求する
/// 「単一 IRng を Start → PlayCard → EndTurn に貫通させる」契約を満たす。
/// </summary>
public sealed record BattleSession(BattleState State, IRng Rng);

/// <summary>
/// 戦闘中の <see cref="BattleSession"/> を accountId 単位でメモリ保持する。
/// Phase 10.3-MVP 暫定: save に乗らない (リロードで戦闘進行リセット)。
/// Phase 10.5 で本格保存への移行を検討。
/// </summary>
public sealed class BattleSessionStore
{
    private readonly ConcurrentDictionary<string, BattleSession> _sessions = new();

    public bool TryGet(string accountId, [MaybeNullWhen(false)] out BattleSession session)
        => _sessions.TryGetValue(accountId, out session);

    public void Set(string accountId, BattleSession session)
        => _sessions[accountId] = session;

    public void Remove(string accountId)
        => _sessions.TryRemove(accountId, out _);

    /// <summary>
    /// 全 session を一括削除する。test 間の isolation 用。
    /// 本番経路では accountId 単位の <see cref="Remove"/> を使うこと。
    /// </summary>
    public void Clear() => _sessions.Clear();
}
