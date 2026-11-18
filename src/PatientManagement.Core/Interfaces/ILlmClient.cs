namespace PatientManagement.Core.Interfaces;

public sealed class LlmGenerationRequest
{
    public string Model { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.2;
    public double TopP { get; set; } = 0.9;
    public int MaxOutputTokens { get; set; } = 1024;
    public bool ExpectJson { get; set; }
    public object? ResponseSchema { get; set; }
}

public sealed class LlmGenerationResponse
{
    public string Provider { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public long LatencyMs { get; set; }
}

/// <summary>
/// Provider-agnostic LLM client abstraction supporting Google Gemini, local Ollama, and future providers.
/// </summary>
public interface ILlmClient
{
    string ProviderName { get; }
    Task<LlmGenerationResponse> GenerateAsync(LlmGenerationRequest request, CancellationToken cancellationToken = default);
}
