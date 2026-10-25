namespace ProfitGraph.Venues.Steam.Inventory;

/// <summary>
///     The inventory could not be read whole; nothing may be listed from an incomplete inventory.
/// </summary>
internal sealed class SteamInventoryException(string message) : Exception(message);