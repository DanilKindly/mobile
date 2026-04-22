using NETmessenger.Contracts.Chats;

namespace NETmessenger.Contracts.Messages;

public record MessageChangesDto(
    long Cursor,
    IReadOnlyCollection<GetMessageDto> Messages,
    IReadOnlyCollection<GetChatDto> Chats);
