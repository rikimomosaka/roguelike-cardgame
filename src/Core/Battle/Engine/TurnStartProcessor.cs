using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン開始処理。10.2.B で 毒ダメージ tick / status countdown / 死亡判定で Outcome 確定 を実装。
/// 10.2.E で OnTurnStart レリック発火を追加。
/// 親 spec §4-2 / Phase 10.2.B spec §5 参照。
/// </summary>
internal static class TurnStartProcessor
{
    /// <summary>
    /// 1 ターンに引く基本枚数 (Phase 10.6.B 以降は relic で modifier 加算可)。
    /// 命名: BattleState.DrawPerTurn (snapshot field) との混同を避けるため Base prefix。
    /// </summary>
    public const int BaseDrawPerTurn = 5;

    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng, DataCatalog catalog)
    {
        // 決着済み state へ直接呼ばれても、ターン・エナジー・手札を進めない。
        if (state.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending
            || state.Phase == BattlePhase.Resolved)
            return (state, ImmutableArray<BattleEvent>.Empty);

        // EndTurn を経由しない直接呼び出しでも、新しいターンへ前ターンのコンボ状態を
        // 持ち越さない。通常フローでは TurnEndProcessor のリセットと二重に保証する。
        // Explicit participant battles commit this compatibility root back to
        // participant zero before each participant is refreshed below, so the
        // marker remains set until the participant's first real card play.
        var s = ComboTurnBoundaryRules.ResetAtTurnStart(state) with
        {
            Turn = state.Turn + 1,
        };
        s = BattleCardCostModifierLifecycle.Remove(
            s, CardCostModifierDuration.UntilTurnEnd);
        // Participant zero mirrors the legacy root. Persist the direct-call
        // turn reset before any participant-owned status projects resources
        // back through that root; otherwise an Insight/Haste tick could
        // restore stale per-turn state from the primary participant.
        s = BattleParticipantTurnLifecycle.CommitPrimaryRoot(s);
        s = BattleParticipantTurnLifecycle
            .ResetCpuPlayCountsForAllParticipants(s);
        var events = new List<BattleEvent>();
        int order = 0;

        // Regeneration and other beneficial start effects resolve before
        // Burn/Poison so their ordering is deterministic and visible.
        // 加速・消耗のエナジー増減は、規定値までの回復（ApplyStartToAllParticipants）の後に適用する。
        // 先に足すと「規定値まで回復」に吸収されてしまうため。
        var pendingEnergy = new Dictionary<string, int>(StringComparer.Ordinal);
        s = ApplyTurnStartStatuses(s, events, ref order, rng, catalog, pendingEnergy);

        // Step 2: 毒ダメージ tick（Allies → Enemies、SlotIndex 順、InstanceId 検索で更新）
        s = ApplyPoisonTick(s, events, ref order, catalog, rng);

        // 10.2.D: 毒死で召喚も死んだ場合のクリーンアップ（Outcome 確定前に SummonHeld → Discard）
        s = SummonCleanup.Apply(s, events, ref order);

        // Step 3: tick 後の死亡判定 + 自動切替 + Outcome 確定
        s = TargetingAutoSwitch.Apply(s);
        var (terminal, terminalEvents) =
            BattleOutcomeResolver.ApplyIfTerminal(s, order);
        if (terminalEvents.Count > 0)
        {
            s = terminal;
            foreach (var battleEvent in terminalEvents)
                events.Add(battleEvent with { Order = order++ });
            return (s, events);
        }

        // Step 4: status countdown は廃止。新仕様では BattleEngine.EndTurn 内で
        //  「ターンを終えた側」の actor だけを SideStatusCountdown で減算する
        //  (weak/vulnerable 等が enemy 側付与で即削除されるバグ対応)。

        // Step 5: Lifetime tick（10.2.D）
        s = ApplyLifetimeTick(s, events, ref order);

        // 10.2.D: Lifetime 死亡で召喚カードを Discard へ
        s = SummonCleanup.Apply(s, events, ref order);

        // Step 6-7: エナジーを規定値（EnergyMax）まで回復して Draw。
        // 規定値に満たなければ規定値まで回復し、規定値以上残っていればそのまま（2026-11-27）。
        // EnergyMax は互換性のため名称を維持しているが、保有上限ではない（規定値を超えて持てる）。
        // Refresh every participant before any OnTurnStart relic or power
        // fires. Primary and secondary participants therefore share the same
        // base-energy/draw -> trigger ordering.
        s = BattleParticipantTurnLifecycle.ApplyStartToAllParticipants(
            s, rng, catalog, out var refreshShuffleEvents);
        foreach (var battleEvent in refreshShuffleEvents)
            events.Add(battleEvent with { Order = order++ });
        foreach (var (actorId, delta) in pendingEnergy)
            if (delta != 0) s = AddEnergyForActorOwner(s, actorId, delta);

        // 敵の先制行動: 最初の手札を配った直後、他の全処理の前に即時解決する。
        if (s.Turn == 1)
        {
            s = EnemyOpeningActions.Apply(s, events, ref order, rng, catalog);
            s = TargetingAutoSwitch.Apply(s);
        }

        // Character passives observe the refreshed per-participant hand and
        // resources, then resolve before armed TurnStart Contingencies and
        // ordinary relic/power triggers. Projection is explicit so a
        // secondary character never mutates the primary participant's zones.
        s = ApplyTurnStartTraits(s, events, ref order, rng, catalog);

        s = SummonCleanup.Apply(s, events, ref order);
        s = TargetingAutoSwitch.Apply(s);
        var (afterTraitOutcome, traitTerminalEvents) =
            BattleOutcomeResolver.ApplyIfTerminal(s, order);
        if (traitTerminalEvents.Count > 0)
        {
            s = afterTraitOutcome;
            foreach (var battleEvent in traitTerminalEvents)
                events.Add(battleEvent with { Order = order++ });
            return (s, events);
        }

        // Contingencies observe the fully refreshed hand/energy state, but
        // resolve before OnTurnStart relics and powers. A human choice yields
        // here and the outer queued-turn state machine resumes this boundary.
        var contingency = ContingencyResolutionEngine.ResolveTurnStart(
            s, rng, catalog, order);
        s = contingency.State;
        foreach (var battleEvent in contingency.Events)
            events.Add(battleEvent with { Order = order++ });
        if (contingency.PausedForChoice)
        {
            s = s with
            {
                PendingTurnResolution = new PendingTurnResolution(
                    TurnResolutionContinuationStage.TurnStartContingencies),
            };
            return (s, events);
        }

        return ContinueAfterContingenciesCore(
            s, events, ref order, rng, catalog);
    }

    private static BattleState ApplyTurnStartTraits(
        BattleState state,
        List<BattleEvent> events,
        ref int order,
        IRng rng,
        DataCatalog catalog)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            var actor = ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
            if (actor is null)
                return state;

            var result = CharacterTraitRuntime.TryTrigger(
                state,
                actor.InstanceId,
                CharacterTraitTrigger.OnTurnStart,
                catalog,
                rng,
                orderStart: order);
            foreach (var battleEvent in result.Events)
                events.Add(battleEvent with { Order = order++ });
            return result.State;
        }

        var participantIds = BattleParticipantAccess.InActorSlotOrder(state)
            .Select(participant => participant.ParticipantId)
            .ToArray();
        var next = state;
        foreach (string participantId in participantIds)
        {
            if (!BattleParticipantAccess.TryGet(next, participantId, out var participant))
                continue;
            var actor = next.Allies.FirstOrDefault(candidate =>
                candidate.IsAlive
                && string.Equals(
                    candidate.InstanceId,
                    participant.ActorInstanceId,
                    System.StringComparison.Ordinal));
            if (actor is null)
                continue;

            var projected = BattleParticipantAccess.ProjectToRoot(next, participantId);
            var result = CharacterTraitRuntime.TryTrigger(
                projected,
                actor.InstanceId,
                CharacterTraitTrigger.OnTurnStart,
                catalog,
                rng,
                orderStart: order);
            next = BattleParticipantAccess.CommitRootToParticipant(
                result.State,
                participantId);
            foreach (var battleEvent in result.Events)
                events.Add(battleEvent with { Order = order++ });
        }

        return next;
    }

    /// <summary>
    /// Runs the post-Contingency half of TurnStart after a human choice and any
    /// remaining armed cards have completed.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>)
        ContinueAfterContingencies(
            BattleState state, IRng rng, DataCatalog catalog)
    {
        var events = new List<BattleEvent>();
        int order = 0;
        return ContinueAfterContingenciesCore(
            state with { PendingTurnResolution = null },
            events,
            ref order,
            rng,
            catalog);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>)
        ContinueAfterContingenciesCore(
            BattleState s,
            List<BattleEvent> events,
            ref int order,
            IRng rng,
            DataCatalog catalog)
    {

        // Step 8: OnTurnStart レリック発動 (10.2.E / 10.5.L1.5: 文字列 trigger に変更)
        var (afterRelic, evsRelic) =
            RelicTriggerProcessor.FireForAllLivingParticipants(
            s, "OnTurnStart", catalog, rng, orderStart: order);
        s = afterRelic;
        foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

        // Step 8.5: OnTurnStart power カード発動 (10.5.E)
        var (afterPower, evsPower) =
            PowerTriggerProcessor.FireForAllLivingParticipants(
            s, "OnTurnStart", catalog, rng, orderStart: order);
        s = afterPower;
        foreach (var ev in evsPower) { events.Add(ev with { Order = order++ }); }

        // Trigger effects can deal self damage. Resolve death only after the
        // complete OnTurnStart trigger sequence so Revive remains authoritative,
        // but never return PlayerInput with a dead required player.
        s = SummonCleanup.Apply(s, events, ref order);
        s = TargetingAutoSwitch.Apply(s);
        var (afterStartTriggers, startTriggerTerminalEvents) =
            BattleOutcomeResolver.ApplyIfTerminal(s, order);
        if (startTriggerTerminalEvents.Count > 0)
        {
            s = afterStartTriggers;
            foreach (var battleEvent in startTriggerTerminalEvents)
                events.Add(battleEvent with { Order = order++ });
            return (s, events);
        }

        // Step 9: TurnStart event
        events.Add(new BattleEvent(BattleEventKind.TurnStart, Order: order++, Note: $"turn={s.Turn}"));
        return (s, events);
    }

    private static BattleState ApplyTurnStartStatuses(
        BattleState state,
        List<BattleEvent> events,
        ref int order,
        IRng rng,
        DataCatalog catalog,
        Dictionary<string, int> pendingEnergy)
    {
        var actorIds = state.Allies.OrderBy(actor => actor.SlotIndex)
            .Select(actor => actor.InstanceId)
            .Concat(state.Enemies.OrderBy(actor => actor.SlotIndex)
                .Select(actor => actor.InstanceId))
            .ToList();
        var s = state;

        foreach (string actorId in actorIds)
        {
            var actor = FindActor(s, actorId);
            if (actor is null || !actor.IsAlive) continue;

            int regeneration = actor.GetStatus(BattleStatusIds.Regeneration);
            if (regeneration > 0)
            {
                int healed = System.Math.Min(regeneration, actor.MaxHp - actor.CurrentHp);
                actor = BattleStatusRules.Decrement(actor, BattleStatusIds.Regeneration);
                if (healed > 0) actor = actor with { CurrentHp = actor.CurrentHp + healed };
                s = ReplaceActor(s, actorId, actor);
                if (healed > 0)
                    events.Add(new BattleEvent(
                        BattleEventKind.Heal, order++, actorId, actorId,
                        Amount: healed, Note: BattleStatusIds.Regeneration));
            }

            actor = FindActor(s, actorId)!;
            int growth = actor.GetStatus(BattleStatusIds.Growth);
            if (growth > 0)
            {
                int strengthBefore = actor.GetStatus(BattleStatusIds.Strength);
                int resonanceBefore = actor.GetStatus(BattleStatusIds.Resonance);
                var growthSource = actor.GetStatusSource(BattleStatusIds.Growth)
                    ?? CombatSource.Unattributed;
                actor = BattleStatusRules.AddSaturating(
                    actor, BattleStatusIds.Strength, growth);
                int strengthAfter = actor.GetStatus(BattleStatusIds.Strength);
                if (strengthAfter != strengthBefore)
                {
                    actor = actor with
                    {
                        StatusSources = actor.StatusSources.SetItem(
                            BattleStatusIds.Strength,
                            growthSource),
                    };
                }
                s = ReplaceActor(s, actorId, actor);
                if (strengthAfter != strengthBefore)
                {
                    events.Add(new BattleEvent(
                        BattleEventKind.ApplyStatus, order++, actorId, actorId,
                        Amount: growth, Note: BattleStatusIds.Strength,
                        Source: growthSource));
                    if (!s.Participants.IsDefaultOrEmpty
                        && BattleParticipantAccess.TryGetByActor(
                            s, actorId, out var resonanceOwner))
                    {
                        var projected = BattleParticipantAccess.ProjectToRoot(
                            s, resonanceOwner.ParticipantId);
                        projected = PositiveBuffApplicationRules.ApplyResonance(
                            projected,
                            actorId,
                            resonanceBefore,
                            [new StatusChange(BattleStatusIds.Strength, growth)],
                            growthSource,
                            events,
                            ref order,
                            catalog,
                            rng);
                        s = BattleParticipantAccess.CommitRootToParticipant(
                            projected, resonanceOwner.ParticipantId);
                    }
                    else
                    {
                        s = PositiveBuffApplicationRules.ApplyResonance(
                            s,
                            actorId,
                            resonanceBefore,
                            [new StatusChange(BattleStatusIds.Strength, growth)],
                            growthSource,
                            events,
                            ref order,
                            catalog,
                            rng);
                    }
                }
            }

            actor = FindActor(s, actorId)!;
            if (OwnsCardResources(s, actor))
            {
                int insight = actor.GetStatus(BattleStatusIds.Insight);
                if (insight > 0)
                {
                    actor = BattleStatusRules.Clear(actor, BattleStatusIds.Insight);
                    s = ReplaceActor(s, actorId, actor);
                    s = DrawForActorOwner(
                        s, actorId, insight, rng, catalog,
                        out int drawn, out int shuffledCardCount);
                    if (shuffledCardCount > 0)
                    {
                        events.Add(new BattleEvent(
                            BattleEventKind.Shuffle, order++, actorId, actorId,
                            Amount: shuffledCardCount,
                            Note: BattleStatusIds.Insight));
                    }
                    events.Add(new BattleEvent(
                        BattleEventKind.Draw, order++, actorId, actorId,
                        Amount: drawn, Note: BattleStatusIds.Insight));
                }

                actor = FindActor(s, actorId)!;
                int haste = actor.GetStatus(BattleStatusIds.Haste);
                if (haste > 0)
                {
                    actor = BattleStatusRules.Clear(actor, BattleStatusIds.Haste);
                    s = ReplaceActor(s, actorId, actor);
                    pendingEnergy[actorId] = pendingEnergy.GetValueOrDefault(actorId) + haste;
                    events.Add(new BattleEvent(
                        BattleEventKind.GainEnergy, order++, actorId, actorId,
                        Amount: haste, Note: BattleStatusIds.Haste));
                }

                actor = FindActor(s, actorId)!;
                int mastery = actor.GetStatus(BattleStatusIds.Mastery);
                if (mastery > 0)
                {
                    actor = BattleStatusRules.Decrement(actor, BattleStatusIds.Mastery);
                    s = ReplaceActor(s, actorId, actor);
                    s = AddComboGaugeForActorOwner(
                        s, actorId, mastery, out int actualGain);
                    events.Add(new BattleEvent(
                        BattleEventKind.ComboGaugeChanged, order++, actorId, actorId,
                        Amount: actualGain, Note: BattleStatusIds.Mastery));
                }
            }
        }

        foreach (string actorId in actorIds)
        {
            var actor = FindActor(s, actorId);
            if (actor is null || !actor.IsAlive) continue;

            int burn = actor.GetStatus(BattleStatusIds.Burn);
            if (burn > 0)
            {
                bool wasEnemy = actor.Side == ActorSide.Enemy;
                int damage = System.Math.Max(1,
                    (int)((long)System.Math.Max(0, actor.CurrentHp) * burn / 100));
                int actual = System.Math.Min(actor.CurrentHp, damage);
                var burnSource = actor.GetStatusSource(BattleStatusIds.Burn)
                    ?? CombatSource.Unattributed;
                var burnChain = new DamageCauseChain(ImmutableArray.Create(
                    new DamageCauseFrame(
                        0, DamageType.Environment, burnSource, null,
                        BattleStatusIds.Burn)));
                int scorch = actor.GetStatus(BattleStatusIds.Scorch);
                var afterBurnStatus = scorch > 0
                    ? BattleStatusRules.Decrement(actor, BattleStatusIds.Scorch)
                    : BattleStatusRules.Clear(actor, BattleStatusIds.Burn);
                var damaged = afterBurnStatus with
                {
                    CurrentHp = System.Math.Max(0, actor.CurrentHp - damage),
                };
                bool revived = BattleStatusRules.TryRevive(damaged, out var afterRevive);
                s = ReplaceActor(s, actorId, afterRevive);
                events.Add(new BattleEvent(
                    BattleEventKind.DealDamage, order++, actorId, actorId,
                    Amount: damage, Note: BattleStatusIds.Burn,
                    DamageType: DamageType.Environment,
                    Source: burnSource, CauseChain: burnChain,
                    ActualHpLoss: actual, HitLanded: damage > 0));
                if (revived)
                {
                    events.Add(new BattleEvent(
                        BattleEventKind.RemoveStatus, order++, actorId, actorId,
                        Note: BattleStatusIds.Revive,
                        Source: actor.GetStatusSource(BattleStatusIds.Revive)));
                    events.Add(new BattleEvent(
                        BattleEventKind.Heal, order++, actorId, actorId,
                        Amount: afterRevive.CurrentHp, Note: BattleStatusIds.Revive));
                }
                else if (!afterRevive.IsAlive)
                {
                    events.Add(new BattleEvent(
                        BattleEventKind.ActorDeath, order++, actorId, actorId,
                        Note: BattleStatusIds.Burn,
                        DamageType: DamageType.Environment,
                        Source: burnSource, CauseChain: burnChain,
                        ActualHpLoss: actual, HitLanded: true));
                    if (wasEnemy)
                    {
                        var (afterRelic, relicEvents) =
                            RelicTriggerProcessor.FireOnEnemyDeath(
                                s, actorId, catalog, rng, orderStart: order);
                        s = afterRelic;
                        foreach (var relicEvent in relicEvents)
                            events.Add(relicEvent with { Order = order++ });

                        var (afterPowerDeath, powerDeathEvents) =
                            PowerTriggerProcessor.FireOnEnemyDeath(
                                s, actorId, catalog, rng,
                                orderStart: order);
                        s = afterPowerDeath;
                        foreach (var powerEvent in powerDeathEvents)
                            events.Add(powerEvent with { Order = order++ });
                    }
                }
                else if (afterRevive.IsAlive && actual > 0)
                {
                    var (afterPower, powerEvents) =
                        PowerTriggerProcessor.FireOnDamageReceived(
                            s, actorId, catalog, rng, orderStart: order);
                    s = afterPower;
                    foreach (var powerEvent in powerEvents)
                        events.Add(powerEvent with { Order = order++ });
                }
            }

            actor = FindActor(s, actorId);
            if (actor is null || !actor.IsAlive || !OwnsCardResources(s, actor)) continue;
            int exhaustion = actor.GetStatus(BattleStatusIds.Exhaustion);
            if (exhaustion > 0)
            {
                actor = BattleStatusRules.Decrement(actor, BattleStatusIds.Exhaustion);
                s = ReplaceActor(s, actorId, actor);
                pendingEnergy[actorId] = pendingEnergy.GetValueOrDefault(actorId) - exhaustion;
                events.Add(new BattleEvent(
                    BattleEventKind.GainEnergy, order++, actorId, actorId,
                    Amount: -exhaustion, Note: BattleStatusIds.Exhaustion));
            }
        }
        return s;
    }

    private static bool OwnsCardResources(BattleState state, CombatActor actor)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return actor.DefinitionId == "hero";
        return BattleParticipantAccess.TryGetByActor(
            state, actor.InstanceId, out _);
    }

    private static BattleState DrawForActorOwner(
        BattleState state,
        string actorInstanceId,
        int count,
        IRng rng,
        DataCatalog catalog,
        out int drawn,
        out int shuffledCardCount)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return DrawHelper.Draw(
                state, count, rng, out drawn, out shuffledCardCount);
        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant))
        {
            drawn = 0;
            shuffledCardCount = 0;
            return state;
        }

        var projected = BattleParticipantAccess.ProjectToRoot(
            state, participant.ParticipantId);
        var owner = FindActor(projected, actorInstanceId);
        int handLimit = catalog.TryGetUnit(participant.CharacterId, out var unit)
            ? unit.HandLimit
            : DrawHelper.HandCap;
        projected = DrawHelper.Draw(
            projected, count, rng, handLimit, owner,
            out drawn, out shuffledCardCount);
        return BattleParticipantAccess.CommitRootToParticipant(
            projected, participant.ParticipantId);
    }

    private static BattleState AddEnergyForActorOwner(
        BattleState state,
        string actorInstanceId,
        int amount)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            int energy = amount >= 0
                ? EnergyMath.AddSaturating(state.Energy, amount)
                : System.Math.Max(0, state.Energy + amount);
            return state with { Energy = energy };
        }
        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant))
            return state;

        var projected = BattleParticipantAccess.ProjectToRoot(
            state, participant.ParticipantId);
        int updated = amount >= 0
            ? EnergyMath.AddSaturating(projected.Energy, amount)
            : System.Math.Max(0, projected.Energy + amount);
        projected = projected with { Energy = updated };
        return BattleParticipantAccess.CommitRootToParticipant(
            projected, participant.ParticipantId);
    }

    private static BattleState AddComboGaugeForActorOwner(
        BattleState state,
        string actorInstanceId,
        int amount,
        out int actualGain)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            int before = state.ComboGauge;
            int after = (int)System.Math.Min(
                BattleEngine.MaxComboGauge, (long)before + amount);
            actualGain = after - before;
            return state with { ComboGauge = after };
        }
        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant))
        {
            actualGain = 0;
            return state;
        }

        int next = (int)System.Math.Min(
            BattleEngine.MaxComboGauge, (long)participant.ComboGauge + amount);
        actualGain = next - participant.ComboGauge;
        return BattleParticipantAccess.Replace(
            state, participant with { ComboGauge = next });
    }

    private static BattleState ApplyPoisonTick(
        BattleState state, List<BattleEvent> events, ref int order,
        DataCatalog catalog, IRng rng)
    {
        // Allies と Enemies の InstanceId スナップショットを採る
        var actorIds = state.Allies.OrderBy(a => a.SlotIndex).Select(a => a.InstanceId)
            .Concat(state.Enemies.OrderBy(e => e.SlotIndex).Select(e => e.InstanceId))
            .ToList();

        var s = state;
        foreach (var aid in actorIds)
        {
            CombatActor? actor = FindActor(s, aid);
            if (actor is null || !actor.IsAlive) continue;
            int poison = actor.GetStatus("poison");
            if (poison <= 0) continue;

            bool wasAlive = actor.IsAlive;
            bool wasEnemy = actor.Side == ActorSide.Enemy;

            // Phase 10.5.M6.5: ダメージを与えた直後に poison stack を -1 する。
            //  旧仕様 (countdown 経由) では Enemy turn 終了後 countdown → 次 turn 開始で
            //  poison-1 のダメージ、という順序になり、毒 4 のはずが 3 ダメで止まるバグ
            //  があった。Slay the Spire と同じく「毒ダメ後に -1」が正しい。
            int newPoison = poison - 1;
            int virulent = System.Math.Max(0, actor.GetStatus(BattleStatusIds.VirulentPoison));
            long multiplier = virulent > 0 ? virulent : 1L;
            int poisonDamage = (int)System.Math.Clamp(
                (long)poison * multiplier,
                0L,
                int.MaxValue);
            int actualHpLoss = System.Math.Min(System.Math.Max(actor.CurrentHp, 0), poisonDamage);
            int overkill = System.Math.Max(0, poisonDamage - System.Math.Max(actor.CurrentHp, 0));
            var poisonSource = actor.GetStatusSource("poison")
                ?? CombatSource.Unattributed;
            var poisonChain = new DamageCauseChain(System.Collections.Immutable.ImmutableArray.Create(
                new DamageCauseFrame(0, DamageType.Poison, poisonSource, null, "poison")));
            var newStatuses = newPoison <= 0
                ? actor.Statuses.Remove("poison")
                : actor.Statuses.SetItem("poison", newPoison);
            var newStatusSources = newPoison <= 0
                ? actor.StatusSources.Remove("poison")
                : actor.StatusSources;
            var updated = actor with
            {
                CurrentHp = System.Math.Max(0, actor.CurrentHp - poisonDamage),
                Statuses = newStatuses,
                StatusSources = newStatusSources,
            };
            bool revived = BattleStatusRules.TryRevive(updated, out var afterRevive);
            updated = afterRevive;
            s = ReplaceActor(s, aid, updated);

            events.Add(new BattleEvent(
                BattleEventKind.PoisonTick, Order: order++,
                TargetInstanceId: aid, Amount: poisonDamage, Note: "poison",
                DamageType: DamageType.Poison, Source: poisonSource,
                CauseChain: poisonChain, ActualHpLoss: actualHpLoss,
                Overkill: overkill, HitLanded: poison > 0));

            if (newPoison <= 0)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus, Order: order++,
                    TargetInstanceId: aid, Note: "poison",
                    DamageType: DamageType.Poison, Source: poisonSource,
                    CauseChain: poisonChain, ActualHpLoss: actualHpLoss,
                    Overkill: overkill, HitLanded: poison > 0));
            }

            if (revived)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus, Order: order++,
                    TargetInstanceId: aid,
                    Note: BattleStatusIds.Revive,
                    Source: actor.GetStatusSource(BattleStatusIds.Revive)));
                events.Add(new BattleEvent(
                    BattleEventKind.Heal, Order: order++,
                    TargetInstanceId: aid, Amount: updated.CurrentHp,
                    Note: BattleStatusIds.Revive));
            }
            else if (wasAlive && !updated.IsAlive)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ActorDeath, Order: order++,
                    TargetInstanceId: aid, Note: "poison",
                    DamageType: DamageType.Poison, Source: poisonSource,
                    CauseChain: poisonChain, ActualHpLoss: actualHpLoss,
                    Overkill: overkill, HitLanded: poison > 0));

                // 10.2.E: 敵の毒死で OnEnemyDeath 発火
                if (wasEnemy)
                {
                    var (afterRelic, evsRelic) = RelicTriggerProcessor.FireOnEnemyDeath(
                        s, aid, catalog, rng, orderStart: order);
                    s = afterRelic;
                    foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

                    var (afterPowerDeath, evsPowerDeath) =
                        PowerTriggerProcessor.FireOnEnemyDeath(
                            s, aid, catalog, rng, orderStart: order);
                    s = afterPowerDeath;
                    foreach (var ev in evsPowerDeath)
                        events.Add(ev with { Order = order++ });
                }
            }
            else if (updated.IsAlive && actualHpLoss > 0)
            {
                var (afterPower, evsPower) = PowerTriggerProcessor.FireOnDamageReceived(
                    s, aid, catalog, rng, orderStart: order);
                s = afterPower;
                foreach (var ev in evsPower) { events.Add(ev with { Order = order++ }); }
            }
        }
        return s;
    }

    // ApplyStatusCountdown は SideStatusCountdown.ApplyForSide に移動 (新仕様)。
    // BattleEngine.EndTurn が PlayerAttacking 直後に Ally 側、EnemyAttacking 直後に
    // Enemy 側の countdown を呼ぶ形に変更したため、ここでの全 actor 一括 countdown は
    // 削除した。

    private static BattleState ApplyLifetimeTick(
        BattleState state, List<BattleEvent> events, ref int order)
    {
        // Lifetime あり ally の InstanceId スナップショット
        var allyIds = state.Allies
            .Where(a => a.Side == ActorSide.Ally
                     && a.RemainingLifetimeTurns is not null
                     && a.IsAlive)
            .OrderBy(a => a.SlotIndex)
            .Select(a => a.InstanceId)
            .ToList();

        var s = state;
        foreach (var aid in allyIds)
        {
            var actor = FindActor(s, aid);
            if (actor is null || !actor.IsAlive) continue;
            if (actor.RemainingLifetimeTurns is null) continue;

            int newRemaining = actor.RemainingLifetimeTurns.Value - 1;

            if (newRemaining <= 0)
            {
                // 死亡
                var diedActor = actor with
                {
                    RemainingLifetimeTurns = newRemaining,
                    CurrentHp = 0,
                };
                s = ReplaceActor(s, aid, diedActor);
                events.Add(new BattleEvent(
                    BattleEventKind.ActorDeath, Order: order++,
                    TargetInstanceId: aid, Note: "lifetime"));
            }
            else
            {
                s = ReplaceActor(s, aid, actor with { RemainingLifetimeTurns = newRemaining });
            }
        }
        return s;
    }

    private static CombatActor? FindActor(BattleState state, string instanceId)
    {
        foreach (var a in state.Allies) if (a.InstanceId == instanceId) return a;
        foreach (var e in state.Enemies) if (e.InstanceId == instanceId) return e;
        return null;
    }

    private static BattleState ReplaceActor(BattleState state, string instanceId, CombatActor after)
    {
        if (after.Side == ActorSide.Ally)
        {
            for (int i = 0; i < state.Allies.Length; i++)
                if (state.Allies[i].InstanceId == instanceId)
                    return state with { Allies = state.Allies.SetItem(i, after) };
        }
        else
        {
            for (int i = 0; i < state.Enemies.Length; i++)
                if (state.Enemies[i].InstanceId == instanceId)
                    return state with { Enemies = state.Enemies.SetItem(i, after) };
        }
        return state;
    }

}
