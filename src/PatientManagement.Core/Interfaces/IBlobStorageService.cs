namespace PatientManagement.Core.Interfaces;

public record BlobUploadResult(string BlobName, string BlobUrl, string ContentType, long Size);
public record BlobReadResult(Stream Content, string ContentType);

public interface IBlobStorageService
{
    Task<BlobUploadResult> UploadAsync(
        Stream stream,
        string originalFileName,
        string contentType,
        string? containerName = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string blobName,
        string? containerName = null,
        CancellationToken cancellationToken = default);

    string GetReadSasUrl(string blobName, TimeSpan? ttl = null, string? containerName = null);

    Task<BlobReadResult?> OpenReadAsync(string blobName, string? containerName = null, CancellationToken cancellationToken = default);
}
