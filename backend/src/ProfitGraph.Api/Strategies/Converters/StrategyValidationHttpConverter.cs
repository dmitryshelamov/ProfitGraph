using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Api.Strategies.Converters;

internal static class StrategyValidationHttpConverter
{
    public static StrategyValidationResponse ToResponse(this StrategyValidationInternal validation)
    {
        return new StrategyValidationResponse(
            validation.IsValid,
            validation.Version,
            validation.Diagnostics.Select(item => new StrategyDiagnosticResponse(
                    item.Code,
                    item.Message,
                    item.BlockId,
                    item.ValueName,
                    item.ParameterName,
                    item.Setting,
                    item.CheckIndex))
                .ToArray());
    }
}