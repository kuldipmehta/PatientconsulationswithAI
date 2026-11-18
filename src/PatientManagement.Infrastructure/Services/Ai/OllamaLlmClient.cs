using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PatientManagement.Core.Interfaces;

namespace PatientManagement.Infrastructure.Services.Ai;

/// <summary>
/// Non-Gemini local LLM client backed by Ollama (/api/chat) for offline or self-hosted models.
/// </summary>
public sealed class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _defaultModel;

    public string ProviderName => "Ollama";

    public OllamaLlmClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _baseUrl = (configuration["Ollama:BaseUrl"] ?? "http://localhost:11434").TrimEnd('/');
        _defaultModel = string.IsNullOrWhiteSpace(configuration["Ollama:Model"]) ? "llama3.2" : configuration["Ollama:Model"]!.Trim();
    }

    public async Task<LlmGenerationResponse> GenerateAsync(LlmGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? _defaultModel : request.Model.Trim();
        var endpoint = $"{_baseUrl}/api/chat";

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["stream"] = false,
            ["messages"] = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            ["options"] = new
            {
                temperature = request.Temperature,
                top_p = request.TopP,
                num_predict = request.MaxOutputTokens
            }
        };

        if (request.ExpectJson)
        {
            payload["format"] = request.ResponseSchema ?? (object)"json";
        }

        var sw = Stopwatch.StartNew();
        using var response = await _httpClient.PostAsJsonAsync(endpoint, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        sw.Stop();

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var text = root.TryGetProperty("message", out var msgEl) && msgEl.TryGetProperty("content", out var contentEl)
            ? contentEl.GetString() ?? string.Empty
            : string.Empty;

        int? promptTokens = root.TryGetProperty("prompt_eval_count", out var pEl) && pEl.TryGetInt32(out var pv) ? pv : null;
        int? completionTokens = root.TryGetProperty("eval_count", out var cEl) && cEl.TryGetInt32(out var cv) ? cv : null;
        int? totalTokens = (promptTokens.HasValue || completionTokens.HasValue)
            ? (promptTokens ?? 0) + (completionTokens ?? 0)
            : null;

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
