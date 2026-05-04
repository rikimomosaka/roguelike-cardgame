using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RewardStateDto(
    int Gold, bool GoldClaimed,
    string? PotionId, bool PotionClaimed,
    IReadOnlyList<string> CardChoices,
    string CardStatus,
    string? RelicId,
    bool RelicClaimed,
    bool IsBossReward,
    bool RerollUsed,           // Phase 10.6.B T7
    bool RerollAvailable);     // Phase 10.6.B T7: derived from PassiveCapability check
