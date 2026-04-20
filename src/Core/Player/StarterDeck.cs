using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Player;

/// <summary>初期デッキ（固定 10 枚）。</summary>
public static class StarterDeck
{
    public static readonly IReadOnlyList<string> DefaultCardIds = new[]
    {
        "strike", "strike", "strike", "strike", "strike",
        "defend", "defend", "defend", "defend", "defend",
    };
}
