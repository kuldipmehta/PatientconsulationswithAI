using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PatientManagement.Core.Interfaces;
using PatientManagement.Infrastructure.Services.Rag;

namespace PatientManagement.Infrastructure.Services.Ai;

public class GeminiTextEmbeddingService : ITextEmbeddingService
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string EmbeddingModel = "models/text-embedding-004";
    private const int Dimensions = 768;

    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiTextEmbeddingService> _logger;
    private readonly string? _configuredApiKey;
    private readonly string _baseUrl;

    public GeminiTextEmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiTextEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _configuredApiKey = configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        _baseUrl = configuration["Gemini:BaseUrl"] ?? DefaultBaseUrl;
    }

    private bool HasLiveApiKey =>
        !string.IsNullOrWhiteSpace(_configuredApiKey) &&
        !_configuredApiKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase);

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[Dimensions];
        }

        if (!HasLiveApiKey)
        {
            return VectorOperations.GenerateDeterministicPseudoEmbedding(text, Dimensions);
        }

        try
        {
            var endpoint = $"{_baseUrl}/{EmbeddingModel}:embedContent?key={_configuredApiKey}";
            var payload = new JsonObject
            {
                ["model"] = EmbeddingModel,
                ["content"] = new JsonObject
                {
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = text })
                }
            };

            using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
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

            _logger.LogWarning("Gemini embedContent returned status {StatusCode}. Falling back to deterministic pseudo-embedding.", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calling Gemini text-embedding API. Falling back to deterministic pseudo-embedding.");
        }

        return VectorOperations.GenerateDeterministicPseudoEmbedding(text, Dimensions);
    }

    public async Task<List<float[]>> GenerateBatchEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return new List<float[]>();
        }

        if (!HasLiveApiKey)
        {
            return texts.Select(t => VectorOperations.GenerateDeterministicPseudoEmbedding(t, Dimensions)).ToList();
        }

        try
        {
            var endpoint = $"{_baseUrl}/{EmbeddingModel}:batchEmbedContents?key={_configuredApiKey}";
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
            using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
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

            _logger.LogWarning("Gemini batchEmbedContents failed with status {StatusCode}. Falling back to individual/pseudo embeddings.", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calling Gemini batchEmbedContents API. Falling back to individual/pseudo embeddings.");
        }

        var fallbackList = new List<float[]>();
        foreach (var t in texts)
        {
            fallbackList.Add(await GenerateEmbeddingAsync(t, cancellationToken));
        }
        return fallbackList;
    }
}
