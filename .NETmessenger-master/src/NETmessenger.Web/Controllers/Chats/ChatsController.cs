using Microsoft.AspNetCore.Mvc;
using NETmessenger.Application.Abstractions.Chats;
using NETmessenger.Application.Exceptions;
using NETmessenger.Contracts.Chats;

namespace NETmessenger.Web.Controllers.Chats;

[ApiController]
[Route("api/chats")]
public class ChatsController(IChatService chatService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GetChatDto>> Create([FromBody] CreateChatDto dto, CancellationToken cancellationToken)
    {
        try
        {
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
        var chat = await chatService.GetByIdAsync(chatId, cancellationToken);
        return chat is null ? NotFound() : Ok(chat);
    }

    [HttpGet("by-user/{userId:guid}")]
    public async Task<ActionResult<IReadOnlyCollection<GetChatDto>>> GetByUserId(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var chats = await chatService.GetByUserIdAsync(userId, cancellationToken);
            return Ok(chats);
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
    }
}
