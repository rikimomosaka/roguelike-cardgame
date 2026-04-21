namespace RoguelikeCardGame.Core.Random;

/// <summary>決定論的なマップ生成やその他のランダム処理に使う乱数抽象。</summary>
/// <remarks>
/// VR (Udon#) 移植時は本インターフェイスを削除し、呼び出し側が UnityEngine.Random を直接叩く。
/// Phase 3 のマップ生成は VR 側では「事前生成済み JSON を読み込む」運用に切り替わる想定。
/// </remarks>
public interface IRng
{
    /// <summary>[minInclusive, maxExclusive) の範囲で整数を返す。</summary>
    int NextInt(int minInclusive, int maxExclusive);

    /// <summary>[0.0, 1.0) の範囲で double を返す。</summary>
    double NextDouble();
}
