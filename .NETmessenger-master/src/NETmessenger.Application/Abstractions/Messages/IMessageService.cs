using System.IO;
using NETmessenger.Contracts.Messages;

namespace NETmessenger.Application.Abstractions.Messages;

public interface IMessageService
{
    Task<IReadOnlyCollection<GetMessageDto>> GetByChatIdAsync(Guid chatId, CancellationToken cancellationToken);
    Task<GetMessageDto> SendAsync(Guid chatId, SendMessageDto dto, CancellationToken cancellationToken);
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
}
