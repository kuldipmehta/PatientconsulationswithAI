using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.Exceptions;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Services.Rag;

namespace PatientManagement.Infrastructure.Services.Ai;

public class GeminiTextEmbeddingService : ITextEmbeddingService
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string EmbeddingModel = "models/gemini-embedding-001";
    private const int Dimensions = 3072;

    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiTextEmbeddingService> _logger;
    private readonly string? _configuredApiKey;
    private readonly string _baseUrl;

    // Pseudo embeddings are hash-based and NOT semantic. They are only allowed when explicitly enabled
    // (Development / tests). In every other environment a failed embedding call must fail the request,
    // otherwise documents get indexed with meaningless vectors and nobody notices.
    private readonly bool _allowPseudoEmbeddings;

    public GeminiTextEmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiTextEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuredApiKey = configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _baseUrl = (configuration["Gemini:ApiBaseUrl"] ?? configuration["Gemini:BaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');
        _allowPseudoEmbeddings = configuration.GetValue<bool>("Rag:AllowPseudoEmbeddings");
    }

    private bool HasLiveApiKey =>
        !string.IsNullOrWhiteSpace(_configuredApiKey) &&
        !_configuredApiKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase);

    private float[] PseudoOrThrow(string text, string reason, Exception? inner = null)
    {
        if (!_allowPseudoEmbeddings)
        {
            throw new EmbeddingUnavailableException(reason, inner);
        }

        _logger.LogWarning("{Reason} Using deterministic pseudo-embedding because Rag:AllowPseudoEmbeddings is enabled.", reason);
        return VectorOperations.GenerateDeterministicPseudoEmbedding(text, Dimensions);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[Dimensions];
        }

        if (!HasLiveApiKey)
        {
            return PseudoOrThrow(text, "Gemini API key is not configured.");
        }

        try
        {
            var endpoint = $"{_baseUrl}/{EmbeddingModel}:embedContent";
            var payload = new JsonObject
            {
                ["model"] = EmbeddingModel,
                ["content"] = new JsonObject
                {
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = text })
                }
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            requestMessage.Headers.Add("x-goog-api-key", _configuredApiKey);

            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("embedding", out var emb) &&
                    emb.TryGetProperty("values", out var vals))
                {
                    var result = new List<float>();
                    foreach (var val in vals.EnumerateArray())
                    {
                        result.Add(val.GetSingle());
                    }
                    if (result.Count > 0)
                    {
                        return result.ToArray();
                    }
                }
            }

            return PseudoOrThrow(text, $"Gemini embedContent returned status {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EmbeddingUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PseudoOrThrow(text, "Error calling the Gemini embedding API.", ex);
        }
    }

    public async Task<List<float[]>> GenerateBatchEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return new List<float[]>();
        }

        if (!HasLiveApiKey)
        {
            return texts.Select(t => PseudoOrThrow(t, "Gemini API key is not configured.")).ToList();
        }

        try
        {
            var endpoint = $"{_baseUrl}/{EmbeddingModel}:batchEmbedContents";
            var requestsArray = new JsonArray();
            foreach (var t in texts)
            {
                requestsArray.Add(new JsonObject
                {
                    ["model"] = EmbeddingModel,
                    ["content"] = new JsonObject
                    {
                        ["parts"] = new JsonArray(new JsonObject { ["text"] = t })
                    }
                });
            }

            var payload = new JsonObject { ["requests"] = requestsArray };
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            requestMessage.Headers.Add("x-goog-api-key", _configuredApiKey);

            using var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("embeddings", out var embeddingsEl))
                {
                    var results = new List<float[]>();
                    foreach (var item in embeddingsEl.EnumerateArray())
                    {
                        if (item.TryGetProperty("values", out var vals))
                        {
                            var vector = new List<float>();
                            foreach (var v in vals.EnumerateArray())
                            {
                                vector.Add(v.GetSingle());
                            }
                            results.Add(vector.ToArray());
                        }
                    }

                    if (results.Count == texts.Count)
                    {
                        return results;
                    }
                }
            }

            if (!_allowPseudoEmbeddings)
            {
                throw new EmbeddingUnavailableException(
                    $"Gemini batchEmbedContents failed with status {(int)response.StatusCode}.");
            }

            _logger.LogWarning(
                "Gemini batchEmbedContents failed with status {StatusCode}. Using pseudo embeddings (Rag:AllowPseudoEmbeddings).",
                response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EmbeddingUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!_allowPseudoEmbeddings)
            {
                throw new EmbeddingUnavailableException("Error calling the Gemini batch embedding API.", ex);
            }

            _logger.LogWarning(ex, "Error calling Gemini batchEmbedContents API. Using pseudo embeddings (Rag:AllowPseudoEmbeddings).");
        }

        return texts.Select(t => VectorOperations.GenerateDeterministicPseudoEmbedding(t, Dimensions)).ToList();
    }
}
