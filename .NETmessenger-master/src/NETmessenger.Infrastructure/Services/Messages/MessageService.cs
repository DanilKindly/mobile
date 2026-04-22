using System.IO;
using Microsoft.EntityFrameworkCore;
using NETmessenger.Application.Abstractions.Files;
using NETmessenger.Application.Abstractions.Messages;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Messages;
using NETmessenger.Domain.Entities;
using NETmessenger.Infrastructure.Persistence;

namespace NETmessenger.Infrastructure.Services.Messages;

public sealed class MessageService(AppDbContext dbContext, IVoiceStorage voiceStorage, IMediaStorage mediaStorage) : IMessageService
{
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
            .Select(m => new GetMessageDto(
                m.Id,
                m.ChatId,
                m.SenderId,
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
                m.SentAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<GetMessageDto> SendAsync(Guid chatId, SendMessageDto dto, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
        {
            throw new DomainValidationException("Message text is required.");
        }

        var chatExists = await dbContext.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId, cancellationToken);
        if (!chatExists)
        {
            throw new ResourceNotFoundException($"Chat '{chatId}' was not found.");
        }

        var senderExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == dto.SenderUserId, cancellationToken);

        if (!senderExists)
        {
            throw new ResourceNotFoundException($"User '{dto.SenderUserId}' was not found.");
        }

        var senderInChat = await dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Id == chatId)
            .SelectMany(c => c.Participants)
            .AnyAsync(p => p.Id == dto.SenderUserId, cancellationToken);

        if (!senderInChat)
        {
            throw new DomainValidationException("Sender is not a participant of this chat.");
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = dto.SenderUserId,
            Type = Domain.Entities.MessageType.Text,
            Text = dto.Text.Trim(),
            SentAt = DateTime.UtcNow
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

        var chatExists = await dbContext.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId, cancellationToken);
        if (!chatExists)
        {
            throw new ResourceNotFoundException($"Chat '{chatId}' was not found.");
        }

        var senderExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == senderUserId, cancellationToken);

        if (!senderExists)
        {
            throw new ResourceNotFoundException($"User '{senderUserId}' was not found.");
        }

        var senderInChat = await dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Id == chatId)
            .SelectMany(c => c.Participants)
            .AnyAsync(p => p.Id == senderUserId, cancellationToken);

        if (!senderInChat)
        {
            throw new DomainValidationException("Sender is not a participant of this chat.");
        }

        var stored = await voiceStorage.SaveAsync(audioStream, originalFileName, contentType, cancellationToken);

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = senderUserId,
            Type = Domain.Entities.MessageType.Voice,
            Text = null,
            AudioUrl = stored.Url,
            AudioContentType = stored.ContentType,
            AudioDurationSeconds = durationSeconds,
            AudioSizeBytes = stored.SizeBytes,
            SentAt = DateTime.UtcNow
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

        var chatExists = await dbContext.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId, cancellationToken);
        if (!chatExists)
        {
            throw new ResourceNotFoundException($"Chat '{chatId}' was not found.");
        }

        var senderExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == senderUserId, cancellationToken);

        if (!senderExists)
        {
            throw new ResourceNotFoundException($"User '{senderUserId}' was not found.");
        }

        var senderInChat = await dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Id == chatId)
            .SelectMany(c => c.Participants)
            .AnyAsync(p => p.Id == senderUserId, cancellationToken);

        if (!senderInChat)
        {
            throw new DomainValidationException("Sender is not a participant of this chat.");
        }

        var stored = await mediaStorage.SaveAsync(fileStream, originalFileName, contentType, cancellationToken);

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = senderUserId,
            Type = Domain.Entities.MessageType.Media,
            Text = null,
            MediaUrl = stored.Url,
            MediaContentType = stored.ContentType,
            MediaFileName = stored.FileName,
            MediaSizeBytes = stored.SizeBytes,
            SentAt = DateTime.UtcNow
        };

        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(message);
    }

    private static GetMessageDto MapToDto(Message message)
    {
        return new GetMessageDto(
            message.Id,
            message.ChatId,
            message.SenderId,
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
