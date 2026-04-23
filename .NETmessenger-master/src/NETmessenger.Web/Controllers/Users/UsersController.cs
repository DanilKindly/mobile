using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NETmessenger.Application.Abstractions.Security;
using NETmessenger.Application.Abstractions.Users;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Users;
using NETmessenger.Web.Security;

namespace NETmessenger.Web.Controllers.Users;

[ApiController]
[Route("api/users")]
public class UsersController(
    IUserService userService,
    ISecurityAuditService auditService,
    IAbuseGuard abuseGuard,
    IConfiguration configuration) : ControllerBase
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
    [EnableRateLimiting("user-search")]
    public async Task<ActionResult<IReadOnlyCollection<GetUserDto>>> SearchByLogin(
        [FromQuery] string login,
        CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
            {
                await AuditAsync("blocked_user_search", "denied", currentUserId, "users", "search", "user is blocked", cancellationToken);
                return Forbid();
            }

            var users = await userService.SearchByLoginAsync(login, cancellationToken);
            await AuditAsync("user_search", "success", currentUserId, "users", "search", $"results={users.Count}", cancellationToken);
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
    [EnableRateLimiting("auth-register")]
    public async Task<ActionResult<AuthResponseDto>> Register(
        [FromBody] RegisterUserDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await userService.RegisterAsync(dto, cancellationToken);
            AppendAuthCookie(result.Token);
            await AuditAsync("auth_register", "success", result.UserId, "users", result.UserId.ToString("D"), null, cancellationToken);
            return Ok(result);
        }
        catch (ConflictException ex)
        {
            await AuditAsync("auth_register", "failed", null, "users", null, "conflict", cancellationToken);
            return Conflict(new { error = ex.Message });
        }
        catch (DomainValidationException ex)
        {
            await AuditAsync("auth_register", "failed", null, "users", null, "validation_failed", cancellationToken);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<AuthResponseDto>> Login(
        [FromBody] LoginUserDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await userService.LoginAsync(dto, cancellationToken);
            if (await abuseGuard.IsBlockedAsync(result.UserId, cancellationToken))
            {
                await AuditAsync("auth_login_blocked", "denied", result.UserId, "users", result.UserId.ToString("D"), "user is blocked", cancellationToken);
                return Forbid();
            }

            AppendAuthCookie(result.Token);
            await AuditAsync("auth_login", "success", result.UserId, "users", result.UserId.ToString("D"), null, cancellationToken);

            return Ok(result);
        }
        catch (ResourceNotFoundException)
        {
            await AuditAsync("auth_login", "failed", null, "users", null, "invalid_credentials", cancellationToken);
            return Unauthorized(new { error = "Invalid credentials" });
        }
        catch (DomainValidationException ex)
        {
            await AuditAsync("auth_login", "failed", null, "users", null, "invalid_credentials", cancellationToken);
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
                await AuditAsync("user_update_forbidden", "denied", currentUserId, "users", userId.ToString("D"), "cannot update another user", cancellationToken);
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

    private Task AuditAsync(
        string eventType,
        string outcome,
        Guid? userId,
        string? resourceType,
        string? resourceId,
        string? reason,
        CancellationToken cancellationToken)
    {
        return auditService.RecordAsync(new SecurityAuditEventInput(
            eventType,
            outcome,
            outcome == "success" ? "info" : "warning",
            userId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            resourceType,
            resourceId,
            reason),
            cancellationToken);
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
