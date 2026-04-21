namespace NETmessenger.Contracts.Users;

public record GetUserDto(Guid UserId, string Nickname, string Name, string? PhoneNumber);
