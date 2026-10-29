using System.Globalization;

namespace ProfitGraph.Engine.Grids;

/// <summary>
///     The tags of a grid's orders (ADR 0058, section 2): one per level and side, and one for the stop's market sale, so the
///     order book keeps one active order per tag.
/// </summary>
public static class GridTags
{
    public const string StopRole = "stop";
    public const string BuyRole = "buy";
    public const string SellRole = "sell";

    public static string Prefix(string gridNodeId)
    {
        return "grid." + gridNodeId + ".";
    }

    public static string Buy(string gridNodeId, int level)
    {
        return Prefix(gridNodeId) + level.ToString(CultureInfo.InvariantCulture) + "." + BuyRole;
    }

    public static string Sell(string gridNodeId, int level)
    {
        return Prefix(gridNodeId) + level.ToString(CultureInfo.InvariantCulture) + "." + SellRole;
    }

    public static string Stop(string gridNodeId)
    {
        return Prefix(gridNodeId) + StopRole;
    }

    /// <summary>
    ///     The level and role of a grid order's tag; the stop has level 0. False for a tag that is not this grid's.
    /// </summary>
    public static bool TryParse(string gridNodeId, string? tag, out int level, out string role)
    {
        level = 0;
        role = string.Empty;
        string prefix = Prefix(gridNodeId);

        if (tag is null || !tag.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string rest = tag[prefix.Length..];

        if (rest == StopRole)
        {
            role = StopRole;

            return true;
        }

        int dot = rest.IndexOf(value: '.', StringComparison.Ordinal);

        if (dot <= 0 || !int.TryParse(rest[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out level) || level < 1 ||
            rest[(dot + 1)..] is not (BuyRole or SellRole))
        {
            return false;
        }

        role = rest[(dot + 1)..];

        return true;
    }
}