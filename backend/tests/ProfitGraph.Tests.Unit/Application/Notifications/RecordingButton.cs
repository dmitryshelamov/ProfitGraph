using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Updates;

namespace ProfitGraph.Tests.Unit.Application.Notifications;

/// <summary>
///     An <c>ack</c> button handler that records its calls and can fail.
/// </summary>
internal sealed class RecordingButton : ITelegramCallbackHandler
{
    public List<(long ChatId, string Argument)> Calls { get; } = [];
    public bool Fail { get; set; }

    public string Action => "ack";

    public Task<string?> Handle(TelegramCallbackInternal callback, long chatId, string argument, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("The handler failed.");
        }

        Calls.Add((chatId, argument));

        return Task.FromResult<string?>("done");
    }
}