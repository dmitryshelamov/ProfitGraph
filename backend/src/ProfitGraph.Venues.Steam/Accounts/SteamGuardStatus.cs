namespace ProfitGraph.Venues.Steam.Accounts;

/// <summary>
///     Where a Steam account's authenticator stands (Profit Fox Designer ADR 0083). Only <see cref="Ready" /> may list items.
/// </summary>
internal enum SteamGuardStatus
{
    /// <summary>
    ///     Imported or rechecked; identity, market eligibility and wallet are not verified yet.
    /// </summary>
    Checking,

    /// <summary>
    ///     A new authenticator is added but not activated yet.
    /// </summary>
    PendingActivation,

    /// <summary>
    ///     Activated recently; Steam holds market sales until the authenticator has aged.
    /// </summary>
    CoolingDown,

    Ready,

    /// <summary>
    ///     Steam refuses market use on this account.
    /// </summary>
    Restricted
}