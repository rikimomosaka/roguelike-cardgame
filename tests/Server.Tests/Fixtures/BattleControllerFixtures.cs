using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Controllers;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Fixtures;

/// <summary>
/// BattleController の test 用 helper。
/// 既存 <see cref="TempDataFactory"/> + <see cref="BattleTestHelpers"/> を流用し、
/// 「アカウント作成 → ラン開始 → 敵タイル進入で <c>RunState.ActiveBattle</c> が non-null」
/// の状態まで進めた HttpClient を返す。
/// </summary>
internal static class BattleControllerFixtures
{
    public const string AccountHeader = "X-Account-Id";

    /// <summary>
    /// IClassFixture から渡された <see cref="TempDataFactory"/> を使い、
    /// accountId / ラン / 敵タイル進入をセットアップして HttpClient を返す。
    /// fixture の所有者 (xUnit) が disposal を担うため、呼び出し側で factory を Dispose しないこと。
    /// </summary>
    public static async Task<(HttpClient client, string accountId)>
        SetupRunWithActiveBattleAsync(TempDataFactory factory, string accountId = "test-account")
    {
        factory.ResetData();
        var client = factory.CreateClient();

        await BattleTestHelpers.EnsureAccountAsync(client, accountId);
        BattleTestHelpers.WithAccount(client, accountId);
        await BattleTestHelpers.StartRunAndMoveToEnemyAsync(client);

        return (client, accountId);
    }

    public static void AssertNoSession(System.IServiceProvider services, string accountId)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        Assert.False(store.TryGet(accountId, out _));
    }
}
