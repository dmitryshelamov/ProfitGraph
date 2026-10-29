using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.CreateScanTrade;
using ProfitGraph.Application.Scanner.SetScanTradeSettings;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanTradeCardRequestConverter
{
    public static CreateScanTradeCommand ToCommand(this CreateScanTradeRequest request, Guid ownerId, string venue)
    {
        var errors = new List<InputError>();

        if (!Enum.TryParse(request.Scenario, ignoreCase: false, out ScanScenario scenario) || !Enum.IsDefined(scenario) || int.TryParse(request.Scenario, out _))
        {
            errors.Add(new InputError("invalid_value", nameof(request.Scenario), "Unknown scenario."));
        }

        decimal entry = Number(request.Entry, nameof(request.Entry), errors);
        decimal stop = Number(request.Stop, nameof(request.Stop), errors);
        decimal target = Number(request.Target, nameof(request.Target), errors);

        return errors.Count > 0
            ? throw new ApplicationValidationException(errors)
            : new CreateScanTradeCommand(ownerId, venue, request.Symbol, request.Timeframe, scenario, entry, stop, target);
    }

    public static SetScanTradeSettingsCommand ToCommand(this ScanTradeSettingsRequest request, Guid ownerId, string venue)
    {
        var errors = new List<InputError>();
        decimal risk = Number(request.Risk, nameof(request.Risk), errors);
        decimal maxAmount = Number(request.MaxAmount, nameof(request.MaxAmount), errors);

        return errors.Count > 0
            ? throw new ApplicationValidationException(errors)
            : new SetScanTradeSettingsCommand(ownerId, venue, request.CredentialId, risk, maxAmount);
    }

    private static decimal Number(string? value, string field, List<InputError> errors)
    {
        if (ExactGraphNumberParser.TryDecimal(value, out decimal number))
        {
            return number;
        }

        errors.Add(new InputError("invalid_number", field, "Use an exact invariant decimal string."));

        return 0m;
    }
}