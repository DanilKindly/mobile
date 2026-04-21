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
                .Include(c => c.Participants)
                .Where(c => !c.IsGroup && c.Participants.Count == 2)
                .FirstOrDefaultAsync(c =>
                    c.Participants.Any(p => p.Id == firstParticipantId) &&
                    c.Participants.Any(p => p.Id == secondParticipantId),
                    cancellationToken);

            if (existingDirectChat is not null)
            {
                return MapToDto(existingDirectChat);
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

        return MapToDto(chat);
    }

    public async Task<GetChatDto?> GetByIdAsync(Guid chatId, CancellationToken cancellationToken)
    {
        var chat = await dbContext.Chats
            .AsNoTracking()
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == chatId, cancellationToken);

        return chat is null ? null : MapToDto(chat);
    }

    public async Task<IReadOnlyCollection<GetChatDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userExists = await dbContext.Users.AsNoTracking().AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
        {
            throw new ResourceNotFoundException($"User '{userId}' was not found.");
        }

        var chats = await dbContext.Chats
            .AsNoTracking()
            .Include(c => c.Participants)
            .Where(c => c.Participants.Any(p => p.Id == userId))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return chats.Select(MapToDto).ToArray();
    }

    private static string? NormalizeName(string? name)
    {
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private static GetChatDto MapToDto(Chat chat)
    {
        return new GetChatDto(
            chat.Id,
            chat.CreatedAt,
            chat.IsGroup,
            chat.Name,
            chat.Participants.Select(p => p.Id).ToArray());
    }
}
