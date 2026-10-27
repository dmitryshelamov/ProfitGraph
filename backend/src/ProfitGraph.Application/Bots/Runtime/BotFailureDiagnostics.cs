using System.Text.RegularExpressions;

using ProfitGraph.Domain.Common;

namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     Describes why a bot's run failed in one short line that is safe for the log and the bot's feed (ADR 0029). The
///     message of an exception is included only when this application wrote it: a safety exception, a Domain exception, or an
///     <see cref="InvalidOperationException" /> or <see cref="ArgumentException" /> that code of ProfitGraph threw. Every
///     other exception, whatever its type, is reduced to its type name, because messages of the framework, the database
///     driver, the HTTP client and the venues may carry addresses, account data or the venue's raw answer. Long
///     token-like runs of characters are masked in what is included, so that a text that slipped through still holds no key.
/// </summary>
public static partial class BotFailureDiagnostics
{
    private const int MaxDepth = 3;
    private const int MaxMessageLength = 160;
    private const string OwnAssemblyPrefix = "ProfitGraph.";

    public static string Describe(Exception exception)
    {
        var parts = new List<string>();
        Exception? current = exception;

        for (var depth = 0; current is not null && depth < MaxDepth; depth++, current = current.InnerException)
        {
            parts.Add(IsAuthoredHere(current) ? $"{current.GetType().Name}: {Mask(current.Message)}" : current.GetType().Name);
        }

        return string.Join(" <- ", parts);
    }

    private static bool IsAuthoredHere(Exception exception)
    {
        return exception switch
        {
            OrderGatewaySafetyException or DomainException => true,
            _ => (exception.GetType() == typeof(InvalidOperationException) || exception.GetType() == typeof(ArgumentException)) &&
                exception.TargetSite?.DeclaringType?.Assembly.GetName().Name?.StartsWith(OwnAssemblyPrefix, StringComparison.Ordinal) == true
        };
    }

    private static string Mask(string message)
    {
        string line = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string masked = TokenLike().Replace(line, "***");

        return masked.Length <= MaxMessageLength ? masked : masked[..MaxMessageLength];
    }

    [GeneratedRegex("[A-Za-z0-9+/_=-]{20,}")]
    private static partial Regex TokenLike();
}
