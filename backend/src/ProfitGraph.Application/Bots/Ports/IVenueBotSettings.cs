using System.Text.Json;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     A venue module's own bot settings (ADR 0003, ADR 0022 C9): their current version, their defaults and how a request's
///     settings are checked. The core stores what <see cref="Read" /> returns and never reads its fields. A venue without this
///     port takes no settings.
/// </summary>
public interface IVenueBotSettings : IVenuePort
{
    BotVenueSettings Defaults { get; }

    /// <summary>
    ///     The settings a request gave, checked and written in the module's current version. Missing fields take their
    ///     defaults; an invalid value throws <c>ApplicationValidationException</c> naming the field.
    /// </summary>
    BotVenueSettings Read(JsonElement settings);
}