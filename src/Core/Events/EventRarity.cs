namespace RoguelikeCardGame.Core.Events;

/// <summary>イベントの発生しやすさ。Common ほど出やすく、Rare ほど出にくい。</summary>
public enum EventRarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
}
