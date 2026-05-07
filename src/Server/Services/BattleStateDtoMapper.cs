using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Dtos;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// Core <see cref="BattleState"/> を Server DTO に変換する。
/// Phase 10.3-MVP spec §4-2 参照。
/// 10.2.B で実装済の <see cref="AttackPool.Display"/> / <see cref="BlockPool.Display"/>
/// を使い、力/敏捷/脱力反映後の表示値を計算する。
/// data 引数は intent 計算で actor 定義を参照する目的（UX 補強）。
/// </summary>
internal static class BattleStateDtoMapper
{
    public static BattleStateDto ToDto(BattleState state, DataCatalog data)
    {
        // Why: hero の statuses を一度だけ計算し、各 pile のカード変換で再利用。
        // 10.5.C: formatter に渡して [N:N|up] / [N:N|down] のマーカーを emit させる。
        var heroCtx = BuildHeroContext(state);
        return new(
            Turn: state.Turn,
            Phase: state.Phase.ToString(),
            Outcome: state.Outcome.ToString(),
            Allies: state.Allies.Select(a => ToActorDto(a, data)).ToList(),
            Enemies: state.Enemies.Select(a => ToActorDto(a, data)).ToList(),
            TargetAllyIndex: state.TargetAllyIndex,
            TargetEnemyIndex: state.TargetEnemyIndex,
            Energy: state.Energy,
            EnergyMax: state.EnergyMax,
            DrawPile: state.DrawPile.Select(c => ToCardDto(c, data, heroCtx)).ToList(),
            Hand: state.Hand.Select(c => ToCardDto(c, data, heroCtx)).ToList(),
            DiscardPile: state.DiscardPile.Select(c => ToCardDto(c, data, heroCtx)).ToList(),
            ExhaustPile: state.ExhaustPile.Select(c => ToCardDto(c, data, heroCtx)).ToList(),
            SummonHeld: state.SummonHeld.Select(c => ToCardDto(c, data, heroCtx)).ToList(),
            PowerCards: state.PowerCards.Select(c => ToCardDto(c, data, heroCtx)).ToList(),
            ComboCount: state.ComboCount,
            LastPlayedOrigCost: state.LastPlayedOrigCost,
            NextCardComboFreePass: state.NextCardComboFreePass,
            OwnedRelicIds: state.OwnedRelicIds.ToList(),
            Potions: state.Potions.ToList(),
            EncounterId: state.EncounterId,
            // Phase 10.5.M2-Choose: choose modal 待ち状態を DTO 化。null の場合はそのまま null。
            PendingCardPlay: state.PendingCardPlay is null ? null : new PendingCardPlayDto(
                CardInstanceId: state.PendingCardPlay.CardInstanceId,
                EffectIndex: state.PendingCardPlay.EffectIndex,
                Choice: new PendingChoiceDto(
                    Action: state.PendingCardPlay.Choice.Action,
                    Pile: state.PendingCardPlay.Choice.Pile,
                    Count: state.PendingCardPlay.Choice.Count,
                    CandidateInstanceIds: state.PendingCardPlay.Choice.CandidateInstanceIds.ToArray())));
    }

    /// <summary>
    /// 10.5.C: hero (caster) の statuses を <see cref="CardActorContext"/> に変換。
    /// hero が見つからない場合は <see cref="CardActorContext.Empty"/>。
    /// </summary>
    private static CardActorContext BuildHeroContext(BattleState state)
    {
        var hero = state.Allies.FirstOrDefault(a => a.DefinitionId == "hero");
        if (hero is null) return CardActorContext.Empty;
        return new CardActorContext(
            Strength: hero.GetStatus("strength"),
            Weak: hero.GetStatus("weak"),
            Dexterity: hero.GetStatus("dexterity"));
    }

    private static CombatActorDto ToActorDto(CombatActor a, DataCatalog data) =>
        new(
            InstanceId: a.InstanceId,
            DefinitionId: a.DefinitionId,
            Side: a.Side.ToString(),
            SlotIndex: a.SlotIndex,
            CurrentHp: a.CurrentHp,
            MaxHp: a.MaxHp,
            BlockDisplay: a.Block.Display(a.GetStatus("dexterity")),
            AttackSingleDisplay: a.AttackSingle.Display(a.GetStatus("strength"), a.GetStatus("weak")),
            AttackRandomDisplay: a.AttackRandom.Display(a.GetStatus("strength"), a.GetStatus("weak")),
            AttackAllDisplay: a.AttackAll.Display(a.GetStatus("strength"), a.GetStatus("weak")),
            Statuses: a.Statuses.ToDictionary(kv => kv.Key, kv => kv.Value),
            CurrentMoveId: a.CurrentMoveId,
            RemainingLifetimeTurns: a.RemainingLifetimeTurns,
            AssociatedSummonHeldInstanceId: a.AssociatedSummonHeldInstanceId,
            Intent: ComputeIntent(a, data));

    /// <summary>
    /// actor の予定行動を計算する。
    /// - hero: pool (AttackSingle/Random/All) を読み取り、Display 値を per-scope に
    ///   詰める。block 等は battle state で即時反映済なので予定としては出さない。
    /// - enemy: CurrentMoveId の effects を集計。仕様により attack は全体扱い。
    /// - summon ally: enemy と同仕様 (attack=全体)。
    /// </summary>
    private static IntentDto? ComputeIntent(CombatActor a, DataCatalog data)
    {
        // hero: pool から intent を組み立て
        if (a.DefinitionId == "hero")
        {
            int strength = a.GetStatus("strength");
            int weak = a.GetStatus("weak");
            int single = a.AttackSingle.Display(strength, weak);
            int random = a.AttackRandom.Display(strength, weak);
            int all = a.AttackAll.Display(strength, weak);
            int hits = a.AttackSingle.AddCount + a.AttackRandom.AddCount + a.AttackAll.AddCount;
            if (single == 0 && random == 0 && all == 0) return null;
            return new IntentDto(
                AttackSingle: single > 0 ? single : null,
                AttackRandom: random > 0 ? random : null,
                AttackAll: all > 0 ? all : null,
                AttackHits: hits > 0 ? hits : null,
                Block: null, HasBuff: false, HasDebuff: false, HasHeal: false);
        }

        if (a.CurrentMoveId is null) return null;

        IReadOnlyList<MoveDefinition>? moves = null;
        if (a.Side == ActorSide.Enemy && data.Enemies.TryGetValue(a.DefinitionId, out var enemyDef))
            moves = enemyDef.Moves;
        else if (a.Side == ActorSide.Ally && data.TryGetUnit(a.DefinitionId, out var unitDef))
            moves = unitDef.Moves;

        if (moves is null) return null;
        var move = moves.FirstOrDefault(m => m.Id == a.CurrentMoveId);
        if (move is null) return null;

        // Why: enemy / summon ally の attack は仕様で全体着弾 (EnemyAttackingResolver /
        // ResolveSummonMove)。表示も AttackAll に統一する。
        int strengthN = a.GetStatus("strength");
        int weakN = a.GetStatus("weak");
        int attackAllAmount = 0;
        int attackHitsN = 0;
        int blockAmount = 0;
        bool hasBuff = false;
        bool hasDebuff = false;
        bool hasHeal = false;

        foreach (var eff in move.Effects)
        {
            switch (eff.Action)
            {
                case "attack":
                    attackAllAmount += AdjustAttackAmount(eff.Amount, strengthN, weakN);
                    attackHitsN += 1;
                    break;
                case "block":
                    blockAmount += eff.Amount;
                    break;
                case "buff":   hasBuff = true; break;
                case "debuff": hasDebuff = true; break;
                case "heal":   hasHeal = true; break;
            }
        }

        if (attackHitsN == 0 && blockAmount == 0 && !hasBuff && !hasDebuff && !hasHeal)
            return null;

        return new IntentDto(
            AttackSingle: null,
            AttackRandom: null,
            AttackAll: attackHitsN > 0 ? attackAllAmount : null,
            AttackHits: attackHitsN > 0 ? attackHitsN : null,
            Block: blockAmount > 0 ? blockAmount : null,
            HasBuff: hasBuff, HasDebuff: hasDebuff, HasHeal: hasHeal);
    }

    /// <summary>
    /// AttackPool.Display と同等の整数版調整。strength は加算、weak は 0.75 倍 (端数切り捨て)。
    /// </summary>
    private static int AdjustAttackAmount(int baseAmount, int strength, int weak)
    {
        int withStr = baseAmount + strength;
        if (weak > 0) return (int)(withStr * 0.75);
        return withStr;
    }

    /// <summary>
    /// 10.5.C: hero context を formatter に渡して adjustedDescription を populate する。
    /// catalog から CardDefinition を引けない場合は null のまま (Client は catalog の
    /// description にフォールバック)。upgraded 版は IsUpgradable の場合のみ計算。
    /// </summary>
    private static BattleCardInstanceDto ToCardDto(
        BattleCardInstance c, DataCatalog data, CardActorContext heroCtx)
    {
        string? adjusted = null;
        string? adjustedUp = null;
        if (data.Cards.TryGetValue(c.CardDefinitionId, out var def))
        {
            adjusted = CardTextFormatter.Format(def, upgraded: false, heroCtx);
            if (def.IsUpgradable)
                adjustedUp = CardTextFormatter.Format(def, upgraded: true, heroCtx);
        }
        return new BattleCardInstanceDto(
            c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride,
            adjusted, adjustedUp);
    }

    public static BattleEventDto ToEventDto(BattleEvent ev) =>
        new(
            Kind: ev.Kind.ToString(),
            Order: ev.Order,
            CasterInstanceId: ev.CasterInstanceId,
            TargetInstanceId: ev.TargetInstanceId,
            Amount: ev.Amount,
            CardId: ev.CardId,
            Note: ev.Note);

    /// <summary>
    /// Phase 10.3-MVP 確定 (spec §3-5): SnapshotAfter は常に最終 state と同一。
    /// Client は CSS transition で HP/Block 数値を補間する。
    /// 将来 Phase 10.4 で本物の中間 state に進化させる場合、DTO 形式を維持して
    /// 中身だけ差し替え可能。
    /// </summary>
    public static BattleActionResponseDto ToActionResponse(
        BattleState finalState, IReadOnlyList<BattleEvent> events, DataCatalog data)
    {
        var stateDto = ToDto(finalState, data);
        var steps = events
            .Select(ev => new BattleEventStepDto(ToEventDto(ev), stateDto))
            .ToList();
        return new BattleActionResponseDto(stateDto, steps);
    }
}
