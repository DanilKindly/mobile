using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NETmessenger.Application.Abstractions.Messages;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Messages;
using NETmessenger.Web.Hubs;

namespace NETmessenger.Web.Controllers.Messages;

[ApiController]
[Route("api/chats/{chatId:guid}/messages")]
public class MessagesController(IMessageService messageService, IHubContext<ChatHub> hubContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<GetMessageDto>>> GetByChatId(Guid chatId, CancellationToken cancellationToken)
    {
        try
        {
            var messages = await messageService.GetByChatIdAsync(chatId, cancellationToken);
            return Ok(messages);
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<GetMessageDto>> Send(Guid chatId, [FromBody] SendMessageDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var message = await messageService.SendAsync(chatId, dto, cancellationToken);
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
            await using var stream = request.Audio.OpenReadStream();
            var message = await messageService.SendVoiceAsync(
                chatId,
                request.SenderUserId,
                stream,
                request.Audio.FileName,
                request.Audio.ContentType,
                request.Audio.Length,
                request.DurationSeconds,
                cancellationToken);

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
            await using var stream = request.File.OpenReadStream();
            var message = await messageService.SendMediaAsync(
                chatId,
                request.SenderUserId,
                stream,
                request.File.FileName,
                request.File.ContentType ?? "application/octet-stream",
                request.File.Length,
                cancellationToken);

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
}
