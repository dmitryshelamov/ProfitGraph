using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Api.Strategies.Converters;
using ProfitGraph.Application.Strategies.GetBlockCatalog;

namespace ProfitGraph.Api.Strategies;

[ApiController]
[Route("api/block-catalog")]
public sealed class BlockCatalogController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BlockCatalogResponse>> Get(CancellationToken cancellationToken)
    {
        return (await sender.Send(new GetBlockCatalogQuery(), cancellationToken)).ToResponse();
    }
}