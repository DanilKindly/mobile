namespace NETmessenger.Contracts.Users;

public record AuthResponseDto(
    Guid UserId,
    string Login,
    string Username,
    string Token,
    DateTime LastSeenAt,
    string? AvatarUrl,
    DateTime? AvatarUpdatedAt);

