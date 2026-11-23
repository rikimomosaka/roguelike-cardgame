namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// 戦闘中のカードインスタンス DTO。
/// </summary>
/// <param name="InstanceId">インスタンス ID</param>
/// <param name="CardDefinitionId">カード定義 ID (catalog のキー)</param>
/// <param name="IsUpgraded">強化済みフラグ</param>
/// <param name="CostOverride">
/// 旧Client互換用のコンボ軽減前コスト。新ClientはOriginalCost/CurrentCostを使用する。
/// </param>
/// <param name="OriginalCost">コンボ連鎖が参照する、強化状態反映後の定義コスト。</param>
/// <param name="CurrentCost">Coreが全補正を適用した、実際に支払う現在コスト。</param>
/// <param name="AdjustedDescription">
/// 10.5.C: hero (caster) の statuses (strength/weak/dexterity) を反映した
/// description。null の場合は catalog の auto/manual description にフォールバック。
/// 戦闘外 (catalog endpoint 等) では null。
/// </param>
/// <param name="AdjustedUpgradedDescription">
/// 10.5.C: 強化版の context 反映後 description。
/// IsUpgradable でない、または context 不要なら null。
/// </param>
public sealed record BattleCardInstanceDto(
    string InstanceId,
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride,
    int? OriginalCost,
    int? CurrentCost,
    string? AdjustedDescription = null,
    string? AdjustedUpgradedDescription = null,
    bool ComboReductionEligible = false,
    bool ComboContinues = false,
    bool ComboRingHighlighted = false,
    bool WildDisabled = false,
    bool CardUsageLimitReached = false,
    bool CardPlayRestricted = false,
    string? CardPlayRestrictionReason = null,
    IReadOnlyList<CardCostChangeDto>? CostChanges = null,
    string? PoolClass = null,
    bool IsContingencyArmed = false,
    string? ContingencyTriggerText = null);

/// <summary>
/// Ordered, authoritative step in the payable-cost calculation.
/// Fixed steps use Value as the replacement cost; other steps use a signed delta.
/// </summary>
public sealed record CardCostChangeDto(
    string Kind,
    long Value,
    string? SourceId,
    string? SourceName);
