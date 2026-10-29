using System.Collections.ObjectModel;

namespace ProfitGraph.Engine.Blocks;

/// <summary>
///     One block of a strategy. A grid block keeps its settings by name in <see cref="Settings" />; a cancel block either
///     cancels a tag or names a grid block in <see cref="Grid" /> (ADR 0058).
/// </summary>
public sealed record StrategyBlock
{
    public string Id { get; }
    public string Kind { get; }
    public string? DisplayName { get; }
    public string? Source { get; }
    public string? Join { get; }
    public IReadOnlyList<StrategyCheck> Checks { get; }
    public string? OrderKind { get; }
    public string? Side { get; }
    public ValueExpression? Tag { get; }
    public ValueExpression? Amount { get; }
    public ValueExpression? Quantity { get; }
    public ValueExpression? Price { get; }
    public string? SizeMode { get; }
    public string? MinimumOrderPolicy { get; }
    public IReadOnlyDictionary<string, ValueExpression> Settings { get; }
    public string? Grid { get; }

    public StrategyBlock(
        string id,
        string kind,
        string? displayName = null,
        string? source = null,
        string? join = null,
        IEnumerable<StrategyCheck>? checks = null,
        string? orderKind = null,
        string? side = null,
        ValueExpression? tag = null,
        ValueExpression? amount = null,
        ValueExpression? quantity = null,
        ValueExpression? price = null,
        string? sizeMode = null,
        string? minimumOrderPolicy = null,
        IReadOnlyDictionary<string, ValueExpression>? settings = null,
        string? grid = null)
    {
        Id = id;
        Kind = kind;
        DisplayName = displayName;
        Source = source;
        Join = join;
        Checks = Array.AsReadOnly((checks ?? Array.Empty<StrategyCheck>()).ToArray());
        OrderKind = orderKind;
        Side = side;
        Tag = tag;
        Amount = amount;
        Quantity = quantity;
        Price = price;
        SizeMode = sizeMode;
        MinimumOrderPolicy = minimumOrderPolicy == "Reject" ? null : minimumOrderPolicy;
        Settings = new ReadOnlyDictionary<string, ValueExpression>(
            new Dictionary<string, ValueExpression>(settings ?? new Dictionary<string, ValueExpression>(), StringComparer.Ordinal));
        Grid = grid;
    }
}