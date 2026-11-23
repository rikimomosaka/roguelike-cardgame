using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中のパイルカード instance。
/// `Cards.CardInstance`（RunState.Deck 用、Id+Upgraded のみ）とは別物。
/// バトル開始時に `Cards.CardInstance` から生成され、戦闘終了で破棄される。
/// 親 spec §3-4 参照。
/// </summary>
/// <param name="InstanceId">バトル中の一意 ID（重複カード識別用）</param>
/// <param name="CardDefinitionId">マスター定義 ID</param>
/// <param name="IsUpgraded">強化済みかどうか</param>
/// <param name="CostOverride">旧形式の絶対コスト上書き。新規効果は CostModifiers を使う。</param>
/// <param name="CostModifiers">領域を移動しても同じカード個体に追従する加算式コスト変更。</param>
public sealed record BattleCardInstance(
    string InstanceId,
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride,
    ImmutableArray<CardCostModifier>? CostModifiers = null)
{
    /// <summary>
    /// Battle-only affiliation captured for a dynamically generated card.
    /// Null means the ordinary catalog definition remains authoritative.
    /// </summary>
    public CardPoolClass? PoolClassOverride { get; init; }

    /// <summary>旧state/fixtureの null を安全に空配列として扱う。</summary>
    public ImmutableArray<CardCostModifier> EffectiveCostModifiers =>
        CostModifiers is { } modifiers && !modifiers.IsDefault
            ? modifiers
            : ImmutableArray<CardCostModifier>.Empty;

    /// <summary>
    /// The visual/content pool carried by this concrete battle card. Cards
    /// created by another card keep the creating card's pool through this
    /// override; ordinary cards fall back to their catalog definition. Legacy
    /// or unknown definitions are deliberately presented as Universal.
    /// </summary>
    public CardPoolClass EffectivePoolClass(CardDefinition? definition) =>
        PoolClassOverride
        ?? definition?.PoolClass
        ?? CardPoolClass.Universal;

    public BattleCardInstance AddCostModifier(CardCostModifier modifier)
    {
        if (modifier.Amount == 0) return this;
        return this with { CostModifiers = EffectiveCostModifiers.Add(modifier) };
    }

    /// <summary>プレイが受理されたカードから、カード個体に付いたコスト変更を全て消す。</summary>
    public BattleCardInstance ConsumeCostChangesOnPlay() => this with
    {
        CostOverride = null,
        CostModifiers = ImmutableArray<CardCostModifier>.Empty,
    };

    /// <summary>指定期限のmodifierだけを取り除く。カードの領域は問わない。</summary>
    public BattleCardInstance RemoveCostModifiers(CardCostModifierDuration duration)
    {
        if (EffectiveCostModifiers.IsEmpty) return this;
        return this with
        {
            CostModifiers = EffectiveCostModifiers
                .Where(modifier => modifier.Duration != duration)
                .ToImmutableArray(),
        };
    }

    public bool Equals(BattleCardInstance? other) =>
        other is not null
        && InstanceId == other.InstanceId
        && CardDefinitionId == other.CardDefinitionId
        && IsUpgraded == other.IsUpgraded
        && CostOverride == other.CostOverride
        && PoolClassOverride == other.PoolClassOverride
        && EffectiveCostModifiers.SequenceEqual(other.EffectiveCostModifiers);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(InstanceId, StringComparer.Ordinal);
        hash.Add(CardDefinitionId, StringComparer.Ordinal);
        hash.Add(IsUpgraded);
        hash.Add(CostOverride);
        hash.Add(PoolClassOverride);
        foreach (var modifier in EffectiveCostModifiers)
            hash.Add(modifier);
        return hash.ToHashCode();
    }
}
