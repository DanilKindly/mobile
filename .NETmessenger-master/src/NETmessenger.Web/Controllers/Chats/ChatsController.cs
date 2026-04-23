using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETmessenger.Application.Abstractions.Chats;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Chats;
using NETmessenger.Web.Security;

namespace NETmessenger.Web.Controllers.Chats;

[ApiController]
[Authorize]
[Route("api/chats")]
public class ChatsController(IChatService chatService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GetChatDto>> Create([FromBody] CreateChatDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (!dto.ParticipantUserIds.Contains(currentUserId))
            {
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

        return chat.ParticipantUserIds.Contains(currentUserId) ? Ok(chat) : Forbid();
    }

    [HttpGet("by-user/{userId:guid}")]
    public async Task<ActionResult<IReadOnlyCollection<GetChatDto>>> GetByUserId(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var currentUserId = User.GetRequiredUserId();
            if (currentUserId != userId)
            {
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
}
