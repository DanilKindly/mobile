using System.IO;
using NETmessenger.Contracts.Messages;

namespace NETmessenger.Application.Abstractions.Messages;

public interface IMessageService
{
    Task<IReadOnlyCollection<GetMessageDto>> GetByChatIdAsync(Guid chatId, CancellationToken cancellationToken);
    Task<GetMessagesPageDto> GetPageByChatIdAsync(
        Guid chatId,
        DateTime? beforeSentAt,
        Guid? beforeMessageId,
        int limit,
        CancellationToken cancellationToken);
    Task<GetMessageDto> SendAsync(Guid chatId, SendMessageDto dto, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Guid>> MarkMessagesAsReadAsync(Guid chatId, Guid readerUserId, CancellationToken cancellationToken);
    Task<GetMessageDto> SendVoiceAsync(
        Guid chatId,
        Guid senderUserId,
        Stream audioStream,
        string originalFileName,
        string contentType,
        long length,
        int? durationSeconds,
        CancellationToken cancellationToken);
    Task<GetMessageDto> SendMediaAsync(
        Guid chatId,
        Guid senderUserId,
        Stream fileStream,
        string originalFileName,
        string contentType,
        long length,
        CancellationToken cancellationToken);
    Task<MessageChangesDto> GetChangesByUserAsync(Guid userId, long cursor, int limit, CancellationToken cancellationToken);
}
