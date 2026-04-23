namespace NETmessenger.Domain.Entities;

public class UserBlock
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsPermanent { get; set; }
    public DateTime? BlockedUntilUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
