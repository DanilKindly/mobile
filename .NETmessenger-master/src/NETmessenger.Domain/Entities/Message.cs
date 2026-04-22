namespace NETmessenger.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    public MessageType Type { get; set; } = MessageType.Text;
    public string? Text { get; set; }
    public string? AudioUrl { get; set; }
    public string? AudioContentType { get; set; }
    public int? AudioDurationSeconds { get; set; }
    public long? AudioSizeBytes { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaContentType { get; set; }
    public string? MediaFileName { get; set; }
    public long? MediaSizeBytes { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    
    public Guid SenderId { get; set; }
    public Guid ChatId { get; set; }     

    public User? Sender { get; set; }
    public Chat? Chat { get; set; }       
}
