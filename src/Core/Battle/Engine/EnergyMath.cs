namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>エナジー値を非負の int 範囲で扱うための共通演算。</summary>
internal static class EnergyMath
{
    /// <summary>
    /// delta を加算し、0 未満は 0、int 上限超過は int.MaxValue へ飽和させる。
    /// </summary>
    public static int AddSaturating(int current, int delta)
    {
        long result = (long)current + delta;
        if (result <= 0) return 0;
        if (result >= int.MaxValue) return int.MaxValue;
        return (int)result;
    }

    /// <summary>
    /// ターン開始時のエナジー回復: 規定値（target）に満たなければ規定値まで回復する。
    /// 規定値以上残っている時は減らしも増やしもしない。
    /// </summary>
    public static int RefillTo(int current, int target) =>
        System.Math.Max(System.Math.Max(0, current), System.Math.Max(0, target));
}
