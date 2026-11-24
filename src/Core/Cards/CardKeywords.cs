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
            "このカード及び次に使うカードではコンボが途切れない。このコンボ中、以降のワイルドを無効にする。"),
        ["superwild"] = new("superwild", "スーパーワイルド",
            "このカード及び次に使うカードではコンボが途切れない。"),
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
        ["momentum"] = new("momentum", "乗勢",
            "効果の実行時、自身のこのターンの最大コンボがX以上であれば適用される。"),
        ["finisher"] = new("finisher", "終撃",
            "効果の適用後、このカードを含むこのコンボ中の全てのアタックカードを保存し、現在コンボを0にする。"),
        ["reprise"] = new("reprise", "再演",
            "再演条件を満たし、この戦闘中に最後に効果の解決を完了した自身のカード1枚の効果を再実行する。"),
        ["contingency"] = new("contingency", "伏策",
            "使用時は効果が実行されず、発動待ちとして除外する。伏策条件を満たした時に効果を実行し、捨札へ移動する。"),
    };

    public static KeywordMeta? Get(string id) =>
        _map.TryGetValue(id, out var meta) ? meta : null;

    public static IReadOnlyDictionary<string, KeywordMeta> All => _map;
}
