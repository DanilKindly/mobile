using System.IO;
using System.Linq.Expressions;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using NETmessenger.Application.Abstractions.Files;
using NETmessenger.Application.Abstractions.Messages;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Chats;
using NETmessenger.Contracts.Messages;
using NETmessenger.Domain.Entities;
using NETmessenger.Infrastructure.Persistence;

namespace NETmessenger.Infrastructure.Services.Messages;

public sealed class MessageService(AppDbContext dbContext, IVoiceStorage voiceStorage, IMediaStorage mediaStorage) : IMessageService
{
    private static long _versionSeed = DateTime.UtcNow.Ticks;

    public async Task<IReadOnlyCollection<GetMessageDto>> GetByChatIdAsync(Guid chatId, CancellationToken cancellationToken)
    {
        var chatExists = await dbContext.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId, cancellationToken);
        if (!chatExists)
        {
            throw new ResourceNotFoundException($"Chat '{chatId}' was not found.");
        }

        return await dbContext.Messages
            .AsNoTracking()
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .Select(MapProjection())
            .ToArrayAsync(cancellationToken);
    }

    public async Task<GetMessagesPageDto> GetPageByChatIdAsync(
        Guid chatId,
        long? beforeVersion,
        int limit,
        CancellationToken cancellationToken)
    {
        var chatExists = await dbContext.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId, cancellationToken);
        if (!chatExists)
        {
            throw new ResourceNotFoundException($"Chat '{chatId}' was not found.");
        }

        var normalizedLimit = Math.Clamp(limit, 1, 100);

        var query = dbContext.Messages
            .AsNoTracking()
            .Where(m => m.ChatId == chatId);

        if (beforeVersion.HasValue)
        {
            query = query.Where(m => m.Version < beforeVersion.Value);
        }

        var pageWindow = await query
            .OrderByDescending(m => m.Version)
            .ThenByDescending(m => m.Id)
            .Take(normalizedLimit + 1)
            .Select(MapProjection())
            .ToListAsync(cancellationToken);

        var hasMoreOlder = pageWindow.Count > normalizedLimit;
        if (hasMoreOlder)
        {
            pageWindow = pageWindow.Take(normalizedLimit).ToList();
        }

        var ordered = pageWindow
            .OrderBy(m => m.Version)
            .ThenBy(m => m.MessageId)
            .ToArray();

        var nextBeforeVersion = ordered.Length > 0
            ? ordered.Min(m => m.Version)
            : beforeVersion;

        return new GetMessagesPageDto(ordered, hasMoreOlder, nextBeforeVersion);
    }

    public async Task<GetMessageDto> SendAsync(Guid chatId, SendMessageDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
        {
            throw new DomainValidationException("Message text is required.");
        }

        await EnsureParticipantAsync(chatId, dto.SenderUserId, cancellationToken);

        var normalizedClientMessageId = NormalizeClientMessageId(dto.ClientMessageId);
        if (!string.IsNullOrWhiteSpace(normalizedClientMessageId))
        {
            var existing = await dbContext.Messages
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.ChatId == chatId &&
                         m.SenderId == dto.SenderUserId &&
                         m.ClientMessageId == normalizedClientMessageId,
                    cancellationToken);

            if (existing is not null)
            {
                return MapToDto(existing);
            }
        }

        var now = DateTime.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = dto.SenderUserId,
            ClientMessageId = normalizedClientMessageId,
            SentAtClient = dto.SentAtClient,
            Version = NextVersion(),
            Type = Domain.Entities.MessageType.Text,
            Text = dto.Text.Trim(),
            SentAt = now
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(message);
    }

    public async Task<IReadOnlyCollection<Guid>> MarkMessagesAsReadAsync(
        Guid chatId,
        Guid readerUserId,
        CancellationToken cancellationToken)
    {
        var chatExists = await dbContext.Chats
            .AsNoTracking()
            .AnyAsync(c => c.Id == chatId, cancellationToken);

        if (!chatExists)
        {
            throw new ResourceNotFoundException($"Chat '{chatId}' was not found.");
        }

        var readerInChat = await dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Id == chatId)
            .SelectMany(c => c.Participants)
            .AnyAsync(p => p.Id == readerUserId, cancellationToken);

        if (!readerInChat)
        {
            throw new DomainValidationException("Reader is not a participant of this chat.");
        }

        var unreadMessages = await dbContext.Messages
            .Where(m => m.ChatId == chatId && m.SenderId != readerUserId && !m.IsRead)
            .ToListAsync(cancellationToken);

        if (unreadMessages.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var readAt = DateTime.UtcNow;
        foreach (var message in unreadMessages)
        {
            message.IsRead = true;
            message.ReadAt = readAt;
            message.Version = NextVersion();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return unreadMessages.Select(m => m.Id).ToArray();
    }

    public async Task<GetMessageDto> SendVoiceAsync(
        Guid chatId,
        Guid senderUserId,
        Stream audioStream,
        string originalFileName,
        string contentType,
        long length,
        int? durationSeconds,
        CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            throw new DomainValidationException("Audio file is required.");
        }

        await EnsureParticipantAsync(chatId, senderUserId, cancellationToken);
        var stored = await voiceStorage.SaveAsync(audioStream, originalFileName, contentType, cancellationToken);

        var now = DateTime.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = senderUserId,
            Version = NextVersion(),
            Type = Domain.Entities.MessageType.Voice,
            Text = null,
            AudioUrl = stored.Url,
            AudioContentType = stored.ContentType,
            AudioDurationSeconds = durationSeconds,
            AudioSizeBytes = stored.SizeBytes,
            SentAt = now
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(message);
    }

    public async Task<GetMessageDto> SendMediaAsync(
        Guid chatId,
        Guid senderUserId,
        Stream fileStream,
        string originalFileName,
        string contentType,
        long length,
        CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            throw new DomainValidationException("File is required.");
        }

        await EnsureParticipantAsync(chatId, senderUserId, cancellationToken);
        var stored = await mediaStorage.SaveAsync(fileStream, originalFileName, contentType, cancellationToken);

        var now = DateTime.UtcNow;
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = senderUserId,
            Version = NextVersion(),
            Type = Domain.Entities.MessageType.Media,
            Text = null,
            MediaUrl = stored.Url,
            MediaContentType = stored.ContentType,
            MediaFileName = stored.FileName,
            MediaSizeBytes = stored.SizeBytes,
            SentAt = now
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(message);
    }

    public async Task<MessageChangesDto> GetChangesByUserAsync(Guid userId, long cursor, int limit, CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 500);

        var userExists = await dbContext.Users.AsNoTracking().AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new ResourceNotFoundException($"User '{userId}' was not found.");
        }

        var chatIdsQuery = dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Participants.Any(p => p.Id == userId))
            .Select(c => c.Id);

        var changedMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(m => chatIdsQuery.Contains(m.ChatId) && m.Version > cursor)
            .OrderBy(m => m.Version)
            .ThenBy(m => m.Id)
            .Take(normalizedLimit)
            .Select(MapProjection())
            .ToArrayAsync(cancellationToken);

        var changedChatIds = changedMessages.Select(m => m.ChatId).Distinct().ToArray();

        var changedChats = await dbContext.Chats
            .AsNoTracking()
            .Where(c => changedChatIds.Contains(c.Id))
            .Select(c => new GetChatDto(
                c.Id,
                c.CreatedAt,
                c.IsGroup,
                c.Name,
                c.Participants.Select(p => p.Id).ToArray(),
                c.Messages.OrderByDescending(m => m.SentAt).Select(m => m.Text).FirstOrDefault(),
                c.Messages.OrderByDescending(m => m.SentAt).Select(m => (Contracts.Messages.MessageType?)m.Type).FirstOrDefault(),
                c.Messages.OrderByDescending(m => m.SentAt).Select(m => (Guid?)m.SenderId).FirstOrDefault(),
                c.Messages.OrderByDescending(m => m.SentAt).Select(m => (DateTime?)m.SentAt).FirstOrDefault()))
            .ToArrayAsync(cancellationToken);

        var nextCursor = changedMessages.Length > 0 ? changedMessages.Max(m => m.Version) : cursor;
        return new MessageChangesDto(nextCursor, changedMessages, changedChats);
    }

    private async Task EnsureParticipantAsync(Guid chatId, Guid userId, CancellationToken cancellationToken)
    {
        var chatExists = await dbContext.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId, cancellationToken);
        if (!chatExists)
        {
            throw new ResourceNotFoundException($"Chat '{chatId}' was not found.");
        }

        var senderExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId, cancellationToken);

        if (!senderExists)
        {
            throw new ResourceNotFoundException($"User '{userId}' was not found.");
        }

        var senderInChat = await dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Id == chatId)
            .SelectMany(c => c.Participants)
            .AnyAsync(p => p.Id == userId, cancellationToken);

        if (!senderInChat)
        {
            throw new DomainValidationException("Sender is not a participant of this chat.");
        }
    }

    private static string? NormalizeClientMessageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }

    private static long NextVersion()
    {
        return Interlocked.Increment(ref _versionSeed);
    }

    private static Expression<Func<Message, GetMessageDto>> MapProjection()
    {
        return m => new GetMessageDto(
            m.Id,
            m.ChatId,
            m.SenderId,
            m.ClientMessageId,
            m.SentAtClient,
            m.Version,
            m.IsRead ? MessageDeliveryStatus.Read : MessageDeliveryStatus.Sent,
            (Contracts.Messages.MessageType)m.Type,
            m.Text,
            m.AudioUrl,
            m.AudioContentType,
            m.AudioDurationSeconds,
            m.AudioSizeBytes,
            m.MediaUrl,
            m.MediaContentType,
            m.MediaFileName,
            m.MediaSizeBytes,
            m.IsRead,
            m.ReadAt,
            m.SentAt);
    }

    private static GetMessageDto MapToDto(Message message)
    {
        return new GetMessageDto(
            message.Id,
            message.ChatId,
            message.SenderId,
            message.ClientMessageId,
            message.SentAtClient,
            message.Version,
            message.IsRead ? MessageDeliveryStatus.Read : MessageDeliveryStatus.Sent,
            (Contracts.Messages.MessageType)message.Type,
            message.Text,
            message.AudioUrl,
            message.AudioContentType,
            message.AudioDurationSeconds,
            message.AudioSizeBytes,
            message.MediaUrl,
            message.MediaContentType,
            message.MediaFileName,
            message.MediaSizeBytes,
            message.IsRead,
            message.ReadAt,
            message.SentAt);
    }
}
