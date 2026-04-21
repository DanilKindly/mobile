using Microsoft.AspNetCore.Http;

namespace NETmessenger.Web.Controllers.Messages;

public sealed class MediaMessageRequest
{
    public Guid SenderUserId { get; set; }
    public IFormFile File { get; set; } = default!;
}
