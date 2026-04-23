namespace NETmessenger.Application.Abstractions.Security;

public interface IAbuseGuard
{
    Task<bool> IsBlockedAsync(Guid userId, CancellationToken cancellationToken);
}
