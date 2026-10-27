using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Updates;

namespace ProfitGraph.Tests.Unit.Application.Notifications;

/// <summary>
///     The update processor (ADR 0042): a command goes to its handler, anything else gets the help text, a press goes to the
///     handler of its action and is always answered, and bots and chats that are not private are ignored.
/// </summary>
public sealed class TelegramUpdateProcessorTests
{
    private RecordingButton _ack = null!;
    private RecordingCommand _start = null!;
    private RecordingTelegram _telegram = null!;

    [SetUp]
    public void CreateFakes()
    {
        _telegram = new RecordingTelegram();
        _start = new RecordingCommand();
        _ack = new RecordingButton();
    }

    [Test]
    public async Task CommandGoesToItsHandlerWithItsArguments()
    {
        await Processor().Process(Message("/START@profitgraph_bot  CODE123 extra"), CancellationToken.None);

        _start.Calls.ShouldBe([(42L, "CODE123 extra")]);
        _telegram.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task UnknownTextGetsTheHelpText()
    {
        await Processor().Process(Message("hello"), CancellationToken.None);

        _telegram.Sent.ShouldBe([(42L, TelegramMessages.Help)]);
        _start.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task BotsAndGroupsAreIgnored()
    {
        await Processor()
            .Process(
                new TelegramUpdateInternal(UpdateId: 1, new TelegramIncomingMessageInternal(ChatId: 42, "private", FromBot: true, "/start X")),
                CancellationToken.None);
        await Processor()
            .Process(
                new TelegramUpdateInternal(UpdateId: 2, new TelegramIncomingMessageInternal(ChatId: -5, "group", FromBot: false, "/start X")),
                CancellationToken.None);

        _start.Calls.ShouldBeEmpty();
        _telegram.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task PressGoesToTheHandlerOfItsActionAndIsAnswered()
    {
        await Processor().Process(Press("ack:abc", "private"), CancellationToken.None);

        _ack.Calls.ShouldBe([(42L, "abc")]);
        _telegram.Answers.ShouldBe([("cb", "done")]);
    }

    [Test]
    public async Task UnknownPressIsAnsweredWithANotice()
    {
        await Processor().Process(Press("open:1", "private"), CancellationToken.None);

        _ack.Calls.ShouldBeEmpty();
        _telegram.Answers.ShouldBe([("cb", TelegramMessages.UnknownButton)]);
    }

    [Test]
    public async Task PressFromAGroupIsAnsweredButNotHandled()
    {
        await Processor().Process(Press("ack:abc", "group"), CancellationToken.None);

        _ack.Calls.ShouldBeEmpty();
        _telegram.Answers.ShouldBe([("cb", null)]);
    }

    [Test]
    public async Task FailedPressIsStillAnswered()
    {
        _ack.Fail = true;

        await Should.ThrowAsync<InvalidOperationException>(() => Processor().Process(Press("ack:abc", "private"), CancellationToken.None));

        _telegram.Answers.ShouldBe([("cb", null)]);
    }

    private TelegramUpdateProcessor Processor()
    {
        return new TelegramUpdateProcessor([_start], [_ack], _telegram);
    }

    private static TelegramUpdateInternal Message(string text)
    {
        return new TelegramUpdateInternal(UpdateId: 1, new TelegramIncomingMessageInternal(ChatId: 42, "private", FromBot: false, text));
    }

    private static TelegramUpdateInternal Press(string data, string chatType)
    {
        return new TelegramUpdateInternal(UpdateId: 1, Callback: new TelegramCallbackInternal("cb", FromBot: false, ChatId: 42, chatType, MessageId: 7, data));
    }
}