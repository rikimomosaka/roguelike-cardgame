using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Controllers;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

/// <summary>位相と、後付け能力（双極）入りの説明文を DTO に載せる。</summary>
public class BattleStateDtoMapperPhaseTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleStateDtoMapperPhaseTests(TempDataFactory factory) => _factory = factory;

    /// <summary>戦闘を開始し、主人公の位相と手札 1 枚目の双極を差し替えた state を返す。</summary>
    private async Task<(BattleState State, DataCatalog Catalog, string CardId)> CalmHeroWithBipolarAsync(
        string accountId, ActorPhase phase, int bipolar)
    {
        var (client, account) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory, accountId: accountId);
        await client.PostAsync("/api/v1/runs/current/battle/start", null);
        var store = _factory.Services.GetRequiredService<BattleSessionStore>();
        var catalog = _factory.Services.GetRequiredService<DataCatalog>();
        Assert.True(store.TryGet(account, out var session));
        var participant = BattleParticipantAccess.GetPrimary(session.State);
        var card = bipolar > 0
            ? participant.Hand.First().AddKeyword(CardKeywords.Bipolar, bipolar)
            : participant.Hand.First();
        participant = participant with { Hand = participant.Hand.SetItem(0, card) };
        var state = BattleParticipantAccess.Replace(session.State, participant);
        int heroIndex = state.Allies.IndexOf(state.Allies.First(a => a.InstanceId == participant.ActorInstanceId));
        state = state with { Allies = state.Allies.SetItem(heroIndex, state.Allies[heroIndex] with { Phase = phase }) };
        return (BattleParticipantAccess.ProjectToRoot(state, participant.ParticipantId), catalog, card.InstanceId);
    }

    [Fact]
    public async Task ActorDto_HasPhase()
    {
        var (state, catalog, _) = await CalmHeroWithBipolarAsync("dto-phase-actor", ActorPhase.Calm, 1);

        var dto = BattleStateDtoMapper.ToDto(state, catalog);

        Assert.Equal("calm", dto.Allies[0].Phase);
        Assert.All(dto.Enemies, enemy => Assert.Equal("none", enemy.Phase));
    }

    [Fact]
    public async Task HandCard_WithBipolar2_DescriptionHasCountedKeyword()
    {
        var (state, catalog, cardId) = await CalmHeroWithBipolarAsync("dto-phase-bipolar2", ActorPhase.None, 2);

        var dto = BattleStateDtoMapper.ToDto(state, catalog);

        var card = dto.Hand.Single(c => c.InstanceId == cardId);
        // 後付け（エンハンス）のキーワードは "+" 付きで、数があれば続ける。
        // 双極は上限 1 なので数は付かない。
        Assert.Contains("[K:bipolar|+]", card.AdjustedDescription);
        Assert.DoesNotContain("[K:bipolar|+2]", card.AdjustedDescription);
    }

    [Fact]
    public async Task HandAttack_RetainsAtTurnEnd_WhenCalmOwnerHasCalmTurnEndTrait_EvenBeforeBipolar()
    {
        var (state, catalog, _) = await CalmHeroWithBipolarAsync("dto-phase-predict", ActorPhase.Calm, 0);
        // 転相拳士シオン（静でターンを終えると手札の全アタックに双極）として表示する。
        state = state with { CharacterId = "striker_shion" };

        var dto = BattleStateDtoMapper.ToDto(state, catalog);

        Assert.All(dto.Hand, card =>
        {
            bool isAttack = catalog.Cards[card.CardDefinitionId].CardType == CardType.Attack;
            if (isAttack) Assert.True(card.RetainsAtTurnEnd, card.CardDefinitionId);
        });
    }

    [Fact]
    public async Task HandAttack_DoesNotRetain_WhenCalmOwnerLacksTheTrait()
    {
        // 静の位相そのものは、ターン終了時にアタックへ双極を付けない（2026-11-28）。
        var (state, catalog, _) = await CalmHeroWithBipolarAsync("dto-phase-no-trait", ActorPhase.Calm, 0);

        var dto = BattleStateDtoMapper.ToDto(state, catalog);

        Assert.All(dto.Hand, card => Assert.False(card.RetainsAtTurnEnd, card.CardDefinitionId));
    }

    [Fact]
    public async Task HandCard_RetainsAtTurnEnd_WhenOwnerCalmAndBipolar()
    {
        var (state, catalog, cardId) = await CalmHeroWithBipolarAsync("dto-phase-retain", ActorPhase.Calm, 1);

        var dto = BattleStateDtoMapper.ToDto(state, catalog);

        var card = dto.Hand.Single(c => c.InstanceId == cardId);
        Assert.True(card.RetainsAtTurnEnd);
        Assert.Contains("[K:bipolar|+]", card.AdjustedDescription);
    }
}
