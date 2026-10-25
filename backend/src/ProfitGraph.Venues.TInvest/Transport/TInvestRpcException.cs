using System.Globalization;

using Grpc.Core;

namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     A failed Invest API call as a closed classification with a fixed message. The broker's own text, metadata and the
///     token never enter it.
/// </summary>
internal sealed class TInvestRpcException : Exception
{
    public TInvestFailureKind Kind { get; }

    public StatusCode Status { get; }

    /// <summary>
    ///     The broker's numeric code, such as <c>30079</c>, when the status description is exactly that number.
    /// </summary>
    public int? BrokerCode { get; }

    public TInvestRpcException(TInvestFailureKind kind, StatusCode status, int? brokerCode)
        : base($"T-Invest call failed: {kind} ({status}{(brokerCode is { } code ? ", code " + code.ToString(CultureInfo.InvariantCulture) : string.Empty)}).")
    {
        Kind = kind;
        Status = status;
        BrokerCode = brokerCode;
    }

    public static TInvestRpcException From(RpcException error)
    {
        int? code = int.TryParse(error.Status.Detail, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed > 0 ? parsed : null;
        TInvestFailureKind kind = error.StatusCode switch
        {
            StatusCode.Unauthenticated => TInvestFailureKind.Unauthenticated,
            StatusCode.PermissionDenied => TInvestFailureKind.PermissionDenied,
            StatusCode.NotFound => TInvestFailureKind.NotFound,
            StatusCode.ResourceExhausted => TInvestFailureKind.RateLimited,
            StatusCode.DeadlineExceeded => TInvestFailureKind.Timeout,
            StatusCode.Unavailable or StatusCode.Internal or StatusCode.Aborted => TInvestFailureKind.Unavailable,
            StatusCode.InvalidArgument or StatusCode.FailedPrecondition or StatusCode.OutOfRange when code is not null => TInvestFailureKind.Rejected,
            _ => TInvestFailureKind.Unknown
        };

        return new TInvestRpcException(kind, error.StatusCode, code);
    }
}