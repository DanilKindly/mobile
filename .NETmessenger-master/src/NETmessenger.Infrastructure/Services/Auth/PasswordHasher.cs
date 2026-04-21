using NETmessenger.Application.Abstractions.Auth;

namespace NETmessenger.Infrastructure.Services;

public class PasswordHasher: IPasswordHasher
{
    public string GenerateHash(string password) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(password);
    public bool VerifyPassword( string password, string hashedPassword) =>
        BCrypt.Net.BCrypt.EnhancedVerify(password, hashedPassword);
}

