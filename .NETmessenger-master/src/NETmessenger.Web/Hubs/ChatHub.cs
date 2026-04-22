using Microsoft.AspNetCore.SignalR;
using NETmessenger.Application.Abstractions.Messages;
using NETmessenger.Contracts.Messages;

namespace NETmessenger.Web.Hubs;

public class ChatHub(IMessageService messageService) : Hub
{
    public Task JoinChat(Guid chatId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, GetChatGroup(chatId));
    }

    public Task LeaveChat(Guid chatId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GetChatGroup(chatId));
    }

    public async Task SendMessage(Guid chatId, SendMessageDto dto)
    {
        var message = await messageService.SendAsync(chatId, dto, CancellationToken.None);
        await Clients.Group(GetChatGroup(chatId)).SendAsync("MessageReceived", message);
    }

    public async Task MarkMessagesAsRead(Guid chatId, Guid readerUserId)
    {
        var readMessageIds = await messageService.MarkMessagesAsReadAsync(chatId, readerUserId, CancellationToken.None);
        if (readMessageIds.Count == 0)
        {
            return;
        }

        await Clients.Group(GetChatGroup(chatId))
            .SendAsync("MessagesRead", chatId, readMessageIds, readerUserId);
    }

    public static string GetChatGroup(Guid chatId)
    {
        return $"chat:{chatId:D}";
    }
    
    private async Task StartTyping(Guid chatId, Guid userId)
    {
        await Clients.Group(GetChatGroup(chatId)).SendAsync("UserTyping", userId);
    }
    
    private async Task StopTyping(Guid chatId, Guid userId)
    {
        await Clients.Group(GetChatGroup(chatId)).SendAsync("UserStoppedTyping", userId);
    }
}
