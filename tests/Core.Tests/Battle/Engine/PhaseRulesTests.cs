using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class PhaseRulesTests
{
    [Fact]
    public void Parse_KnownNames()
    {
        Assert.Equal(ActorPhase.None, PhaseRules.Parse("none"));
        Assert.Equal(ActorPhase.Calm, PhaseRules.Parse("calm"));
        Assert.Equal(ActorPhase.Motion, PhaseRules.Parse("motion"));
        Assert.Throws<ArgumentException>(() => PhaseRules.Parse("rage"));
    }

    [Fact]
    public void Invert_SwapsCalmAndMotion_AndKeepsNone()
    {
        Assert.Equal(ActorPhase.Motion, PhaseRules.Invert(ActorPhase.Calm));
        Assert.Equal(ActorPhase.Calm, PhaseRules.Invert(ActorPhase.Motion));
        Assert.Equal(ActorPhase.None, PhaseRules.Invert(ActorPhase.None));
    }

    [Fact]
    public void AfterCardUsed_CalmAttack_BecomesNone() =>
        Assert.Equal(ActorPhase.None, PhaseRules.AfterCardUsed(ActorPhase.Calm, CardType.Attack));

    [Fact]
    public void AfterCardUsed_CalmSkill_StaysCalm() =>
        Assert.Equal(ActorPhase.Calm, PhaseRules.AfterCardUsed(ActorPhase.Calm, CardType.Skill));

    [Fact]
    public void AfterCardUsed_MotionSkill_BecomesNone() =>
        Assert.Equal(ActorPhase.None, PhaseRules.AfterCardUsed(ActorPhase.Motion, CardType.Skill));

    [Fact]
    public void AfterCardUsed_MotionPower_BecomesNone() =>
        Assert.Equal(ActorPhase.None, PhaseRules.AfterCardUsed(ActorPhase.Motion, CardType.Power));

    [Fact]
    public void AfterCardUsed_MotionAttack_StaysMotion() =>
        Assert.Equal(ActorPhase.Motion, PhaseRules.AfterCardUsed(ActorPhase.Motion, CardType.Attack));

    [Fact]
    public void AfterCardUsed_None_StaysNone()
    {
        foreach (var type in Enum.GetValues<CardType>())
            Assert.Equal(ActorPhase.None, PhaseRules.AfterCardUsed(ActorPhase.None, type));
    }

    [Fact]
    public void SetPhase_EmitsPhaseChanged_OnlyWhenChanged()
    {
        var state = BattleFixtures.MinimalState();
        var events = new List<BattleEvent>();
        int order = 0;

        state = PhaseRules.SetPhase(state, "hero_inst", ActorPhase.Calm, "effect", ref order, events);
        Assert.Equal(ActorPhase.Calm, state.Allies[0].Phase);
        var changed = Assert.Single(events);
        Assert.Equal(BattleEventKind.PhaseChanged, changed.Kind);
        Assert.Equal("hero_inst", changed.TargetInstanceId);
        Assert.Equal("none->calm:effect", changed.Note);

        state = PhaseRules.SetPhase(state, "hero_inst", ActorPhase.Calm, "effect", ref order, events);
        Assert.Single(events);
    }

    [Fact]
    public void MarkExitOnUse_DefersRemovalUntilResolve()
    {
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = ActorPhase.Motion }));
        var events = new List<BattleEvent>();
        int order = 0;

        state = PhaseRules.MarkExitOnUse(state, "hero_inst", CardType.Skill);
        Assert.Equal(ActorPhase.Motion, state.Allies[0].Phase);
        Assert.True(state.Allies[0].PhaseExitPending);

        state = PhaseRules.ResolvePendingExit(state, "hero_inst", ref order, events);
        Assert.Equal(ActorPhase.None, state.Allies[0].Phase);
        Assert.False(state.Allies[0].PhaseExitPending);
        Assert.Equal("motion->none:used", Assert.Single(events).Note);
    }

    [Fact]
    public void SetPhaseByEffect_ClearsPendingExit_EvenWithTheSamePhase()
    {
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero() with { Phase = ActorPhase.Calm }));
        var events = new List<BattleEvent>();
        int order = 0;

        state = PhaseRules.MarkExitOnUse(state, "hero_inst", CardType.Attack);
        state = PhaseRules.SetPhase(state, "hero_inst", ActorPhase.Calm, "effect", ref order, events);
        state = PhaseRules.ResolvePendingExit(state, "hero_inst", ref order, events);

        Assert.Equal(ActorPhase.Calm, state.Allies[0].Phase);
        Assert.Empty(events);
    }

    [Fact]
    public void SetPhase_FindsEnemyByInstanceId()
    {
        var state = BattleFixtures.MinimalState();
        var events = new List<BattleEvent>();
        int order = 0;

        state = PhaseRules.SetPhase(state, "goblin_inst_0", ActorPhase.Motion, "effect", ref order, events);

        Assert.Equal(ActorPhase.Motion, state.Enemies[0].Phase);
        Assert.Equal(ActorPhase.None, state.Allies[0].Phase);
    }

    [Fact]
    public void LegacyActorJson_WithoutPhase_DeserializesAsNone()
    {
        var actor = BattleFixtures.Hero() with { Phase = ActorPhase.Calm };
        var node = JsonNode.Parse(JsonSerializer.Serialize(actor))!.AsObject();
        Assert.True(node.Remove("Phase"));

        var restored = JsonSerializer.Deserialize<CombatActor>(node.ToJsonString())!;

        Assert.Equal(ActorPhase.None, restored.Phase);
    }
}
