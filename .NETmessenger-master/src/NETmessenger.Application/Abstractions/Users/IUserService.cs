using NETmessenger.Contracts.Users;

namespace NETmessenger.Application.Abstractions.Users;

public interface IUserService
{
    Task<IReadOnlyCollection<GetUserDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<GetUserDto?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<GetUserDto> UpdateAsync(Guid userId, UpdateUserDto dto, CancellationToken cancellationToken);

    Task<AuthResponseDto> RegisterAsync(RegisterUserDto dto, CancellationToken cancellationToken);
    Task<AuthResponseDto> LoginAsync(LoginUserDto dto, CancellationToken cancellationToken);

}

