namespace PatientManagement.Core.Interfaces;

public interface IRagContextCompressor
{
    /// <summary>
    /// Compresses a clinical chunk by extracting the most salient sentences aligned with the query,
    /// removing administrative boilerplate (e.g. headers, signatures, room numbers) while preserving clinical values.
    /// </summary>
    /// <param name="query">The user or clinical query.</param>
    /// <param name="content">The raw chunk text.</param>
    /// <param name="maxWords">The target maximum word count for the compressed snippet.</param>
    /// <returns>The compressed high-signal clinical text.</returns>
    string Compress(string query, string content, int maxWords = 80);
}
