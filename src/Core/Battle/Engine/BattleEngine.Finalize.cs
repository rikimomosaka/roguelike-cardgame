using System;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (RunState, BattleSummary) Finalize(BattleState state, RunState before)
    {
        if (state.Phase != BattlePhase.Resolved)
            throw new InvalidOperationException($"Finalize requires Phase=Resolved, got {state.Phase}");

        int finalHp = state.Allies[0].CurrentHp;

        var after = before with
        {
            CurrentHp = finalHp,
            ActiveBattle = null, // 戦闘終了 → 呼び出し側で次の遷移を決定
            Progress = state.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat
                ? RunProgress.GameOver
                : before.Progress,
        };

        var summary = new BattleSummary(
            FinalHeroHp: finalHp,
            Outcome: state.Outcome,
            EncounterId: state.EncounterId);

        return (after, summary);
    }
}
