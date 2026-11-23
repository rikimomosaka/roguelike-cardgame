using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class CombatAttributionAndReactionTests
{
    [Fact]
    public void AttackPool_preserves_attributed_order_and_json_round_trip()
    {
        var first = new CombatSource(CombatSourceKind.CardEffect, 0, "hero_inst", "hero_inst",
            DefinitionId: "strike", InstanceId: "strike-h0", EffectIndex: 0);
        var second = new CombatSource(CombatSourceKind.CardEffect, 0, "hero_inst", "hero_inst",
            DefinitionId: "jab", InstanceId: "jab-h1", EffectIndex: 1);
        var pool = AttackPool.Empty.Add(6, first).Add(2, second);

        Assert.Equal(new[] { 6, 2 }, pool.HitAmounts);
        Assert.Equal(new[] { first, second }, pool.Hits.Select(h => h.Source));

        var restored = JsonSerializer.Deserialize<AttackPool>(JsonSerializer.Serialize(pool));
        Assert.Equal(pool, restored);
        Assert.Equal("jab-h1", restored.Hits[1].Source.InstanceId);
    }

    [Fact]
    public void Played_card_captures_card_instance_and_effect_index_in_queued_hit()
    {
        var cardDef = BattleFixtures.Strike(6);
        var card = BattleFixtures.MakeBattleCard("strike", "strike-h7");
        var state = BattleFixtures.MinimalState(hand: ImmutableArray.Create(card));

        var (next, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(cards: new[] { cardDef }));

        var hit = Assert.Single(next.Allies[0].AttackSingle.Hits);
        Assert.Equal(CombatSourceKind.CardEffect, hit.Source.Kind);
        Assert.Equal("strike", hit.Source.DefinitionId);
        Assert.Equal("strike-h7", hit.Source.InstanceId);
        Assert.Equal(0, hit.Source.EffectIndex);
        Assert.Equal(0, hit.Source.OwnerSlot);
        Assert.Equal("single", hit.Source.Scope);
    }

    [Fact]
    public void Fully_blocked_hit_does_not_consume_barrier_but_still_fires_OnHit()
    {
        var onHitPower = OnHitPower("on_hit", chance: 100, amount: 2);
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(5) };
        var enemy = BattleFixtures.WithStatus(BattleFixtures.Goblin(hp: 20), "barrier", 2) with
        {
            Block = BlockPool.Empty.Add(9),
        };
        var state = PlayerAttackState(hero, enemy) with
        {
            PowerCards = ImmutableArray.Create(BattleFixtures.MakeBattleCard("on_hit", "on-hit-p0")),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(cards: new[] { onHitPower }));

        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(2, next.Enemies[0].GetStatus("barrier"));
        Assert.Equal(4, next.Enemies[0].Block.Sum);
        Assert.Equal(2, next.Enemies[0].GetStatus("weak"));
        var damage = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(0, damage.ActualHpLoss);
        Assert.True(damage.HitLanded);
    }

    [Fact]
    public void Lifesteal_uses_actual_hp_loss_and_damage_reports_overkill()
    {
        var hero = BattleFixtures.WithStatus(BattleFixtures.Hero(currentHp: 50, maxHp: 70), "lifesteal", 50) with
        {
            AttackSingle = AttackPool.Empty.Add(100),
        };
        var state = PlayerAttackState(hero, BattleFixtures.Goblin(hp: 3));

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []), BattleFixtures.MinimalCatalog());

        Assert.Equal(51, next.Allies[0].CurrentHp);
        Assert.Equal(0, next.Enemies[0].CurrentHp);
        var damage = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(3, damage.ActualHpLoss);
        Assert.Equal(97, damage.Overkill);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Heal && e.Note == "lifesteal" && e.Amount == 1);
    }

    [Fact]
    public void Summon_attack_uses_its_owners_lifesteal_and_heals_the_owner()
    {
        var unit = new UnitDefinition(
            "wolf", "Wolf", "wolf", 10, "bite",
            new[] { new MoveDefinition("bite", MoveKind.Attack,
                new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4) },
                "bite") });
        var firstHero = BattleFixtures.Hero(currentHp: 40, maxHp: 70);
        var owner = BattleFixtures.WithStatus(
            BattleFixtures.Hero(currentHp: 50, maxHp: 70, slotIndex: 5) with
            {
                InstanceId = "hero-owner-5",
                OwnerPlayerSlot = 5,
                OwnerActorInstanceId = "hero-owner-5",
            },
            "lifesteal", 50);
        var summon = BattleFixtures.SummonActor(
            "wolf-inst", "wolf", 1, associatedCardId: "wolf-card", moveId: "bite") with
        {
            OwnerPlayerSlot = 5,
            OwnerActorInstanceId = owner.InstanceId,
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(firstHero, owner, summon),
            enemies: ImmutableArray.Create(BattleFixtures.Goblin(hp: 4))) with
        {
            Phase = BattlePhase.PlayerAttacking,
            TargetEnemyIndex = 0,
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []), BattleFixtures.MinimalCatalog(units: new[] { unit }));

        Assert.Equal(40, next.Allies.Single(a => a.InstanceId == firstHero.InstanceId).CurrentHp);
        Assert.Equal(52, next.Allies.Single(a => a.InstanceId == owner.InstanceId).CurrentHp);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Heal
            && e.Note == "lifesteal" && e.TargetInstanceId == owner.InstanceId && e.Amount == 2);
        var damage = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage
            && e.DamageType == DamageType.Attack);
        Assert.Equal(5, damage.Source?.OwnerSlot);
        Assert.Equal(owner.InstanceId, damage.Source?.OwnerInstanceId);
    }

    [Fact]
    public void Participant_controlled_summon_uses_its_own_lifesteal_not_its_summoners()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(currentHp: 40, maxHp: 70),
            "lifesteal", 100);
        var source = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: hero.SlotIndex,
            OwnerInstanceId: hero.InstanceId,
            ActorInstanceId: "wisp-participant",
            DefinitionId: "wisp-card");
        var wisp = BattleFixtures.WithStatus(
            BattleFixtures.SummonActor(
                "wisp-participant", "wisp", slotIndex: 1,
                hp: 30) with
            {
                CurrentHp = 20,
                OwnerPlayerSlot = 0,
                OwnerActorInstanceId = hero.InstanceId,
                AttackSingle = AttackPool.Empty.Add(10, source),
            },
            "lifesteal", 50);
        var state = BattleFixtures.MinimalState(
            allies: [hero, wisp],
            enemies: [BattleFixtures.Goblin(hp: 20)]) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };
        state = BattleParticipantAccess.InitializePrimary(state);
        var primary = BattleParticipantAccess.GetPrimary(state);
        state = BattleParticipantAccess.Add(state, primary with
        {
            ParticipantId = "cpu:wisp-participant",
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            CharacterId = wisp.DefinitionId,
            TargetAllyIndex = 1,
            DrawPile = [],
            Hand = [],
            DiscardPile = [],
            ExhaustPile = [],
            SummonHeld = [],
            PowerCards = [],
            OwnedRelicIds = [],
            Potions = [],
        });

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []), BattleFixtures.MinimalCatalog());

        Assert.Equal(40, next.Allies.Single(actor =>
            actor.InstanceId == hero.InstanceId).CurrentHp);
        Assert.Equal(25, next.Allies.Single(actor =>
            actor.InstanceId == wisp.InstanceId).CurrentHp);
        var heal = Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.Heal
            && battleEvent.Note == "lifesteal");
        Assert.Equal(wisp.InstanceId, heal.TargetInstanceId);
        Assert.Equal(5, heal.Amount);
    }

    [Fact]
    public void Reflect_is_direct_ignores_block_and_does_not_reflect_again()
    {
        var hero = BattleFixtures.WithStatus(BattleFixtures.Hero(), "reflect", 99) with
        {
            AttackSingle = AttackPool.Empty.Add(2),
            Block = BlockPool.Empty.Add(50),
        };
        var enemy = BattleFixtures.WithStatus(BattleFixtures.Goblin(hp: 20), "reflect", 4);

        var (next, events) = PlayerAttackingResolver.Resolve(
            PlayerAttackState(hero, enemy), new FakeRng([], []), BattleFixtures.MinimalCatalog());

        Assert.Equal(66, next.Allies[0].CurrentHp);
        Assert.Equal(50, next.Allies[0].Block.Sum);
        var damages = events.Where(e => e.Kind == BattleEventKind.DealDamage).ToList();
        Assert.Equal(2, damages.Count);
        var reflected = Assert.Single(damages, e => e.DamageType == DamageType.Reflected);
        Assert.Equal(4, reflected.ActualHpLoss);
        Assert.Equal(2, reflected.CauseChain?.Length);
        Assert.Equal("reflect", reflected.CauseChain?[1].Cause);
    }

    [Fact]
    public void Reflect_triggers_once_per_hit_even_when_block_absorbs_all_damage()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(2).Add(2),
        };
        var enemy = BattleFixtures.WithStatus(BattleFixtures.Goblin(hp: 20), "reflect", 4) with
        {
            Block = BlockPool.Empty.Add(99),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            PlayerAttackState(hero, enemy), new FakeRng([], []), BattleFixtures.MinimalCatalog());

        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(62, next.Allies[0].CurrentHp);
        Assert.Equal(2, events.Count(e =>
            e.Kind == BattleEventKind.DealDamage
            && e.DamageType == DamageType.Reflected
            && e.Amount == 4));
    }

    [Fact]
    public void OnHit_fires_before_lethal_reflect_kills_the_attacker()
    {
        var relic = BattleFixtures.Relic(
            id: "on_hit_before_reflect", trigger: "OnHit", implemented: true,
            new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1,
                Name: "weak", Trigger: "OnHit", ChancePercent: 100));
        var hero = BattleFixtures.Hero(hp: 3) with { AttackSingle = AttackPool.Empty.Add(2) };
        var enemy = BattleFixtures.WithStatus(BattleFixtures.Goblin(hp: 20), "reflect", 5);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero), enemies: ImmutableArray.Create(enemy),
            ownedRelicIds: ImmutableArray.Create(relic.Id)) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(relics: new[] { relic }));

        Assert.Equal(0, next.Allies[0].CurrentHp);
        Assert.Equal(1, next.Enemies[0].GetStatus("weak"));
        int onHitOrder = Assert.Single(events, e => e.Kind == BattleEventKind.ApplyStatus).Order;
        int reflectOrder = Assert.Single(events, e =>
            e.Kind == BattleEventKind.DealDamage && e.DamageType == DamageType.Reflected).Order;
        Assert.True(onHitOrder < reflectOrder);
        Assert.Contains(events, e => e.Kind == BattleEventKind.ActorDeath
            && e.DamageType == DamageType.Reflected && e.TargetInstanceId == hero.InstanceId);
    }

    [Fact]
    public void Reflect_amount_is_snapshotted_before_OnHit_changes_the_status()
    {
        var relic = BattleFixtures.Relic(
            id: "remove_reflect_on_hit", trigger: "OnHit", implemented: true,
            new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, -4,
                Name: "reflect", Trigger: "OnHit", ChancePercent: 100));
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(2) };
        var enemy = BattleFixtures.WithStatus(BattleFixtures.Goblin(hp: 20), "reflect", 4);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero), enemies: ImmutableArray.Create(enemy),
            ownedRelicIds: ImmutableArray.Create(relic.Id)) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(relics: new[] { relic }));

        Assert.Equal(0, next.Enemies[0].GetStatus("reflect"));
        Assert.Contains(events, e => e.Kind == BattleEventKind.DealDamage
            && e.DamageType == DamageType.Reflected && e.Amount == 4);
    }

    [Fact]
    public void Chance_OnHit_rolls_independently_for_each_All_target_even_when_barrier_blocks()
    {
        var power = OnHitPower("coin_hit", chance: 50, amount: 1);
        var hero = BattleFixtures.Hero() with { AttackAll = AttackPool.Empty.Add(3) };
        var enemies = ImmutableArray.Create(
            BattleFixtures.WithStatus(BattleFixtures.Goblin(0), "barrier", 1),
            BattleFixtures.WithStatus(BattleFixtures.Goblin(1), "barrier", 1));
        var state = BattleFixtures.MinimalState(allies: ImmutableArray.Create(hero), enemies: enemies) with
        {
            Phase = BattlePhase.PlayerAttacking,
            PowerCards = ImmutableArray.Create(BattleFixtures.MakeBattleCard("coin_hit", "coin-p0")),
        };

        var (next, _) = PlayerAttackingResolver.Resolve(
            state, new FakeRng(new[] { 0, 99 }, []),
            BattleFixtures.MinimalCatalog(cards: new[] { power }));

        Assert.Equal(1, next.Enemies[0].GetStatus("weak"));
        Assert.Equal(0, next.Enemies[1].GetStatus("weak"));
        Assert.All(next.Enemies, e => Assert.Equal(0, e.GetStatus("barrier")));
    }

    [Fact]
    public void Relic_OnHit_uses_actual_target_and_independent_chance_per_All_target()
    {
        var relic = BattleFixtures.Relic(
            id: "claw_test", trigger: "OnHit", implemented: true,
            new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1,
                Name: "weak", Trigger: "OnHit", ChancePercent: 50));
        var hero = BattleFixtures.Hero() with { AttackAll = AttackPool.Empty.Add(3) };
        var enemies = ImmutableArray.Create(
            BattleFixtures.WithStatus(BattleFixtures.Goblin(0), "barrier", 1),
            BattleFixtures.WithStatus(BattleFixtures.Goblin(1), "barrier", 1));
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero), enemies: enemies,
            ownedRelicIds: ImmutableArray.Create("claw_test")) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng(new[] { 0, 99 }, []),
            BattleFixtures.MinimalCatalog(relics: new[] { relic }));

        Assert.Equal(1, next.Enemies[0].GetStatus("weak"));
        Assert.Equal(0, next.Enemies[1].GetStatus("weak"));
        var applied = Assert.Single(events, e => e.Kind == BattleEventKind.ApplyStatus);
        Assert.Equal(next.Enemies[0].InstanceId, applied.TargetInstanceId);
        Assert.Equal(CombatSourceKind.RelicEffect, applied.Source?.Kind);
        Assert.Equal("claw_test", applied.Source?.DefinitionId);
        Assert.Equal(2, applied.CauseChain?.Length);
    }

    [Fact]
    public void Enemy_move_damage_carries_move_source_and_actual_loss()
    {
        var state = BattleFixtures.MinimalState() with { Phase = BattlePhase.EnemyAttacking };

        var (_, events) = EnemyAttackingResolver.Resolve(
            state, new FakeRng([], []), BattleFixtures.MinimalCatalog());

        var damage = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(CombatSourceKind.EnemyMove, damage.Source?.Kind);
        Assert.Equal("swing", damage.Source?.MoveId);
        Assert.Equal(0, damage.Source?.EffectIndex);
        Assert.Equal("all", damage.Source?.Scope);
        Assert.Equal(5, damage.ActualHpLoss);
        Assert.Equal(0, damage.Overkill);
    }

    [Fact]
    public void Summon_move_damage_is_owned_by_slot_zero_and_carries_move_source()
    {
        var unit = new UnitDefinition(
            "wolf", "Wolf", "wolf", 10, "bite",
            new[] { new MoveDefinition("bite", MoveKind.Attack,
                new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4) },
                "bite") });
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor(
            "wolf-inst", "wolf", 1, associatedCardId: "wolf-card-inst", moveId: "bite");
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, summon),
            enemies: ImmutableArray.Create(BattleFixtures.Goblin())) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };

        var (_, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []), BattleFixtures.MinimalCatalog(units: new[] { unit }));

        var damage = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(CombatSourceKind.SummonMove, damage.Source?.Kind);
        Assert.Equal("hero_inst", damage.Source?.OwnerInstanceId);
        Assert.Equal(0, damage.Source?.OwnerSlot);
        Assert.Equal("wolf-inst", damage.Source?.ActorInstanceId);
        Assert.Equal("wolf-card-inst", damage.Source?.InstanceId);
        Assert.Equal("bite", damage.Source?.MoveId);
        Assert.Equal("single", damage.Source?.Scope);
    }

    [Fact]
    public void Enemy_non_attack_events_keep_their_effect_specific_move_source()
    {
        var definition = new EnemyDefinition(
            "ritualist", "Ritualist", "ritualist", 12,
            new EnemyPool(1, EnemyTier.Weak), "prepare",
            new[]
            {
                new MoveDefinition("prepare", MoveKind.Buff,
                    new CardEffect[]
                    {
                        new("buff", EffectScope.Self, null, 2, Name: "strength"),
                        new("block", EffectScope.Self, null, 4),
                    },
                    "prepare"),
            });
        var ritualist = new CombatActor(
            "ritualist-inst", definition.Id, ActorSide.Enemy, 3, 12, 12,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, "prepare", null, null);
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(ritualist),
            phase: BattlePhase.EnemyAttacking);

        var (_, events) = EnemyAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(enemies: new[] { definition }));

        var status = Assert.Single(events, e => e.Kind == BattleEventKind.ApplyStatus);
        Assert.Equal(CombatSourceKind.EnemyMove, status.Source?.Kind);
        Assert.Equal(3, status.Source?.OwnerSlot);
        Assert.Equal(ritualist.InstanceId, status.Source?.OwnerInstanceId);
        Assert.Equal(ritualist.InstanceId, status.Source?.ActorInstanceId);
        Assert.Equal(definition.Id, status.Source?.DefinitionId);
        Assert.Equal("prepare", status.Source?.MoveId);
        Assert.Equal(0, status.Source?.EffectIndex);
        Assert.Equal("self", status.Source?.Scope);

        var block = Assert.Single(events, e => e.Kind == BattleEventKind.GainBlock);
        Assert.Equal(CombatSourceKind.EnemyMove, block.Source?.Kind);
        Assert.Equal(1, block.Source?.EffectIndex);
        Assert.Equal("self", block.Source?.Scope);
    }

    [Fact]
    public void Enemy_self_damage_and_death_keep_enemy_move_attribution()
    {
        var definition = new EnemyDefinition(
            "volatile", "Volatile", "volatile", 3,
            new EnemyPool(1, EnemyTier.Weak), "rupture",
            new[]
            {
                new MoveDefinition("rupture", MoveKind.Attack,
                    new[] { new CardEffect("selfDamage", EffectScope.Self, null, 5) },
                    "rupture"),
            });
        var enemy = new CombatActor(
            "volatile-inst", definition.Id, ActorSide.Enemy, 2, 3, 3,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, "rupture", null, null);
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(enemy),
            phase: BattlePhase.EnemyAttacking);

        var (next, events) = EnemyAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(enemies: new[] { definition }));

        Assert.False(next.Enemies[0].IsAlive);
        var damage = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
        var death = Assert.Single(events, e => e.Kind == BattleEventKind.ActorDeath);
        foreach (var battleEvent in new[] { damage, death })
        {
            Assert.Equal(DamageType.SelfDamage, battleEvent.DamageType);
            Assert.Equal(CombatSourceKind.EnemyMove, battleEvent.Source?.Kind);
            Assert.Equal(2, battleEvent.Source?.OwnerSlot);
            Assert.Equal(enemy.InstanceId, battleEvent.Source?.OwnerInstanceId);
            Assert.Equal(enemy.InstanceId, battleEvent.Source?.ActorInstanceId);
            Assert.Equal(definition.Id, battleEvent.Source?.DefinitionId);
            Assert.Equal("rupture", battleEvent.Source?.MoveId);
            Assert.Equal(0, battleEvent.Source?.EffectIndex);
            Assert.Equal("self", battleEvent.Source?.Scope);
        }
    }

    [Fact]
    public void Summon_non_attack_events_are_owned_by_the_hero_and_keep_effect_source()
    {
        var unit = new UnitDefinition(
            "guardian", "Guardian", "guardian", 10, "fortify",
            new[]
            {
                new MoveDefinition("fortify", MoveKind.Buff,
                    new CardEffect[]
                    {
                        new("buff", EffectScope.Self, null, 1, Name: "strength"),
                        new("block", EffectScope.Self, null, 3),
                    },
                    "fortify"),
            });
        var hero = BattleFixtures.Hero(hp: 70, slotIndex: 4);
        var summon = BattleFixtures.SummonActor(
            "guardian-inst", unit.Id, 7,
            associatedCardId: "guardian-card-inst", moveId: "fortify");
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, summon),
            phase: BattlePhase.PlayerAttacking);

        var (_, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(units: new[] { unit }));

        var status = Assert.Single(events, e => e.Kind == BattleEventKind.ApplyStatus);
        Assert.Equal(CombatSourceKind.SummonMove, status.Source?.Kind);
        Assert.Equal(hero.SlotIndex, status.Source?.OwnerSlot);
        Assert.Equal(hero.InstanceId, status.Source?.OwnerInstanceId);
        Assert.Equal(summon.InstanceId, status.Source?.ActorInstanceId);
        Assert.Equal(unit.Id, status.Source?.DefinitionId);
        Assert.Equal("guardian-card-inst", status.Source?.InstanceId);
        Assert.Equal("fortify", status.Source?.MoveId);
        Assert.Equal(0, status.Source?.EffectIndex);
        Assert.Equal("self", status.Source?.Scope);

        var block = Assert.Single(events, e => e.Kind == BattleEventKind.GainBlock);
        Assert.Equal(CombatSourceKind.SummonMove, block.Source?.Kind);
        Assert.Equal(hero.SlotIndex, block.Source?.OwnerSlot);
        Assert.Equal(1, block.Source?.EffectIndex);
        Assert.Equal("self", block.Source?.Scope);
    }

    [Fact]
    public void Summon_self_damage_and_death_keep_summon_move_and_owner_attribution()
    {
        var unit = new UnitDefinition(
            "ember", "Ember", "ember", 3, "burnout",
            new[]
            {
                new MoveDefinition("burnout", MoveKind.Attack,
                    new[] { new CardEffect("selfDamage", EffectScope.Self, null, 5) },
                    "burnout"),
            });
        var hero = BattleFixtures.Hero(hp: 70, slotIndex: 5);
        var summon = BattleFixtures.SummonActor(
            "ember-inst", unit.Id, 8, hp: 3,
            associatedCardId: "ember-card-inst", moveId: "burnout");
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, summon),
            phase: BattlePhase.PlayerAttacking);

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(units: new[] { unit }));

        Assert.DoesNotContain(next.Allies, a => a.InstanceId == summon.InstanceId);
        var damage = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
        var death = Assert.Single(events, e => e.Kind == BattleEventKind.ActorDeath);
        foreach (var battleEvent in new[] { damage, death })
        {
            Assert.Equal(DamageType.SelfDamage, battleEvent.DamageType);
            Assert.Equal(CombatSourceKind.SummonMove, battleEvent.Source?.Kind);
            Assert.Equal(hero.SlotIndex, battleEvent.Source?.OwnerSlot);
            Assert.Equal(hero.InstanceId, battleEvent.Source?.OwnerInstanceId);
            Assert.Equal(summon.InstanceId, battleEvent.Source?.ActorInstanceId);
            Assert.Equal(unit.Id, battleEvent.Source?.DefinitionId);
            Assert.Equal("ember-card-inst", battleEvent.Source?.InstanceId);
            Assert.Equal("burnout", battleEvent.Source?.MoveId);
            Assert.Equal(0, battleEvent.Source?.EffectIndex);
            Assert.Equal("self", battleEvent.Source?.Scope);
        }
    }

    [Fact]
    public void Enemy_attack_respects_barrier_and_is_reflected_as_non_recursive_direct_damage()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.WithStatus(BattleFixtures.Hero(), "barrier", 1), "reflect", 3) with
        {
            Block = BlockPool.Empty.Add(7),
        };
        var state = BattleFixtures.MinimalState(allies: ImmutableArray.Create(hero)) with
        {
            Phase = BattlePhase.EnemyAttacking,
        };

        var (next, events) = EnemyAttackingResolver.Resolve(
            state, new FakeRng([], []), BattleFixtures.MinimalCatalog());

        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.Equal(1, next.Allies[0].GetStatus("barrier"));
        Assert.Equal(2, next.Allies[0].Block.Sum);
        Assert.Equal(17, next.Enemies[0].CurrentHp);
        Assert.Equal(2, events.Count(e => e.Kind == BattleEventKind.DealDamage));
        Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage
            && e.DamageType == DamageType.Reflected && e.ActualHpLoss == 3);
    }

    [Fact]
    public void Enemy_killed_by_reflect_fires_OnEnemyDeath_once_after_the_hit()
    {
        var relic = BattleFixtures.Relic(
            id: "reflect_kill_reward", trigger: "OnEnemyDeath", implemented: true,
            new CardEffect("buff", EffectScope.Self, null, 2,
                Name: "strength", Trigger: "OnEnemyDeath"));
        var hero = BattleFixtures.WithStatus(BattleFixtures.Hero(), "reflect", 99);
        var enemy = BattleFixtures.Goblin(hp: 5);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(enemy),
            ownedRelicIds: ImmutableArray.Create(relic.Id)) with
        {
            Phase = BattlePhase.EnemyAttacking,
        };

        var (next, events) = EnemyAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(relics: new[] { relic }));

        Assert.False(next.Enemies[0].IsAlive);
        Assert.Equal(2, next.Allies[0].GetStatus("strength"));
        Assert.Single(events, e => e.Kind == BattleEventKind.ApplyStatus
            && e.Note!.Contains("relic:reflect_kill_reward"));
    }

    [Fact]
    public void CardEffectParser_reads_and_validates_chance_percent()
    {
        using var document = JsonDocument.Parse(
            "{\"action\":\"debuff\",\"scope\":\"single\",\"side\":\"enemy\",\"amount\":1," +
            "\"name\":\"weak\",\"trigger\":\"OnHit\",\"chancePercent\":25}");
        var effect = CardEffectParser.ParseEffect(document.RootElement, message => new JsonException(message));
        Assert.Equal(25, effect.ChancePercent);

        using var invalid = JsonDocument.Parse(
            "{\"action\":\"debuff\",\"scope\":\"single\",\"side\":\"enemy\",\"amount\":1," +
            "\"name\":\"weak\",\"chancePercent\":101}");
        Assert.Throws<JsonException>(() =>
            CardEffectParser.ParseEffect(invalid.RootElement, message => new JsonException(message)));
    }

    private static CardDefinition OnHitPower(string id, int chance, int amount) => new(
        id, id, null, CardRarity.Common, CardType.Power, 1, null,
        new[] { new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, amount,
            Name: "weak", Trigger: "OnHit", ChancePercent: chance) },
        null, null);

    private static BattleState PlayerAttackState(CombatActor hero, CombatActor enemy) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero), enemies: ImmutableArray.Create(enemy)) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };
}
