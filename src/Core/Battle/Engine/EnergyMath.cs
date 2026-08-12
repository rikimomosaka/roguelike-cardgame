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
}
