using Microsoft.EntityFrameworkCore;
using NETmessenger.Application.Abstractions.Auth;
using NETmessenger.Application.Abstractions.Users;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Users;
using NETmessenger.Domain.Entities;
using NETmessenger.Infrastructure.Persistence;

namespace NETmessenger.Infrastructure.Services.Users;

public sealed class UserService : IUserService
{

    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;

    public UserService(
        AppDbContext dbContext, 
        IPasswordHasher passwordHasher, 
        IJwtService jwtService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
    }
    public async Task<IReadOnlyCollection<GetUserDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .OrderBy(u => u.Nickname)
            .Select(u => new GetUserDto(u.Id, u.Nickname, u.Name, u.PhoneNumber))
            .ToArrayAsync(cancellationToken);
    }


    public async Task<GetUserDto?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new GetUserDto(u.Id, u.Nickname, u.Name, u.PhoneNumber))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterUserDto dto, CancellationToken cancellationToken)
    {
        var nickname = NormalizeRequired(dto.Nickname, "Nickname is required.");
        var name = NormalizeRequired(dto.Name, "Name is required.");

        await EnsureNicknameIsUniqueAsync(nickname, null, cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Nickname = nickname,
            Name = name,
            PhoneNumber = NormalizeOptional(dto.PhoneNumber),
            Password = _passwordHasher.GenerateHash(dto.Password) // Хешируем пароль
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var token = _jwtService.GenerateToken(user);

        return new AuthResponseDto(user.Id, user.Nickname, token);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginUserDto dto, CancellationToken cancellationToken)
    {
        var nickname = NormalizeRequired(dto.Nickname, "Nickname is required.");
        var loweredNickname = nickname.ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Nickname.ToLower() == loweredNickname, cancellationToken);

        if (user is null || string.IsNullOrEmpty(user.Password))
        {
            throw new ResourceNotFoundException("User not found or invalid credentials.");
        }

        if (!_passwordHasher.VerifyPassword(dto.Password, user.Password))
        {
            throw new DomainValidationException("Invalid password.");
        }

        var token = _jwtService.GenerateToken(user);

        return new AuthResponseDto(user.Id, user.Nickname, token);
    }
    

    public async Task<GetUserDto> UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken)
    {
        var nickname = NormalizeRequired(dto.Nickname, "Nickname is required.");
        var name = NormalizeRequired(dto.Name, "Name is required.");

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new ResourceNotFoundException($"User '{userId}' was not found.");

        await EnsureNicknameIsUniqueAsync(nickname, userId, cancellationToken);

        user.Nickname = nickname;
        user.Name = name;
        user.PhoneNumber = NormalizeOptional(dto.PhoneNumber);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(user);
    }

    private async Task EnsureNicknameIsUniqueAsync(string nickname, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var loweredNickname = nickname.ToLowerInvariant();

        var exists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u =>
                (!exceptUserId.HasValue || u.Id != exceptUserId.Value) &&
                u.Nickname.ToLower() == loweredNickname,
                cancellationToken);

        if (exists)
        {
            throw new ConflictException("Nickname is already taken.");
        }
    }

    private static string NormalizeRequired(string value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException(errorMessage);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static GetUserDto MapToDto(User user)
    {
        return new GetUserDto(user.Id, user.Nickname, user.Name, user.PhoneNumber);
    }
}
