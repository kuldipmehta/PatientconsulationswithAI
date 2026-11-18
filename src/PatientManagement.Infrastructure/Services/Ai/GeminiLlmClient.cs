using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai;

public sealed class GeminiLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly string _baseUrl;

    public string ProviderName => "GoogleGemini";

    public GeminiLlmClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = (configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty).Trim();
        _defaultModel = string.IsNullOrWhiteSpace(configuration["Gemini:Model"]) ? "gemini-flash-latest" : configuration["Gemini:Model"]!.Trim();
        _baseUrl = (configuration["Gemini:ApiBaseUrl"] ?? "https://generativelanguage.googleapis.com/v1beta").TrimEnd('/');
    }

    public async Task<LlmGenerationResponse> GenerateAsync(LlmGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        }

        var model = string.IsNullOrWhiteSpace(request.Model) ? _defaultModel : request.Model.Trim();
        var endpoint = $"{_baseUrl}/models/{model}:generateContent";

        var generationConfig = new Dictionary<string, object?>
        {
            ["temperature"] = request.Temperature,
            ["topP"] = request.TopP,
            ["maxOutputTokens"] = request.MaxOutputTokens
        };
        if (request.ExpectJson)
        {
            generationConfig["responseMimeType"] = "application/json";
            if (request.ResponseSchema != null)
            {
                generationConfig["responseSchema"] = request.ResponseSchema;
            }
        }

        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = request.UserPrompt } } } },
            generationConfig
        };

        var sw = Stopwatch.StartNew();
        using var msg = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        msg.Headers.Add("x-goog-api-key", _apiKey);

        using var response = await _httpClient.SendAsync(msg, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        sw.Stop();

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var text = string.Empty;
        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var content) &&
            content.TryGetProperty("parts", out var parts))
        {
            text = string.Join("\n", parts.EnumerateArray()
                .Where(p => p.TryGetProperty("text", out _))
                .Select(p => p.GetProperty("text").GetString() ?? string.Empty));
        }

        int? promptTokens = null;
        int? completionTokens = null;
        int? totalTokens = null;
        if (root.TryGetProperty("usageMetadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            var p = meta.TryGetProperty("promptTokenCount", out var pEl) && pEl.TryGetInt32(out var pv) ? pv : 0;
            var c = meta.TryGetProperty("candidatesTokenCount", out var cEl) && cEl.TryGetInt32(out var cv) ? cv : 0;
            var th = meta.TryGetProperty("thoughtsTokenCount", out var thEl) && thEl.TryGetInt32(out var thv) ? thv : 0;
            var t = meta.TryGetProperty("totalTokenCount", out var tEl) && tEl.TryGetInt32(out var tv) ? tv : p + c + th;
            promptTokens = p;
            completionTokens = c + th;
            totalTokens = t;
        }

        return new LlmGenerationResponse
        {
            Provider = ProviderName,
            ModelUsed = model,
            Text = text,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            LatencyMs = sw.ElapsedMilliseconds
        };
    }
}
