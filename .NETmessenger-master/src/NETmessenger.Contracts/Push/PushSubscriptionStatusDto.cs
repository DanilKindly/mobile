namespace NETmessenger.Contracts.Push;

public record PushSubscriptionStatusDto(
    bool HasActiveSubscription,
    string? EndpointMasked,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    int FailureCount,
    string? LastErrorCode);

