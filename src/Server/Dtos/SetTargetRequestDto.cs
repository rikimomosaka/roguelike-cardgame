namespace RoguelikeCardGame.Server.Dtos;

public sealed record SetTargetRequestDto(
    string Side,
    int SlotIndex);
