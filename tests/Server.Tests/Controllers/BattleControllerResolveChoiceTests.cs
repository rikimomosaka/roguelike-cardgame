using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Phase 10.5.M2-Choose Task 5: POST /api/v1/runs/current/battle/resolve-card-choice の
/// 統合テスト。
/// </summary>
public class BattleControllerResolveChoiceTests : IClassFixture<TempDataFactory>
{
    private const string ResolvePath = "/api/v1/runs/current/battle/resolve-card-choice";

    private readonly TempDataFactory _factory;

    public BattleControllerResolveChoiceTests(TempDataFactory factory) => _factory = factory;

    /// <summary>
    /// テスト用の choose card 定義 (exhaustCard + Select=choose + Pile=hand)。
    /// </summary>
    private static CardDefinition ExhaustChooseCardDef(string id) =>
        new(
            Id: id,
            Name: id,
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("exhaustCard", EffectScope.Self, null, 1,
                               Pile: "hand", Select: "choose"),
            },
            UpgradedEffects: null,
            Keywords: null);

    /// <summary>
    /// 直接 catalog (live dictionary) に choose card 定義を inject する。
    /// catalog の Cards は <see cref="Dictionary{TKey, TValue}"/> 実体なので runtime 追加可能。
    /// </summary>
    private static void InjectChooseCardIntoCatalog(System.IServiceProvider services, CardDefinition def)
    {
        var catalog = services.GetRequiredService<DataCatalog>();
        if (catalog.Cards is IDictionary<string, CardDefinition> dict)
            dict[def.Id] = def;
        else
            throw new System.InvalidOperationException(
                "catalog.Cards is not a mutable Dictionary; test setup needs adjustment");
    }

    /// <summary>
    /// session.State.Hand に choose card instance を 1 枚 inject + PendingCardPlay を直接書く。
    /// candidates には Hand 内の他カード instance ids を入れる。
    /// </summary>
    private static (string cardInstanceId, string[] candidateIds) InjectPendingChooseStateAsync(
        System.IServiceProvider services, string accountId, string cardDefId)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        if (!store.TryGet(accountId, out var session))
            throw new System.InvalidOperationException(
                $"InjectPendingChooseStateAsync: session not found for {accountId}");

        const string chooseCardInstanceId = "choose_card_test_1";
        var chooseInstance = new BattleCardInstance(
            InstanceId: chooseCardInstanceId,
            CardDefinitionId: cardDefId,
            IsUpgraded: false,
            CostOverride: null);

        // 既存 Hand に追加 (starter strike + defend) - candidates は hand 全体になる前提
        var newHand = session.State.Hand.Add(chooseInstance);
        var candidateIds = new string[newHand.Length];
        for (int i = 0; i < newHand.Length; i++) candidateIds[i] = newHand[i].InstanceId;

        var pending = new PendingCardPlay(
            CardInstanceId: chooseCardInstanceId,
            EffectIndex: 0,
            SummonSucceededBefore: false,
            Choice: new PendingChoice(
                Action: "exhaustCard",
                Pile: "hand",
                Count: 1,
                CandidateInstanceIds: candidateIds.ToImmutableArray()));

        var newState = session.State with
        {
            Hand = newHand,
            PendingCardPlay = pending,
        };
        store.Set(accountId, session with { State = newState });

        return (chooseCardInstanceId, candidateIds);
    }

    // 1. session が無い場合は 409
    [Fact]
    public async Task ResolveCardChoice_when_no_session_returns_409()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            // start を呼ばずに直接 resolve
            var resp = await client.PostAsJsonAsync(ResolvePath,
                new ResolveCardChoiceRequestDto(System.Array.Empty<string>()));
            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // 2. session ありだが PendingCardPlay 未設定 → engine が InvalidOperationException → 400
    [Fact]
    public async Task ResolveCardChoice_when_no_pending_returns_400()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var resp = await client.PostAsJsonAsync(ResolvePath,
                new ResolveCardChoiceRequestDto(System.Array.Empty<string>()));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // 3. 選択数が pending.Choice.Count と不一致 → 400
    [Fact]
    public async Task ResolveCardChoice_with_wrong_selection_count_returns_400()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            const string cardDefId = "exhaust_choose_test_count";
            InjectChooseCardIntoCatalog(_factory.Services, ExhaustChooseCardDef(cardDefId));
            var (_, candidates) = InjectPendingChooseStateAsync(_factory.Services, accountId, cardDefId);

            // Count=1 だが 2 件選択 → engine reject
            var resp = await client.PostAsJsonAsync(ResolvePath,
                new ResolveCardChoiceRequestDto(new[] { candidates[0], candidates[1] }));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // 4. 候補に含まれない instance id を選択 → 400
    [Fact]
    public async Task ResolveCardChoice_with_non_candidate_id_returns_400()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            const string cardDefId = "exhaust_choose_test_noncand";
            InjectChooseCardIntoCatalog(_factory.Services, ExhaustChooseCardDef(cardDefId));
            InjectPendingChooseStateAsync(_factory.Services, accountId, cardDefId);

            var resp = await client.PostAsJsonAsync(ResolvePath,
                new ResolveCardChoiceRequestDto(new[] { "not-a-real-instance-id" }));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            client.Dispose();
        }
    }

    // 5. happy-path: pending を resolve → 200 + state.PendingCardPlay == null
    [Fact]
    public async Task ResolveCardChoice_with_valid_selection_clears_pending_returns_200()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(_factory);
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            const string cardDefId = "exhaust_choose_test_happy";
            InjectChooseCardIntoCatalog(_factory.Services, ExhaustChooseCardDef(cardDefId));
            var (_, candidates) = InjectPendingChooseStateAsync(_factory.Services, accountId, cardDefId);

            // 候補から先頭を 1 件選択 (Count=1)
            var resp = await client.PostAsJsonAsync(ResolvePath,
                new ResolveCardChoiceRequestDto(new[] { candidates[0] }));

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);
            // resolve 後は pending クリア
            Assert.Null(body!.State.PendingCardPlay);
        }
        finally
        {
            client.Dispose();
        }
    }
}
