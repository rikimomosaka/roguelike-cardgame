using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RewardStateDto(
    int Gold, bool GoldClaimed,
    string? PotionId, bool PotionClaimed,
    IReadOnlyList<string> CardChoices,
    string CardStatus);
