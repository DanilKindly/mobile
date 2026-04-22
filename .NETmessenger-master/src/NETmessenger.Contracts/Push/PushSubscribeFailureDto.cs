namespace NETmessenger.Contracts.Push;

public record PushSubscribeFailureDto(
    string ErrorName,
    string ErrorMessage,
    string? UserAgent,
    bool IsStandalone);

