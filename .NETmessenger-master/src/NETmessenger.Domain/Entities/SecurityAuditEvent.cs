namespace NETmessenger.Domain.Entities;

public class SecurityAuditEvent
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public Guid? UserId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
}
