namespace NETmessenger.Contracts.Messages;

public record GetMessageDto(
    Guid MessageId,
    Guid ChatId,
    Guid SenderUserId,
    MessageType Type,
    string? Text,
    string? AudioUrl,
    string? AudioContentType,
    int? AudioDurationSeconds,
    long? AudioSizeBytes,
    string? MediaUrl,
    string? MediaContentType,
    string? MediaFileName,
    long? MediaSizeBytes,
    DateTime SentAt);
