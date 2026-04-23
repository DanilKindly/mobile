using NETmessenger.Application.Abstractions.Security;
using NETmessenger.Domain.Entities;
using NETmessenger.Infrastructure.Persistence;

namespace NETmessenger.Infrastructure.Services.Security;

public sealed class SecurityAuditService(AppDbContext dbContext) : ISecurityAuditService
{
    public async Task RecordAsync(SecurityAuditEventInput input, CancellationToken cancellationToken)
    {
        var auditEvent = new SecurityAuditEvent
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            EventType = Truncate(input.EventType, 80) ?? "unknown",
            Outcome = Truncate(input.Outcome, 40) ?? "unknown",
            Severity = Truncate(input.Severity, 20) ?? "info",
            UserId = input.UserId,
            IpAddress = Truncate(input.IpAddress, 64),
            UserAgent = Truncate(input.UserAgent, 512),
            ResourceType = Truncate(input.ResourceType, 80),
            ResourceId = Truncate(input.ResourceId, 128),
            Reason = Truncate(input.Reason, 256),
            MetadataJson = Truncate(input.MetadataJson, 2000)
        };

        dbContext.SecurityAuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
