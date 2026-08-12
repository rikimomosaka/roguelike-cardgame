using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class BattleControllerRandomDiscardTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleControllerRandomDiscardTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task PlayCard_random_discard_selecting_playing_card_returns_200_and_resolves_all_effects()
    {
        const string cardDefinitionId = "test_defensive_strike_random_discard";
        const string playedInstanceId = "played-defensive-strike";
        const string discardTargetInstanceId = "discard-target";

        var (client, accountId) =
            await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            var startResponse =
                await client.PostAsync("/api/v1/runs/current/battle/start", null);
            Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            var mutableCards = Assert.IsAssignableFrom<IDictionary<string, CardDefinition>>(
                catalog.Cards);
            mutableCards[cardDefinitionId] = new CardDefinition(
                Id: cardDefinitionId,
                Name: "防御撃（ランダム捨札テスト）",
                DisplayName: null,
                Rarity: CardRarity.Common,
                CardType: CardType.Attack,
                Cost: 1,
                UpgradedCost: null,
                Effects: new[]
                {
                    new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5),
                    new CardEffect("block", EffectScope.Self, null, 4),
                    new CardEffect(
                        "discard", EffectScope.Single, EffectSide.Enemy, 1,
                        Select: "random"),
                },
                UpgradedEffects: null,
                Keywords: null);

            var sessions = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(sessions.TryGet(accountId, out var session));

            var playedCard = new BattleCardInstance(
                playedInstanceId, cardDefinitionId, IsUpgraded: false, CostOverride: null);
            var discardTarget = new BattleCardInstance(
                discardTargetInstanceId, "defend", IsUpgraded: false, CostOverride: null);
            var hero = session.State.Allies[0] with
            {
                Block = BlockPool.Empty,
                AttackSingle = AttackPool.Empty,
                AttackRandom = AttackPool.Empty,
                AttackAll = AttackPool.Empty,
            };
            var state = session.State with
            {
                Allies = session.State.Allies.SetItem(0, hero),
                Energy = 3,
                Hand = ImmutableArray.Create(playedCard, discardTarget),
                DiscardPile = ImmutableArray<BattleCardInstance>.Empty,
                ExhaustPile = ImmutableArray<BattleCardInstance>.Empty,
                PowerCards = ImmutableArray<BattleCardInstance>.Empty,
                OwnedRelicIds = ImmutableArray<string>.Empty,
                ComboCount = 0,
                LastPlayedOrigCost = null,
                NextCardComboFreePass = false,
                PendingCardPlay = null,
            };

            // 旧実装では Hand index 0 の使用中カード自身が捨札抽選に当たり、
            // FinalizeCardPlay が HTTP 400 になった条件を固定する。
            sessions.Set(accountId, new BattleSession(
                state,
                new FakeRng(new[] { 0 }, System.Array.Empty<double>())));

            var response = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/play-card",
                new PlayCardRequestDto(HandIndex: 0, TargetEnemyIndex: null, TargetAllyIndex: null));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);

            Assert.Equal(2, body!.State.Energy);
            Assert.Empty(body.State.Hand);
            Assert.Equal(5, body.State.Allies[0].AttackSingleDisplay);
            Assert.Equal(4, body.State.Allies[0].BlockDisplay);
            Assert.Equal(
                new[] { discardTargetInstanceId, playedInstanceId },
                body.State.DiscardPile.Select(card => card.InstanceId));
            Assert.Contains(body.Steps, step =>
                step.Event.Kind == "Discard" && step.Event.Amount == 1);
            Assert.Contains(body.Steps, step =>
                step.Event.Kind == "PlayCard" && step.Event.CardId == cardDefinitionId);
        }
        finally
        {
            client.Dispose();
        }
    }
}
