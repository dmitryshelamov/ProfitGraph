namespace ProfitGraph.Application.MarketData.Contracts;

/// <summary>
///     How a venue shows an instrument to people: a picture, a short kind label and description lines. Display only, never
///     used for trading. <see cref="ImageUrl" /> is an https address; the colors are six hex digits without a hash.
/// </summary>
public sealed record InstrumentAppearanceInternal(
    string? ImageUrl,
    string? Kind,
    string? NameColor,
    string? BackgroundColor,
    IReadOnlyList<string> Description);
