using Microsoft.AspNetCore.Http;

namespace NETmessenger.Web.Controllers.Messages;

public sealed class VoiceMessageRequest
{
    public Guid SenderUserId { get; set; }
    public IFormFile Audio { get; set; } = default!;
    public int? DurationSeconds { get; set; }
}
