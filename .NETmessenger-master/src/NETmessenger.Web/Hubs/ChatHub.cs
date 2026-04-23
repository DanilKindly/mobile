using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NETmessenger.Application.Abstractions.Chats;
using NETmessenger.Application.Abstractions.Messages;
using NETmessenger.Application.Abstractions.Security;
using NETmessenger.Application.Abstractions.Users;
using NETmessenger.Contracts.Messages;
using NETmessenger.Web.Security;

namespace NETmessenger.Web.Hubs;

[Authorize]
public class ChatHub(
    IMessageService messageService,
    IChatService chatService,
    IUserService userService,
    ISecurityAuditService auditService,
    IAbuseGuard abuseGuard) : Hub
{
    private static readonly ConcurrentDictionary<string, Guid> ConnectionToUser = new();
    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> UserConnections = new();

    public async Task JoinChat(Guid chatId)
    {
        var currentUserId = Context.User!.GetRequiredUserId();
        if (await abuseGuard.IsBlockedAsync(currentUserId, CancellationToken.None))
        {
            await AuditAsync("blocked_user_hub_join", "denied", currentUserId, "chat", chatId.ToString("D"), "user is blocked");
            throw new HubException("Forbidden.");
        }

        await EnsureCurrentUserCanAccessChat(chatId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GetChatGroup(chatId));
    }

    public async Task LeaveChat(Guid chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetChatGroup(chatId));
    }

    public async Task<RealtimeEventDto> SendMessage(Guid chatId, SendMessageDto dto)
    {
        var currentUserId = Context.User!.GetRequiredUserId();
        if (await abuseGuard.IsBlockedAsync(currentUserId, CancellationToken.None))
        {
            await AuditAsync("blocked_user_hub_send", "denied", currentUserId, "chat", chatId.ToString("D"), "user is blocked");
            throw new HubException("Forbidden.");
        }

        await EnsureCurrentUserCanAccessChat(chatId);

        var trustedDto = dto with { SenderUserId = currentUserId };
        var message = await messageService.SendAsync(chatId, trustedDto, CancellationToken.None);
        await AuditAsync("hub_message_send", "success", currentUserId, "message", message.MessageId.ToString("D"), null);
        var eventDto = await BuildMessageCreatedEventAsync(chatId, message);

        await Clients.Group(GetChatGroup(chatId)).SendAsync("RealtimeEvent", eventDto);
        await Clients.Group(GetChatGroup(chatId)).SendAsync("MessageReceived", message);

        return eventDto;
    }

    public async Task MarkMessagesAsRead(Guid chatId, Guid readerUserId)
    {
        var currentUserId = Context.User!.GetRequiredUserId();
        if (await abuseGuard.IsBlockedAsync(currentUserId, CancellationToken.None))
        {
            await AuditAsync("blocked_user_hub_read", "denied", currentUserId, "chat", chatId.ToString("D"), "user is blocked");
            throw new HubException("Forbidden.");
        }

        await EnsureCurrentUserCanAccessChat(chatId);

        var readMessageIds = await messageService.MarkMessagesAsReadAsync(chatId, currentUserId, CancellationToken.None);
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
            currentUserId,
            null);

        await Clients.Group(GetChatGroup(chatId)).SendAsync("RealtimeEvent", eventDto);
        await Clients.Group(GetChatGroup(chatId)).SendAsync("MessagesRead", chatId, readMessageIds, currentUserId);
    }

    public async Task<MessageChangesDto> GetChanges(Guid userId, long cursor, int limit = 250)
    {
        var currentUserId = Context.User!.GetRequiredUserId();
        if (currentUserId != userId)
        {
            await AuditAsync("hub_changes_forbidden", "denied", currentUserId, "users", userId.ToString("D"), "cannot read another user's changes");
            throw new HubException("Forbidden.");
        }

        return await messageService.GetChangesByUserAsync(currentUserId, cursor, limit, CancellationToken.None);
    }

    public async Task SetPresence(Guid userId, bool isOnline)
    {
        var currentUserId = Context.User!.GetRequiredUserId();
        if (currentUserId == Guid.Empty)
        {
            return;
        }
        if (await abuseGuard.IsBlockedAsync(currentUserId, CancellationToken.None))
        {
            await AuditAsync("blocked_user_presence", "denied", currentUserId, "presence", currentUserId.ToString("D"), "user is blocked");
            throw new HubException("Forbidden.");
        }

        if (isOnline)
        {
            var becameOnline = RegisterConnection(currentUserId, Context.ConnectionId);
            if (becameOnline)
            {
                await Clients.All.SendAsync("PresenceChanged", currentUserId, true, (DateTime?)null);
            }

            return;
        }

        var becameOffline = UnregisterConnection(Context.ConnectionId, currentUserId);
        if (becameOffline)
        {
            var seenAtUtc = DateTime.UtcNow;
            await userService.UpdateLastSeenAsync(currentUserId, seenAtUtc, CancellationToken.None);
            await Clients.All.SendAsync("PresenceChanged", currentUserId, false, seenAtUtc);
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

    private async Task EnsureCurrentUserCanAccessChat(Guid chatId)
    {
        var currentUserId = Context.User!.GetRequiredUserId();
        var chat = await chatService.GetByIdAsync(chatId, CancellationToken.None);
        if (chat is null || !chat.ParticipantUserIds.Contains(currentUserId))
        {
            await AuditAsync("hub_chat_access_forbidden", "denied", currentUserId, "chat", chatId.ToString("D"), "not a participant");
            throw new HubException("Forbidden.");
        }
    }

    private Task AuditAsync(
        string eventType,
        string outcome,
        Guid? userId,
        string? resourceType,
        string? resourceId,
        string? reason)
    {
        var httpContext = Context.GetHttpContext();
        return auditService.RecordAsync(new SecurityAuditEventInput(
            eventType,
            outcome,
            outcome == "success" ? "info" : "warning",
            userId,
            httpContext?.Connection.RemoteIpAddress?.ToString(),
            httpContext?.Request.Headers.UserAgent.ToString(),
            resourceType,
            resourceId,
            reason),
            CancellationToken.None);
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
