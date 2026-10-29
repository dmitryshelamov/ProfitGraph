using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Application.Scanner;

internal static class ScanSources
{
    public const string VenueNotFound = "scanner_venue_not_found";

    /// <summary>
    ///     The scan source of the venue a request names; a venue without a scanner is missing.
    /// </summary>
    public static IMarketScanSource Find(this IEnumerable<IMarketScanSource> sources, string venue)
    {
        return sources.FirstOrDefault(source => source.Venue.Value == venue) ??
            throw new ResourceNotFoundException("The venue has no market scanner.", VenueNotFound);
    }
}