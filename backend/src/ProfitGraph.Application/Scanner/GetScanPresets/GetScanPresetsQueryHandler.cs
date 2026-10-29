using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.GetScanPresets;

internal sealed class GetScanPresetsQueryHandler(IEnumerable<IMarketScanSource> sources, IScanPresetRepository presets)
    : IRequestHandler<GetScanPresetsQuery, IReadOnlyList<ScanPresetInternal>>
{
    public async Task<IReadOnlyList<ScanPresetInternal>> Handle(GetScanPresetsQuery query, CancellationToken cancellationToken)
    {
        IMarketScanSource source = sources.Find(query.Venue);
        IReadOnlyList<ScanPreset> own = await presets.List(new UserId(query.UserId), source.Venue, cancellationToken);

        return [.. source.BuiltInPresets, .. own.OrderBy(preset => preset.Name.Value, StringComparer.OrdinalIgnoreCase).Select(preset => preset.ToInternal())];
    }
}