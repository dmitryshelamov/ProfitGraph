using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner;

public interface IScanAlertWatchRepository
{
    Task<ScanAlertWatch?> GetForUpdate(ScanPresetId presetId, CancellationToken cancellationToken);

    /// <summary>
    ///     The watches of the venue's presets, locked for the snapshot check.
    /// </summary>
    Task<IReadOnlyList<ScanAlertWatch>> ListForUpdate(VenueId venue, CancellationToken cancellationToken);

    /// <summary>
    ///     The presets of the owner whose alerts are on.
    /// </summary>
    Task<IReadOnlySet<ScanPresetId>> ListPresets(UserId ownerId, CancellationToken cancellationToken);

    Task Add(ScanAlertWatch watch, CancellationToken cancellationToken);

    Task Update(ScanAlertWatch watch, CancellationToken cancellationToken);

    Task Delete(ScanPresetId presetId, CancellationToken cancellationToken);
}