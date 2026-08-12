using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>Damage resolution category. Only <see cref="Attack"/> uses Block and attack modifiers.</summary>
public enum DamageType
{
    Attack = 0,
    Poison = 1,
    DirectHpLoss = 2,
    SelfDamage = 3,
    Reflected = 4,
    Environment = 5,
}

public enum CombatSourceKind
{
    Unattributed = 0,
    CardEffect = 1,
    EnemyMove = 2,
    SummonMove = 3,
    PowerEffect = 4,
    RelicEffect = 5,
    PotionEffect = 6,
    Status = 7,
    CharacterSkill = 8,
}

/// <summary>
/// Stable attribution for a combat action. IDs come from battle state or content data;
/// no random GUID is generated while resolving a cause chain.
/// </summary>
public sealed record CombatSource(
    CombatSourceKind Kind,
    int? OwnerSlot,
    string? OwnerInstanceId,
    string? ActorInstanceId,
    string? DefinitionId = null,
    string? InstanceId = null,
    string? MoveId = null,
    int? EffectIndex = null,
    string? Scope = null)
{
    public static CombatSource Unattributed { get; } =
        new(CombatSourceKind.Unattributed, null, null, null);
}

/// <summary>A deterministic parent-to-child damage chain frame.</summary>
public sealed record DamageCauseFrame(
    int Depth,
    DamageType DamageType,
    CombatSource Source,
    int? HitIndex,
    string Cause);

/// <summary>Immutable cause frames with structural equality for deterministic event comparison.</summary>
public readonly record struct DamageCauseChain
{
    private readonly ImmutableArray<DamageCauseFrame> _frames;

    public DamageCauseChain(ImmutableArray<DamageCauseFrame> frames) =>
        _frames = frames.IsDefault ? ImmutableArray<DamageCauseFrame>.Empty : frames;

    public static DamageCauseChain Empty => default;
    public int Length => _frames.IsDefault ? 0 : _frames.Length;
    public DamageCauseFrame this[int index] => Frames[index];
    public ImmutableArray<DamageCauseFrame> Frames =>
        _frames.IsDefault ? ImmutableArray<DamageCauseFrame>.Empty : _frames;
    public DamageCauseChain Add(DamageCauseFrame frame) => new(Frames.Add(frame));

    public bool Equals(DamageCauseChain other) =>
        Frames.AsSpan().SequenceEqual(other.Frames.AsSpan());

    public override int GetHashCode()
    {
        var hash = new System.HashCode();
        foreach (var frame in Frames) hash.Add(frame);
        return hash.ToHashCode();
    }
}

/// <summary>Per-target, per-hit resolution context.</summary>
public sealed record HitContext(
    DamageType DamageType,
    CombatSource Source,
    DamageCauseChain CauseChain,
    int? HitIndex = null)
{
    public static HitContext Attack(CombatSource source, int? hitIndex)
    {
        var frame = new DamageCauseFrame(0, DamageType.Attack, source, hitIndex, "attack");
        return new HitContext(DamageType.Attack, source,
            new DamageCauseChain(ImmutableArray.Create(frame)), hitIndex);
    }

    public HitContext CreateChild(DamageType damageType, CombatSource source, string cause)
    {
        var frame = new DamageCauseFrame(CauseChain.Length, damageType, source, HitIndex, cause);
        return new HitContext(damageType, source, CauseChain.Add(frame), HitIndex);
    }
}
