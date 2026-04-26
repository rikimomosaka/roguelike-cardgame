using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (RunState, BattleSummary) Finalize(BattleState state, RunState before)
    {
        if (state.Phase != BattlePhase.Resolved)
            throw new InvalidOperationException($"Finalize requires Phase=Resolved, got {state.Phase}");

        var hero = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero")
                   ?? throw new InvalidOperationException("hero not found in Allies");
        int finalHp = Math.Max(0, hero.CurrentHp);

        // 10.2.E: ConsumedPotionIds を before vs state.Potions の slot index 順 diff として算出
        var consumed = ImmutableArray.CreateBuilder<string>();
        int slotCount = Math.Min(before.Potions.Length, state.Potions.Length);
        for (int i = 0; i < slotCount; i++)
        {
            if (before.Potions[i] != "" && state.Potions[i] == "")
                consumed.Add(before.Potions[i]);
        }
        var consumedIds = consumed.ToImmutable();

        var after = before with
        {
            CurrentHp = finalHp,
            Potions = state.Potions,                              // 10.2.E: 消費反映 (丸ごとコピー)
            ActiveBattle = null,
            Progress = state.Outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat
                ? RunProgress.GameOver
                : before.Progress,
        };

        var summary = new BattleSummary(
            FinalHeroHp: finalHp,
            Outcome: state.Outcome,
            EncounterId: state.EncounterId,
            ConsumedPotionIds: consumedIds);

        return (after, summary);
    }
}
