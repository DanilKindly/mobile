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
        if (!CanSendPush() || message is null)
        {
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
                return;
            }

            var recipientIds = chatData.Participants
                .Where(p => p.Id != message.SenderUserId)
                .Select(p => p.Id)
                .Distinct()
                .ToArray();

            if (recipientIds.Length == 0)
            {
                return;
            }

            var subscriptions = await _dbContext.PushSubscriptions
                .Where(x => x.IsActive && recipientIds.Contains(x.UserId))
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
            {
                return;
            }

            var senderDisplayName = chatData.Participants
                .FirstOrDefault(p => p.Id == message.SenderUserId)
                ?.Username ?? "Новое сообщение";

            var title = chatData.IsGroup
                ? (string.IsNullOrWhiteSpace(chatData.Name) ? "Новое сообщение в группе" : chatData.Name.Trim())
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

            var client = new WebPushClient();
            var vapidDetails = new VapidDetails(_vapidSubject, _vapidPublicKey!, _vapidPrivateKey!);
            var now = DateTime.UtcNow;
            var changed = false;

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

                    _logger.LogInformation(
                        "Push delivered. messageId={MessageId} recipientUserId={RecipientUserId} subscriptionId={SubscriptionId} endpoint={Endpoint}",
                        message.MessageId,
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
                        "Push subscription invalidated. messageId={MessageId} recipientUserId={RecipientUserId} subscriptionId={SubscriptionId} status={StatusCode}",
                        message.MessageId,
                        subscription.UserId,
                        subscription.Id,
                        ex.StatusCode);
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
                        "Failed to deliver push. messageId={MessageId} recipientUserId={RecipientUserId} subscriptionId={SubscriptionId}",
                        message.MessageId,
                        subscription.UserId,
                        subscription.Id);
                }
            }

            if (changed)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push notification send pipeline failed for message {MessageId}", message.MessageId);
        }
    }

    private bool CanSendPush()
    {
        return !string.IsNullOrWhiteSpace(_vapidPublicKey) && !string.IsNullOrWhiteSpace(_vapidPrivateKey);
    }

    private static string BuildBody(GetMessageDto message)
    {
        return message.Type switch
        {
            MessageType.Voice => "Голосовое сообщение",
            MessageType.Media => "Медиафайл",
            _ => string.IsNullOrWhiteSpace(message.Text) ? "Новое сообщение" : message.Text.Trim(),
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
}
