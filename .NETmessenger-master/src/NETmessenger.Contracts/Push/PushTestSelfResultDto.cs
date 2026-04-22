namespace NETmessenger.Contracts.Push;

public record PushTestSelfResultDto(
    bool Sent,
    int AttemptedSubscriptions,
    int SuccessfulSubscriptions,
    string? ErrorCode,
    string? ErrorMessage);

