using System;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// ブロック値の蓄積プール。10.2.B で Display(dex) を追加。
/// Consume の dexterity 対応版は Task 10 で導入予定。
/// 親 spec §3-3 / Phase 10.2.B spec §2-5 参照。
/// </summary>
public readonly record struct BlockPool(int Sum, int AddCount)
{
    public static BlockPool Empty => new(0, 0);

    public BlockPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>敏捷を遡及反映（×AddCount）した表示・吸収用 Block 量。</summary>
    public int Display(int dexterity) => Sum + AddCount * dexterity;

    /// <summary>10.2.A 互換 API。Task 10 で `Consume(int, int)` に置換予定。</summary>
    public BlockPool Consume(int incomingAttack)
    {
        var remaining = Math.Max(0, Sum - incomingAttack);
        return new(remaining, 0);
    }

    /// <summary>10.2.A の暫定 API。Task 11 で internal 化。</summary>
    public int RawTotal => Sum;
}
