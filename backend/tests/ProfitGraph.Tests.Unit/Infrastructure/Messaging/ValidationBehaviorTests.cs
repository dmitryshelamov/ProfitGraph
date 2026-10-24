using FluentValidation;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Infrastructure.Messaging;

namespace ProfitGraph.Tests.Unit.Infrastructure.Messaging;

public sealed class ValidationBehaviorTests
{
    [Test]
    public async Task InvalidRequestFailsBeforeHandler()
    {
        var behavior = new ValidationBehavior<RenameCommand, string>([new RenameCommandValidator()]);
        var handlerCalled = false;

        var exception = await Should.ThrowAsync<ApplicationValidationException>(() =>
            behavior.Handle(
                new RenameCommand(string.Empty),
                _ =>
                {
                    handlerCalled = true;

                    return Task.FromResult("renamed");
                },
                CancellationToken.None));

        handlerCalled.ShouldBeFalse();
        exception.Errors.ShouldHaveSingleItem().Field.ShouldBe(nameof(RenameCommand.Name));
    }

    [Test]
    public async Task ValidRequestReachesHandler()
    {
        var behavior = new ValidationBehavior<RenameCommand, string>([new RenameCommandValidator()]);

        string result = await behavior.Handle(new RenameCommand("Ladder"), _ => Task.FromResult("renamed"), CancellationToken.None);

        result.ShouldBe("renamed");
    }

    private sealed record RenameCommand(string Name) : ICommand<string>;

    private sealed class RenameCommandValidator : AbstractValidator<RenameCommand>
    {
        public RenameCommandValidator()
        {
            RuleFor(command => command.Name).NotEmpty();
        }
    }
}