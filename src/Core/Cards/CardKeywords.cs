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
        // Phase 10.5.M3: wild は「コンボ継続を保証する」キーワード。
        //  engine: コスト連番判定 (LastPlayedOrigCost+1 == Cost) を満たさなくても
        //          コンボが切れずに継続する。
        ["wild"] = new("wild", "ワイルド",
            "プレイ時、コスト連番に関係なくコンボが継続する。"),
        ["superwild"] = new("superwild", "スーパーワイルド",
            "プレイ時、コンボが継続する。さらに次にプレイするカードもコンボ継続が保証される。"),
        // Phase 10.5.M2: retainSelf action から keyword 化。
        ["wait"] = new("wait", "待機",
            "このカードはプレイ後も捨札に行かず、次ターンに手札へ持ち越される。"),
        // Phase 10.5.M3: exhaustSelf action から keyword 化。
        ["exhaust"] = new("exhaust", "消費",
            "プレイ後、このカードは捨札ではなく除外山札に送られる (戦闘終了まで戻らない)。"),
    };

    public static KeywordMeta? Get(string id) =>
        _map.TryGetValue(id, out var meta) ? meta : null;

    public static IReadOnlyDictionary<string, KeywordMeta> All => _map;
}
