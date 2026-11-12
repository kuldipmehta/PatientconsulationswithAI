using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services;

public class AzureBlobStorageService : IBlobStorageService
{
    private readonly ILogger<AzureBlobStorageService> _logger;
    private readonly string? _connectionString;
    private readonly string _defaultContainer;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly string _fallbackLocalStoragePath;

    public AzureBlobStorageService(IConfiguration configuration, ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;
        _connectionString = configuration["AzureBlobStorage:ConnectionString"];
        _defaultContainer = configuration["AzureBlobStorage:ContainerName"] ?? "patient-pictures";
        _fallbackLocalStoragePath = Path.Combine(AppContext.BaseDirectory, "LocalStorage", "blobs");

        if (!string.IsNullOrWhiteSpace(_connectionString) &&
            !_connectionString.Contains("YOUR_AZURE_STORAGE_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _blobServiceClient = new BlobServiceClient(_connectionString);
                _logger.LogInformation("Private Azure Blob Storage service initialized. Container: '{Container}'", _defaultContainer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Azure BlobServiceClient. Using private local storage fallback.");
                _blobServiceClient = null;
            }
        }
        else
        {
            _logger.LogInformation("No Azure Blob Storage connection string configured. Using private local storage at '{Path}'.", _fallbackLocalStoragePath);
        }
    }

    public async Task<BlobUploadResult> UploadAsync(
        Stream stream,
        string originalFileName,
        string contentType,
        string? containerName = null,
        CancellationToken cancellationToken = default)
    {
        var targetContainer = containerName ?? _defaultContainer;
        var extension = Path.GetExtension(originalFileName);
        var uniqueBlobName = $"{Guid.NewGuid()}{extension}".ToLowerInvariant();

        if (_blobServiceClient != null)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(targetContainer);
                // Enforce PRIVATE container access (no anonymous blob access)
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

                var blobClient = containerClient.GetBlobClient(uniqueBlobName);
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
                };

                stream.Position = 0;
                await blobClient.UploadAsync(
                    stream,
                    new BlobUploadOptions { HttpHeaders = blobHttpHeaders },
                    cancellationToken);

                var sasUrl = GetReadSasUrl(uniqueBlobName, TimeSpan.FromMinutes(15), targetContainer);
                _logger.LogInformation("Uploaded blob '{BlobName}' to PRIVATE Azure container '{Container}'.", uniqueBlobName, targetContainer);

                return new BlobUploadResult(uniqueBlobName, sasUrl, contentType, stream.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload to Azure Blob Storage container '{Container}'. Falling back to private local storage.", targetContainer);
            }
        }

        // Private local simulated storage (served only via authenticated API endpoint)
        var localContainerPath = Path.Combine(_fallbackLocalStoragePath, targetContainer);
        Directory.CreateDirectory(localContainerPath);
        var localFilePath = Path.Combine(localContainerPath, uniqueBlobName);

        stream.Position = 0;
        using (var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        return new BlobUploadResult(uniqueBlobName, uniqueBlobName, contentType, stream.Length);
    }

    public async Task<bool> DeleteAsync(string blobName, string? containerName = null, CancellationToken cancellationToken = default)
    {
        var targetContainer = containerName ?? _defaultContainer;

        if (_blobServiceClient != null)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(targetContainer);
                var blobClient = containerClient.GetBlobClient(blobName);
                var response = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
                return response.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete blob '{BlobName}' from Azure Blob Storage.", blobName);
            }
        }

        var localPath = Path.Combine(_fallbackLocalStoragePath, targetContainer, Path.GetFileName(blobName));
        if (File.Exists(localPath))
        {
            File.Delete(localPath);
            return true;
        }

        return false;
    }

    public string GetReadSasUrl(string blobName, TimeSpan? ttl = null, string? containerName = null)
    {
        var targetContainer = containerName ?? _defaultContainer;

        if (_blobServiceClient != null)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(targetContainer);
                var blobClient = containerClient.GetBlobClient(blobName);

                if (blobClient.CanGenerateSasUri)
                {
                    var sasBuilder = new BlobSasBuilder
                    {
                        BlobContainerName = targetContainer,
                        BlobName = blobName,
                        Resource = "b",
                        StartsOn = DateTimeOffset.UtcNow.AddMinutes(-1),
                        ExpiresOn = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(15))
                    };
                    sasBuilder.SetPermissions(BlobSasPermissions.Read);

                    return blobClient.GenerateSasUri(sasBuilder).AbsoluteUri;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error generating short-lived SAS URI for '{BlobName}'.", blobName);
            }
        }

        // Local fallback signals callers to use the authenticated picture proxy endpoint
        return string.Empty;
    }

    public async Task<BlobReadResult?> OpenReadAsync(string blobName, string? containerName = null, CancellationToken cancellationToken = default)
    {
        var targetContainer = containerName ?? _defaultContainer;

        if (_blobServiceClient != null)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(targetContainer);
                var blobClient = containerClient.GetBlobClient(blobName);
                if (await blobClient.ExistsAsync(cancellationToken))
                {
                    var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
                    var contentType = download.Value.Details.ContentType ?? "application/octet-stream";
                    return new BlobReadResult(download.Value.Content, contentType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error streaming blob '{BlobName}' from Azure Storage.", blobName);
            }
        }

        var safeName = Path.GetFileName(blobName);
        var localPath = Path.Combine(_fallbackLocalStoragePath, targetContainer, safeName);
        if (!File.Exists(localPath))
        {
            return null;
        }

        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        Stream stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new BlobReadResult(stream, mime);
    }
}
