using MediatR;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.GetScanPresets;

internal sealed class GetScanPresetsQueryHandler(IEnumerable<IMarketScanSource> sources, IScanPresetRepository presets, IScanAlertWatchRepository watches)
    : IRequestHandler<GetScanPresetsQuery, IReadOnlyList<ScanPresetInternal>>
{
    public async Task<IReadOnlyList<ScanPresetInternal>> Handle(GetScanPresetsQuery query, CancellationToken cancellationToken)
    {
        IMarketScanSource source = sources.Find(query.Venue);
        var owner = new UserId(query.UserId);
        IReadOnlyList<ScanPreset> own = await presets.List(owner, source.Venue, cancellationToken);
        IReadOnlySet<ScanPresetId> alerting = await watches.ListPresets(owner, cancellationToken);

        return
        [
            .. source.BuiltInPresets,
            .. own.OrderBy(preset => preset.Name.Value, StringComparer.OrdinalIgnoreCase).Select(preset => preset.ToInternal(alerting.Contains(preset.Id)))
        ];
    }
}