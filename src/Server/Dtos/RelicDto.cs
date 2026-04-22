namespace RoguelikeCardGame.Server.Dtos;

public sealed record RelicDto(
    string Id,
    string Name,
    string Description,
    string Rarity,
    string Trigger);
