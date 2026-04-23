namespace NETmessenger.Contracts.Users;

public record CurrentUserDto(Guid UserId, string Login, string Username, DateTime LastSeenAt);
