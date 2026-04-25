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
/// <param name="CostOverride">戦闘内一時上書き（10.2.A では未使用、後続 phase で利用）</param>
public sealed record BattleCardInstance(
    string InstanceId,
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride);
