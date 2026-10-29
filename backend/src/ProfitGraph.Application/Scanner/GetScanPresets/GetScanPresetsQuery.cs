using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.GetScanPresets;

/// <summary>
///     The venue's built-in presets first, then the user's own by name.
/// </summary>
public sealed record GetScanPresetsQuery(Guid UserId, string Venue) : IQuery<IReadOnlyList<ScanPresetInternal>>;