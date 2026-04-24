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
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        await AuditAsync(
            "user_directory_list_denied",
            "denied",
            User.TryGetUserId(out var userId) ? userId : null,
            "users",
            "all",
            "mass user directory is disabled",
            cancellationToken);

        return StatusCode(StatusCodes.Status410Gone, new { error = "User directory is disabled." });
    }

    [HttpGet("participants")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyCollection<GetUserDto>>> GetVisibleParticipants(
        [FromQuery] string ids,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
        {
            await AuditAsync("blocked_user_request", "denied", currentUserId, "users", "participants", "user is blocked", cancellationToken);
            return Forbid();
        }

        var users = await userService.GetVisibleParticipantsAsync(
            currentUserId,
            ParseGuidList(ids),
            cancellationToken);

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

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<GetUserDto>> GetMe(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var user = await userService.GetByIdAsync(currentUserId, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPut("me/avatar")]
    [Authorize]
    [EnableRateLimiting("files")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<GetUserDto>> UploadAvatar(
        [FromForm] IFormFile? avatar,
        CancellationToken cancellationToken)
    {
        if (avatar is null || avatar.Length == 0)
        {
            return BadRequest(new { error = "Avatar file is required." });
        }

        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
            {
                await AuditAsync("blocked_user_avatar_upload", "denied", currentUserId, "users", currentUserId.ToString("D"), "user is blocked", cancellationToken);
                return Forbid();
            }

            await using var stream = avatar.OpenReadStream();
            var user = await userService.UpdateAvatarAsync(
                currentUserId,
                stream,
                avatar.FileName,
                avatar.ContentType ?? string.Empty,
                avatar.Length,
                cancellationToken);

            await AuditAsync("user_avatar_upload", "success", currentUserId, "users", currentUserId.ToString("D"), null, cancellationToken);
            return Ok(user);
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
        catch (DomainValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("me/avatar")]
    [Authorize]
    [EnableRateLimiting("files")]
    public async Task<ActionResult<GetUserDto>> DeleteAvatar(CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
            {
                await AuditAsync("blocked_user_avatar_delete", "denied", currentUserId, "users", currentUserId.ToString("D"), "user is blocked", cancellationToken);
                return Forbid();
            }

            var user = await userService.DeleteAvatarAsync(currentUserId, cancellationToken);
            await AuditAsync("user_avatar_delete", "success", currentUserId, "users", currentUserId.ToString("D"), null, cancellationToken);
            return Ok(user);
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
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

    private static IReadOnlyCollection<Guid> ParseGuidList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<Guid>();
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(100)
            .ToArray();
    }
}
