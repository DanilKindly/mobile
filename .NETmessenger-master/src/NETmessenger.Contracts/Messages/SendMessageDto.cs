namespace NETmessenger.Contracts.Messages;

public record SendMessageDto(Guid SenderUserId, string Text);
