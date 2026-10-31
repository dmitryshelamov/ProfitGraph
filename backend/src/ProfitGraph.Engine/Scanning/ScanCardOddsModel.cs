using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A card's odds from the cards of one or more preset backtests (ADR 0073). The chance starts from the random walk's
///     <c>1 / (1 + RR)</c> and moves by what the backtests measured, level by level: the card's kind (timeframe, scenario and direction),
///     its bands and its bands in the current market band. At each level the measured share of targets is shrunk towards what the level
///     above predicts by <c>priorTrades</c> imagined trades, <c>(targets + k × prior) / (trades + k)</c>, with the trades counted as the
///     independent trades they are worth, and the level's shift over its own mean random walk chance is kept in log-odds. The card gets
///     that shift on its own <c>1 / (1 + RR)</c>, so cards of one band with different targets get different chances. The cautious chance is
///     the 10th percentile of the last level's share. A stopped trade loses one risk plus its band's slip beyond the stop over the card's
///     stop distance and costs in NATRs, so nearer stops lose more. A kind with fewer than <c>minimumTrades</c> closed trades gives no
///     odds. Deterministic for identical inputs.
/// </summary>
public sealed class ScanCardOddsModel
{
    /// <summary>
    ///     The standard normal quantile of the 10th percentile.
    /// </summary>
    private const double LowQuantile = 1.2815515655446004;

    /// <summary>
    ///     The nearest a chance comes to 0 or 1, so that its log-odds stay finite.
    /// </summary>
    private const double Edge = 0.0001;

    private const int Decimals = 6;
    private const decimal Percent = 100m;

    private readonly ScanCardOddsPool _all = new ScanCardOddsPool();
    private readonly Dictionary<(string, ScanScenario, PositionDirection, int, int), ScanCardOddsPool> _bands = [];
    private readonly Dictionary<(string, ScanScenario, PositionDirection), ScanCardOddsPool> _kinds = [];
    private readonly Dictionary<(string, ScanScenario, PositionDirection, int, int, int), ScanCardOddsPool> _markets = [];
    private readonly int _minimumTrades;
    private readonly decimal _prior;

    public ScanCardOddsModel(IEnumerable<ScanWeightedOdds> sources, decimal priorTrades, int minimumTrades)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priorTrades);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumTrades);

        _prior = priorTrades;
        _minimumTrades = minimumTrades;

        foreach (ScanWeightedOdds source in sources)
        {
            if (source.Weight <= 0m)
            {
                continue;
            }

            foreach (ScanCardOdds odds in source.Odds)
            {
                _all.Add(odds, source.Weight);
                Pool(_kinds, (odds.Timeframe, odds.Scenario, odds.Direction)).Add(odds, source.Weight);
                Pool(_bands, (odds.Timeframe, odds.Scenario, odds.Direction, odds.ChanceBand, odds.StopBand)).Add(odds, source.Weight);

                if (odds.MarketBand is { } market)
                {
                    Pool(_markets, (odds.Timeframe, odds.Scenario, odds.Direction, odds.ChanceBand, odds.StopBand, market)).Add(odds, source.Weight);
                }
            }
        }
    }

    /// <summary>
    ///     The odds of <paramref name="idea" /> proposed on <paramref name="timeframe" />'s levels with the pair's NATR, the reference
    ///     pair's market trend and the pair's costs; null without the NATR or enough trades of its kind.
    /// </summary>
    public ScanCardChance? Of(string timeframe, ScanTradeIdea idea, decimal? natrPercent, decimal? marketTrend, ScanTradeCosts costs)
    {
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(costs);

        if (ScanCardBands.StopNatr(idea, natrPercent) is not { } stopNatr)
        {
            return null;
        }

        return Of(
            timeframe,
            idea.Scenario,
            idea.Direction,
            ScanCardBands.Chance(idea.RewardRisk),
            ScanCardBands.Geometry(idea.RewardRisk),
            stopNatr,
            costs.AtStop * Percent / natrPercent!.Value,
            ScanCardBands.Market(marketTrend),
            ScanCardFollow.ResultR(idea, idea.Target, ScanTradeExit.Target, costs));
    }

    /// <summary>
    ///     The odds of a card a backtest followed; null without its NATR or enough trades of its kind.
    /// </summary>
    public ScanCardChance? Of(ScanCardTrade card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return card is { StopNatr: { } stopNatr, CostNatr: { } costNatr }
            ? Of(card.Timeframe, card.Scenario, card.Direction, card.ChanceBand, card.Geometry, stopNatr, costNatr, card.MarketBand, card.TargetR)
            : null;
    }

    /// <summary>
    ///     The share of targets of the card's kind and bands as measured, pooled over market bands, the odds before ADR 0073; null with
    ///     fewer than the minimum trades in the bands or without the card's NATR.
    /// </summary>
    public decimal? Banded(ScanCardTrade card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (card.StopBand is not { } stopBand ||
            !_bands.TryGetValue((card.Timeframe, card.Scenario, card.Direction, card.ChanceBand, stopBand), out ScanCardOddsPool? band) ||
            band.Trades < _minimumTrades)
        {
            return null;
        }

        return Math.Round(band.Targets / band.Filled, Decimals);
    }

    private ScanCardChance? Of(
        string timeframe,
        ScanScenario scenario,
        PositionDirection direction,
        int chanceBand,
        decimal geometry,
        decimal stopNatr,
        decimal costNatr,
        int? marketBand,
        decimal targetR)
    {
        if (!_kinds.TryGetValue((timeframe, scenario, direction), out ScanCardOddsPool? kind) || kind.Trades < _minimumTrades)
        {
            return null;
        }

        int stopBand = ScanCardBands.Stop(stopNatr);
        ScanCardOddsPool? band = _bands.GetValueOrDefault((timeframe, scenario, direction, chanceBand, stopBand));
        ScanCardOddsPool? market = marketBand is { } value ? _markets.GetValueOrDefault((timeframe, scenario, direction, chanceBand, stopBand, value)) : null;
        ScanCardOddsLevel level = Step(kind, shift: 0d);

        if (band is { Filled: > 0m })
        {
            level = Step(band, level.Shift);
        }

        if (market is { Filled: > 0m })
        {
            level = Step(market, level.Shift);
        }

        double own = Logit((double)geometry);
        double spread = LowQuantile * Math.Sqrt(level.Share * (1d - level.Share) / (level.Strength + 1d));
        decimal chance = Round(Sigmoid(own + level.Shift));
        decimal low = Round(Sigmoid(own + Logit(level.Share - spread) - Logit(level.Geometry)));
        decimal fill = Math.Round(Fill(kind, band), Decimals);
        decimal missed = Math.Round(Missed(kind, band, stopNatr + costNatr), Decimals);

        return new ScanCardChance(
            chance,
            low,
            fill,
            missed,
            Math.Round(fill * (chance * targetR + (1m - chance) * missed), Decimals),
            Math.Round(fill * (low * targetR + (1m - low) * missed), Decimals),
            band?.Trades ?? 0);
    }

    /// <summary>
    ///     The level of <paramref name="pool" /> under a level above that shifts the random walk's log-odds by <paramref name="shift" />.
    /// </summary>
    private ScanCardOddsLevel Step(ScanCardOddsPool pool, double shift)
    {
        double geometry = Clamp((double)pool.Geometry!.Value);
        double prior = Sigmoid(Logit(geometry) + shift);
        var worth = (double)(pool.Effective / pool.Filled);
        double trades = (double)pool.Filled * worth;
        double targets = (double)pool.Targets * worth;
        double strength = trades + (double)_prior;
        double share = Clamp((targets + (double)_prior * prior) / strength);

        return new ScanCardOddsLevel(Logit(share) - Logit(geometry), share, strength, geometry);
    }

    /// <summary>
    ///     The share of entries that fill in the bands, shrunk towards the kind's.
    /// </summary>
    private decimal Fill(ScanCardOddsPool kind, ScanCardOddsPool? band)
    {
        decimal kindFill = kind.Cards > 0m ? kind.Filled / kind.Cards : 1m;

        return band is null ? kindFill : (band.Filled + _prior * kindFill) / (band.Cards + _prior);
    }

    /// <summary>
    ///     The R of a trade that misses its target: stopped ones lose a risk and the slip beyond the stop over the card's
    ///     <paramref name="riskNatr" /> (its stop distance and costs in NATRs), the others what the trades held to the horizon made. Odds
    ///     stored before ADR 0073 know no slip, so without one the bands' measured mean of the missed trades stands.
    /// </summary>
    private decimal Missed(ScanCardOddsPool kind, ScanCardOddsPool? band, decimal riskNatr)
    {
        if (_all.Slip is not { } usual || riskNatr <= 0m)
        {
            return band?.MissedR ?? kind.MissedR ?? -1m;
        }

        decimal slip = ((band?.Slip ?? 0m) * (band?.SlipStops ?? 0m) + _prior * usual) / ((band?.SlipStops ?? 0m) + _prior);
        ScanCardOddsPool shares = band is { } own && own.Stops + own.Held > 0m ? own : kind;
        decimal stopped = shares.Stops + shares.Held > 0m ? shares.Stops / (shares.Stops + shares.Held) : 1m;
        decimal held = band?.HeldR ?? kind.HeldR ?? 0m;

        return stopped * (-1m - slip / riskNatr) + (1m - stopped) * held;
    }

    private static ScanCardOddsPool Pool<TKey>(Dictionary<TKey, ScanCardOddsPool> pools, TKey key)
        where TKey : notnull
    {
        if (!pools.TryGetValue(key, out ScanCardOddsPool? pool))
        {
            pool = new ScanCardOddsPool();
            pools[key] = pool;
        }

        return pool;
    }

    private static double Clamp(double chance)
    {
        return Math.Clamp(chance, Edge, 1d - Edge);
    }

    private static double Logit(double chance)
    {
        double clamped = Clamp(chance);

        return Math.Log(clamped / (1d - clamped));
    }

    private static double Sigmoid(double value)
    {
        return 1d / (1d + Math.Exp(-value));
    }

    private static decimal Round(double chance)
    {
        return Math.Round((decimal)Clamp(chance), Decimals);
    }
}