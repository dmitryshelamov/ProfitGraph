namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Where an attempt of an engine order stands (ADR 0023). <see cref="Planned" /> is recorded before the place request
///     may leave the process and is never at T-Invest; <see cref="Sent" /> may be; <see cref="Unknown" /> had no clear
///     answer and is resolved only by looking it up by its request ID.
/// </summary>
internal enum TInvestOrderAttemptState
{
    Planned,
    Sent,
    Active,
    Final,
    Unknown
}