using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization;
using RoguelikeCardGame.Core.Battle.Events;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// ターン中に蓄積した攻撃を、追加された順のヒット列として保持する。
/// <see cref="Sum"/> と <see cref="AddCount"/> は既存APIとの互換用の派生値であり、
/// 実際のダメージ解決には <see cref="HitAmounts"/> を使用する。
/// </summary>
public readonly record struct AttackPool
{
    private readonly ImmutableArray<AttackHit> _hits;

    [JsonConstructor]
    public AttackPool(ImmutableArray<int> hitAmounts, ImmutableArray<AttackHit> hits = default)
    {
        if (hitAmounts.IsDefault && hits.IsDefault)
            throw new ArgumentException(
                "AttackPool requires an explicit hitAmounts or hits array. " +
                "Legacy Sum/AddCount data cannot restore per-hit values.",
                nameof(hitAmounts));
        _hits = !hits.IsDefault
            ? hits
            : hitAmounts.Select(AttackHit.Unattributed).ToImmutableArray();
    }

    private AttackPool(ImmutableArray<AttackHit> hits) => _hits = hits;

    /// <summary>Ordered attributed hits. Serialized together with legacy HitAmounts.</summary>
    public ImmutableArray<AttackHit> Hits =>
        _hits.IsDefault ? ImmutableArray<AttackHit>.Empty : _hits;

    /// <summary>追加された順に並んだ、補正前の各ヒット威力。</summary>
    public ImmutableArray<int> HitAmounts =>
        Hits.Select(hit => hit.Amount).ToImmutableArray();

    public int Sum
    {
        get
        {
            int sum = 0;
            foreach (int amount in HitAmounts)
                sum = unchecked(sum + amount);
            return sum;
        }
    }

    public int AddCount => HitAmounts.Length;

    // default(AttackPool) も正規の空プールとして扱える。
    public static AttackPool Empty => default;

    public AttackPool Add(int amount) => Add(AttackHit.Unattributed(amount));

    public AttackPool Add(int amount, CombatSource source) =>
        Add(new AttackHit(amount, source, DamageCauseChain.Empty));

    public AttackPool Add(AttackHit hit) => new(Hits.Add(hit));

    /// <summary>
    /// Keeps the first <paramref name="count"/> hits in their original order.
    /// Used when an immediate trigger temporarily appends attacks to a pool and
    /// those appended hits are consumed during the current hit resolution.
    /// </summary>
    public AttackPool TakeFirst(int count)
    {
        if (count < 0 || count > Hits.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count == Hits.Length) return this;
        if (count == 0) return Empty;
        return new AttackPool(Hits.Take(count).ToImmutableArray());
    }

    /// <summary>左側のヒット列の後ろへ、右側のヒット列を順序を保って連結する。</summary>
    public static AttackPool operator +(AttackPool a, AttackPool b)
    {
        if (a.AddCount == 0) return b;
        if (b.AddCount == 0) return a;
        return new AttackPool(a.Hits.AddRange(b.Hits));
    }

    /// <summary>
    /// 各ヒットへ個別に Strength と Weak を適用し、その表示値を合計する。
    /// Weak の端数処理とダメージの0下限もヒットごとに行う。
    /// </summary>
    public int Display(int strength, int weak)
    {
        long total = 0;
        foreach (int amount in HitAmounts)
        {
            long adjusted = (long)amount + strength;
            if (weak > 0)
                adjusted = adjusted * 3 / 4;
            if (adjusted > 0)
                total += adjusted;
        }

        return unchecked((int)total);
    }

    /// <summary>旧API互換。補正前の全ヒット合計。</summary>
    internal int RawTotal => Sum;

    /// <summary>旧 positional record の分解APIとの互換。</summary>
    public void Deconstruct(out int sum, out int addCount)
    {
        sum = Sum;
        addCount = AddCount;
    }

    public bool Equals(AttackPool other)
    {
        if (Hits.Length != other.Hits.Length) return false;
        for (int i = 0; i < Hits.Length; i++)
        {
            var left = Hits[i];
            var right = other.Hits[i];
            if (left.Amount != right.Amount || left.Source != right.Source) return false;
            if (left.CauseChain != right.CauseChain) return false;
            if (left.TargetInstanceId != right.TargetInstanceId) return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var hit in Hits)
        {
            hash.Add(hit.Amount);
            hash.Add(hit.Source);
            hash.Add(hit.TargetInstanceId);
            foreach (var cause in hit.CauseChain.Frames)
                hash.Add(cause);
        }
        return hash.ToHashCode();
    }
}
