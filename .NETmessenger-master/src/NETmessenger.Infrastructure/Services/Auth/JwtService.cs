using System.IdentityModel.Tokens.Jwt;           
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;            
using NETmessenger.Application.Abstractions.Auth;
using NETmessenger.Domain.Entities;

namespace NETmessenger.Infrastructure.Services.Auth;

public class JwtService : IJwtService  
{
    private readonly IConfiguration _configuration;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"] 
            ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured");
        var expiredHours = int.Parse(jwtSettings["TokenExpirationHours"] ?? "12");


        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        Claim[] claims =
        [
            new("user_id", user.Id.ToString()),
            new("nickname", user.Nickname),
        ];

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiredHours),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}