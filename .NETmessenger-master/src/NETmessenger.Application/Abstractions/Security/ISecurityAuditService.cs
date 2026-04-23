namespace NETmessenger.Application.Abstractions.Security;

public interface ISecurityAuditService
{
    Task RecordAsync(SecurityAuditEventInput input, CancellationToken cancellationToken);
}
