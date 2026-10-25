using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;

namespace ProfitGraph.Application.Accounts.ListVenueAccounts;

internal sealed class ListVenueAccountsQueryHandler(VenueRegistry venues, VenuePorts<IVenueAccountDirectory> directories)
    : IRequestHandler<ListVenueAccountsQuery, IReadOnlyList<VenueAccountInternal>>
{
    public Task<IReadOnlyList<VenueAccountInternal>> Handle(ListVenueAccountsQuery query, CancellationToken cancellationToken)
    {
        return directories.Find(venues.Find(query.Venue)!.Id)!.List(query.Fields!, cancellationToken);
    }
}