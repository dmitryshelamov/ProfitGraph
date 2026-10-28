using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotLeaseDbConverter
{
    public static BotLease ToDomain(this BotLeaseDb row)
    {
        return BotLease.Restore(new BotId(row.BotId), new LeaseHolderId(row.HolderId), row.Epoch, row.ExpiresAt, row.SubmissionsReady);
    }

    public static BotLeaseDb ToDb(this BotLease lease)
    {
        return new BotLeaseDb
        {
            BotId = lease.BotId.Value,
            HolderId = lease.HolderId.Value,
            Epoch = lease.Epoch,
            ExpiresAt = lease.ExpiresAt,
            SubmissionsReady = lease.SubmissionsReady
        };
    }
}