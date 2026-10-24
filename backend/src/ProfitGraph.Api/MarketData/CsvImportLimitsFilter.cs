using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

using ProfitGraph.Application.MarketData.ImportCandles;

namespace ProfitGraph.Api.MarketData;

/// <summary>
///     Applies the configured CSV and multipart limits before the form is read, only on the import endpoint.
/// </summary>
internal sealed class CsvImportLimitsFilter(CsvImportOptions options) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        IFeatureCollection features = context.HttpContext.Features;

        if (features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } requestSize)
        {
            requestSize.MaxRequestBodySize = options.MaxRequestSizeBytes;
        }

        if (features.Get<IFormFeature>()?.Form is null)
        {
            features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request, new FormOptions { MultipartBodyLengthLimit = options.MaxFileSizeBytes }));
        }
    }
}