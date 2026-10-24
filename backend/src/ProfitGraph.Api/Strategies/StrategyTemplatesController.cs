using MediatR;

using Microsoft.AspNetCore.Mvc;

using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Api.Strategies.Converters;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.GetStrategyTemplates;

namespace ProfitGraph.Api.Strategies;

[ApiController]
[Route("api/strategy-templates")]
public sealed class StrategyTemplatesController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StrategyTemplateResponse>>> Get(CancellationToken cancellationToken)
    {
        IReadOnlyList<StrategyTemplateInternal> templates = await sender.Send(new GetStrategyTemplatesQuery(), cancellationToken);

        return templates.Select(template => template.ToResponse()).ToArray();
    }
}