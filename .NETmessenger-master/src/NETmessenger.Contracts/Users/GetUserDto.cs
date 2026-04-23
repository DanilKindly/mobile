namespace NETmessenger.Contracts.Users;

public record GetUserDto(Guid UserId, string Username, DateTime LastSeenAt);
