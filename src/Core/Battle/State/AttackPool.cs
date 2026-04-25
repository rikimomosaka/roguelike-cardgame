namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// 攻撃値の蓄積プール。Phase 10.2.A は素値のみ（遡及計算なし）。
/// 力バフ / 脱力での Display 計算は 10.2.B で追加する。
/// </summary>
public readonly record struct AttackPool(int Sum, int AddCount)
{
    public static AttackPool Empty => new(0, 0);

    public AttackPool Add(int amount) => new(Sum + amount, AddCount + 1);

    /// <summary>10.2.A の暫定。10.2.B で `Display(strength, weak)` 拡張。</summary>
    public int RawTotal => Sum;
}
