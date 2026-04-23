using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NETmessenger.Application.Abstractions.Security;
using NETmessenger.Infrastructure.Persistence;
using NETmessenger.Web.Security;

namespace NETmessenger.Web.Controllers.Files;

[ApiController]
[Authorize]
public class FilesController(
    AppDbContext dbContext,
    ISecurityAuditService auditService,
    IAbuseGuard abuseGuard,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("/media/{fileName}")]
    [EnableRateLimiting("files")]
    public Task<IActionResult> GetMedia(string fileName, CancellationToken cancellationToken)
    {
        return ServeStoredFile("media", fileName, cancellationToken);
    }

    [HttpGet("/voice/{fileName}")]
    [EnableRateLimiting("files")]
    public Task<IActionResult> GetVoice(string fileName, CancellationToken cancellationToken)
    {
        return ServeStoredFile("voice", fileName, cancellationToken);
    }

    private async Task<IActionResult> ServeStoredFile(
        string bucket,
        string fileName,
        CancellationToken cancellationToken)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName != fileName)
        {
            return NotFound();
        }

        var currentUserId = User.GetRequiredUserId();
        if (await abuseGuard.IsBlockedAsync(currentUserId, cancellationToken))
        {
            await AuditAsync("blocked_user_file_access", "denied", currentUserId, bucket, safeFileName, "user is blocked", cancellationToken);
            return Forbid();
        }

        var storedUrl = $"/{bucket}/{safeFileName}";

        var access = bucket == "media"
            ? await dbContext.Messages
                .AsNoTracking()
                .Where(m => m.MediaUrl == storedUrl)
                .Select(m => new
                {
                    ContentType = m.MediaContentType,
                    Allowed = m.Chat!.Participants.Any(p => p.Id == currentUserId)
                })
                .FirstOrDefaultAsync(cancellationToken)
            : await dbContext.Messages
                .AsNoTracking()
                .Where(m => m.AudioUrl == storedUrl)
                .Select(m => new
                {
                    ContentType = m.AudioContentType,
                    Allowed = m.Chat!.Participants.Any(p => p.Id == currentUserId)
                })
                .FirstOrDefaultAsync(cancellationToken);

        if (access is null)
        {
            await AuditAsync("file_access_missing", "denied", currentUserId, bucket, safeFileName, "file is not attached to a message", cancellationToken);
            return NotFound();
        }

        if (!access.Allowed)
        {
            await AuditAsync("file_access_forbidden", "denied", currentUserId, bucket, safeFileName, "not a participant", cancellationToken);
            return Forbid();
        }

        var physicalPath = ResolveStoredFilePath(bucket, safeFileName);
        if (physicalPath is null)
        {
            await AuditAsync("file_access_missing", "denied", currentUserId, bucket, safeFileName, "file missing on disk", cancellationToken);
            return NotFound();
        }

        return PhysicalFile(
            physicalPath,
            string.IsNullOrWhiteSpace(access.ContentType) ? "application/octet-stream" : access.ContentType,
            enableRangeProcessing: true);
    }

    private string? ResolveStoredFilePath(string bucket, string safeFileName)
    {
        foreach (var dir in GetCandidateDirectories(bucket))
        {
            var candidate = Path.Combine(dir, safeFileName);
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private IEnumerable<string> GetCandidateDirectories(string bucket)
    {
        var configuredRootPath = configuration["FileStorage:RootPath"];
        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            var normalizedRoot = Path.IsPathRooted(configuredRootPath)
                ? configuredRootPath
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredRootPath));

            yield return Path.Combine(normalizedRoot, bucket);
        }

        yield return Path.Combine(environment.ContentRootPath, "wwwroot", bucket);
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", bucket));
    }

    private Task AuditAsync(
        string eventType,
        string outcome,
        Guid? userId,
        string? resourceType,
        string? resourceId,
        string? reason,
        CancellationToken cancellationToken)
    {
        return auditService.RecordAsync(new SecurityAuditEventInput(
            eventType,
            outcome,
            outcome == "success" ? "info" : "warning",
            userId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            resourceType,
            resourceId,
            reason),
            cancellationToken);
    }
}
