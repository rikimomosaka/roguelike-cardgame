using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Events;

/// <summary>EventChoice の選択可否判定。null なら常に選択可。</summary>
public abstract record EventCondition
{
    public abstract bool IsSatisfied(RunState state);

    public sealed record MinGold(int Amount) : EventCondition
    {
        public override bool IsSatisfied(RunState s) => s.Gold >= Amount;
    }

    public sealed record MinHp(int Amount) : EventCondition
    {
        public override bool IsSatisfied(RunState s) => s.CurrentHp >= Amount;
    }
}
