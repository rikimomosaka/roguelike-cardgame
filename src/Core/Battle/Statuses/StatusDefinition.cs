using System;
using System.Collections.Generic;
using System.Linq;

namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>
/// 状態異常の静的定義。Phase 10 では JSON 化せず C# の static リストで保持。
/// 親 spec §2-6 / Phase 10.2.B spec §2-2 参照。
/// </summary>
public sealed record StatusDefinition(
    string Id,
    StatusKind Kind,
    bool IsPermanent,
    StatusTickDirection TickDirection)
{
    public static IReadOnlyList<StatusDefinition> All { get; } = new[]
    {
        new StatusDefinition("strength",   StatusKind.Buff,   IsPermanent: true,  StatusTickDirection.None),
        new StatusDefinition("dexterity",  StatusKind.Buff,   IsPermanent: true,  StatusTickDirection.None),
        new StatusDefinition("omnistrike", StatusKind.Buff,   IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("vulnerable", StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("weak",       StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        // Phase 10.5.M6.5: poison は SideStatusCountdown ではなく TurnStartProcessor の
        // ApplyPoisonTick 内でダメージ適用と同時に -1 する (Slay the Spire 慣習)。
        // 旧仕様では countdown が damage より先に走り、N→N-1 されてから N-1 ダメージで
        // 残 N-1、というバグがあった。
        new StatusDefinition("poison",     StatusKind.Debuff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("barrier",    StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("lifesteal",  StatusKind.Buff,   IsPermanent: true,  StatusTickDirection.None),
        new StatusDefinition("reflect",    StatusKind.Buff,   IsPermanent: true,  StatusTickDirection.None),
        // 手札にある間だけカードコストへ動的に作用する。値は変化量であり
        // ターン数ではないため、自動countdownせず明示的なbuff/debuff効果で増減する。
        new StatusDefinition("handCostReduction", StatusKind.Buff,   IsPermanent: true, StatusTickDirection.None),
        new StatusDefinition("handCostIncrease",  StatusKind.Debuff, IsPermanent: true, StatusTickDirection.None),

        new StatusDefinition("vigor",        StatusKind.Buff,   IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("pursuit",      StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("regeneration", StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("fortify",      StatusKind.Buff,   IsPermanent: true,  StatusTickDirection.None),
        new StatusDefinition("resistance",   StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("revive",       StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("growth",       StatusKind.Buff,   IsPermanent: true,  StatusTickDirection.None),
        new StatusDefinition("insight",      StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("haste",        StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("continuity",   StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("mastery",      StatusKind.Buff,   IsPermanent: false, StatusTickDirection.None),

        // Class-mechanic buffs have explicit lifecycle hooks.  They must not use
        // the side countdown because ally countdown runs before EnemyAttacking,
        // where Cover and DefensiveStance still need to be active.
        new StatusDefinition("afterimage",       StatusKind.Buff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("steadfast",        StatusKind.Buff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("cover",            StatusKind.Buff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("defensiveStance",  StatusKind.Buff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("comboCarryover",   StatusKind.Buff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("afterglow",        StatusKind.Buff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("resonance",        StatusKind.Buff, IsPermanent: false, StatusTickDirection.None),

        new StatusDefinition("virulentPoison", StatusKind.Debuff, IsPermanent: true,  StatusTickDirection.None),
        new StatusDefinition("stagnation",     StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("burn",           StatusKind.Debuff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("scorch",         StatusKind.Debuff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("confusion",      StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("curse",          StatusKind.Debuff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("bind",           StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("silence",        StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("seal",           StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("exhaustion",     StatusKind.Debuff, IsPermanent: false, StatusTickDirection.None),
        new StatusDefinition("distress",       StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
        new StatusDefinition("disruption",     StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
    };

    public static StatusDefinition Get(string id) =>
        All.FirstOrDefault(s => s.Id == id)
        ?? throw new InvalidOperationException($"unknown status id '{id}'");
}
