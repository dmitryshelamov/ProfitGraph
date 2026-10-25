using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProfitGraph.Tests.Integration.Steam;

/// <summary>
///     An in-process Steam with one invented KZT account and one item's market, for live bot scenarios (ADR 0022). Its
///     state stays coherent the way Steam's does: a filled buy order counts its purchases, writes a bought event and puts
///     the item into the inventory under a new asset ID with its trade hold; a listing waits for its mobile confirmation,
///     leaves the inventory and, once sold, writes a sold event; a removed listing returns its item. The test moves the
///     market by hand. Hosts end in <c>.invalid</c>, so nothing leaves the process.
/// </summary>
internal sealed class MockSteamMarket(TimeProvider clock, string steamId) : HttpMessageHandler
{
    public const string Community = "https://community.steam.invalid/";
    public const string Login = "https://login.steam.invalid/";
    public const string Api = "https://api.steam.invalid/";
    public const string RefreshToken = "refresh-token-not-real";
    public const string Item = "Dreams & Nightmares Case";

    /// <summary>
    ///     The invented identity secret of the imported authenticator (<c>identity-not-real</c>), which signs confirmation
    ///     requests.
    /// </summary>
    public const string IdentitySecret = "aWRlbnRpdHktbm90LXJlYWw=";

    private const int AppId = 730;
    private const string Context = "2";
    private const int BoughtEvent = 4;
    private const int SoldEvent = 3;
    private const int ListedEvent = 1;
    private const int CancelledEvent = 2;

    /// <summary>
    ///     The item's lowest listing in minor units, which a market buy pays.
    /// </summary>
    private const long LowestListing = 48000;

    /// <summary>
    ///     The item's highest buy order in minor units, which a market sell lists at.
    /// </summary>
    private const long HighestBuyOrder = 40000;

    private readonly Lock _gate = new Lock();
    private readonly List<string> _requests = new List<string>();
    private readonly List<string> _sold = new List<string>();
    private readonly Dictionary<string, BuyOrder> _buyOrders = new Dictionary<string, BuyOrder>(StringComparer.Ordinal);
    private readonly List<Confirmation> _confirmations = new List<Confirmation>();
    private readonly HashSet<string> _approved = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<Listing> _listings = new List<Listing>();
    private readonly List<HistoryEvent> _history = new List<HistoryEvent>();
    private readonly List<Asset> _inventory = new List<Asset>();
    private long _ids = 1000;
    private string? _session;

    /// <summary>
    ///     How long a bought item stays untradable; the inventory shows the end rounded up to Steam's whole minute.
    /// </summary>
    public TimeSpan TradeHold { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Whether a removed listing returns its item under a new asset ID, as Steam sometimes does.
    /// </summary>
    public bool ReturnsUnderNewIds { get; set; }

    /// <summary>
    ///     Answers a request instead of the default handling when it returns a response.
    /// </summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

    public int Count(string path)
    {
        lock (_gate)
        {
            return _requests.Count(request => request == path);
        }
    }

    /// <summary>
    ///     Ends the account's web session on Steam's side; Community sends every later request to sign-in.
    /// </summary>
    public void ExpireSession()
    {
        lock (_gate)
        {
            _session = null;
        }
    }

    /// <summary>
    ///     The asset IDs of the items sold, in order.
    /// </summary>
    public IReadOnlyList<string> Sold()
    {
        lock (_gate)
        {
            return [.. _sold];
        }
    }

    /// <summary>
    ///     Sellers fill every active buy order of the item in full at its price, one item per purchase.
    /// </summary>
    public int SellToBuyOrders()
    {
        lock (_gate)
        {
            var sold = 0;

            foreach ((string _, BuyOrder order) in _buyOrders.Where(pair => pair.Value.Active))
            {
                while (order.Remaining > 0)
                {
                    string asset = NextId();
                    long fee = order.Price * 15 / 115;
                    _history.Add(new HistoryEvent(BoughtEvent, NextId(), Now(), NextId(), asset, NextId(), order.Price - fee, fee, Received: 0));
                    _inventory.Add(new Asset(asset, Minute(clock.GetUtcNow() + TradeHold)));
                    order.Remaining--;
                    sold++;
                }

                order.Active = false;
            }

            return sold;
        }
    }

    /// <summary>
    ///     Buyers buy every active listing of the account; the seller receives the listing's price.
    /// </summary>
    public int BuyListings()
    {
        lock (_gate)
        {
            Listing[] active = [.. _listings.Where(listing => listing.Confirmed)];

            foreach (Listing listing in active)
            {
                _history.Add(new HistoryEvent(SoldEvent, listing.Id, Now(), listing.AssetId, NewAssetId: null, NextId(), Paid: 0, Fee: 0, listing.SellerPrice));
                _listings.Remove(listing);
                _sold.Add(listing.AssetId);
            }

            return active.Length;
        }
    }

    /// <summary>
    ///     The item's listings of the account: their asset, seller price and whether they are confirmed.
    /// </summary>
    public IReadOnlyList<(string AssetId, long SellerPrice, bool Confirmed)> Listings()
    {
        lock (_gate)
        {
            return [.. _listings.Select(listing => (listing.AssetId, listing.SellerPrice, listing.Confirmed))];
        }
    }

    /// <summary>
    ///     The account's active buy orders of the item: price per item in minor units and items still wanted.
    /// </summary>
    public IReadOnlyList<(long Price, int Remaining)> BuyOrders()
    {
        lock (_gate)
        {
            return [.. _buyOrders.Values.Where(order => order.Active).Select(order => (order.Price, order.Remaining))];
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri!.AbsolutePath;

        lock (_gate)
        {
            _requests.Add(path);
        }

        if (Override?.Invoke(request) is { } answer)
        {
            return answer;
        }

        string form = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        bool signedIn = SignedIn(request);

        lock (_gate)
        {
            return (request.RequestUri.Host, path) switch
            {
                ("login.steam.invalid", "/jwt/finalizelogin") when form.Contains(RefreshToken, StringComparison.Ordinal) => Json(
                    $$$"""{"steamID":"{{{steamId}}}","transfer_info":[{"url":"{{{Community}}}login/settoken","params":{"nonce":"n1","auth":"a1"}}]}"""),
                ("login.steam.invalid", "/jwt/finalizelogin") => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                ("community.steam.invalid", "/login/settoken") => SetToken(),
                ("api.steam.invalid", "/ITwoFactorService/QueryTime/v1/") => Json($$$"""{"response":{"server_time":"{{{Now()}}}"}}"""),
                ("community.steam.invalid", _) when !signedIn => SignInRedirect(),
                ("community.steam.invalid", "/market/") => Market(),
                ("community.steam.invalid", "/market/eligibilitycheck/") => Redirect("/market/"),
                ("community.steam.invalid", "/market/priceoverview/") => Json($$"""{"success":true,"lowest_price":"{{Money(LowestListing)}}","volume":"12"}"""),
                ("community.steam.invalid", "/market/orderbook") => Json(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{{\"data\":{{\"success\":1,\"data\":{{\"eCurrency\":37,\"amtMaxBuyOrder\":{HighestBuyOrder},\"amtMinSellOrder\":{LowestListing}}}}}}}")),
                ("community.steam.invalid", "/market/createbuyorder/") => CreateBuyOrder(Fields(form)),
                ("community.steam.invalid", "/market/cancelbuyorder/") => CancelBuyOrder(Fields(form)),
                ("community.steam.invalid", "/market/getbuyorderstatus/") => BuyOrderStatus(Fields(request.RequestUri.Query.TrimStart('?'))),
                ("community.steam.invalid", "/mobileconf/getlist") => ConfirmationList(Fields(request.RequestUri.Query.TrimStart('?'))),
                ("community.steam.invalid", "/mobileconf/ajaxop") => ApproveConfirmation(Fields(request.RequestUri.Query.TrimStart('?'))),
                ("community.steam.invalid", "/market/sellitem/") => SellItem(Fields(form)),
                ("community.steam.invalid", "/market/mylistings/") => MyListings(),
                ("community.steam.invalid", "/market/myhistory/render/") => MyHistory(Fields(request.RequestUri.Query.TrimStart('?'))),
                ("community.steam.invalid", var profile) when profile == $"/profiles/{steamId}/inventory/" => Html(
                    """<script>var g_rgAppContextData = {"730":{"appid":730,"rgContexts":{"2":{"id":"2"}}}};</script>"""),
                ("community.steam.invalid", var inventory) when inventory == $"/inventory/{steamId}/730/2" => InventoryPage(),
                ("community.steam.invalid", var removal) when removal.StartsWith("/market/removelisting/", StringComparison.Ordinal) => RemoveListing(
                    removal["/market/removelisting/".Length..]),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }
    }

    private static Dictionary<string, string> Fields(string form)
    {
        return form.Split(separator: '&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(separator: '=', count: 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace(oldChar: '+', newChar: ' ')),
                pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace(oldChar: '+', newChar: ' ')) : string.Empty,
                StringComparer.Ordinal);
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage Html(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
        redirect.Headers.Location = new Uri(location, UriKind.Relative);

        return redirect;
    }

    private static HttpResponseMessage SignInRedirect()
    {
        return Redirect("/login/home/?goto=market%2F");
    }

    private static string Money(long minor)
    {
        return (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture).Replace(oldChar: '.', newChar: ',') + "₸";
    }

    /// <summary>
    ///     Steam shows an unlock to the minute, so the item stays locked until the next whole minute.
    /// </summary>
    private static DateTimeOffset Minute(DateTimeOffset at)
    {
        var truncated = new DateTimeOffset(at.Year, at.Month, at.Day, at.Hour, at.Minute, second: 0, TimeSpan.Zero);

        return truncated == at ? at : truncated.AddMinutes(1);
    }

    private long Now()
    {
        return clock.GetUtcNow().ToUnixTimeSeconds();
    }

    private string NextId()
    {
        return (++_ids).ToString(CultureInfo.InvariantCulture);
    }

    private bool SignedIn(HttpRequestMessage request)
    {
        lock (_gate)
        {
            return _session is not null && request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies) &&
                cookies.Any(cookie => cookie.Contains($"steamLoginSecure={_session}", StringComparison.Ordinal));
        }
    }

    private HttpResponseMessage SetToken()
    {
        string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { exp = clock.GetUtcNow().AddDays(1).ToUnixTimeSeconds() }))
            .TrimEnd('=')
            .Replace(oldChar: '+', newChar: '-')
            .Replace(oldChar: '/', newChar: '_');
        _session = Uri.EscapeDataString($"{steamId}||header.{payload}.signature{NextId()}");
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("Set-Cookie", $"steamLoginSecure={_session}; Path=/; Secure; HttpOnly");

        return response;
    }

    private HttpResponseMessage Market()
    {
        string rows = string.Concat(
            _buyOrders.Where(pair => pair.Value.Active)
                .Select(pair =>
                    $"""<div class="market_listing_row" id="mybuyorder_{pair.Key}"><a class="market_listing_item_name_link" href="{Community}market/listings/730/{Uri.EscapeDataString(Item)}">Case</a><span class="market_listing_price">{pair.Value.Remaining} @ {Money(pair.Value.Price)}</span></div>"""));

        return Html(
            $$"""<html><script>var g_rgWalletInfo = {"wallet_currency":37,"wallet_country":"KZ","wallet_fee":"1","wallet_fee_minimum":"1","wallet_fee_percent":"0.05","wallet_publisher_fee_percent_default":"0.10","wallet_fee_base":"0","wallet_balance":"10000000","wallet_delayed_balance":"0","wallet_max_balance":"100000000","wallet_trade_max_balance":"90000000","success":1,"rwgrsn":-2,"wallet_market_minimum":1,"wallet_currency_increment":1};</script><div id="myListings">{{rows}}</div></html>""");
    }

    private HttpResponseMessage CreateBuyOrder(Dictionary<string, string> fields)
    {
        if (fields["market_hash_name"] != Item || _buyOrders.Values.Any(order => order.Active))
        {
            return Json("""{"success":29}""");
        }

        // Every new buy order needs its mobile confirmation first, then the same request again with the confirmation's creator.
        if (!_approved.Contains(fields["confirmation"]))
        {
            string creator = NextId();
            _confirmations.Add(new Confirmation(NextId(), NextId(), Type: 12, creator));

            return new HttpResponseMessage(HttpStatusCode.NotAcceptable)
            {
                Content = new StringContent(
                    $$$"""{"success":22,"need_confirmation":true,"confirmation":{"confirmation_id":"{{{creator}}}"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }

        int quantity = int.Parse(fields["quantity"], CultureInfo.InvariantCulture);
        long total = long.Parse(fields["price_total"], CultureInfo.InvariantCulture);
        string id = NextId();
        _buyOrders[id] = new BuyOrder(total / quantity, quantity);

        return Json($$"""{"success":1,"buy_orderid":"{{id}}"}""");
    }

    private HttpResponseMessage CancelBuyOrder(Dictionary<string, string> fields)
    {
        if (!_buyOrders.TryGetValue(fields["buy_orderid"], out BuyOrder? order))
        {
            return Json("""{"success":8}""");
        }

        order.Active = false;

        return Json("""{"success":1}""");
    }

    private HttpResponseMessage BuyOrderStatus(Dictionary<string, string> query)
    {
        return _buyOrders.TryGetValue(query["buy_orderid"], out BuyOrder? order)
            ? Json(
                $$"""{"success":1,"active":{{(order.Active ? 1 : 0)}},"purchased":{{order.Quantity - order.Remaining}},"quantity":"{{order.Quantity}}","quantity_remaining":"{{order.Remaining}}","purchases":[]}""")
            : Json("""{"success":8}""");
    }

    private HttpResponseMessage ConfirmationList(Dictionary<string, string> query)
    {
        if (!Signed(query, "conf"))
        {
            return Json("""{"success":false}""");
        }

        return Json(
            JsonSerializer.Serialize(
                new
                {
                    success = true,
                    conf = _confirmations.Select(confirmation => new
                    {
                        id = confirmation.Id,
                        nonce = confirmation.Nonce,
                        type = confirmation.Type,
                        creator_id = confirmation.CreatorId
                    })
                }));
    }

    private HttpResponseMessage ApproveConfirmation(Dictionary<string, string> query)
    {
        Confirmation? confirmation = _confirmations.SingleOrDefault(item => item.Id == query["cid"] && item.Nonce == query["ck"]);

        if (!Signed(query, "accept") || query["op"] != "allow" || confirmation is null)
        {
            return Json("""{"success":false}""");
        }

        _confirmations.Remove(confirmation);
        _approved.Add(confirmation.CreatorId);

        foreach (Listing listing in _listings.Where(listing => confirmation.Type == 3 && listing.Id == confirmation.CreatorId))
        {
            listing.Confirmed = true;
        }

        return Json("""{"success":true}""");
    }

    /// <summary>
    ///     A confirmation request signed for Steam's time, within the minute the clocks may disagree by.
    /// </summary>
    private bool Signed(Dictionary<string, string> query, string tag)
    {
        if (query.GetValueOrDefault("tag") != tag || query.GetValueOrDefault("a") != steamId ||
            !long.TryParse(query.GetValueOrDefault("t"), NumberStyles.None, CultureInfo.InvariantCulture, out long time) || Math.Abs(time - Now()) > 60)
        {
            return false;
        }

        byte[] input = [.. BitConverter.GetBytes(time).Reverse(), .. Encoding.UTF8.GetBytes(tag)];

        return query.GetValueOrDefault("k") == Convert.ToBase64String(HMACSHA1.HashData(Convert.FromBase64String(IdentitySecret), input));
    }

    private HttpResponseMessage SellItem(Dictionary<string, string> fields)
    {
        Asset? asset = _inventory.SingleOrDefault(item => item.Id == fields["assetid"]);

        if (asset is null || asset.UnlockAt > clock.GetUtcNow() || fields["amount"] != "1" || fields["contextid"] != Context)
        {
            return Json("""{"success":false}""");
        }

        string id = NextId();
        _inventory.Remove(asset);
        _listings.Add(new Listing(id, asset.Id, long.Parse(fields["price"], CultureInfo.InvariantCulture)));
        _confirmations.Add(new Confirmation(NextId(), NextId(), Type: 3, id));
        _history.Add(new HistoryEvent(ListedEvent, id, Now(), asset.Id, NewAssetId: null, PurchaseId: null, Paid: 0, Fee: 0, Received: 0));

        return Json("""{"success":true,"requires_confirmation":1,"needs_mobile_confirmation":true,"needs_email_confirmation":false,"email_domain":""}""");
    }

    private HttpResponseMessage RemoveListing(string id)
    {
        Listing? listing = _listings.SingleOrDefault(item => item.Id == id);

        if (listing is null)
        {
            return Json("""{"success":false}""");
        }

        string? returned = ReturnsUnderNewIds ? NextId() : null;
        _listings.Remove(listing);
        _confirmations.RemoveAll(confirmation => confirmation.Type == 3 && confirmation.CreatorId == id);
        _inventory.Add(new Asset(returned ?? listing.AssetId, clock.GetUtcNow()));
        _history.Add(new HistoryEvent(CancelledEvent, id, Now(), listing.AssetId, returned, PurchaseId: null, Paid: 0, Fee: 0, Received: 0));

        return Json("""{"success":true}""");
    }

    private HttpResponseMessage MyListings()
    {
        string rows = string.Concat(
            _listings.Select(listing =>
                $"""<div class="market_listing_row" id="mylisting_{listing.Id}"><a href="javascript:{(listing.Confirmed ? "RemoveMarketListing" : "CancelMarketListingConfirmation")}('mylisting', '{listing.Id}', {AppId}, '{Context}', '{listing.AssetId}')">Remove</a><span id="mylisting_{listing.Id}_name">item</span></div>"""));

        return Json(JsonSerializer.Serialize(new { success = true, pagesize = 100, total_count = _listings.Count, start = 0, results_html = rows }));
    }

    private HttpResponseMessage MyHistory(Dictionary<string, string> query)
    {
        int start = int.Parse(query["start"], CultureInfo.InvariantCulture);
        int count = int.Parse(query["count"], CultureInfo.InvariantCulture);
        HistoryEvent[] page = [.. Enumerable.Reverse(_history).Skip(start).Take(count)];
        var listings = new Dictionary<string, object>(StringComparer.Ordinal);
        var purchases = new Dictionary<string, object>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (HistoryEvent item in page)
        {
            // One listing has one asset; once cancelled, it names the returned item's new ID when Steam gave it one. The page
            // runs newest first, so the listing's latest event describes it.
            listings.TryAdd(
                item.ListingId,
                item is { Type: CancelledEvent, NewAssetId: { } returned }
                    ? new { listingid = item.ListingId, asset = (object)new { appid = AppId, contextid = Context, id = item.AssetId, amount = "1", new_id = returned } }
                    : new { listingid = item.ListingId, asset = (object)new { appid = AppId, contextid = Context, id = item.AssetId, amount = "1" } });
            descriptions.TryAdd(item.AssetId, new { market_hash_name = Item, amount = "1", original_amount = "1" });

            if (item.PurchaseId is { } purchase)
            {
                purchases[item.ListingId + "_" + purchase] = new
                {
                    listingid = item.ListingId,
                    purchaseid = purchase,
                    asset = new { appid = AppId, contextid = Context, id = item.AssetId, amount = "1", new_id = item.NewAssetId, new_contextid = Context },
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

        return Json(
            JsonSerializer.Serialize(
                new
                {
                    success = true,
                    pagesize = count,
                    total_count = _history.Count,
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
                    assets = new Dictionary<string, object> { ["730"] = new Dictionary<string, object> { [Context] = descriptions } }
                }));
    }

    private HttpResponseMessage InventoryPage()
    {
        DateTimeOffset now = clock.GetUtcNow();
        object[] descriptions =
        [
            .. _inventory.Select(asset => asset.UnlockAt > now
                ? new
                {
                    appid = AppId,
                    classid = asset.Id,
                    instanceid = "0",
                    market_hash_name = Item,
                    marketable = 0,
                    tradable = 0,
                    owner_descriptions = new[]
                    {
                        new
                        {
                            type = "html",
                            value = "Tradable/Marketable After " +
                                asset.UnlockAt.ToString("d MMM '@' h:mmtt", CultureInfo.InvariantCulture)
                                    .Replace("AM", "am", StringComparison.Ordinal)
                                    .Replace("PM", "pm", StringComparison.Ordinal)
                        }
                    }
                }
                : (object)new
                {
                    appid = AppId,
                    classid = asset.Id,
                    instanceid = "0",
                    market_hash_name = Item,
                    marketable = 1,
                    tradable = 1
                })
        ];

        return Json(
            JsonSerializer.Serialize(
                new
                {
                    success = 1,
                    total_inventory_count = _inventory.Count,
                    assets =
                        _inventory.Select(asset => new { appid = AppId, contextid = Context, assetid = asset.Id, classid = asset.Id, instanceid = "0", amount = "1" }),
                    descriptions
                }));
    }

    private sealed class BuyOrder(long price, int quantity)
    {
        public long Price { get; } = price;

        public int Quantity { get; } = quantity;

        public int Remaining { get; set; } = quantity;

        public bool Active { get; set; } = true;
    }

    private sealed class Listing(string id, string assetId, long sellerPrice)
    {
        public string Id { get; } = id;

        public string AssetId { get; } = assetId;

        public long SellerPrice { get; } = sellerPrice;

        public bool Confirmed { get; set; }
    }

    private sealed record Confirmation(string Id, string Nonce, int Type, string CreatorId);

    private sealed record Asset(string Id, DateTimeOffset UnlockAt);

    private sealed record HistoryEvent(
        int Type,
        string ListingId,
        long Seconds,
        string AssetId,
        string? NewAssetId,
        string? PurchaseId,
        long Paid,
        long Fee,
        long Received);
}