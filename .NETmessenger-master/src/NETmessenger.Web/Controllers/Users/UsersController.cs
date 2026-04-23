using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETmessenger.Application.Abstractions.Users;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Users;
using NETmessenger.Web.Security;

namespace NETmessenger.Web.Controllers.Users;

[ApiController]
[Route("api/users")]
public class UsersController(IUserService userService, IConfiguration configuration) : ControllerBase
{
    private readonly IConfiguration _configuration = configuration;

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyCollection<GetUserDto>>> GetAll(CancellationToken cancellationToken)
    {
        var users = await userService.GetAllAsync(cancellationToken);
        return Ok(users);
    }

    [HttpGet("search")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyCollection<GetUserDto>>> SearchByLogin(
        [FromQuery] string login,
        CancellationToken cancellationToken)
    {
        try
        {
            var users = await userService.SearchByLoginAsync(login, cancellationToken);
            return Ok(users);
        }
        catch (DomainValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{userId:guid}")]
    [Authorize]
    public async Task<ActionResult<GetUserDto>> GetById(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userService.GetByIdAsync(userId, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(
        [FromBody] RegisterUserDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await userService.RegisterAsync(dto, cancellationToken);
            AppendAuthCookie(result.Token);
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

            AppendAuthCookie(result.Token);

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
            var currentUserId = User.GetRequiredUserId();
            if (currentUserId != userId)
            {
                return Forbid();
            }

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

    private void AppendAuthCookie(string token)
    {
        var expirationHours = int.TryParse(
            _configuration["JwtSettings:TokenExpirationHours"],
            out var h) ? h : 12;

        Response.Cookies.Append("auth_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            IsEssential = true,
            Expires = DateTime.UtcNow.AddHours(expirationHours)
        });
    }
}
