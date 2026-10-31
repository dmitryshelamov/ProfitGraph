namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     The weighted counts of the card odds pooled into one level of <see cref="ScanCardOddsModel" />: a kind, its bands or its bands in
///     one market band. Odds stored before ADR 0073 count their closed trades as independent and their chance band's middle as their
///     geometry.
/// </summary>
internal sealed class ScanCardOddsPool
{
    private decimal _geometry;
    private decimal _slip;
    private decimal _heldR;
    private decimal _heldTrades;
    private decimal _missedR;
    private decimal _missedTrades;

    public decimal Cards { get; private set; }
    public decimal Filled { get; private set; }
    public decimal Targets { get; private set; }
    public decimal Stops { get; private set; }
    public decimal Held { get; private set; }
    public decimal Effective { get; private set; }
    public decimal SlipStops { get; private set; }

    /// <summary>
    ///     The closed trades pooled, unweighted.
    /// </summary>
    public int Trades { get; private set; }

    /// <summary>
    ///     The closed trades' mean random walk chance; null without a closed trade.
    /// </summary>
    public decimal? Geometry => Filled > 0m ? _geometry / Filled : null;

    /// <summary>
    ///     How far beyond their stops the stopped trades left on average, in NATRs; null without one that knows it.
    /// </summary>
    public decimal? Slip => SlipStops > 0m ? _slip / SlipStops : null;

    /// <summary>
    ///     The mean R of the trades held to the horizon; null without one that knows it.
    /// </summary>
    public decimal? HeldR => _heldTrades > 0m ? _heldR / _heldTrades : null;

    /// <summary>
    ///     The mean R of the trades that missed their target; null without one.
    /// </summary>
    public decimal? MissedR => _missedTrades > 0m ? _missedR / _missedTrades : null;

    public void Add(ScanCardOdds odds, decimal weight)
    {
        int held = odds.Filled - odds.Targets - odds.Stops;
        int missed = odds.Filled - odds.Targets;
        Cards += weight * odds.Cards;
        Filled += weight * odds.Filled;
        Targets += weight * odds.Targets;
        Stops += weight * odds.Stops;
        Held += weight * held;
        Effective += weight * Math.Min(odds.EffectiveTrades ?? odds.Filled, odds.Filled);
        _geometry += weight * odds.Filled * (odds.Geometry ?? ScanCardBands.ChanceCenter(odds.ChanceBand));
        Trades += odds.Filled;

        if (odds.StopSlip is { } slip && odds.Stops > 0)
        {
            _slip += weight * odds.Stops * slip;
            SlipStops += weight * odds.Stops;
        }

        if (odds.HeldMeanR is { } heldR && held > 0)
        {
            _heldR += weight * held * heldR;
            _heldTrades += weight * held;
        }

        if (odds.MissedMeanR is { } missedR && missed > 0)
        {
            _missedR += weight * missed * missedR;
            _missedTrades += weight * missed;
        }
    }
}