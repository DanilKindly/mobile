using System.IO;

namespace NETmessenger.Application.Abstractions.Files;

public interface IVoiceStorage
{
    Task<StoredVoiceFile> SaveAsync(
        Stream audioStream,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken);
}

public record StoredVoiceFile(string Url, long SizeBytes, string ContentType);

public interface IMediaStorage
{
    Task<StoredMediaFile> SaveAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken);
}

public record StoredMediaFile(string Url, long SizeBytes, string ContentType, string FileName);
