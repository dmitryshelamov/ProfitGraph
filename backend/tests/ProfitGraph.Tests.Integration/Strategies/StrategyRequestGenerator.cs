using System.Globalization;

namespace ProfitGraph.Tests.Integration.Strategies;

/// <summary>
///     Deterministic block definition with a named SMA value and a shared period parameter.
/// </summary>
internal static class StrategyRequestGenerator
{
    public static object Create(int period = 2)
    {
        return new
        {
            name = "SMA strategy",
            definition = new
            {
                parameters = new[] { new { name = "period", valueType = "Integer", defaultValue = period.ToString(CultureInfo.InvariantCulture) } },
                values = new[]
                {
                    new
                    {
                        name = "sma", expression = new
                        {
                            kind = "SMA", arguments = new Dictionary<string, object>
                            {
                                ["in"] = new { kind = "candle", field = "Close" },
                                ["period"] = new { kind = "parameter", name = "period" }
                            }
                        }
                    }
                },
                blocks = new object[]
                {
                    new { id = "source", kind = "event", source = "OnCandleClosed" },
                    new
                    {
                        id = "signal", kind = "condition", join = "all", checks = new[]
                        {
                            new
                            {
                                type = "Compare", operation = ">", operands = new Dictionary<string, object>
                                {
                                    ["a"] = new { kind = "value", name = "sma" },
                                    ["b"] = new { kind = "constant", valueType = "Price", number = "0" }
                                }
                            }
                        }
                    }
                },
                wires = new[] { new { from = new { block = "source", exit = "out" }, to = "signal" } }
            },
            layout = new { positions = new Dictionary<string, object> { ["source"] = new { x = 10d, y = 20d }, ["signal"] = new { x = 100d, y = 20d } } }
        };
    }
}