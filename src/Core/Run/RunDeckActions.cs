using System;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Run;

/// <summary>
/// RunState のデッキ操作を集約するヘルパ。Phase 10.6.A で OnCardAddedToDeck トリガー集約点として導入。
/// MerchantActions.BuyCard / RewardApplier.PickCard など全カード追加経路はこのメソッド経由にする。
/// </summary>
public static class RunDeckActions
{
    /// <summary>
    /// カードをデッキ末尾に追加し、OnCardAddedToDeck トリガーを持つレリック効果を発火する。
    /// unknown な cardId は ArgumentException。
    /// </summary>
    public static RunState AddCardToDeck(RunState s, string cardId, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cardId);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.TryGetCard(cardId, out _))
            throw new ArgumentException($"unknown card id \"{cardId}\"", nameof(cardId));
        var s1 = s with { Deck = s.Deck.Add(new CardInstance(cardId, false)) };
        return NonBattleRelicEffects.ApplyOnCardAddedToDeck(s1, catalog);
    }
}
