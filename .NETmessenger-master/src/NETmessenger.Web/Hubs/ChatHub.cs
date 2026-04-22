using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using NETmessenger.Application.Abstractions.Chats;
using NETmessenger.Application.Abstractions.Messages;
using NETmessenger.Application.Abstractions.Users;
using NETmessenger.Contracts.Messages;

namespace NETmessenger.Web.Hubs;

public class ChatHub(IMessageService messageService, IChatService chatService, IUserService userService) : Hub
{
    private static readonly ConcurrentDictionary<string, Guid> ConnectionToUser = new();
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> UserConnections = new();

    public Task JoinChat(Guid chatId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, GetChatGroup(chatId));
    }

    public Task LeaveChat(Guid chatId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GetChatGroup(chatId));
    }

    public async Task<RealtimeEventDto> SendMessage(Guid chatId, SendMessageDto dto)
    {
        var message = await messageService.SendAsync(chatId, dto, CancellationToken.None);
        var eventDto = await BuildMessageCreatedEventAsync(chatId, message);

        await Clients.Group(GetChatGroup(chatId)).SendAsync("RealtimeEvent", eventDto);
        await Clients.Group(GetChatGroup(chatId)).SendAsync("MessageReceived", message);

        return eventDto;
    }

    public async Task MarkMessagesAsRead(Guid chatId, Guid readerUserId)
    {
        var readMessageIds = await messageService.MarkMessagesAsReadAsync(chatId, readerUserId, CancellationToken.None);
        if (readMessageIds.Count == 0)
        {
            return;
        }

        var nowCursor = DateTime.UtcNow.Ticks;
        var eventDto = new RealtimeEventDto(
            "MessageUpdatedStatus",
            nowCursor,
            nowCursor,
            null,
            chatId,
            readMessageIds,
            readerUserId,
            null);

        await Clients.Group(GetChatGroup(chatId)).SendAsync("RealtimeEvent", eventDto);
        await Clients.Group(GetChatGroup(chatId)).SendAsync("MessagesRead", chatId, readMessageIds, readerUserId);
    }

    public async Task<MessageChangesDto> GetChanges(Guid userId, long cursor, int limit = 250)
    {
        return await messageService.GetChangesByUserAsync(userId, cursor, limit, CancellationToken.None);
    }

    public async Task SetPresence(Guid userId, bool isOnline)
    {
        if (userId == Guid.Empty)
        {
            return;
        }

        if (isOnline)
        {
            var becameOnline = RegisterConnection(userId, Context.ConnectionId);
            if (becameOnline)
            {
                await Clients.All.SendAsync("PresenceChanged", userId, true, (DateTime?)null);
            }

            return;
        }

        var becameOffline = UnregisterConnection(Context.ConnectionId, userId);
        if (becameOffline)
        {
            var seenAtUtc = DateTime.UtcNow;
            await userService.UpdateLastSeenAsync(userId, seenAtUtc, CancellationToken.None);
            await Clients.All.SendAsync("PresenceChanged", userId, false, seenAtUtc);
        }
    }

    public Task<IReadOnlyCollection<Guid>> GetOnlineUsers()
    {
        var onlineUsers = UserConnections
            .Where(x => !x.Value.IsEmpty)
            .Select(x => x.Key)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<Guid>>(onlineUsers);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var becameOffline = UnregisterConnection(Context.ConnectionId);
        if (becameOffline.HasValue)
        {
            var seenAtUtc = DateTime.UtcNow;
            await userService.UpdateLastSeenAsync(becameOffline.Value, seenAtUtc, CancellationToken.None);
            await Clients.All.SendAsync("PresenceChanged", becameOffline.Value, false, seenAtUtc);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string GetChatGroup(Guid chatId)
    {
        return $"chat:{chatId:D}";
    }

    private async Task<RealtimeEventDto> BuildMessageCreatedEventAsync(Guid chatId, GetMessageDto message)
    {
        var chatPreview = await chatService.GetByIdAsync(chatId, CancellationToken.None);
        var cursor = Math.Max(message.Version, DateTime.UtcNow.Ticks);
        return new RealtimeEventDto(
            "MessageCreated",
            cursor,
            message.Version,
            message,
            chatId,
            null,
            null,
            chatPreview);
    }

    private static bool RegisterConnection(Guid userId, string connectionId)
    {
        if (ConnectionToUser.TryGetValue(connectionId, out var existingUserId) && existingUserId != userId)
        {
            UnregisterConnection(connectionId, existingUserId);
        }

        ConnectionToUser[connectionId] = userId;

        var bucket = UserConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        var wasOffline = bucket.IsEmpty;
        bucket[connectionId] = 0;

        return wasOffline;
    }

    private static Guid? UnregisterConnection(string connectionId)
    {
        if (!ConnectionToUser.TryRemove(connectionId, out var userId))
        {
            return null;
        }

        return RemoveFromUserBucket(connectionId, userId) ? userId : null;
    }

    private static bool UnregisterConnection(string connectionId, Guid expectedUserId)
    {
        if (ConnectionToUser.TryGetValue(connectionId, out var mappedUserId) && mappedUserId != expectedUserId)
        {
            return false;
        }

        ConnectionToUser.TryRemove(connectionId, out _);
        return RemoveFromUserBucket(connectionId, expectedUserId);
    }

    private static bool RemoveFromUserBucket(string connectionId, Guid userId)
    {
        if (!UserConnections.TryGetValue(userId, out var bucket))
        {
            return false;
        }

        bucket.TryRemove(connectionId, out _);
        var isOffline = bucket.IsEmpty;

        if (isOffline)
        {
            UserConnections.TryRemove(userId, out _);
        }

        return isOffline;
    }
}
