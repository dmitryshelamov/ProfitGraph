namespace ProfitGraph.Api.MarketData.Contracts;

public sealed record InstrumentAppearanceResponse(
    string? ImageUrl,
    string? Kind,
    string? NameColor,
    string? BackgroundColor,
    IReadOnlyList<string> Description);
