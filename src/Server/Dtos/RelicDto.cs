namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// Phase 10.5.L1.5: Trigger フィールド削除。レリックの発動タイミングは effects[].trigger で
/// per-effect 管理されるため、catalog DTO に冗長に含める必要が無くなった。
/// </summary>
public sealed record RelicDto(
    string Id,
    string Name,
    string Description,
    string Rarity);
