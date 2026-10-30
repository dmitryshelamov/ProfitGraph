using FluentValidation;

using ProfitGraph.Application.Scanner.Validators;

namespace ProfitGraph.Application.Scanner.StartScanBacktest;

internal sealed class StartScanBacktestCommandValidator : AbstractValidator<StartScanBacktestCommand>
{
    /// <summary>
    ///     The furthest back a backtest's period may end, in days.
    /// </summary>
    public const int MaxDaysBack = 335;

    public StartScanBacktestCommandValidator()
    {
        RuleFor(command => command.Definition).NotNull().SetValidator(new ScanPresetDefinitionInternalValidator());
        RuleFor(command => command.DaysBack).InclusiveBetween(from: 0, MaxDaysBack);
    }
}