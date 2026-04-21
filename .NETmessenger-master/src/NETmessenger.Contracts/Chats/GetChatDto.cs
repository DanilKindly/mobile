namespace NETmessenger.Contracts.Chats;

public record GetChatDto(Guid ChatId, DateTime CreatedAt, bool IsGroup, string? Name, IReadOnlyCollection<Guid> ParticipantUserIds);
