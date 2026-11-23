namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>自身のターン終了時に行う汎用的な状態値の減衰方向。個別発動で減る状態は None。</summary>
public enum StatusTickDirection
{
    None      = 0,
    Decrement = 1,
}
