using NETmessenger.Contracts.Messages;
using NETmessenger.Contracts.Push;

namespace NETmessenger.Application.Abstractions.Push;

public interface IPushNotificationService
{
    PushVapidPublicKeyDto? GetPublicKey();
    Task UpsertSubscriptionAsync(Guid userId, PushSubscriptionRequestDto dto, CancellationToken cancellationToken);
    Task RemoveSubscriptionAsync(Guid userId, string endpoint, CancellationToken cancellationToken);
    Task NotifyIncomingMessageAsync(GetMessageDto message, CancellationToken cancellationToken);
}

