namespace NETmessenger.Contracts.Messages;

public record SendMessageDto(
    Guid SenderUserId,
    string Text,
    string? ClientMessageId = null,
    DateTime? SentAtClient = null);
