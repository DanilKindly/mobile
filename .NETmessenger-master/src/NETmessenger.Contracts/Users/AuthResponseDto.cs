namespace NETmessenger.Contracts.Users;

public record AuthResponseDto(Guid UserId, string Nickname, string Token);

