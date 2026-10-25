using Google.Protobuf.Reflection;

using Grpc.Core;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     An in-process Invest API: unary and server-stream handlers per method with invented data, and a record of every call with its
///     metadata. Nothing leaves the process. Handlers throw <see cref="RpcException" /> to answer like the broker does.
/// </summary>
internal sealed class MockTInvestApi : CallInvoker
{
    private readonly Dictionary<string, Func<object, Metadata, object>> _handlers = new Dictionary<string, Func<object, Metadata, object>>();
    private readonly Dictionary<string, Delegate> _streams = new Dictionary<string, Delegate>();

    public List<(string Method, Metadata Headers, DateTime? Deadline)> Calls { get; } = [];

    public static string Name(ServiceDescriptor service, string method)
    {
        return $"/{service.FullName}/{service.FindMethodByName(method).Name}";
    }

    public MockTInvestApi On<TRequest, TResponse>(ServiceDescriptor service, string method, Func<TRequest, TResponse> handler)
    {
        _handlers[Name(service, method)] = (request, _) => handler((TRequest)request)!;

        return this;
    }

    public MockTInvestApi OnStream<TRequest, TResponse>(
        ServiceDescriptor service,
        string method,
        Func<TRequest, CancellationToken, IAsyncEnumerable<TResponse>> handler)
    {
        _streams[Name(service, method)] = handler;

        return this;
    }

    public static RpcException Refusal(StatusCode status, string detail, Metadata? trailers = null)
    {
        return new RpcException(new Status(status, detail), trailers ?? []);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request)
    {
        lock (Calls)
        {
            Calls.Add((method.FullName, options.Headers ?? [], options.Deadline));
        }

        Task<TResponse> response = Task.Run(() => _handlers.TryGetValue(method.FullName, out Func<object, Metadata, object>? handler)
            ? (TResponse)handler(request, options.Headers ?? [])
            : throw Refusal(StatusCode.Unimplemented, "mock"));

        return new AsyncUnaryCall<TResponse>(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
    {
        throw new NotSupportedException("The mock answers asynchronous calls only.");
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request)
    {
        lock (Calls)
        {
            Calls.Add((method.FullName, options.Headers ?? [], options.Deadline));
        }

        if (!_streams.TryGetValue(method.FullName, out Delegate? handler))
        {
            throw Refusal(StatusCode.Unimplemented, "mock");
        }

        var reader = new MockTInvestStreamReader<TResponse>(
            ((Func<TRequest, CancellationToken, IAsyncEnumerable<TResponse>>)handler)(request, options.CancellationToken),
            options.CancellationToken);

        return new AsyncServerStreamingCall<TResponse>(reader, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], reader.Dispose);
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options)
    {
        throw new NotSupportedException("The Invest API has no client streams ProfitGraph uses.");
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options)
    {
        throw new NotSupportedException("Streams arrive with live market data.");
    }
}