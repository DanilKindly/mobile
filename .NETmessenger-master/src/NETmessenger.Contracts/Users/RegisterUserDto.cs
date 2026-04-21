namespace NETmessenger.Contracts.Users;

public record RegisterUserDto(string Nickname, string Name, string? PhoneNumber, string Password);

