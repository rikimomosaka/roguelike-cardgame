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
        new StatusDefinition("poison",     StatusKind.Debuff, IsPermanent: false, StatusTickDirection.Decrement),
    };

    public static StatusDefinition Get(string id) =>
        All.FirstOrDefault(s => s.Id == id)
        ?? throw new InvalidOperationException($"unknown status id '{id}'");
}
