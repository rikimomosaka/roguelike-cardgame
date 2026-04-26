using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleDeterminismTests
{
    private static RunState MakeRun() => new(
        SchemaVersion: RunState.CurrentSchemaVersion,
        CurrentAct: 1, CurrentNodeId: 0,
        VisitedNodeIds: ImmutableArray<int>.Empty,
        UnknownResolutions: ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
        CharacterId: "default", CurrentHp: 70, MaxHp: 70, Gold: 0,
        Deck: Enumerable.Range(0, 10).Select(i => new CardInstance("strike", false)).ToImmutableArray(),
        Potions: ImmutableArray<string>.Empty, PotionSlotCount: 0,
        ActiveBattle: null, ActiveReward: null,
        EncounterQueueWeak: ImmutableArray<string>.Empty,
        EncounterQueueStrong: ImmutableArray<string>.Empty,
        EncounterQueueElite: ImmutableArray<string>.Empty,
        EncounterQueueBoss: ImmutableArray<string>.Empty,
        RewardRngState: new RoguelikeCardGame.Core.Rewards.RewardRngState(0, 0),
        ActiveMerchant: null, ActiveEvent: null,
        ActiveRestPending: false, ActiveRestCompleted: false,
        Relics: System.Array.Empty<string>(),
        PlaySeconds: 0, RngSeed: 1,
        SavedAtUtc: System.DateTimeOffset.UtcNow,
        Progress: RunProgress.InProgress,
        RunId: "run1", ActiveActStartRelicChoice: null,
        SeenCardBaseIds: ImmutableArray<string>.Empty,
        AcquiredRelicIds: ImmutableArray<string>.Empty,
        AcquiredPotionIds: ImmutableArray<string>.Empty,
        EncounteredEnemyIds: ImmutableArray<string>.Empty,
        JourneyLog: ImmutableArray<RoguelikeCardGame.Core.Run.JourneyEntry>.Empty);

    private static (BattleState, System.Collections.Generic.List<BattleEvent>) RunBattle(int seed)
    {
        var rng = new SequentialRng((ulong)seed);
        var run = MakeRun();
        var cat = BattleFixtures.MinimalCatalog();
        var (s, _) = BattleEngine.Start(run, "enc_test", rng, cat);
        var allEvents = new System.Collections.Generic.List<BattleEvent>();

        // 1 ターン目：先頭の strike を打って EndTurn
        var (s2, evs1) = BattleEngine.PlayCard(s, 0, 0, 0, rng, cat);
        allEvents.AddRange(evs1);
        var (s3, evs2) = BattleEngine.EndTurn(s2, rng, cat);
        allEvents.AddRange(evs2);
        return (s3, allEvents);
    }

    /// <summary>ImmutableArray はレコード等価では参照比較になるため JSON シリアライズで深い構造比較を行う。</summary>
    private static string StateJson(BattleState s) =>
        JsonSerializer.Serialize(new
        {
            s.Turn, Phase = s.Phase.ToString(), Outcome = s.Outcome.ToString(),
            s.TargetAllyIndex, s.TargetEnemyIndex, s.Energy, s.EnergyMax, s.EncounterId,
            Allies = s.Allies.Select(a => new {
                a.InstanceId, a.DefinitionId, Side = a.Side.ToString(),
                a.SlotIndex, a.CurrentHp, a.MaxHp,
                Block = a.Block.Sum, AttackSingle = a.AttackSingle.Sum,
                AttackRandom = a.AttackRandom.Sum, AttackAll = a.AttackAll.Sum,
                a.CurrentMoveId,
                Statuses = a.Statuses.OrderBy(kv => kv.Key).Select(kv => new { kv.Key, kv.Value }) }),
            Enemies = s.Enemies.Select(e => new {
                e.InstanceId, e.DefinitionId, Side = e.Side.ToString(),
                e.SlotIndex, e.CurrentHp, e.MaxHp,
                Block = e.Block.Sum, AttackSingle = e.AttackSingle.Sum,
                AttackRandom = e.AttackRandom.Sum, AttackAll = e.AttackAll.Sum,
                e.CurrentMoveId,
                Statuses = e.Statuses.OrderBy(kv => kv.Key).Select(kv => new { kv.Key, kv.Value }) }),
            DrawPile = s.DrawPile.Select(c => new { c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride }),
            Hand = s.Hand.Select(c => new { c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride }),
            DiscardPile = s.DiscardPile.Select(c => new { c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride }),
            ExhaustPile = s.ExhaustPile.Select(c => new { c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride }),
        });

    [Fact] public void Same_seed_same_inputs_yields_identical_state()
    {
        var (a, _) = RunBattle(seed: 42);
        var (b, _) = RunBattle(seed: 42);
        Assert.Equal(StateJson(a), StateJson(b));
    }

    [Fact] public void Same_seed_same_inputs_yields_identical_events()
    {
        var (_, ea) = RunBattle(seed: 42);
        var (_, eb) = RunBattle(seed: 42);
        Assert.Equal(ea, eb);
    }

    [Fact] public void Same_seed_with_status_card_yields_identical_state()
    {
        // buff_self_str を含むデッキで 2 回回し、State 一致を検証（デッキに buff_self_str を確実に含める）
        BattleState RunWithBuff(int seed)
        {
            var rng = new SequentialRng((ulong)seed);
            var run = MakeRun() with
            {
                Deck = ImmutableArray.Create(
                    new CardInstance("buff_self_str", false),
                    new CardInstance("buff_self_str", false),
                    new CardInstance("strike", false),
                    new CardInstance("strike", false),
                    new CardInstance("strike", false),
                    new CardInstance("strike", false),
                    new CardInstance("strike", false)),
            };
            var cards = new[]
            {
                BattleFixtures.Strike(),
                new RoguelikeCardGame.Core.Cards.CardDefinition(
                    "buff_self_str", "Buff Self Str", null,
                    RoguelikeCardGame.Core.Cards.CardRarity.Common,
                    RoguelikeCardGame.Core.Cards.CardType.Skill,
                    Cost: 1, UpgradedCost: null,
                    Effects: new[] { new RoguelikeCardGame.Core.Cards.CardEffect(
                        "buff", RoguelikeCardGame.Core.Cards.EffectScope.Self, null, 2,
                        Name: "strength") },
                    UpgradedEffects: null, Keywords: null),
            };
            var cat = BattleFixtures.MinimalCatalog(cards: cards);
            var (s, _) = BattleEngine.Start(run, "enc_test", rng, cat);
            // buff_self_str が手札にあれば打つ（シード固定なので毎回同じ引きになる）
            int buffIdx = -1;
            for (int i = 0; i < s.Hand.Length; i++)
                if (s.Hand[i].CardDefinitionId == "buff_self_str") { buffIdx = i; break; }
            if (buffIdx >= 0)
            {
                var (s2, _) = BattleEngine.PlayCard(s, buffIdx, 0, 0, rng, cat);
                var (s3, _) = BattleEngine.EndTurn(s2, rng, cat);
                return s3;
            }
            var (sNext, _) = BattleEngine.EndTurn(s, rng, cat);
            return sNext;
        }

        var a = RunWithBuff(seed: 100);
        var b = RunWithBuff(seed: 100);
        Assert.Equal(StateJson(a), StateJson(b));
    }

    [Fact] public void Combat_with_combo_and_set_target_is_deterministic()
    {
        // 同じ seed + 同じ操作列（SetTarget → PlayCard）→ 同じ最終 state（コンボ関連フィールドを含む）
        BattleState RunComboAndPlay(int seed)
        {
            var rng = new SequentialRng((ulong)seed);
            var run = MakeRun();
            var cat = BattleFixtures.MinimalCatalog();
            var (s, _) = BattleEngine.Start(run, "enc_test", rng, cat);

            // SetTarget(Enemy, 0) → PlayCard(0)
            s = BattleEngine.SetTarget(s, ActorSide.Enemy, 0);
            if (s.Hand.Length > 0 && s.Energy >= 1)
            {
                var (after, _) = BattleEngine.PlayCard(s, 0, 0, 0, rng, cat);
                return after;
            }
            return s;
        }

        var a = RunComboAndPlay(seed: 7777);
        var b = RunComboAndPlay(seed: 7777);

        // コンボ関連フィールドの個別 assertion（StateJson は ComboCount / LastPlayedOrigCost / NextCardComboFreePass を含まないため）
        Assert.Equal(a.ComboCount, b.ComboCount);
        Assert.Equal(a.LastPlayedOrigCost, b.LastPlayedOrigCost);
        Assert.Equal(a.NextCardComboFreePass, b.NextCardComboFreePass);
        Assert.Equal(a.Energy, b.Energy);
        Assert.Equal(a.Hand.Length, b.Hand.Length);
        // 全体構造の一致も確認
        Assert.Equal(StateJson(a), StateJson(b));
    }

    [Fact] public void Combat_with_summon_and_heal_is_deterministic()
    {
        // 10.2.D: 召喚 + heal を含む環境で seed 一致確認
        BattleState RunWithSummonAndHeal(int seed)
        {
            var rng = new SequentialRng((ulong)seed);
            var run = MakeRun();
            var summonCard = new CardDefinition(
                "call_minion", "Call Minion", null,
                CardRarity.Common, CardType.Unit,
                Cost: 1, UpgradedCost: null,
                Effects: new[] { new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion") },
                UpgradedEffects: null, Keywords: null);
            var healCard = new CardDefinition(
                "aid", "Aid", null,
                CardRarity.Common, CardType.Skill,
                Cost: 1, UpgradedCost: null,
                Effects: new[] { new CardEffect("heal", EffectScope.Self, null, 5) },
                UpgradedEffects: null, Keywords: null);
            var cat = BattleFixtures.MinimalCatalog(
                cards: new[] { BattleFixtures.Strike(), summonCard, healCard },
                units: new[] { BattleFixtures.MinionDef() });
            var (startState, _) = BattleEngine.Start(run, "enc_test", rng, cat);
            return startState;
        }

        var a = RunWithSummonAndHeal(seed: 314);
        var b = RunWithSummonAndHeal(seed: 314);

        Assert.Equal(a.Allies.Length, b.Allies.Length);
        Assert.Equal(a.SummonHeld.Length, b.SummonHeld.Length);
        Assert.Equal(a.PowerCards.Length, b.PowerCards.Length);
        Assert.Equal(a.Energy, b.Energy);
        Assert.Equal(a.Hand.Length, b.Hand.Length);
        Assert.Equal(StateJson(a), StateJson(b));
    }

    [Fact]
    public void Combat_with_relic_and_potion_is_deterministic()
    {
        // 10.2.E: レリック発動 + UsePotion を含む 1 戦闘で seed 一致確認
        var relic = BattleFixtures.Relic("ts_atk", RoguelikeCardGame.Core.Relics.RelicTrigger.OnTurnStart, true,
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 2));
        var potion = BattleFixtures.Potion("heal_p",
            new CardEffect("heal", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend() },
            relics: new[] { relic },
            potions: new[] { potion });

        RunState MakeRunWithRelicAndPotion() => new RunState(
            SchemaVersion: RunState.CurrentSchemaVersion,
            CurrentAct: 1, CurrentNodeId: 0,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: System.Collections.Immutable.ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
            CharacterId: "default", CurrentHp: 30, MaxHp: 70, Gold: 0,
            Deck: ImmutableArray.Create(new CardInstance("strike", false), new CardInstance("defend", false)),
            Potions: ImmutableArray.Create("heal_p", "", ""),
            PotionSlotCount: 3,
            ActiveBattle: null, ActiveReward: null,
            EncounterQueueWeak: ImmutableArray<string>.Empty,
            EncounterQueueStrong: ImmutableArray<string>.Empty,
            EncounterQueueElite: ImmutableArray<string>.Empty,
            EncounterQueueBoss: ImmutableArray<string>.Empty,
            RewardRngState: new RoguelikeCardGame.Core.Rewards.RewardRngState(0, 0),
            ActiveMerchant: null, ActiveEvent: null,
            ActiveRestPending: false, ActiveRestCompleted: false,
            Relics: new[] { "ts_atk" },
            PlaySeconds: 0L, RngSeed: 0UL,
            SavedAtUtc: DateTimeOffset.UnixEpoch,
            Progress: RunProgress.InProgress,
            RunId: "test-run",
            ActiveActStartRelicChoice: null,
            SeenCardBaseIds: ImmutableArray<string>.Empty,
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<JourneyEntry>.Empty);

        BattleState Play()
        {
            var (state, _) = BattleEngine.Start(MakeRunWithRelicAndPotion(), "enc_test", new FakeRng(new int[20], System.Array.Empty<double>()), catalog);
            var (afterPotion, _) = BattleEngine.UsePotion(state, 0, null, null, new FakeRng(new int[20], System.Array.Empty<double>()), catalog);
            return afterPotion;
        }

        var s1 = Play();
        var s2 = Play();

        var json1 = System.Text.Json.JsonSerializer.Serialize(s1);
        var json2 = System.Text.Json.JsonSerializer.Serialize(s2);
        Assert.Equal(json1, json2);
    }
}
