using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
                _logger.LogInformation("Azure Blob Storage service initialized with configured connection string. Default container: '{Container}'", _defaultContainer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Azure BlobServiceClient with connection string. Will fall back to local simulated storage.");
                _blobServiceClient = null;
            }
        }
        else
        {
            _logger.LogInformation("No Azure Blob Storage connection string configured. Using local simulated storage at '{Path}'.", _fallbackLocalStoragePath);
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
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

                var blobClient = containerClient.GetBlobClient(uniqueBlobName);

                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
                };

                stream.Position = 0;
                var uploadInfo = await blobClient.UploadAsync(
                    stream,
                    new BlobUploadOptions { HttpHeaders = blobHttpHeaders },
                    cancellationToken);

                var blobUrl = blobClient.Uri.AbsoluteUri;
                _logger.LogInformation("Successfully uploaded blob '{BlobName}' to Azure container '{Container}'. URI: {Uri}",
                    uniqueBlobName, targetContainer, blobUrl);

                return new BlobUploadResult(uniqueBlobName, blobUrl, contentType, stream.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload to Azure Blob Storage container '{Container}'. Falling back to local simulated storage.", targetContainer);
            }
        }

        // Local simulated fallback
        var localContainerPath = Path.Combine(_fallbackLocalStoragePath, targetContainer);
        Directory.CreateDirectory(localContainerPath);
        var localFilePath = Path.Combine(localContainerPath, uniqueBlobName);

        stream.Position = 0;
        using (var fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        var simulatedUrl = $"https://patientstorageaccount.blob.core.windows.net/{targetContainer}/{uniqueBlobName}";
        _logger.LogInformation("Stored file locally (simulated Azure Blob Storage) at '{LocalPath}'. Simulated URI: {Uri}",
            localFilePath, simulatedUrl);

        return new BlobUploadResult(uniqueBlobName, simulatedUrl, contentType, stream.Length);
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
                _logger.LogInformation("Deleted blob '{BlobName}' from Azure Blob Storage. Deleted: {Deleted}", blobName, response.Value);
                return response.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete blob '{BlobName}' from Azure Blob Storage.", blobName);
            }
        }

        var localPath = Path.Combine(_fallbackLocalStoragePath, targetContainer, blobName);
        if (File.Exists(localPath))
        {
            File.Delete(localPath);
            _logger.LogInformation("Deleted simulated blob file at '{LocalPath}'.", localPath);
            return true;
        }

        return false;
    }

    public string GetBlobUrl(string blobName, string? containerName = null)
    {
        var targetContainer = containerName ?? _defaultContainer;

        if (_blobServiceClient != null)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(targetContainer);
                var blobClient = containerClient.GetBlobClient(blobName);
                return blobClient.Uri.AbsoluteUri;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting Azure Blob URL for '{BlobName}'.", blobName);
            }
        }

        return $"https://patientstorageaccount.blob.core.windows.net/{targetContainer}/{blobName}";
    }
}
