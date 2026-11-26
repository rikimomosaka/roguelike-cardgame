namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// カード 1 枚に後から足された能力。戦闘中だけ有効で、カード個体について領域を移動する。
/// 今は Kind = "keyword"（キーワードとその数。例: 双極2）だけ。
/// 将来のエンハンスで効果そのものを足す場合は Kind を増やす。
/// </summary>
public sealed record CardAugment(string Kind, string KeywordId, int Count)
{
    public const string KeywordKind = "keyword";
}
