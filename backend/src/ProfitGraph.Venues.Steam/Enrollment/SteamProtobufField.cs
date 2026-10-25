namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     One field of a protocol buffer message: its number, its bytes when length-delimited or fixed, and its integer value.
/// </summary>
internal sealed record SteamProtobufField(int Number, byte[] Data, ulong Integer);