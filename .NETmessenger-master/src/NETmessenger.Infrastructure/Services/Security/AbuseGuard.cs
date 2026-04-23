using Microsoft.EntityFrameworkCore;
using NETmessenger.Application.Abstractions.Security;
using NETmessenger.Infrastructure.Persistence;

namespace NETmessenger.Infrastructure.Services.Security;

public sealed class AbuseGuard(AppDbContext dbContext) : IAbuseGuard
{
    public Task<bool> IsBlockedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Task.FromResult(true);
        }

        var now = DateTime.UtcNow;
        return dbContext.UserBlocks
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId &&
                     x.IsActive &&
                     (x.IsPermanent || x.BlockedUntilUtc == null || x.BlockedUntilUtc > now),
                cancellationToken);
    }
}
