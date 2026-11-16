namespace PatientManagement.Core.Interfaces;

public interface ITextEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    Task<List<float[]>> GenerateBatchEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}
