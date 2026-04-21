namespace NETmessenger.Domain.Entities;

public class Chat
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsGroup { get; set; }
    public string? Name { get; set; }

    public ICollection<User> Participants { get; set; } = new List<User>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
