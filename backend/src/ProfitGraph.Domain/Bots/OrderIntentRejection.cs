using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Why a submit never became a venue order, with a short safe message; never a raw venue payload.
/// </summary>
public sealed record OrderIntentRejection
{
    public const int MaxMessageLength = 500;

    public RejectionReason Reason { get; }
    public string Message { get; }

    public OrderIntentRejection(RejectionReason reason, string? message)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentException("Unknown rejection reason.", nameof(reason));
        }

        Reason = reason;
        string text = message?.Trim() ?? string.Empty;
        Message = text.Length > MaxMessageLength ? text[..MaxMessageLength] : text;
    }
}