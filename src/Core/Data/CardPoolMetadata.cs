using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Data;

/// <summary>プレイアブルキャラクターが属する、固定のクラス分類。</summary>
public enum CharacterClass
{
    Striker,
    Guardian,
    Tactician,
    Enhancer,
}

/// <summary>
/// カードを提示するプール。Universal はショップ・イベント用の共有プールであり、
/// プレイアブルキャラクターのクラスではない。
/// </summary>
public enum CardPoolClass
{
    Universal,
    Striker,
    Guardian,
    Tactician,
    Enhancer,
}

/// <summary>
/// カードの用途分類。Basic と Reward は排他的ではなく、明示的な例外カードは両方を持てる。
/// どちらも持たないカードはメイト固有カードや生成カードなど、通常プール外として扱う。
/// </summary>
public enum CardPoolKind
{
    Basic,
    Reward,
}

/// <summary>カード取得経路が共有する、クラス・キャラクター制限の権威ルール。</summary>
public static class CardPoolRules
{
    public static CardPoolClass ToCardPoolClass(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Striker => CardPoolClass.Striker,
        CharacterClass.Guardian => CardPoolClass.Guardian,
        CharacterClass.Tactician => CardPoolClass.Tactician,
        CharacterClass.Enhancer => CardPoolClass.Enhancer,
        _ => throw new ArgumentOutOfRangeException(nameof(characterClass)),
    };

    public static bool IsBasic(CardDefinition card) =>
        card.EffectivePoolKinds.Contains(CardPoolKind.Basic);

    public static bool IsReward(CardDefinition card) =>
        card.EffectivePoolKinds.Contains(CardPoolKind.Reward);

    public static bool IsEligibleForCharacter(
        CardDefinition card,
        CharacterDefinition character)
    {
        var eligibleIds = card.EffectiveEligibleCharacterIds;
        return eligibleIds.Count == 0
            || eligibleIds.Contains(character.Id, StringComparer.Ordinal);
    }

    public static bool IsClassRewardFor(
        CardDefinition card,
        CharacterDefinition character) =>
        IsReward(card)
        && card.PoolClass == ToCardPoolClass(character.CharacterClass)
        && IsEligibleForCharacter(card, character);

    public static bool IsUniversalRewardFor(
        CardDefinition card,
        CharacterDefinition character) =>
        IsReward(card)
        && card.PoolClass == CardPoolClass.Universal
        && IsEligibleForCharacter(card, character);
}
