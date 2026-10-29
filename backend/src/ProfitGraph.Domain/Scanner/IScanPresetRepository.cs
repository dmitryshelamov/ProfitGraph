using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Domain.Scanner;

public interface IScanPresetRepository
{
    /// <summary>
    ///     The owner's preset, locked for update; another user's preset is missing.
    /// </summary>
    Task<ScanPreset?> GetForUpdate(ScanPresetId id, UserId ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScanPreset>> List(UserId ownerId, VenueId venue, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether another preset of the owner on the venue has this name, ignoring case.
    /// </summary>
    Task<bool> NameTaken(UserId ownerId, VenueId venue, ScanPresetName name, ScanPresetId? except, CancellationToken cancellationToken);

    Task Add(ScanPreset preset, CancellationToken cancellationToken);

    Task Update(ScanPreset preset, CancellationToken cancellationToken);

    Task<bool> Delete(ScanPresetId id, UserId ownerId, CancellationToken cancellationToken);
}