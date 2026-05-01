using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// カードキーワード ID → 表示名 / 説明文 のマッピング。
/// formatter のキーワード行表示と Client tooltip popup が共有する。
/// 将来は JSON catalog 化を検討 (現状は ワイルド / スーパーワイルド のみ)。
/// </summary>
public static class CardKeywords
{
    public sealed record KeywordMeta(string Id, string Name, string Description);

    private static readonly Dictionary<string, KeywordMeta> _map = new()
    {
        ["wild"] = new("wild", "ワイルド",
            "敵単体を対象とする攻撃が、ランダムな敵を対象に変わる。"),
        ["superwild"] = new("superwild", "スーパーワイルド",
            "敵単体を対象とする攻撃が、敵全体を対象に変わる。"),
        // Phase 10.5.M2: retainSelf action から keyword 化。
        ["wait"] = new("wait", "待機",
            "このカードはプレイ後も捨札に行かず、次ターンに手札へ持ち越される。"),
    };

    public static KeywordMeta? Get(string id) =>
        _map.TryGetValue(id, out var meta) ? meta : null;

    public static IReadOnlyDictionary<string, KeywordMeta> All => _map;
}
