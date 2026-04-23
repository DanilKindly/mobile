using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETmessenger.Application.Abstractions.Chats;
using NETmessenger.Application.Abstractions.Security;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Chats;
using NETmessenger.Web.Security;

namespace NETmessenger.Web.Controllers.Chats;

[ApiController]
[Authorize]
[Route("api/chats")]
public class ChatsController(
    IChatService chatService,
    ISecurityAuditService auditService,
    IAbuseGuard abuseGuard) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GetChatDto>> Create([FromBody] CreateChatDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
            {
                await AuditAsync("blocked_user_create_chat", "denied", currentUserId, "chat", null, "user is blocked", cancellationToken);
                return Forbid();
            }

            if (!dto.ParticipantUserIds.Contains(currentUserId))
            {
                await AuditAsync("chat_create_forbidden", "denied", currentUserId, "chat", null, "current user missing from participants", cancellationToken);
                return Forbid();
            }

            var chat = await chatService.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { chatId = chat.ChatId }, chat);
        }
        catch (ResourceNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (DomainValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{chatId:guid}")]
    public async Task<ActionResult<GetChatDto>> GetById(Guid chatId, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var chat = await chatService.GetByIdAsync(chatId, cancellationToken);
        if (chat is null)
        {
            return NotFound();
        }

        if (!chat.ParticipantUserIds.Contains(currentUserId))
        {
            await AuditAsync("chat_read_forbidden", "denied", currentUserId, "chat", chatId.ToString("D"), "not a participant", cancellationToken);
            return Forbid();
        }

        return Ok(chat);
    }

    [HttpGet("by-user/{userId:guid}")]
    public async Task<ActionResult<IReadOnlyCollection<GetChatDto>>> GetByUserId(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (currentUserId != userId)
            {
                await AuditAsync("chat_list_forbidden", "denied", currentUserId, "users", userId.ToString("D"), "cannot list another user's chats", cancellationToken);
                return Forbid();
            }

            var chats = await chatService.GetByUserIdAsync(userId, cancellationToken);
            return Ok(chats);
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
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
}
