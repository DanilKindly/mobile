using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using NETmessenger.Application.Abstractions.Chats;
using NETmessenger.Application.Abstractions.Messages;
using NETmessenger.Application.Abstractions.Security;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Messages;
using NETmessenger.Web.Hubs;
using NETmessenger.Web.Security;

namespace NETmessenger.Web.Controllers.Messages;

[ApiController]
[Authorize]
[Route("api/chats/{chatId:guid}/messages")]
public class MessagesController(
    IMessageService messageService,
    IChatService chatService,
    ISecurityAuditService auditService,
    IAbuseGuard abuseGuard,
    IHubContext<ChatHub> hubContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<GetMessagesPageDto>> GetByChatId(
        Guid chatId,
        [FromQuery] DateTime? beforeSentAt = null,
        [FromQuery] Guid? beforeMessageId = null,
        [FromQuery] int limit = 40,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await CurrentUserCanAccessChat(chatId, cancellationToken))
            {
                await AuditAsync("message_read_forbidden", "denied", User.GetRequiredUserId(), "chat", chatId.ToString("D"), "not a participant", cancellationToken);
                return Forbid();
            }

            var messagesPage = await messageService.GetPageByChatIdAsync(
                chatId,
                beforeSentAt,
                beforeMessageId,
                limit,
                cancellationToken);
            return Ok(messagesPage);
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [EnableRateLimiting("send-message")]
    public async Task<ActionResult<GetMessageDto>> Send(Guid chatId, [FromBody] SendMessageDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
            {
                await AuditAsync("blocked_user_send", "denied", currentUserId, "chat", chatId.ToString("D"), "user is blocked", cancellationToken);
                return Forbid();
            }

            if (!await CurrentUserCanAccessChat(chatId, cancellationToken))
            {
                await AuditAsync("message_send_forbidden", "denied", currentUserId, "chat", chatId.ToString("D"), "not a participant", cancellationToken);
                return Forbid();
            }

            var trustedDto = dto with { SenderUserId = currentUserId };
            var message = await messageService.SendAsync(chatId, trustedDto, cancellationToken);
            await AuditAsync("message_send", "success", currentUserId, "message", message.MessageId.ToString("D"), null, cancellationToken);
            var cursor = Math.Max(message.Version, DateTime.UtcNow.Ticks);
            var realtimeEvent = new RealtimeEventDto(
                "MessageCreated",
                cursor,
                message.Version,
                message,
                chatId,
                null,
                null,
                null);
            await hubContext.Clients.Group(ChatHub.GetChatGroup(chatId)).SendAsync("RealtimeEvent", realtimeEvent, cancellationToken);
            await hubContext.Clients.Group(ChatHub.GetChatGroup(chatId)).SendAsync("MessageReceived", message, cancellationToken);
            return Ok(message);
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

    [HttpPost("voice")]
    [EnableRateLimiting("send-message")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<GetMessageDto>> SendVoice(
        Guid chatId,
        [FromForm] VoiceMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Audio is null || request.Audio.Length == 0)
        {
            return BadRequest(new { error = "Audio file is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Audio.ContentType) ||
            !request.Audio.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Invalid audio content type." });
        }

        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
            {
                await AuditAsync("blocked_user_send_voice", "denied", currentUserId, "chat", chatId.ToString("D"), "user is blocked", cancellationToken);
                return Forbid();
            }

            if (!await CurrentUserCanAccessChat(chatId, cancellationToken))
            {
                await AuditAsync("voice_send_forbidden", "denied", currentUserId, "chat", chatId.ToString("D"), "not a participant", cancellationToken);
                return Forbid();
            }

            await using var stream = request.Audio.OpenReadStream();
            var message = await messageService.SendVoiceAsync(
                chatId,
                currentUserId,
                stream,
                request.Audio.FileName,
                request.Audio.ContentType,
                request.Audio.Length,
                request.DurationSeconds,
                cancellationToken);
            await AuditAsync("message_send_voice", "success", currentUserId, "message", message.MessageId.ToString("D"), null, cancellationToken);

            var cursor = Math.Max(message.Version, DateTime.UtcNow.Ticks);
            var realtimeEvent = new RealtimeEventDto(
                "MessageCreated",
                cursor,
                message.Version,
                message,
                chatId,
                null,
                null,
                null);
            await hubContext.Clients.Group(ChatHub.GetChatGroup(chatId)).SendAsync("RealtimeEvent", realtimeEvent, cancellationToken);
            await hubContext.Clients.Group(ChatHub.GetChatGroup(chatId)).SendAsync("MessageReceived", message, cancellationToken);
            return Ok(message);
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

    [HttpPost("media")]
    [EnableRateLimiting("send-message")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<ActionResult<GetMessageDto>> SendMedia(
        Guid chatId,
        [FromForm] MediaMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new { error = "File is required." });
        }

        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
            {
                await AuditAsync("blocked_user_send_media", "denied", currentUserId, "chat", chatId.ToString("D"), "user is blocked", cancellationToken);
                return Forbid();
            }

            if (!await CurrentUserCanAccessChat(chatId, cancellationToken))
            {
                await AuditAsync("media_send_forbidden", "denied", currentUserId, "chat", chatId.ToString("D"), "not a participant", cancellationToken);
                return Forbid();
            }

            await using var stream = request.File.OpenReadStream();
            var message = await messageService.SendMediaAsync(
                chatId,
                currentUserId,
                stream,
                request.File.FileName,
                request.File.ContentType ?? "application/octet-stream",
                request.File.Length,
                cancellationToken);
            await AuditAsync("message_send_media", "success", currentUserId, "message", message.MessageId.ToString("D"), null, cancellationToken);

            var cursor = Math.Max(message.Version, DateTime.UtcNow.Ticks);
            var realtimeEvent = new RealtimeEventDto(
                "MessageCreated",
                cursor,
                message.Version,
                message,
                chatId,
                null,
                null,
                null);
            await hubContext.Clients.Group(ChatHub.GetChatGroup(chatId)).SendAsync("RealtimeEvent", realtimeEvent, cancellationToken);
            await hubContext.Clients.Group(ChatHub.GetChatGroup(chatId)).SendAsync("MessageReceived", message, cancellationToken);
            return Ok(message);
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

    [HttpGet("/api/chats/changes/by-user/{userId:guid}")]
    public async Task<ActionResult<MessageChangesDto>> GetChangesByUser(
        Guid userId,
        [FromQuery] long cursor = 0,
        [FromQuery] int limit = 250,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (currentUserId != userId)
            {
                await AuditAsync("changes_read_forbidden", "denied", currentUserId, "users", userId.ToString("D"), "cannot read another user's changes", cancellationToken);
                return Forbid();
            }

            var changes = await messageService.GetChangesByUserAsync(userId, cursor, limit, cancellationToken);
            return Ok(changes);
        }
        catch (ResourceNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    private async Task<bool> CurrentUserCanAccessChat(Guid chatId, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var chat = await chatService.GetByIdAsync(chatId, cancellationToken);
        return chat is not null && chat.ParticipantUserIds.Contains(currentUserId);
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
