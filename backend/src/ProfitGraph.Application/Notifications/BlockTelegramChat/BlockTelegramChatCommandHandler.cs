using MediatR;

using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Application.Notifications.BlockTelegramChat;

internal sealed class BlockTelegramChatCommandHandler(ITelegramLinkRepository links, TimeProvider clock) : IRequestHandler<BlockTelegramChatCommand>
{
    public async Task Handle(BlockTelegramChatCommand command, CancellationToken cancellationToken)
    {
        if (await links.GetByChatForUpdate(command.ChatId, cancellationToken) is { } link && link.Block(clock.GetUtcNow()))
        {
            await links.Update(link, cancellationToken);
        }
    }
}