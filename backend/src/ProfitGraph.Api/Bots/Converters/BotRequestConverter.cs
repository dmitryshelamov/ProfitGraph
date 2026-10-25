using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Bots.ChangeBotParameters;
using ProfitGraph.Application.Bots.ChangeBotRiskLimits;
using ProfitGraph.Application.Bots.CreateBot;
using ProfitGraph.Application.Bots.SubmitManualOrder;
using ProfitGraph.Application.Common.Exceptions;

namespace ProfitGraph.Api.Bots.Converters;

/// <summary>
///     Parses exact decimal strings of bot requests; every malformed number is reported at once.
/// </summary>
internal static class BotRequestConverter
{
    public static CreateBotCommand ToCommand(this CreateBotRequest request, Guid ownerId)
    {
        var errors = new List<InputError>();
        Dictionary<string, decimal>? parameters = Parameters(request.Parameters, errors);
        decimal allocationCap = Number(request.AllocationCap, "AllocationCap", errors);
        decimal maxOrderQuote = Number(request.MaxOrderQuote, "MaxOrderQuote", errors);
        decimal dailyLossLimitQuote = Number(request.DailyLossLimitQuote, "DailyLossLimitQuote", errors);
        ThrowIfAny(errors);

        return new CreateBotCommand(
            ownerId,
            request.StrategyId,
            request.CredentialId,
            request.Symbol,
            request.Timeframe,
            parameters,
            allocationCap,
            maxOrderQuote,
            dailyLossLimitQuote,
            request.AllowForeignOrders);
    }

    public static ChangeBotParametersCommand ToCommand(this ChangeBotParametersRequest request, Guid ownerId, Guid id)
    {
        var errors = new List<InputError>();
        Dictionary<string, decimal>? parameters = Parameters(request.Parameters, errors);
        decimal allocationCap = Number(request.AllocationCap, "AllocationCap", errors);
        ThrowIfAny(errors);

        return new ChangeBotParametersCommand(ownerId, id, parameters, allocationCap);
    }

    public static ChangeBotRiskLimitsCommand ToCommand(this ChangeBotRiskLimitsRequest request, Guid ownerId, Guid id)
    {
        var errors = new List<InputError>();
        decimal maxOrderQuote = Number(request.MaxOrderQuote, "MaxOrderQuote", errors);
        decimal dailyLossLimitQuote = Number(request.DailyLossLimitQuote, "DailyLossLimitQuote", errors);
        ThrowIfAny(errors);

        return new ChangeBotRiskLimitsCommand(ownerId, id, maxOrderQuote, dailyLossLimitQuote);
    }

    private static Dictionary<string, decimal>? Parameters(IReadOnlyDictionary<string, string>? values, List<InputError> errors)
    {
        return values?.ToDictionary(pair => pair.Key, pair => Number(pair.Value, $"Parameters.{pair.Key}", errors), StringComparer.Ordinal);
    }

    public static SubmitManualOrderCommand ToCommand(this SubmitManualOrderRequest request, Guid ownerId, Guid botId)
    {
        var errors = new List<InputError>();
        decimal amount = Number(request.Amount, "Amount", errors);
        ThrowIfAny(errors);

        return new SubmitManualOrderCommand(ownerId, botId, request.Side, amount);
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

    private static void ThrowIfAny(List<InputError> errors)
    {
        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }
    }
}