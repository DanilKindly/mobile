using System;
using System.CodeDom.Compiler;

namespace NETmessenger.Application.Abstractions.Auth;

public interface IPasswordHasher
{
    string GenerateHash(string password);
    bool VerifyPassword(string password, string hashedPassword);
}
