using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NETmessenger.Application.Abstractions.Files;

namespace NETmessenger.Infrastructure.Services.Files;

public sealed class LocalAvatarStorage(IHostEnvironment environment, IConfiguration configuration) : IAvatarStorage
{
    private const string AvatarFolderName = "avatars";

    public async Task<StoredAvatarFile> SaveAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        var extension = NormalizeExtension(originalFileName, contentType);
        var fileName = $"{Guid.NewGuid():N}{extension}";

        var configuredRootPath = configuration["FileStorage:RootPath"];
        var targetDirs = GetTargetDirectories(environment.ContentRootPath, AvatarFolderName, configuredRootPath);
        foreach (var dir in targetDirs)
        {
            Directory.CreateDirectory(dir);
        }

        var primaryPath = Path.Combine(targetDirs[0], fileName);
        await using (var output = new FileStream(primaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await fileStream.CopyToAsync(output, cancellationToken);
        }

        foreach (var altDir in targetDirs.Skip(1))
        {
            var mirrorPath = Path.Combine(altDir, fileName);
            if (!File.Exists(mirrorPath))
            {
                File.Copy(primaryPath, mirrorPath);
            }
        }

        var fileInfo = new FileInfo(primaryPath);
        return new StoredAvatarFile($"/avatars/{fileName}", fileInfo.Length, contentType);
    }

    public Task DeleteAsync(string? storedUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storedUrl))
        {
            return Task.CompletedTask;
        }

        var fileName = Path.GetFileName(storedUrl);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Task.CompletedTask;
        }

        foreach (var dir in GetTargetDirectories(
                     environment.ContentRootPath,
                     AvatarFolderName,
                     configuration["FileStorage:RootPath"]))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    private static string NormalizeExtension(string originalFileName, string contentType)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension;
        }

        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
    }

    private static List<string> GetTargetDirectories(string contentRootPath, string folderName, string? configuredRootPath)
    {
        var result = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredRootPath))
        {
            var normalizedRoot = Path.IsPathRooted(configuredRootPath)
                ? configuredRootPath
                : Path.GetFullPath(Path.Combine(contentRootPath, configuredRootPath));

            result.Add(Path.Combine(normalizedRoot, folderName));
            return result;
        }

        result.Add(Path.Combine(contentRootPath, "wwwroot", folderName));

        var projectWebRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", folderName));
        if (!result.Contains(projectWebRoot, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(projectWebRoot);
        }

        return result;
    }
}
