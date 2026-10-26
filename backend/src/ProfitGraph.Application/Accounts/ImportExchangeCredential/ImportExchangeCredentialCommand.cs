using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.ImportExchangeCredential;

/// <summary>
///     Stores a credential whose fields the venue declares (ADR 0022, C6), for a venue that does not take credentials from
///     the web form; its module's operator command sends it. The public hint is four characters the owner may see, such as
///     the end of an account number; the optional public identifier is shown in full. Values are never printed.
/// </summary>
public sealed record ImportExchangeCredentialCommand(
    string OwnerLogin,
    string Venue,
    string Label,
    string PublicHint,
    IReadOnlyDictionary<string, string> Fields,
    string? PublicId = null)
    : ICommand<ExchangeCredentialInternal>
{
    public override string ToString()
    {
        return $"ImportExchangeCredentialCommand {{ OwnerLogin = {OwnerLogin}, Venue = {Venue}, Redacted }}";
    }
}