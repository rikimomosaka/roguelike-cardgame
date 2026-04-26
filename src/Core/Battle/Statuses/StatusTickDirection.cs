namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>ターン開始 tick 時の amount 減衰方向。strength / dexterity は None、それ以外は Decrement。</summary>
public enum StatusTickDirection
{
    None      = 0,
    Decrement = 1,
}
