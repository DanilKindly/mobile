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
    private const int MinPasswordLength = 6;

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
            .OrderBy(u => u.Login)
            .Select(u => new GetUserDto(u.Id, u.Login, u.Username))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<GetUserDto>> SearchByLoginAsync(string login, CancellationToken cancellationToken)
    {
        var normalizedLogin = NormalizeRequired(login, "Login is required.").ToLowerInvariant();

        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Login.ToLower().Contains(normalizedLogin))
            .OrderBy(u => u.Login)
            .Take(20)
            .Select(u => new GetUserDto(u.Id, u.Login, u.Username))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<GetUserDto?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new GetUserDto(u.Id, u.Login, u.Username))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterUserDto dto, CancellationToken cancellationToken)
    {
        var login = NormalizeRequired(dto.Login, "Login is required.");
        var username = NormalizeRequired(dto.Username, "Username is required.");
        var password = NormalizeRequired(dto.Password, "Password is required.");

        ValidateLogin(login);
        ValidatePassword(password);
        await EnsureLoginIsUniqueAsync(login, null, cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Login = login,
            Username = username,
            PasswordHash = _passwordHasher.GenerateHash(password)
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var token = _jwtService.GenerateToken(user);

        return new AuthResponseDto(user.Id, user.Login, user.Username, token);
    }

    public async Task<AuthResponseDto> LoginAsync(LoginUserDto dto, CancellationToken cancellationToken)
    {
        var login = NormalizeRequired(dto.Login, "Login is required.");
        var loweredLogin = login.ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Login.ToLower() == loweredLogin, cancellationToken);

        if (user is null)
        {
            throw new ResourceNotFoundException("User not found.");
        }

        if (!_passwordHasher.VerifyPassword(dto.Password, user.PasswordHash))
        {
            throw new DomainValidationException("Invalid credentials.");
        }

        var token = _jwtService.GenerateToken(user);

        return new AuthResponseDto(user.Id, user.Login, user.Username, token);
    }

    public async Task<GetUserDto> UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken)
    {
        var login = NormalizeRequired(dto.Login, "Login is required.");
        var username = NormalizeRequired(dto.Username, "Username is required.");

        ValidateLogin(login);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new ResourceNotFoundException($"User '{userId}' was not found.");

        await EnsureLoginIsUniqueAsync(login, userId, cancellationToken);

        user.Login = login;
        user.Username = username;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(user);
    }

    private async Task EnsureLoginIsUniqueAsync(string login, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var loweredLogin = login.ToLowerInvariant();

        var exists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u =>
                (!exceptUserId.HasValue || u.Id != exceptUserId.Value) &&
                u.Login.ToLower() == loweredLogin,
                cancellationToken);

        if (exists)
        {
            throw new ConflictException("Login is already taken.");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < MinPasswordLength)
        {
            throw new DomainValidationException($"Password must be at least {MinPasswordLength} characters long.");
        }
    }

    private static void ValidateLogin(string login)
    {
        if (login.Length < 3)
        {
            throw new DomainValidationException("Login must be at least 3 characters long.");
        }

        if (!login.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-'))
        {
            throw new DomainValidationException("Login can contain only letters, digits, underscore and dash.");
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

    private static GetUserDto MapToDto(User user)
    {
        return new GetUserDto(user.Id, user.Login, user.Username);
    }
}
