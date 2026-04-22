namespace NETmessenger.Contracts.Messages;

public record GetMessagesPageDto(
    IReadOnlyCollection<GetMessageDto> Messages,
    bool HasMoreOlder,
    DateTime? NextBeforeSentAt,
    Guid? NextBeforeMessageId);
