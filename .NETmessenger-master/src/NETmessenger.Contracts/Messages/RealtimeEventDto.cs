using NETmessenger.Contracts.Chats;

namespace NETmessenger.Contracts.Messages;

public record RealtimeEventDto(
    string EventType,
    long Cursor,
    long Version,
    GetMessageDto? Message,
    Guid? ChatId,
    IReadOnlyCollection<Guid>? MessageIds,
    Guid? ReaderUserId,
    GetChatDto? ChatPreview);
