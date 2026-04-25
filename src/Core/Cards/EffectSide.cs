namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// effect の対象側。行動主体から見た相対視点で:
/// Enemy = 自分の対立側、Ally = 自分側。
/// </summary>
public enum EffectSide
{
    Enemy = 0,
    Ally = 1,
}
