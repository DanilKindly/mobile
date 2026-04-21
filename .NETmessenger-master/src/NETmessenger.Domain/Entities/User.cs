namespace NETmessenger.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<Chat> Chats { get; set; } = new List<Chat>();
}
