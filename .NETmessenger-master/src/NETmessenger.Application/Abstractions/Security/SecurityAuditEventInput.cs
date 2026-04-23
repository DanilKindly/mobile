namespace NETmessenger.Application.Abstractions.Security;

public record SecurityAuditEventInput(
    string EventType,
    string Outcome,
    string Severity = "info",
    Guid? UserId = null,
    string? IpAddress = null,
    string? UserAgent = null,
    string? ResourceType = null,
    string? ResourceId = null,
    string? Reason = null,
    string? MetadataJson = null);
