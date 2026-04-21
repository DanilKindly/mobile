using NETmessenger.Contracts.Chats;

namespace NETmessenger.Application.Abstractions.Chats;

public interface IChatService
{
    Task<GetChatDto> CreateAsync(CreateChatDto dto, CancellationToken cancellationToken);
    Task<GetChatDto?> GetByIdAsync(Guid chatId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<GetChatDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
