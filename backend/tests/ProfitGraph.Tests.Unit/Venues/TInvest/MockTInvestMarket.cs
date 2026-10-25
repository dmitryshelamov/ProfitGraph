using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     Invented MOEX shares and their exchange candles behind the mock Invest API: <c>ShareBy</c>, <c>FindInstrument</c> and
///     <c>GetCandles</c>, which refuses <c>limit</c> together with the exchange source (<c>30220</c>) like the real API.
/// </summary>
internal sealed class MockTInvestMarket
{
    private readonly Dictionary<string, List<HistoricCandle>> _candles = new Dictionary<string, List<HistoricCandle>>();
    private readonly Dictionary<string, Share> _shares = new Dictionary<string, Share>();

    public List<GetCandlesRequest> CandleRequests { get; } = [];

    public MockTInvestMarket(MockTInvestApi api)
    {
        api.On<InstrumentRequest, ShareResponse>(InstrumentsService.Descriptor, "ShareBy", ShareBy);
        api.On<FindInstrumentRequest, FindInstrumentResponse>(InstrumentsService.Descriptor, "FindInstrument", Find);
        api.On<GetCandlesRequest, GetCandlesResponse>(MarketDataService.Descriptor, "GetCandles", Candles);
    }

    public Share AddShare(string ticker, int lot, decimal increment, DateTimeOffset firstMinute, string currency = "rub", bool apiTrading = true)
    {
        var share = new Share
        {
            Ticker = ticker,
            ClassCode = "TQBR",
            Uid = Guid.NewGuid().ToString("D"),
            Name = "Invented " + ticker,
            Lot = lot,
            Currency = currency,
            ApiTradeAvailableFlag = apiTrading,
            Exchange = "moex_mrng_evng_e_wknd_dlr",
            MinPriceIncrement = Quotation(increment),
            First1MinCandleDate = Timestamp.FromDateTimeOffset(firstMinute),
            First1DayCandleDate = Timestamp.FromDateTimeOffset(firstMinute)
        };
        _shares[ticker] = share;
        _candles[share.Uid] = [];

        return share;
    }

    public void AddCandle(string ticker, DateTimeOffset openTime, decimal close, long lots = 5, CandleSource source = CandleSource.Exchange)
    {
        _candles[_shares[ticker].Uid]
            .Add(
                new HistoricCandle
                {
                    Time = Timestamp.FromDateTimeOffset(openTime),
                    Open = Quotation(close),
                    High = Quotation(close + 1m),
                    Low = Quotation(close - 1m),
                    Close = Quotation(close),
                    Volume = lots,
                    IsComplete = true,
                    CandleSource = source
                });
    }

    private static Quotation Quotation(decimal value)
    {
        var units = decimal.ToInt64(decimal.Truncate(value));

        return new Quotation { Units = units, Nano = decimal.ToInt32((value - units) * 1_000_000_000m) };
    }

    private ShareResponse ShareBy(InstrumentRequest request)
    {
        return _shares.TryGetValue(request.Id, out Share? share) && share.ClassCode == request.ClassCode
            ? new ShareResponse { Instrument = share }
            : throw MockTInvestApi.Refusal(StatusCode.NotFound, "50002");
    }

    private FindInstrumentResponse Find(FindInstrumentRequest request)
    {
        var response = new FindInstrumentResponse();
        response.Instruments.AddRange(
            _shares.Values
                .Where(share => share.Ticker.Contains(request.Query, StringComparison.OrdinalIgnoreCase))
                .Select(share => new InstrumentShort
                {
                    Ticker = share.Ticker,
                    ClassCode = share.ClassCode,
                    Uid = share.Uid,
                    Name = share.Name,
                    Lot = share.Lot,
                    ApiTradeAvailableFlag = share.ApiTradeAvailableFlag,
                    InstrumentKind = InstrumentType.Share
                }));

        return response;
    }

    private GetCandlesResponse Candles(GetCandlesRequest request)
    {
        lock (CandleRequests)
        {
            CandleRequests.Add(request);
        }

        if (request.HasLimit && request.CandleSourceType == GetCandlesRequest.Types.CandleSource.Exchange)
        {
            throw MockTInvestApi.Refusal(StatusCode.InvalidArgument, "30220");
        }

        var response = new GetCandlesResponse();
        response.Candles.AddRange(_candles[request.InstrumentId].Where(candle => candle.Time >= request.From && candle.Time < request.To));

        return response;
    }
}