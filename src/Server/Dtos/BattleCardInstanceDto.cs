namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// 戦闘中のカードインスタンス DTO。
/// </summary>
/// <param name="InstanceId">インスタンス ID</param>
/// <param name="CardDefinitionId">カード定義 ID (catalog のキー)</param>
/// <param name="IsUpgraded">強化済みフラグ</param>
/// <param name="CostOverride">コスト上書き (null = 通常コスト)</param>
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
    string? AdjustedDescription = null,
    string? AdjustedUpgradedDescription = null);
