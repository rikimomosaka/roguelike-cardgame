namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// effect の対象スコープ。
/// Self = 発動主体本人、Single = 対象指定中の 1 体、
/// Random = ランダム 1 体、All = 全員。
/// </summary>
public enum EffectScope
{
    Self = 0,
    Single = 1,
    Random = 2,
    All = 3,
}
