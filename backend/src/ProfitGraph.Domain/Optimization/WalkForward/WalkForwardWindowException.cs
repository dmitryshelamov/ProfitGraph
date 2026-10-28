using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Optimization.WalkForward;

/// <summary>
///     Walk-forward settings or an in-sample range that cannot form the requested windows.
/// </summary>
public sealed class WalkForwardWindowException : DomainException
{
    public const string InvalidWindows = "walk_forward_invalid_windows";

    public int? InSampleBars { get; }

    public WalkForwardWindowException(int inSampleBars)
        : base(InvalidWindows, $"The in-sample range of {inSampleBars} bars is too short for the requested walk-forward windows.")
    {
        InSampleBars = inSampleBars;
    }

    public WalkForwardWindowException(string message)
        : base(InvalidWindows, message)
    {
    }
}