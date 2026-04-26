namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// 攻撃値の蓄積プール。10.2.B で Display(str, weak) と + operator を追加。
/// 親 spec §3-3 / Phase 10.2.B spec §2-4 参照。
/// </summary>
public readonly record struct AttackPool(int Sum, int AddCount)
{
    public static AttackPool Empty => new(0, 0);

    public AttackPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>omnistrike 合算用。Sum / AddCount をペアで加算。</summary>
    public static AttackPool operator +(AttackPool a, AttackPool b) =>
        new(a.Sum + b.Sum, a.AddCount + b.AddCount);

    /// <summary>
    /// 力バフを遡及反映（×AddCount）し、脱力 weak > 0 で 0.75 倍切捨。
    /// 整数演算で誤差なし。long キャストで AddCount × strength のオーバーフロー防御。
    /// </summary>
    public int Display(int strength, int weak)
    {
        long boosted = (long)Sum + (long)AddCount * strength;
        return weak > 0 ? (int)(boosted * 3 / 4) : (int)boosted;
    }

    /// <summary>10.2.A の暫定 API。Task 11 で internal 化。</summary>
    internal int RawTotal => Sum;
}
