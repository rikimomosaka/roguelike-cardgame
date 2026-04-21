using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// マップ全体のタイル分布ルール。
/// </summary>
/// <param name="BaseWeights">各 Kind の割当時重み（合計は任意、内部で正規化）。キー欠落の Kind は重み 0 = 選ばれない。</param>
/// <param name="MinPerMap">マップ全体での最小個数。下回ったら再生成。キー欠落は制約なし。</param>
/// <param name="MaxPerMap">マップ全体での最大個数。超えたら再生成（割当時にも候補から除外）。キー欠落は制約なし。</param>
public sealed record TileDistributionRule(
    ImmutableDictionary<TileKind, double> BaseWeights,
    ImmutableDictionary<TileKind, int> MinPerMap,
    ImmutableDictionary<TileKind, int> MaxPerMap);
