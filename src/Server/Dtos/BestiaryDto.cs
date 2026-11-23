using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BestiaryDto(
    int SchemaVersion,
    IReadOnlyList<string> DiscoveredCardBaseIds,
    IReadOnlyList<string> DiscoveredRelicIds,
    IReadOnlyList<string> DiscoveredPotionIds,
    IReadOnlyList<string> EncounteredEnemyIds,
    IReadOnlyList<string> AllKnownCardBaseIds,
    IReadOnlyList<string> AllKnownRelicIds,
    IReadOnlyList<string> AllKnownPotionIds,
    IReadOnlyList<string> AllKnownEnemyIds,
    IReadOnlyList<BestiaryCharacterDto> PlayerCharacters,
    IReadOnlyList<BestiaryCharacterDto> AllyCharacters);

/// <summary>
/// 実績画面のキャラクター図鑑用の最小表示情報。
/// Kind は "player" または "ally"。ImageId は専用画像がない場合 null。
/// </summary>
public sealed record BestiaryCharacterDto(
    string Id,
    string Name,
    string Kind,
    int MaxHp,
    string? ImageId,
    int HeightTier,
    int? LifetimeTurns,
    bool IsDiscovered,
    string Description,
    string TraitDescription,
    IReadOnlyList<BestiaryCharacterActionDto> Actions,
    IReadOnlyList<BestiarySummonCardDto> SummonCards,
    string? CharacterClass = null,
    // 能力値: 行動力(エナジー回復量)・知力(ドロー枚数)・攻撃力・守備力。体力は MaxHp。
    int EnergyPerTurn = 0,
    int DrawPerTurn = 0,
    int Attack = 0,
    int Defense = 0,
    // 特性名と書式付き効果文（戦技・奥義と同じくキーワード補足を表示するため）。
    string? TraitName = null,
    string? TraitEffects = null);

public sealed record BestiaryCharacterActionDto(
    string Category,
    string Name,
    string Description,
    int EnergyCost,
    int ComboGaugeCost);

public sealed record BestiarySummonCardDto(
    string Id,
    string Name,
    int Rarity,
    string CardType,
    int? Cost,
    string Description,
    string? UpgradedDescription,
    string? PoolClass = null);
