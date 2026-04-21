using System.IO;
using Microsoft.Extensions.Hosting;
using NETmessenger.Application.Abstractions.Files;

namespace NETmessenger.Infrastructure.Services.Files;

public sealed class LocalVoiceStorage(IHostEnvironment environment) : IVoiceStorage
{
    private const string VoiceFolderName = "voice";

    public async Task<StoredVoiceFile> SaveAsync(
        Stream audioStream,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var safeExtension = Path.GetExtension(originalFileName);
        var fileName = $"{Guid.NewGuid():N}{safeExtension}";

        var targetDirs = GetTargetDirectories(environment.ContentRootPath, VoiceFolderName);
        foreach (var dir in targetDirs)
        {
            Directory.CreateDirectory(dir);
        }

        var primaryPath = Path.Combine(targetDirs[0], fileName);
        await using (var output = new FileStream(primaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await audioStream.CopyToAsync(output, cancellationToken);
        }

        // Keep a mirror in alternate web roots for different run working directories.
        foreach (var altDir in targetDirs.Skip(1))
        {
            var mirrorPath = Path.Combine(altDir, fileName);
            if (!File.Exists(mirrorPath))
            {
                File.Copy(primaryPath, mirrorPath);
            }
        }

        var fileInfo = new FileInfo(primaryPath);
        var url = $"/voice/{fileName}";
        return new StoredVoiceFile(url, fileInfo.Length, contentType);
    }

    private static List<string> GetTargetDirectories(string contentRootPath, string folderName)
    {
        var result = new List<string>
        {
            Path.Combine(contentRootPath, "wwwroot", folderName)
        };

        var projectWebRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", folderName));
        if (!result.Contains(projectWebRoot, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(projectWebRoot);
        }

        return result;
    }
}
