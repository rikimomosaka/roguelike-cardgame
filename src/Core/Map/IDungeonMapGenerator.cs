using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Map;

/// <summary>ダンジョンマップ生成器のインターフェイス。</summary>
public interface IDungeonMapGenerator
{
    /// <summary>
    /// 指定の乱数と設定でマップを生成する。制約を満たすまで内部で再生成を試行し、
    /// <see cref="MapGenerationConfig.MaxRegenerationAttempts"/> を超えたら <see cref="MapGenerationException"/> を投げる。
    /// </summary>
    DungeonMap Generate(IRng rng, MapGenerationConfig config);
}
