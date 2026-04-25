using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// バトルエンジンの公開ファサード。`Start` / `PlayCard` / `EndTurn` / `Finalize` を提供。
/// 親 spec §3-§10 参照。
/// </summary>
public static partial class BattleEngine
{
    public const int InitialEnergy = 3;

    public static BattleState Start(
        RunState run, string encounterId, IRng rng, DataCatalog catalog)
    {
        if (!catalog.TryGetEncounter(encounterId, out var encounter))
            throw new System.InvalidOperationException($"encounter '{encounterId}' not found in catalog");

        // 1. 主人公 CombatActor 生成
        var hero = new CombatActor(
            InstanceId: "hero_inst", DefinitionId: "hero",
            Side: ActorSide.Ally, SlotIndex: 0,
            CurrentHp: run.CurrentHp, MaxHp: run.MaxHp,
            Block: BlockPool.Empty,
            AttackSingle: AttackPool.Empty,
            AttackRandom: AttackPool.Empty,
            AttackAll: AttackPool.Empty,
            CurrentMoveId: null);

        // 2. 敵 CombatActor 生成
        var enemiesBuilder = ImmutableArray.CreateBuilder<CombatActor>();
        for (int i = 0; i < encounter.EnemyIds.Count; i++)
        {
            var eid = encounter.EnemyIds[i];
            if (!catalog.TryGetEnemy(eid, out var def))
                throw new System.InvalidOperationException($"enemy '{eid}' not found in catalog");
            enemiesBuilder.Add(new CombatActor(
                InstanceId: $"enemy_inst_{i}", DefinitionId: eid,
                Side: ActorSide.Enemy, SlotIndex: i,
                CurrentHp: def.Hp, MaxHp: def.Hp,
                Block: BlockPool.Empty,
                AttackSingle: AttackPool.Empty,
                AttackRandom: AttackPool.Empty,
                AttackAll: AttackPool.Empty,
                CurrentMoveId: def.InitialMoveId));
        }

        // 3. Deck コピー & シャッフル → 山札
        var deckCards = run.Deck
            .Select((c, idx) => new BattleCardInstance($"card_inst_{idx}", c.Id, c.Upgraded, null))
            .ToArray();
        ShuffleInPlace(deckCards, rng);
        var drawPile = deckCards.ToImmutableArray();

        // 4. 初期 BattleState（Turn=0、TurnStartProcessor で +1 して Turn=1 へ）
        var initial = new BattleState(
            Turn: 0,
            Phase: BattlePhase.PlayerInput,
            Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: enemiesBuilder.ToImmutable(),
            TargetAllyIndex: 0,
            TargetEnemyIndex: enemiesBuilder.Count > 0 ? 0 : (int?)null,
            Energy: 0, EnergyMax: InitialEnergy,
            DrawPile: drawPile,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: encounterId);

        // 5. ターン 1 開始処理（5 ドロー、Energy=3、TurnStart イベント発火）
        var (afterTurnStart, _) = TurnStartProcessor.Process(initial, rng);
        return afterTurnStart;
    }

    private static void ShuffleInPlace(BattleCardInstance[] arr, IRng rng)
    {
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }
}
