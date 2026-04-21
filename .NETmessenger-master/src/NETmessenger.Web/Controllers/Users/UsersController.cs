using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETmessenger.Application.Abstractions.Users;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Users;

namespace NETmessenger.Web.Controllers.Users;

[ApiController]
[Route("api/users")]
public class UsersController(IUserService userService, IConfiguration configuration) : ControllerBase
{

    private readonly IConfiguration _configuration = configuration;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<GetUserDto>>> GetAll(CancellationToken cancellationToken)
    {
        var users = await userService.GetAllAsync(cancellationToken);
        return Ok(users);
    }

    [HttpGet("{userId:guid}")]
    [Authorize]
    public async Task<ActionResult<GetUserDto>> GetById(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(userId, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<GetUserDto>> Create(
        [FromBody] CreateUserDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            // Backward-compatible endpoint for old frontend flow.
            var registerResult = await userService.RegisterAsync(
                new RegisterUserDto(dto.Nickname, dto.Name, dto.PhoneNumber, dto.Nickname),
                cancellationToken);

            var createdUser = await userService.GetByIdAsync(registerResult.UserId, cancellationToken);
            return createdUser is null ? NotFound() : Ok(createdUser);
        }
        catch (ConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (DomainValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }


    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(
        [FromBody] RegisterUserDto dto, 
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await userService.RegisterAsync(dto, cancellationToken);
            return Ok(result);
        }
        catch (ConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (DomainValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(
        [FromBody] LoginUserDto dto, 
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await userService.LoginAsync(dto, cancellationToken);

            var expirationHours = int.TryParse(
                _configuration["JwtSettings:TokenExpirationHours"], 
                out var h) ? h : 12;

            Response.Cookies.Append("auth_token", result.Token, new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTime.UtcNow.AddHours(expirationHours)
            });

            return Ok(result);
        }
        catch (ResourceNotFoundException)
        {
            return Unauthorized(new { error = "Invalid credentials" });
        }
        catch (DomainValidationException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }


    [HttpPut("{userId:guid}")]
    [Authorize]
    public async Task<ActionResult<GetUserDto>> Update(Guid userId, [FromBody] UpdateUserDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var user = await userService.UpdateAsync(userId, dto, cancellationToken);
            return Ok(user);
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
        catch (ConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (DomainValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
