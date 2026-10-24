using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.AspNetCore.SignalR.Client;

namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     Records the messages of one connection.
/// </summary>
internal sealed class BacktestProgressClient
{
    private readonly ConcurrentQueue<JsonElement> _messages = new ConcurrentQueue<JsonElement>();
    private readonly TaskCompletionSource<string> _finished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

    public HubConnection Connection { get; }

    public List<JsonElement> Messages => [.. _messages];

    public List<long> Sequences => [.. _messages.Select(message => message.GetProperty("observation").GetProperty("sequence").GetInt64())];

    public List<Guid> Runs => [.. _messages.Select(message => message.GetProperty("runId").GetGuid())];

    public Task<string> Finished => _finished.Task;

    public BacktestProgressClient(HubConnection connection)
    {
        Connection = connection;
        connection.On<JsonElement>("BacktestProgress", message => _messages.Enqueue(message.Clone()));
        connection.On<JsonElement>("BacktestFinished", message => _finished.TrySetResult(message.GetProperty("outcome").GetString()!));
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