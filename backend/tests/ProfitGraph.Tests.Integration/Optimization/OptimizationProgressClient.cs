using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.AspNetCore.SignalR.Client;

namespace ProfitGraph.Tests.Integration.Optimization;

/// <summary>
///     Records the messages of one connection to the optimization hub.
/// </summary>
internal sealed class OptimizationProgressClient
{
    private readonly ConcurrentQueue<JsonElement> _messages = new ConcurrentQueue<JsonElement>();
    private readonly TaskCompletionSource<string> _finished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

    public HubConnection Connection { get; }

    public List<long> Sequences => [.. _messages.Select(message => message.GetProperty("trial").GetProperty("sequence").GetInt64())];

    public List<Guid> Optimizations => [.. _messages.Select(message => message.GetProperty("optimizationId").GetGuid())];

    public Task<string> Finished => _finished.Task;

    public OptimizationProgressClient(HubConnection connection)
    {
        Connection = connection;
        connection.On<JsonElement>("OptimizationProgress", message => _messages.Enqueue(message.Clone()));
        connection.On<JsonElement>("OptimizationFinished", message => _finished.TrySetResult(message.GetProperty("status").GetString()!));
    }

    public async Task Received(int count)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);

        while (_messages.Count < count)
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, $"Only {_messages.Count} progress messages arrived.");
            await Task.Delay(millisecondsDelay: 5);
        }
    }
}