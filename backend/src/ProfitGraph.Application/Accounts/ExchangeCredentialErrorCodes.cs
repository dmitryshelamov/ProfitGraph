namespace ProfitGraph.Application.Accounts;

public static class ExchangeCredentialErrorCodes
{
    public const string NotFound = "exchange_credential_not_found";
    public const string Unavailable = "credentials_unavailable";
    public const string InputUnsupported = "credential_input_unsupported";
    public const string AccountsUnsupported = "credential_accounts_unsupported";
    public const string AccountsUnavailable = "venue_accounts_unavailable";
    public const string AccountIneligible = "venue_account_ineligible";
    public const string AccountExists = "exchange_credential_account_exists";
    public const string AuthenticatorUnavailable = "authenticator_unavailable";
    public const string EnrollmentUnsupported = "credential_enrollment_unsupported";
}