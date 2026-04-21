namespace NETmessenger.Domain.Entities;

public interface IJwtService
{
    string GenerateToken(User user);
}