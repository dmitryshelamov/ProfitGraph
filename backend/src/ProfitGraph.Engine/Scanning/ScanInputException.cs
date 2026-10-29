using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     Свечи сканера не по порядку, не на сетке интервала или с пропуском: метрики по ним не считаются.
/// </summary>
public sealed class ScanInputException(string message) : EngineException(message);