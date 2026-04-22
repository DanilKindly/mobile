using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NETmessenger.Application.Abstractions.Push;
using NETmessenger.Contracts.Messages;
using NETmessenger.Contracts.Push;
using NETmessenger.Infrastructure.Persistence;
using WebPush;
using PushSubscriptionEntity = NETmessenger.Domain.Entities.PushSubscription;

namespace NETmessenger.Infrastructure.Services.Push;

public sealed class PushNotificationService : IPushNotificationService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly string? _vapidPublicKey;
    private readonly string? _vapidPrivateKey;
    private readonly string _vapidSubject;

    public PushNotificationService(
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<PushNotificationService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _vapidPublicKey = configuration["Push:VapidPublicKey"];
        _vapidPrivateKey = configuration["Push:VapidPrivateKey"];
        _vapidSubject = configuration["Push:VapidSubject"] ?? "mailto:admin@kindly-messenger.local";
    }

    public PushVapidPublicKeyDto? GetPublicKey()
    {
        if (string.IsNullOrWhiteSpace(_vapidPublicKey) || string.IsNullOrWhiteSpace(_vapidPrivateKey))
        {
            return null;
        }

        return new PushVapidPublicKeyDto(_vapidPublicKey);
    }

    public async Task UpsertSubscriptionAsync(Guid userId, PushSubscriptionRequestDto dto, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return;
        }

        var endpoint = NormalizeRequired(dto.Endpoint);
        var p256dh = NormalizeRequired(dto.P256dh);
        var auth = NormalizeRequired(dto.Auth);
        var now = DateTime.UtcNow;

        var existing = await _dbContext.PushSubscriptions
            .FirstOrDefaultAsync(x => x.Endpoint == endpoint, cancellationToken);

        if (existing is null)
        {
            var subscription = new PushSubscriptionEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                UserAgent = NormalizeOptional(dto.UserAgent, 512),
                IsActive = true,
                FailureCount = 0,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _dbContext.PushSubscriptions.Add(subscription);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        existing.UserId = userId;
        existing.P256dh = p256dh;
        existing.Auth = auth;
        existing.UserAgent = NormalizeOptional(dto.UserAgent, 512);
        existing.IsActive = true;
        existing.FailureCount = 0;
        existing.UpdatedAt = now;
        existing.LastFailureAt = null;
        existing.LastErrorCode = null;
        existing.LastErrorMessage = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSubscriptionAsync(Guid userId, string endpoint, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        var normalizedEndpoint = endpoint.Trim();
        var existing = await _dbContext.PushSubscriptions
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Endpoint == normalizedEndpoint, cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.PushSubscriptions.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PushSubscriptionStatusDto> GetStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return new PushSubscriptionStatusDto(false, null, null, null, 0, null);
        }

        var subscription = await _dbContext.PushSubscriptions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return new PushSubscriptionStatusDto(false, null, null, null, 0, null);
        }

        return new PushSubscriptionStatusDto(
            subscription.IsActive,
            MaskEndpoint(subscription.Endpoint),
            subscription.LastSuccessAt,
            subscription.LastFailureAt,
            subscription.FailureCount,
            subscription.LastErrorCode);
    }

    public async Task<PushTestSelfResultDto> SendTestPushToUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return new PushTestSelfResultDto(false, 0, 0, "unauthorized", "User is not authorized.");
        }

        if (!CanSendPush())
        {
            LogPushSuppression("push_not_configured", "push-test-self", null, new[] { userId }, 0);
            return new PushTestSelfResultDto(false, 0, 0, "push_not_configured", "Push is not configured on server.");
        }

        var subscriptions = await _dbContext.PushSubscriptions
            .Where(x => x.IsActive && x.UserId == userId)
            .ToListAsync(cancellationToken);

        LogPushTrigger("push-test-self", null, new[] { userId }, subscriptions.Count);
        if (subscriptions.Count == 0)
        {
            LogPushSuppression("no_active_subscriptions", "push-test-self", null, new[] { userId }, 0);
            return new PushTestSelfResultDto(false, 0, 0, "no_active_subscriptions", "No active push subscription for current user.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            title = "Kindly Messenger",
            body = "Test push message",
            icon = "/icon-192.png",
            badge = "/icon-192.png",
            tag = $"push-test-{userId:D}",
            data = new
            {
                url = "/chats",
                chatId = (Guid?)null,
                messageId = (Guid?)null,
            }
        });

        var (attempted, successful) = await SendPayloadToSubscriptionsAsync(
            subscriptions,
            payload,
            "push-test-self",
            null,
            cancellationToken);

        return successful > 0
            ? new PushTestSelfResultDto(true, attempted, successful, null, null)
            : new PushTestSelfResultDto(false, attempted, successful, "push_send_failed", "Failed to deliver test push to active subscriptions.");
    }

    public Task TrackClientSubscribeFailureAsync(Guid userId, PushSubscribeFailureDto dto, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Push subscribe failed on client. userId={UserId} error={ErrorName} message={ErrorMessage} standalone={IsStandalone} userAgent={UserAgent}",
            userId,
            dto.ErrorName,
            dto.ErrorMessage,
            dto.IsStandalone,
            dto.UserAgent ?? string.Empty);

        return Task.CompletedTask;
    }

    public async Task NotifyIncomingMessageAsync(GetMessageDto message, CancellationToken cancellationToken)
    {
        if (message is null)
        {
            LogPushSuppression("null_message", "unknown", null, Array.Empty<Guid>(), 0);
            return;
        }

        if (!CanSendPush())
        {
            LogPushSuppression("push_not_configured", message.MessageId.ToString("D"), message.ChatId, Array.Empty<Guid>(), 0);
            return;
        }

        try
        {
            var chatData = await _dbContext.Chats
                .AsNoTracking()
                .Where(c => c.Id == message.ChatId)
                .Select(c => new
                {
                    c.Id,
                    c.IsGroup,
                    c.Name,
                    Participants = c.Participants.Select(p => new
                    {
                        p.Id,
                        p.Username,
                    }).ToArray()
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (chatData is null)
            {
                LogPushSuppression("chat_not_found", message.MessageId.ToString("D"), message.ChatId, Array.Empty<Guid>(), 0);
                return;
            }

            var recipientIds = chatData.Participants
                .Where(p => p.Id != message.SenderUserId)
                .Select(p => p.Id)
                .Distinct()
                .ToArray();

            if (recipientIds.Length == 0)
            {
                LogPushSuppression("no_recipients", message.MessageId.ToString("D"), message.ChatId, recipientIds, 0);
                return;
            }

            var subscriptions = await _dbContext.PushSubscriptions
                .Where(x => x.IsActive && recipientIds.Contains(x.UserId))
                .ToListAsync(cancellationToken);

            LogPushTrigger(message.MessageId.ToString("D"), message.ChatId, recipientIds, subscriptions.Count);
            if (subscriptions.Count == 0)
            {
                LogPushSuppression("no_active_subscriptions", message.MessageId.ToString("D"), message.ChatId, recipientIds, 0);
                return;
            }

            var senderDisplayName = chatData.Participants
                .FirstOrDefault(p => p.Id == message.SenderUserId)
                ?.Username ?? "New message";

            var title = chatData.IsGroup
                ? (string.IsNullOrWhiteSpace(chatData.Name) ? "New group message" : chatData.Name.Trim())
                : senderDisplayName;

            var body = BuildBody(message);
            var payload = JsonSerializer.Serialize(new
            {
                title,
                body,
                icon = "/icon-192.png",
                badge = "/icon-192.png",
                tag = $"chat-{chatData.Id:D}",
                data = new
                {
                    chatId = chatData.Id,
                    messageId = message.MessageId,
                    url = $"/chat/{chatData.Id:D}",
                }
            });

            await SendPayloadToSubscriptionsAsync(
                subscriptions,
                payload,
                message.MessageId.ToString("D"),
                message.ChatId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push notification send pipeline failed for message {MessageId}", message.MessageId);
        }
    }

    private async Task<(int Attempted, int Successful)> SendPayloadToSubscriptionsAsync(
        IReadOnlyCollection<PushSubscriptionEntity> subscriptions,
        string payload,
        string messageIdForLog,
        Guid? chatIdForLog,
        CancellationToken cancellationToken)
    {
        if (subscriptions.Count == 0)
        {
            return (0, 0);
        }

        var client = new WebPushClient();
        var vapidDetails = new VapidDetails(_vapidSubject, _vapidPublicKey!, _vapidPrivateKey!);
        var now = DateTime.UtcNow;
        var changed = false;
        var successful = 0;

        foreach (var subscription in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(
                    new WebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    payload,
                    vapidDetails,
                    cancellationToken);

                subscription.LastSuccessAt = now;
                subscription.UpdatedAt = now;
                subscription.LastFailureAt = null;
                subscription.FailureCount = 0;
                subscription.IsActive = true;
                subscription.LastErrorCode = null;
                subscription.LastErrorMessage = null;
                changed = true;
                successful += 1;

                _logger.LogInformation(
                    "PushSend messageId={MessageId} chatId={ChatId} recipientUserId={RecipientUserId} subscriptionId={SubscriptionId} endpoint={Endpoint} success=true statusCode=201 errorCode=",
                    messageIdForLog,
                    chatIdForLog,
                    subscription.UserId,
                    subscription.Id,
                    MaskEndpoint(subscription.Endpoint));
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                subscription.IsActive = false;
                subscription.LastFailureAt = now;
                subscription.UpdatedAt = now;
                subscription.FailureCount += 1;
                subscription.LastErrorCode = $"webpush:{(int)ex.StatusCode}";
                subscription.LastErrorMessage = NormalizeOptional(ex.Message, 2000);
                changed = true;

                _logger.LogWarning(
                    "PushSend messageId={MessageId} chatId={ChatId} recipientUserId={RecipientUserId} subscriptionId={SubscriptionId} endpoint={Endpoint} success=false statusCode={StatusCode} errorCode={ErrorCode}",
                    messageIdForLog,
                    chatIdForLog,
                    subscription.UserId,
                    subscription.Id,
                    MaskEndpoint(subscription.Endpoint),
                    (int)ex.StatusCode,
                    subscription.LastErrorCode);
            }
            catch (Exception ex)
            {
                subscription.LastFailureAt = now;
                subscription.UpdatedAt = now;
                subscription.FailureCount += 1;
                subscription.LastErrorCode = "push_send_failed";
                subscription.LastErrorMessage = NormalizeOptional(ex.Message, 2000);
                changed = true;

                _logger.LogWarning(
                    ex,
                    "PushSend messageId={MessageId} chatId={ChatId} recipientUserId={RecipientUserId} subscriptionId={SubscriptionId} endpoint={Endpoint} success=false statusCode=0 errorCode={ErrorCode}",
                    messageIdForLog,
                    chatIdForLog,
                    subscription.UserId,
                    subscription.Id,
                    MaskEndpoint(subscription.Endpoint),
                    subscription.LastErrorCode);
            }
        }

        if (changed)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return (subscriptions.Count, successful);
    }

    private bool CanSendPush()
    {
        return !string.IsNullOrWhiteSpace(_vapidPublicKey) && !string.IsNullOrWhiteSpace(_vapidPrivateKey);
    }

    private static string BuildBody(GetMessageDto message)
    {
        return message.Type switch
        {
            MessageType.Voice => "Voice message",
            MessageType.Media => "Media file",
            _ => string.IsNullOrWhiteSpace(message.Text) ? "New message" : message.Text.Trim(),
        };
    }

    private static string NormalizeRequired(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Push subscription field is required.");
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? MaskEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        var value = endpoint.Trim();
        if (value.Length <= 24)
        {
            return value;
        }

        return $"{value[..14]}...{value[^8..]}";
    }

    private void LogPushTrigger(string messageId, Guid? chatId, IReadOnlyCollection<Guid> recipientIds, int subscriptionsCount)
    {
        _logger.LogInformation(
            "PushTrigger messageId={MessageId} chatId={ChatId} recipientIds={RecipientIds} subscriptionsCount={SubscriptionsCount}",
            messageId,
            chatId,
            string.Join(",", recipientIds.Select(id => id.ToString("D"))),
            subscriptionsCount);
    }

    private void LogPushSuppression(
        string reason,
        string messageId,
        Guid? chatId,
        IReadOnlyCollection<Guid> recipientIds,
        int subscriptionsCount)
    {
        _logger.LogInformation(
            "PushSuppression send=false reason={Reason} messageId={MessageId} chatId={ChatId} recipientIds={RecipientIds} subscriptionsCount={SubscriptionsCount}",
            reason,
            messageId,
            chatId,
            string.Join(",", recipientIds.Select(id => id.ToString("D"))),
            subscriptionsCount);
    }
}

