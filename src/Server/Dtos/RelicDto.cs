namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// Phase 10.5.L1.5: Trigger フィールド削除。レリックの発動タイミングは effects[].trigger で
/// per-effect 管理されるため、catalog DTO に冗長に含める必要が無くなった。
///
/// Phase 10.5.M6.3: tooltip で「効果 (上) / 点線 / フレーバー (下、斜体グレー)」の
/// 層別レイアウトを表現するため、EffectText / Flavor を分離して提供。Description は
/// 既存呼出元のため combined ("{auto}\n{flavor}") を残す。
/// </summary>
public sealed record RelicDto(
    string Id,
    string Name,
    string Description,
    string EffectText,
    string Flavor,
    string Rarity);
