namespace ProfitGraph.Application.Bots;

public static class BotErrorCodes
{
    public const string NotFound = "bot_not_found";
    public const string AlertNotFound = "bot_alert_not_found";
    public const string LeaseLost = "bot_lease_lost";
    public const string FeeRatesUnavailable = "fee_rates_unavailable";
    public const string ParametersMismatch = "parameters_mismatch";
    public const string ParametersIncompatible = "parameters_incompatible";
    public const string AllocationBelowCommitted = "allocation_below_committed";
    public const string MissingCheckpoint = "missing_checkpoint";
    public const string CredentialInUse = "exchange_credential_in_use";
    public const string OrderIntentNotFound = "bot_order_intent_not_found";
    public const string AbsenceNotConfirmable = "order_absence_not_confirmable";
    public const string GridNotSupportedByVenue = "grid_not_supported_by_venue";
    public const string GridExceedsVenueOrders = "grid_exceeds_venue_orders";
    public const string GridProfitBelowFees = "grid_profit_below_fees";
}