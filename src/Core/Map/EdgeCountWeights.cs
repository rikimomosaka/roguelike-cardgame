namespace RoguelikeCardGame.Core.Map;

/// <summary>出次数 1/2/3 それぞれの重み（確率は Weight1/(W1+W2+W3) のように正規化して使う）。</summary>
public sealed record EdgeCountWeights(double Weight1, double Weight2, double Weight3);
