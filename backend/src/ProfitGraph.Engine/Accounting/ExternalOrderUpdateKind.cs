namespace ProfitGraph.Engine.Accounting;

public enum ExternalOrderUpdateKind
{
    Accepted,
    Rejected,
    PartialFill,
    FinalFill,
    CancelConfirmed,
    LocalAccepted,
    FeeCorrection
}