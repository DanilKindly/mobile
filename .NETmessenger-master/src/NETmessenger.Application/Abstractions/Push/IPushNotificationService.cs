using NETmessenger.Contracts.Messages;
using NETmessenger.Contracts.Push;

namespace NETmessenger.Application.Abstractions.Push;

public interface IPushNotificationService
{
    PushVapidPublicKeyDto? GetPublicKey();
    Task UpsertSubscriptionAsync(Guid userId, PushSubscriptionRequestDto dto, CancellationToken cancellationToken);
    Task RemoveSubscriptionAsync(Guid userId, string endpoint, CancellationToken cancellationToken);
    Task<PushSubscriptionStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken);
    Task<PushTestSelfResultDto> SendTestPushToUserAsync(Guid userId, CancellationToken cancellationToken);
    Task TrackClientSubscribeFailureAsync(Guid userId, PushSubscribeFailureDto dto, CancellationToken cancellationToken);
    Task NotifyIncomingMessageAsync(GetMessageDto message, CancellationToken cancellationToken);
}
