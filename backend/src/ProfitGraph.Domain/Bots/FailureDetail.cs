namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The diagnostic text of a bot's failure: one line of at most <see cref="MaxLength" /> characters, so that an oversized
///     text never reaches the outbox or the feed.
/// </summary>
internal static class FailureDetail
{
    public const int MaxLength = 300;

    public static string? Limit(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return null;
        }

        string line = string.Join(' ', detail.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return line.Length <= MaxLength ? line : line[..MaxLength];
    }
}
