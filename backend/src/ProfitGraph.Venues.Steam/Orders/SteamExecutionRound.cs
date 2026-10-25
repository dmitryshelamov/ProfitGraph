using ProfitGraph.Venues.Steam.History;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     The receipts of one round of <see cref="SteamExecutions" /> and what is attributed so far.
/// </summary>
internal sealed class SteamExecutionRound(Dictionary<string, SteamMarketReceipt> all, List<SteamAttribution> attributed)
{
    public IEnumerable<SteamMarketReceipt> Free => all.Values.Where(receipt => attributed.All(attribution => attribution.ReceiptId != receipt.Id));

    public IEnumerable<SteamMarketReceipt> Of(string clientOrderId)
    {
        return attributed.Where(attribution => attribution.ClientOrderId == clientOrderId)
            .Select(attribution => all.GetValueOrDefault(attribution.ReceiptId))
            .OfType<SteamMarketReceipt>();
    }

    public void Add(IEnumerable<SteamAttribution> added)
    {
        attributed.AddRange(added);
    }
}