namespace ProfitGraph.Application.Bots;

public static class BotErrorCodes
{
    public const string NotFound = "bot_not_found";
    public const string LeaseLost = "bot_lease_lost";
    public const string FeeRatesUnavailable = "fee_rates_unavailable";
    public const string ParametersMismatch = "parameters_mismatch";
    public const string ParametersIncompatible = "parameters_incompatible";
    public const string AllocationBelowCommitted = "allocation_below_committed";
    public const string MissingCheckpoint = "missing_checkpoint";
    public const string CredentialInUse = "exchange_credential_in_use";
}