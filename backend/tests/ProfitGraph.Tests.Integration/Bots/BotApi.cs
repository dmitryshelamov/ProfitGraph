using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Backtesting;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Requests of the bot API tests: owners with a strategy and an invented credential, and bots on the test venue.
/// </summary>
internal static class BotApi
{
    internal const string Path = "/api/bots";
    internal const string ApiKey = "test-api-key-not-real";

    private static readonly string FakeMasterKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x44, count: 32).ToArray());

    /// <summary>
    ///     An invented master key, so credentials can be stored.
    /// </summary>
    internal static Dictionary<string, string> Settings()
    {
        return new Dictionary<string, string>
        {
            ["Secrets:ActiveKeyId"] = "test-v1",
            ["Secrets:MasterKeys:test-v1"] = FakeMasterKey
        };
    }

    internal static async Task<BotOwner> NewOwner(ProfitGraphApiFactory factory, string venue = TestHistoryVenue.WithHistory, UserInternal? account = null)
    {
        UserInternal user = account ?? await factory.CreateAccount(UserRole.User);
        HttpClient client = await factory.SignIn(user);
        Guid strategyId = await BacktestScenario.CreateStrategy(factory, user.Id);
        HttpResponseMessage credential = await client.PostAsJsonAsync(
            "/api/exchange-credentials",
            new { venue, label = "invented", apiKey = ApiKey, apiSecret = "test-api-secret-not-real" });
        credential.StatusCode.ShouldBe(HttpStatusCode.Created);

        return new BotOwner(client, strategyId, (await Json(credential)).GetProperty("id").GetGuid());
    }

    internal static object Request(
        BotOwner owner,
        Guid? strategyId = null,
        Guid? credentialId = null,
        string symbol = "BTCUSDT",
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        return new
        {
            strategyId = strategyId ?? owner.StrategyId,
            credentialId = credentialId ?? owner.CredentialId,
            symbol,
            timeframe = "1h",
            parameters = parameters ?? Parameters(),
            allocationCap = "100",
            maxOrderQuote = "20",
            dailyLossLimitQuote = "10",
            allowForeignOrders = false
        };
    }

    internal static Dictionary<string, string> Parameters()
    {
        return new Dictionary<string, string> { ["fastPeriod"] = "2", ["slowPeriod"] = "4", ["buyAmount"] = "10" };
    }

    internal static async Task<Guid> Create(BotOwner owner)
    {
        HttpResponseMessage response = await owner.Client.PostAsJsonAsync(Path, Request(owner));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await Json(response)).GetProperty("id").GetGuid();
    }

    internal static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static async Task<string?> Code(HttpResponseMessage response)
    {
        return (await Json(response)).GetProperty("code").GetString();
    }
}