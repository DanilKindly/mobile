using Microsoft.EntityFrameworkCore;
using NETmessenger.Application.Abstractions.Chats;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Chats;
using NETmessenger.Domain.Entities;
using NETmessenger.Infrastructure.Persistence;

namespace NETmessenger.Infrastructure.Services.Chats;

public sealed class ChatService(AppDbContext dbContext) : IChatService
{
    public async Task<GetChatDto> CreateAsync(CreateChatDto dto, CancellationToken cancellationToken)
    {
        var requestedParticipantIds = (dto.ParticipantUserIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .ToArray();

        var participantUserIds = requestedParticipantIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (participantUserIds.Length == 0)
        {
            throw new DomainValidationException("Chat must contain at least one participant.");
        }

        if (!dto.IsGroup && participantUserIds.Length != 2)
        {
            throw new DomainValidationException("Direct chat must contain exactly two participants.");
        }

        if (!dto.IsGroup && requestedParticipantIds.Length != participantUserIds.Length)
        {
            throw new DomainValidationException("You cannot create a direct chat with yourself.");
        }

        var participants = await dbContext.Users
            .Where(u => participantUserIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        if (participants.Count != participantUserIds.Length)
        {
            var missingId = participantUserIds.First(id => participants.All(u => u.Id != id));
            throw new ResourceNotFoundException($"User '{missingId}' was not found.");
        }

        if (!dto.IsGroup)
        {
            var firstParticipantId = participantUserIds[0];
            var secondParticipantId = participantUserIds[1];

            var existingDirectChat = await dbContext.Chats
                .AsNoTracking()
                .Where(c => !c.IsGroup && c.Participants.Count == 2)
                .Where(c =>
                    c.Participants.Any(p => p.Id == firstParticipantId) &&
                    c.Participants.Any(p => p.Id == secondParticipantId))
                .FirstOrDefaultAsync(cancellationToken);

            if (existingDirectChat is not null)
            {
                return await BuildChatDto(existingDirectChat.Id, cancellationToken)
                    ?? throw new ResourceNotFoundException($"Chat '{existingDirectChat.Id}' was not found.");
            }
        }

        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            IsGroup = dto.IsGroup,
            Name = NormalizeName(dto.Name),
            Participants = participants
        };

        dbContext.Chats.Add(chat);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new GetChatDto(
            chat.Id,
            chat.CreatedAt,
            chat.IsGroup,
            chat.Name,
            chat.Participants.Select(p => p.Id).ToArray(),
            null,
            null,
            null,
            null);
    }

    public async Task<GetChatDto?> GetByIdAsync(Guid chatId, CancellationToken cancellationToken)
    {
        return await BuildChatDto(chatId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<GetChatDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userExists = await dbContext.Users.AsNoTracking().AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new ResourceNotFoundException($"User '{userId}' was not found.");
        }

        await CleanupEmptyDirectDuplicateChatsForUserAsync(userId, cancellationToken);

        var chats = await dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Participants.Any(p => p.Id == userId))
            .Select(c => new
            {
                c.Id,
                c.CreatedAt,
                c.IsGroup,
                c.Name,
                ParticipantUserIds = c.Participants.Select(p => p.Id).ToArray(),
                LastMessage = c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => new
                    {
                        m.Text,
                        m.Type,
                        m.SenderId,
                        m.SentAt
                    })
                    .FirstOrDefault()
            })
            .OrderByDescending(c => c.LastMessage != null ? c.LastMessage.SentAt : c.CreatedAt)
            .ToListAsync(cancellationToken);

        var normalizedChats = chats
            .Select(c => new GetChatDto(
                c.Id,
                c.CreatedAt,
                c.IsGroup,
                c.Name,
                c.ParticipantUserIds,
                c.LastMessage != null ? c.LastMessage.Text : null,
                c.LastMessage != null ? (Contracts.Messages.MessageType?)c.LastMessage.Type : null,
                c.LastMessage?.SenderId,
                c.LastMessage?.SentAt))
            .ToArray();

        return DeduplicateDirectChats(normalizedChats);
    }

    private static string? NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private Task<GetChatDto?> BuildChatDto(Guid chatId, CancellationToken cancellationToken)
    {
        return dbContext.Chats
            .AsNoTracking()
            .Where(c => c.Id == chatId)
            .Select(c => new GetChatDto(
                c.Id,
                c.CreatedAt,
                c.IsGroup,
                c.Name,
                c.Participants.Select(p => p.Id).ToArray(),
                c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => m.Text)
                    .FirstOrDefault(),
                c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => (Contracts.Messages.MessageType?)m.Type)
                    .FirstOrDefault(),
                c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => (Guid?)m.SenderId)
                    .FirstOrDefault(),
                c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => (DateTime?)m.SentAt)
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task CleanupEmptyDirectDuplicateChatsForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var directChats = await dbContext.Chats
            .Where(c => !c.IsGroup && c.Participants.Any(p => p.Id == userId))
            .Select(c => new
            {
                c.Id,
                c.CreatedAt,
                ParticipantUserIds = c.Participants.Select(p => p.Id).ToArray(),
                HasMessages = c.Messages.Any(),
                LastMessageSentAt = c.Messages
                    .OrderByDescending(m => m.SentAt)
                    .Select(m => (DateTime?)m.SentAt)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var duplicateGroups = directChats
            .GroupBy(c => BuildDirectPairKey(c.ParticipantUserIds))
            .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1);

        var emptyDuplicateIdsToDelete = new List<Guid>();

        foreach (var group in duplicateGroups)
        {
            var canonical = group
                .OrderByDescending(c => c.LastMessageSentAt.HasValue)
                .ThenByDescending(c => c.LastMessageSentAt)
                .ThenByDescending(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .First();

            var emptyDuplicates = group
                .Where(c => c.Id != canonical.Id && !c.HasMessages)
                .Select(c => c.Id);

            emptyDuplicateIdsToDelete.AddRange(emptyDuplicates);
        }

        if (emptyDuplicateIdsToDelete.Count == 0)
        {
            return;
        }

        var chatsToDelete = await dbContext.Chats
            .Where(c => emptyDuplicateIdsToDelete.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (chatsToDelete.Count == 0)
        {
            return;
        }

        dbContext.Chats.RemoveRange(chatsToDelete);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyCollection<GetChatDto> DeduplicateDirectChats(IReadOnlyCollection<GetChatDto> chats)
    {
        var grouped = chats
            .GroupBy(c => c.IsGroup ? $"group:{c.ChatId:D}" : $"direct:{BuildDirectPairKey(c.ParticipantUserIds)}")
            .Select(group =>
            {
                if (group.First().IsGroup)
                {
                    return group.First();
                }

                return group
                    .OrderByDescending(c => c.LastMessageSentAt.HasValue)
                    .ThenByDescending(c => c.LastMessageSentAt)
                    .ThenByDescending(c => c.CreatedAt)
                    .ThenBy(c => c.ChatId)
                    .First();
            })
            .OrderByDescending(c => c.LastMessageSentAt ?? c.CreatedAt)
            .ToArray();

        return grouped;
    }

    private static string BuildDirectPairKey(IEnumerable<Guid> participantUserIds)
    {
        var normalized = participantUserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => id.ToString("D"))
            .ToArray();

        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(":", normalized);
    }
}
