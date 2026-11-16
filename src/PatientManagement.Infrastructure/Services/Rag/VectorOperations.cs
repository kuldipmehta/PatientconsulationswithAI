using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PatientManagement.Infrastructure.Services.Rag;

public static class VectorOperations
{
    public static double CosineSimilarity(float[]? a, float[]? b)
    {
        if (a == null || b == null || a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0.0;
        }

        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0.0 || normB <= 0.0)
        {
            return 0.0;
        }

        var similarity = dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Math.Clamp(similarity, -1.0, 1.0);
    }

    public static string SerializeVector(float[] vector)
    {
        return JsonSerializer.Serialize(vector);
    }

    public static float[] DeserializeVector(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<float>();
        }

        try
        {
            return JsonSerializer.Deserialize<float[]>(json) ?? Array.Empty<float>();
        }
        catch
        {
            return Array.Empty<float>();
        }
    }

    /// <summary>
    /// Generates a deterministic unit-normalized pseudo-embedding vector from text.
    /// Used when offline or when no live Gemini API key is configured, ensuring deterministic similarity in test suites.
    /// </summary>
    public static float[] GenerateDeterministicPseudoEmbedding(string text, int dimensions = 768)
    {
        var vector = new float[dimensions];
        if (string.IsNullOrWhiteSpace(text))
        {
            return vector;
        }

        var tokens = text.ToLowerInvariant()
            .Split(new[] { ' ', '\r', '\n', '\t', '.', ',', ';', ':', '(', ')', '[', ']', '-', '/', '\\', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return vector;
        }

        foreach (var token in tokens)
        {
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var primaryBucket = (int)(BitConverter.ToUInt32(hashBytes, 0) % (uint)dimensions);
            var secondaryBucket = (int)(BitConverter.ToUInt32(hashBytes, 4) % (uint)dimensions);
            var sign1 = (hashBytes[8] % 2 == 0) ? 1.0f : -1.0f;
            var sign2 = (hashBytes[9] % 2 == 0) ? 0.5f : -0.5f;

            vector[primaryBucket] += sign1;
            vector[secondaryBucket] += sign2;
        }

        // L2 Normalization
        double sumSquares = 0.0;
        for (int i = 0; i < dimensions; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        if (sumSquares > 0.0)
        {
            var norm = (float)Math.Sqrt(sumSquares);
            for (int i = 0; i < dimensions; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}
