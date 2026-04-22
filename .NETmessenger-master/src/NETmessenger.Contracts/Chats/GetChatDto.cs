using NETmessenger.Contracts.Messages;

namespace NETmessenger.Contracts.Chats;

public record GetChatDto(
    Guid ChatId,
    DateTime CreatedAt,
    bool IsGroup,
    string? Name,
    IReadOnlyCollection<Guid> ParticipantUserIds,
    string? LastMessageText,
    MessageType? LastMessageType,
    Guid? LastMessageSenderUserId,
    DateTime? LastMessageSentAt);
