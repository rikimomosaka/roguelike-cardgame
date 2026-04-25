using System;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// ブロック値の蓄積プール。Phase 10.2.A は素値のみ（敏捷遡及計算なし）。
/// 敏捷バフでの Display 計算は 10.2.B で追加する。
/// </summary>
public readonly record struct BlockPool(int Sum, int AddCount)
{
    public static BlockPool Empty => new(0, 0);

    public BlockPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>10.2.A の暫定。10.2.B で `Display(dexterity)` 拡張。</summary>
    public int RawTotal => Sum;

    /// <summary>
    /// 攻撃の総量を受けて Block を消費。引数 `incomingAttack` は「ブロック適用前の攻撃値」を渡す。
    /// 残量を新 Sum、AddCount=0 にリセット（消費後は遡及性を失う）。
    /// 10.2.B で `Consume(incomingAttack, dexterity)` 拡張予定。
    /// </summary>
    public BlockPool Consume(int incomingAttack)
    {
        var remaining = Math.Max(0, Sum - incomingAttack);
        return new(remaining, 0);
    }
}
