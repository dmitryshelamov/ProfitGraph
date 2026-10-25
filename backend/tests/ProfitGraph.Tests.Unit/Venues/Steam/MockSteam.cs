using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     An in-process Steam with one invented account (Profit Fox Designer's mock contract, after the fixes that aligned it
///     with real responses). Hosts end in <c>.invalid</c>, so no request can leave the process. It counts every request and
///     can be told to answer differently.
/// </summary>
internal sealed class MockSteam : HttpMessageHandler
{
    public const string Community = "https://community.steam.invalid/";
    public const string Login = "https://login.steam.invalid/";
    public const string Api = "https://api.steam.invalid/";
    public const string SteamId = "76561190000000001";
    public const string RefreshToken = "refresh-token-not-real";

    private readonly List<(HttpMethod Method, string Path)> _requests = new List<(HttpMethod Method, string Path)>();
    private int _issued;

    public DateTimeOffset ServerTime { get; set; } = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 12, minute: 0, second: 0, TimeSpan.Zero);

    /// <summary>
    ///     The cookie value Community accepts; signing in issues a new one.
    /// </summary>
    public string? ValidSession { get; private set; }

    public string ReturnedSteamId { get; set; } = SteamId;

    public long? TokenExpiry { get; set; }

    /// <summary>
    ///     Answers a request instead of the default handling when it returns a response.
    /// </summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

    public IReadOnlyList<string> Cookies { get; private set; } = [];

    /// <summary>
    ///     The <c>g_rgWalletInfo</c> object the signed-in market page carries (KZT, as the owner's wallet).
    /// </summary>
    public string WalletInfo { get; set; } =
        """{"wallet_currency":37,"wallet_country":"KZ","wallet_fee":"1","wallet_fee_minimum":"1","wallet_fee_percent":"0.05","wallet_publisher_fee_percent_default":"0.10","wallet_fee_base":"0","wallet_balance":"123456","wallet_delayed_balance":"0","wallet_max_balance":"100000000","wallet_trade_max_balance":"90000000","success":1,"rwgrsn":-2,"wallet_market_minimum":1,"wallet_currency_increment":1}""";

    public string PriceHistory { get; set; } = """{"success":true,"price_prefix":"","price_suffix":"₸","prices":[]}""";

    public string SearchResults { get; set; } = """{"success":true,"start":0,"pagesize":10,"total_count":0,"results":[]}""";

    public string OrderBook { get; set; } = """{"data":{"success":1,"data":{"eCurrency":37,"amtMaxBuyOrder":51234,"amtMinSellOrder":52000}}}""";

    /// <summary>
    ///     The item's lowest listing as <c>priceoverview</c> writes it, or null when nobody sells it.
    /// </summary>
    public string? LowestPrice { get; set; } = "520,00₸";

    /// <summary>
    ///     The account's buy orders by Steam ID; one active order per item, as Steam allows.
    /// </summary>
    public Dictionary<string, MockSteamBuyOrder> BuyOrders { get; } = new Dictionary<string, MockSteamBuyOrder>(StringComparer.Ordinal);

    /// <summary>
    ///     The form fields of the last <c>createbuyorder</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> LastBuyForm { get; private set; } = new Dictionary<string, string>();

    /// <summary>
    ///     Whether a new buy order needs a mobile confirmation first, as Steam asks of some accounts.
    /// </summary>
    public bool BuysNeedConfirmation { get; set; }

    /// <summary>
    ///     The account's pending mobile confirmations; an approved one leaves the list.
    /// </summary>
    public List<MockSteamConfirmation> Confirmations { get; } = new List<MockSteamConfirmation>();

    /// <summary>
    ///     The creators whose confirmations were approved.
    /// </summary>
    public HashSet<string> Approved { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    ///     The identity secret confirmation requests must be signed with (the harness's invented one).
    /// </summary>
    public string IdentitySecret { get; set; } = "aWRlbnRpdHktbm90LXJlYWw=";

    /// <summary>
    ///     The account's listings; a new one waits for its mobile confirmation, as on an account with Steam Guard.
    /// </summary>
    public List<MockSteamListing> Listings { get; } = new List<MockSteamListing>();

    /// <summary>
    ///     The account's market history, oldest first; pages are served newest first.
    /// </summary>
    public List<MockSteamHistoryEvent> History { get; } = new List<MockSteamHistoryEvent>();

    public int Count(string path)
    {
        lock (_requests)
        {
            return _requests.Count(request => request.Path == path);
        }
    }

    public void ExpireSessions()
    {
        ValidSession = null;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;

        lock (_requests)
        {
            _requests.Add((request.Method, path));
            Cookies = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies) ? [.. cookies] : [];
        }

        if (Override?.Invoke(request) is { } answer)
        {
            return answer;
        }

        string form = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        return (request.RequestUri.Host, path) switch
        {
            ("login.steam.invalid", "/jwt/finalizelogin") when form.Contains(RefreshToken, StringComparison.Ordinal) => Json(
                $$$"""{"steamID":"{{{ReturnedSteamId}}}","transfer_info":[{"url":"https://help.steam.invalid/login/settoken","params":{"nonce":"x"}},{"url":"{{{Community}}}login/settoken","params":{"nonce":"n1","auth":"a1"}}]}"""),
            ("login.steam.invalid", "/jwt/finalizelogin") => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            ("community.steam.invalid", "/login/settoken") => SetToken(),
            ("api.steam.invalid", "/ITwoFactorService/QueryTime/v1/") => Json($$$"""{"response":{"server_time":"{{{ServerTime.ToUnixTimeSeconds()}}}"}}"""),
            ("community.steam.invalid", "/market/") => Market(request),
            ("community.steam.invalid", "/market/pricehistory/") => Json(PriceHistory),
            ("community.steam.invalid", "/market/search/render/") => Json(SearchResults),
            ("community.steam.invalid", "/market/orderbook") => Json(OrderBook),
            ("community.steam.invalid", "/market/priceoverview/") => Json(
                LowestPrice is null ? """{"success":true,"volume":"0"}""" : $$"""{"success":true,"lowest_price":"{{LowestPrice}}","volume":"12"}"""),
            ("community.steam.invalid", "/market/createbuyorder/") => CreateBuyOrder(form),
            ("community.steam.invalid", "/market/cancelbuyorder/") => CancelBuyOrder(form),
            ("community.steam.invalid", "/market/getbuyorderstatus/") => BuyOrderStatus(request.RequestUri),
            ("community.steam.invalid", "/mobileconf/getlist") => ConfirmationList(request.RequestUri),
            ("community.steam.invalid", "/market/sellitem/") => SellItem(form),
            ("community.steam.invalid", "/market/mylistings/") => MyListings(),
            ("community.steam.invalid", "/market/myhistory/render/") => MyHistory(request.RequestUri),
            ("community.steam.invalid", var removal) when removal.StartsWith("/market/removelisting/", StringComparison.Ordinal) => RemoveListing(removal),
            ("community.steam.invalid", "/mobileconf/ajaxop") => ApproveConfirmation(request.RequestUri),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    private static Dictionary<string, string> Fields(string form)
    {
        return form.Split(separator: '&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(separator: '=', count: 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace(oldChar: '+', newChar: ' ')),
                pair => Uri.UnescapeDataString(pair[1].Replace(oldChar: '+', newChar: ' ')));
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private HttpResponseMessage CreateBuyOrder(string form)
    {
        Dictionary<string, string> fields = Fields(form);

        lock (BuyOrders)
        {
            LastBuyForm = fields;
            string name = fields["market_hash_name"];

            if (BuyOrders.Values.Any(order => order.Active && order.MarketHashName == name))
            {
                return Json("""{"success":29}""");
            }

            if (BuysNeedConfirmation && !Approved.Contains(fields["confirmation"]))
            {
                var creator = (7700 + Confirmations.Count + Approved.Count + 1).ToString(CultureInfo.InvariantCulture);

                if (fields["confirmation"] == "0")
                {
                    Confirmations.Add(new MockSteamConfirmation("55" + creator, "99" + creator, Type: 12, creator));
                }

                return new HttpResponseMessage(HttpStatusCode.NotAcceptable)
                {
                    Content = new StringContent(
                        $$$"""{"success":22,"need_confirmation":true,"confirmation":{"confirmation_id":"{{{creator}}}"}}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            var id = (9000 + BuyOrders.Count + 1).ToString(CultureInfo.InvariantCulture);
            BuyOrders[id] = new MockSteamBuyOrder(
                name,
                int.Parse(fields["quantity"], CultureInfo.InvariantCulture),
                long.Parse(fields["price_total"], CultureInfo.InvariantCulture));

            return Json($$"""{"success":1,"buy_orderid":"{{id}}"}""");
        }
    }

    private HttpResponseMessage ConfirmationList(Uri address)
    {
        if (!Signed(address, "conf"))
        {
            return Json("""{"success":false}""");
        }

        lock (BuyOrders)
        {
            return Json(
                JsonSerializer.Serialize(
                    new
                    {
                        success = true,
                        conf = Confirmations.Select(confirmation => new
                        {
                            id = confirmation.Id,
                            nonce = confirmation.Nonce,
                            type = confirmation.Type,
                            creator_id = confirmation.CreatorId
                        })
                    }));
        }
    }

    private HttpResponseMessage ApproveConfirmation(Uri address)
    {
        Dictionary<string, string> query = Fields(address.Query.TrimStart('?'));

        lock (BuyOrders)
        {
            MockSteamConfirmation? confirmation = Confirmations.SingleOrDefault(item => item.Id == query["cid"] && item.Nonce == query["ck"]);

            if (!Signed(address, "accept") || query["op"] != "allow" || confirmation is null)
            {
                return Json("""{"success":false}""");
            }

            Confirmations.Remove(confirmation);
            Approved.Add(confirmation.CreatorId);

            foreach (MockSteamListing listing in Listings.Where(listing => confirmation.Type == 3 && listing.Id == confirmation.CreatorId))
            {
                listing.Confirmed = true;
            }

            return Json("""{"success":true}""");
        }
    }

    private HttpResponseMessage SellItem(string form)
    {
        Dictionary<string, string> fields = Fields(form);

        lock (BuyOrders)
        {
            if (Listings.Any(listing => listing.AssetId == fields["assetid"]) || fields["amount"] != "1")
            {
                return Json("""{"success":false}""");
            }

            var id = (8800 + Listings.Count + 1).ToString(CultureInfo.InvariantCulture);
            Listings.Add(
                new MockSteamListing(
                    id,
                    int.Parse(fields["appid"], CultureInfo.InvariantCulture),
                    fields["contextid"],
                    fields["assetid"],
                    long.Parse(fields["price"], CultureInfo.InvariantCulture)));
            Confirmations.Add(new MockSteamConfirmation("66" + id, "77" + id, Type: 3, id));

            return Json("""{"success":true,"requires_confirmation":1,"needs_mobile_confirmation":true,"needs_email_confirmation":false,"email_domain":""}""");
        }
    }

    private HttpResponseMessage RemoveListing(string path)
    {
        string id = path["/market/removelisting/".Length..];

        lock (BuyOrders)
        {
            return Listings.RemoveAll(listing => listing.Id == id) == 1 ? Json("""{"success":true}""") : Json("""{"success":false}""");
        }
    }

    private HttpResponseMessage MyListings()
    {
        lock (BuyOrders)
        {
            string rows = string.Concat(
                Listings.Select(listing =>
                    $"""<div class="market_listing_row" id="mylisting_{listing.Id}"><a href="javascript:{(listing.Confirmed ? "RemoveMarketListing" : "CancelMarketListingConfirmation")}('mylisting', '{listing.Id}', {listing.AppId}, '{listing.ContextId}', '{listing.AssetId}')">Remove</a><span id="mylisting_{listing.Id}_name">item</span></div>"""));

            return Json(JsonSerializer.Serialize(new { success = true, pagesize = 100, total_count = Listings.Count, start = 0, results_html = rows }));
        }
    }

    private HttpResponseMessage MyHistory(Uri address)
    {
        Dictionary<string, string> query = Fields(address.Query.TrimStart('?'));
        int start = int.Parse(query["start"], CultureInfo.InvariantCulture);
        int count = int.Parse(query["count"], CultureInfo.InvariantCulture);

        lock (BuyOrders)
        {
            MockSteamHistoryEvent[] page = [.. Enumerable.Reverse(History).Skip(start).Take(count)];
            var listings = new Dictionary<string, object>();
            var purchases = new Dictionary<string, object>();
            var descriptions = new Dictionary<string, object>();

            foreach (MockSteamHistoryEvent item in page)
            {
                listings[item.ListingId] = new { listingid = item.ListingId, asset = new { appid = 730, contextid = "2", id = item.AssetId, amount = "1" } };
                descriptions[item.AssetId] = new { market_hash_name = item.Name, amount = "1", original_amount = "1" };

                if (item.PurchaseId is { } purchase)
                {
                    purchases[item.ListingId + "_" + purchase] = new
                    {
                        listingid = item.ListingId,
                        purchaseid = purchase,
                        asset = new { appid = 730, contextid = "2", id = item.AssetId, amount = "1", new_id = item.NewAssetId, new_contextid = "2" },
                        paid_amount = item.Paid,
                        paid_fee = item.Fee,
                        currencyid = "2037",
                        received_amount = item.Received,
                        received_currencyid = "2037",
                        failed = 0,
                        needs_rollback = 0,
                        funds_returned = 0
                    };
                }
            }

            object body = new
            {
                success = true,
                pagesize = count,
                total_count = History.Count,
                start,
                events = page.Select(item => new
                {
                    event_type = item.Type,
                    listingid = item.ListingId,
                    purchaseid = item.PurchaseId,
                    time_event = item.Seconds,
                    time_event_fraction = 0
                }),
                listings,
                purchases,
                assets = new Dictionary<string, object> { ["730"] = new Dictionary<string, object> { ["2"] = descriptions } }
            };

            return Json(JsonSerializer.Serialize(body));
        }
    }

    private bool Signed(Uri address, string tag)
    {
        Dictionary<string, string> query = Fields(address.Query.TrimStart('?'));
        byte[] input = [.. BitConverter.GetBytes(long.Parse(query["t"], CultureInfo.InvariantCulture)).Reverse(), .. Encoding.UTF8.GetBytes(tag)];
        string expected = Convert.ToBase64String(HMACSHA1.HashData(Convert.FromBase64String(IdentitySecret), input));

        return query["tag"] == tag && query["k"] == expected && query["a"] == SteamId && query["m"] == "android" &&
            query["t"] == ServerTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    private HttpResponseMessage CancelBuyOrder(string form)
    {
        lock (BuyOrders)
        {
            if (!BuyOrders.TryGetValue(Fields(form)["buy_orderid"], out MockSteamBuyOrder? order))
            {
                return Json("""{"success":8}""");
            }

            order.Active = false;

            return Json("""{"success":1}""");
        }
    }

    private HttpResponseMessage BuyOrderStatus(Uri address)
    {
        string id = Uri.UnescapeDataString(address.Query.Split("buy_orderid=")[1]);

        lock (BuyOrders)
        {
            return BuyOrders.TryGetValue(id, out MockSteamBuyOrder? order)
                ? Json(
                    $$"""{"success":1,"active":{{(order.Active ? 1 : 0)}},"purchased":{{order.Quantity - order.Remaining}},"quantity":"{{order.Quantity}}","quantity_remaining":"{{order.Remaining}}","purchases":[]}""")
                : Json("""{"success":8}""");
        }
    }

    private HttpResponseMessage SetToken()
    {
        int number = Interlocked.Increment(ref _issued);
        string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { exp = TokenExpiry ?? ServerTime.AddDays(1).ToUnixTimeSeconds() }))
            .TrimEnd('=')
            .Replace(oldChar: '+', newChar: '-')
            .Replace(oldChar: '/', newChar: '_');
        ValidSession = Uri.EscapeDataString($"{SteamId}||header.{payload}.signature{number}");
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("Set-Cookie", $"steamLoginSecure={ValidSession}; Path=/; Secure; HttpOnly");
        response.Headers.Add("Set-Cookie", "steamCountry=XX%7Cinvented; Path=/");

        return response;
    }

    private HttpResponseMessage Market(HttpRequestMessage request)
    {
        bool signedIn = ValidSession is not null && Cookies.Any(cookie => cookie.Contains($"steamLoginSecure={ValidSession}", StringComparison.Ordinal));

        if (signedIn)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"<html><script>var g_rgWalletInfo = {WalletInfo};</script>market</html>", Encoding.UTF8, "text/html")
            };
        }

        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri("/login/home/?goto=market%2F", UriKind.Relative);

        return redirect;
    }
}