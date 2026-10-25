using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.Extensions.Options;

namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     One gRPC channel to the configured endpoint, created on first use with the pinned certificate trust and without
///     logging or retries; the caller decides what may be repeated. A disabled venue sends nothing.
/// </summary>
internal sealed class TInvestChannelInvokers(IOptions<TInvestOptions> options, TimeProvider clock) : ITInvestCallInvokers, IDisposable
{
    private readonly Lazy<(GrpcChannel Channel, TInvestTrustedCertificates Certificates)> _channel = new Lazy<(GrpcChannel, TInvestTrustedCertificates)>(
        () => Open(options.Value, clock),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public void Dispose()
    {
        if (_channel.IsValueCreated)
        {
            _channel.Value.Channel.Dispose();
            _channel.Value.Certificates.Dispose();
        }
    }

    public CallInvoker Get()
    {
        if (!options.Value.Enabled)
        {
            throw new TInvestRpcException(TInvestFailureKind.Unavailable, StatusCode.Unavailable, brokerCode: null);
        }

        return _channel.Value.Channel.CreateCallInvoker();
    }

    private static (GrpcChannel, TInvestTrustedCertificates) Open(TInvestOptions options, TimeProvider clock)
    {
        TInvestTrustedCertificates certificates = TInvestTrustedCertificates.Load(options.TrustedRootsPath, clock.GetUtcNow());
        var validator = new TInvestCertificateValidator(certificates.Root, certificates.Intermediate);
        GrpcChannel channel = GrpcChannel.ForAddress(
            options.Endpoint,
            new GrpcChannelOptions
            {
                HttpHandler = validator.CreateHandler(),
                DisposeHttpClient = true,
                MaxRetryAttempts = 0,
                LoggerFactory = null
            });

        return (channel, certificates);
    }
}