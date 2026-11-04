namespace PatientManagement.Core.Interfaces;

public record BlobUploadResult(string BlobName, string BlobUrl, string ContentType, long Size);

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

    string GetBlobUrl(string blobName, string? containerName = null);
}
