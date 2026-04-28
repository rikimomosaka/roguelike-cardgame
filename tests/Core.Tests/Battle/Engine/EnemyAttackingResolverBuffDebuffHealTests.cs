using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// 敵 Move の buff / debuff / heal effect を EnemyAttackingResolver が
/// 実体化することを検証する。intent 表示と実挙動の乖離防止が目的。
///
/// JSON 規約: side は caster 視点（CardEffect.Side comment 準拠）。
/// 敵 caster の "side: enemy" = state.Allies、"side: ally" = state.Enemies。
/// </summary>
public class EnemyAttackingResolverBuffDebuffHealTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.EnemyAttacking,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    private static BattleState StateMultiAlly(CombatActor[] allies, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.EnemyAttacking,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: allies.ToImmutableArray(),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    private static EnemyDefinition EnemyWithMove(
        string id, string moveId, MoveKind kind, params CardEffect[] effects) =>
        new(id, id, $"img_{id}", 30, new EnemyPool(1, EnemyTier.Weak),
            moveId,
            new[] {
                new MoveDefinition(moveId, kind, effects, moveId)
            });

    private static CombatActor MakeEnemy(string id, int slot, int hp, string moveId) =>
        new($"{id}_inst_{slot}", id, ActorSide.Enemy, slot, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, moveId,
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);

    // ========== buff scope=self ==========

    [Fact]
    public void Enemy_buff_self_strength_boosts_casting_enemy()
    {
        // dire_wolf "howl": { action: buff, scope: self, name: strength, amount: 2 }
        var hero = BattleFixtures.Hero(70);
        var wolf = MakeEnemy("dire_wolf", 0, 40, "howl");
        var def = EnemyWithMove("dire_wolf", "howl", MoveKind.Buff,
            new CardEffect("buff", EffectScope.Self, null, 2, Name: "strength"));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });

        var (next, events) = EnemyAttackingResolver.Resolve(State(hero, wolf), Rng(), catalog);

        var afterWolf = next.Enemies.First(e => e.InstanceId == wolf.InstanceId);
        Assert.Equal(2, afterWolf.GetStatus("strength"));
        Assert.Contains(events, e => e.Kind == BattleEventKind.ApplyStatus
            && e.TargetInstanceId == wolf.InstanceId
            && e.Note == "strength"
            && e.Amount == 2);
    }

    [Fact]
    public void Enemy_buff_self_does_not_affect_other_enemies()
    {
        // 複数敵: 1 体目だけ buff move → 他の敵には strength が付かない
        var hero = BattleFixtures.Hero(70);
        var wolf = MakeEnemy("dire_wolf", 0, 40, "howl");
        // 2 体目はダミー attack だけ持つ
        var goblin = MakeEnemy("goblin", 1, 20, "swing");

        var wolfDef = EnemyWithMove("dire_wolf", "howl", MoveKind.Buff,
            new CardEffect("buff", EffectScope.Self, null, 3, Name: "strength"));
        var goblinDef = EnemyWithMove("goblin", "swing", MoveKind.Attack,
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 1));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { wolfDef, goblinDef });

        var (next, _) = EnemyAttackingResolver.Resolve(State(hero, wolf, goblin), Rng(), catalog);

        Assert.Equal(3, next.Enemies.First(e => e.InstanceId == wolf.InstanceId).GetStatus("strength"));
        Assert.Equal(0, next.Enemies.First(e => e.InstanceId == goblin.InstanceId).GetStatus("strength"));
    }

    // ========== debuff scope=all side=enemy (caster perspective) ==========

    [Fact]
    public void Enemy_debuff_all_side_enemy_targets_all_allies()
    {
        // cave_bat_a "screech": { action: debuff, scope: all, side: enemy, name: weak, amount: 1 }
        // caster は敵なので "side: enemy" = caster の敵 = state.Allies に着弾
        var hero = BattleFixtures.Hero(70);
        var bat = MakeEnemy("cave_bat", 0, 12, "screech");
        var def = EnemyWithMove("cave_bat", "screech", MoveKind.Debuff,
            new CardEffect("debuff", EffectScope.All, EffectSide.Enemy, 1, Name: "weak"));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });

        var (next, events) = EnemyAttackingResolver.Resolve(State(hero, bat), Rng(), catalog);

        Assert.Equal(1, next.Allies[0].GetStatus("weak"));
        Assert.Contains(events, e => e.Kind == BattleEventKind.ApplyStatus
            && e.TargetInstanceId == hero.InstanceId
            && e.Note == "weak");
    }

    [Fact]
    public void Enemy_debuff_all_does_not_hit_caster_or_other_enemies()
    {
        // 敵 "side: enemy" は caster 視点で「敵 = state.Allies」。
        // 自分や他の敵 (state.Enemies) には付与されない。
        var hero = BattleFixtures.Hero(70);
        var bat1 = MakeEnemy("cave_bat", 0, 12, "screech");
        var bat2 = MakeEnemy("cave_bat", 1, 12, "screech");
        var def = EnemyWithMove("cave_bat", "screech", MoveKind.Debuff,
            new CardEffect("debuff", EffectScope.All, EffectSide.Enemy, 2, Name: "vulnerable"));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });

        var (next, _) = EnemyAttackingResolver.Resolve(State(hero, bat1, bat2), Rng(), catalog);

        // hero に重ね掛け: 各 bat が +2 → 計 4
        Assert.Equal(4, next.Allies[0].GetStatus("vulnerable"));
        // 敵自身には付かない
        foreach (var e in next.Enemies)
            Assert.Equal(0, e.GetStatus("vulnerable"));
    }

    [Fact]
    public void Enemy_debuff_all_targets_all_living_allies_when_party()
    {
        // 召喚 ally がいるパーティで全員に weak が付く
        var hero = BattleFixtures.Hero(70);
        var summon = BattleFixtures.SummonActor("wisp_inst_1", "wisp", 1, hp: 10);
        var bat = MakeEnemy("cave_bat", 0, 12, "screech");
        var def = EnemyWithMove("cave_bat", "screech", MoveKind.Debuff,
            new CardEffect("debuff", EffectScope.All, EffectSide.Enemy, 1, Name: "weak"));
        var unitDef = BattleFixtures.MinionDef("wisp", hp: 10);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def }, units: new[] { unitDef });

        var (next, _) = EnemyAttackingResolver.Resolve(
            StateMultiAlly(new[] { hero, summon }, bat), Rng(), catalog);

        Assert.Equal(1, next.Allies.First(a => a.InstanceId == hero.InstanceId).GetStatus("weak"));
        Assert.Equal(1, next.Allies.First(a => a.InstanceId == summon.InstanceId).GetStatus("weak"));
    }

    // ========== heal scope=self ==========

    [Fact]
    public void Enemy_heal_self_increases_caster_hp_capped_at_maxhp()
    {
        // 仮想 enemy: heal scope=self amount=8
        // 現状のデータには無いが、将来用 + intent 表示との一致のため対応必要
        var hero = BattleFixtures.Hero(70);
        var injuredEnemy = MakeEnemy("priest", 0, 30, "mend") with { CurrentHp = 5 };
        var def = EnemyWithMove("priest", "mend", MoveKind.Buff,
            new CardEffect("heal", EffectScope.Self, null, 8));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });

        var (next, events) = EnemyAttackingResolver.Resolve(State(hero, injuredEnemy), Rng(), catalog);

        var after = next.Enemies.First(e => e.InstanceId == injuredEnemy.InstanceId);
        Assert.Equal(13, after.CurrentHp);  // 5 + 8
        Assert.Contains(events, e => e.Kind == BattleEventKind.Heal
            && e.TargetInstanceId == injuredEnemy.InstanceId
            && e.Amount == 8);
    }

    [Fact]
    public void Enemy_heal_self_caps_at_maxhp()
    {
        // 既に MaxHp 近くなら overshot しない
        var hero = BattleFixtures.Hero(70);
        var enemy = MakeEnemy("priest", 0, 30, "mend") with { CurrentHp = 28 };
        var def = EnemyWithMove("priest", "mend", MoveKind.Buff,
            new CardEffect("heal", EffectScope.Self, null, 100));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });

        var (next, events) = EnemyAttackingResolver.Resolve(State(hero, enemy), Rng(), catalog);

        var after = next.Enemies.First(e => e.InstanceId == enemy.InstanceId);
        Assert.Equal(30, after.CurrentHp);  // capped at MaxHp
        Assert.Contains(events, e => e.Kind == BattleEventKind.Heal
            && e.Amount == 2);   // actualHeal = 2 (30-28)
    }

    // ========== heal scope=all side=ally (heal own party — caster perspective) ==========

    [Fact]
    public void Enemy_heal_all_side_ally_targets_all_alive_enemies()
    {
        // 仮想: heal scope=all side=ally → caster 視点 "ally" = state.Enemies (敵の仲間)
        // 全敵が回復する
        var hero = BattleFixtures.Hero(70);
        var healer = MakeEnemy("priest", 0, 30, "groupHeal") with { CurrentHp = 10 };
        var ally = MakeEnemy("goblin", 1, 20, "wait") with { CurrentHp = 5 };

        var healerDef = EnemyWithMove("priest", "groupHeal", MoveKind.Buff,
            new CardEffect("heal", EffectScope.All, EffectSide.Ally, 5));
        var goblinDef = EnemyWithMove("goblin", "wait", MoveKind.Defend,
            new CardEffect("block", EffectScope.Self, null, 0));
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { healerDef, goblinDef });

        var (next, _) = EnemyAttackingResolver.Resolve(State(hero, healer, ally), Rng(), catalog);

        Assert.Equal(15, next.Enemies.First(e => e.InstanceId == healer.InstanceId).CurrentHp);
        Assert.Equal(10, next.Enemies.First(e => e.InstanceId == ally.InstanceId).CurrentHp);
        // hero (= caster の敵) は回復しない
        Assert.Equal(70, next.Allies[0].CurrentHp);
    }
}
