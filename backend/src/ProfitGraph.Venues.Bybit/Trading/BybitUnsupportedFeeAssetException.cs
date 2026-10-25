namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Bybit charged a fee in an asset the engine account cannot convert exactly; the bot must degrade with
///     <c>UnsupportedFeeAsset</c>.
/// </summary>
internal sealed class BybitUnsupportedFeeAssetException() : Exception("Bybit reported a fee in an unsupported asset.");
