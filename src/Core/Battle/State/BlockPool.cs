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

    /// <summary>
    /// 攻撃の総量を受けて Block を消費。`incomingAttack` は「ブロック適用前の攻撃値」を渡す。
    /// dexterity を反映した Display(dexterity) を計算してから消費量を判定。
    /// 残量を新 Sum、AddCount=0 にリセット（消費後は遡及性を失う）。
    /// 親 spec §3-3 / §4-4 参照。
    /// </summary>
    public BlockPool Consume(int incomingAttack, int dexterity)
    {
        var current = Display(dexterity);
        var remaining = Math.Max(0, current - incomingAttack);
        return new(remaining, 0);
    }

    /// <summary>10.2.A の暫定 API。Task 11 で internal 化。</summary>
    public int RawTotal => Sum;
}
