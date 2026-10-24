using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     An account operation that the account rules forbid, such as blocking yourself or the last active Owner.
/// </summary>
public sealed class AccountRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidLogin = "invalid_login";
    public const string CannotBlockSelf = "cannot_block_self";
    public const string LastOwner = "last_owner";
}