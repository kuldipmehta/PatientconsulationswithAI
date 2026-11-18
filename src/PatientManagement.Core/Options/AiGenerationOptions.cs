namespace PatientManagement.Core.Options;

public class AiFeatureGenerationOptions
{
    public double Temperature { get; set; } = 0.2;
    public double TopP { get; set; } = 0.9;
    public int MaxOutputTokens { get; set; } = 1024;
    public string PromptVersion { get; set; } = string.Empty;
}
