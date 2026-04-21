namespace NETmessenger.Contracts.Chats;

public record CreateChatDto(bool IsGroup, string? Name, IReadOnlyCollection<Guid> ParticipantUserIds);
