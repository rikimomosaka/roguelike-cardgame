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
        // Phase 10.5.M4: wild / superwild は 1 コンボ連鎖につき最初の 1 回しか発動しない。
        //  engine: コスト連番判定を満たさなくてもコンボ継続を保証 (初回のみ)。
        //          以降そのコンボ連鎖中は wild/superwild キーワードが無効化される。
        ["wild"] = new("wild", "ワイルド",
            "このカードではコンボが途切れない。このコンボ中、以降のワイルドを無効にする。"),
        ["superwild"] = new("superwild", "スーパーワイルド",
            "このカード及び次に使うカードではコンボが途切れない。このコンボ中、以降のワイルドを無効にする。"),
        // Phase 10.5.M2/M4: retainSelf action から keyword 化。
        ["wait"] = new("wait", "待機",
            "手札にある限り、次のターンに持ち越される。"),
        // Phase 10.5.M3/M4: exhaustSelf action から keyword 化。M4 で名前 喪失 に変更。
        ["exhaust"] = new("exhaust", "喪失",
            "このカードを除外する。"),
        // Phase 10.5.M6.7: summon action の用語 "召喚" を keyword 化。tooltip 二段目で
        //  "召喚 — 指定されたユニットを味方として呼び出す。" を表示。
        //  色は status と同じオレンジ系で「動詞的な作用」を表現。
        ["summon"] = new("summon", "召喚",
            "指定されたユニットを味方として呼び出す。"),
    };

    public static KeywordMeta? Get(string id) =>
        _map.TryGetValue(id, out var meta) ? meta : null;

    public static IReadOnlyDictionary<string, KeywordMeta> All => _map;
}
