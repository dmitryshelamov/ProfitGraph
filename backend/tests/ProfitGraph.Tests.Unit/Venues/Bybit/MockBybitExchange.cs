using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     A stateful Bybit V5 Spot exchange in the test process (Profit Fox Designer's MockBybitExchangeHandler). It checks
///     every signature against an invented secret, keeps orders by <c>orderLinkId</c> and answers with scripted outcomes;
///     no request leaves the machine.
/// </summary>
internal sealed class MockBybitExchange : HttpMessageHandler
{
    public const string ApiKey = "test-bybit-key-not-real";
    public const string ApiSecret = "test-bybit-secret-not-real";
    public const long ServerTime = 1_793_000_000_000;

    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _orders = new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

    public List<(HttpMethod Method, string Path)> Requests { get; } = [];
    public List<string> CreateBodies { get; } = [];

    /// <summary>
    ///     The retCode of the next order creates, or null to accept them.
    /// </summary>
    public int? CreateCode { get; set; }

    /// <summary>
    ///     Answers order creates with HTTP 503 after storing nothing, as a lost request would look.
    /// </summary>
    public bool CreateUnavailable { get; set; }

    public int? CancelCode { get; set; }

    public void AddOrder(string clientOrderId, string side, string status, decimal cumulativeBase, decimal cumulativeQuote, Dictionary<string, decimal> fees)
    {
        _orders[clientOrderId] = Order(clientOrderId, "BTCUSDT", side, status, cumulativeBase, cumulativeQuote, fees);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;
        string query = request.RequestUri.Query.TrimStart('?');
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        lock (Requests)
        {
            Requests.Add((request.Method, path));
        }

        if (path.StartsWith("/v5/market/", StringComparison.Ordinal))
        {
            return path switch
            {
                "/v5/market/time" => Json(new { retCode = 0, time = ServerTime }),
                "/v5/market/tickers" => Json(new { retCode = 0, result = new { list = new[] { new { symbol = "BTCUSDT", lastPrice = "100" } } } }),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        if (!SignatureIsValid(request, request.Method == HttpMethod.Get ? query : body ?? string.Empty))
        {
            return Json(new { retCode = 10004, retMsg = "error sign" });
        }

        return path switch
        {
            "/v5/order/create" => Create(body!),
            "/v5/order/cancel" => Cancel(body!),
            "/v5/order/realtime" => Lookup(query, open: true),
            "/v5/order/history" => Lookup(query, open: false),
            "/v5/account/fee-rate" => Json(new { retCode = 0, result = new { list = new[] { new { symbol = "BTCUSDT", makerFeeRate = "0.0008", takerFeeRate = "0.001" } } } }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private HttpResponseMessage Create(string body)
    {
        lock (CreateBodies)
        {
            CreateBodies.Add(body);
        }

        if (CreateUnavailable)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        if (CreateCode is { } code)
        {
            return Json(new { retCode = code, retMsg = "scripted" });
        }

        using JsonDocument document = JsonDocument.Parse(body);
        string clientOrderId = document.RootElement.GetProperty("orderLinkId").GetString()!;

        if (_orders.ContainsKey(clientOrderId))
        {
            return Json(new { retCode = 110072, retMsg = "OrderLinkedID is duplicate" });
        }

        _orders[clientOrderId] = Order(clientOrderId, "BTCUSDT", document.RootElement.GetProperty("side").GetString()!, "New", cumulativeBase: 0m, cumulativeQuote: 0m, []);

        return Json(new { retCode = 0, result = new { orderId = "1", orderLinkId = clientOrderId } });
    }

    private HttpResponseMessage Cancel(string body)
    {
        if (CancelCode is { } code)
        {
            return Json(new { retCode = code, retMsg = "scripted" });
        }

        using JsonDocument document = JsonDocument.Parse(body);
        string clientOrderId = document.RootElement.GetProperty("orderLinkId").GetString()!;

        if (!_orders.TryGetValue(clientOrderId, out Dictionary<string, string>? order))
        {
            return Json(new { retCode = 110001, retMsg = "order not exists" });
        }

        order["orderStatus"] = "Cancelled";

        return Json(new { retCode = 0, result = new { orderId = "1", orderLinkId = clientOrderId } });
    }

    private HttpResponseMessage Lookup(string query, bool open)
    {
        string clientOrderId = HttpUtility.ParseQueryString(query)["orderLinkId"] ?? string.Empty;
        object[] list = _orders.TryGetValue(clientOrderId, out Dictionary<string, string>? order) &&
            open == (order["orderStatus"] is "New" or "PartiallyFilled")
                ? [Shape(order)]
                : [];

        return Json(new { retCode = 0, result = new { list } });
    }

    private static object Shape(Dictionary<string, string> order)
    {
        return new Dictionary<string, object>
        {
            ["orderLinkId"] = order["orderLinkId"],
            ["symbol"] = order["symbol"],
            ["side"] = order["side"],
            ["orderStatus"] = order["orderStatus"],
            ["cumExecQty"] = order["cumExecQty"],
            ["cumExecValue"] = order["cumExecValue"],
            ["cumFeeDetail"] = JsonSerializer.Deserialize<Dictionary<string, string>>(order["cumFeeDetail"])!
        };
    }

    private static Dictionary<string, string> Order(string clientOrderId, string symbol, string side, string status, decimal cumulativeBase, decimal cumulativeQuote, Dictionary<string, decimal> fees)
    {
        return new Dictionary<string, string>
        {
            ["orderLinkId"] = clientOrderId,
            ["symbol"] = symbol,
            ["side"] = side,
            ["orderStatus"] = status,
            ["cumExecQty"] = cumulativeBase.ToString(CultureInfo.InvariantCulture),
            ["cumExecValue"] = cumulativeQuote.ToString(CultureInfo.InvariantCulture),
            ["cumFeeDetail"] = JsonSerializer.Serialize(fees.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(CultureInfo.InvariantCulture)))
        };
    }

    private static bool SignatureIsValid(HttpRequestMessage request, string payload)
    {
        string? key = Header(request, "X-BAPI-API-KEY");
        string? timestamp = Header(request, "X-BAPI-TIMESTAMP");
        string? window = Header(request, "X-BAPI-RECV-WINDOW");
        string? signature = Header(request, "X-BAPI-SIGN");

        if (key != ApiKey || timestamp is null || window is null || signature is null || Math.Abs(long.Parse(timestamp, CultureInfo.InvariantCulture) - ServerTime) > 5_000)
        {
            return false;
        }

        string expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ApiSecret), Encoding.UTF8.GetBytes(timestamp + key + window + payload)));

        return expected == signature;
    }

    private static string? Header(HttpRequestMessage request, string name)
    {
        return request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.Single() : null;
    }

    private static HttpResponseMessage Json(object value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
