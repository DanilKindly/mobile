namespace NETmessenger.Contracts.Users;

public record GetUserDto(Guid UserId, string Login, string Username, DateTime LastSeenAt);
