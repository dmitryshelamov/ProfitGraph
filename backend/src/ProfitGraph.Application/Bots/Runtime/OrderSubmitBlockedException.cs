namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     The submit may not leave the process now: the bot no longer runs, trading is halted, or another runtime holds it.
/// </summary>
public sealed class OrderSubmitBlockedException() : Exception("The submit is blocked.");