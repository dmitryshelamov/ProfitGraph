using System.Text.Json;
using System.Text.Json.Serialization;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class OrderIntentDbConverter
{
    private static readonly JsonSerializerOptions PayloadOptions = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

    public static OrderIntent ToDomain(this OrderIntentDb row)
    {
        return OrderIntent.Restore(
            new OrderIntentSnapshot(
                new OrderIntentId(row.Id),
                new BotId(row.BotId),
                row.EngineOrderId,
                Enum.Parse<OrderIntentKind>(row.Kind),
                new ClientOrderId(row.ClientOrderId),
                row.Payload is null ? null : JsonSerializer.Deserialize<OrderSnapshot>(row.Payload, PayloadOptions),
                row.FromKill,
                Enum.Parse<OrderIntentStatus>(row.Status),
                row.Attempts,
                row.NextAttemptAt,
                row.ClaimedAt,
                row.PlaceStartedAt,
                row.SentAt,
                row.RejectionReason is null ? null : new OrderIntentRejection(Enum.Parse<RejectionReason>(row.RejectionReason), row.RejectionMessage),
                row.CreatedAt));
    }

    public static OrderIntentDb ToDb(this OrderIntent intent)
    {
        OrderIntentSnapshot snapshot = intent.ToSnapshot();

        return new OrderIntentDb
        {
            Id = snapshot.Id.Value,
            BotId = snapshot.BotId.Value,
            EngineOrderId = snapshot.EngineOrderId,
            Kind = snapshot.Kind.ToString(),
            ClientOrderId = snapshot.ClientOrderId.Value,
            Payload = snapshot.Order is null ? null : JsonSerializer.Serialize(snapshot.Order, PayloadOptions),
            FromKill = snapshot.FromKill,
            Status = snapshot.Status.ToString(),
            Attempts = snapshot.Attempts,
            NextAttemptAt = snapshot.NextAttemptAt,
            ClaimedAt = snapshot.ClaimedAt,
            PlaceStartedAt = snapshot.PlaceStartedAt,
            SentAt = snapshot.SentAt,
            RejectionReason = snapshot.Rejection?.Reason.ToString(),
            RejectionMessage = snapshot.Rejection?.Message,
            CreatedAt = snapshot.CreatedAt
        };
    }
}