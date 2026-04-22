namespace NETmessenger.Contracts.Push;

public record PushSubscriptionRequestDto(
    string Endpoint,
    string P256dh,
    string Auth,
    string? UserAgent);

